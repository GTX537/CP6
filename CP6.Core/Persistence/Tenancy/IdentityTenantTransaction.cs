using System.Data.Common;
using CP6.Core.Services.CrmIdentity;
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
/// All four tables stay in crm_identity_priority, so the existing tenant-independent model cache is correct.
/// </summary>
internal static class IdentityTenantTransaction
{
    internal const string ModelVersion = "identity-priority-v1";
    internal const string MigrationVersion = "identity-priority-tenant-rls-v1";

    internal static Task<T> ExecuteIdentityAsync<T>(this TenantStorageTransaction transaction,
        Func<IdentityMessagingContext, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return transaction.ExecuteAsync(async (connection, dbTransaction, token) =>
        {
            var binding = transaction.CurrentBinding;
            if (!TenantMessagingProfile.Supports(binding, identity: true))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);

            using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, binding.TenantId);
            // Reuse the same provider services as DatabaseContextOptions.ConfigurePostgreSql.
            // Its legacy public-only connection validator is intentionally not used for a managed pg_catalog lease.
            var options = new DbContextOptionsBuilder<IdentityMessagingContext>()
                .UseNpgsql(connection, contextOwnsConnection: false)
                .ReplaceService<IMigrationsSqlGenerator, PostgreSqlPrerequisiteMigrationsSqlGenerator>()
                .ReplaceService<IRelationalTypeMappingSource, PostgreSqlTextTypeMappingSource>()
                .ReplaceService<IQuerySqlGeneratorFactory, PostgreSqlTextQuerySqlGeneratorFactory>()
                .AddInterceptors(guard).Options;
            await using var context = new IdentityMessagingContext(options);
            await context.Database.UseTransactionAsync(dbTransaction, token).ConfigureAwait(false);
            try { return await action(context, token).ConfigureAwait(false); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException or TenantStorageException)
            { throw TenantConnectionManager.Safe(error.InnerException); }
        }, ct);
    }

}
