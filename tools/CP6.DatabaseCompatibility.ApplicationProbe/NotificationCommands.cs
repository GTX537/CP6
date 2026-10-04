using CP6.Core.Services.Oa;
using CP6.DatabaseCompatibility.Testing;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static async Task EnqueueAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        await using (var db = Core(owned, options.Tenant))
        {
            Require(await db.Sys_Tenants.AnyAsync(x => x.Id == options.Tenant && x.Enable, ct), "notification-tenant-existing-enabled");
            Require(await db.Sys_Users.AnyAsync(x => x.TenantId == options.Tenant && x.Id == options.User && x.Enable, ct), "notification-user-existing-enabled");
            Require(!await db.Wf_Notifications.AnyAsync(x => x.EventKey == options.EventKey, ct), "notification-event-key-new");
            await new NotificationService(db).CreateOutboxAsync(options.User, WfNotificationType.TodoCreated,
                "WP6 application worker fixture", "Bounded local worker observation", null, null, null,
                options.EventKey!, inAppRequested: true, emailRequested: false);
            // The source service only tracks. Its real transaction is committed here.
            await owned.VerifyAsync(ct);
            await db.SaveChangesAsync(ct);
        }
        var row = await NotificationAsync(owned, options, ct);
        Require(row is not null, "notification-committed-row-visible");
        NotificationReport(row!, options, report);
        report.Assertions.Add(new("production-outbox-service-committed-one-row", true));
    }

    private static async Task NotificationStatusAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.WaitSeconds);
        Wf_Notification? row;
        while (true)
        {
            row = await NotificationAsync(owned, options, ct);
            if (row is not null && (options.ExpectedStatus == "queued" ? row.DispatchStatus == 0 : row.DispatchStatus == 1)) break;
            if (DateTimeOffset.UtcNow >= deadline) break;
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
        Require(row is not null, "notification-one-owned-row");
        report.Assertions.Add(new("notification-one-owned-row", true, ExpectedCount: 1, ActualCount: 1));
        NotificationReport(row!, options, report);
        var wanted = options.ExpectedStatus == "queued" ? 0 : 1;
        var status = row!.DispatchStatus == wanted;
        var attempts = row.DispatchAttempts == options.ExpectedAttempts;
        var delivered = wanted != 1 || (row.DispatchedAtUtc is not null && row.NextAttemptAtUtc is null && row.LastDispatchError is null);
        report.Assertions.Add(new("notification-status-exact", status, ExpectedCount: wanted, ActualCount: row.DispatchStatus));
        report.Assertions.Add(new("notification-attempts-exact", attempts, ExpectedCount: options.ExpectedAttempts, ActualCount: row.DispatchAttempts));
        report.Assertions.Add(new("notification-success-persisted", delivered));
        Require(status && attempts && delivered, "notification-persisted-state-matches");
    }

    private static async Task<Wf_Notification?> NotificationAsync(OwnedTestDatabase owned, ProbeArguments options, CancellationToken ct)
    {
        await using var db = Core(owned, options.Tenant);
        return await db.Wf_Notifications.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == options.Tenant && x.UserId == options.User && x.EventKey == options.EventKey, ct);
    }

    private static void NotificationReport(Wf_Notification row, ProbeArguments options, SafeReport report)
    {
        report.Counts["NotificationRows"] = 1;
        report.Counts["DispatchStatus"] = row.DispatchStatus;
        report.Counts["DispatchAttempts"] = row.DispatchAttempts;
        report.Hashes["EventKeySha256"] = Hash(options.EventKey!);
        report.Hashes["TenantSha256"] = Hash(options.Tenant.ToString("D"));
        report.Hashes["UserSha256"] = Hash(options.User.ToString("D"));
        report.Hashes["NotificationIdSha256"] = Hash(row.Id.ToString("D"));
    }
}
