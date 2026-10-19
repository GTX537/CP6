using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class SsoConfigurationExceptionV1(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
}

public sealed record SsoConfigOperationV1(Guid OperationId, string State, string? ConfigVersion = null, string? PolicyVersion = null);

/// <summary>Current PostgreSQL tenant authority, configuration CAS and immutable original-operation recovery.</summary>
public sealed class SsoConfigurationAdminV1(CP6Context db, AuthenticationPolicyServiceV1 policies,
    ICurrentPermissionContext permissions, IDataProtectionProvider protection)
{
    private readonly IDataProtector secret = protection.CreateProtector("CP6.Sso.ClientSecret");
    private readonly IDataProtector request = protection.CreateProtector("CP6.Sso.ConfigurationRequest.V1");
    public bool IsPostgreSql => db.Database.IsNpgsql();

    public async Task<object> ReadAsync(Func<CancellationToken,Task<Guid>> currentActor, CancellationToken ct)
        => await ExecuteAsync(currentActor, "query", async (actor, token) =>
        {
            var cfg = await Configuration(token); var policy = await ReadAdmissionAsync(db, db.CurrentTenantId, token);
            return (object)new { tenantId = db.CurrentTenantId, actorId = actor, authority = cfg?.Authority ?? "",
                clientId = cfg?.ClientId ?? "", scopes = cfg?.Scopes ?? "openid email profile", emailClaim = cfg?.EmailClaim ?? "email",
                enabled = cfg?.Enabled ?? false, enforced = cfg?.Enforced ?? false, autoProvision = cfg?.AutoProvision ?? false,
                defaultRoleId = cfg?.DefaultRoleId, hasClientSecret = !string.IsNullOrEmpty(cfg?.ClientSecretProtected),
                configVersion = await ConfigVersion(token), policyVersion = policy?.Version.ToString("D") ?? "none", admissionPolicy = policy };
        }, ct);

    public async Task<SsoConfigOperationV1> ChangeAsync(SsoConfigInput input, Func<CancellationToken,Task<Guid>> currentActor, CancellationToken ct)
    {
        ValidateInput(input);
        return await ExecuteAsync(currentActor, "edit", async (actor, token) =>
        {
            var fingerprint = AuthenticationPolicyServiceV1.Hash(input);
            var original = await Operation(input.OperationId, token);
            if (original != null)
            {
                RequireOwner(original, actor);
                if (original.State == "CLOSED") throw Error(409, "SSO_OPERATION_CLOSED");
                if (original.RequestProtected == null || request.Unprotect(original.RequestProtected) != fingerprint)
                    throw Error(409, "SSO_OPERATION_INPUT_CONFLICT");
                return JsonSerializer.Deserialize<SsoConfigOperationV1>(original.ResultJson)!;
            }
            var cfg = await Configuration(token);
            var previous = await ReadAdmissionAsync(db, db.CurrentTenantId, token);
            if (input.ExpectedConfigVersion != await ConfigVersion(token)
                || input.ExpectedPolicyVersion != (previous?.Version.ToString("D") ?? "none") || input.ExpectedPolicyRef != previous?.PolicyRef)
                throw Error(412, "SSO_CONFIGURATION_CHANGED");
            var admission = await ResolvePolicy(input, previous, actor, token);
            // Existing legacy AutoProvision=true does not itself approve first identity creation.
            if (input.DefaultRoleId != null && cfg?.DefaultRoleId != input.DefaultRoleId
                || input.AutoProvision && cfg?.AutoProvision != true)
                await RequireJitRole(input, admission, token);
            if (input.Admission!.Action == "approve" && input.AutoProvision) await RequireJitRole(input, admission, token);

            var entity = await db.Sys_TenantSsoConfigs.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == db.CurrentTenantId, token);
            if (entity == null) { entity = new Sys_TenantSsoConfig { TenantId = db.CurrentTenantId }; db.Sys_TenantSsoConfigs.Add(entity); }
            entity.Authority = input.Authority; entity.ClientId = input.ClientId; entity.Scopes = input.Scopes;
            entity.EmailClaim = input.EmailClaim; entity.Enabled = input.Enabled; entity.Enforced = input.Enforced;
            entity.AutoProvision = input.AutoProvision; entity.DefaultRoleId = input.DefaultRoleId;
            if (!string.IsNullOrEmpty(input.ClientSecret)) entity.ClientSecretProtected = secret.Protect(input.ClientSecret);
            if (input.Admission.Action != "preserve")
            {
                if (admission == null)
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_authentication_v1.sso_admission_policies WHERE tenant_id={db.CurrentTenantId}", token);
                else
                {
                    var document = JsonSerializer.Serialize(admission);
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_admission_policies(tenant_id,document) VALUES({db.CurrentTenantId},{document}) ON CONFLICT(tenant_id) DO UPDATE SET document=EXCLUDED.document", token);
                }
            }
            Audit(actor, input.OperationId, input.Reason!, input.Admission.Action, previous?.Version, admission?.Version);
            await db.SaveChangesAsync(token);
            var result = new SsoConfigOperationV1(input.OperationId, "COMMITTED", await ConfigVersion(token), admission?.Version.ToString("D") ?? "none");
            await InsertOperation(actor, result, request.Protect(fingerprint), token);
            return result;
        }, ct);
    }

    public Task<SsoConfigOperationV1> ResolveAsync(Guid operationId, bool close,
        Func<CancellationToken,Task<Guid>> currentActor, CancellationToken ct)
    {
        if (operationId == Guid.Empty) throw Error(400, "SSO_OPERATION_REQUIRED");
        return ExecuteAsync(currentActor, "edit", async (actor, token) =>
        {
            var original = await Operation(operationId, token);
            if (original != null) { RequireOwner(original, actor); return JsonSerializer.Deserialize<SsoConfigOperationV1>(original.ResultJson)!; }
            var result = new SsoConfigOperationV1(operationId, close ? "CLOSED" : "UNKNOWN");
            if (close)
            {
                Audit(actor, operationId, "Closed original uncertain configuration operation", "close", null, null);
                await db.SaveChangesAsync(token); await InsertOperation(actor, result, null, token);
            }
            return result;
        }, ct);
    }

    public static async Task<SsoIdentityAdmissionPolicyV1?> ReadAdmissionAsync(CP6Context db, Guid tenantId, CancellationToken ct = default)
    {
        var document = await db.Database.SqlQuery<string>($"SELECT document AS \"Value\" FROM cp6_authentication_v1.sso_admission_policies WHERE tenant_id={tenantId}").SingleOrDefaultAsync(ct);
        return document == null ? null : JsonSerializer.Deserialize<SsoIdentityAdmissionPolicyV1>(document)
            ?? throw new InvalidOperationException("SSO_IDENTITY_POLICY_UNAVAILABLE");
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<Guid>> currentActor, string action,
        Func<Guid,CancellationToken,Task<T>> body, CancellationToken ct)
    {
        if (!IsPostgreSql || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()) throw Error(503, "SSO_CONFIGURATION_UNAVAILABLE");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var attempted = false;
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var actor = await currentActor(ct);
            var current = await permissions.GetAsync();
            if (actor == Guid.Empty || actor != current.UserId || !current.ActionKeys.Contains("sys-sso-config:" + action)) throw Error(403, "SSO_CONFIGURATION_FORBIDDEN");
            var result = await body(actor, ct); ct.ThrowIfCancellationRequested(); attempted = true;
            await tx.CommitAsync(ct); return result;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { throw Error(503, "SSO_OPERATION_UNKNOWN"); }
            finally { db.ChangeTracker.Clear(); }
            if (attempted) throw Error(503, "SSO_OPERATION_UNKNOWN");
            throw;
        }
    }

    private Task<Sys_TenantSsoConfig?> Configuration(CancellationToken ct) => db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == db.CurrentTenantId, ct);
    private async Task<string> ConfigVersion(CancellationToken ct) { var p = await policies.ReadAsync(db.CurrentTenantId, ct); return p.SsoConfigurationVersion ?? "absent:" + p.Version; }
    private async Task<SsoIdentityAdmissionPolicyV1?> ResolvePolicy(SsoConfigInput input, SsoIdentityAdmissionPolicyV1? previous, Guid actor, CancellationToken ct)
    {
        var change = input.Admission!;
        if (change.Action == "revoke") return null;
        if (change.Action == "preserve")
        {
            if (previous != null && (previous.Authority != input.Authority || previous.ClientId != input.ClientId || previous.EmailClaim != input.EmailClaim))
                throw Error(409, "SSO_ADMISSION_REAPPROVAL_REQUIRED");
            return previous;
        }
        if (!change.IssuerIsEmailAuthority || string.IsNullOrWhiteSpace(change.ApprovedIssuer)
            || change.ApprovedIssuer.Length > 500 || !Uri.TryCreate(change.ApprovedIssuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme is not ("http" or "https") || change.EmailClaimSemantic != "verified-email")
            throw Error(400, "SSO_ADMISSION_EXPLICIT_APPROVAL_REQUIRED");
        var roles = (change.ApprovedJitRoleIds ?? []).Distinct().Order().ToArray();
        if (roles.Length > 100 || change.AllowJit && roles.Length == 0 || roles.Any(x => x <= 0)) throw Error(400, "SSO_ADMISSION_ROLES_INVALID");
        if (await db.Sys_Roles.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.TenantId == db.CurrentTenantId && x.Enable && roles.Contains(x.RoleId), ct) != roles.Length)
            throw Error(409, "SSO_ADMISSION_ROLE_CHANGED");
        return new(previous?.PolicyRef ?? Guid.NewGuid(), Guid.NewGuid(), input.Authority, change.ApprovedIssuer, input.ClientId,
            input.EmailClaim, change.EmailClaimSemantic, true, change.AllowInitialEmailLink, change.AllowJit, roles, actor, DateTimeOffset.UtcNow);
    }
    private async Task RequireJitRole(SsoConfigInput input, SsoIdentityAdmissionPolicyV1? policy, CancellationToken ct)
    {
        if (policy is not { AllowJit: true } || input.DefaultRoleId is not int role || !policy.ApprovedJitRoleIds.Contains(role)
            || !await db.Sys_Roles.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == db.CurrentTenantId && x.RoleId == role && x.Enable, ct))
            throw Error(409, "SSO_JIT_APPROVAL_REQUIRED");
    }
    private void Audit(Guid actor, Guid operationId, string reason, string action, Guid? before, Guid? after)
        => db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = db.CurrentTenantId, UserId = actor,
            EventType = (int)SecurityEventType.SsoConfigChanged, CreatedAt = DateTime.Now, Reason = reason,
            ContextJson = JsonSerializer.Serialize(new { operationId, admissionAction = action, previousPolicyVersion = before, policyVersion = after }) });
    private Task<OperationRow?> Operation(Guid id, CancellationToken ct) => db.Database.SqlQuery<OperationRow>($"SELECT actor_id AS \"ActorId\", state AS \"State\", request_protected AS \"RequestProtected\", result_json AS \"ResultJson\" FROM cp6_authentication_v1.sso_config_operations WHERE tenant_id={db.CurrentTenantId} AND operation_id={id}").SingleOrDefaultAsync(ct);
    private Task<int> InsertOperation(Guid actor, SsoConfigOperationV1 result, string? fingerprint, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result);
        return db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_config_operations(tenant_id,operation_id,actor_id,state,request_protected,result_json) VALUES({db.CurrentTenantId},{result.OperationId},{actor},{result.State},{fingerprint},{json})", ct);
    }
    private static void RequireOwner(OperationRow row, Guid actor) { if (row.ActorId != actor) throw Error(403, "SSO_OPERATION_OWNER_MISMATCH"); }
    private static void ValidateInput(SsoConfigInput input)
    {
        if (input.OperationId == Guid.Empty || string.IsNullOrEmpty(input.ExpectedConfigVersion) || string.IsNullOrEmpty(input.ExpectedPolicyVersion)
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 256 || input.Admission?.Action is not ("preserve" or "approve" or "revoke"))
            throw Error(400, "SSO_CONFIGURATION_ENVELOPE_REQUIRED");
        if (string.IsNullOrWhiteSpace(input.Authority) || input.Authority.Length > 500 || string.IsNullOrWhiteSpace(input.ClientId) || input.ClientId.Length > 200
            || string.IsNullOrWhiteSpace(input.Scopes) || input.Scopes.Length > 500 || string.IsNullOrWhiteSpace(input.EmailClaim) || input.EmailClaim.Length > 100)
            throw Error(400, "SSO_CONFIGURATION_INVALID");
    }
    private static SsoConfigurationExceptionV1 Error(int status, string code) => new(status, code);
    public sealed class OperationRow { public Guid ActorId { get; set; } public string State { get; set; } = ""; public string? RequestProtected { get; set; } public string ResultJson { get; set; } = ""; }
}
