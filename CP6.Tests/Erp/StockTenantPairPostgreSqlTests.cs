using System.Text.Json;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record StockPoolSession(string Login, int Pid);
    private sealed record StockTenantPairResult(Guid OperationId, JsonElement AResult, JsonElement BResult, StockPoolSession[] Sessions);

    private static async Task<StockTenantPairResult> VerifyStockTenantPairAsync(Harness a, Harness b, NpgsqlConnection control,
        AuthenticationApiProcess host, Dictionary<string, string> aSession, string aLogin, string bLogin)
    {
        const string root = "api/wms/stock";
        var bSession = await Login(host, b);
        var aBefore = await Stock(aSession);
        var bBefore = await Stock(bSession);
        Assert.NotEqual(aBefore.Id, bBefore.Id);
        Assert.NotEqual(aBefore.Quantity, bBefore.Quantity);
        var firstPools = await StockPoolSessionsAsync(control, aLogin, bLogin);
        Assert.Equal(new[] { aLogin, bLogin }, firstPools.Select(row => row.Login));
        Assert.NotEqual(firstPools[0].Pid, firstPools[1].Pid);

        var operation = Guid.NewGuid();
        const decimal aDelta = .33333333m;
        const decimal bDelta = .55555555m;
        var aResult = await Apply(aSession, aDelta);
        Assert.Equal((aBefore.Id, aBefore.Quantity + aDelta), await Stock(aSession));
        Assert.Equal(bBefore, await Stock(bSession));
        Assert.Equal("UNKNOWN", (await Original(bSession)).GetProperty("state").GetString());

        var bResult = await Apply(bSession, bDelta);
        await AssertOriginal(a, aSession, aResult, aDelta);
        await AssertOriginal(b, bSession, bResult, bDelta);
        Assert.Equal(aResult.GetRawText(), (await Apply(aSession, aDelta)).GetRawText());
        Assert.Equal(bResult.GetRawText(), (await Apply(bSession, bDelta)).GetRawText());

        var observed = new List<StockPoolSession[]> { firstPools };
        for (var round = 0; round < 3; round++)
        {
            Assert.Equal((aBefore.Id, aBefore.Quantity + aDelta), await Stock(aSession));
            Assert.Equal((bBefore.Id, bBefore.Quantity + bDelta), await Stock(bSession));
            Assert.Equal((aBefore.Id, aBefore.Quantity + aDelta), await Stock(aSession));
            var current = await StockPoolSessionsAsync(control, aLogin, bLogin);
            Assert.Equal(firstPools, current);
            observed.Add(current);
        }
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "stock-pair-pools.json"), JsonSerializer.Serialize(observed, new JsonSerializerOptions { WriteIndented = true }));
        return new(operation, aResult, bResult, firstPools);

        async Task<(Guid Id, decimal Quantity)> Stock(Dictionary<string, string> session)
        {
            using var response = await host.Send(OperationsRequest(session, HttpMethod.Get,
                root + "?warehouseCd=W01&locationCd=L1&productCd=P1&lotNo=LOT"));
            var row = Assert.Single((await StockCreationResponse(response)).GetProperty("items").EnumerateArray());
            return (row.GetProperty("id").GetGuid(), row.GetProperty("physicalQty").GetDecimal());
        }
        async Task<JsonElement> Apply(Dictionary<string, string> session, decimal delta)
        {
            using var response = await host.Send(OperationsRequest(session, HttpMethod.Post, root + "/commands/v1/apply",
                new StockApplyCommandV1
                {
                    OperationId = operation, TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1",
                    ProductCd = "P1", LotNo = "LOT", Qty = delta, UnitPrice = 3m
                }));
            return await StockCreationResponse(response);
        }
        async Task<JsonElement> Original(Dictionary<string, string> session)
        {
            using var response = await host.Send(OperationsRequest(session, HttpMethod.Get, root + $"/operations/v1/{operation:D}"));
            return await StockCreationResponse(response);
        }
        async Task AssertOriginal(Harness tenant, Dictionary<string, string> session, JsonElement result, decimal delta)
        {
            var original = await Original(session);
            Assert.Equal("COMMITTED", original.GetProperty("state").GetString());
            Assert.Equal(result.GetRawText(), original.GetProperty("result").GetRawText());
            var number = result.GetProperty("txnNo").GetString();
            Assert.Equal(delta, await tenant.Db.StockTransactions.Where(x => x.TxnNo == number).Select(x => x.Qty).SingleAsync());
            Assert.Equal(tenant.User, await tenant.Db.Database.SqlQuery<Guid>($"""
                SELECT actor_id AS "Value" FROM cp6_main_owner_v1.operations
                WHERE tenant_id={tenant.Tenant} AND owner={StockOutboxProtocolV1.SourceOwner} AND operation_id={operation}
                """).SingleAsync());
            var message = await tenant.Db.Database.SqlQuery<string>($"""
                SELECT m."MessageId" AS "Value"
                FROM cp6_stock_outbox_v1."Source" s
                JOIN cp6_stock_outbox_v1."Cp6_OutboxMessage" m ON m."Id"=s."NativeId" AND m."TenantId"=s."TenantId"
                JOIN cp6_stock_outbox_v1."Cp6_OutboxBindingV2" b ON b."NativeId"=m."Id" AND b."MessageId"=m."MessageId"
                JOIN cp6_stock_outbox_v1."Cp6_OutboxRouteV2" r
                    ON r."Id"=b."RootId" AND r."TenantId"=s."TenantId" AND r."CurrentEpoch"=b."AssignedEpoch"
                JOIN cp6_stock_outbox_v1."Authority" a ON a."RootId"=r."Id" AND a."TenantId"=s."TenantId" AND a."Enabled"
                WHERE s."TenantId"={tenant.Tenant} AND s."OperationId"={operation}
                """).SingleAsync();
            Assert.Equal($"stock-hint:{tenant.Tenant:N}:{operation:N}", message);
        }
    }

    private static async Task<StockPoolSession[]> StockPoolSessionsAsync(NpgsqlConnection control, string aLogin, string bLogin)
    {
        await using var command = new NpgsqlCommand("""
            SELECT usename::text,pid FROM pg_stat_activity
            WHERE datname=current_database() AND application_name='cp6-tenant-storage-v1' AND usename::text=ANY(@roles)
            ORDER BY usename,pid
            """, control);
        command.Parameters.AddWithValue("roles", new[] { aLogin, bLogin });
        await using var rows = await command.ExecuteReaderAsync();
        var result = new List<StockPoolSession>();
        while (await rows.ReadAsync()) result.Add(new(rows.GetString(0), rows.GetInt32(1)));
        return result.ToArray();
    }
}
