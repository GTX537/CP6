using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

public sealed class IdentityBootstrapService(CP6Context db, CrmIdentityRuntime runtime)
{
    public async Task InitializeAsync(Guid tenant, CancellationToken cancellationToken = default)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenant)) throw new InvalidOperationException("C02_TENANT_NOT_CONFIGURED");
        if (!db.Database.IsNpgsql())
        {
            await InitializeTransactionAsync(tenant, cancellationToken);
            return;
        }
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("C02_BOOTSTRAP_REQUIRES_OWN_TRANSACTION");

        // Bootstrap can be absent. Wait for its owner before starting a Serializable snapshot;
        // an advisory transaction lock acquired after waiting would retain an older snapshot.
        var openedHere = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var ownership = await DatabaseResourceLocks.TryAcquireSessionAsync(
                db.Database.GetDbConnection(), DatabaseProvider.PostgreSql,
                $"cp6:c02:bootstrap:{tenant:N}", 30_000, cancellationToken)
                ?? throw new TimeoutException("C02_BOOTSTRAP_LOCK_TIMEOUT");
            // Serialization failures propagate. The bootstrap worker retries the entire command
            // in a new scope/context; neither the writer nor this Serializable transaction retries.
            await InitializeTransactionAsync(tenant, cancellationToken);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    private async Task InitializeTransactionAsync(Guid tenant, CancellationToken cancellationToken)
    {
        // Serialize bootstrap ownership. Existing request writers use the same aggregate locks,
        // so bootstrap can never overwrite a newer committed business snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var states = db.Database.IsNpgsql()
            ? db.CrmIdentityBootstrapStates.FromSqlInterpolated($"""
                SELECT * FROM crm_identity."Bootstrap" WHERE "TenantId"={tenant} FOR UPDATE
                """)
            : db.CrmIdentityBootstrapStates.FromSqlInterpolated($"SELECT * FROM crm_identity.Bootstrap WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenant}");
        var state = await states.SingleOrDefaultAsync(cancellationToken);
        if (state?.CompletedAtUtc is not null && state.ContractBundleSha256 == runtime.ContractBundleSha256)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        state ??= new CrmIdentityBootstrapState { TenantId = tenant };
        if (db.Entry(state).State == EntityState.Detached) db.CrmIdentityBootstrapStates.Add(state);
        state.CompletedAtUtc = null;
        state.ContractBundleSha256 = runtime.ContractBundleSha256;
        // The first cut is one consistent tenant transaction. It publishes full snapshots through
        // the normal writer and is idempotent: unchanged data retains its existing version.
        var targets = new List<string> { $"tenant:{tenant:D}" };
        targets.AddRange((await db.Sys_Users.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.Id).ToArrayAsync(cancellationToken)).Select(x => $"user:{x:D}"));
        targets.AddRange((await db.Sys_Depts.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.Id).ToArrayAsync(cancellationToken)).Select(x => $"department:{x:D}"));
        targets.AddRange((await db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == tenant).Select(x => x.RoleId).ToArrayAsync(cancellationToken)).Select(x => $"permission:role:{x}"));
        // Preserve and refresh known non-token tombstones if a missed pre-enablement delete occurred.
        targets.AddRange(await db.CrmIdentitySnapshots.Where(x => x.TenantId == tenant && !x.AggregateId.StartsWith("token:"))
            .Select(x => x.AggregateId).ToArrayAsync(cancellationToken));
        var writer = new IdentitySnapshotWriter(db, runtime);
        foreach (var aggregate in targets.Distinct().Order(StringComparer.Ordinal))
            await writer.WriteCurrentAsync(tenant, aggregate, cancellationToken);
        state.CompletedAtUtc = runtime.Clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
