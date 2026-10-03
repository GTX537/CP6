using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class SharedContextGates
{
    public static async Task<string> TransactionAsync(RuntimeFixture fixture)
    {
        await fixture.VerifyOwnerAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = budget.Token;
        var name = "cp6_wp3_shared_" + Guid.NewGuid().ToString("N");
        var table = (fixture.IsPostgreSql ? "public." : "dbo.") + fixture.Column(name);
        var id = fixture.Column("Id");
        var created = false;
        try
        {
            await using (var setup = fixture.Connection())
            {
                await setup.OpenAsync(cancellationToken);
                await ExecuteAsync(setup, null, $"CREATE TABLE {table} ({id} int NOT NULL PRIMARY KEY)", cancellationToken);
                created = true;
            }

            await using var caller = fixture.Core();
            await caller.Database.OpenConnectionAsync(cancellationToken);
            var connection = caller.Database.GetDbConnection();
            await using (var transaction = await caller.Database.BeginTransactionAsync(cancellationToken))
            {
                var physicalTransaction = transaction.GetDbTransaction();
                await ExecuteAsync(connection, physicalTransaction, $"INSERT INTO {table} ({id}) VALUES (1)", cancellationToken);
                var expectedCount = 1;

                await using (var ordinary = await DatabaseSharedContext.CreateAsync<CP6Context>(caller,
                    DatabaseContextKind.Core, options => new(options), cancellationToken))
                {
                    await AssertAndWriteAsync(ordinary, fixture, DatabaseContextKind.Core, connection, physicalTransaction,
                        table, id, ++expectedCount, cancellationToken);
                }
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);

                await using (var priority = await DatabaseSharedContext.CreateAsync<IdentityMessagingContext>(caller,
                    DatabaseContextKind.IdentityPriority, options => new(options), cancellationToken))
                {
                    await AssertAndWriteAsync(priority, fixture, DatabaseContextKind.IdentityPriority, connection,
                        physicalTransaction, table, id, ++expectedCount, cancellationToken);
                }
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);

                await using (var queue = await DatabaseSharedContext.CreateAsync<ErpIntegrationContext>(caller,
                    DatabaseContextKind.ErpIntegration, options => new(options), cancellationToken))
                {
                    await AssertAndWriteAsync(queue, fixture, DatabaseContextKind.ErpIntegration, connection,
                        physicalTransaction, table, id, ++expectedCount, cancellationToken);
                    await using (var business = await DatabaseSharedContext.CreateAsync<CP6Context>(queue,
                        DatabaseContextKind.Core, options => new(options, new TenantContext()), cancellationToken))
                    {
                        await AssertAndWriteAsync(business, fixture, DatabaseContextKind.Core, connection,
                            physicalTransaction, table, id, ++expectedCount, cancellationToken);
                    }
                    await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);
                }
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);

                await using (var space = await DatabaseSharedContext.CreateAsync<SpaceContext>(caller,
                    DatabaseContextKind.Space, options => new(options, new ExecutionContext(), new SystemSpaceClock()), cancellationToken))
                {
                    await AssertAndWriteAsync(space, fixture, DatabaseContextKind.Space, connection,
                        physicalTransaction, table, id, ++expectedCount, cancellationToken);
                    await using (var runtime = await DatabaseSharedContext.CreateAsync<CP6Context>(space,
                        DatabaseContextKind.Core, options => new(options, new TenantContext()), cancellationToken))
                    {
                        await AssertAndWriteAsync(runtime, fixture, DatabaseContextKind.Core, connection,
                            physicalTransaction, table, id, ++expectedCount, cancellationToken);
                    }
                    await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);
                }
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);

                await using (var external = await DatabaseSharedContext.CreateAsync<CP6Context>(fixture.Database, connection,
                    physicalTransaction, DatabaseContextKind.Core, options => new(options), cancellationToken))
                {
                    await AssertAndWriteAsync(external, fixture, DatabaseContextKind.Core, connection,
                        physicalTransaction, table, id, ++expectedCount, cancellationToken);
                }
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);
                ProbeAssert.Require(expectedCount == 8, "All seven child contexts must write inside the caller's one transaction.");
                await RequireFailedChildDisposedAsync(caller, fixture, cancellationToken);
                await RequireParentUsableAsync(connection, physicalTransaction, table, expectedCount, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
            }

            ProbeAssert.Require(connection.State == ConnectionState.Open,
                "Disposing the caller transaction after rollback must leave its explicitly opened connection usable.");
            await using var verify = fixture.Connection();
            await verify.OpenAsync(cancellationToken);
            ProbeAssert.Require(await ScalarAsync(verify, null, $"SELECT COUNT(*) FROM {table}", cancellationToken) == 0,
                "A fresh physical session must observe no parent or child rows after full caller rollback.");
        }
        finally
        {
            if (created)
            {
                await using var cleanup = fixture.Connection();
                await cleanup.OpenAsync(CancellationToken.None);
                await ExecuteAsync(cleanup, null, $"DROP TABLE {table}", CancellationToken.None);
                await using var absence = cleanup.CreateCommand();
                absence.CommandText = fixture.IsPostgreSql
                    ? "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename=@name"
                    : "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name=@name";
                var parameter = absence.CreateParameter();
                parameter.ParameterName = "@name";
                parameter.Value = name;
                absence.Parameters.Add(parameter);
                ProbeAssert.Require(Convert.ToInt32(await absence.ExecuteScalarAsync()) == 0,
                    "The exact task-generated shared-context fixture table must be absent after cleanup.");
            }
        }
        return "Seven child contexts: exact configured provider/target history, same physical connection/transaction, non-owning disposal, failed child disposal preserves parent, full rollback and fixture absence; business SQL compatibility not exercised.";
    }

    private static async Task RequireFailedChildDisposedAsync(CP6Context caller, RuntimeFixture fixture,
        CancellationToken cancellationToken)
    {
        CP6Context? incorrect = null;
        try
        {
            try
            {
                await using var unexpected = await DatabaseSharedContext.CreateAsync<CP6Context>(caller,
                    DatabaseContextKind.Core, _ => incorrect = fixture.Core(), cancellationToken);
                throw new ProbeAssertionException("A child factory selecting another physical connection must be rejected.");
            }
            catch (InvalidOperationException) { }
            ProbeAssert.Require(incorrect is not null, "The ordinary factory failure must occur after child construction.");
            try
            {
                _ = incorrect!.Model;
                throw new ProbeAssertionException("A constructed child context rejected by the factory must already be disposed.");
            }
            catch (ObjectDisposedException) { }
        }
        finally
        {
            if (incorrect is not null) await incorrect.DisposeAsync();
        }
    }

    private static async Task AssertAndWriteAsync(DbContext child, RuntimeFixture fixture, DatabaseContextKind target,
        DbConnection connection, DbTransaction transaction, string table, string id, int rowId, CancellationToken cancellationToken)
    {
        ProbeAssert.Require(child.Database.ProviderName == (fixture.IsPostgreSql
            ? "Npgsql.EntityFrameworkCore.PostgreSQL" : "Microsoft.EntityFrameworkCore.SqlServer"),
            "A child context must use the caller's explicitly selected EF provider.");
        ProbeAssert.Require(ReferenceEquals(connection, child.Database.GetDbConnection()) &&
            ReferenceEquals(transaction, child.Database.CurrentTransaction?.GetDbTransaction()),
            "Child contexts must share the exact caller connection and physical transaction objects.");
        var relational = RelationalOptionsExtension.Extract(child.GetService<IDbContextOptions>());
        var profile = DatabaseMigrationProfile.For(fixture.Database, target);
        ProbeAssert.Require(!relational.IsConnectionOwned && relational.MigrationsAssembly == profile.MigrationsAssembly &&
            relational.MigrationsHistoryTableName == profile.HistoryTable && relational.MigrationsHistoryTableSchema == profile.HistorySchema,
            "The child must use non-owning connection options and its exact target migration profile.");
        // Table/column are the internally generated fixture identifiers; values use EF parameters.
        var insertSql = $"INSERT INTO {table} ({id}) VALUES ({{0}})";
        await child.Database.ExecuteSqlRawAsync(insertSql, new object[] { rowId }, cancellationToken);
    }

    private static async Task RequireParentUsableAsync(DbConnection connection, DbTransaction transaction,
        string table, int count, CancellationToken cancellationToken)
    {
        ProbeAssert.Require(connection.State == ConnectionState.Open && ReferenceEquals(transaction.Connection, connection),
            "Child disposal must leave the caller's connection and physical transaction open.");
        ProbeAssert.Require(await ScalarAsync(connection, transaction, $"SELECT COUNT(*) FROM {table}", cancellationToken) == count,
            "The caller must still read all shared writes using the same active transaction after child disposal.");
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 15;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 15;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed class ExecutionContext : ISpaceExecutionContext
    {
        public Guid TenantId => TenantContext.DefaultTenant;
        public Guid ActorId => Guid.Parse("6481ce94-87da-4c23-bc9f-d19986b20f9e");
    }
}
