using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Oa;

internal enum NativeTimeoutFailureKindV2 { Retryable, Denied, AuthorityUnavailable, Unknown, Permanent }

public sealed partial class NativeTimeoutOwnerV2
{
    private async Task<NativeTimeoutClaimV2?> OriginalClaimAsync(NativeTimeoutClaimV2 claim, CancellationToken ct)
    {
        var saved = await Claims().SingleOrDefaultAsync(c => c.ExecutionId == claim.ExecutionId && c.ServiceId == options.ServiceId, ct);
        if (saved != null && (claim.ServiceId != options.ServiceId || InputDigest(saved) != InputDigest(claim)))
            throw Error("TIMEOUT_ORIGINAL_INPUT_MISMATCH");
        return saved;
    }

    private async Task<NativeTimeoutResultV2?> OriginalResultAsync(Guid id, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT body AS "Value" FROM cp6_native_timeout_v2.results WHERE execution_id={id}
            """).SingleOrDefaultAsync(ct);
        return json == null ? null : Parse<NativeTimeoutResultV2>(json);
    }

    private async Task HistoryServiceAsync(CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.registrations
             WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
               AND service_id={options.ServiceId} AND host_key={options.HostKey} AND history_read) AS "Value"
            """).SingleAsync(ct)) throw Error("TIMEOUT_HISTORY_DENIED", 403);
    }

    /// <summary>按当前读取资格回查原作业。缺回执时只接受共同事务权威的原输入继续证明。</summary>
    public async Task<NativeTimeoutRecoveryV2> ResolveOriginalAsync(NativeTimeoutClaimV2 claim, CancellationToken ct = default)
    {
        Entry();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await fence.Root(ct);
        await HistoryServiceAsync(ct);

        var digest = InputDigest(claim);
        NativeTimeoutRecoveryV2 View(string outcome, NativeTimeoutRecoveryActionV2 action, string? reason = null,
            NativeTimeoutResultV2? result = null) => new(claim.ExecutionId, claim.Revision, digest, outcome, action, reason, result);

        var saved = await OriginalClaimAsync(claim, ct);
        if (saved == null)
            return View("UNKNOWN", NativeTimeoutRecoveryActionV2.Isolate, "ORIGINAL_RESULT_NOT_LOCATED");
        var result = await OriginalResultAsync(claim.ExecutionId, ct);
        if (result != null) return View("TERMINAL", NativeTimeoutRecoveryActionV2.ReturnOriginal, result: result);

        try
        {
            await RequireEffectContextAsync(saved, ct);
            await fence.AcquireAsync(ct);
            await Ready(ct);
            var registration = await Service(ct);
            await EnsureDispatchEnabledAsync(ct);
            await RequireOriginalPendingAsync(saved, ct);
            if (saved.ClaimId != claim.ClaimId || saved.Epoch != claim.Epoch || saved.RegistrationRevision != registration.Revision)
                throw Error("TIMEOUT_LEASE_LOST");
            return View("NOT_APPLIED_PROVEN", NativeTimeoutRecoveryActionV2.ContinueOriginal);
        }
        catch (ScopeAuthorizationExceptionV1 error) when (error.Message == "TIMEOUT_LEASE_LOST")
        {
            return View("PROCESSING_OR_UNCONFIRMED", NativeTimeoutRecoveryActionV2.Wait, error.Message);
        }
        catch (ScopeAuthorizationExceptionV1 error) when (error.Message == "TIMEOUT_ORIGINAL_ISOLATED")
        {
            return View("UNKNOWN", NativeTimeoutRecoveryActionV2.Isolate, error.Message);
        }
        catch (ScopeAuthorizationExceptionV1 error)
        {
            return View("UNKNOWN", NativeTimeoutRecoveryActionV2.Blocked, error.Message);
        }
    }

    private async Task RequireOriginalPendingAsync(NativeTimeoutClaimV2 saved, CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.pending p
             JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=p.task_id AND d.revision=p.revision
             WHERE p.task_id={saved.TaskId} AND p.revision={saved.Revision} AND p.occurrence={saved.Occurrence}
               AND p.state IN('READY','CLAIMED') AND d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId}) AS "Value"
            """).SingleAsync(ct))
            throw Error("TIMEOUT_ORIGINAL_NOT_PENDING");
        if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.attempts a
              JOIN cp6_native_timeout_v2.attempt_observations o ON o.attempt_id=a.id
              WHERE a.execution_id={saved.ExecutionId} AND o.state='ISOLATED') AS "Value"
            """).SingleAsync(ct))
            throw Error("TIMEOUT_ORIGINAL_ISOLATED");
    }

    /// <summary>原 Owner 按固定输入继续已证明可恢复的操作。租约到期时，在共同门内重新领取同一操作。</summary>
    public async Task<NativeTimeoutResultV2> ResumeOriginalAsync(NativeTimeoutClaimV2 claim, string digest, CancellationToken ct = default)
    {
        if (!string.Equals(digest, InputDigest(claim), StringComparison.Ordinal))
            throw Error("TIMEOUT_ORIGINAL_INPUT_MISMATCH");
        Entry();
        NativeTimeoutClaimV2 next;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await fence.Root(ct);
            await HistoryServiceAsync(ct);
            var saved = await OriginalClaimAsync(claim, ct) ?? throw Error("TIMEOUT_ORIGINAL_NOT_LOCATED");
            if (await OriginalResultAsync(claim.ExecutionId, ct) is { } result)
            {
                await ReconcileResultsAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }

            await RequireEffectContextAsync(saved, ct);
            var gate = await fence.AcquireAsync(ct);
            await Ready(ct);
            var registration = await Service(ct);
            await EnsureDispatchEnabledAsync(ct);
            await RequireOriginalPendingAsync(saved, ct);
            if (saved.ClaimId != claim.ClaimId || saved.Epoch != claim.Epoch || saved.RegistrationRevision != registration.Revision)
                throw Error("TIMEOUT_LEASE_LOST");
            var now = await Clock(ct);
            next = saved;
            if (saved.LeaseUntil <= now)
            {
                var schedule = await EnsureScheduleAsync(now, ct);
                if (!Parse<ScheduleSettings>(schedule.Settings).Definition.Enabled)
                    throw Error("TIMEOUT_SCHEDULE_DISABLED", 403);
                var run = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_native_timeout_v2.runs(id,tenant_id,environment_id,service_id,schedule_version,evaluated_at,
                      claim_count,host_observed,recovery)
                    VALUES({run},{db.CurrentTenantId},{fence.EnvironmentId},{options.ServiceId},{schedule.Version},{now},1,false,true)
                    """, ct);
                next = saved with { ClaimId = Guid.NewGuid(), Epoch = saved.Epoch + 1, LeaseUntil = now.AddSeconds(60) };
                await SaveClaimAsync(next, run, ct);
                await fence.ValidateAsync(gate, ct);
                await tx.CommitAsync(ct);
            }
        }
        return await ExecuteAsync(next, ct);
    }

    private async Task RequireEffectContextAsync(NativeTimeoutClaimV2 saved, CancellationToken ct)
    {
        var context = await db.Database.SqlQuery<string>($"""
            SELECT context_kind AS "Value" FROM cp6_native_timeout_v2.deadlines
            WHERE task_id={saved.TaskId} AND revision={saved.Revision} AND tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
            """).SingleAsync(ct);
        if (!fence.Owns(db) || (context == "SFS" ? !forms.OwnsTimeoutContext(db) : !approvals.OwnsTimeoutContext(db)))
            throw Error("TIMEOUT_COMMON_AUTHORITY_UNAVAILABLE", 503);
    }

    internal static NativeTimeoutFailureKindV2 Classify(Exception error)
    {
        if (error is ScopeAuthorizationExceptionV1 declared)
        {
            if (declared.Message == "TIMEOUT_OUTCOME_UNKNOWN") return NativeTimeoutFailureKindV2.Unknown;
            if (declared.Status == 503) return NativeTimeoutFailureKindV2.AuthorityUnavailable;
            if (declared.Status is 401 or 403 || declared.Message is "TIMEOUT_LEASE_LOST" or "TIMEOUT_CLAIM_VERSION_CHANGED" or "TIMEOUT_ORIGINAL_NOT_PENDING")
                return NativeTimeoutFailureKindV2.Denied;
            return NativeTimeoutFailureKindV2.Permanent;
        }
        var storage = error is DbUpdateException update ? update.InnerException : error;
        return storage switch
        {
            PostgresException { SqlState: "40001" or "40P01" or "55P03" } => NativeTimeoutFailureKindV2.Retryable,
            PostgresException { SqlState: "40003" } => NativeTimeoutFailureKindV2.Unknown,
            PostgresException => NativeTimeoutFailureKindV2.Permanent,
            NpgsqlException { IsTransient: true } => NativeTimeoutFailureKindV2.Retryable,
            _ => NativeTimeoutFailureKindV2.Permanent
        };
    }

    internal Task ObserveFailureAsync(NativeTimeoutClaimV2 claim, NativeTimeoutFailureKindV2 kind, bool retrying, CancellationToken ct) => kind switch
    {
        NativeTimeoutFailureKindV2.Unknown => ObserveAsync(claim, "UNKNOWN", "OWNER_RESULT_REQUIRED", ct),
        NativeTimeoutFailureKindV2.Denied => ObserveAsync(claim, "BLOCKED", "CURRENT_AUTHORITY_DENIED", ct),
        NativeTimeoutFailureKindV2.AuthorityUnavailable => ObserveAsync(claim, "BLOCKED", "COMMON_AUTHORITY_UNAVAILABLE", ct),
        NativeTimeoutFailureKindV2.Retryable when retrying => ObserveAsync(claim, "RETRYING", "TRANSIENT_BEFORE_EFFECT", ct),
        NativeTimeoutFailureKindV2.Retryable => ObserveAsync(claim, "ISOLATED", "RETRY_LIMIT_REACHED", ct),
        _ => ObserveAsync(claim, "ISOLATED", "PERMANENT_OR_CONFLICT", ct)
    };

    internal Task ObserveWorkerFailureAsync(NativeTimeoutClaimV2 claim, NativeTimeoutFailureKindV2 kind, bool retrying, CancellationToken ct) =>
        RecordWhileDatabaseAvailable(() => ObserveFailureAsync(claim, kind, retrying, ct));

    private Task<int> ObserveAsync(NativeTimeoutClaimV2 claim, string state, string? reason, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_native_timeout_v2.attempt_observations(attempt_id,state,reason)
            SELECT a.id,{state},{reason} FROM cp6_native_timeout_v2.attempts a
              JOIN cp6_native_timeout_v2.runs r ON r.id=a.run_id
            WHERE a.id={claim.ClaimId} AND a.execution_id={claim.ExecutionId} AND a.input_digest={InputDigest(claim)}
              AND r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId} AND r.service_id={options.ServiceId}
            ON CONFLICT DO NOTHING
            """, ct);

    private Task<int> ReconcileResultsAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_native_timeout_v2.attempt_observations(attempt_id,state,reason)
        SELECT a.id,'OWNER_VERIFIED','ORIGINAL_RECEIPT' FROM cp6_native_timeout_v2.attempts a
          JOIN cp6_native_timeout_v2.runs s ON s.id=a.run_id
          JOIN cp6_native_timeout_v2.results r ON r.execution_id=a.execution_id
        WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={fence.EnvironmentId} AND s.service_id={options.ServiceId}
          AND (r.body::jsonb->>'claimEpoch')::bigint=a.epoch
          AND NOT EXISTS(SELECT FROM cp6_native_timeout_v2.attempt_observations o WHERE o.attempt_id=a.id AND o.state='OWNER_VERIFIED')
        ORDER BY s.evaluated_at,a.id LIMIT 100
        ON CONFLICT DO NOTHING
        """, ct);

    internal async Task ObserveResultAsync(NativeTimeoutClaimV2 claim, NativeTimeoutResultV2 result, CancellationToken ct)
    {
        if (result.State == "DEFERRED_STATE")
            await ObserveAsync(claim, "BLOCKED", "SUSPENDED", ct);
        else if (result.ClaimEpoch == claim.Epoch)
            await ObserveAsync(claim, "OWNER_VERIFIED", "ORIGINAL_RECEIPT", ct);
        else
            await ReconcileResultsAsync(ct);
    }
}
