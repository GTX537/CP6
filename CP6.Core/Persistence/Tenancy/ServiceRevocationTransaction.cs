using System.Runtime.CompilerServices;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record ServiceRevocationDatabaseIdentity(Guid Installation, uint DatabaseOid, string SystemIdentifier, string Server, int Port, bool ServiceTokens = false)
{
    internal string Profile => ServiceTokens ? ServiceTokenProfileV1.Profile : ServiceRevocationTransaction.Profile;
    internal string Migration => ServiceTokens ? ServiceTokenProfileV1.MigrationVersion : ServiceRevocationTransaction.MigrationVersion;
    internal string Schema => ServiceTokens ? ServiceTokenProfileV1.Schema : "cp6_service_revocation_v1";
}

/// <summary>Only this bounded adapter may mark a context as a managed revocation participant.</summary>
internal sealed class ServiceRevocationTransaction : IDisposable
{
    internal const string Profile = "service-revocation-v1";
    internal const string MigrationVersion = "service-revocation-tenant-rls-v1";
    private static readonly ConditionalWeakTable<DbContext, ServiceRevocationTransaction> Participants = new();
    private readonly TenantEfCommandGuard _guard;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _closed;

    private ServiceRevocationTransaction(TenantStorageTransaction owner, NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid tenant)
    {
        _guard = new(owner, connection, transaction, tenant);
        _connection = connection; _transaction = transaction;
    }

    internal static bool TryGet(DbContext context, out ServiceRevocationTransaction participant) =>
        Participants.TryGetValue(context, out participant!);

