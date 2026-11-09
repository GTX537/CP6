using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task VerifyOrdinaryV2StockAdjustmentAsync(Harness h, MappingHarness mapping, NpgsqlConnection control,
        string connection, string login, string password, Guid historicalStock)
    {
        h.Db.ChangeTracker.Clear();
        await StandaloneScope(h, mapping);
        var settings = StockReadSettings();
        settings["StockReadTenancy__EnableV1Adjust"] = "true";
        settings["StockReadTenancy__EnableV2Adjust"] = "true";
        ConfigureStockReader(settings, 0, h, login, password);
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var session = await Login(host, h);
        const string root = "api/wms/stock/";
        const string product = "HTTP-CREATION";
        const string creation = root + "creation-context/adjust?warehouseCd=W01&locationCd=L3&productCd=" + product + "&lotNo=LOT";
        var operation = Guid.NewGuid();
        var existingAudits = await AuditCount();

        await CurrentPermissionPostgresFixture.Sql(control, $"""REVOKE SELECT ("DeptName") ON public."Sys_Dept" FROM {login}""");
        using (var missingRead = await host.Send(OperationsRequest(session, HttpMethod.Get, creation)))
            Assert.True(missingRead.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(missingRead));
        await CurrentPermissionPostgresFixture.Sql(control, $"""GRANT SELECT ("DeptName") ON public."Sys_Dept" TO {login}""");

        var previous = await CreationVersion();
        await BindStandalone(h, mapping, "stock", historicalStock);
        var current = await CreationVersion();
        Assert.NotEqual(previous, current);
        using (var stale = await Apply(previous, mapping.Child))
        {
            Assert.True(stale.StatusCode == HttpStatusCode.PreconditionFailed, await OperationsFailure(stale));
            Assert.Contains("CREATION_AUTHORITY_CHANGED", await stale.Content.ReadAsStringAsync());
        }
        using (var wrongDepartment = await Apply(current, mapping.Root))
        {
            Assert.True(wrongDepartment.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(wrongDepartment));
            Assert.Contains("CREATION_DEPARTMENT_FORBIDDEN", await wrongDepartment.Content.ReadAsStringAsync());
        }

        const string audit = "cp6_stock_creation_writer_v2.record_audit(uuid,uuid,text,timestamp,timestamp,jsonb)";
        await CurrentPermissionPostgresFixture.Sql(control, $"REVOKE EXECUTE ON FUNCTION {audit} FROM {login}");
        using (var missingWrite = await Apply(current, mapping.Child))
            Assert.True(missingWrite.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(missingWrite));
        await CurrentPermissionPostgresFixture.Sql(control, $"GRANT EXECUTE ON FUNCTION {audit} TO {login}");
        Assert.False(await h.Db.Stocks.AnyAsync(s => s.ProductCd == product));
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(existingAudits, await AuditCount());
        Assert.Equal(0, await Operations(h));
        Assert.Equal(0, await StockNotifications(h));
        using (var unknown = await Original())
            Assert.Equal("UNKNOWN", (await Data(unknown)).GetProperty("state").GetString());

        using var created = await Apply(current, mapping.Child);
        var result = await Data(created);
        using var replay = await Apply(current, mapping.Child);
        Assert.Equal(result.GetRawText(), (await Data(replay)).GetRawText());
        using var original = await Original();
        var recovered = await Data(original);
        Assert.Equal("COMMITTED", recovered.GetProperty("state").GetString());
        Assert.Equal(result.GetRawText(), recovered.GetProperty("result").GetRawText());
        var stock = await h.Db.Stocks.AsNoTracking().SingleAsync(s => s.ProductCd == product);
        var movement = await h.Db.StockTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(9007199254740.12345678m, stock.PhysicalQty);
        Assert.Equal(stock.PhysicalQty, movement.Qty);
        Assert.Equal("price-owner", stock.Creator);
        Assert.Equal("price-owner", movement.OperatorCd);
        Assert.Equal(result.GetProperty("txnNo").GetString(), movement.TxnNo);
        var departments = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT department_id AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND (object_kind='stock' AND object_id={stock.Id}
                OR object_kind='stock-transaction' AND object_id={movement.Id})
            """).ToArrayAsync();
        Assert.Equal(new[] { mapping.Child, mapping.Child }, departments);
        Assert.Equal(existingAudits + 1, await AuditCount());
        Assert.Equal(1, await Operations(h));
        Assert.Equal(1, await StockNotifications(h));

        async Task<string> CreationVersion()
        {
            using var response = await host.Send(OperationsRequest(session, HttpMethod.Get, creation));
            var data = await Data(response);
            Assert.True(data.GetProperty("requiresCreation").GetBoolean());
            Assert.False(data.GetProperty("allowsUnbound").GetBoolean());
            Assert.Equal(mapping.Child, Assert.Single(data.GetProperty("departments").EnumerateArray()).GetProperty("id").GetGuid());
            return data.GetProperty("authorityVersion").GetString()!;
        }
        Task<HttpResponseMessage> Apply(string version, Guid department) => host.Send(OperationsRequest(session, HttpMethod.Post,
            root + "commands/v2/apply", new
            {
                operationId = operation, txnType = "ADJ", warehouseCd = "W01", locationCd = "L3", productCd = product,
                lotNo = "LOT", qty = "9007199254740.12345678", operatorCd = "forged",
                newStock = new { departmentId = department, expectedAuthorityVersion = version }
            }));
        Task<HttpResponseMessage> Original() =>
            host.Send(OperationsRequest(session, HttpMethod.Get, root + $"operations/v1/{operation:D}"));
        Task<int> AuditCount() => h.Db.Sys_SecurityLogs.CountAsync(x => x.UserId == h.User &&
            x.Reason == OperationsAccessV1.StockCreationAuditReason);
        static async Task<JsonElement> Data(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        }
    }
}
