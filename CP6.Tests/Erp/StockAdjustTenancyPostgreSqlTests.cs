using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_V1_adjustment_uses_current_authority_and_the_ordinary_postgres_transaction()
    {
        await using var h = await Seed();
        await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        var stock = await h.Db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == "P1");
        var mapping = await MappingAdmin(h);
        await BindStandalone(h, mapping, "stock", stock.Id);
        await StandaloneScope(h, mapping, 3);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(g => g.AreaCd, "A"));

        var ownerConnection = h.Db.Database.GetConnectionString()!;
        await using var control = new NpgsqlConnection(ownerConnection);
        await control.OpenAsync();
        const string login = "stock_adjust_a";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"CREATE ROLE {login} LOGIN PASSWORD '{password}'");
        TenantLoginV1[] logins = [new(h.Tenant, login)];
        Guid installation;
        await using (var install = await control.BeginTransactionAsync())
        {
            installation = await StockReadStorageV1.InstallAsync(control, install, logins);
            await StockWriteAuthorityFenceV1.InstallAsync(control, install, installation, logins);
            await StockOriginalJournalStorageV1.InstallAsync(control, install, installation, logins);
            await StockOutboxWriterStorageV1.InstallAsync(control, install, installation, logins);
            await StockMovementWriterStorageV1.InstallAsync(control, install, installation, logins);
            await install.CommitAsync();
        }

        var settings = StockReadSettings();
        settings["StockReadTenancy__EnableV1Adjust"] = "true";
        ConfigureStockReader(settings, 0, h, login, password);
        StockApplyCommandV1 Command(string product = "P1", string location = "L1", decimal quantity = 1.12345678m) => new()
        {
            OperationId = Guid.NewGuid(), TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = location,
            ProductCd = product, LotNo = "LOT", Qty = quantity, UnitPrice = 3m, OperatorCd = "forged"
        };
        const string path = "api/wms/stock/commands/v1/apply";
        var command = Command();
        await using (var unready = await AuthenticationApiProcess.Start(ownerConnection, settings))
        {
            var session = await Login(unready, h);
            using var missing = await unready.Send(OperationsRequest(session, HttpMethod.Post, path, command));
            Assert.True(missing.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(missing));
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await missing.Content.ReadAsStringAsync());
        }
        Assert.Equal(stock.PhysicalQty, await Quantity());
        Assert.Empty(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync());

        await using (var install = await control.BeginTransactionAsync())
        {
            await StockAdjustOwnerStorageV1.InstallAsync(control, install, installation, logins);
            await install.CommitAsync();
        }
        Assert.Equal(false, await CurrentPermissionPostgresFixture.Scalar(control, """
            SELECT has_table_privilege('stock_adjust_a','public."T_WorkOrder"','MAINTAIN,SELECT,UPDATE')
            """));

        await using var host = await AuthenticationApiProcess.Start(ownerConnection, settings);
        var cookies = await Login(host, h);
        Task<HttpResponseMessage> Adjust(StockApplyCommandV1 input, bool csrf = true) =>
            host.Send(OperationsRequest(cookies, HttpMethod.Post, path, input, csrf));
        using (var csrf = await Adjust(command, csrf: false)) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var foreignDepartment = await Adjust(Command("P2", "L2")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreignDepartment.StatusCode);
            Assert.Contains("OBJECT_FORBIDDEN", await foreignDepartment.Content.ReadAsStringAsync());
        }
        using (var insufficient = await Adjust(Command(quantity: -100m)))
            Assert.Equal(HttpStatusCode.BadRequest, insufficient.StatusCode);

        string originalResult;
        await using (var legacyWriter = await control.BeginTransactionAsync())
        {
            await using var table = new NpgsqlCommand("""LOCK TABLE public."T_Stock" IN ROW EXCLUSIVE MODE""", control, legacyWriter);
            await table.ExecuteNonQueryAsync();
            var pending = Adjust(command);
            await WaitForWriter(control.ProcessID);
            await legacyWriter.RollbackAsync();
            using var committed = await pending;
            Assert.True(committed.StatusCode == HttpStatusCode.OK, await OperationsFailure(committed));
            originalResult = await committed.Content.ReadAsStringAsync();
        }
        var txn = await h.Db.StockTransactions.AsNoTracking().SingleAsync();
        using (var result = JsonDocument.Parse(originalResult))
            Assert.Equal(txn.TxnNo, result.RootElement.GetProperty("data").GetProperty("txnNo").GetString());
        Assert.Equal("price-owner", txn.OperatorCd);
        Assert.Equal(command.Qty, txn.Qty);
        Assert.Equal(stock.PhysicalQty + command.Qty, await Quantity());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_stock_outbox_v1."Source"
            WHERE "TenantId"={h.Tenant} AND "OperationId"={command.OperationId}
            """).SingleAsync());

        await CurrentPermissionPostgresFixture.Sql(control, """
            REVOKE UPDATE ("PhysicalQty") ON public."T_Stock" FROM stock_adjust_a
            """);
        using (var noStockWrite = await Adjust(Command()))
            Assert.True(noStockWrite.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(noStockWrite));
        await CurrentPermissionPostgresFixture.Sql(control, """
            GRANT UPDATE ("PhysicalQty") ON public."T_Stock" TO stock_adjust_a
            """);

        await using (var revoke = await h.Db.Database.BeginTransactionAsync())
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 3 });
            await h.Db.SaveChangesAsync();
            var pending = Adjust(Command());
            await WaitForWriter(((NpgsqlConnection)h.Db.Database.GetDbConnection()).ProcessID);
            await revoke.CommitAsync();
            using var denied = await pending;
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
            Assert.Contains("FIELD_WRITE_FORBIDDEN", await denied.Content.ReadAsStringAsync());
        }
        using (var replay = await Adjust(command))
        {
            Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
            Assert.Equal(originalResult, await replay.Content.ReadAsStringAsync());
        }
        Assert.Equal(stock.PhysicalQty + command.Qty, await Quantity());
        Assert.Equal(1, await h.Db.StockTransactions.CountAsync());

        Task<decimal> Quantity() => h.Db.Stocks.Where(s => s.Id == stock.Id).Select(s => s.PhysicalQty).SingleAsync();
        async Task WaitForWriter(int blocker)
        {
            await using var observer = new NpgsqlConnection(ownerConnection);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await observer.OpenAsync(deadline.Token);
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT FROM pg_stat_activity WHERE usename=@login AND datname=current_database()
                    AND query LIKE '%cp6_stock_adjust_owner_v1.lock_graph%' AND @blocker=ANY(pg_blocking_pids(pid)))
                """, observer);
            waiting.Parameters.AddWithValue("login", login);
            waiting.Parameters.AddWithValue("blocker", blocker);
            while (await waiting.ExecuteScalarAsync(deadline.Token) is not true)
                await Task.Delay(40, deadline.Token);
        }
    }
}
