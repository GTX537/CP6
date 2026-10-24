using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>Reconciles the frozen host-owned registration set. No caller-supplied identity or grants.</summary>
public sealed class InquiryServiceIdentityPublisherV1(CP6Context db, ITenantContext tenant,
    InquiryServicePrincipalRegistryV1 registry, CrmIdentityRuntime runtime)
{
    public async Task PublishAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!registry.IdentityPublicationEnabled) return;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.Entries().Any() || !runtime.Options.Tenants.ContainsKey(tenantId)
            || registry.Sources.Any(x => !runtime.Options.Tenants.ContainsKey(x.Tenant)))
            throw new InvalidOperationException("C02_SERVICE_SOURCE_UNAVAILABLE");
        var previous = tenant.CurrentTenantId;
        try
        {
            tenant.CurrentTenantId = tenantId;
            if (db.CurrentTenantId != tenantId) throw new InvalidOperationException("C02_SERVICE_SOURCE_UNAVAILABLE");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            // Existing directory/bootstrap must already be ready under this exact contract bundle.
            if (!await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.CompletedAtUtc != null
                && x.ContractBundleSha256 == runtime.ContractBundleSha256, ct)) throw new InvalidOperationException("C02_BOOTSTRAP_NOT_READY");
            var sources = registry.Sources.Where(x => x.Tenant == tenantId).ToDictionary(x => x.Subject);
            if (await db.Sys_Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && sources.Keys.Contains(x.Id), ct))
                throw new InvalidOperationException("C02_SERVICE_SOURCE_COLLISION");
            var stored = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenantId
                && x.EventType == IdentityEventContracts.ServicePrincipalChanged).OrderBy(x => x.AggregateId).ToArrayAsync(ct);
            var writer = new IdentitySnapshotWriter(db, runtime);
            foreach (var row in stored)
            {
                var old = JsonSerializer.Deserialize<ServicePrincipalIdentityData>(row.PayloadJson, IdentityEventContracts.Json)!;
                if (old.SourceProfile != "cp6-inquiry-service-v1") throw new InvalidOperationException("C02_SERVICE_SOURCE_COLLISION");
                if (!sources.ContainsKey(old.PrincipalId) && !row.IsDeleted)
                    await writer.WriteServicePrincipalAsync(old with { Enabled = false, Roles = [], Version = 0, RegistrationVersion = registry.Version, RegistrationRevision = registry.Revision }, true, ct);
            }
            foreach (var source in sources.Values.OrderBy(x => x.Subject))
                await writer.WriteServicePrincipalAsync(new(tenantId, source.Subject, source.Client, source.Department, source.Enabled, 0,
                    source.Enabled ? source.Roles.Select(IdentityEventContracts.RoleSubject).ToArray() : [],
                    source.AuthenticationEpoch, source.Version, source.Revision), false, ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            registry.MarkPublished(tenantId);
        }
        finally { tenant.CurrentTenantId = previous; }
    }
}
