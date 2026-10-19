using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CP6.Core.Services.Sys;

public partial class RefreshTokenService
{
    private void RequireOwnedCleanTransaction()
    {
        if (_db.Database.CurrentTransaction != null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("REFRESH_REQUIRES_OWNED_CLEAN_TRANSACTION");
    }

    // Hold current account/tenant rows through the issue/rotation commit. Family then token
    // locks retain the logout order. No current value is used to repair missing original evidence.
    private async Task<Sys_User> LockActiveUserAsync(Guid userId, Guid tenantId)
    {
        // Authentication writers now acquire this root before user/tenant rows. Match that
        // order even before the full policy adapters are enabled for refresh completion.
        await AuthenticationCompletionGateV1.AcquireAsync(_db);
        var organization = await _db.Sys_Tenants.FromSqlInterpolated($"SELECT * FROM public.\"Sys_Tenants\" WHERE \"Id\"={tenantId} FOR SHARE")
            .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();
        // Serialize issuance/rotation for this account so a genuine reuse revocation cannot miss
        // a replacement concurrently inserted by another device. Other accounts remain independent.
        var user = await _db.Sys_Users.FromSqlInterpolated($"SELECT * FROM public.\"Sys_Users\" WHERE \"Id\"={userId} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();
        if (organization is not { Enable: true } || organization.ExpireDate <= DateTime.Now
            || user is not { Enable: true } || user.TenantId != tenantId || user.LockedUntil > DateTime.Now)
            throw new InvalidOperationException("E-SEC-007");
        return user;
    }

    private async Task<string> IssuePostgreSqlAsync(Sys_User original, string? ip, string? ua, RefreshTokenClientContext client)
    {
        RequireOwnedCleanTransaction();
        await using var tx = await _db.Database.BeginTransactionAsync();
        var added = new List<object>(); var commitAttempted = false;
        try
        {
            var current = await LockActiveUserAsync(original.Id, original.TenantId);
            var version = AuthSessionVersion.For(original);
            if (version != AuthSessionVersion.For(current)) throw new InvalidOperationException("E-SEC-007");
            _tenant.CurrentTenantId = current.TenantId;
            var raw = NewRaw(); Guid? familyId = null;
            if (_configuration?.GetValue<bool>("CrmOidc:Enabled") == true && client.ClientKind == "Web")
            {
                var family = new Sys_BrowserSession { Id = Guid.NewGuid(), UserId = current.Id,
                    TenantId = current.TenantId, AuthenticationVersion = version };
                familyId = family.Id; added.Add(family); _db.Sys_BrowserSessions.Add(family);
            }
            var row = NewRow(current, client, raw, ip, ua, familyId, version);
            added.Add(row); _db.Sys_RefreshTokens.Add(row);
            await _db.SaveChangesAsync();
            await RequireCurrentTenantLifetimeAsync(current.TenantId);
            commitAttempted = true; await tx.CommitAsync(); return raw;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            finally { foreach (var item in added) _db.Entry(item).State = EntityState.Detached; }
            if (commitAttempted) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }

    private Sys_RefreshToken NewRow(Sys_User user, RefreshTokenClientContext client, string raw,
        string? ip, string? ua, Guid? family, string version) => new()
    {
        UserId = user.Id, TenantId = user.TenantId, TokenHash = HashOf(raw), BrowserSessionId = family,
        AuthenticationVersion = version, ExpiresAt = DateTime.Now.AddDays(_t.RefreshTokenDays),
        CreatedIp = ip, UserAgent = ua, ClientKind = client.ClientKind, DeviceId = client.DeviceId, AppVersion = client.AppVersion
    };

    public Task<(string newToken, Sys_User user)> RotateForCompletionAsync(string raw, string? ip, string? ua, RefreshTokenClientContext client)
        => _db.Database.IsNpgsql() ? RotatePostgreSqlAsync(raw, ip, ua, client, pendingCompletion: true) : RotateAsync(raw, ip, ua, client);

    private async Task<(string newToken, Sys_User user)> RotatePostgreSqlAsync(string raw, string? ip, string? ua, RefreshTokenClientContext client, bool pendingCompletion = false)
    {
        RequireOwnedCleanTransaction(); var hash = HashOf(raw);
        var observed = await _db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (observed == null || observed.ExpiresAt <= DateTime.Now) throw new InvalidOperationException("E-SEC-007");
        await using var tx = await _db.Database.BeginTransactionAsync();
        Sys_RefreshToken? replacement = null; var commitAttempted = false; var committed = false;
        try
        {
            var user = await LockActiveUserAsync(observed.UserId, observed.TenantId);
            if (string.IsNullOrEmpty(observed.AuthenticationVersion) || observed.AuthenticationVersion != AuthSessionVersion.For(user))
                throw new InvalidOperationException("E-SEC-007");
            if (observed.BrowserSessionId.HasValue) await RequireBrowserSessionAsync(observed, user, lockForRotation: true);
            var row = await _db.Sys_RefreshTokens.FromSqlInterpolated($"SELECT * FROM public.\"Sys_RefreshTokens\" WHERE \"Id\"={observed.Id} FOR UPDATE")
                .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();
            if (row == null || row.UserId != observed.UserId || row.TenantId != observed.TenantId
                || row.BrowserSessionId != observed.BrowserSessionId || row.AuthenticationVersion != observed.AuthenticationVersion
                || row.ExpiresAt <= DateTime.Now || row.CompletionRejectedAtUtc != null || row.CompletionPending)
                throw new InvalidOperationException("E-SEC-007");
            if (!string.Equals(row.ClientKind, client.ClientKind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.DeviceId, client.DeviceId, StringComparison.Ordinal))
                throw new InvalidOperationException("E-SEC-024");
            if (row.RevokedAt != null)
            {
                // A simultaneous loser and a delivery-rejected replacement are ordinary failures.
                // Only a previously observed, still-authentic reused token enters the risk path.
                if (observed.RevokedAt == null || row.ReplacedByTokenHash == null
                    || await _db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TokenHash == row.ReplacedByTokenHash && x.UserId == row.UserId && x.TenantId == row.TenantId
                        && (x.CompletionRejectedAtUtc != null || x.CompletionPending)))
                    throw new InvalidOperationException("E-SEC-007");
                await _db.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.TenantId == user.TenantId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.Now));
                commitAttempted = true; await tx.CommitAsync(); committed = true;
                throw new InvalidOperationException("E-SEC-008");
            }
            _tenant.CurrentTenantId = user.TenantId;
            var nextRaw = NewRaw(); replacement = NewRow(user, client, nextRaw, ip, ua, row.BrowserSessionId, row.AuthenticationVersion!);
            replacement.CompletionPending = pendingCompletion;
            await _db.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.Id == row.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.Now).SetProperty(x => x.ReplacedByTokenHash, replacement.TokenHash));
            _db.Sys_RefreshTokens.Add(replacement); await _db.SaveChangesAsync();
            // Lock waits and persistence can consume the remaining source lifetime.
            if (row.ExpiresAt <= DateTime.Now) throw new InvalidOperationException("E-SEC-007");
            await RequireCurrentTenantLifetimeAsync(user.TenantId);
            commitAttempted = true; await tx.CommitAsync(); committed = true;
            return (nextRaw, user);
        }
        catch (Exception error)
        {
            if (committed) throw;
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            finally { if (replacement != null) _db.Entry(replacement).State = EntityState.Detached; }
            if (commitAttempted) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }

    public async Task RejectCompletionAsync(string rawToken)
    {
        if (!_db.Database.IsNpgsql()) { await RevokeAsync(rawToken); return; }
        try
        {
            var hash = HashOf(rawToken);
            // One atomic statement, no request cancellation: persist the rejection even after disconnect.
            if (await _db.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.Now)
                    .SetProperty(x => x.CompletionPending, false)
                    .SetProperty(x => x.CompletionRejectedAtUtc, DateTime.UtcNow), CancellationToken.None) != 1)
                throw new InvalidOperationException("Replacement missing during completion rejection.");
        }
        catch (Exception error) { throw new RefreshCompletionUnknownException(error); }
    }

    private async Task RequireCurrentTenantLifetimeAsync(Guid tenantId)
    {
        var expiry = await _db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == tenantId).Select(x => x.ExpireDate).SingleAsync();
        if (expiry <= DateTime.Now) throw new InvalidOperationException("E-SEC-007");
    }

    public async Task CompleteRotationAsync(string rawToken)
    {
        if (!_db.Database.IsNpgsql()) return;
        RequireOwnedCleanTransaction(); var hash = HashOf(rawToken);
        var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (row == null) throw new InvalidOperationException("E-SEC-007");
        await using var tx = await _db.Database.BeginTransactionAsync(); var commitAttempted = false;
        try
        {
            var user = await LockActiveUserAsync(row.UserId, row.TenantId);
            if (row.AuthenticationVersion != AuthSessionVersion.For(user)) throw new InvalidOperationException("E-SEC-007");
            if (row.BrowserSessionId.HasValue) await RequireBrowserSessionAsync(row, user, lockForRotation: true);
            if (await _db.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.Id == row.Id && x.CompletionPending
                    && x.CompletionRejectedAtUtc == null && x.RevokedAt == null && x.ExpiresAt > DateTime.Now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletionPending, false)) != 1)
                throw new InvalidOperationException("E-SEC-007");
            // SQL now() is the transaction start in PostgreSQL; recheck wall-clock deadlines after lock waits.
            var sourceExpiry = await _db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.UserId == row.UserId && x.TenantId == row.TenantId && x.ReplacedByTokenHash == hash)
                .Select(x => (DateTime?)x.ExpiresAt).SingleOrDefaultAsync();
            if (sourceExpiry == null || sourceExpiry <= DateTime.Now || row.ExpiresAt <= DateTime.Now)
                throw new InvalidOperationException("E-SEC-007");
            await RequireCurrentTenantLifetimeAsync(row.TenantId);
            commitAttempted = true; await tx.CommitAsync();
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            if (commitAttempted) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }
}

public sealed class RefreshCompletionUnknownException(Exception inner)
    : Exception("AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN", inner);
