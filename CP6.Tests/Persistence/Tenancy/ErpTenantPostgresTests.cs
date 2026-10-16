using System.Data.Common;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("ErpTenantPostgres", DisableParallelization = true)]
public sealed class ErpTenantPostgresCollection : ICollectionFixture<ErpTenantPostgresFixture>;

public sealed class ErpTenantPostgresFixture : IAsyncLifetime
{
    internal TenantStoragePostgresFixture Server { get; } = new();
    internal IModel Model { get; private set; } = null!;
    internal static readonly string[] Tables = ["Aggregate", "CommandInbox", "Cp6_DeadLetterRecord", "Cp6_OutboxMessage",
        "DeliveryReplayAudit", "InboxReplayAudit", "OrderBridgeDispatch", "Request"];
    internal static ErpIntegrationContext Context(NpgsqlConnection connection)
    {
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.ErpIntegration);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ErpIntegrationContext>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }
    public async Task InitializeAsync()
    {
        await Server.InitializeAsync();
        if (!TenantStoragePostgresFactAttribute.Selected) return;
        try
        {
            await using var connection = await Server.OpenOwnerAsync(searchPath: "public");
            await using var context = Context(connection);
            await context.Database.MigrateAsync();
            Model = context.Model;
            // Each anomaly is exercised against the untouched actual baseline and rolled back.
            // None may be silently adopted, even if a later GRANT would otherwise hide it.
            foreach (var anomaly in new[] {
                "CREATE POLICY unknown_allow ON erp_integration.\"Aggregate\" USING (true)",
                "CREATE TABLE erp_integration.unknown_object(id int)",
                "GRANT SELECT ON erp_integration.\"Aggregate\" TO tenant_a",
                "GRANT UPDATE (\"ReasonCode\") ON erp_integration.\"InboxReplayAudit\" TO tenant_a",
                "GRANT UPDATE (\"ReasonCode\") ON erp_integration.\"DeliveryReplayAudit\" TO PUBLIC",
                "ALTER DEFAULT PRIVILEGES GRANT SELECT, UPDATE ON TABLES TO tenant_a",
                "CREATE FUNCTION public.unexpected_definer() RETURNS void LANGUAGE sql SECURITY DEFINER AS 'SELECT NULL::void'",
                "CREATE TABLE public.preexisting_exposed(id int); GRANT SELECT(id) ON public.preexisting_exposed TO PUBLIC",
                "CREATE ROLE unexpected_owner NOLOGIN; ALTER TABLE erp_integration.\"Aggregate\" OWNER TO unexpected_owner",
                "ALTER TABLE erp_integration.\"Aggregate\" DISABLE TRIGGER cp6_rowversion_v1"
            })
            {
                await using var rejected = await connection.BeginTransactionAsync();
                await TenantStoragePostgresFixture.Sql(connection, anomaly);
                await Assert.ThrowsAsync<InvalidOperationException>(() => ErpTenantRowSecurityV1.InstallAsync(connection, rejected,
                    [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")]));
                await rejected.RollbackAsync();
            }
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await ErpTenantRowSecurityV1.InstallAsync(connection, transaction,
                    [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")]);
                await transaction.CommitAsync();
            }
            foreach (var tenant in new[] { TenantStorageContext.A, TenantStorageContext.B })
                foreach (var entity in context.Model.GetEntityTypes())
                    context.Add(Row(entity.ClrType, tenant));
            await context.SaveChangesAsync();
        }
        catch { await Server.DisposeAsync(); throw; }
    }
    internal static object Row(Type type, Guid tenant)
    {
        var row = Activator.CreateInstance(type, nonPublic: true)!;
        foreach (var property in type.GetProperties().Where(p => p.CanWrite))
        {
            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? value = valueType == typeof(Guid) ? Guid.NewGuid()
                : valueType == typeof(string) ? "fixture-" + Guid.NewGuid().ToString("N")[..8]
                : valueType == typeof(byte[]) ? new byte[8]
                : valueType == typeof(DateTimeOffset) ? DateTimeOffset.UtcNow
                : valueType == typeof(DateTime) ? DateTime.UtcNow
                : valueType == typeof(int) ? 0 : null;
            if (value is not null) property.SetValue(row, value);
        }
        type.GetProperty("TenantId")!.SetValue(row, tenant);
        return row;
    }
    public Task DisposeAsync() => Server.DisposeAsync();

    internal async Task<int> Insert(NpgsqlConnection connection, string table, Guid tenant, Action<object>? configure = null)
    {
        var entity = Model.GetEntityTypes().Single(x => x.GetTableName() == table);
        var row = Row(entity.ClrType, tenant);
        configure?.Invoke(row);
        var properties = entity.GetProperties().Where(p => p.ValueGenerated != ValueGenerated.OnAddOrUpdate).ToArray();
        var store = StoreObjectIdentifier.Table(table, "erp_integration");
        await using var command = new NpgsqlCommand("INSERT INTO erp_integration.\"" + table + "\" ("
            + string.Join(",", properties.Select(p => "\"" + p.GetColumnName(store) + "\"")) + ") VALUES ("
            + string.Join(",", properties.Select((_, i) => "@p" + i)) + ")", connection);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, "p" + i, property.PropertyInfo!.GetValue(row)));
        }
        return await command.ExecuteNonQueryAsync();
    }
}

