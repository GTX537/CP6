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
            if (binding.ModelVersion != ModelVersion || binding.MigrationVersion != MigrationVersion
                || !binding.QueueDomainRefs.Contains(ErpIntegrationContext.Schema, StringComparer.Ordinal))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);

            using var guard = new CommandGuard(transaction, connection, dbTransaction, binding.TenantId);
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

    private sealed class CommandGuard(TenantStorageTransaction owner, NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid tenant) : DbCommandInterceptor, IDisposable
    {
        private bool _closed;
        public void Dispose() => _closed = true;
        private void Check(DbCommand command)
        {
            if (_closed || !ReferenceEquals(command.Connection, connection) || !ReferenceEquals(command.Transaction, transaction))
                throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
            _ = owner.CurrentBinding;
        }
        private async Task ValidateAsync(DbCommand command, CancellationToken ct)
        {
            Check(command);
            await owner.CheckConnectionAsync(ct).ConfigureAwait(false);
            await using var identity = new NpgsqlCommand("SELECT cp6_tenancy_v1.current_tenant_id()", connection, transaction);
            if (await identity.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid actual || actual != tenant)
                throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
            Check(command);
        }
        private void Validate(DbCommand command) => ValidateAsync(command, default).GetAwaiter().GetResult();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r)
        { Validate(c); return r; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r)
        { Validate(c); return r; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r)
        { Validate(c); return r; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e,
            InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e,
            InterceptionResult<int> r, CancellationToken ct = default)
        { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e,
            InterceptionResult<object> r, CancellationToken ct = default)
        { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
    }
}
