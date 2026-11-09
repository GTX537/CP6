using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("StockOriginalJournalPostgres", DisableParallelization = true)]
public sealed class StockOriginalJournalPostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

[Collection("StockOriginalJournalPostgres")]
public sealed class StockOriginalJournalPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_stock_journal_preserves_original_keys_and_rollback_without_exposing_other_owners()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var closed = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        const string loginA = "stock_journal_a", loginB = "stock_journal_b";
        const string stock = StockOutboxProtocolV1.SourceOwner, contract = StockOutboxProtocolV1.SourceContract;
        const string unrelated = "Main.Test.Unrelated";
        var secretA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var secretB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var input = MainOwnerOperationJournalV1.Encode(new { qty = 1.23456789m });
        var result = new { operation, objectId };
        var audit = new { qty = 1.23456789m };

        await using var controlDb = server.Context(new TenantContext { CurrentTenantId = tenantA });
        foreach (var tenant in new[] { tenantA, tenantB })
            controlDb.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "stock journal fixture", Enable = true });
        await controlDb.SaveChangesAsync();
        var settings = new NpgsqlConnectionStringBuilder(controlDb.Database.GetConnectionString())
        { SearchPath = "pg_catalog", Pooling = false };
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE ROLE {loginA} LOGIN PASSWORD '{secretA}';
            CREATE ROLE {loginB} LOGIN PASSWORD '{secretB}';
            """);
        TenantLoginV1[] logins = [new(tenantA, loginA), new(tenantB, loginB)];
        Guid installation;
        await using (var tx = await control.BeginTransactionAsync())
        {
            installation = await StockReadStorageV1.InstallAsync(control, tx, logins);
            await tx.CommitAsync();
        }
        await using (var tx = await control.BeginTransactionAsync())
        {
            await StockWriteAuthorityFenceV1.InstallAsync(control, tx, installation, logins);
            await tx.CommitAsync();
        }
        await using (var tx = await controlDb.Database.BeginTransactionAsync())
        {
            await new MainOwnerOperationJournalV1(controlDb).Store(unrelated, "fixture.v1", operation, actor, input, result, [objectId], audit, default);
            await tx.CommitAsync();
        }
        await using (var tx = await control.BeginTransactionAsync())
        {
            await StockOriginalJournalStorageV1.InstallAsync(control, tx, installation, logins);
            await tx.CommitAsync();
        }

        await using var writer = OpenAs(loginA, secretA);
        var journal = new MainOwnerOperationJournalV1(writer, stockTenantWriter: true);
        await using (var tx = await writer.Database.BeginTransactionAsync())
        {
            await Fence(writer);
            Assert.Null(await journal.Read(stock, operation, default));
            await journal.Store(stock, contract, operation, actor, input, result, [objectId], audit, default);
            Assert.Equal(actor, (await journal.Read(stock, operation, default))!.Actor);
            await tx.RollbackAsync();
        }
        await using (var tx = await writer.Database.BeginTransactionAsync())
        {
            await Fence(writer);
            Assert.Null(await journal.Read(stock, operation, default));
            await journal.Store(stock, contract, operation, actor, input, result, [objectId], audit, default);
            await journal.Store(stock, contract, closed, actor, null, new { closed }, [], audit, default);
            await tx.CommitAsync();
        }
        await using (var tx = await writer.Database.BeginTransactionAsync())
        {
            var original = (await journal.Read(stock, operation, default))!;
            Assert.Equal("COMMITTED", original.State);
            Assert.True(MainOwnerOperationJournalV1.Matches(original, input));
            Assert.Equal(MainOwnerOperationJournalV1.Encode(result), original.Result);
            Assert.Equal(new[] { objectId }, original.Objects);
            Assert.Equal("CLOSED", (await journal.Read(stock, closed, default))!.State);
            Assert.Null(await journal.Read(unrelated, operation, default));
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                journal.Store(stock, contract, operation, actor, input, result, [objectId], audit, default));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            await tx.RollbackAsync();
        }
        Assert.Equal("1.23456789", await controlDb.Database.SqlQuery<string>($"""
            SELECT audit_json->>'qty' AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={tenantA} AND owner={stock} AND operation_id={operation}
            """).SingleAsync());

        await using (var tx = await controlDb.Database.BeginTransactionAsync())
        {
            await new MainOwnerOperationJournalV1(controlDb).Store("Main.Wms.LocationRuleWithdrawal.operations.v1",
                "Main.Wms.LocationRuleWithdrawal.commands.v1", Guid.NewGuid(), actor, input,
                new { State = "WITHDRAWN", TransportCommandId = Guid.NewGuid() }, [objectId], audit, default);
            var missingFact = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
            Assert.Equal("LOCATION_RULE_WITHDRAWAL_ATOMIC_FACT_REQUIRED", missingFact.MessageText);
        }

        await using var otherLogin = OpenAs(loginB, secretB);
        await using (var tx = await otherLogin.Database.BeginTransactionAsync())
        {
            // Simulate a caller with the wrong EF tenant against a real session bound to B.
            var other = new MainOwnerOperationJournalV1(otherLogin, stockTenantWriter: true);
            Assert.Null(await other.Read(stock, operation, default));
            var wrongTenant = await Assert.ThrowsAsync<PostgresException>(() =>
                other.Store(stock, contract, Guid.NewGuid(), actor, input, result, [objectId], audit, default));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, wrongTenant.SqlState);
            await tx.RollbackAsync();
        }
        foreach (var sql in new[]
        {
            "SELECT audit_json FROM cp6_main_owner_v1.operations",
            "INSERT INTO cp6_main_owner_v1.operations DEFAULT VALUES",
            "UPDATE cp6_main_owner_v1.operations SET result_bytes=''::bytea"
        })
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                writer.Database.ExecuteSqlRawAsync(sql))).SqlState);

        CP6Context OpenAs(string login, string password)
        {
            var context = server.Context(new TenantContext { CurrentTenantId = tenantA });
            var ordinary = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
            { Username = login, Password = password, Enlist = false, IncludeErrorDetail = false };
            context.Database.SetConnectionString(ordinary.ConnectionString);
            return context;
        }
        Task<int> Fence(CP6Context context) => context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT cp6_stock_write_fence_v1.acquire({tenantA})
            """);
    }
}
