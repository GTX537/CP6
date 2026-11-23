using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public enum WorkflowNotificationEmailOutcomeV1 { Accepted, RetryableFailure, Unknown }

public sealed record WorkflowNotificationEmailV1(string DeliveryKey, string Address, string Summary);

/// <summary>仅用于已采用的提供方。接受结果表示传输提供方确认接收。</summary>
public interface IWorkflowNotificationEmailV1
{
    bool SupportsOriginalKeyAndLookup { get; }
    Task<WorkflowNotificationEmailOutcomeV1> SendAsync(WorkflowNotificationEmailV1 message, CancellationToken ct);
    Task<WorkflowNotificationEmailOutcomeV1> OriginalAsync(string deliveryKey, CancellationToken ct);
}

public sealed class WorkflowNotificationDeliveryService(CP6Context db, ScopeAuthorizationFenceV1 scope,
    IEnumerable<IWorkflowNotificationEmailV1> transports)
{
    private const string InApp = "IN_APP", Email = "EMAIL";
    private const string Accepted = "ACCEPTED_BY_PROVIDER", Retryable = "RETRYABLE_FAILED", Unknown = "UNKNOWN",
        Suppressed = "SUPPRESSED", BlockedAddress = "BLOCKED_ADDRESS", InFlight = "IN_FLIGHT";

    private sealed record Source(Guid Id, Guid Recipient, Guid OriginalActor, Guid? InstanceId, Guid? TaskId, int Type,
        string Kind, bool InAppRequested, bool EmailRequested, bool HasPreferenceVersion);
    private sealed record LegacyDelivery(int Status, int Attempts, bool HasDispatchedAt, bool HasError, bool Deferred);
    private sealed record Delivery(string RecipientVersion, string State, DateTimeOffset? NextAttempt,
        Guid? AttemptId, int Attempts);
    private sealed record Current(string Version, string? Address, string? HeldReason);
    private sealed record Prepared(Source Notification, Delivery Delivery, bool Lookup);
    private sealed record Preparation(bool Refresh = false, Prepared? EmailWork = null);

    public bool IsAvailable => db.Database.IsNpgsql() && scope.HasDeploymentEnvironment;

    public async Task ScanAsync(Func<Guid, Guid, Guid, CancellationToken, Task> invalidateInbox, CancellationToken ct)
    {
        var adapters = transports.Take(2).ToArray();
        var transport = adapters.Length == 1 ? adapters[0] : null;
        foreach (var source in await DueAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (source.InAppRequested && (await PrepareAsync(source, InApp, transport, ct)).Refresh)
                await invalidateInbox(db.CurrentTenantId, scope.EnvironmentId, source.Recipient, ct);
            if (source.EmailRequested)
            {
                var prepared = await PrepareAsync(source, Email, transport, ct);
                if (prepared.EmailWork is not null && transport is not null)
                    await FinishEmailAsync(prepared.EmailWork, transport, ct);
            }
        }
    }

    private async Task<List<Source>> DueAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var rows = await db.Database.SqlQuery<Source>($"""
            SELECT n."Id",n."UserId" AS "Recipient",s.actor_id AS "OriginalActor",n."InstanceId",n."TaskId",n."Type",s.kind AS "Kind",
                   n."InAppRequested",n."EmailRequested",s.preference_version IS NOT NULL AS "HasPreferenceVersion"
            FROM public."Wf_Notification" n JOIN cp6_oa_notification_v1.owner_sources s ON s.notification_id=n."Id"
            WHERE n."TenantId"={db.CurrentTenantId} AND s.tenant_id=n."TenantId" AND s.environment_id={scope.EnvironmentId}
              AND s.recipient_id=n."UserId" AND s.task_id IS NOT DISTINCT FROM n."TaskId"
              AND NOT (s.preference_version IS NULL AND n."DispatchStatus"=0 AND n."DispatchAttempts"=0
                  AND n."NextAttemptAtUtc" IS NOT NULL AND n."NextAttemptAtUtc">clock_timestamp())
              AND EXISTS (SELECT 1 FROM (VALUES ('IN_APP',n."InAppRequested"),('EMAIL',n."EmailRequested")) lane(channel,requested)
                  WHERE lane.requested AND NOT EXISTS (SELECT 1 FROM cp6_oa_notification_v1.deliveries d
                    WHERE d.tenant_id=n."TenantId" AND d.environment_id=s.environment_id AND d.notification_id=n."Id"
                      AND d.recipient_id=n."UserId" AND d.channel=lane.channel
                      AND (d.next_attempt_at IS NULL OR d.next_attempt_at>clock_timestamp())))
            ORDER BY n."CreateDate",n."Id" LIMIT 50
            """).ToListAsync(ct);
        await scope.ValidateAsync(lease, ct);
        return rows;
    }

    private async Task<Preparation> PrepareAsync(Source source, string channel, IWorkflowNotificationEmailV1? transport,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scopeLease = await scope.AcquireAsync(ct);
        var prior = await ReadDeliveryAsync(source, channel, ct);
        var now = DateTimeOffset.UtcNow;
        if (prior is not null && (prior.NextAttempt is null || prior.NextAttempt > now)) return new();

        if (!source.HasPreferenceVersion && await RetainLegacyResultAsync(source, channel, scopeLease, ct))
        {
            await transaction.CommitAsync(ct);
            return new();
        }

        if (prior is { State: InFlight or Unknown })
        {
            var lookup = transport?.SupportsOriginalKeyAndLookup == true;
            var next = lookup ? now.AddSeconds(30) : (DateTimeOffset?)null;
            await WriteAsync(source, channel, prior.RecipientVersion, Unknown, "PROVIDER_OUTCOME_UNKNOWN",
                prior.AttemptId, prior.Attempts, next, ct);
            await scope.ValidateAsync(scopeLease, ct);
            await transaction.CommitAsync(ct);
            return new(EmailWork: lookup ? new(source, prior, true) : null);
        }

        var current = await EligibilityAsync(source, channel, ct);
        var version = prior?.RecipientVersion ?? current.Version;
        var reason = current.HeldReason;
        if (reason is null && prior is not null && prior.RecipientVersion != current.Version)
            reason = "CURRENT_RECIPIENT_CHANGED";

        if (reason is not null)
        {
            await WriteAsync(source, channel, version, reason == "ADDRESS_MISSING" ? BlockedAddress : Suppressed,
                reason, prior?.AttemptId, prior?.Attempts ?? 0, null, ct);
            await scope.ValidateAsync(scopeLease, ct);
            await transaction.CommitAsync(ct);
            return new();
        }

        if (channel == InApp)
        {
            // 站内接受依据是持久通知已提交。在线提示仅用于唤起列表刷新。
            await WriteAsync(source, channel, version, Accepted, null, null, 1, null, ct);
            await scope.ValidateAsync(scopeLease, ct);
            await transaction.CommitAsync(ct);
            return new(Refresh: true);
        }

        if (transport is null)
        {
            await WriteAsync(source, channel, version, Unknown, "PROVIDER_UNPROVEN", null, 0, null, ct);
            await scope.ValidateAsync(scopeLease, ct);
            await transaction.CommitAsync(ct);
            return new();
        }

        var claim = new Delivery(version, InFlight, now.AddMinutes(1), Guid.NewGuid(), (prior?.Attempts ?? 0) + 1);
        await WriteAsync(source, channel, version, claim.State, null, claim.AttemptId, claim.Attempts, claim.NextAttempt, ct);
        await scope.ValidateAsync(scopeLease, ct);
        await transaction.CommitAsync(ct);
        return new(EmailWork: new(source, claim, false));
    }

    private async Task<bool> RetainLegacyResultAsync(Source source, string channel, ScopeAuthorizationLeaseV1 lease,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<LegacyDelivery>($"""
            SELECT "DispatchStatus" AS "Status","DispatchAttempts" AS "Attempts",
                "DispatchedAtUtc" IS NOT NULL AS "HasDispatchedAt","LastDispatchError" IS NOT NULL AS "HasError",
                coalesce("NextAttemptAtUtc">clock_timestamp(),false) AS "Deferred"
            FROM public."Wf_Notification"
            WHERE "TenantId"={db.CurrentTenantId} AND "Id"={source.Id} AND "UserId"={source.Recipient}
            FOR UPDATE
            """).ToListAsync(ct);
        var history = rows.Single();
        var neverAttempted = history is { Status: 0, Attempts: 0, HasDispatchedAt: false, HasError: false };
        if (neverAttempted && history.Deferred) return true;
        if (channel == InApp && neverAttempted) return false;

        // 旧状态是整条通知的结果，只有已完成的站内记录足以确认原刷新处理完成。
        // 无通道收据的旧邮件或半程记录一律保留原证据，不生成第二次派送。
        var accepted = channel == InApp && history is { Status: 1, Attempts: > 0, HasDispatchedAt: true, HasError: false };
        var reason = channel == Email ? "LEGACY_EMAIL_PROVIDER_UNPROVEN" :
            accepted ? "LEGACY_IN_APP_COMPLETED" : "LEGACY_IN_APP_OUTCOME_UNKNOWN";
        await WriteAsync(source, channel, RecipientVersion(source.Id, source.Recipient, null), accepted ? Accepted : Unknown,
            reason, null, history.Attempts, null, ct);
        await scope.ValidateAsync(lease, ct);
        return true;
    }

    private async Task FinishEmailAsync(Prepared work, IWorkflowNotificationEmailV1 transport, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scopeLease = await scope.AcquireAsync(ct);
        var source = work.Notification;
        var actual = await ReadDeliveryAsync(source, Email, ct);
        var expected = work.Lookup ? Unknown : InFlight;
        if (actual is null || actual.State != expected || actual.AttemptId != work.Delivery.AttemptId ||
            actual.RecipientVersion != work.Delivery.RecipientVersion)
            return;

        string? address = null;
        if (!work.Lookup)
        {
            var current = await EligibilityAsync(source, Email, ct);
            if (current.HeldReason is not null || current.Version != actual.RecipientVersion)
            {
                var held = current.HeldReason ?? "CURRENT_RECIPIENT_CHANGED";
                await WriteAsync(source, Email, actual.RecipientVersion, held == "ADDRESS_MISSING" ? BlockedAddress : Suppressed,
                    held, actual.AttemptId, actual.Attempts, null, ct);
                await scope.ValidateAsync(scopeLease, ct);
                await transaction.CommitAsync(ct);
                return;
            }
            address = current.Address;
        }

        // 在同一已验证作用域内做最后确认。超时或结果不明留原键供查，不能当作失败重发。
        var key = $"{source.Id:N}:{Email}:{actual.RecipientVersion}";
        var outcome = WorkflowNotificationEmailOutcomeV1.Unknown;
        await scope.ValidateAsync(scopeLease, ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var operation = work.Lookup ? transport.OriginalAsync(key, timeout.Token) :
                transport.SendAsync(new(key, address!, NotifyMatrix.Summary(source.Type)), timeout.Token);
            outcome = await operation.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* 不保存可含地址或正文的提供方错误。 */ }
        var now = DateTimeOffset.UtcNow;
        var (state, reason, next) = outcome switch
        {
            WorkflowNotificationEmailOutcomeV1.Accepted => (Accepted, (string?)null, (DateTimeOffset?)null),
            WorkflowNotificationEmailOutcomeV1.RetryableFailure => (Retryable, "PROVIDER_CONFIRMED_NOT_ACCEPTED", now.AddSeconds(10)),
            _ => (Unknown, "PROVIDER_OUTCOME_UNKNOWN", transport.SupportsOriginalKeyAndLookup ? now.AddSeconds(30) : (DateTimeOffset?)null)
        };
        await WriteAsync(source, Email, actual.RecipientVersion, state, reason, actual.AttemptId, actual.Attempts, next, ct);
        await scope.ValidateAsync(scopeLease, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Current> EligibilityAsync(Source source, string channel, CancellationToken ct)
    {
        var locks = await db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM public."Sys_Users" WHERE "TenantId"={db.CurrentTenantId} AND "Id"={source.Recipient} FOR SHARE
            """).ToListAsync(ct);
        var user = locks.Count == 1 ? await WorkflowNotificationAuthorityV1.ActiveRecipientAsync(db, source.Recipient, ct) : null;
        if (user is null || !await WorkflowNotificationAuthorityV1.HasActionAsync(db, source.Recipient, "oa-inbox", "read", ct) ||
            !await OwnsResourceAsync(source, ct))
            return new(RecipientVersion(source.Id, source.Recipient, null), null, "CURRENT_RECIPIENT_INELIGIBLE");

        await WorkflowNotificationPreferencesV1.LockAsync(db, source.Recipient, ct);
        var prefs = await WorkflowNotificationPreferencesV1.SnapshotAsync(db, source.Recipient, ct);
        var matrix = NotifyMatrix.Rows().SingleOrDefault(row => row.TypeValue == source.Type);
        var isInApp = channel == InApp;
        var address = isInApp ? null : user.Email?.Trim();
        var version = RecipientVersion(source.Id, source.Recipient, address);
        if (matrix is null || !(isInApp ? matrix.InAppSupported : matrix.EmailSupported) ||
            !NotifyMatrix.IsEnabled(prefs.PrefsJson, matrix.TypeKey, isInApp ? NotifyMatrix.ChannelInApp : NotifyMatrix.ChannelEmail))
            return new(version, null, "SUPPRESSED_BY_CURRENT_PREFERENCE");
        if (!isInApp && (!MailAddress.TryCreate(address, out var mailbox) || mailbox.Address != address))
            return new(version, null, "ADDRESS_MISSING");
        return new(version, address, null);
    }

    private Task<bool> OwnsResourceAsync(Source source, CancellationToken ct)
    {
        if (source.Kind == "FORM_SUBMITTED" && source.TaskId is null)
            return Task.FromResult(source.OriginalActor == source.Recipient);
        if (source.Kind is "TASK_ASSIGNED" or "TASK_TRANSFERRED" or "TIMEOUT_REMINDER" or "TIMEOUT_ESCALATED"
            or "DEADLINE_RECALCULATED" or "LEGACY_ENGINE_ASSIGNMENT")
        {
            return source.TaskId is Guid id
                ? db.Wf_FlowTasks.AnyAsync(t => t.Id == id && t.InstanceId == source.InstanceId &&
                    t.AssigneeId == source.Recipient && t.Status == FlowTaskStatus.Pending, ct)
                : Task.FromResult(false);
        }
        return source.InstanceId is Guid instance && source.Kind is "FLOW_FINISHED" or "BRANCH_PRUNED" or "TASK_SENT_BACK"
            or "TIMEOUT_BLOCKED" or "TIMEOUT_EXECUTED" or "LEGACY_ENGINE_STARTER"
            ? db.Wf_FlowInstances.AnyAsync(row => row.Id == instance && row.StarterId == source.Recipient, ct)
            : Task.FromResult(false);
    }

    private Task<Delivery?> ReadDeliveryAsync(Source source, string channel, CancellationToken ct)
        => db.Database.SqlQuery<Delivery>($"""
            SELECT recipient_version AS "RecipientVersion",state AS "State",next_attempt_at AS "NextAttempt",
                attempt_id AS "AttemptId",attempts AS "Attempts" FROM cp6_oa_notification_v1.deliveries
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}
              AND notification_id={source.Id} AND recipient_id={source.Recipient} AND channel={channel}
            ORDER BY updated_at DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    private Task WriteAsync(Source source, string channel, string version, string state, string? reason, Guid? attempt,
        int attempts, DateTimeOffset? next, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_notification_v1.deliveries(tenant_id,environment_id,notification_id,recipient_id,channel,
                recipient_version,state,reason,attempt_id,attempts,next_attempt_at)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{source.Id},{source.Recipient},{channel},{version},{state},{reason},{attempt},{attempts},{next})
            ON CONFLICT(tenant_id,environment_id,notification_id,channel,recipient_version) DO UPDATE
            SET state=excluded.state,reason=excluded.reason,attempt_id=excluded.attempt_id,attempts=excluded.attempts,
                next_attempt_at=excluded.next_attempt_at,updated_at=clock_timestamp()
            WHERE deliveries.recipient_id=excluded.recipient_id
            """, ct);

    private static string RecipientVersion(Guid notification, Guid recipient, string? address)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{notification:D}:{recipient:D}:{address}"))).ToLowerInvariant();
}
