using CP6.Core.Services.Oa;
using CP6.DatabaseCompatibility.Testing;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static bool Dispatched(Wf_Notification row) => row.DispatchStatus == 1 && row.DispatchAttempts == 1
        && row.DispatchedAtUtc is not null && row.NextAttemptAtUtc is null && row.LastDispatchError is null;

    private static async Task<(Wf_Notification Before, Wf_Notification After, int Written)> ReplayAsync(
        OwnedTestDatabase owned, ProbeArguments options, CancellationToken ct)
    {
        var before = await NotificationAsync(owned, options, ct);
        Require(before is not null && Dispatched(before), "replay-existing-dispatched-row");
        await RequireOneEventRowAsync(owned, options, ct);
        int written;
        await using (var db = Core(owned, options.Tenant))
        {
            // Different input deliberately verifies that the production event-key replay does not overwrite.
            await new NotificationService(db).CreateOutboxAsync(options.User, WfNotificationType.TodoCreated,
                "WP6 replay must not replace original", "No duplicate write or dispatch reset", null, null, null,
                options.EventKey!, inAppRequested: true, emailRequested: false);
            await owned.VerifyAsync(ct);
            written = await db.SaveChangesAsync(ct);
        }
        var after = await NotificationAsync(owned, options, ct);
        Require(after is not null, "replay-one-owned-row");
        await RequireOneEventRowAsync(owned, options, ct);
        return (before, after, written);
    }

    private static async Task RequireOneEventRowAsync(OwnedTestDatabase owned, ProbeArguments options, CancellationToken ct)
    {
        await using var db = Core(owned, options.Tenant);
        Require(await db.Wf_Notifications.CountAsync(x => x.TenantId == options.Tenant && x.EventKey == options.EventKey, ct) == 1,
            "notification-event-key-has-exact-one-row");
    }

    private static async Task ReplayNotificationAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        var replay = await ReplayAsync(owned, options, ct);
        var originalHash = ObjectHash(replay.Before);
        var currentHash = ObjectHash(replay.After);
        NotificationReport(replay.After, options, report);
        report.Counts["ReplaySaveChanges"] = replay.Written;
        report.Hashes["OriginalNotificationStateSha256"] = originalHash;
        report.Hashes["ReplayedNotificationStateSha256"] = currentHash;
        report.Assertions.Add(new("replay-existing-dispatched-row", true));
        report.Assertions.Add(new("replay-production-service-no-duplicate", replay.Written == 0,
            ExpectedCount: 0, ActualCount: replay.Written));
        report.Assertions.Add(new("replay-kept-original-id-and-worker-state", originalHash == currentHash && Dispatched(replay.After),
            originalHash, currentHash));
        Require(report.Assertions.All(x => x.Passed), "replay-persisted-state-exact");
    }
}
