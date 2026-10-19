using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// Opt-in trusted host reader. Exposes detached DTOs only and never falls back to an Owner connection.
/// Authentication/current reader authorization remain the host's responsibility.
/// </summary>
internal sealed class TenantIdentityDirectoryReader(TenantConnectionManager manager, CrmIdentityRuntime runtime,
    IDataProtectionProvider protection, IdentityDirectoryDatabaseIdentity? installedIdentity = null) : IIdentitySnapshotReader, IIdentityRawSnapshotReader
{
    public Task<IdentityVersionPage> ReadVersionsAsync(Guid tenant, string? cursor, int size = 200, CancellationToken cancellationToken = default)
        => ReadAsync(tenant, (db, ct) => new IdentitySnapshotReader(db, runtime, protection).ReadVersionsAsync(tenant, cursor, size, ct), cancellationToken);
    public Task<IdentitySnapshot?> ReadSnapshotAsync(Guid tenant, string aggregate, CancellationToken cancellationToken = default)
        => ReadAsync(tenant, (db, ct) => new IdentitySnapshotReader(db, runtime, protection).ReadSnapshotAsync(tenant, aggregate, ct), cancellationToken);

    public Task<IdentityBaselinePageV1> ReadBaselinePageAsync(Guid tenant, string? cursor, int size = 200, CancellationToken ct = default)
        => ReadAsync(tenant, (db, token) => Raw(db, tenant).ReadBaselinePageAsync(tenant, cursor, size, token), ct);
    public Task<IdentityRawSnapshotV1?> ReadRawSnapshotAsync(Guid tenant, string aggregate, CancellationToken ct = default)
        => ReadAsync(tenant, (db, token) => Raw(db, tenant).ReadRawSnapshotAsync(tenant, aggregate, token), ct);

    private IdentityRawSnapshotReader Raw(CP6Context db, Guid tenant)
    {
        if (installedIdentity is null || !runtime.Options.Tenants.TryGetValue(tenant, out var region))
            throw new IdentityReadException("C02_IDENTITY_STORE_UNAVAILABLE");
        return new(db, runtime, protection, new(tenant, runtime.Options.Issuer, region, runtime.ContractBundleSha256, installedIdentity.BaselineSha256));
    }

    private async Task<T> ReadAsync<T>(Guid tenant, Func<CP6Context, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ReadResult<T> result;
        try
        {
            await using var lease = await manager.OpenCurrentAsync(ct).ConfigureAwait(false);
            var binding = lease.CurrentBinding;
            var knownProfile = (binding.ModelVersion, binding.MigrationVersion) is
                (IdentityDirectoryRowSecurityV1.ModelVersion, IdentityDirectoryRowSecurityV1.MigrationVersion) or
                (IdentityDirectoryAuthorizationV2.ModelVersion, IdentityDirectoryAuthorizationV2.MigrationVersion);
            if (binding.TenantId != tenant || !knownProfile || binding.QueueDomainRefs.Count != 0 ||
                (installedIdentity is not null && binding.ModelVersion != IdentityDirectoryAuthorizationV2.ModelVersion))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);
            result = await lease.InTransactionAsync(async (transaction, token) =>
            {
                var read = await transaction.ExecuteAsync(async (connection, dbTransaction, cancellation) =>
                {
                    using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                    if (installedIdentity is not null)
                    {
                        await guard.ValidateCurrentAsync(cancellation).ConfigureAwait(false);
                        await installedIdentity.ValidateAsync(connection, dbTransaction, cancellation).ConfigureAwait(false);
                    }
                    var options = new DbContextOptionsBuilder<CP6Context>()
                        .UseNpgsql(connection, contextOwnsConnection: false)
                        .ReplaceService<IMigrationsSqlGenerator, PostgreSqlPrerequisiteMigrationsSqlGenerator>()
                        .ReplaceService<IRelationalTypeMappingSource, PostgreSqlTextTypeMappingSource>()
                        .ReplaceService<IQuerySqlGeneratorFactory, PostgreSqlTextQuerySqlGeneratorFactory>()
                        .AddInterceptors(guard).Options;
                    await using var db = new DirectoryContext(options, tenant);
                    await db.Database.UseTransactionAsync(dbTransaction, cancellation).ConfigureAwait(false);
                    try
                    {
                        return new ReadResult<T>(await action(db, cancellation).ConfigureAwait(false), null);
                    }
                    // Carry only fixed public contract errors across the storage sanitization boundary.
                    // Unknown exceptions still fail closed through TenantConnectionManager.Safe.
                    catch (IdentityReadException error) when (error.Message is "C02_INVALID_PAGE_SIZE" or "C02_INVALID_CURSOR" or
                        "C02_INVALID_AGGREGATE" or "C02_BOOTSTRAP_NOT_READY" or "C02_SNAPSHOT_BOUNDARY_CHANGED" or "C02_IDENTITY_STORE_UNAVAILABLE")
                    { return new ReadResult<T>(default, error.Message); }
                }, token).ConfigureAwait(false);
                if (read.Error is null) await transaction.CommitAsync(token).ConfigureAwait(false);
                return read;
            }, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (TenantStorageException) { throw new IdentityReadException("C02_IDENTITY_STORE_UNAVAILABLE"); }
        if (result.Error is not null) throw new IdentityReadException(result.Error);
        return result.Value!;
    }

    private sealed record ReadResult<T>(T? Value, string? Error);

    // The legacy Core model leaves this existing public table's schema implicit. A managed
    // lease deliberately uses pg_catalog, so qualify it in this private read-only model.
    // A distinct context type keeps its fixed model separate from normal Core contexts;
    // no tenant-dependent cache key, migration, schema creation or write surface is added.
    private sealed class DirectoryContext(DbContextOptions<CP6Context> options, Guid tenant)
        : CP6Context(options, new TenantContext { CurrentTenantId = tenant })
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CrmIdentityTenantGeneration>().ToTable("CrmIdentityTenantGenerations", "public");
        }
    }
}
