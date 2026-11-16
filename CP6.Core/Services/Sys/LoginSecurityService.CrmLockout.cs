using System.Data.Common;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public partial class LoginSecurityService
{
    private const string PasswordLockout = "login-password-lockout";
    internal const string FactorLockout = "login-factor-lockout";

    internal enum PasswordDecision { Accepted, Rejected, AlreadyLocked, CredentialChanged }

    internal async Task<PasswordDecision> EvaluatePasswordAsync(Sys_User user, Func<Sys_User, bool> verify, bool browser,
        string? requestTenantCode, string? ip, string? userAgent, string reason, CancellationToken ct)
    {
        var userName = user.UserName;
        var audit = new FailureAudit(browser, requestTenantCode, ip, userAgent, reason, null);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (user.UserName != userName) return PasswordDecision.Rejected;
            if (user.LockedUntil > DateTime.Now) return PasswordDecision.AlreadyLocked;
            if (verify(user)) return PasswordDecision.Accepted;
            var result = await RecordPasswordFailureAsync(user, audit, ct);
            if (result != PasswordDecision.CredentialChanged) return result;
        }
        return PasswordDecision.Rejected;
    }

    private async Task<PasswordDecision> RecordPasswordFailureAsync(Sys_User observed, FailureAudit audit, CancellationToken ct)
    {
        var operation = Guid.NewGuid();
        try
        {
            var prepared = await PrepareFailureAsync(observed.TenantId, observed.Id, observed.CredentialRevision, operation, null, audit, ct);
            if (prepared.Intent is not { } intent) return prepared.PasswordResult;
            await CompleteLockoutAsync(observed.TenantId, intent, ct);
        }
        catch (RefreshCompletionUnknownException) when (!ct.IsCancellationRequested)
        {
            // The anonymous password response stays generic while the durable operation is pending.
        }
        return PasswordDecision.Rejected;
    }

    internal async Task RecordFactorFailureAsync(AuthenticationProofV1 proof, Guid lease, string? ip, string? userAgent,
        CancellationToken ct)
    {
        var detail = JsonSerializer.Serialize(new { proof.CompletionRef, proof.PrimaryMethod, proof.PrimaryVerifiedAtUtc, proof.PolicyVersion });
        var audit = new FailureAudit(false, null, ip, userAgent, null, detail);
        var factor = new FailedFactor(proof.CompletionRef, lease, proof.AuthenticationVersion);
        var prepared = await PrepareFailureAsync(proof.TenantId, proof.UserId, null, lease, factor, audit, ct);
        if (prepared.Intent is { } intent) await CompleteLockoutAsync(proof.TenantId, intent, ct);
    }

    // The successful password path calls this after checking the current credential and before accepting a new session.
    internal async Task ResolveLockoutBeforeLoginAsync(Sys_User user, CancellationToken ct)
    {
        if (await ReadLoginLockoutAsync(user.TenantId, user.Id, ct) is { } intent)
            await CompleteLockoutAsync(user.TenantId, intent, ct);
        await _db.Entry(user).ReloadAsync(ct);
    }

    internal async Task ResolveLockoutForSsoAsync(AuthenticationProofV1 proof, AuthenticationCompletionGateV1 gate, CancellationToken ct)
    {
        if (await ReadLoginLockoutAsync(proof.TenantId, proof.UserId, ct) is not { } intent) return;
        await gate.ExecuteAsync(proof, static (_, _) => Task.FromResult(true), ct);
        await CompleteLockoutAsync(proof.TenantId, intent, ct);
    }

    internal async Task RequireNoPendingLoginLockoutAsync(Guid tenant, Guid user, CancellationToken ct)
    {
        if (await ReadLoginLockoutAsync(tenant, user, ct) is not null)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
    }

    private Task<PreparedFailure> PrepareFailureAsync(Guid tenant, Guid userId, Guid? credential, Guid operation,
        FailedFactor? factor, FailureAudit audit, CancellationToken ct) => WithinLoginRootAsync<PreparedFailure>(tenant, async token =>
    {
        var user = await ReadSubjectAsync(tenant, userId, token);
        if (factor is not null)
        {
            if (user is null || AuthSessionVersion.For(user) != factor.AuthenticationVersion)
                throw new InvalidOperationException("REAUTH_REQUIRED");
            await new AuthenticationInteractionStoreV1(_db).RequireLeaseAsync(factor.Completion, factor.Lease, token);
        }
        else if (user is null || user.CredentialRevision != credential)
            return new(PasswordDecision.CredentialChanged, null);

        var state = FailureState.Read(user!);
        var now = DateTime.Now;
        if (factor is null && state.LockedUntil > now) return new(PasswordDecision.AlreadyLocked, null);
        var pending = await IamCrmUserSealV1.ReadAsync(_db, tenant, Guid.Empty, token, userId);
        if (pending is not null && IsLoginLockout(pending))
        {
            if (factor is null) return new(PasswordDecision.Rejected, null);
            throw new RefreshCompletionUnknownException(new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE"));
        }

        var count = state.LastFailure is { } last && (now - last).TotalMinutes > _l.ResetCounterMinutes ? 1 : state.Count + 1;
        var effectiveAt = StoredTime(now);
        var until = count >= _l.MaxFailedAttempts ? StoredTime(now.AddMinutes(_l.LockoutMinutes)) : state.LockedUntil;
        var plan = new LoginLockoutPlan(state, count, effectiveAt, until, factor, audit);
        var physicalStore = _db.InquiryFence?.RegisteredStore(tenant);
        if (physicalStore is null || until == state.LockedUntil)
        {
            await ApplyFailureAsync(user!, plan, until, token);
            await FinishFailureAsync(user!, plan, token);
            return new(PasswordDecision.Rejected, null);
        }
        if (pending is not null)
        {
            if (factor is null) return new(PasswordDecision.Rejected, null);
            throw new RefreshCompletionUnknownException(new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE"));
        }

        var protection = _lockoutProtector ?? throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        _db.RequireIdentityCapture(tenant);
        var transaction = _db.Database.CurrentTransaction!;
        const string preview = "login_lockout_preview";
        await transaction.CreateSavepointAsync(preview, token);
        var advances = await CaptureFailureAsync(user!, plan, token);
        if (advances.Length == 0)
        {
            await transaction.ReleaseSavepointAsync(preview, token);
            await FinishFailureAsync(user!, plan, token);
            return new(PasswordDecision.Rejected, null);
        }

        await transaction.RollbackToSavepointAsync(preview, token);
        await transaction.ReleaseSavepointAsync(preview, token);
        _db.ChangeTracker.Clear();
        await IamAdministrationV1.RequireSealDurabilityAsync(_db, token);
        var kind = factor is null ? PasswordLockout : FactorLockout;
        await IamCrmUserSealV1.InsertAsync(_db, tenant, userId, Guid.Empty, kind, operation,
            protection.Protect(JsonSerializer.Serialize(plan)), physicalStore.Value, advances, token, factor?.Completion);
        var accepted = await IamCrmUserSealV1.ReadAsync(_db, tenant, operation, token);
        return new(PasswordDecision.Rejected, accepted ?? throw LockoutUnknown());
    }, ct);

    private async Task CompleteLockoutAsync(Guid tenant, IamCrmUserSealV1 accepted, CancellationToken ct)
    {
        var protection = _lockoutProtector ?? throw LockoutUnknown();
        var plan = JsonSerializer.Deserialize<LoginLockoutPlan>(protection.Unprotect(accepted.RequestProtected))!;
        var expected = accepted.ReadAdvances().ToDictionary(advance => advance.Aggregate, StringComparer.Ordinal);
        var userSource = $"user:{accepted.UserId:D}";
        try
        {
            if (!accepted.SealStarted && ExpiredBeforeSeal(plan) && await MarkStartedOrCompleteAsync(startSeal: false)) return;
            var fence = _db.InquiryFence ?? throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
            // The durable intent precedes the first physical connection. The started marker precedes remote writes.
            await using var crm = await fence.OpenSealAsync(tenant, accepted.StoreId, ct);
            if (await MarkStartedOrCompleteAsync(startSeal: true)) return;
            await WithinLoginRootAsync(tenant, async token =>
            {
                if (await HasLockoutReceiptAsync(tenant, accepted, token)) return true;
                var current = await RequireOriginalAsync(token);
                if (!current.SealStarted) throw LockoutUnknown();
                var user = await RequireOriginalSubjectAsync(token);
                var advances = await CaptureFailureAsync(user, plan, token);
                if (advances.SingleOrDefault(advance => advance.Aggregate == userSource) != expected.GetValueOrDefault(userSource) ||
                    advances.Any(advance => expected.TryGetValue(advance.Aggregate, out var original) && advance != original))
                    throw LockoutUnknown();

                await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, tenant, current.StoreId,
                    advances.Select(advance => (advance.Aggregate, advance.Version)).ToArray(), token);
                await FinishAcceptedAsync(current, user, token);
                return true;
            }, ct);
        }
        catch (RefreshCompletionUnknownException) when (!ct.IsCancellationRequested)
        {
            if (await ReadReceiptAfterUnknownAsync(tenant, accepted, ct)) return;
            throw;
        }
        catch (TenantManagementExceptionV1 error) when (error.Message == "AUTHORITY_FENCE_UNAVAILABLE")
        {
            throw new RefreshCompletionUnknownException(error);
        }

        Task<bool> MarkStartedOrCompleteAsync(bool startSeal) => WithinLoginRootAsync(tenant, async token =>
        {
            if (await HasLockoutReceiptAsync(tenant, accepted, token)) return true;
            var current = await RequireOriginalAsync(token);
            var user = await RequireOriginalSubjectAsync(token);
            if (current.SealStarted) return false;
            if (ExpiredBeforeSeal(plan))
            {
                await ApplyFailureAsync(user, plan, plan.Before.LockedUntil, token);
                await FinishAcceptedAsync(current, user, token, expiredWithoutSeal: true);
                return true;
            }
            if (startSeal)
            {
                await IamAdministrationV1.RequireSealDurabilityAsync(_db, token);
                await current.MarkStartedAsync(_db, tenant, token);
            }
            return false;
        }, ct);

        async Task<IamCrmUserSealV1> RequireOriginalAsync(CancellationToken token)
        {
            var current = await IamCrmUserSealV1.ReadAsync(_db, tenant, accepted.OperationId, token) ?? throw LockoutUnknown();
            if (current.UserId != accepted.UserId || current.Kind != accepted.Kind || current.CompletionId != accepted.CompletionId ||
                current.StoreId != accepted.StoreId || current.RequestProtected != accepted.RequestProtected || current.SourceSeals != accepted.SourceSeals)
                throw LockoutUnknown();
            return current;
        }

        async Task<Sys_User> RequireOriginalSubjectAsync(CancellationToken token)
        {
            var user = await ReadSubjectAsync(tenant, accepted.UserId, token);
            if (user is null || FailureState.Read(user) != plan.Before) throw LockoutUnknown();
            if (plan.Factor is { } failedFactor)
                await new AuthenticationInteractionStoreV1(_db).RequireLeaseAsync(failedFactor.Completion, failedFactor.Lease, token);
            return user;
        }

        async Task FinishAcceptedAsync(IamCrmUserSealV1 current, Sys_User user, CancellationToken token, bool expiredWithoutSeal = false)
        {
            await FinishFailureAsync(user, plan, token, expiredWithoutSeal);
            await IamAdministrationV1.RequireSealDurabilityAsync(_db, token);
            var result = JsonSerializer.Serialize(new { UserId = user.Id, plan.Count, plan.EffectiveAt, plan.LockedUntil, ExpiredWithoutSeal = expiredWithoutSeal });
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_authentication_v1.iam_operations(tenant_id,operation_id,actor_id,kind,state,result_json)
                VALUES ({tenant},{current.OperationId},{Guid.Empty},{current.Kind},'COMMITTED',{result})
                """, token);
            await current.RemoveAsync(_db, tenant, token);
        }
    }

    private async Task<CrmUserSealAdvanceV1[]> CaptureFailureAsync(Sys_User user, LoginLockoutPlan plan, CancellationToken ct)
    {
        _db.CaptureLoginLockoutAt = plan.EffectiveAt;
        try
        {
            return await IamAdministrationV1.CaptureCredentialAdvancesAsync(_db, user.TenantId, user.Id,
                () => ApplyFailureAsync(user, plan, plan.LockedUntil, ct));
        }
        finally { _db.CaptureLoginLockoutAt = null; }
    }

    private Task ApplyFailureAsync(Sys_User user, LoginLockoutPlan plan, DateTime? until, CancellationToken ct)
    {
        user.FailedLoginCount = plan.Count;
        user.LastFailedLoginAt = plan.EffectiveAt;
        user.LockedUntil = until;
        return _db.SaveChangesAsync(ct);
    }

    private async Task FinishFailureAsync(Sys_User user, LoginLockoutPlan plan, CancellationToken ct, bool expiredWithoutSeal = false)
    {
        var factor = plan.Factor;
        var type = factor is not null ? SecurityEventType.TwoFactorFailed :
            plan.Audit.Browser && !expiredWithoutSeal && plan.LockedUntil > plan.EffectiveAt ? SecurityEventType.AccountLocked : SecurityEventType.LoginFailed;
        var row = SecurityAuditService.CreateRecord(type, user.Id, user.UserName, plan.Audit.RequestTenantCode,
            plan.Audit.Ip, plan.Audit.UserAgent, plan.Audit.Reason);
        row.TenantId = user.TenantId;
        row.CreatedAt = plan.EffectiveAt;
        row.ContextJson = plan.Audit.FactorDetail;
        await SecurityAuditService.SaveWithinRootAsync(_db, row, bestEffort: factor is null, ct);
        if (factor is not null && await new AuthenticationInteractionStoreV1(_db).FinishAsync(factor.Completion, factor.Lease, false, ct) != 1)
            throw new InvalidOperationException("E-SEC-013");
    }

    private async Task<Sys_User?> ReadSubjectAsync(Guid tenant, Guid userId, CancellationToken ct)
    {
        var user = await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(row => row.TenantId == tenant && row.Id == userId, ct);
        if (user is null) return null;
        var tracked = _db.Sys_Users.Local.SingleOrDefault(row => row.Id == userId);
        if (tracked is null)
        {
            _db.Sys_Users.Attach(user);
            return user;
        }
        var entry = _db.Entry(tracked);
        entry.CurrentValues.SetValues(user);
        entry.OriginalValues.SetValues(user);
        return tracked;
    }

    private async Task<IamCrmUserSealV1?> ReadLoginLockoutAsync(Guid tenant, Guid user, CancellationToken ct)
    {
        var pending = await IamCrmUserSealV1.ReadAsync(_db, tenant, Guid.Empty, ct, user);
        return pending is not null && IsLoginLockout(pending) ? pending : null;
    }

    internal static bool IsLoginLockout(IamCrmUserSealV1 pending) => pending.Kind is PasswordLockout or FactorLockout;

    private Task<bool> HasLockoutReceiptAsync(Guid tenant, IamCrmUserSealV1 intent, CancellationToken ct)
        => _db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_authentication_v1.iam_operations
                WHERE tenant_id={tenant} AND operation_id={intent.OperationId} AND actor_id={Guid.Empty}
                  AND kind={intent.Kind} AND state='COMMITTED') AS "Value"
            """).SingleAsync(ct);

    private async Task<bool> ReadReceiptAfterUnknownAsync(Guid tenant, IamCrmUserSealV1 intent, CancellationToken ct)
    {
        try { return await HasLockoutReceiptAsync(tenant, intent, ct); }
        catch (DbException) { return false; }
    }

    private async Task<T> WithinLoginRootAsync<T>(Guid tenant, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql() || _db.CurrentTenantId != tenant || _db.Database.CurrentTransaction is not null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
        var result = await work(ct);
        try { await transaction.CommitAsync(ct); }
        catch (Exception error) when (error is DbException or TimeoutException or OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            throw new RefreshCompletionUnknownException(error);
        }
        return result;
    }

    private static bool ExpiredBeforeSeal(LoginLockoutPlan plan) => plan.LockedUntil > plan.EffectiveAt && plan.LockedUntil <= DateTime.Now;
    private static DateTime StoredTime(DateTime value) => value.AddTicks(-(value.Ticks % 10));
    private static RefreshCompletionUnknownException LockoutUnknown()
        => new(new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE"));

    private sealed record PreparedFailure(PasswordDecision PasswordResult, IamCrmUserSealV1? Intent);
    private sealed record FailedFactor(Guid Completion, Guid Lease, string AuthenticationVersion);
    private sealed record FailureAudit(bool Browser, string? RequestTenantCode, string? Ip, string? UserAgent, string? Reason, string? FactorDetail);
    private sealed record LoginLockoutPlan(FailureState Before, int Count, DateTime EffectiveAt, DateTime? LockedUntil, FailedFactor? Factor, FailureAudit Audit);

    private sealed record FailureState(Guid CredentialRevision, int Count, DateTime? LastFailure, DateTime? LockedUntil, DateTime? LastSuccess)
    {
        internal static FailureState Read(Sys_User user)
            => new(user.CredentialRevision, user.FailedLoginCount, user.LastFailedLoginAt, user.LockedUntil, user.LastLoginTime);
    }
}
