using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task VerifyStockCredentialRotationAsync(Harness a, (Harness Tenant, string Login, string Password) b,
        NpgsqlConnection control, string connection, string aLogin, string oldPassword, StockTenantPairResult pair)
    {
        var afterOriginalHost = await WaitForPoolsToExit();
        var aBefore = await StockTenantEffectsAsync(a);
        var bBefore = await StockTenantEffectsAsync(b.Tenant);
        var roleQuery = $"SELECT oid::text FROM pg_roles WHERE rolname='{aLogin}'";
        var roleOid = Assert.IsType<string>(await CurrentPermissionPostgresFixture.Scalar(control, roleQuery));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $"ALTER ROLE {aLogin} PASSWORD '{password}'");
        Assert.Equal(roleOid, await CurrentPermissionPostgresFixture.Scalar(control, roleQuery));

        var stale = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = aLogin, Password = oldPassword, SearchPath = "pg_catalog", Pooling = false, Enlist = false
        };
        await using var staleConnection = new NpgsqlConnection(stale.ConnectionString);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => staleConnection.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InvalidPassword, rejected.SqlState);

        const decimal delta = .76543218m;
        var input = new StockApplyCommandV1
        {
            OperationId = Guid.NewGuid(), TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1",
            ProductCd = "P1", LotNo = "LOT", Qty = delta, UnitPrice = 3m
        };
        StockPoolSession[] stalePools;
        await using (var staleHost = await AuthenticationApiProcess.Start(connection, Settings(oldPassword)))
        {
            var aSession = await Login(staleHost, a);
            using var denied = await Apply(staleHost, aSession);
            Assert.True(denied.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(denied));
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await denied.Content.ReadAsStringAsync());
            var bSession = await Login(staleHost, b.Tenant);
            await AssertOriginal(staleHost, bSession, pair.OperationId, pair.BResult);
            stalePools = await StockPoolSessionsAsync(control, aLogin, b.Login);
            Assert.Equal(b.Login, Assert.Single(stalePools).Login);
        }
        var afterStaleHost = await WaitForPoolsToExit();
        Assert.Equal(aBefore, await StockTenantEffectsAsync(a));
        Assert.Equal(bBefore, await StockTenantEffectsAsync(b.Tenant));

        var renewed = Settings(password);
        renewed["StockReadTenancy__Revision"] = "test-v2";
        renewed["StockReadTenancy__Tenants__0__CredentialVersion"] = "test-v2";
        await using var freshHost = await AuthenticationApiProcess.Start(connection, renewed);
        var freshA = await Login(freshHost, a);
        var freshB = await Login(freshHost, b.Tenant);
        await AssertOriginal(freshHost, freshA, pair.OperationId, pair.AResult);
        await AssertOriginal(freshHost, freshB, pair.OperationId, pair.BResult);
        Assert.Equal("UNKNOWN", (await Original(freshHost, freshA, input.OperationId)).GetProperty("state").GetString());
        using var applied = await Apply(freshHost, freshA);
        var result = await StockCreationResponse(applied);
        await AssertOriginal(freshHost, freshA, input.OperationId, result);
        Assert.Equal("UNKNOWN", (await Original(freshHost, freshB, input.OperationId)).GetProperty("state").GetString());
        Assert.Equal((aBefore.StockId, aBefore.Quantity + delta, aBefore.Movements + 1, aBefore.Originals + 1, aBefore.Outbox + 1),
            await StockTenantEffectsAsync(a));
        Assert.Equal(bBefore, await StockTenantEffectsAsync(b.Tenant));
        var freshPools = await StockPoolSessionsAsync(control, aLogin, b.Login);
        Assert.Equal(new[] { aLogin, b.Login }, freshPools.Select(row => row.Login));
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "stock-credential-rotation.json"), JsonSerializer.Serialize(new
            {
                roleOid, priorOperation = pair.OperationId, acceptedOperation = input.OperationId, oldCredentialSqlState = rejected.SqlState,
                originalPools = pair.Sessions, afterOriginalHost, stalePools, afterStaleHost, freshPools, delta
            }));

        Dictionary<string, string> Settings(string aPassword)
        {
            var settings = StockReadSettings();
            settings["StockReadTenancy__EnableV1Adjust"] = "true";
            ConfigureStockReader(settings, 0, a, aLogin, aPassword);
            ConfigureStockReader(settings, 1, b.Tenant, b.Login, b.Password);
            return settings;
        }
        async Task<int> WaitForPoolsToExit()
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                var remaining = await StockPoolSessionsAsync(control, aLogin, b.Login);
                if (remaining.Length == 0) return remaining.Length;
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Exited Stock host retained ordinary PostgreSQL sessions.");
                await Task.Delay(50);
            }
        }
        Task<HttpResponseMessage> Apply(AuthenticationApiProcess host, Dictionary<string, string> session) =>
            host.Send(OperationsRequest(session, HttpMethod.Post, "api/wms/stock/commands/v1/apply", input));
        static async Task<JsonElement> Original(AuthenticationApiProcess host, Dictionary<string, string> session, Guid operation)
        {
            using var response = await host.Send(OperationsRequest(session, HttpMethod.Get, $"api/wms/stock/operations/v1/{operation:D}"));
            return await StockCreationResponse(response);
        }
        static async Task AssertOriginal(AuthenticationApiProcess host, Dictionary<string, string> session, Guid operation, JsonElement expected)
        {
            var actual = await Original(host, session, operation);
            Assert.Equal("COMMITTED", actual.GetProperty("state").GetString());
            Assert.Equal(expected.GetRawText(), actual.GetProperty("result").GetRawText());
        }
    }
}
