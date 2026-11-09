using System.Net;
using System.Net.Sockets;
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
        Task<HttpResponseMessage> ReadOriginal(Guid operation) => host.Send(
            OperationsRequest(cookies, HttpMethod.Get, $"api/wms/stock/operations/v1/{operation:D}"));
        Task<HttpResponseMessage> CloseOriginal(Guid operation) => host.Send(
            OperationsRequest(cookies, HttpMethod.Post, $"api/wms/stock/operations/v1/{operation:D}/close"));
        using (var csrf = await Adjust(command, csrf: false)) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var foreignDepartment = await Adjust(Command("P2", "L2")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreignDepartment.StatusCode);
            Assert.Contains("OBJECT_FORBIDDEN", await foreignDepartment.Content.ReadAsStringAsync());
        }
        using (var insufficient = await Adjust(Command(quantity: -100m)))
            Assert.Equal(HttpStatusCode.BadRequest, insufficient.StatusCode);

        await using (var legacyWriter = await control.BeginTransactionAsync())
        {
            await using var table = new NpgsqlCommand("""LOCK TABLE public."T_Stock" IN ROW EXCLUSIVE MODE""", control, legacyWriter);
            await table.ExecuteNonQueryAsync();
            using var request = OperationsRequest(cookies, HttpMethod.Post, path, command);
            var pending = DropStockResponseAsync(host.Origin, request);
            await WaitForWriter(control.ProcessID);
            await legacyWriter.RollbackAsync();
            await pending;
        }
        var txn = await h.Db.StockTransactions.AsNoTracking().SingleAsync();
        const string rootRevisionSql = "SELECT revision FROM cp6_platform_admin_guard_v1.root WHERE singleton";
        var rootRevision = await CurrentPermissionPostgresFixture.Scalar(control, rootRevisionSql);
        string originalResult;
        using (var recovered = await ReadOriginal(command.OperationId))
        {
            Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
            using var result = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
            var data = result.RootElement.GetProperty("data");
            Assert.Equal("COMMITTED", data.GetProperty("state").GetString());
            var original = data.GetProperty("result");
            Assert.Equal(txn.TxnNo, original.GetProperty("txnNo").GetString());
            originalResult = original.GetRawText();
        }
        using (var absent = await ReadOriginal(Guid.NewGuid()))
        {
            Assert.True(absent.StatusCode == HttpStatusCode.OK, await OperationsFailure(absent));
            using var result = JsonDocument.Parse(await absent.Content.ReadAsStringAsync());
            Assert.Equal("UNKNOWN", result.RootElement.GetProperty("data").GetProperty("state").GetString());
        }
        Assert.Equal(rootRevision, await CurrentPermissionPostgresFixture.Scalar(control, rootRevisionSql));

        await CurrentPermissionPostgresFixture.Sql(control, """
            REVOKE SELECT (result_bytes) ON cp6_main_owner_v1.operations FROM stock_adjust_a
            """);
        using (var noJournalRead = await ReadOriginal(command.OperationId))
            Assert.True(noJournalRead.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(noJournalRead));
        await CurrentPermissionPostgresFixture.Sql(control, """
            GRANT SELECT (result_bytes) ON cp6_main_owner_v1.operations TO stock_adjust_a
            """);
        await CurrentPermissionPostgresFixture.Sql(control, "ALTER TABLE cp6_main_owner_v1.operations DISABLE ROW LEVEL SECURITY");
        using (var noJournalRls = await ReadOriginal(command.OperationId))
            Assert.True(noJournalRls.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(noJournalRls));
        await CurrentPermissionPostgresFixture.Sql(control, "ALTER TABLE cp6_main_owner_v1.operations ENABLE ROW LEVEL SECURITY");
        Assert.Equal("price-owner", txn.OperatorCd);
        Assert.Equal(command.Qty, txn.Qty);
        Assert.Equal(stock.PhysicalQty + command.Qty, await Quantity());
        Assert.Equal(1, await OutboxCount(command.OperationId));

        var uncertainCommand = Command(quantity: .87654321m);
        var origin = new NpgsqlConnectionStringBuilder(ownerConnection);
        await using (var relay = new StockPostgresCommitRelay(origin.Port, login))
        {
            var forwardedConnection = new NpgsqlConnectionStringBuilder(ownerConnection) { Port = relay.Port }.ConnectionString;
            await using var forwardedHost = await AuthenticationApiProcess.Start(forwardedConnection, settings);
            var forwardedSession = await Login(forwardedHost, h);
            relay.Arm();
            using var unknown = await forwardedHost.Send(OperationsRequest(forwardedSession, HttpMethod.Post, path, uncertainCommand));
            Assert.True(unknown.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(unknown));
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await unknown.Content.ReadAsStringAsync());
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(3));
        }
        using (var recovered = await ReadOriginal(uncertainCommand.OperationId))
        {
            Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
            using var data = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
            var receipt = data.RootElement.GetProperty("data");
            Assert.Equal("COMMITTED", receipt.GetProperty("state").GetString());
            var number = receipt.GetProperty("result").GetProperty("txnNo").GetString();
            Assert.Equal(uncertainCommand.Qty, await h.Db.StockTransactions.Where(x => x.TxnNo == number).Select(x => x.Qty).SingleAsync());
        }
        Assert.Equal(1, await OutboxCount(uncertainCommand.OperationId));

        var deniedClose = Guid.NewGuid();
        await CurrentPermissionPostgresFixture.Sql(control, "REVOKE EXECUTE ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) FROM stock_adjust_a");
        using (var noCloseWrite = await CloseOriginal(deniedClose))
            Assert.True(noCloseWrite.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(noCloseWrite));
        await CurrentPermissionPostgresFixture.Sql(control, "GRANT EXECUTE ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) TO stock_adjust_a");
        using (var unknown = await ReadOriginal(deniedClose))
        {
            Assert.True(unknown.StatusCode == HttpStatusCode.OK, await OperationsFailure(unknown));
            using var data = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
            Assert.Equal("UNKNOWN", data.RootElement.GetProperty("data").GetProperty("state").GetString());
        }

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
            using var result = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(originalResult, result.RootElement.GetProperty("data").GetRawText());
        }

        var closedCommand = Command();
        await using (var legacyWriter = await control.BeginTransactionAsync())
        {
            await using var table = new NpgsqlCommand("""LOCK TABLE public."T_Stock" IN ROW EXCLUSIVE MODE""", control, legacyWriter);
            await table.ExecuteNonQueryAsync();
            var closing = CloseOriginal(closedCommand.OperationId);
            var closerPid = await WaitForWriter(control.ProcessID);
            var late = Adjust(closedCommand);
            await WaitForWriter(closerPid);
            await legacyWriter.RollbackAsync();
            using var closed = await closing;
            Assert.True(closed.StatusCode == HttpStatusCode.OK, await OperationsFailure(closed));
            using var data = JsonDocument.Parse(await closed.Content.ReadAsStringAsync());
            Assert.Equal("CLOSED", data.RootElement.GetProperty("data").GetProperty("state").GetString());
            using var rejected = await late;
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Contains("ORIGINAL_OPERATION_CLOSED", await rejected.Content.ReadAsStringAsync());
        }
        using (var committed = await CloseOriginal(uncertainCommand.OperationId))
        {
            Assert.True(committed.StatusCode == HttpStatusCode.OK, await OperationsFailure(committed));
            using var data = JsonDocument.Parse(await committed.Content.ReadAsStringAsync());
            Assert.Equal("COMMITTED", data.RootElement.GetProperty("data").GetProperty("state").GetString());
        }
        Assert.Equal(0, await OutboxCount(closedCommand.OperationId));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(rows => rows.SetProperty(g => g.AreaCd, "NO-ACCESS"));
        using (var hidden = await ReadOriginal(command.OperationId))
        {
            Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
            var error = await hidden.Content.ReadAsStringAsync();
            Assert.Contains("OBJECT_FORBIDDEN", error);
            Assert.DoesNotContain(txn.TxnNo, error);
        }
        Assert.Equal(stock.PhysicalQty + command.Qty + uncertainCommand.Qty, await Quantity());
        Assert.Equal(2, await h.Db.StockTransactions.CountAsync());

        Task<decimal> Quantity() => h.Db.Stocks.Where(s => s.Id == stock.Id).Select(s => s.PhysicalQty).SingleAsync();
        Task<int> OutboxCount(Guid operation) => h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_stock_outbox_v1."Source"
            WHERE "TenantId"={h.Tenant} AND "OperationId"={operation}
            """).SingleAsync();
        async Task<int> WaitForWriter(int blocker)
        {
            await using var observer = new NpgsqlConnection(ownerConnection);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await observer.OpenAsync(deadline.Token);
            await using var waiting = new NpgsqlCommand("""
                SELECT pid FROM pg_stat_activity WHERE usename=@login AND datname=current_database()
                    AND query LIKE '%cp6_stock_adjust_owner_v1.lock_graph%' AND @blocker=ANY(pg_blocking_pids(pid))
                ORDER BY pid LIMIT 1
                """, observer);
            waiting.Parameters.AddWithValue("login", login);
            waiting.Parameters.AddWithValue("blocker", blocker);
            while (true)
            {
                if (await waiting.ExecuteScalarAsync(deadline.Token) is int backend) return backend;
                await Task.Delay(40, deadline.Token);
            }
        }
    }

    private static async Task DropStockResponseAsync(Uri origin, HttpRequestMessage request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var forwarding = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start(1);
        using var caller = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)relay.LocalEndpoint).Port}") };
        var requestResult = caller.SendAsync(request, deadline.Token);
        using var client = await relay.AcceptTcpClientAsync(deadline.Token);
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(origin.Host, origin.Port, deadline.Token);
        var sent = client.GetStream().CopyToAsync(upstream.GetStream(), forwarding.Token);
        using var response = new StreamReader(upstream.GetStream(), leaveOpen: true);
        var status = await response.ReadLineAsync(deadline.Token);
        await forwarding.CancelAsync();
        try { await sent; }
        catch (OperationCanceledException) when (forwarding.IsCancellationRequested) { }
        client.Close();
        await Assert.ThrowsAsync<HttpRequestException>(() => requestResult);
        Assert.StartsWith("HTTP/1.1 200", status);
    }
}