[Collection("ErpTenantPostgres")]
public sealed class ErpTenantPostgresTests(ErpTenantPostgresFixture postgres)
{
    private static string Table(string table) => "erp_integration.\"" + table + "\"";
    private static string Probe(Guid tenant) => "t_" + tenant.ToString("N") + ".component_probe";
    private static bool Audit(string table) => table is "DeliveryReplayAudit" or "InboxReplayAudit";
    private static TenantLoginV1[] Logins => [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")];
    private TenantStorageCatalog Catalog(string model = ErpTenantTransaction.ModelVersion, string revision = "r1", string? queue = "erp_integration") =>
        new("main", [model], new[] { TenantStorageContext.A, TenantStorageContext.B }.Select(tenant =>
            new TenantStorageBindingV1(tenant, "main", "db", TenantStorageBindingV1.SupportedLayout, model, revision, "v1",
                tenant == TenantStorageContext.A ? "principal-a" : "principal-b", queue is null ? [] : [queue],
                TenantStorageState.Ready, ErpTenantTransaction.MigrationVersion)),
            [new("db", "127.0.0.1", TenantStoragePostgresFixture.Port, TenantStoragePostgresFixture.Database, SslMode.Disable)],
            [new("principal-a", "tenant_a"), new("principal-b", "tenant_b")]);
    private TenantConnectionManager Manager(TenantStorageContext context, TenantStorageCatalog? catalog = null) => new(
        "main", context.Accessor, catalog ?? Catalog(), new TenantStorageCredentials
        { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == TenantStorageContext.A ? postgres.Server.PasswordA : postgres.Server.PasswordB) },
        new(2, 2, 1));

    [TenantStoragePostgresFact]
    public async Task Ordinary_logins_read_only_own_rows_in_all_eight_actual_tables()
    {
        foreach (var (role, password) in new[] { ("tenant_a", postgres.Server.PasswordA), ("tenant_b", postgres.Server.PasswordB) })
        {
            await using var connection = await TenantStoragePostgresFixture.OpenRawAsync(role, password);
            foreach (var table in ErpTenantPostgresFixture.Tables)
                Assert.Equal(1L, await TenantStoragePostgresFixture.Scalar(connection, $"SELECT count(*) FROM erp_integration.\"{table}\""));
        }
    }

