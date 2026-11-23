using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CP6.Core.Services.Oa;

public sealed record WorkflowWorkdayDeadlineV1(int Workdays, TimeOnly LocalTime, int? AmbiguousOffsetMinutes = null);

public sealed record WorkflowDeadlineInstantV1(DateTimeOffset? DueAt, DateTime? LocalDueAt, int? OffsetMinutes,
    string? FailureCode, int[]? AvailableOffsets = null);

internal sealed record WorkflowDeadlineZoneV1(string? Id, TimeZoneInfo? Rules, string? Serialized, string? Version, string? FailureCode)
{
    internal static async Task<WorkflowDeadlineZoneV1> ResolveAsync(CP6Context db, CancellationToken ct)
    {
        var id = await db.Sys_Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => t.TimeZoneId).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(id)) return new(null, null, null, null, "TENANT_TIMEZONE_REQUIRED");
        try
        {
            var rules = TimeZoneInfo.FindSystemTimeZoneById(id);
            var serialized = rules.ToSerializedString();
            return new(id, rules, serialized, MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(serialized)), null);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return new(id, null, null, null, "TENANT_TIMEZONE_INVALID");
        }
    }

    internal Task RetainAsync(CP6Context db, CancellationToken ct) => Rules == null ? Task.CompletedTask :
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_work_calendar_v1.time_zone_rules(zone_id,version,rules) VALUES({Id},{Version},{Serialized})
            ON CONFLICT (zone_id,version) DO NOTHING
            """, ct);

    internal WorkflowDeadlineInstantV1 Calculate(DateTimeOffset start, int? hours,
        WorkflowWorkdayDeadlineV1? timing, WorkCalendarSnapshotV1? calendar)
    {
        if (Rules == null) return new(null, null, null, FailureCode);
        if (timing != null)
            return CalculateWorkdays(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, Rules).DateTime), timing, calendar!);
        if (hours is not > 0) return new(null, null, null, "DEADLINE_POLICY_INVALID");
        try
        {
            var utc = start.ToUniversalTime().AddHours(hours.Value);
            var at = TimeZoneInfo.ConvertTime(utc, Rules);
            return new(utc, at.DateTime, (int)at.Offset.TotalMinutes, null);
        }
        catch (ArgumentOutOfRangeException) { return new(null, null, null, "DEADLINE_POLICY_INVALID"); }
    }

    internal WorkflowDeadlineInstantV1 CalculateWorkdays(DateOnly from, WorkflowWorkdayDeadlineV1 timing, WorkCalendarSnapshotV1 calendar)
    {
        if (Rules == null) return new(null, null, null, FailureCode);
        if (timing.Workdays < 1) return new(null, null, null, "DEADLINE_POLICY_INVALID");
        var day = from;
        var remaining = timing.Workdays;
        var exceptions = calendar.Items.ToDictionary(x => x.Date, x => x.IsWorkday);
        for (var scanned = 0; scanned < 366 && day < DateOnly.MaxValue && remaining > 0; scanned++)
        {
            day = day.AddDays(1);
            if (exceptions.GetValueOrDefault(day, WorkCalendarSnapshotV1.Weekday(day))) remaining--;
        }
        if (remaining > 0) return new(null, null, null, "CALENDAR_RANGE_EXCEEDED");
        var local = day.ToDateTime(timing.LocalTime, DateTimeKind.Unspecified);
        if (Rules.IsInvalidTime(local)) return new(null, local, null, "AMBIGUOUS_OR_INVALID_LOCAL_TIME");
        var offset = Rules.GetUtcOffset(local);
        if (Rules.IsAmbiguousTime(local))
        {
            var choices = Rules.GetAmbiguousTimeOffsets(local).Select(x => (int)x.TotalMinutes).OrderDescending().ToArray();
            if (timing.AmbiguousOffsetMinutes is not int selected || !choices.Contains(selected))
                return new(null, local, null, "AMBIGUOUS_OR_INVALID_LOCAL_TIME", choices);
            offset = TimeSpan.FromMinutes(selected);
        }
        try { return new(new DateTimeOffset(local, offset).ToUniversalTime(), local, (int)offset.TotalMinutes, null); }
        catch (ArgumentOutOfRangeException) { return new(null, local, null, "CALENDAR_RANGE_EXCEEDED"); }
    }
}
