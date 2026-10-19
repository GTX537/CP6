using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public partial class SsoService
{
    // Only called after signature, issuer, audience, nonce and one-use state validation.
    private async Task<Sys_User> MapPostgreSqlIdentityAsync(AuthenticationPolicyV1 original, Sys_TenantSsoConfig exchangedConfig,
        string issuer, string subject, string email, bool verifiedEmail, string reference, string kind, string binding,
        DateTimeOffset verifiedAt)
    {
        if (_db.Database.CurrentTransaction != null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("SSO_IDENTITY_WRITER_UNAVAILABLE");
        var deadline = verifiedAt.AddMinutes(original.InteractionMinutes);
        MappingIntent intent;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var attempted = false;
            try
            {
                await AuthenticationCompletionGateV1.AcquireAsync(_db);
                await RequireMappingAuthority(original, exchangedConfig, reference, kind, binding, deadline);
                var issuerBytes = Encoding.UTF8.GetBytes(issuer); var subjectBytes = Encoding.UTF8.GetBytes(subject);
                var pending = await _db.Database.SqlQuery<string>($"SELECT i.operation_ref AS \"Value\" FROM cp6_authentication_v1.sso_mapping_intents i WHERE i.tenant_id={original.TenantId} AND i.issuer={issuerBytes} AND i.subject={subjectBytes} AND NOT EXISTS(SELECT FROM cp6_authentication_v1.sso_mapping_receipts r WHERE r.operation_ref=i.operation_ref) ORDER BY i.created_at LIMIT 1").SingleOrDefaultAsync();
                if (pending != null) throw new SsoMappingRecoveryRequiredV1(pending);
                var exact = await SsoIdentityQueriesV1.ExactAsync(_db, original.TenantId, issuer, subject);
                if (exact != null)
                {
                    RequireActive(exact); await tx.CommitAsync(); return exact;
                }
                intent = await PrepareMapping(original.TenantId, exchangedConfig, issuer, subject, email, verifiedEmail);
                _db.RequireIdentityCapture(original.TenantId);
                var document = JsonSerializer.Serialize(intent);
                await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_mapping_intents(operation_ref,tenant_id,issuer,subject,client_kind,client_binding,document) VALUES({reference},{original.TenantId},{issuerBytes},{subjectBytes},{kind},{binding},{document})");
                attempted = true; await tx.CommitAsync();
            }
            catch (Exception error)
            {
                try { await tx.RollbackAsync(); } catch { throw new SsoMappingRecoveryRequiredV1(reference, error); }
                if (attempted) throw new SsoMappingRecoveryRequiredV1(reference, error);
                throw;
            }
        }

        // The intent is an immutable recovery marker, not a resumable code/token or an active identity.
        // All authority is checked again after reacquiring the same root used by actual writers.
        Exception? failure = null; var uncertain = false;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var attempted = false;
            try
            {
                await AuthenticationCompletionGateV1.AcquireAsync(_db);
                await RequireMappingAuthority(original, exchangedConfig, reference, kind, binding, deadline);
                var current = await PrepareMapping(original.TenantId, exchangedConfig, issuer, subject, email, verifiedEmail, intent.UserId);
                if (current != intent) throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
                Sys_User user;
                if (intent.Kind == "LINK")
                {
                    user = await _db.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.TenantId == original.TenantId && x.Id == intent.UserId);
                    user.ExternalProvider = issuer; user.ExternalSubject = subject;
                    // Federated fields are not in UserIdentityData. Advance the existing authentication
                    // epoch so Main captures a new identity version and invalidates prior authentication.
                    user.AuthenticationEpoch = Guid.NewGuid();
                }
                else
                {
                    user = new Sys_User { Id = intent.UserId, TenantId = original.TenantId, UserName = email, Email = email,
                        ExternalProvider = issuer, ExternalSubject = subject, RoleId = intent.RoleId,
                        Enable = true, IsPlatformAdmin = false, MustChangePassword = false, AuthenticationEpoch = Guid.NewGuid(),
                        Password = _hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(64))) };
                    _db.Sys_Users.Add(user);
                }
                await _db.SaveChangesAsync(); // Main identity version + Outbox use this same PostgreSQL transaction.
                var aggregate = "user:" + user.Id.ToString("D");
                var version = await _db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == original.TenantId && x.AggregateId == aggregate).Select(x => x.Version).SingleAsync();
                var outcome = intent.Kind == "LINK" ? "IDENTITY_BOUND" : "USER_PROVISIONED";
                _db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = original.TenantId, UserId = user.Id,
                    EventType = (int)(intent.Kind == "LINK" ? SecurityEventType.SsoIdentityLinked : SecurityEventType.SsoUserProvisioned),
                    CreatedAt = DateTime.Now, Reason = "Approved first SSO identity mapping",
                    ContextJson = JsonSerializer.Serialize(new { operationRef = reference, state = outcome, identityVersion = version,
                        admissionPolicyRef = intent.PolicyRef, admissionPolicyVersion = intent.PolicyVersion }) });
                await _db.SaveChangesAsync();
                // Clock-based tenant eligibility can expire while required writes are in progress.
                await RequireMappingAuthority(original, exchangedConfig, reference, kind, binding, deadline);
                await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_mapping_receipts(operation_ref,tenant_id,state,user_id,identity_version) VALUES({reference},{original.TenantId},{outcome},{user.Id},{version})");
                if (DateTimeOffset.UtcNow >= deadline) throw new InvalidOperationException("REAUTH_REQUIRED");
                var fresh = await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == user.Id && x.TenantId == original.TenantId);
                attempted = true; await tx.CommitAsync(); return fresh;
            }
            catch (Exception error)
            {
                failure = error; uncertain = attempted;
                try { await tx.RollbackAsync(); } catch { uncertain = true; }
                _db.ChangeTracker.Clear();
            }
        }
        if (uncertain) throw new SsoMappingRecoveryRequiredV1(reference, failure);
        // A proven rollback can be fenced. A failure to persist that fence remains UNKNOWN.
        try
        {
            await using var closure = await _db.Database.BeginTransactionAsync();
            await AuthenticationCompletionGateV1.AcquireAsync(_db);
            await new SsoMappingRecoveryV1(_db).ResolveOwnedAsync(reference, original.TenantId, true, null, default);
            await closure.CommitAsync();
        }
        catch (Exception error) { throw new SsoMappingRecoveryRequiredV1(reference, error); }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure!).Throw();
        throw new InvalidOperationException("SSO_IDENTITY_MAPPING_FAILED");
    }

    private async Task RequireMappingAuthority(AuthenticationPolicyV1 original, Sys_TenantSsoConfig exchangedConfig,
        string reference, string kind, string binding, DateTimeOffset deadline)
    {
        if (_policies == null || DateTimeOffset.UtcNow >= deadline || (await _policies.ReadAsync(original.TenantId)).Version != original.Version)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        var state = await new SsoMappingRecoveryV1(_db).StateAsync(reference, default);
        if (state == null || !state.Consumed || state.Kind != kind || state.Binding != binding
            || JsonSerializer.Deserialize<AuthenticationPolicyV1>(state.Policy) != original)
            throw new InvalidOperationException("E-SEC-022");
        var receipt = await new SsoMappingRecoveryV1(_db).ResolveOwnedAsync(reference, original.TenantId, false, null, default);
        if (receipt.State != "UNKNOWN") throw new InvalidOperationException("SSO_MAPPING_CLOSED");
        if (kind == "Web" && await _db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_authentication_v1.browser_closures WHERE challenge_hash={binding}) AS \"Value\"").SingleAsync())
            throw new InvalidOperationException("AUTH_BROWSER_CLOSED");
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == original.TenantId);
        if (tenant == null || !tenant.Enable || tenant.ExpireDate is { } expires && expires <= DateTime.Now)
            throw new InvalidOperationException("E-SEC-027");
        var config = await _db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TenantId == original.TenantId);
        if (!config.Enabled || config.Id != exchangedConfig.Id || config.Authority != exchangedConfig.Authority
            || config.ClientId != exchangedConfig.ClientId || config.EmailClaim != exchangedConfig.EmailClaim)
            throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    private async Task<MappingIntent> PrepareMapping(Guid tenant, Sys_TenantSsoConfig cfg, string issuer, string subject,
        string email, bool verifiedEmail, Guid? allocatedUser = null)
    {
        var policy = await SsoConfigurationAdminV1.ReadAdmissionAsync(_db, tenant);
        if (policy == null || !policy.IssuerIsEmailAuthority || policy.EmailClaimSemantic != "verified-email"
            || policy.Authority != cfg.Authority || policy.ClientId != cfg.ClientId || policy.EmailClaim != cfg.EmailClaim
            || policy.ApprovedIssuer != issuer || !verifiedEmail)
            throw new InvalidOperationException("SSO_IDENTITY_POLICY_UNAVAILABLE");
        // Existing Main storage contract; do not truncate or normalize protocol identifiers.
        if (issuer.Length > 300 || subject.Length > 200 || email.Length > 100)
            throw new InvalidOperationException("SSO_IDENTITY_STORAGE_BOUNDARY");
        if (await SsoIdentityQueriesV1.ExactAsync(_db, tenant, issuer, subject) != null)
            throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
        // Main's PostgreSQL email domain is case-insensitive. Multiple matches are a conflict,
        // including disabled users; no arbitrary row, cross-tenant fallback or local-password login.
        var matches = await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.Email == email).Take(2).ToArrayAsync();
        if (matches.Length > 1) throw new InvalidOperationException("SSO_IDENTITY_CONFLICT");
        if (matches.Length == 1)
        {
            var user = matches[0]; RequireActive(user);
            if (!policy.AllowInitialEmailLink || !string.IsNullOrEmpty(user.ExternalSubject)
                || !string.IsNullOrEmpty(user.ExternalProvider) && user.ExternalProvider != issuer)
                throw new InvalidOperationException("SSO_IDENTITY_CONFLICT");
            return new("LINK", user.Id, user.RoleId, await Revision(tenant, "user:" + user.Id.ToString("D")), null, policy.PolicyRef, policy.Version);
        }
        if (!cfg.AutoProvision || !policy.AllowJit || cfg.DefaultRoleId is not int role || !policy.ApprovedJitRoleIds.Contains(role)
            || !await _db.Sys_Roles.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.RoleId == role && x.Enable))
            throw new InvalidOperationException("SSO_JIT_APPROVAL_REQUIRED");
        if (await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.UserName == email))
            throw new InvalidOperationException("SSO_IDENTITY_CONFLICT");
        return new("JIT", allocatedUser ?? Guid.NewGuid(), role, null, await Revision(tenant, "role:" + role), policy.PolicyRef, policy.Version);
    }
    private Task<Guid> Revision(Guid tenant, string key) => _db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={tenant} AND object_key={key}").SingleAsync();
    private static void RequireActive(Sys_User user)
    {
        if (!user.Enable || user.LockedUntil is { } locked && locked > DateTime.Now) throw new InvalidOperationException("E-SEC-027");
    }
    private sealed record MappingIntent(string Kind, Guid UserId, int? RoleId, Guid? UserRevision, Guid? RoleRevision, Guid PolicyRef, Guid PolicyVersion);
}
