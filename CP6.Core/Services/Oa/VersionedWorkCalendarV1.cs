using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record WorkCalendarExceptionV1(DateOnly Date, bool IsWorkday, string? Note);

internal sealed record WorkCalendarSnapshotV1(Guid CalendarId, long Revision, DateTimeOffset? RecordedAt, string Origin,
    WorkCalendarExceptionV1[] Items)
{
    internal static bool Weekday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}

public sealed record HolidayPackageV1(string Version, string Sha256, int[] Years);
public sealed record WorkCalendarYearV1(Guid CalendarId, long Revision, DateTimeOffset? RecordedAt, string Origin, int Year,
    string WeekRule, WorkCalendarExceptionV1[] Items, string? TimeZoneId, string? TimeZoneRulesVersion, string TimeZoneSource,
    string? TimeZoneFailure, HolidayPackageV1 JapaneseHolidays);
public sealed record WorkCalendarCalculationV1(Guid CalendarId, long Revision, string? TimeZoneId, string? TimeZoneRulesVersion,
    WorkflowDeadlineInstantV1 Deadline);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkCalendarChangeV1(Guid CommandId, Guid CalendarId, long? ExpectedRevision, DateOnly Date, bool? IsWorkday, string? Note);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkCalendarImportV1(Guid CommandId, Guid CalendarId, long? ExpectedRevision, int Year);
public sealed record WorkCalendarChangeReceiptV1(Guid CommandId, Guid CalendarId, long Revision, DateOnly Date, bool IsWorkday, string Source);
public sealed record WorkCalendarImportReceiptV1(Guid CommandId, Guid CalendarId, long Revision, int Year, HolidayPackageV1 Package,
    DateOnly[] Inserted, DateOnly[] Skipped);

