using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record AuthenticationDeviceEvidenceV1(string ClientKind, string DeviceId, string? AppVersion,
    string? PlatformVersion, string RegistrationVersion, string PolicyVersion, Guid? SharedWindow = null);

/// <summary>Native registration and shared-window evidence, coordinated with the actual PostgreSQL device writers.</summary>
public sealed class AuthenticationDeviceAuthorityV1(CP6Context db)
{
    public async Task<AuthenticationDeviceEvidenceV1> CaptureAsync(Guid tenant, ClientContextDto client, CancellationToken ct)
    {
        var device = await DeviceAsync(tenant, client.DeviceId, ct);
        var required = await db.WmsFeatureFlags.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted && x.ProductionMoveEnabled, ct);
        if (device != null && (device.IsDeleted || device.Status != ClientDeviceStatus.Active || device.Platform != client.ClientKind)
            || device == null && required) throw new InvalidOperationException("AUTHENTICATION_DENIED");
        var revision = await db.Database.SqlQuery<string>($"SELECT revision::text AS \"Value\" FROM cp6_authentication_v1.device_versions WHERE tenant_id={tenant} AND device_id={client.DeviceId}").SingleOrDefaultAsync(ct);
        if (device != null && revision == null) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        var policy = await db.Database.SqlQuery<string>($"SELECT revision::text AS \"Value\" FROM cp6_authentication_v1.device_policy_versions WHERE tenant_id={tenant}").SingleOrDefaultAsync(ct);
        // No activation policy is the existing Owner's explicit optional-feature behavior, not a missing MFA policy.
        return new(client.ClientKind, client.DeviceId, client.AppVersion, client.PlatformVersion, revision ?? "unregistered", policy ?? "unconfigured");
    }

    public async Task ValidateAsync(SessionAuthenticationEvidenceV1 evidence, CancellationToken ct)
    {
        if (evidence.Primary.ClientKind == "Web")
        { if (evidence.Device != null) throw new InvalidOperationException("REAUTH_REQUIRED"); return; }
        var old = evidence.Device ?? throw new InvalidOperationException("REAUTH_REQUIRED");
        if (old.ClientKind != evidence.Primary.ClientKind || string.IsNullOrWhiteSpace(old.DeviceId)) throw new InvalidOperationException("REAUTH_REQUIRED");
        var current = await CaptureAsync(evidence.Primary.TenantId, new ClientContextDto { ClientKind = old.ClientKind, DeviceId = old.DeviceId,
            AppVersion = old.AppVersion ?? "", PlatformVersion = old.PlatformVersion }, ct);
        if (current.RegistrationVersion != old.RegistrationVersion || current.PolicyVersion != old.PolicyVersion)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (evidence.Primary.PrimaryMethod == AuthenticationPrimaryMethod.QuickSwitchPin)
        {
            var window = await RequireWindowAsync(evidence.Primary.TenantId, old, evidence.Primary.PolicyVersion, ct);
            if (old.SharedWindow != window) throw new InvalidOperationException("REAUTH_REQUIRED");
        }
    }

    public async Task<Guid> RequireWindowAsync(Guid tenant, AuthenticationDeviceEvidenceV1 device, string policy, CancellationToken ct)
    {
        var current = await DeviceAsync(tenant, device.DeviceId, ct);
        var window = await db.Database.SqlQuery<WindowRow>($"SELECT window_id AS \"Id\", device_revision AS \"Revision\", policy_version AS \"Policy\", expires_at AS \"Expires\" FROM cp6_authentication_v1.shared_windows w JOIN cp6_authentication_v1.interactions i ON i.completion_id=w.completion_id WHERE w.tenant_id={tenant} AND w.device_id={device.DeviceId} AND w.invalidated_at IS NULL AND i.state='completed' AND EXISTS (SELECT 1 FROM cp6_authentication_v1.session_evidence s JOIN public.\"Sys_RefreshTokens\" r ON r.\"TokenHash\"=s.refresh_hash WHERE s.completion_id=w.completion_id AND s.parent_refresh_hash IS NULL AND s.rejected_at IS NULL AND NOT r.\"CompletionPending\" AND r.\"CompletionRejectedAtUtc\" IS NULL)").SingleOrDefaultAsync(ct);
        if (current == null || current.IsDeleted || current.Status != ClientDeviceStatus.Active || current.DeviceMode != ClientDeviceMode.Shared
            || current.Platform != device.ClientKind || current.QuickSwitchFailureCount >= 5 || window == null
            || window.Revision != device.RegistrationVersion || window.Policy != policy || window.Expires <= DateTimeOffset.UtcNow
            || current.FullAuthExpiresAt == null || new DateTimeOffset(DateTime.SpecifyKind(current.FullAuthExpiresAt.Value, DateTimeKind.Utc)) != window.Expires)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        return window.Id;
    }

    public async Task RecordWindowAsync(AuthenticationProofV1 proof, AuthenticationDeviceEvidenceV1 device, CancellationToken ct)
    {
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        if (proof.PrimaryMethod == AuthenticationPrimaryMethod.QuickSwitchPin) throw new InvalidOperationException("REAUTH_REQUIRED");
        var current = await DeviceAsync(proof.TenantId, device.DeviceId, ct);
        if (current == null || current.DeviceMode != ClientDeviceMode.Shared) return;
        if (current.IsDeleted || current.Status != ClientDeviceStatus.Active || current.FullAuthExpiresAt == null)
            throw new InvalidOperationException("AUTHENTICATION_DENIED");
        var expires = new DateTimeOffset(DateTime.SpecifyKind(current.FullAuthExpiresAt.Value, DateTimeKind.Utc));
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.shared_windows(tenant_id,device_id,window_id,completion_id,device_revision,policy_version,expires_at) VALUES ({proof.TenantId},{device.DeviceId},{id},{proof.CompletionRef},{device.RegistrationVersion},{proof.PolicyVersion},{expires}) ON CONFLICT(tenant_id,device_id) DO UPDATE SET window_id=EXCLUDED.window_id,completion_id=EXCLUDED.completion_id,device_revision=EXCLUDED.device_revision,policy_version=EXCLUDED.policy_version,expires_at=EXCLUDED.expires_at,invalidated_at=NULL", ct);
    }

    private Task<ClientDevice?> DeviceAsync(Guid tenant, string device, CancellationToken ct)
        => db.ClientDevices.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.DeviceId == device, ct);
    public sealed class WindowRow
    {
        public Guid Id { get; set; }
        public string Revision { get; set; } = "";
        public string Policy { get; set; } = "";
        public DateTimeOffset Expires { get; set; }
    }
}
