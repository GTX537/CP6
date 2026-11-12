using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Dictionary<string, string> StockReadSettings() => new()
    {
        ["StockReadTenancy__Enabled"] = "true",
        ["StockReadTenancy__Revision"] = "test-v1",
        ["StockReadTenancy__SslMode"] = "Disable"
    };

    private static void ConfigureStockReader(Dictionary<string, string> settings, int index, Harness h, string login, string password)
    {
        var key = $"StockReadTenancy__Tenants__{index}__";
        settings[key + "TenantId"] = h.Tenant.ToString("D");
        settings[key + "Login"] = login;
        settings[key + "Password"] = password;
        settings[key + "CredentialVersion"] = "test-v1";
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_read_tenancy_without_installation_returns_unavailable()
    {
        await using var h = await Seed();
        await GrantStocktakeDashboard(h);
        var settings = StockReadSettings();
        ConfigureStockReader(settings, 0, h, "stock_missing", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/stock"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Main_trusted_tenant_route_rejects_missing_empty_and_foreign_identity_before_business_connection()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        await GrantStocktakeDashboard(a);
        await SeedInventory(a);
        var before = await a.Db.Stocks.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.PhysicalQty, row.AvailableQty }).ToArrayAsync();

        var ownerConnection = a.Db.Database.GetConnectionString()!;
        await using var control = new NpgsqlConnection(ownerConnection);
        await control.OpenAsync();
        const string loginA = "pgt004_runtime_a", loginB = "pgt004_runtime_b";
        var passwordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var passwordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE ROLE {loginA} LOGIN PASSWORD '{passwordA}';
            CREATE ROLE {loginB} LOGIN PASSWORD '{passwordB}';
            """);
        await using (var install = await control.BeginTransactionAsync())
        {
            await StockReadStorageV1.InstallAsync(control, install, [new(a.Tenant, loginA), new(b.Tenant, loginB)]);
            await install.CommitAsync();
        }

        // The cluster is owned by this test fixture. PostgreSQL 18 authorization logs
        // include the successful login and application name even after a socket closes.
        await CurrentPermissionPostgresFixture.Sql(control, "ALTER SYSTEM SET log_connections = 'authorization'");
        try
        {
            Assert.Equal(true, await CurrentPermissionPostgresFixture.Scalar(control, "SELECT pg_reload_conf()"));
            var probeConnection = new NpgsqlConnectionStringBuilder(ownerConnection) { Pooling = false };
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                await using var probe = new NpgsqlConnection(probeConnection.ConnectionString);
                await probe.OpenAsync();
                // PostgreSQL applies this backend setting to new sessions.
                if (Equals("authorization", await CurrentPermissionPostgresFixture.Scalar(probe, "SHOW log_connections"))) break;
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "The owned server did not reload log_connections.");
                await Task.Delay(30);
            }

            var application = "pgt004-main-" + Guid.NewGuid().ToString("N");
            var connection = new NpgsqlConnectionStringBuilder(ownerConnection) { ApplicationName = application, Pooling = false };
            var settings = StockReadSettings();
            ConfigureStockReader(settings, 0, a, loginA, passwordA);
            ConfigureStockReader(settings, 1, b, loginB, passwordB);
            await using var host = await AuthenticationApiProcess.Start(connection.ConnectionString, settings);

            int AuthorizedConnections(string marker)
            {
                var log = Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!, "postgres.log");
                using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(file);
                var count = 0;
                while (reader.ReadLine() is { } line)
                    if (line.Contains("connection authorized:", StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal))
                        count++;
                return count;
            }

            var hostConnections = AuthorizedConnections("application_name=" + application);
            using (var live = await host.Send(new HttpRequestMessage(HttpMethod.Get, "health/live")))
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(hostConnections, AuthorizedConnections("application_name=" + application));

            var cookies = await Login(host, a);
            var original = cookies["cp6_at"];
            var rejected = new[]
            {
                host.TokenWithTestTenant(original, tenant: null),
                host.TokenWithTestTenant(original, Guid.Empty.ToString("D")),
                host.TokenWithTestTenant(original, a.Tenant.ToString("D"), issuer: "https://unregistered.invalid"),
                host.TokenWithTestTenant(original, b.Tenant.ToString("D"))
            };
            foreach (var token in rejected)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/wms/stock");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await host.Send(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(a.Tenant.ToString("D"), body);
                Assert.DoesNotContain(b.Tenant.ToString("D"), body);
                Assert.DoesNotContain(loginA, body);
                Assert.DoesNotContain(loginB, body);
            }
            Assert.Equal(0, AuthorizedConnections("user=" + loginA));
            Assert.Equal(0, AuthorizedConnections("user=" + loginB));
            Assert.Equal(before, await a.Db.Stocks.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.PhysicalQty, row.AvailableQty }).ToArrayAsync());

            using var accepted = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/stock"));
            Assert.True(accepted.StatusCode == HttpStatusCode.OK, await OperationsFailure(accepted));
            Assert.Equal(1, AuthorizedConnections("user=" + loginA));
            Assert.Equal(0, AuthorizedConnections("user=" + loginB));
            Assert.True(AuthorizedConnections("application_name=" + application) > hostConnections);
        }
        finally
        {
            await CurrentPermissionPostgresFixture.Sql(control, "ALTER SYSTEM RESET log_connections");
            await CurrentPermissionPostgresFixture.Scalar(control, "SELECT pg_reload_conf()");
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_read_tenancy_uses_ordinary_logins_and_the_current_stock_owner_scope()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        await GrantStocktakeDashboard(a);
        await GrantStocktakeDashboard(b);
        await SeedInventory(a);
        var aStock = await a.Db.Stocks.AsNoTracking().SingleAsync(x => x.ProductCd == "P1");
        var aHidden = await a.Db.Stocks.AsNoTracking().SingleAsync(x => x.ProductCd == "P2");
        var txn = new StockTransaction { TxnNo = "TEST-READ-A", TxnType = WmsTxnType.ADJ, WarehouseCd = "W01",
            LocationCd = "L1", ProductCd = "P1", LotNo = "LOT", Qty = .12345678m, UnitPrice = 7m, Creator = "price-owner" };
        a.Db.StockTransactions.Add(txn);
        a.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 3 });
        await a.Db.SaveChangesAsync();
        a.Db.ChangeTracker.Clear();
        var mapping = await MappingAdmin(a);
        await BindStandalone(a, mapping, "stock", aStock.Id);
        await BindStandalone(a, mapping, "stock-transaction", txn.Id);
        await BindStandalone(a, mapping, "stock", aHidden.Id, mapping.Other);
        await StandaloneScope(a, mapping, 3);
        await a.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(g => g.AreaCd, "A"));
        var bStock = new Stock { WarehouseCd = aStock.WarehouseCd, LocationCd = aStock.LocationCd, ProductCd = aStock.ProductCd, LotNo = aStock.LotNo,
            PhysicalQty = 20.87654321m, AvailableQty = 20.87654321m, Creator = "price-owner" };
        b.Db.Stocks.Add(bStock);
        await b.Db.SaveChangesAsync();
        b.Db.ChangeTracker.Clear();

        var ownerConnection = a.Db.Database.GetConnectionString()!;
        await using var control = new NpgsqlConnection(ownerConnection);
        await control.OpenAsync();
        const string loginA = "stock_reader_a", loginB = "stock_reader_b";
        var passwordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var passwordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE ROLE {loginA} LOGIN PASSWORD '{passwordA}';
            CREATE ROLE {loginB} LOGIN PASSWORD '{passwordB}';
            GRANT SELECT ON public."Sys_Users" TO {loginA};
            """);
        TenantLoginV1[] bindings = [new(a.Tenant, loginA), new(b.Tenant, loginB)];
        await using (var rejected = await control.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<TenantStorageException>(() => StockReadStorageV1.InstallAsync(control, rejected, bindings));
            await rejected.RollbackAsync();
        }
        Assert.Equal(true, await CurrentPermissionPostgresFixture.Scalar(control, "SELECT to_regnamespace('cp6_stock_read_v1') IS NULL"));
        await CurrentPermissionPostgresFixture.Sql(control, $$"""REVOKE SELECT ON public."Sys_Users" FROM {{loginA}}""");
        await using (var install = await control.BeginTransactionAsync())
        {
            Assert.NotEqual(Guid.Empty, await StockReadStorageV1.InstallAsync(control, install, bindings));
            await install.CommitAsync();
        }

        await using (var ordinary = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ownerConnection)
        {
            Username = loginA, Password = passwordA, SearchPath = "pg_catalog", Pooling = false, Enlist = false
        }.ConnectionString))
        {
            await ordinary.OpenAsync();
            Assert.Equal(a.Tenant, await CurrentPermissionPostgresFixture.Scalar(ordinary, "SELECT cp6_tenancy_v1.current_tenant_id()"));
            Assert.Equal(2L, await CurrentPermissionPostgresFixture.Scalar(ordinary, """SELECT count(*) FROM public."T_Stock" """));
            await using var foreignStock = new NpgsqlCommand("""SELECT count(*) FROM public."T_Stock" WHERE "Id"=@id""", ordinary);
            foreignStock.Parameters.AddWithValue("id", bStock.Id);
            Assert.Equal(0L, await foreignStock.ExecuteScalarAsync());
            foreach (var sql in new[]
            {
                """UPDATE public."T_Stock" SET "PhysicalQty"=0""",
                """SELECT "DeptName" FROM public."Sys_Dept" """,
                """SELECT count(*) FROM public."Sys_Users" """,
                """SELECT count(*) FROM cp6_tenancy_v1.login_tenant"""
            })
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
                    () => CurrentPermissionPostgresFixture.Sql(ordinary, sql))).SqlState);
        }

        var settings = StockReadSettings();
        ConfigureStockReader(settings, 0, a, loginA, passwordA);
        ConfigureStockReader(settings, 1, b, loginB, passwordB);
        await using var host = await AuthenticationApiProcess.Start(ownerConnection, settings);
        var aCookies = await Login(host, a);
        var bCookies = await Login(host, b);

        async Task<JsonElement> Read(Dictionary<string, string> cookies, string path)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("data").Clone();
        }
        const string list = "api/wms/stock";
        var aPage = await Read(aCookies, list + "?productCd=P&pageSize=1");
        Assert.Equal(1, aPage.GetProperty("total").GetInt32());
        var row = Assert.Single(aPage.GetProperty("items").EnumerateArray());
        Assert.Equal(aStock.Id, row.GetProperty("id").GetGuid());
        Assert.Equal(10.12345678m, row.GetProperty("physicalQty").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("unitPrice").ValueKind);
        var bPage = await Read(bCookies, list);
        Assert.Equal(bStock.Id, Assert.Single(bPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var history = await Read(aCookies, list + $"/{aStock.Id}/history");
        var historicalTxn = Assert.Single(history.GetProperty("transactions").EnumerateArray());
        Assert.Equal(txn.Id, historicalTxn.GetProperty("id").GetGuid());
        Assert.Equal(.12345678m, historicalTxn.GetProperty("qty").GetDecimal());
        Assert.Equal(JsonValueKind.Null, historicalTxn.GetProperty("unitPrice").ValueKind);
        var ledger = await Read(aCookies, list + "/transactions");
        Assert.Equal(txn.Id, Assert.Single(ledger.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using (var foreign = await host.Send(OperationsRequest(aCookies, HttpMethod.Get, list + $"/{bStock.Id}/history")))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        // An actual ordinary role configured for the other tenant must be rejected even if it logs in.
        var swapped = StockReadSettings();
        ConfigureStockReader(swapped, 0, b, loginA, passwordA);
        await using var wrong = await AuthenticationApiProcess.Start(ownerConnection, swapped);
        var wrongCookies = await Login(wrong, b);
        using var mismatch = await wrong.Send(OperationsRequest(wrongCookies, HttpMethod.Get, list));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, mismatch.StatusCode);

        // The legacy application connection still has SELECT. A tenant-credential failure must reach the caller.
        await CurrentPermissionPostgresFixture.Sql(control, $$"""REVOKE SELECT ON public."T_Stock" FROM {{loginA}}""");
        using (var denied = await host.Send(OperationsRequest(aCookies, HttpMethod.Get, list)))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Equal(bStock.Id, Assert.Single((await Read(bCookies, list)).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        await CurrentPermissionPostgresFixture.Sql(control, """ALTER TABLE public."T_Stock" DISABLE ROW LEVEL SECURITY""");
        using (var unsafeProfile = await host.Send(OperationsRequest(bCookies, HttpMethod.Get, list)))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unsafeProfile.StatusCode);
    }
}