public sealed partial class VersionedWorkCalendarV1(CP6Context db, ScopeAuthorizationFenceV1 fence,
    IHttpContextAccessor http, IWorkCalendarService legacy, INotificationService notifications)
{
    private const string Menu = "oa-work-calendar";
    private const string Owner = "OA_WORK_CALENDAR";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly WorkCalendarExceptionV1[] Holidays = JapaneseHolidayData.Items
        .Select(x => new WorkCalendarExceptionV1(new(x.Y, x.M, x.D), false, x.Note)).OrderBy(x => x.Date).ToArray();
    private static readonly HolidayPackageV1 Package = new("JP-STATIC-2026-2027/1",
        MainOwnerOperationJournalV1.Digest(JsonSerializer.SerializeToUtf8Bytes(Holidays, Json)),
        Holidays.Select(x => x.Date.Year).Distinct().ToArray());

    private sealed record SnapshotRow(long Revision, DateTimeOffset RecordedAt, string Origin, string Days);
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 422) => new(code, status);

    internal static async Task RequireCoverageAsync(CP6Context db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || !await db.Database.SqlQueryRaw<bool>("SELECT cp6_work_calendar_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("CALENDAR_WRITER_COVERAGE_UNAVAILABLE", 503);
    }

    internal static async Task<WorkCalendarSnapshotV1> SnapshotAsync(CP6Context db, Guid? calendarId, long? revision,
        CancellationToken ct)
    {
        // 现有原生模型每租户只有一本工作日历。其租户 UUID 是稳定的日历身份。
        var id = db.CurrentTenantId;
        if (id == Guid.Empty || calendarId != null && calendarId != id) throw Error("CALENDAR_NOT_FOUND", 404);
        if (revision < 0) throw Error("CALENDAR_REVISION_INVALID");
        if (revision == 0) return new(id, 0, null, "DEFAULT", []);
        var row = await db.Database.SqlQuery<SnapshotRow>($"""
            SELECT revision AS "Revision",recorded_at AS "RecordedAt",origin AS "Origin",days::text AS "Days"
            FROM cp6_work_calendar_v1.revisions WHERE tenant_id={id} AND ({revision}::bigint IS NULL OR revision={revision})
            ORDER BY revision DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (row == null)
            return revision != null ? throw Error("CALENDAR_REVISION_NOT_FOUND", 404) : new(id, 0, null, "DEFAULT", []);
        return new(id, row.Revision, row.RecordedAt, row.Origin, JsonSerializer.Deserialize<WorkCalendarExceptionV1[]>(row.Days, Json)!);
    }

    public async Task<WorkCalendarYearV1> YearAsync(int year, Guid? calendarId, long? revision, CancellationToken ct)
    {
        if (year is < 1 or > 9999) throw Error("CALENDAR_YEAR_INVALID");
        await using var tx = await BeginReadAsync(ct);
        var calendar = await SnapshotAsync(db, calendarId, revision, ct);
        var zone = await WorkflowDeadlineZoneV1.ResolveAsync(db, ct);
        await tx.CommitAsync(ct);
        return new(calendar.CalendarId, calendar.Revision, calendar.RecordedAt, calendar.Origin, year, "MON_FRI",
            calendar.Items.Where(x => x.Date.Year == year).ToArray(), zone.Id, zone.Version,
            zone.Rules == null ? "UNPROVEN" : "TENANT_SYSTEM_RULES", zone.FailureCode, Package);
    }

    public async Task<WorkCalendarCalculationV1> CalculateAsync(Guid calendarId, long revision, DateOnly from,
        int workdays, TimeOnly localTime, int? ambiguousOffsetMinutes, string? expectedTimeZoneRulesVersion, CancellationToken ct)
    {
        await using var tx = await BeginReadAsync(ct);
        var calendar = await SnapshotAsync(db, calendarId, revision, ct);
        var zone = await WorkflowDeadlineZoneV1.ResolveAsync(db, ct);
        if (expectedTimeZoneRulesVersion != null && zone.Version != expectedTimeZoneRulesVersion)
            throw Error("TIMEZONE_VERSION_CHANGED", 412);
        var result = zone.CalculateWorkdays(from, new(workdays, localTime, ambiguousOffsetMinutes), calendar);
        await tx.CommitAsync(ct);
        return new(calendar.CalendarId, calendar.Revision, zone.Id, zone.Version, result);
    }

    public async Task<WorkCalendarChangeReceiptV1> ChangeAsync(WorkCalendarChangeV1 command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId, command.CalendarId, command.ExpectedRevision);
        if (command.Note?.Length > 100 || command.IsWorkday == null && command.Note != null)
            throw Error("CALENDAR_CHANGE_INVALID");
        var (authority, actor) = await WriteAuthorityAsync("Calendar.Edit", ct);
        var input = Input(command);
        const string contract = "OA_WORK_CALENDAR_CHANGE/1";
        if (await Original<WorkCalendarChangeReceiptV1>(command.CommandId, contract, actor, input, ct) is { } original) return original;
        await CurrentAsync(command.CalendarId, command.ExpectedRevision!.Value, ct);
        var date = command.Date.ToDateTime(TimeOnly.MinValue);
        if (command.IsWorkday is bool workday) await legacy.SetDayAsync(date, workday, command.Note, ct);
        else await legacy.ClearDayAsync(date, ct);
        var current = await SnapshotAsync(db, command.CalendarId, null, ct);
        var result = new WorkCalendarChangeReceiptV1(command.CommandId, command.CalendarId, current.Revision, command.Date,
            command.IsWorkday ?? WorkCalendarSnapshotV1.Weekday(command.Date), command.IsWorkday == null ? "DEFAULT_WEEK" : "EXCEPTION");
        await Journal(command.CommandId, contract, actor, input, result, [command.CalendarId],
            new { previousRevision = command.ExpectedRevision, result.Revision, result.Date }, authority, ct);
        return result;
    }

    public async Task<WorkCalendarImportReceiptV1> ImportAsync(WorkCalendarImportV1 command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId, command.CalendarId, command.ExpectedRevision);
        var (authority, actor) = await WriteAuthorityAsync("Calendar.Edit", ct);
        var input = Input(command);
        const string contract = "OA_WORK_CALENDAR_IMPORT_JP/1";
        if (await Original<WorkCalendarImportReceiptV1>(command.CommandId, contract, actor, input, ct) is { } original) return original;
        if (!Package.Years.Contains(command.Year)) throw Error("CALENDAR_YEAR_UNSUPPORTED");
        var prior = await CurrentAsync(command.CalendarId, command.ExpectedRevision!.Value, ct);
        var existing = prior.Items.Select(x => x.Date).ToHashSet();
        var package = Holidays.Where(x => x.Date.Year == command.Year).ToArray();
        var additions = package.Where(x => !existing.Contains(x.Date)).ToArray();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_WorkCalendar"("Id","TenantId","Date","IsWorkday","Note","CreateDate")
            SELECT gen_random_uuid(),{db.CurrentTenantId},x.date,false,x.note,clock_timestamp() AT TIME ZONE 'UTC'
            FROM jsonb_to_recordset(CAST({JsonSerializer.Serialize(additions, Json)} AS jsonb)) x(date date,note text)
            ON CONFLICT ("TenantId","Date") DO NOTHING
            """, ct);
        if (inserted != additions.Length) throw Error("CALENDAR_REVISION_CHANGED", 412);
        var current = await SnapshotAsync(db, command.CalendarId, null, ct);
        var result = new WorkCalendarImportReceiptV1(command.CommandId, command.CalendarId, current.Revision, command.Year, Package,
            additions.Select(x => x.Date).ToArray(), package.Where(x => existing.Contains(x.Date)).Select(x => x.Date).ToArray());
        await Journal(command.CommandId, contract, actor, input, result, [command.CalendarId],
            new { previousRevision = prior.Revision, result.Revision, result.Year, result.Package }, authority, ct);
        return result;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginReadAsync(CancellationToken ct, bool forecast = false)
    {
        if (!db.Database.IsNpgsql()) throw Error("CALENDAR_WRITER_COVERAGE_UNAVAILABLE", 503);
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await fence.Root(ct);
            await RequireActorAsync("Calendar.View", ct, forecast);
            await RequireCoverageAsync(db, ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    private async Task<Guid> RequireActorAsync(string action, CancellationToken ct, bool forecast = false)
    {
        var actor = CurrentPermissionContext.RequireCurrentUser(db, http);
        var permission = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
        if (!permission.MenuKeys.Contains(Menu) || !permission.ActionKeys.Contains(Menu + ":" + action))
            throw Error("ACTION_FORBIDDEN", 403);
        if (forecast)
        {
            var principal = http.HttpContext!.User;
            var expires = principal.FindFirst("exp")?.Value;
            if (principal.HasClaim(c => c.Type == "impersonator_id") ||
                expires != null && (!long.TryParse(expires, out var end) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= end) ||
                !await db.Sys_Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.Enable && (u.LockedUntil == null || u.LockedUntil <= DateTime.Now), ct) ||
                !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == db.CurrentTenantId && t.Enable &&
                    (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct))
                throw Error("REAUTH_REQUIRED", 401);
            await WorkflowForecastAuthorityV1.RequireAsync(db, permission, ct);
        }
        return actor;
    }

    private async Task<(ScopeAuthorizationLeaseV1 Lease, Guid Actor)> WriteAuthorityAsync(string action, CancellationToken ct)
    {
        // HTTP 写入口持有原 W531 事务，并在提交前重新验证实际身份和操作授权。
        var authority = await fence.AcquireAsync(ct);
        await RequireCoverageAsync(db, ct);
        if (action == "Calendar.Recalculate" && !await db.Database.SqlQueryRaw<bool>("SELECT cp6_work_calendar_v1.recalculation_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("DEADLINE_RECALCULATION_WRITER_UNAVAILABLE", 503);
        return (authority, await RequireActorAsync(action, ct));
    }

    private async Task<WorkCalendarSnapshotV1> CurrentAsync(Guid calendarId, long expected, CancellationToken ct)
    {
        var current = await SnapshotAsync(db, calendarId, null, ct);
        if (current.Revision != expected) throw Error("CALENDAR_REVISION_CHANGED", 412);
        return current;
    }

    private void ValidateCommand(Guid id, Guid calendarId, long? expected)
    {
        if (id == Guid.Empty || expected is not >= 0) throw Error("CALENDAR_COMMAND_INVALID");
        if (calendarId == Guid.Empty || calendarId != db.CurrentTenantId) throw Error("CALENDAR_NOT_FOUND", 404);
    }

    private byte[] Input<T>(T command) => MainOwnerOperationJournalV1.Encode(new { EnvironmentId = fence.EnvironmentId, Command = command });

    private async Task<T?> Original<T>(Guid id, string contract, Guid actor, byte[] input, CancellationToken ct) where T : class
    {
        var original = await new MainOwnerOperationJournalV1(db).Read(Owner, id, ct);
        if (original == null) return null;
        if (original.Actor != actor) throw Error("CALENDAR_COMMAND_NOT_FOUND", 404);
        if (original.Contract != contract || !MainOwnerOperationJournalV1.Matches(original, input))
            throw Error("CALENDAR_COMMAND_CONFLICT", 409);
        return JsonSerializer.Deserialize<T>(original.Result)!;
    }

    private async Task Journal(Guid id, string contract, Guid actor, byte[] input, object result, Guid[] objects, object audit,
        ScopeAuthorizationLeaseV1 authority, CancellationToken ct)
    {
        await new MainOwnerOperationJournalV1(db).Store(Owner, contract, id, actor, input, result, objects,
            new { authority.Snapshot, Detail = audit }, ct);
        await fence.ValidateAsync(authority, ct);
    }
}