    internal static async Task RevokeAsync(TenantStorageTransaction owner, CrmIdentityRuntime runtime,
        string issuer, string client, Guid tenant, string jti, ServiceRevocationDatabaseIdentity databaseIdentity,
        Func<CancellationToken, Task> authorityFence,
        CancellationToken ct)
    {
        await owner.ExecuteAsync(async (connection, transaction, token) =>
        {
            var binding = owner.CurrentBinding;
            if (binding.ModelVersion != databaseIdentity.Profile || binding.MigrationVersion != databaseIdentity.Migration || binding.TenantId != tenant
                || !binding.QueueDomainRefs.SequenceEqual(["crm_identity_priority"]))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);
            // This fence lives until CommitAsync returns; the EF child scope ends earlier.
            owner.SetCommitFence(async finalToken =>
            {
                await CheckTenant(connection, transaction, tenant, finalToken).ConfigureAwait(false);
                await CheckInstallation(connection, transaction, databaseIdentity, finalToken).ConfigureAwait(false);
                await authorityFence(finalToken).ConfigureAwait(false);
            });
            using var participant = new ServiceRevocationTransaction(owner, connection, transaction, tenant);
            await participant._guard.ValidateCurrentAsync(token).ConfigureAwait(false);
            await CheckInstallation(connection, transaction, databaseIdentity, token).ConfigureAwait(false);
            await authorityFence(token).ConfigureAwait(false);
            await using var db = new CP6Context(participant.Options<CP6Context>(DatabaseContextKind.Core).Options,
                new TenantContext { CurrentTenantId = tenant });
            Participants.Add(db, participant);
            await db.Database.UseTransactionAsync(transaction, token).ConfigureAwait(false);
            await CrmServiceTokenRecordStore.RevokeInTransactionAsync(db, runtime, issuer, client, tenant, jti, token).ConfigureAwait(false);
            await authorityFence(token).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    internal static async Task IssueAsync(TenantStorageTransaction owner, CrmIdentityRuntime runtime,
        string issuer, string client, Guid tenant, string jti, DateTimeOffset expiry,
        ServiceRevocationDatabaseIdentity databaseIdentity, Func<CancellationToken, Task> authorityFence,
        TimeProvider clock, CancellationToken ct)
    {
        await owner.ExecuteAsync(async (connection, transaction, token) =>
        {
            var binding = owner.CurrentBinding;
            if (!databaseIdentity.ServiceTokens || binding.ModelVersion != ServiceTokenProfileV1.Profile
                || binding.MigrationVersion != ServiceTokenProfileV1.MigrationVersion || binding.TenantId != tenant
                || !binding.QueueDomainRefs.SequenceEqual(["crm_identity_priority"]))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);
            async Task Fence(CancellationToken finalToken)
            {
                await CheckTenant(connection, transaction, tenant, finalToken).ConfigureAwait(false);
                await CheckInstallation(connection, transaction, databaseIdentity, finalToken).ConfigureAwait(false);
                await ServiceTokenProfileV1.RequireActiveAsync(connection, transaction, clock, finalToken).ConfigureAwait(false);
                await authorityFence(finalToken).ConfigureAwait(false);
                if (clock.GetUtcNow() >= expiry) throw new TenantStorageException(TenantStorageFailure.TenantInactive);
            }
            owner.SetCommitFence(Fence);
            using var participant = new ServiceRevocationTransaction(owner, connection, transaction, tenant);
            await participant._guard.ValidateCurrentAsync(token).ConfigureAwait(false);
            await Fence(token).ConfigureAwait(false);
            await using var db = new CP6Context(participant.Options<CP6Context>(DatabaseContextKind.Core).Options,
                new TenantContext { CurrentTenantId = tenant });
            Participants.Add(db, participant);
            await db.Database.UseTransactionAsync(transaction, token).ConfigureAwait(false);
            await CrmServiceTokenRecordStore.RecordInTransactionAsync(db, runtime, issuer, client, tenant, jti,
                expiry, token).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    internal async Task<TContext> CreateChildAsync<TContext>(DbContext caller, DatabaseContextKind target,
        Func<DbContextOptions<TContext>, TContext> create, CancellationToken ct) where TContext : DbContext
    {
        Check(caller);
        if (target != DatabaseContextKind.IdentityPriority) throw new TenantStorageException(TenantStorageFailure.BindingRejected);
        await _guard.ValidateCurrentAsync(ct).ConfigureAwait(false);
        var context = create(Options<TContext>(target).Options);
        try
        {
            var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
            if (!ReferenceEquals(context.Database.GetDbConnection(), _connection) || relational.IsConnectionOwned
                || !context.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>()
                    .SelectMany(x => x.Interceptors ?? []).Contains(_guard))
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);
            await context.Database.UseTransactionAsync(_transaction, ct).ConfigureAwait(false);
            Participants.Add(context, this);
            return context;
        }
        catch { await context.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal async Task AcquireSnapshotLockAsync(DbContext caller, string resource, CancellationToken ct)
    {
        Check(caller);
        // Same stable resource key as the legacy helper; EF runs both sync/async raw SQL through the guard.
        var key = DatabaseResourceLocks.PostgreSqlKey(resource);
        await caller.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_catalog.pg_advisory_xact_lock({key})", ct).ConfigureAwait(false);
    }

    private DbContextOptionsBuilder<T> Options<T>(DatabaseContextKind kind) where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(new(DatabaseProvider.PostgreSql), kind);
        return new DbContextOptionsBuilder<T>().UseNpgsql(_connection, contextOwnsConnection: false,
                pg => pg.MigrationsAssembly(profile.MigrationsAssembly).MigrationsHistoryTable(profile.HistoryTable!, profile.HistorySchema))
            .ReplaceService<IMigrationsSqlGenerator, PostgreSqlPrerequisiteMigrationsSqlGenerator>()
            .ReplaceService<IRelationalTypeMappingSource, PostgreSqlTextTypeMappingSource>()
            .ReplaceService<IQuerySqlGeneratorFactory, PostgreSqlTextQuerySqlGeneratorFactory>()
            .AddInterceptors(_guard);
    }
    private void Check(DbContext context)
    {
        if (_closed || !ReferenceEquals(context.Database.GetDbConnection(), _connection)
            || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), _transaction))
            throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
    }
    private static async Task CheckTenant(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant, CancellationToken ct)
    {
        await using var check = new NpgsqlCommand("SELECT cp6_tenancy_v1.current_tenant_id()", connection, transaction);
        if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid current || current != tenant)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
    private static async Task CheckInstallation(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ServiceRevocationDatabaseIdentity expected, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($$"""
            SELECT i.installation_id=@installation AND i.profile=@profile AND i.database_oid=@oid
              AND {{expected.Schema}}.system_identifier()=@system
              AND (SELECT oid FROM pg_catalog.pg_database WHERE datname=pg_catalog.current_database())=@oid
              AND pg_catalog.host(pg_catalog.inet_server_addr())=@server AND pg_catalog.inet_server_port()=@port
              AND i.fingerprint={{expected.Schema}}.fingerprint()
            FROM {{expected.Schema}}.installation i WHERE i.singleton
            """, connection, transaction);
        command.Parameters.AddWithValue("installation", expected.Installation); command.Parameters.AddWithValue("profile", expected.Profile);
        command.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, expected.DatabaseOid);
        command.Parameters.AddWithValue("system", expected.SystemIdentifier); command.Parameters.AddWithValue("server", expected.Server);
        command.Parameters.AddWithValue("port", expected.Port);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
    public void Dispose() { _closed = true; _guard.Dispose(); }
}
