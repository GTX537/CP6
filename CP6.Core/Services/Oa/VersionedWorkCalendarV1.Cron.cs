using System.Text.Json.Serialization;
using NCrontab;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CronExplanationInputV1([property: JsonRequired] string Cron, [property: JsonRequired] Guid CalendarId,
    [property: JsonRequired] long CalendarRevision, [property: JsonRequired] string TimeZoneId,
    [property: JsonRequired] string TimeZoneRulesVersion, [property: JsonRequired] DateTimeOffset AfterUtc, bool WorkdaysOnly = false);

public sealed record CronExplanationPointV1(DateTimeOffset Utc, DateTime Local, int OffsetMinutes, int Fold);
public sealed record CronExplanationV1(string Mode, bool IsExecutionPermit, Guid CalendarId, long CalendarRevision,
    string TimeZoneId, string TimeZoneRulesVersion, DateTimeOffset AfterUtc, bool WorkdaysOnly, string MissingLocalTimePolicy,
    string RepeatedLocalTimePolicy, string Coverage, CronExplanationPointV1[] Points);

public sealed partial class VersionedWorkCalendarV1
{
    public async Task<CronExplanationV1> ExplainCronAsync(CronExplanationInputV1 input, CancellationToken ct)
    {
        if (input.CalendarId == Guid.Empty || input.CalendarRevision < 0 || input.AfterUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(input.TimeZoneId) || string.IsNullOrWhiteSpace(input.TimeZoneRulesVersion))
            throw Error("TIME_EXPLANATION_PIN_REQUIRED", 422);
        var schedule = input.Cron is { Length: > 0 and <= 128 } ? CrontabSchedule.TryParse(input.Cron) : null;
        if (schedule == null) throw Error("CRON_EXPRESSION_INVALID", 422);

        await using var tx = await BeginReadAsync(ct);
        var calendar = await SnapshotAsync(db, input.CalendarId, input.CalendarRevision, ct);
        var zone = await WorkflowDeadlineZoneV1.ResolveAsync(db, ct);
        if (zone.Rules == null) throw Error(zone.FailureCode ?? "TIME_CONTEXT_UNPROVEN", 503);
        if (zone.Id != input.TimeZoneId || zone.Version != input.TimeZoneRulesVersion)
            throw Error("TIMEZONE_VERSION_CHANGED", 412);

        var (points, complete) = CronPoints(schedule, input.AfterUtc, zone.Rules, calendar, input.WorkdaysOnly, ct);
        await RequireActorAsync("Calendar.View", ct);
        await tx.CommitAsync(ct);
        return new("EXPLANATION_ONLY", false, calendar.CalendarId, calendar.Revision, input.TimeZoneId, input.TimeZoneRulesVersion,
            input.AfterUtc, input.WorkdaysOnly, "SKIP_MISSING_CIVIL_TIME", "RETURN_BOTH_FOLDS", complete ? "FIVE_POINTS" : "SEARCH_LIMIT",
            points);
    }

    private static (CronExplanationPointV1[] Points, bool Complete) CronPoints(CrontabSchedule schedule,
        DateTimeOffset afterUtc, TimeZoneInfo rules, WorkCalendarSnapshotV1 calendar, bool workdaysOnly, CancellationToken ct)
    {
        const int limit = 5;
        const long offsetBound = 14 * TimeSpan.TicksPerHour;
        const long searchWindow = 40 * 366 * TimeSpan.TicksPerDay;
        var first = Math.Max(DateTime.MinValue.Ticks, afterUtc.Ticks - offsetBound);
        var end = new DateTime(Math.Min(DateTime.MaxValue.Ticks, afterUtc.Ticks + searchWindow));
        var cursor = new DateTime(first);
        var exceptions = calendar.Items.ToDictionary(x => x.Date, x => x.IsWorkday);
        var best = new List<CronExplanationPointV1>(limit + 1);
        // 按当地时刻枚举，使用可能的最大偏移证明 UTC 次序，避免在时钟回拨时跳过第二次出现。
        var utcProvenThrough = afterUtc.Ticks;
        for (var scanned = 0; scanned < 10_000 && cursor < end; scanned++)
        {
            ct.ThrowIfCancellationRequested();
            cursor = schedule.GetNextOccurrence(cursor, end);
            utcProvenThrough = Math.Max(utcProvenThrough, cursor.Ticks - offsetBound);
            if (best.Count == limit && best[^1].Utc.Ticks <= utcProvenThrough) return ([.. best], true);
            if (cursor >= end) break;

            var date = DateOnly.FromDateTime(cursor);
            if (workdaysOnly && !exceptions.GetValueOrDefault(date, WorkCalendarSnapshotV1.Weekday(date)) ||
                rules.IsInvalidTime(cursor)) continue;
            var offsets = rules.IsAmbiguousTime(cursor)
                ? rules.GetAmbiguousTimeOffsets(cursor).OrderDescending().ToArray()
                : [rules.GetUtcOffset(cursor)];
            for (var fold = 0; fold < offsets.Length; fold++)
            {
                var ticks = cursor.Ticks - offsets[fold].Ticks;
                if (ticks <= afterUtc.Ticks || ticks > DateTime.MaxValue.Ticks) continue;
                best.Add(new(new DateTimeOffset(ticks, TimeSpan.Zero), cursor, (int)offsets[fold].TotalMinutes, fold));
                best.Sort((a, b) => a.Utc.CompareTo(b.Utc));
                if (best.Count > limit) best.RemoveAt(limit);
            }
        }
        return (best.Where(p => p.Utc.Ticks <= utcProvenThrough).ToArray(), false);
    }
}
