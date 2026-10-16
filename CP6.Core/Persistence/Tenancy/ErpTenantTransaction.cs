using System.Data.Common;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// Internal trusted-host adapter. The context must not escape the awaited callback or run detached work.
/// The Owner commits TenantStorageTransaction explicitly after this callback returns; no retry is enabled.
/// All eight tables stay in erp_integration, so the existing tenant-independent model cache is correct.
/// </summary>
internal static class ErpTenantTransaction
{
    internal const string ModelVersion = "erp-integration-v1";
    internal const string MigrationVersion = "erp-integration-tenant-rls-v1";

    internal static Task<T> ExecuteErpAsync<T>(this TenantStorageTransaction transaction,
        Func<ErpIntegrationContext, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return transaction.ExecuteAsync(async (connection, dbTransaction, token) =>
        {
            var binding = transaction.CurrentBinding;
            if (!TenantMessagingProfile.Supports(binding, identity: false))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);

            using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, binding.TenantId);
            // Reuse the same provider services as DatabaseContextOptions.ConfigurePostgreSql.
            // Its legacy public-only connection validator is intentionally not used for a managed pg_catalog lease.
            var options = new DbContextOptionsBuilder<ErpIntegrationContext>()
                .UseNpgsql(connection, contextOwnsConnection: false)
                .ReplaceService<IMigrationsSqlGenerator, PostgreSqlPrerequisiteMigrationsSqlGenerator>()
                .ReplaceService<IRelationalTypeMappingSource, PostgreSqlTextTypeMappingSource>()
                .ReplaceService<IQuerySqlGeneratorFactory, PostgreSqlTextQuerySqlGeneratorFactory>()
                .AddInterceptors(guard).Options;
            await using var context = new ErpIntegrationContext(options);
            await context.Database.UseTransactionAsync(dbTransaction, token).ConfigureAwait(false);
            try { return await action(context, token).ConfigureAwait(false); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException or TenantStorageException)
            { throw TenantConnectionManager.Safe(error.InnerException); }
        }, ct);
    }

}
