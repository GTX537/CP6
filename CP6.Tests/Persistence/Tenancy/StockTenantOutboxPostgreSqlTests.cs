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

[CollectionDefinition("StockTenantOutboxPostgres", DisableParallelization = true)]
public sealed class StockTenantOutboxPostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

[Collection("StockTenantOutboxPostgres")]
public sealed class StockTenantOutboxPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_login_commits_original_and_native_outbox_together_with_the_existing_source_proof()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var historical = Guid.NewGuid();
        var input = MainOwnerOperationJournalV1.Encode(new { qty = 1.125m });
        const string loginA = "stock_queue_a", loginB = "stock_queue_b";
        var passwordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var passwordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await using var controlDb = server.Context(new TenantContext { CurrentTenantId = tenantA });
        foreach (var tenant in new[] { tenantA, tenantB })
            controlDb.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "stock enqueue fixture", Enable = true });
        await controlDb.SaveChangesAsync();
        var settings = new NpgsqlConnectionStringBuilder(controlDb.Database.GetConnectionString())
        { SearchPath = "pg_catalog", Pooling = false, Enlist = false, IncludeErrorDetail = false };
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE ROLE {loginA} LOGIN PASSWORD '{passwordA}';
            CREATE ROLE {loginB} LOGIN PASSWORD '{passwordB}';
            """);
        TenantLoginV1[] logins = [new(tenantA, loginA), new(tenantB, loginB)];
        await using (var tx = await control.BeginTransactionAsync())
        {
            var installation = await StockReadStorageV1.InstallAsync(control, tx, logins);
            await StockWriteAuthorityFenceV1.InstallAsync(control, tx, installation, logins);
            await StockOriginalJournalStorageV1.InstallAsync(control, tx, installation, logins);
            await StockOutboxWriterStorageV1.InstallAsync(control, tx, installation, logins);
            await tx.CommitAsync();
        }

        await using var first = Ordinary(tenantA, loginA, passwordA);
        await using var second = Ordinary(tenantB, loginB, passwordB);
        await using (var tx = await first.Database.BeginTransactionAsync())
        {
            await Append(first, operation);
            await StockOutboxEnqueueV1.Enqueue(first, operation, default, stockTenantWriter: true);
            Assert.Equal("0,0,0", await Counts(tenantA, operation));
            await tx.RollbackAsync();
        }
        Assert.Equal("0,0,0", await Counts(tenantA, operation));

        foreach (var writer in new[] { first, second })
        {
            await using var tx = await writer.Database.BeginTransactionAsync();
            await Append(writer, operation);
            await StockOutboxEnqueueV1.Enqueue(writer, operation, default, stockTenantWriter: true);
            await tx.CommitAsync();
            var tenant = writer.CurrentTenantId;
            Assert.Equal("1,1,1", await Counts(tenant, operation));
            Assert.Equal(StockOutboxProtocolV1.Message(tenant, operation), await writer.Database.SqlQuery<string>($"""
                SELECT "MessageId" AS "Value" FROM cp6_stock_outbox_v1."Cp6_OutboxMessage"
                WHERE "TenantId"={tenant}
                """).SingleAsync());
        }
        Assert.Empty(await first.Database.SqlQuery<string>($"""
            SELECT "MessageId" AS "Value" FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" WHERE "TenantId"={tenantB}
            """).ToArrayAsync());

        await using (var tx = await first.Database.BeginTransactionAsync())
        {
            await Append(first, historical);
            await tx.CommitAsync();
        }
        await using (var tx = await first.Database.BeginTransactionAsync())
        {
            await Fence(first);
            await StockOutboxEnqueueV1.Enqueue(first, historical, default, stockTenantWriter: true);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
            Assert.Equal("STOCK_OUTBOX_SOURCE_PROOF_REQUIRED", rejected.MessageText);
        }
        Assert.Equal("1,0,0", await Counts(tenantA, historical));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
            first.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_stock_outbox_v1."Authority" SET "RootId"="RootId" WHERE "TenantId"={tenantA}
                """))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
            first.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_stock_outbox_v1."Cp6_OutboxMessage" SET "Status"=1 WHERE "TenantId"={tenantA}
                """))).SqlState);

        CP6Context Ordinary(Guid tenant, string login, string password)
        {
            var context = server.Context(new TenantContext { CurrentTenantId = tenant });
            context.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(settings.ConnectionString)
            { Username = login, Password = password }.ConnectionString);
            return context;
        }
        Task<int> Fence(CP6Context context) => context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT cp6_stock_write_fence_v1.acquire({context.CurrentTenantId})
            """);
        async Task Append(CP6Context context, Guid id)
        {
            await Fence(context);
            await new MainOwnerOperationJournalV1(context, stockTenantWriter: true).Store(
                StockOutboxProtocolV1.SourceOwner, StockOutboxProtocolV1.SourceContract, id, actor, input,
                new { operation = id }, [Guid.NewGuid()], new { qty = 1.125m }, default);
        }
        Task<string> Counts(Guid tenant, Guid id) => controlDb.Database.SqlQuery<string>($"""
            SELECT concat_ws(',',
                (SELECT count(*) FROM cp6_main_owner_v1.operations WHERE tenant_id={tenant} AND operation_id={id}),
                (SELECT count(*) FROM cp6_stock_outbox_v1."Source" WHERE "TenantId"={tenant} AND "OperationId"={id}),
                (SELECT count(*) FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" WHERE "TenantId"={tenant}
                  AND "MessageId"={StockOutboxProtocolV1.Message(tenant, id)})) AS "Value"
            """).SingleAsync();
    }
}
