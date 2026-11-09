using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_V2_stock_creation_uses_the_original_department_and_audit_transaction()
    {
        await using var h = await Seed();
        var owner = await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        var departments = await MappingAdmin(h);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var control = new NpgsqlConnection(connection);
        await control.OpenAsync();
        const string login = "stock_creation_a";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"CREATE ROLE {login} LOGIN PASSWORD '{password}'");
        TenantLoginV1[] logins = [new(h.Tenant, login)];
        await using (var install = await control.BeginTransactionAsync())
        {
            var read = await StockReadStorageV1.InstallAsync(control, install, logins);
            await StockWriteAuthorityFenceV1.InstallAsync(control, install, read, logins);
            await StockOriginalJournalStorageV1.InstallAsync(control, install, read, logins);
            await StockOutboxWriterStorageV1.InstallAsync(control, install, read, logins);
            await StockMovementWriterStorageV1.InstallAsync(control, install, read, logins);
            await StockCreationWriterStorageV2.InstallAsync(control, install, read, logins);
            await StockAdjustOwnerStorageV1.InstallAsync(control, install, read, logins);
            await install.CommitAsync();
        }
        var ordinary = new NpgsqlConnectionStringBuilder(connection)
        { Username = login, Password = password, SearchPath = "pg_catalog", Pooling = false, Enlist = false };
        var options = new DbContextOptionsBuilder<CP6Context>().UseNpgsql(ordinary.ConnectionString).Options;
        var key = new StockCreationKeyV2("W01", "L3", "NEW-BOUND", "LOT");
        var wrongDepartment = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Create(key, Guid.NewGuid(), commit: false));
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN", wrongDepartment.Message);
        await Create(key, departments.Child, commit: false);
        Assert.False(await h.Db.Stocks.AnyAsync(s => s.ProductCd == key.ProductCd));
        Assert.Equal(0, await AuditCount());

        var id = await Create(key, departments.Child, commit: true, replayAudit: true);
        Assert.Equal(id, await h.Db.Stocks.Where(s => s.ProductCd == key.ProductCd).Select(s => s.Id).SingleAsync());
        Assert.Equal(departments.Child, await h.Db.Database.SqlQuery<Guid>($"""
            SELECT department_id AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='stock' AND object_id={id}
            """).SingleAsync());
        Assert.Equal(1, await AuditCount());

        var audit = await h.Db.Sys_SecurityLogs.AsNoTracking().Where(x => x.UserId == h.User &&
            x.Reason == OperationsAccessV1.StockCreationAuditReason).Select(x => x.ContextJson).SingleAsync();
        await using (var db = new StockMovementWriteContextV1(options, h.Tenant))
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT cp6_stock_creation_writer_v2.record_audit({h.Tenant},{h.User},{"price-owner"},
                    LOCALTIMESTAMP,LOCALTIMESTAMP,CAST({audit} AS jsonb))
                """));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }

        var unbound = await Create(new("W01", "L3", "NEW-UNBOUND", "LOT"), department: null, commit: true);
        Assert.True(await h.Db.Stocks.AnyAsync(s => s.Id == unbound));
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='stock' AND object_id={unbound}
            """).SingleAsync());
        Assert.Equal(1, await AuditCount());

        var historical = await h.Db.Stocks.Where(s => s.ProductCd == "P1").Select(s => s.Id).SingleAsync();
        await using (var db = new StockMovementWriteContextV1(options, h.Tenant))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({h.Tenant})");
            Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."T_Stock" SET "PhysicalQty"="PhysicalQty" WHERE "Id"={historical}
                """));
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT xmin=pg_current_xact_id_if_assigned()::text::xid AS "Value" FROM public."T_Stock" WHERE "Id"={historical}
                """).SingleAsync());
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT cp6_stock_creation_writer_v2.record_department({h.Tenant},{Guid.NewGuid()},{historical},{departments.Child})
                """));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
            Assert.Equal("Stock creation department rejected", rejected.MessageText);
            await tx.RollbackAsync();
        }

        await using (var db = new StockMovementWriteContextV1(options, h.Tenant))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({h.Tenant})");
            await tx.CreateSavepointAsync("before_stock");
            db.Stocks.Add(new() { WarehouseCd = "W01", LocationCd = "L3", ProductCd = "SAVEPOINT-STOCK", LotNo = "LOT" });
            await db.SaveChangesAsync();
            await tx.ReleaseSavepointAsync("before_stock");
            await tx.CommitAsync();
        }
        Assert.True(await h.Db.Stocks.AnyAsync(s => s.ProductCd == "SAVEPOINT-STOCK"));

        h.Db.Stocks.Add(new() { WarehouseCd = "W01", LocationCd = "L3", ProductCd = "LEGACY-STOCK", LotNo = "LOT" });
        await h.Db.SaveChangesAsync();
        Assert.True(await h.Db.Stocks.AnyAsync(s => s.ProductCd == "LEGACY-STOCK"));
        Assert.Equal(0L, await CurrentPermissionPostgresFixture.Scalar(control, "SELECT count(*) FROM cp6_stock_creation_writer_v2.inserted_stock"));
        await VerifyOrdinaryV2StockAdjustmentAsync(h, departments, control, connection, login, password, historical);
        await VerifyOrdinaryV2StockMoveAsync(h, departments, control, connection, login, password, historical);

        Task<int> AuditCount() => h.Db.Sys_SecurityLogs.CountAsync(x => x.UserId == h.User &&
            x.Reason == OperationsAccessV1.StockCreationAuditReason);
        async Task<Guid> Create(StockCreationKeyV2 requested, Guid? department, bool commit, bool replayAudit = false)
        {
            var authority = await owner.AuthorityAsync(OperationsOwnerV1.Stock, "adjust", default);
            await using var db = new StockMovementWriteContextV1(options, h.Tenant);
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT cp6_stock_write_fence_v1.acquire({h.Tenant})");
            var access = new OperationsAccessV1(db, owner, authority);
            var context = await access.StockCreationContext(requested, default);
            Assert.True(context.RequiresCreation);
            Assert.Contains(context.Departments, d => d.Id == departments.Child);
            var ticket = await access.BeginStockCreation(requested, new(context.AuthorityVersion, department), default);
            Assert.NotNull(ticket);
            db.Stocks.Add(new()
            {
                Id = ticket.Id, WarehouseCd = requested.WarehouseCd, LocationCd = requested.LocationCd,
                ProductCd = requested.ProductCd, LotNo = requested.LotNo, Creator = access.Actor
            });
            await db.SaveChangesAsync();
            await access.CompleteStockCreation(ticket, default);
            await access.ValidateFinalAsync(default);
            if (replayAudit)
            {
                await tx.CreateSavepointAsync("after_audit");
                var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT cp6_stock_creation_writer_v2.record_audit({h.Tenant},{h.User},{access.Actor},LOCALTIMESTAMP,LOCALTIMESTAMP,
                        jsonb_build_object('kind','stock-creation.v2','stockId',object_id,'departmentId',department_id,
                            'mappingId',id,'mappingRevision',revision,'resource','wms-stock','action','adjust'))
                    FROM cp6_operations_owner_v1.department_mappings
                    WHERE tenant_id={h.Tenant} AND object_kind='stock' AND object_id={ticket.Id}
                    """));
                Assert.Equal("Stock creation audit rejected", rejected.MessageText);
                await tx.RollbackToSavepointAsync("after_audit");
            }
            if (commit) await tx.CommitAsync();
            else await tx.RollbackAsync();
            return ticket.Id;
        }
    }
}
