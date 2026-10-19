using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>PostgreSQL authentication Owner boundary. Callbacks must stage output until this method returns.</summary>
public sealed class AuthenticationCompletionGateV1(CP6Context db, AuthenticationPolicyServiceV1 policies)
{
    public static Task<long> AcquireAsync(CP6Context db, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        return db.Database.SqlQueryRaw<long>("SELECT cp6_authentication_v1.acquire() AS \"Value\"").SingleAsync(ct);
    }

    public async Task<T> ExecuteAsync<T>(AuthenticationProofV1 proof,
        Func<AuthenticationAuthorityV1, CancellationToken, Task<T>> complete, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || db.CurrentTenantId != proof.TenantId)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        var original = db.ChangeTracker.Entries().ToDictionary(x => x.Entity, x => x.CurrentValues.Clone());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var commitAttempted = false;
        try
        {
            var authority = await ValidateAsync(proof, ct);
            var result = await complete(authority, ct);
            await db.SaveChangesAsync(ct);
            // Clock time can pass while a callback persists data or waits for a dependent service.
            RequireInteraction(proof);
            await RequireActiveAsync(proof.UserId, proof.TenantId, ct);
            if ((await policies.ReadAsync(proof.TenantId, ct)).Version != proof.PolicyVersion)
                throw new InvalidOperationException("REAUTH_REQUIRED");
            ct.ThrowIfCancellationRequested();
            commitAttempted = true;
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            finally
            {
                foreach (var entry in db.ChangeTracker.Entries().ToArray())
                {
                    if (!original.TryGetValue(entry.Entity, out var values)) entry.State = EntityState.Detached;
                    else { entry.CurrentValues.SetValues(values); entry.OriginalValues.SetValues(values); entry.State = EntityState.Unchanged; }
                }
            }
            if (commitAttempted) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }

    public async Task<AuthenticationAuthorityV1> ValidateAsync(AuthenticationProofV1 proof, CancellationToken ct = default)
    {
        // This method also acquires the root; a caller cannot omit it by opening its own transaction.
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId != proof.TenantId)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        await AcquireAsync(db, ct);
        RequireInteraction(proof);
        var current = await RequireActiveAsync(proof.UserId, proof.TenantId, ct);
        var policy = await policies.ReadAsync(proof.TenantId, ct);
        if (proof.AuthenticationVersion != AuthSessionVersion.For(current) || proof.PolicyVersion != policy.Version)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (proof.ExpiresAtUtc > proof.PrimaryVerifiedAtUtc.AddMinutes(policy.InteractionMinutes))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (proof.PrimaryMethod == AuthenticationPrimaryMethod.Sso)
        {
            if (!policy.SsoEnabled || string.IsNullOrWhiteSpace(proof.SsoConfigurationVersion)
                || proof.SsoConfigurationVersion != policy.SsoConfigurationVersion)
                throw new InvalidOperationException("REAUTH_REQUIRED");
        }
        else if (policy.SsoEnforced && (proof.PrimaryMethod != AuthenticationPrimaryMethod.Password || !current.AllowPasswordFallback))
            throw new InvalidOperationException("SSO_REQUIRED");
        // EF may already track the account loaded during the primary interaction. Never reuse
        // stale tracked admission fields after waiting for the root.
        var tracked = db.Sys_Users.Local.SingleOrDefault(x => x.Id == current.Id);
        if (tracked != null)
        {
            db.Entry(tracked).CurrentValues.SetValues(current);
            db.Entry(tracked).OriginalValues.SetValues(current);
            current = tracked;
        }
        else db.Sys_Users.Attach(current);
        return new(proof, policy, current);
    }

    private async Task<Sys_User> RequireActiveAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        var user = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now
            || user is not { Enable: true } || user.TenantId != tenantId || user.LockedUntil > DateTime.Now)
            throw new InvalidOperationException("AUTHENTICATION_DENIED");
        return user;
    }

    private static void RequireInteraction(AuthenticationProofV1 proof)
    {
        var now = DateTimeOffset.UtcNow;
        if (proof.CompletionRef == Guid.Empty || proof.TenantId == Guid.Empty || proof.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(proof.ClientBinding) || string.IsNullOrWhiteSpace(proof.PrimaryEvidenceRef)
            || proof.ClientKind is not ("Web" or "Windows" or "Android") || !Enum.IsDefined(proof.PrimaryMethod)
            || proof.PrimaryVerifiedAtUtc > now || proof.ExpiresAtUtc <= now || proof.ExpiresAtUtc <= proof.PrimaryVerifiedAtUtc
            || string.IsNullOrWhiteSpace(proof.AuthenticationVersion) || string.IsNullOrWhiteSpace(proof.PolicyVersion))
            throw new InvalidOperationException("REAUTH_REQUIRED");
    }
}
