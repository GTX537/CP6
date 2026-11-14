using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("StockTenantMovementPostgres", DisableParallelization = true)]
public sealed class StockTenantMovementPostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

[Collection("StockTenantMovementPostgres")]
public sealed class StockTenantMovementPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_stock_movement_commits_stock_lineage_sequence_journal_and_outbox_as_one_unit()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var department = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        const string login = "stock_movement_a", actorName = "fixture-stock-actor";
        const decimal originalQuantity = 10.87654321m, delta = 2.12345678m;
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var owner = server.Context(new TenantContext { CurrentTenantId = tenantA });
        foreach (var tenant in new[] { tenantA, tenantB })
            owner.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "stock fixture", Enable = true });
        await owner.SaveChangesAsync();
        owner.Sys_Depts.Add(new() { Id = department, DeptCode = "STOCK-A", DeptName = "Stock", Path = $"/{department}/", Enable = true });
        owner.Sys_Users.Add(new() { Id = actor, UserName = actorName, Password = "fixture-only", RoleId = 0, Enable = true });
        owner.Warehouses.Add(new() { WarehouseCd = "W1", WarehouseName = "Warehouse" });
        var stock = NewStock(originalQuantity);
        owner.Stocks.Add(stock);
        await owner.SaveChangesAsync();
        await owner.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{tenantA},'stock',{stock.Id},{department},true)
            """);
        await using var otherOwner = server.Context(new TenantContext { CurrentTenantId = tenantB });
        otherOwner.Stocks.Add(NewStock(70m));
        await otherOwner.SaveChangesAsync();

        var settings = new NpgsqlConnectionStringBuilder(owner.Database.GetConnectionString())
        { SearchPath = "pg_catalog", Pooling = false, Enlist = false, IncludeErrorDetail = false };
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        await CurrentPermissionPostgresFixture.Sql(control, $"""CREATE ROLE {login} LOGIN PASSWORD '{password}'""");
        TenantLoginV1[] logins = [new(tenantA, login)];
        await using (var tx = await control.BeginTransactionAsync())
        {
            var installation = await StockReadStorageV1.InstallAsync(control, tx, logins);
            await StockWriteAuthorityFenceV1.InstallAsync(control, tx, installation, logins);
            await StockOriginalJournalStorageV1.InstallAsync(control, tx, installation, logins);
            await StockOutboxWriterStorageV1.InstallAsync(control, tx, installation, logins);
            await StockMovementWriterStorageV1.InstallAsync(control, tx, installation, logins);
            await tx.CommitAsync();
        }

        Assert.Equal("0,0,0,0,0,0", await Facts());
        await using (var unauthorized = Writer())
        {
            foreach (var command in new[]
            {
                """UPDATE public."T_Stock" SET "PhysicalQty"="PhysicalQty" WHERE "Id"={0}""",
                """INSERT INTO public."T_StockTransaction" DEFAULT VALUES""",
                """UPDATE public."T_WmsSequence" SET "NextNo"=0 WHERE false""",
                """INSERT INTO cp6_stock_return_v1.movement_sequences(tenant_id,pool_id,movement_id) VALUES (DEFAULT,DEFAULT,DEFAULT)"""
            })
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                    unauthorized.Database.ExecuteSqlRawAsync(command, stock.Id));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
                Assert.Equal("STOCK_WRITE_AUTHORITY_REQUIRED", rejected.MessageText);
            }
        }
        await Move(commit: false);
        Assert.Equal(originalQuantity, await Quantity(owner));
        Assert.Equal("0,0,0,0,0,0", await Facts());
        await Move(commit: true);
        Assert.Equal(originalQuantity + delta, await Quantity(owner));
        Assert.Equal(70m, await Quantity(otherOwner));
        Assert.Equal("1,1,1,1,1,1", await Facts());
        Assert.Equal(department, await owner.Database.SqlQuery<Guid>($"""
            SELECT department_id AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={tenantA} AND object_kind='stock-transaction'
            """).SingleAsync());
        Assert.Equal(delta, await owner.StockTransactions.AsNoTracking().Select(t => t.Qty).SingleAsync());

        await owner.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Sys_Tenants" SET "Enable"=false WHERE "Id"={tenantA}
            """);
        await using (var paused = Writer())
        await using (var tx = await paused.Database.BeginTransactionAsync())
        {
            await paused.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({tenantA})");
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => paused.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."T_Stock" SET "PhysicalQty"="PhysicalQty" WHERE "Id"={stock.Id}
                """));
            Assert.Equal("STOCK_WRITE_TENANT_UNAVAILABLE", rejected.MessageText);
            await tx.RollbackAsync();
        }
        await owner.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Sys_Tenants" SET "Enable"=true WHERE "Id"={tenantA}
            """);
        Assert.Equal(originalQuantity + delta, await Quantity(owner));
        Assert.Equal("1,1,1,1,1,1", await Facts());

        owner.StockTakes.Add(new() { StockTakeNo = "FROZEN", TargetWarehouseCd = "W1", Status = StockTakeStatus.Counting });
        owner.StockTakeDetails.Add(new()
        {
            StockTakeNo = "FROZEN", LineNo = 1, StockId = stock.Id, WarehouseCd = "W1",
            LocationCd = "L1", ProductCd = "P1", LotNo = "LOT", BookQty = originalQuantity + delta
        });
        await owner.SaveChangesAsync();
        var frozen = await Assert.ThrowsAsync<InvalidOperationException>(() => Move(commit: false));
        Assert.StartsWith("WM-MSG-304", frozen.Message);
        Assert.Equal("1,1,1,1,1,1", await Facts());

        var environment = Guid.NewGuid();
        await owner.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_stock_move_v1.openings(tenant_id,environment_id,id,body)
            VALUES({tenantA},{environment},'fixture-open',convert_to('fixture','UTF8'));
            INSERT INTO cp6_stock_move_v1.locations(tenant_id,environment_id,id,native_id,opening_id,body)
            VALUES({tenantA},{environment},'fixture-root-location',{Guid.NewGuid()},'fixture-open',convert_to('fixture','UTF8'));
            INSERT INTO cp6_stock_move_v1.writer_bindings(tenant_id,environment_id,location_id,warehouse_code,location_code,product_code)
            VALUES({tenantA},{environment},'fixture-root-location','W1','L1','P1')
            """);
        await using (var probe = Writer())
        {
            var previousMovement = await owner.StockTransactions.AsNoTracking().Select(t => t.Id).SingleAsync();
            var previousAudit = await owner.Sys_SecurityLogs.AsNoTracking().Select(a => a.ContextJson).SingleAsync();
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                probe.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT cp6_stock_movement_writer_v1.record_department({tenantA},{Guid.NewGuid()},{previousMovement},{department})
                    """))).SqlState);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                probe.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT cp6_stock_movement_writer_v1.record_audit({tenantA},{actor},{actorName},
                        LOCALTIMESTAMP,LOCALTIMESTAMP,CAST({previousAudit} AS jsonb))
                    """))).SqlState);
            await using var guarded = await probe.Database.BeginTransactionAsync();
            await probe.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({tenantA})");
            var protectedStock = await Assert.ThrowsAsync<PostgresException>(() => probe.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."T_Stock" SET "PhysicalQty"="PhysicalQty" WHERE "Id"={stock.Id}
                """));
            Assert.Equal("STOCK_ROOT_WRITER_REQUIRED", protectedStock.MessageText);
            await guarded.RollbackAsync();
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                probe.Database.ExecuteSqlRawAsync("SELECT log_id FROM cp6_privacy_security_fields_v1.tombstones"))).SqlState);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                probe.Database.ExecuteSqlRawAsync("SELECT id FROM cp6_location_draft_v1.locations"))).SqlState);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() =>
                probe.Database.ExecuteSqlRawAsync("""INSERT INTO public."Sys_SecurityLogs" DEFAULT VALUES"""))).SqlState);
        }
        Assert.Equal("1,1,1,1,1,1", await Facts());

        async Task Move(bool commit)
        {
            await using var writer = Writer();
            await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({tenantA})");
            var version = await StandaloneDepartmentScopeV1.Version(writer, default);
            var access = new StockTransactionInheritanceV1(writer, actor, actorName, OperationsOwnerV1.Stock, "adjust", version);
            var mover = new StockMovementService(writer, new WmsSequenceService(writer));
            var request = new StockMovementRequest
            {
                TxnType = WmsTxnType.ADJ, WarehouseCd = "W1", LocationCd = "L1", ProductCd = "P1", LotNo = "LOT",
                Qty = delta, UnitPrice = 3.125m, OperatorCd = actorName
            };
            using var inheritance = mover.InheritTransactions(access);
            var txnNo = await mover.ApplyWithinOwnerAsync(request);
            await access.ValidateFinalAsync(default);
            await new MainOwnerOperationJournalV1(writer, stockTenantWriter: true).Store(StockOutboxProtocolV1.SourceOwner,
                StockOutboxProtocolV1.SourceContract, operation, actor, MainOwnerOperationJournalV1.Encode(request),
                new { operation, txnNo }, [stock.Id], new { quantity = delta }, default);
            await StockOutboxEnqueueV1.Enqueue(writer, operation, default, stockTenantWriter: true);
            Assert.Equal("0,0,0,0,0,0", await Facts());
            if (commit) await tx.CommitAsync();
            else await tx.RollbackAsync();
        }

        Task<string> Facts() => owner.Database.SqlQuery<string>($"""
            SELECT concat_ws(',',
              (SELECT count(*) FROM public."T_StockTransaction" WHERE "TenantId"={tenantA}),
              (SELECT count(*) FROM public."T_WmsSequence" WHERE "TenantId"={tenantA} AND "Prefix"='TXN'),
              (SELECT count(*) FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={tenantA}),
              (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenantA} AND "UserId"={actor}),
              (SELECT count(*) FROM cp6_main_owner_v1.operations WHERE tenant_id={tenantA} AND operation_id={operation}),
              (SELECT count(*) FROM cp6_stock_outbox_v1."Source" WHERE "TenantId"={tenantA} AND "OperationId"={operation})) AS "Value"
            """).SingleAsync();
        static Task<decimal> Quantity(CP6Context db) => db.Stocks.AsNoTracking().Select(x => x.PhysicalQty).SingleAsync();
        static Stock NewStock(decimal quantity) => new()
        { WarehouseCd = "W1", LocationCd = "L1", ProductCd = "P1", LotNo = "LOT", PhysicalQty = quantity, AvailableQty = quantity };
        StockMovementWriteContextV1 Writer() => new(new DbContextOptionsBuilder<CP6Context>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Username = login, Password = password }.ConnectionString)
            .Options, tenantA);
    }
}
