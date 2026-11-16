using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
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
        var input = new MappingInput(original, exchangedConfig, issuer, subject, email, verifiedEmail, reference, kind, binding,
            verifiedAt.AddMinutes(original.InteractionMinutes));
        MappingIntent? intent = null;
        SsoCrmLink? crmLink = null;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var attempted = false;
            try
            {
                await AuthenticationCompletionGateV1.AcquireAsync(_db);
                await RequireMappingAuthority(input);
                var issuerBytes = Encoding.UTF8.GetBytes(issuer); var subjectBytes = Encoding.UTF8.GetBytes(subject);
                var browserOriginal = await ReadBrowserOriginalMappingAsync(input, issuerBytes, subjectBytes);
                var pending = await _db.Database.SqlQuery<string>($"SELECT i.operation_ref AS \"Value\" FROM cp6_authentication_v1.sso_mapping_intents i WHERE i.tenant_id={original.TenantId} AND i.issuer={issuerBytes} AND i.subject={subjectBytes} AND NOT EXISTS(SELECT FROM cp6_authentication_v1.sso_mapping_receipts r WHERE r.operation_ref=i.operation_ref) ORDER BY i.created_at LIMIT 1").SingleOrDefaultAsync();
                if (pending != null)
                {
                    crmLink = await ContinueSsoCrmLinkAsync(input, pending);
                    await new SsoMappingRecoveryV1(_db).BindContinuationAsync(reference, pending, default);
                }
                else
                {
                    var exact = await SsoIdentityQueriesV1.ExactAsync(_db, original.TenantId, issuer, subject);
                    if (exact != null)
                    {
                        RequireActive(exact);
                        if (browserOriginal is not null)
                        {
                            await new SsoMappingRecoveryV1(_db).BindContinuationAsync(reference, browserOriginal, default);
                            attempted = true;
                        }
                        await tx.CommitAsync();
                        return exact;
                    }
                    if (browserOriginal is not null) throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
                    intent = await PrepareMapping(input);
                    _db.RequireIdentityCapture(original.TenantId);
                    var document = JsonSerializer.Serialize(intent);
                    await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_mapping_intents(operation_ref,tenant_id,issuer,subject,client_kind,client_binding,document) VALUES({reference},{original.TenantId},{issuerBytes},{subjectBytes},{kind},{binding},{document})");
                    if (intent.Kind == "LINK" && _db.InquiryFence?.RegisteredStore(original.TenantId) is { } store)
                        crmLink = await PrepareSsoCrmLinkAsync(input, intent, store, document, tx);
                }
                attempted = true; await tx.CommitAsync();
            }
            catch (Exception error)
            {
                var recoveryRef = crmLink?.Input.Reference ?? reference;
                try { await tx.RollbackAsync(); } catch { throw new SsoMappingRecoveryRequiredV1(recoveryRef, error); }
                if (attempted) throw new SsoMappingRecoveryRequiredV1(recoveryRef, error);
                throw;
            }
        }
        if (crmLink is not null) return await ExecuteSsoCrmLinkAsync(crmLink);
        return await CompleteLocalMappingAsync(input, intent!);
    }

    private async Task<Sys_User> CompleteLocalMappingAsync(MappingInput input, MappingIntent intent)
    {
        // The intent is an immutable recovery marker, not a resumable code/token or an active identity.
        // All authority is checked again after reacquiring the same root used by actual writers.
        Exception? failure = null; var uncertain = false;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var attempted = false;
            try
            {
                await AuthenticationCompletionGateV1.AcquireAsync(_db);
                await RequireMappingAuthority(input);
                var current = await PrepareMapping(input, intent.UserId);
                if (current != intent) throw new InvalidOperationException("SSO_IDENTITY_CHANGED");
                var user = await ApplyMappingAsync(input, intent, Guid.NewGuid());
                var fresh = await RecordMappingAsync(input, intent, user);
                attempted = true; await tx.CommitAsync(); return fresh;
            }
            catch (Exception error)
            {
                failure = error; uncertain = attempted;
                try { await tx.RollbackAsync(); } catch { uncertain = true; }
                _db.ChangeTracker.Clear();
            }
        }
        if (uncertain) throw new SsoMappingRecoveryRequiredV1(input.Reference, failure);
        // A proven rollback can be fenced. A failure to persist that fence remains UNKNOWN.
        try
        {
            await using var closure = await _db.Database.BeginTransactionAsync();
            await AuthenticationCompletionGateV1.AcquireAsync(_db);
            await new SsoMappingRecoveryV1(_db).ResolveOwnedAsync(input.Reference, input.Policy.TenantId, true, null, default);
            await closure.CommitAsync();
        }
        catch (Exception error) { throw new SsoMappingRecoveryRequiredV1(input.Reference, error); }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure!).Throw();
        throw new InvalidOperationException("SSO_IDENTITY_MAPPING_FAILED");
    }

    private async Task<Sys_User> ApplyMappingAsync(MappingInput input, MappingIntent intent, Guid epoch)
    {
        var tenant = input.Policy.TenantId;
        Sys_User user;
        if (intent.Kind == "LINK")
        {
            user = await _db.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant && x.Id == intent.UserId);
            user.ExternalProvider = input.Issuer;
            user.ExternalSubject = input.Subject;
            // Advance the existing epoch so C02 captures the new authority and invalidates prior authentication.
            user.AuthenticationEpoch = epoch;
        }
        else
        {
            user = new Sys_User { Id = intent.UserId, TenantId = tenant, UserName = input.Email, Email = input.Email,
                ExternalProvider = input.Issuer, ExternalSubject = input.Subject, RoleId = intent.RoleId,
                Enable = true, IsPlatformAdmin = false, MustChangePassword = false, AuthenticationEpoch = epoch,
                Password = _hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(64))) };
            _db.Sys_Users.Add(user);
        }
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Sys_User> RecordMappingAsync(MappingInput input, MappingIntent intent, Sys_User user)
    {
        var tenant = input.Policy.TenantId;
        var aggregate = "user:" + user.Id.ToString("D");
        var version = await _db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).Select(x => x.Version).SingleAsync();
        var outcome = intent.Kind == "LINK" ? "IDENTITY_BOUND" : "USER_PROVISIONED";
        _db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = tenant, UserId = user.Id,
            EventType = (int)(intent.Kind == "LINK" ? SecurityEventType.SsoIdentityLinked : SecurityEventType.SsoUserProvisioned),
            CreatedAt = DateTime.Now, Reason = "Approved first SSO identity mapping",
            ContextJson = JsonSerializer.Serialize(new { operationRef = input.Reference, state = outcome, identityVersion = version,
                admissionPolicyRef = intent.PolicyRef, admissionPolicyVersion = intent.PolicyVersion }) });
        await _db.SaveChangesAsync();
        await RequireMappingAuthority(input);
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_mapping_receipts(operation_ref,tenant_id,state,user_id,identity_version) VALUES({input.Reference},{tenant},{outcome},{user.Id},{version})");
        var fresh = await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == user.Id && x.TenantId == tenant);
        RequireMappingDeadline(input.Deadline);
        return fresh;
    }

    private async Task RequireMappingAuthority(MappingInput input)
    {
        var (original, exchangedConfig, _, _, _, _, reference, kind, binding, deadline) = input;
        RequireMappingDeadline(deadline);
        if (_policies == null || (await _policies.ReadAsync(original.TenantId)).Version != original.Version)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        var state = await new SsoMappingRecoveryV1(_db).StateAsync(reference, default);
        if (state == null || !state.Consumed || state.Kind != kind || state.Binding != binding
            || JsonSerializer.Deserialize<AuthenticationPolicyV1>(state.Policy) != original)
            throw new InvalidOperationException("E-SEC-022");
        var receipt = await new SsoMappingRecoveryV1(_db).ResolveOwnedAsync(reference, original.TenantId, false, null, default);
        if (receipt.State != "UNKNOWN") throw new InvalidOperationException("SSO_MAPPING_CLOSED");
        if (kind == "Web" && await _db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_authentication_v1.browser_closures WHERE challenge_hash={binding}) AS \"Value\"").SingleAsync())
            throw new InvalidOperationException("AUTH_BROWSER_CLOSED");
        await RequireMappingTenant(original.TenantId, deadline);
        var config = await _db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TenantId == original.TenantId);
        if (!config.Enabled || config.Id != exchangedConfig.Id || config.Authority != exchangedConfig.Authority
            || config.ClientId != exchangedConfig.ClientId || config.EmailClaim != exchangedConfig.EmailClaim)
            throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    private async Task RequireMappingTenant(Guid tenantId, DateTimeOffset deadline)
    {
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId);
        if (tenant == null || !tenant.Enable || tenant.ExpireDate is { } expires && expires <= DateTime.Now)
            throw new InvalidOperationException("E-SEC-027");
        RequireMappingDeadline(deadline);
    }

    private static void RequireMappingDeadline(DateTimeOffset deadline)
    {
        if (DateTimeOffset.UtcNow >= deadline) throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    private async Task<MappingIntent> PrepareMapping(MappingInput input, Guid? allocatedUser = null)
    {
        var (original, cfg, issuer, subject, email, verifiedEmail, _, _, _, _) = input;
        var tenant = original.TenantId;
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
        return new("JIT", allocatedUser ?? Guid.NewGuid(), role, null, await Revision(tenant, IdentityEventContracts.RoleSubject(role)), policy.PolicyRef, policy.Version);
    }
    private Task<Guid> Revision(Guid tenant, string key) => _db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={tenant} AND object_key={key}").SingleAsync();
    private static void RequireActive(Sys_User user)
    {
        if (!user.Enable || user.LockedUntil is { } locked && locked > DateTime.Now) throw new InvalidOperationException("E-SEC-027");
    }
    private sealed record MappingInput(AuthenticationPolicyV1 Policy, Sys_TenantSsoConfig Config, string Issuer,
        string Subject, string Email, bool VerifiedEmail, string Reference, string Kind, string Binding, DateTimeOffset Deadline);
    private sealed record MappingIntent(string Kind, Guid UserId, int? RoleId, Guid? UserRevision, Guid? RoleRevision, Guid PolicyRef, Guid PolicyVersion);
}