    [TenantStoragePostgresFact]
    public async Task Four_DML_actions_keep_old_and_new_rows_isolated_and_audits_append_only_for_both_logins()
    {
        foreach (var (tenant, role, password) in new[] { (TenantStorageContext.A, "tenant_a", postgres.Server.PasswordA),
                     (TenantStorageContext.B, "tenant_b", postgres.Server.PasswordB) })
        {
            var other = tenant == TenantStorageContext.A ? TenantStorageContext.B : TenantStorageContext.A;
            await using var connection = await TenantStoragePostgresFixture.OpenRawAsync(role, password);
            foreach (var table in ErpTenantPostgresFixture.Tables)
            {
                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    Assert.Equal(1, await postgres.Insert(connection, table, tenant));
                    Assert.Equal(2L, await TenantStoragePostgresFixture.Scalar(connection, $"SELECT count(*) FROM {Table(table)}"));
                    if (!Audit(table))
                    {
                        // No WHERE or old-column read: UPDATE's USING must still exclude the other tenant.
                        Assert.Equal(2, await TenantStoragePostgresFixture.Sql(connection, $"UPDATE {Table(table)} SET \"TenantId\" = '{tenant}'"));
                    }
                    await transaction.RollbackAsync();
                }
                await Denied(connection, () => postgres.Insert(connection, table, other));
                if (Audit(table))
                {
                    await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"UPDATE {Table(table)} SET \"TenantId\" = '{tenant}'"));
                }
                else await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"UPDATE {Table(table)} SET \"TenantId\" = '{other}'"));
                // No registered ERP runtime writer deletes rows; retention remains control-owned.
                await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"DELETE FROM {Table(table)}"));
                await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, $"TRUNCATE {Table(table)}"));
            }
        }
        await using var owner = await postgres.Server.OpenOwnerAsync();
        foreach (var table in ErpTenantPostgresFixture.Tables)
            Assert.Equal(2L, await TenantStoragePostgresFixture.Scalar(owner, $"SELECT count(*) FROM {Table(table)}"));
    }

    [TenantStoragePostgresFact]
    public async Task Protected_helper_ignores_GUC_and_denies_mapping_role_DDL_and_TEMP_attacks()
    {
        foreach (var (tenant, role, password, otherRole) in new[] { (TenantStorageContext.A, "tenant_a", postgres.Server.PasswordA, "tenant_b"),
                     (TenantStorageContext.B, "tenant_b", postgres.Server.PasswordB, "tenant_a") })
        {
            await using var connection = await TenantStoragePostgresFixture.OpenRawAsync(role, password);
            await TenantStoragePostgresFixture.Sql(connection, $"SET app.tenant_id = '{Guid.NewGuid()}'");
            Assert.Equal(tenant, await TenantStoragePostgresFixture.Scalar(connection, "SELECT cp6_tenancy_v1.current_tenant_id()"));
            foreach (var attack in new[] {
                "SELECT * FROM cp6_tenancy_v1.login_tenant",
                "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id = '11111111-1111-4111-8111-111111111111'",
                "SET ROLE cp6_tenant_lookup_v1", "SET ROLE " + otherRole,
                "CREATE SCHEMA unauthorized", "CREATE TEMP TABLE unauthorized(id int)",
                "CREATE TABLE public.unauthorized(id int)", "ALTER FUNCTION cp6_tenancy_v1.current_tenant_id() SECURITY INVOKER"
            }) await Denied(connection, () => TenantStoragePostgresFixture.Sql(connection, attack));
        }
        await using var owner = await postgres.Server.OpenOwnerAsync();
        Assert.Equal(8L, await TenantStoragePostgresFixture.Scalar(owner, "SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='erp_integration' AND c.relrowsecurity AND c.relforcerowsecurity"));
        Assert.Equal(32L, await TenantStoragePostgresFixture.Scalar(owner, "SELECT count(*) FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid=p.polrelid JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='erp_integration'"));
        Assert.Equal(false, await TenantStoragePostgresFixture.Scalar(owner, "SELECT rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1'"));
        await Denied(owner, () => TenantStoragePostgresFixture.Sql(owner, "TRUNCATE cp6_tenancy_v1.login_tenant"));
        await TenantStoragePostgresFixture.Sql(owner, "SET ROLE cp6_tenant_lookup_v1");
        await Denied(owner, () => TenantStoragePostgresFixture.Sql(owner, "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id=tenant_id"));
        await Denied(owner, () => TenantStoragePostgresFixture.Sql(owner, "SELECT * FROM erp_integration.\"CommandInbox\""));
        await TenantStoragePostgresFixture.Sql(owner, "RESET ROLE");
    }

    [TenantStoragePostgresFact]
    public async Task Unknown_and_recreated_login_cannot_inherit_a_stale_mapping_and_install_never_overwrites()
    {
        await using var owner = await postgres.Server.OpenOwnerAsync();
        await using (var transaction = await owner.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ErpTenantRowSecurityV1.InstallAsync(owner, transaction, Logins));
            await transaction.RollbackAsync();
        }
        // An unknown login is granted only the helper entry for this negative proof, never table rights.
        await TenantStoragePostgresFixture.Sql(owner, "CREATE ROLE unmapped_probe LOGIN; GRANT CONNECT ON DATABASE tenant_component TO unmapped_probe; GRANT USAGE ON SCHEMA cp6_tenancy_v1 TO unmapped_probe; GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO unmapped_probe");
        // SET SESSION AUTHORIZATION is control-only: it supplies a genuine unmapped session_user.
        await TenantStoragePostgresFixture.Sql(owner, "SET SESSION AUTHORIZATION unmapped_probe");
        await Denied(owner, () => TenantStoragePostgresFixture.Scalar(owner, "SELECT cp6_tenancy_v1.current_tenant_id()"));
        await TenantStoragePostgresFixture.Sql(owner, "RESET SESSION AUTHORIZATION");
        await TenantStoragePostgresFixture.Sql(owner, "INSERT INTO cp6_tenancy_v1.login_tenant SELECT oid,rolname,'33333333-3333-4333-8333-333333333333' FROM pg_catalog.pg_roles WHERE rolname='unmapped_probe'; DROP OWNED BY unmapped_probe; DROP ROLE unmapped_probe; CREATE ROLE unmapped_probe LOGIN; GRANT USAGE ON SCHEMA cp6_tenancy_v1 TO unmapped_probe; GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO unmapped_probe; SET SESSION AUTHORIZATION unmapped_probe");
        await Denied(owner, () => TenantStoragePostgresFixture.Scalar(owner, "SELECT cp6_tenancy_v1.current_tenant_id()"));
        await TenantStoragePostgresFixture.Sql(owner, "RESET SESSION AUTHORIZATION");
    }

    [TenantStoragePostgresFact]
    public async Task Original_global_message_keys_conflict_without_disclosing_the_other_tenant_row()
    {
        await using var owner = await postgres.Server.OpenOwnerAsync();
        await using var a = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", postgres.Server.PasswordA);
        var trusted = new TenantStorageContext();
        await using var manager = Manager(trusted);
        foreach (var table in new[] { "CommandInbox", "Cp6_OutboxMessage" })
        {
            var message = (string)(await TenantStoragePostgresFixture.Scalar(owner,
                $"SELECT \"MessageId\" FROM {Table(table)} WHERE \"TenantId\" = '{TenantStorageContext.B}'"))!;
            await using var transaction = await a.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => postgres.Insert(a, table, TenantStorageContext.A,
                row => row.GetType().GetProperty("MessageId")!.SetValue(row, message)));
            Assert.Equal("23505", error.SqlState);
            Assert.DoesNotContain(message, error.ToString());
            await transaction.RollbackAsync();
            if (table == "CommandInbox")
            await trusted.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                var safe = await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync((tx, ct) =>
                    tx.ExecuteErpAsync(async (erp, token) =>
                    {
                        var receipt = (ErpInboxReceipt)ErpTenantPostgresFixture.Row(typeof(ErpInboxReceipt), TenantStorageContext.A);
                        receipt.MessageId = message;
                        erp.Add(receipt);
                        return await erp.SaveChangesAsync(token);
                    }, ct)));
                Assert.Equal(TenantStorageFailure.DatabaseRejected, safe.Failure);
                Assert.Equal("23505", safe.SqlState);
                Assert.Null(safe.InnerException);
                Assert.DoesNotContain(message, safe.ToString());
            });
        }
    }

    [TenantStoragePostgresFact]
    public async Task Ef_business_text_operations_preserve_compatibility_with_pg_catalog_only_search_path()
    {
        var context = new TenantStorageContext();
        await using var manager = Manager(context);
        await context.Run(TenantStorageContext.A, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            var text = await lease.InTransactionAsync((transaction, ct) => transaction.ExecuteErpAsync(async (erp, token) =>
            {
                var receipt = await erp.Inbox.SingleAsync(token);
                var oldToken = receipt.RowVersion.ToArray();
                receipt.EventType = "Aa类型 😀  ";
                await erp.SaveChangesAsync(token);
                Assert.False(oldToken.SequenceEqual(receipt.RowVersion));
                return await erp.Inbox.Select(row => new { Upper = row.EventType.ToUpper(), Length = row.EventType.Length, Raw = row.EventType }).SingleAsync(token);
            }, ct));
            Assert.Equal("AA类型 😀  ", text.Upper);
            Assert.Equal("Aa类型 😀  ", text.Raw);
            Assert.Equal(7, text.Length); // Existing SQL-compatible UTF-16 length excludes trailing spaces.
        });
    }

    [TenantStoragePostgresFact]
    public async Task Ef_uses_actual_owner_connection_transaction_and_atomic_cross_schema_commit_or_rollback()
    {
        var context = new TenantStorageContext();
        await using var manager = Manager(context);
        foreach (var tenant in new[] { TenantStorageContext.A, TenantStorageContext.B })
        await context.Run(tenant, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            foreach (var commit in new[] { false, true })
            {
                var key = Guid.NewGuid();
                var probeId = commit ? 902 : 901;
                ErpIntegrationContext? retained = null;
                await lease.InTransactionAsync(async (transaction, ct) =>
                {
                    DbConnection? physical = null; DbTransaction? actualTransaction = null;
                    await transaction.ExecuteAsync(async (connection, dbTransaction, token) =>
                    {
                        physical = connection; actualTransaction = dbTransaction;
                        return await TenantStoragePostgresFixture.Sql(connection, $"INSERT INTO {Probe(tenant)} VALUES ({probeId}, 'erp-atomic')", token);
                    }, ct);
                    await transaction.ExecuteErpAsync(async (erp, token) =>
                    {
                        retained = erp;
                        Assert.Same(physical, erp.Database.GetDbConnection());
                        Assert.Same(actualTransaction, erp.Database.CurrentTransaction!.GetDbTransaction());
                        Assert.False(erp.Database.CreateExecutionStrategy().RetriesOnFailure);
                        Assert.All(await erp.Aggregates.AsNoTracking().ToListAsync(token), row => Assert.Equal(tenant, row.TenantId));
                        var row = (ErpIntegrationAggregate)ErpTenantPostgresFixture.Row(typeof(ErpIntegrationAggregate), tenant);
                        row.AggregateId = key;
                        erp.Add(row);
                        await erp.SaveChangesAsync(token);
                        Assert.Equal(8, row.RowVersion.Length);
                        return 0;
                    }, ct);
                    Assert.Throws<ObjectDisposedException>(() => retained!.Aggregates.ToList());
                    if (commit) await transaction.CommitAsync(ct);
                    return 0;
                });
                await using var owner = await postgres.Server.OpenOwnerAsync();
                Assert.Equal(commit ? 1L : 0L, await TenantStoragePostgresFixture.Scalar(owner, $"SELECT count(*) FROM {Probe(tenant)} WHERE id={probeId}"));
                Assert.Equal(commit ? 1L : 0L, await TenantStoragePostgresFixture.Scalar(owner, $"SELECT count(*) FROM erp_integration.\"Aggregate\" WHERE \"AggregateId\"='{key}'"));
                if (commit) await TenantStoragePostgresFixture.Sql(owner, $"DELETE FROM {Probe(tenant)} WHERE id={probeId}; DELETE FROM erp_integration.\"Aggregate\" WHERE \"AggregateId\"='{key}'");
            }
        });
    }

    [TenantStoragePostgresFact]
    public async Task Ef_rechecks_nested_scope_binding_rotation_and_database_mapping_before_each_command()
    {
        var context = new TenantStorageContext();
        await using var manager = Manager(context);
        await context.Run(TenantStorageContext.A, async _ =>
        {
            await using var lease = await manager.OpenCurrentAsync();
            await lease.InTransactionAsync(async (transaction, ct) =>
            {
                await transaction.ExecuteErpAsync(async (erp, token) =>
                {
                    Assert.Single(await erp.Aggregates.AsNoTracking().ToListAsync(token));
                    await context.Run(TenantStorageContext.B, async _ =>
                    {
                        Assert.Equal(TenantStorageFailure.ContextChanged, (await Assert.ThrowsAsync<TenantStorageException>(() => erp.Aggregates.CountAsync(token))).Failure);
                        Assert.Equal(TenantStorageFailure.ContextChanged, Assert.Throws<TenantStorageException>(() => erp.Aggregates.Count()).Failure);
                    });
                    Assert.Single(await erp.Aggregates.AsNoTracking().ToListAsync(token));
                    manager.ReplaceCatalog(Catalog(revision: "r2"));
                    Assert.Equal(TenantStorageFailure.BindingChanged, (await Assert.ThrowsAsync<TenantStorageException>(() => erp.Aggregates.CountAsync(token))).Failure);
                    return 0;
                }, ct);
                return 0;
            }).ContinueWith(task =>
            {
                Assert.True(task.IsFaulted);
                Assert.Equal(TenantStorageFailure.BindingChanged, Assert.IsType<TenantStorageException>(task.Exception!.InnerException).Failure);
            }, TaskScheduler.Default);
        });

        foreach (var drift in new[] { "tenant", "missing" })
        {
            await using var owner = await postgres.Server.OpenOwnerAsync();
            try
            {
                await TenantStoragePostgresFixture.Sql(owner, drift == "tenant"
                    ? "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'"
                    : "DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='tenant_a'");
                await context.Run(TenantStorageContext.A, async _ =>
                {
                    await using var lease = await manager.OpenCurrentAsync();
                    var error = await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync((transaction, ct) =>
                        transaction.ExecuteErpAsync((erp, token) => erp.Database.ExecuteSqlRawAsync(
                            "INSERT INTO t_11111111111141118111111111111111.component_probe VALUES (903,'must-not-run')", token), ct)));
                    Assert.Equal(drift == "tenant" ? TenantStorageFailure.IdentityRejected : TenantStorageFailure.DatabaseRejected, error.Failure);
                    Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(owner, $"SELECT count(*) FROM {Probe(TenantStorageContext.A)} WHERE id=903"));
                });
            }
            finally
            {
                await TenantStoragePostgresFixture.Sql(owner, $"DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='tenant_a'; INSERT INTO cp6_tenancy_v1.login_tenant SELECT oid,rolname,'{TenantStorageContext.A}' FROM pg_catalog.pg_roles WHERE rolname='tenant_a'");
            }
        }
    }

    [TenantStoragePostgresFact]
    public async Task Directory_mapping_is_pinned_until_transaction_end_and_waiting_helpers_read_the_new_binding()
    {
        var context = new TenantStorageContext();
        await using var manager = Manager(context);
        await using var writer = await postgres.Server.OpenOwnerAsync();
        await using var observer = await postgres.Server.OpenOwnerAsync();
        const string drift = "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'";
        var restore = $"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{TenantStorageContext.A}' WHERE role_name='tenant_a'";
        Task<int>? write = null;
        try
        {
            await context.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                await lease.InTransactionAsync((transaction, ct) => transaction.ExecuteErpAsync(async (erp, token) =>
                {
                    Assert.Equal(TenantStorageContext.A, (await erp.Aggregates.AsNoTracking().SingleAsync(token)).TenantId);
                    write = TenantStoragePostgresFixture.Sql(writer, drift);
                    await WaitForAdvisory(observer, writer.ProcessID, "ExclusiveLock");
                    Assert.False(write.IsCompleted);
                    Assert.Equal(TenantStorageContext.A, (await erp.Aggregates.AsNoTracking().SingleAsync(token)).TenantId);
                    return 0; // Ending the Owner's transaction releases the directory shared lock.
                }, ct));
                Assert.Equal(1, await write!);
                Assert.Equal(TenantStorageFailure.IdentityRejected, (await Assert.ThrowsAsync<TenantStorageException>(() =>
                    lease.InTransactionAsync((transaction, ct) => transaction.ExecuteErpAsync(
                        (erp, token) => erp.Aggregates.CountAsync(token), ct)))).Failure);
            });
        }
        finally
        {
            if (write is not null) await write;
            await TenantStoragePostgresFixture.Sql(writer, restore);
        }

        // The reverse order proves VOLATILE reads after waiting, rather than retaining the pre-wait snapshot.
        try
        {
            await using var control = await writer.BeginTransactionAsync();
            await TenantStoragePostgresFixture.Sql(writer, drift);
            await context.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                int backend = 0;
                var query = lease.InTransactionAsync(async (transaction, ct) =>
                {
                    await transaction.ExecuteAsync((connection, _, _) => { backend = connection.ProcessID; return Task.FromResult(0); }, ct);
                    return await transaction.ExecuteErpAsync((erp, token) => erp.Aggregates.CountAsync(token), ct);
                });
                for (var attempt = 0; backend == 0 && attempt < 80; attempt++) await Task.Delay(25);
                try
                {
                    await WaitForAdvisory(observer, backend, "ShareLock");
                    Assert.False(query.IsCompleted);
                }
                finally { await control.CommitAsync(); }
                Assert.Equal(TenantStorageFailure.IdentityRejected, (await Assert.ThrowsAsync<TenantStorageException>(() => query)).Failure);
            });
        }
        finally { await TenantStoragePostgresFixture.Sql(writer, restore); }
    }

    private static async Task WaitForAdvisory(NpgsqlConnection observer, int backend, string mode)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (Equals(1L, await TenantStoragePostgresFixture.Scalar(observer,
                $"SELECT count(*) FROM pg_catalog.pg_locks WHERE pid={backend} AND locktype='advisory' AND mode='{mode}' AND NOT granted"))) return;
            await Task.Delay(25);
        }
        Assert.Fail("Expected the directory advisory lock to be waiting on the observed backend.");
    }

    [TenantStoragePostgresFact]
    public async Task Ended_scope_and_unregistered_ERP_profile_never_execute_an_EF_business_command()
    {
        var context = new TenantStorageContext();
        foreach (var catalog in new[] { Catalog(model: "other-model"), Catalog(queue: null) })
        {
            await using var manager = Manager(context, catalog);
            await context.Run(TenantStorageContext.A, async _ =>
            {
                await using var lease = await manager.OpenCurrentAsync();
                var called = false;
                var error = await Assert.ThrowsAsync<TenantStorageException>(() => lease.InTransactionAsync((transaction, ct) =>
                    transaction.ExecuteErpAsync((_, _) => { called = true; return Task.FromResult(0); }, ct)));
                Assert.Equal(TenantStorageFailure.BindingRejected, error.Failure);
                Assert.False(called);
            });
        }
        await using var good = Manager(context);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        TenantConnectionLease? held = null;
        await context.Run(TenantStorageContext.A, async _ =>
        {
            held = await good.OpenCurrentAsync();
            pending = held.InTransactionAsync((transaction, ct) => transaction.ExecuteErpAsync(async (erp, token) =>
            {
                Assert.Single(await erp.Aggregates.AsNoTracking().ToListAsync(token));
                entered.SetResult();
                await resume.Task;
                return await erp.Database.ExecuteSqlRawAsync("INSERT INTO t_11111111111141118111111111111111.component_probe VALUES (904,'ended')", token);
            }, ct));
            await entered.Task;
        });
        resume.SetResult();
        Assert.Equal(TenantStorageFailure.ContextChanged, (await Assert.ThrowsAsync<TenantStorageException>(() => pending!)).Failure);
        await held!.DisposeAsync();
        await using var owner = await postgres.Server.OpenOwnerAsync();
        Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(owner, $"SELECT count(*) FROM {Probe(TenantStorageContext.A)} WHERE id=904"));
    }

    private static async Task Denied(NpgsqlConnection connection, Func<Task> action)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(action)).SqlState);
        await transaction.RollbackAsync();
    }
}
