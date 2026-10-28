using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Wms.Transfer;

internal sealed class TransferEnvironmentStoreV1(CP6Context db, ScopeAuthorizationFenceV1 scope,
    IOptions<TransferEnvironmentOptionsV1> options, QualitySpecificationRuntimeV1 runtime)
{
    internal const string Owner = "Main.Wms.TransferEnvironment.v1", Contract = "Main.Wms.TransferEnvironment.commands.v1";
    internal const string Profile = "MAIN_NATIVE_TRANSFER_SOURCES_V1";
    internal static readonly string[] Owners = ["Main.Erp.ProductIdentity.v1", "Main.Erp.BusinessPartnerIdentity.v1",
        "Main.Erp.UomProfile.v1", "Main.Erp.ProductUnit.v1", "Main.Plm.EngineeringProduct.v1",
        "Main.Plm.EngineeringMapping.v1", "Main.Space.PhysicalSiteIdentity.v1", "Main.Plm.EngineeringBaseline.v1", "Main.Wms.InventoryIdentity.v1"];
    internal static OrderPriceCorrectionException Error(string code, int status = 503) => new(code, status);
    internal TransferDeploymentV1 Deployment()
    {
        var mode = options.Value.Mode; var sandbox = options.Value.SandboxScopeId;
        if (mode is not ("PRODUCTION" or "SIMULATION") || (mode == "PRODUCTION" ? sandbox != null : sandbox is null || sandbox == Guid.Empty)
            || runtime.EvidenceMode != (mode == "PRODUCTION" ? "REAL" : "DEMO")) throw Error("TRANSFER_DEPLOYMENT_UNAVAILABLE");
        return new(scope.EnvironmentId, mode, sandbox);
    }
    internal async Task<TransferDeploymentV1> Fence(OperationsAccessV1 a, bool write, CancellationToken ct)
    {
        if (!scope.Owns(db)) throw Error("TRANSFER_SCOPE_CONTEXT_MISMATCH");
        var configured = Deployment(); await scope.Root(ct); var lease = await scope.AcquireAsync(ct);
        a.AtCommit(t => scope.ValidateAsync(lease, t));
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_transfer_environment_v1.binding,cp6_transfer_environment_v1.tenants,cp6_transfer_environment_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        await Ready(ct); var binding = await Binding(ct);
        if (binding != null && binding != configured) throw Error("TRANSFER_DATABASE_ENVIRONMENT_MISMATCH");
        a.AtCommit(async t =>
        {
            await Ready(t); if (Deployment() != configured || await Binding(t) is {} saved && saved != configured)
                throw Error("TRANSFER_DATABASE_ENVIRONMENT_MISMATCH");
        });
        return configured;
    }
    private async Task Ready(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_transfer_environment_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("TRANSFER_ENVIRONMENT_STORE_UNAVAILABLE");
    }
    private Task<TransferDeploymentV1?> Binding(CancellationToken ct) => db.Database.SqlQuery<TransferDeploymentV1>($"""
        SELECT environment_id AS "EnvironmentId",mode AS "Mode",sandbox_scope_id AS "SandboxScopeId"
        FROM cp6_transfer_environment_v1.binding WHERE singleton
        """).SingleOrDefaultAsync(ct);
    internal async Task Bind(TransferDeploymentV1 deployment, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_transfer_environment_v1.binding(singleton,environment_id,mode,sandbox_scope_id)
            VALUES(true,{deployment.EnvironmentId},{deployment.Mode},{deployment.SandboxScopeId}) ON CONFLICT(singleton) DO NOTHING
            """, ct);
        if (await Binding(ct) != deployment) throw Error("TRANSFER_DATABASE_ENVIRONMENT_MISMATCH");
    }
    private sealed class Row { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    internal async Task<TransferEnvironmentRegistrationV1?> Read(long? version, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.body AS "Body",v.digest AS "Digest" FROM cp6_transfer_environment_v1.tenants h
            JOIN cp6_transfer_environment_v1.versions v ON v.tenant_id=h.tenant_id AND v.id=h.id
              AND v.revision=COALESCE({version},h.revision) WHERE h.tenant_id={db.CurrentTenantId}
            """).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var o = JsonSerializer.Deserialize<TransferEnvironmentOriginalV1>(row.Body, EngineeringProductStoreV1.Json);
        if (o == null || o.TenantId != db.CurrentTenantId || o.Deployment != Deployment() || o.Profile != Profile
            || !o.Owners.SequenceEqual(Owners) || EngineeringProductStoreV1.Digest(row.Body) != row.Digest)
            throw Error("TRANSFER_REGISTRATION_ORIGINAL_UNAVAILABLE");
        return new(new(Owner, o.Id, o.Version, row.Digest), o, Encoding.UTF8.GetString(row.Body));
    }
    internal async Task<TransferEnvironmentRegistrationV1> Append(TransferEnvironmentCommandV1 c, Guid actor,
        TransferEnvironmentRegistrationV1? before, TransferDeploymentV1 deployment, CancellationToken ct)
    {
        if (before?.Original.Enabled == c.Enabled) return before;
        var old = before == null ? 0 : EngineeringProductValuesV1.Revision(before.Reference.Version);
        if (old == long.MaxValue) throw Error("TRANSFER_REGISTRY_VERSION_EXHAUSTED");
        var id = before?.Reference.Id ?? Guid.NewGuid(); var next = old + 1;
        if (before == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_environment_v1.tenants(tenant_id,id,revision) VALUES({db.CurrentTenantId},{id},1)", ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_environment_v1.tenants SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND revision={old}", ct);
        var o = new TransferEnvironmentOriginalV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture), c.OperationId, actor,
            DateTimeOffset.UtcNow, deployment, c.Enabled, Profile, Owners, c.Reason);
        var body = EngineeringProductStoreV1.Encode(o); var digest = EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_transfer_environment_v1.versions(tenant_id,id,revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{id},{next},{c.OperationId},{actor},{body},{digest})
            """, ct);
        return (await Read(next, ct))!;
    }
    internal async Task<TransferEnvironmentRegistrationV1> Acquire(OperationsAccessV1 a, CancellationToken ct)
    {
        await Fence(a, false, ct); var r = await Read(null, ct) ?? throw Error("TRANSFER_TENANT_REGISTRATION_REQUIRED");
        if (!r.Original.Enabled) throw Error("TRANSFER_TENANT_REGISTRATION_DISABLED", 403);
        a.AtCommit(async t =>
        {
            var current = await Read(null, t);
            if (current?.Reference != r.Reference || current.Original.Enabled != true) throw Error("TRANSFER_REGISTRATION_CHANGED", 412);
        });
        return r;
    }
    internal async Task RequireSource(EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        if (!Owners.Contains(reference.Owner)) throw Error("TRANSFER_OWNER_NOT_REGISTERED");
        var space = Deployment();
        if (!await db.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM cp6_transfer_environment_v1.source_namespaces
            WHERE tenant_id={db.CurrentTenantId} AND owner={reference.Owner} AND object_id={reference.Id}
              AND revision={reference.Version} AND digest={reference.Digest} AND environment_id={space.EnvironmentId}
              AND mode={space.Mode} AND sandbox_scope_id IS NOT DISTINCT FROM {space.SandboxScopeId}
            """).AnyAsync(ct)) throw Error("TRANSFER_SOURCE_NAMESPACE_UNPROVEN", 422);
    }
}
