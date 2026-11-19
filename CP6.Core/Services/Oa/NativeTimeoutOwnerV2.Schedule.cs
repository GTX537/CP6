using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class NativeTimeoutOwnerV2
{
    private sealed record ScheduleSettings(NativeTimeoutScheduleDefinitionV2 Definition, string Reason);
    private sealed record CalendarDay(DateOnly Date, bool IsWorkday);
    private sealed record ScheduleRow(Guid Version, Guid? PreviousVersion, DateTimeOffset PublishedAt, string Settings,
        string Calendar, string CalendarVersion, string ZoneId, string ZoneVersion);

    private Task<ScheduleRow?> ScheduleAsync(Guid? version, CancellationToken ct) => db.Database.SqlQuery<ScheduleRow>($"""
        SELECT version AS "Version",previous_version AS "PreviousVersion",published_at AS "PublishedAt",
          settings::text AS "Settings",calendar::text AS "Calendar",calendar_version AS "CalendarVersion",
          zone_id AS "ZoneId",zone_version AS "ZoneVersion"
        FROM cp6_native_timeout_v2.schedules
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND service_id={options.ServiceId}
          AND ({version}::uuid IS NULL OR version={version}) ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);

    // 只接受部署方为具名服务提供的配置命令。原版本、原因、操作键和影响范围随版本保存。
    private async Task<ScheduleRow> EnsureScheduleAsync(DateTimeOffset now, CancellationToken ct)
    {
        var current = await ScheduleAsync(null, ct);
        if (current == null)
        {
            var legacy = new NativeTimeoutScheduleDefinitionV2(true, 0, NativeTimeoutMissedRunV2.ContinueOriginal, 1, false, "UTC");
            current = await InsertScheduleAsync(Guid.Empty, null, new(legacy, "沿用原宿主轮询及原操作"), now, ct);
        }

        var change = options.Schedule;
        if (change == null) return current;
        if (change.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Length > 240
            || change.Definition is not { IntervalSeconds: >= 20 and <= 86400, CatchUpLimit: >= 1 and <= 8 }
            || !Enum.IsDefined(change.Definition.MissedRun)
            || change.Definition.MissedRun != NativeTimeoutMissedRunV2.CatchUpBounded && change.Definition.CatchUpLimit != 1)
            throw Error("TIMEOUT_SCHEDULE_INVALID", 400);

        var settings = new ScheduleSettings(change.Definition, change.Reason);
        var original = await ScheduleAsync(change.OperationId, ct);
        if (original != null)
        {
            if (original.PreviousVersion != change.ExpectedVersion || Parse<ScheduleSettings>(original.Settings) != settings)
                throw Error("TIMEOUT_SCHEDULE_OPERATION_CONFLICT");
            return current;
        }
        if (current.Version != change.ExpectedVersion) throw Error("TIMEOUT_SCHEDULE_VERSION_CHANGED");
        return await InsertScheduleAsync(change.OperationId, change.ExpectedVersion, settings, now, ct);
    }

    private async Task<ScheduleRow> InsertScheduleAsync(Guid version, Guid? previous, ScheduleSettings settings,
        DateTimeOffset now, CancellationToken ct)
    {
        var zone = ScheduleZone(settings.Definition.TimeZoneId);
        var rules = settings.Definition.WorkdaysOnly
            ? await db.Sys_WorkCalendars.AsNoTracking().OrderBy(d => d.Date)
                .Select(d => new CalendarDay(DateOnly.FromDateTime(d.Date), d.IsWorkday)).ToArrayAsync(ct)
            : [];
        var calendar = Json(rules);
        var calendarVersion = Hash("CP6_WORK_CALENDAR_V1\n" + calendar);
        var zoneVersion = Hash("CP6_TIME_ZONE_RULES_V1\n" + zone.ToSerializedString());
        var settingsJson = Json(settings);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_native_timeout_v2.schedules(tenant_id,environment_id,service_id,version,previous_version,published_at,
                settings,calendar,calendar_version,zone_id,zone_version)
            VALUES({db.CurrentTenantId},{fence.EnvironmentId},{options.ServiceId},{version},{previous},{now},
                {settingsJson}::jsonb,{calendar}::jsonb,{calendarVersion},{zone.Id},{zoneVersion})
            """, ct);
        return new(version, previous, now, settingsJson, calendar, calendarVersion, zone.Id, zoneVersion);
    }

    private async Task<(bool Due, DateTimeOffset? Slot)> DueSlotAsync(ScheduleRow schedule, DateTimeOffset now, CancellationToken ct)
    {
        var definition = Parse<ScheduleSettings>(schedule.Settings).Definition;
        if (!definition.Enabled) return (false, null);
        if (definition.IntervalSeconds == 0) return (true, null);

        var zone = ScheduleZone(schedule.ZoneId);
        if (Hash("CP6_TIME_ZONE_RULES_V1\n" + zone.ToSerializedString()) != schedule.ZoneVersion)
            throw Error("TIMEOUT_TIME_ZONE_VERSION_CHANGED", 503);
        if (now < schedule.PublishedAt) return (false, null);
        var period = TimeSpan.FromSeconds(definition.IntervalSeconds);
        var slot = schedule.PublishedAt.AddTicks((now - schedule.PublishedAt).Ticks / period.Ticks * period.Ticks);
        var last = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT max(slot_at) AS "Value" FROM cp6_native_timeout_v2.runs
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
              AND service_id={options.ServiceId} AND schedule_version={schedule.Version}
            """).SingleAsync(ct);
        var calendar = definition.WorkdaysOnly ? Parse<CalendarDay[]>(schedule.Calendar).ToDictionary(d => d.Date, d => d.IsWorkday) : null;
        var limit = definition.MissedRun == NativeTimeoutMissedRunV2.CatchUpBounded ? definition.CatchUpLimit : 1;
        DateTimeOffset? selected = null;
        for (var i = 0; i < limit && slot >= schedule.PublishedAt && (last == null || slot > last); i++, slot = slot.Subtract(period))
        {
            if (calendar != null)
            {
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot, zone).Date);
                if (!calendar.GetValueOrDefault(day, day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))) continue;
            }
            selected = slot;
        }
        return (selected.HasValue, selected);
    }

    private async Task EnsureDispatchEnabledAsync(CancellationToken ct)
    {
        var current = await ScheduleAsync(null, ct);
        if (current != null && !Parse<ScheduleSettings>(current.Settings).Definition.Enabled)
            throw Error("TIMEOUT_SCHEDULE_DISABLED", 403);
    }

    private static TimeZoneInfo ScheduleZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Trim() != id) throw Error("TIMEOUT_TIME_ZONE_INVALID", 400);
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw Error("TIMEOUT_TIME_ZONE_INVALID", 400); }
        catch (InvalidTimeZoneException) { throw Error("TIMEOUT_TIME_ZONE_INVALID", 400); }
    }

    internal static string InputDigest(NativeTimeoutClaimV2 claim) => Hash(string.Create(CultureInfo.InvariantCulture,
        $"CP6_NATIVE_TIMEOUT_OPERATION_V2\n{claim.ExecutionId:D}\n{claim.TaskId:D}\n{claim.Revision}\n{claim.Occurrence}"));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
