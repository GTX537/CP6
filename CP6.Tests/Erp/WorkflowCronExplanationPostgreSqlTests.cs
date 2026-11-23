using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Cron_actual_Main_explanation_binds_calendar_and_time_rules_and_keeps_next_due()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await CalendarPermissions(h, edit: true);
        await TimeoutZone(h);
        var due = new DateTime(2028, 9, 6, 11, 30, 0, DateTimeKind.Utc);
        var trigger = new Wf_FlowTrigger { FlowKey = "forecast-dormant-timer", StarterUserId = h.User, Enabled = false,
            NextDueUtc = due, ConfigJson = """{"cron":"30 1 * * *"}""" };
        h.Db.Wf_FlowTriggers.Add(trigger);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var year = await api.Data(VersionCalendarApi + "?year=2027");
        var revision = year.GetProperty("revision").GetInt64();
        var zone = year.GetProperty("timeZoneId").GetString()!;
        var version = year.GetProperty("timeZoneRulesVersion").GetString()!;
        Assert.Equal("America/New_York", zone);
        Assert.Matches("^[0-9a-f]{64}$", version);
        const string path = VersionCalendarApi + "/cron-explanations";
        CronExplanationInputV1 Explain(string cron, string afterUtc, bool workdaysOnly = false) =>
            new(cron, h.Tenant, revision, zone, version, DateTimeOffset.Parse(afterUtc), workdaysOnly);

        var before = await ForecastReadState(h);
        var fallback = Explain("30 1 * * *", "2027-11-07T04:00:00Z");
        var repeated = await api.Data(path, fallback);
        Assert.Equal("EXPLANATION_ONLY", repeated.GetProperty("mode").GetString());
        Assert.False(repeated.GetProperty("isExecutionPermit").GetBoolean());
        Assert.Equal("RETURN_BOTH_FOLDS", repeated.GetProperty("repeatedLocalTimePolicy").GetString());
        Assert.Equal("SKIP_MISSING_CIVIL_TIME", repeated.GetProperty("missingLocalTimePolicy").GetString());
        Assert.Equal(version, repeated.GetProperty("timeZoneRulesVersion").GetString());
        Assert.Equal(revision, repeated.GetProperty("calendarRevision").GetInt64());
        var times = repeated.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(5, times.Length);
        void Point(JsonElement point, string utc, string local, int offset, int fold)
        {
            Assert.Equal(DateTimeOffset.Parse(utc), point.GetProperty("utc").GetDateTimeOffset());
            Assert.Equal(local, point.GetProperty("local").GetString());
            Assert.Equal(offset, point.GetProperty("offsetMinutes").GetInt32());
            Assert.Equal(fold, point.GetProperty("fold").GetInt32());
        }
        Point(times[0], "2027-11-07T05:30:00Z", "2027-11-07T01:30:00", -240, 0);
        Point(times[1], "2027-11-07T06:30:00Z", "2027-11-07T01:30:00", -300, 1);
        var secondOnly = (await api.Data(path, fallback with { AfterUtc = times[0].GetProperty("utc").GetDateTimeOffset() }))
            .GetProperty("points")[0];
        Point(secondOnly, "2027-11-07T06:30:00Z", "2027-11-07T01:30:00", -300, 1);
        var missing = await api.Data(path, Explain("30 2 * * *", "2027-03-14T05:00:00Z"));
        Point(missing.GetProperty("points")[0], "2027-03-15T06:30:00Z", "2027-03-15T02:30:00", -240, 0);
        var impossible = await api.Data(path, Explain("0 0 30 2 *", "2027-01-01T00:00:00Z"));
        Assert.Equal("SEARCH_LIMIT", impossible.GetProperty("coverage").GetString());
        Assert.Empty(impossible.GetProperty("points").EnumerateArray());

        var work = Explain("0 9 * * *", "2027-11-06T00:00:00Z", workdaysOnly: true);
        var old = (await api.Data(path, work)).GetProperty("points")[0];
        Point(old, "2027-11-08T14:00:00Z", "2027-11-08T09:00:00", -300, 0);
        var changed = await api.Data(VersionCalendarApi + "/changes", new { commandId = Guid.NewGuid(), calendarId = h.Tenant,
            expectedRevision = revision, date = "2027-11-06", isWorkday = true, note = "isolated Saturday" });
        var newRevision = changed.GetProperty("revision").GetInt64();
        Assert.True(newRevision > revision);
        var replay = (await api.Data(path, work)).GetProperty("points")[0];
        Assert.Equal(old.GetRawText(), replay.GetRawText());
        var current = (await api.Data(path, work with { CalendarRevision = newRevision })).GetProperty("points")[0];
        Point(current, "2027-11-06T13:00:00Z", "2027-11-06T09:00:00", -240, 0);
        await api.Reject(path, work with { CalendarRevision = newRevision + 1 }, HttpStatusCode.NotFound, "CALENDAR_REVISION_NOT_FOUND");
        await api.Reject(path, work with { CalendarId = Guid.NewGuid() }, HttpStatusCode.NotFound, "CALENDAR_NOT_FOUND");
        await api.Reject(path, work with { Cron = "not-a-cron" }, HttpStatusCode.UnprocessableEntity, "CRON_EXPRESSION_INVALID");

        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(q => q.SetProperty(t => t.TimeZoneId, "UTC"));
        await api.Reject(path, fallback, HttpStatusCode.PreconditionFailed, "TIMEZONE_VERSION_CHANGED");
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(q => q.SetProperty(t => t.TimeZoneId, (string?)null));
        await api.Reject(path, fallback, HttpStatusCode.ServiceUnavailable, "TENANT_TIMEZONE_REQUIRED");
        var unchanged = await h.Db.Wf_FlowTriggers.AsNoTracking().SingleAsync(x => x.Id == trigger.Id);
        Assert.Equal(due, unchanged.NextDueUtc);
        Assert.Equal(trigger.ConfigJson, unchanged.ConfigJson);
        Assert.False(unchanged.Enabled);
        Assert.Null(unchanged.LastFiredUtc);
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("Cron actual Main HTTP: five points, DST fold -240/-300, gap skipped, old and current Saturday revisions, 412/503, disabled timer NextDue byte-for-byte unchanged.");
    }
}
