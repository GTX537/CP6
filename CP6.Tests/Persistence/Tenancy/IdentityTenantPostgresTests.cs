using System.Data.Common;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("IdentityTenantPostgres", DisableParallelization = true)]
public sealed class IdentityTenantPostgresCollection : ICollectionFixture<IdentityTenantPostgresFixture>;

public sealed class IdentityTenantPostgresFixture : IAsyncLifetime
{
    internal TenantStoragePostgresFixture Server { get; } = new();
    internal IModel Model { get; private set; } = null!;
    internal static TenantLoginV1[] Logins => [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")];
    internal static IdentityMessagingContext Context(NpgsqlConnection connection)
    {
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.IdentityPriority);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }
    public async Task InitializeAsync()
    {
        await Server.InitializeAsync();
        if (!TenantStoragePostgresFactAttribute.Selected) return;
        try
        {
            await using var connection = await Server.OpenOwnerAsync(searchPath: "public");
            // Separate owned database proves Identity-only first creation; the helper role is rolled back.
            await using (var firstConnection = await Server.OpenOwnerAsync("tenant_other", "public"))
            await using (var firstIdentity = Context(firstConnection))
            {
                await firstIdentity.Database.MigrateAsync();
                await using var first = await firstConnection.BeginTransactionAsync();
                await IdentityTenantRowSecurityV1.InstallAsync(firstConnection, first, Logins);
                await first.RollbackAsync();
            }
            // ERP v1 is installed before Identity's append path, as in the shipped deployment order.
            await using var erp = ErpTenantPostgresFixture.Context(connection);
            await erp.Database.MigrateAsync();
            await using (var install = await connection.BeginTransactionAsync())
            {
                await ErpTenantRowSecurityV1.InstallAsync(connection, install, Logins);
                await install.CommitAsync();
            }
            await using var identity = Context(connection);
            await identity.Database.MigrateAsync();
            Model = identity.Model;
            foreach (var anomaly in new[] {
                "GRANT TEMP ON DATABASE tenant_component TO cp6_tenant_lookup_v1",
                "GRANT USAGE ON SEQUENCE cp6_storage_erp.rowversion_v1 TO cp6_tenant_lookup_v1",
                "GRANT EXECUTE ON FUNCTION cp6_storage_erp.set_rowversion_v1() TO cp6_tenant_lookup_v1",
                "GRANT UPDATE ON cp6_tenancy_v1.login_tenant TO cp6_tenant_lookup_v1",
                "ALTER FUNCTION cp6_tenancy_v1.current_tenant_id() STABLE",
                "CREATE OR REPLACE FUNCTION cp6_tenancy_v1.current_tenant_id() RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,cp6_tenancy_v1,pg_temp AS 'BEGIN RETURN NULL; END'",
                "ALTER TABLE cp6_tenancy_v1.login_tenant DISABLE TRIGGER login_tenant_write_v1",
                "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'",
                "GRANT UPDATE (\"MessageId\") ON crm_identity_priority.\"Cp6_OutboxMessage\" TO PUBLIC",
                "CREATE POLICY unexpected ON crm_identity_priority.\"Cp6_InboxMessage\" USING (true)",
                "ALTER TABLE crm_identity_priority.\"Cp6_OutboxMessage\" DISABLE TRIGGER cp6_rowversion_v1"
            })
            {
                await using var rejected = await connection.BeginTransactionAsync();
                await TenantStoragePostgresFixture.Sql(connection, anomaly);
                await Assert.ThrowsAsync<InvalidOperationException>(() => IdentityTenantRowSecurityV1.InstallAsync(connection, rejected, Logins, true));
                await rejected.RollbackAsync();
            }
            await using (var install = await connection.BeginTransactionAsync())
            {
                await IdentityTenantRowSecurityV1.InstallAsync(connection, install, Logins, true);
                await install.CommitAsync();
            }
            foreach (var tenant in new[] { TenantStorageContext.A, TenantStorageContext.B })
            {
                foreach (var entity in identity.Model.GetEntityTypes()) identity.Add(ErpTenantPostgresFixture.Row(entity.ClrType, tenant));
                foreach (var entity in erp.Model.GetEntityTypes()) erp.Add(ErpTenantPostgresFixture.Row(entity.ClrType, tenant));
            }
            await identity.SaveChangesAsync();
            await erp.SaveChangesAsync();
        }
        catch { await Server.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => Server.DisposeAsync();
    internal async Task<int> Insert(NpgsqlConnection connection, string table, Guid tenant, Action<object>? configure = null)
    {
        var entity = Model.GetEntityTypes().Single(x => x.GetTableName() == table);
        var row = ErpTenantPostgresFixture.Row(entity.ClrType, tenant);
        configure?.Invoke(row);
        var properties = entity.GetProperties().Where(p => p.ValueGenerated != ValueGenerated.OnAddOrUpdate).ToArray();
        var store = StoreObjectIdentifier.Table(table, IdentityMessagingContext.Schema);
        await using var command = new NpgsqlCommand("INSERT INTO crm_identity_priority.\"" + table + "\" ("
            + string.Join(",", properties.Select(p => "\"" + p.GetColumnName(store) + "\"")) + ") VALUES ("
            + string.Join(",", properties.Select((_, i) => "@p" + i)) + ")", connection);
        for (var i = 0; i < properties.Length; i++)
            command.Parameters.Add(properties[i].GetRelationalTypeMapping().CreateParameter(command, "p" + i, properties[i].PropertyInfo!.GetValue(row)));
        return await command.ExecuteNonQueryAsync();
    }
}

[Collection("IdentityTenantPostgres")]
public sealed class IdentityTenantPostgresTests(IdentityTenantPostgresFixture postgres)
{
    private static string Table(string table) => "crm_identity_priority.\"" + table + "\"";
    private TenantStorageCatalog Catalog(string model = TenantMessagingProfile.CombinedModelVersion,
        string migration = TenantMessagingProfile.CombinedMigrationVersion, string revision = "r1", string[]? queues = null) =>
        new("main", [model], new[] { TenantStorageContext.A, TenantStorageContext.B }.Select(tenant =>
            new TenantStorageBindingV1(tenant, "main", "db", TenantStorageBindingV1.SupportedLayout, model, revision, "v1",
                tenant == TenantStorageContext.A ? "principal-a" : "principal-b", queues ?? ["erp_integration", "crm_identity_priority"],
                TenantStorageState.Ready, migration)),
            [new("db", "127.0.0.1", TenantStoragePostgresFixture.Port, TenantStoragePostgresFixture.Database, SslMode.Disable)],
            [new("principal-a", "tenant_a"), new("principal-b", "tenant_b")]);
    private TenantConnectionManager Manager(TenantStorageContext context, TenantStorageCatalog? catalog = null) => new(
        "main", context.Accessor, catalog ?? Catalog(), new TenantStorageCredentials
        { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == TenantStorageContext.A ? postgres.Server.PasswordA : postgres.Server.PasswordB) }, new(2, 2, 1));

    [TenantStoragePostgresFact]
    public async Task All_four_tables_isolate_select_insert_old_and_new_update_rows_and_deny_retention()
    {
        foreach (var (tenant, role, password) in new[] { (TenantStorageContext.A,"tenant_a",postgres.Server.PasswordA), (TenantStorageContext.B,"tenant_b",postgres.Server.PasswordB) })
        {
            var other = tenant == TenantStorageContext.A ? TenantStorageContext.B : TenantStorageContext.A;
            await using var connection = await TenantStoragePostgresFixture.OpenRawAsync(role, password);
            foreach (var table in IdentityTenantRowSecurityV1.Tables)
            {
                Assert.Equal(1L, await TenantStoragePostgresFixture.Scalar(connection, $"SELECT count(*) FROM {Table(table)}"));
                await using (var tx = await connection.BeginTransactionAsync())
                {
                    Assert.Equal(1, await postgres.Insert(connection, table, tenant));
                    Assert.Equal(2, await TenantStoragePostgresFixture.Sql(connection, $"UPDATE {Table(table)} SET \"TenantId\"='{tenant}'"));
                    await tx.RollbackAsync();
                }
                await Denied(connection, () => postgres.Insert(connection, table, other));
                await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"UPDATE {Table(table)} SET \"TenantId\"='{other}'"));
                await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"DELETE FROM {Table(table)}"));
                await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"TRUNCATE {Table(table)}"));
            }
            await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, "SELECT * FROM cp6_tenancy_v1.login_tenant"));
            await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, "SET ROLE cp6_tenant_lookup_v1"));
        }
    }

    [TenantStoragePostgresFact]
    public async Task Global_inbox_outbox_keys_remain_global_and_safe_while_checkpoint_includes_tenant()
    {
        var context = new TenantStorageContext();
        await using var manager = Manager(context);
        await using var owner = await postgres.Server.OpenOwnerAsync();
        foreach (var table in new[] { "Cp6_InboxMessage", "Cp6_OutboxMessage", "Cp6_InboxAggregateCheckpoint" })
        {
            var entity = postgres.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
            var keyFields = table == "Cp6_InboxAggregateCheckpoint" ? new[] { "ConsumerName", "AggregateId" }
                : table == "Cp6_InboxMessage" ? new[] { "ConsumerName", "MessageId" } : new[] { "MessageId" };
            var values = new Dictionary<string,string>();
            foreach (var field in keyFields) values[field] = (string)(await TenantStoragePostgresFixture.Scalar(owner,
                $"SELECT \"{field}\" FROM {Table(table)} WHERE \"TenantId\"='{TenantStorageContext.B}'"))!;
            await context.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                var operation = lease.InTransactionAsync((tx,ct) => tx.ExecuteIdentityAsync(async (identity,token) =>
                {
                    var row = ErpTenantPostgresFixture.Row(entity.ClrType, TenantStorageContext.A);
                    foreach (var field in values) entity.ClrType.GetProperty(field.Key)!.SetValue(row, field.Value);
                    identity.Add(row);
                    return await identity.SaveChangesAsync(token);
                },ct));
                if (table == "Cp6_InboxAggregateCheckpoint") Assert.Equal(1, await operation);
                else
                {
                    var error = await Assert.ThrowsAsync<TenantStorageException>(() => operation);
                    Assert.Equal("23505", error.SqlState); Assert.Null(error.InnerException);
                    foreach (var value in values.Values) Assert.DoesNotContain(value,error.ToString());
                }
            });
        }
    }

    [TenantStoragePostgresFact]
    public async Task Real_contexts_share_one_connection_transaction_and_commit_or_rollback_together_with_real_tokens()
    {
        var scope = new TenantStorageContext();
        await using var manager = Manager(scope);
        foreach (var tenant in new[] { TenantStorageContext.A,TenantStorageContext.B })
        await scope.Run(tenant,async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            foreach (var commit in new[] { false,true })
            {
                var erpId=Guid.NewGuid(); var identityId=Guid.NewGuid();
                await lease.InTransactionAsync(async (tx,ct) =>
                {
                    DbConnection? connection=null; DbTransaction? transaction=null;
                    await tx.ExecuteErpAsync(async (erp,token) =>
                    {
                        connection=erp.Database.GetDbConnection(); transaction=erp.Database.CurrentTransaction!.GetDbTransaction();
                        var row=(ErpIntegrationAggregate)ErpTenantPostgresFixture.Row(typeof(ErpIntegrationAggregate),tenant);
                        row.AggregateId=erpId; erp.Add(row); await erp.SaveChangesAsync(token);
                        return 0;
                    },ct);
                    IdentityMessagingContext? retained=null;
                    await tx.ExecuteIdentityAsync(async (identity,token) =>
                    {
                        retained=identity;
                        Assert.Same(connection,identity.Database.GetDbConnection());
                        Assert.Same(transaction,identity.Database.CurrentTransaction!.GetDbTransaction());
                        Assert.False(identity.Database.CreateExecutionStrategy().RetriesOnFailure);
                        foreach (var entity in identity.Model.GetEntityTypes())
                        {
                            var row=ErpTenantPostgresFixture.Row(entity.ClrType,tenant);
                            entity.ClrType.GetProperty("Id")!.SetValue(row,identityId);
                            identity.Add(row); await identity.SaveChangesAsync(token);
                            var old=(byte[])entity.ClrType.GetProperty("RowVersion")!.GetValue(row)!;
                            Assert.Equal(8,old.Length);
                            entity.ClrType.GetProperty("TenantId")!.SetValue(row,tenant);
                            identity.Entry(row).Property("TenantId").IsModified=true;
                            await identity.SaveChangesAsync(token);
                            Assert.False(old.SequenceEqual((byte[])entity.ClrType.GetProperty("RowVersion")!.GetValue(row)!));
                        }
                        var error=await Assert.ThrowsAsync<TenantStorageException>(() => tx.ExecuteErpAsync((_,_)=>Task.FromResult(0),token));
                        Assert.Equal(TenantStorageFailure.OperationInProgress,error.Failure);
                        return 0;
                    },ct);
                    Assert.Throws<ObjectDisposedException>(()=>retained!.Database.GetDbConnection());
                    if(commit) await tx.CommitAsync(ct);
                    return 0;
                });
                await using var owner=await postgres.Server.OpenOwnerAsync();
                Assert.Equal(commit?1L:0L,await TenantStoragePostgresFixture.Scalar(owner,$"SELECT count(*) FROM erp_integration.\"Aggregate\" WHERE \"AggregateId\"='{erpId}'"));
                foreach(var table in IdentityTenantRowSecurityV1.Tables)
                {
                    Assert.Equal(commit?1L:0L,await TenantStoragePostgresFixture.Scalar(owner,$"SELECT count(*) FROM {Table(table)} WHERE \"Id\"='{identityId}'"));
                    if(commit) await TenantStoragePostgresFixture.Sql(owner,$"DELETE FROM {Table(table)} WHERE \"Id\"='{identityId}'");
                }
                if(commit) await TenantStoragePostgresFixture.Sql(owner,$"DELETE FROM erp_integration.\"Aggregate\" WHERE \"AggregateId\"='{erpId}'");
            }
        });
    }

    [TenantStoragePostgresFact]
    public async Task Identity_guard_rejects_nested_scope_catalog_rotation_profile_and_mapping_drift()
    {
        var scope=new TenantStorageContext();
        foreach(var catalog in new[] { Catalog(model:"unknown"),Catalog(migration:"unknown"),Catalog(queues:["crm_identity_priority"]),Catalog(queues:["erp_integration","crm_identity_priority","other"]) })
        {
            await using var manager=Manager(scope,catalog);
            await scope.Run(TenantStorageContext.A,async _ =>
            {
                await using var lease=await manager.OpenCurrentAsync(); var called=false;
                var error=await Assert.ThrowsAsync<TenantStorageException>(()=>lease.InTransactionAsync((tx,ct)=>tx.ExecuteIdentityAsync((_,_)=>{called=true;return Task.FromResult(0);},ct)));
                Assert.Equal(TenantStorageFailure.BindingRejected,error.Failure);Assert.False(called);
            });
        }
        await using var good=Manager(scope);
        await scope.Run(TenantStorageContext.A,async _ =>
        {
            await using var lease=await good.OpenCurrentAsync();
            var error=await Assert.ThrowsAsync<TenantStorageException>(()=>lease.InTransactionAsync((tx,ct)=>tx.ExecuteIdentityAsync(async (identity,token)=>
            {
                await identity.Database.ExecuteSqlRawAsync("SELECT 1",token);
                await scope.Run(TenantStorageContext.B,async _=>
                {
                    Assert.Equal(TenantStorageFailure.ContextChanged,(await Assert.ThrowsAsync<TenantStorageException>(()=>identity.Database.ExecuteSqlRawAsync("SELECT 1",token))).Failure);
                    Assert.Equal(TenantStorageFailure.ContextChanged,Assert.Throws<TenantStorageException>(()=>identity.Database.ExecuteSqlRaw("SELECT 1")).Failure);
                });
                good.ReplaceCatalog(Catalog(revision:"r2"));
                return await identity.Database.ExecuteSqlRawAsync("SELECT 1",token);
            },ct)));
            Assert.Equal(TenantStorageFailure.BindingChanged,error.Failure);
        });
        await using var owner=await postgres.Server.OpenOwnerAsync();
        foreach(var drift in new[] {"tenant","missing"})
        {
            try
            {
                await TenantStoragePostgresFixture.Sql(owner,drift=="tenant"
                    ? "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'"
                    : "DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='tenant_a'");
                await scope.Run(TenantStorageContext.A,async _=>
                {
                    await using var lease=await good.OpenCurrentAsync();
                    var error=await Assert.ThrowsAsync<TenantStorageException>(()=>lease.InTransactionAsync((tx,ct)=>tx.ExecuteIdentityAsync((identity,token)=>identity.Database.ExecuteSqlRawAsync("SELECT 1",token),ct)));
                    Assert.Equal(drift=="tenant"?TenantStorageFailure.IdentityRejected:TenantStorageFailure.DatabaseRejected,error.Failure);
                });
            }
            finally {await TenantStoragePostgresFixture.Sql(owner,$"DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='tenant_a'; INSERT INTO cp6_tenancy_v1.login_tenant SELECT oid,rolname,'{TenantStorageContext.A}' FROM pg_catalog.pg_roles WHERE rolname='tenant_a'");}
        }
    }
    [TenantStoragePostgresFact]
    public async Task Identity_only_profile_is_explicit_and_ended_scope_cannot_issue_a_late_command()
    {
        var scope = new TenantStorageContext();
        await using var manager = Manager(scope, Catalog(IdentityTenantTransaction.ModelVersion,
            IdentityTenantTransaction.MigrationVersion, queues: ["crm_identity_priority"]));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        TenantConnectionLease? held = null;
        await scope.Run(TenantStorageContext.A, async _ =>
        {
            held = await manager.OpenCurrentAsync();
            pending = held.InTransactionAsync((tx, ct) => tx.ExecuteIdentityAsync(async (identity, token) =>
            {
                await identity.Database.ExecuteSqlRawAsync("SELECT 1", token);
                entered.SetResult();
                await resume.Task;
                return await identity.Database.ExecuteSqlRawAsync("SELECT 1", token);
            }, ct));
            await entered.Task;
        });
        resume.SetResult();
        Assert.Equal(TenantStorageFailure.ContextChanged,
            (await Assert.ThrowsAsync<TenantStorageException>(() => pending!)).Failure);
        await held!.DisposeAsync();
        await scope.Run(TenantStorageContext.A, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            Assert.Equal(TenantStorageFailure.BindingRejected,
                (await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync((tx, ct) =>
                    tx.ExecuteErpAsync((_, _) => Task.FromResult(0), ct)))).Failure);
        });
    }

    private static async Task Denied(NpgsqlConnection connection,Func<Task> action)
    {
        await using var tx=await connection.BeginTransactionAsync();
        Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(action)).SqlState);
        await tx.RollbackAsync();
    }
}
