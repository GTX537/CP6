using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PGT017_erp_shipment_projection_keeps_latest_date_after_earlier_retry()
    {
        await using var h = await Seed();
        var first = await AllocateFulfilment(h, quantity: 5m, manualSource: true);
        await SeedAr(h, "invoice-create");
        var productCode = await h.Db.ProductMasters.Where(row => row.Id == first.Product).Select(row => row.ProductCd).SingleAsync();
        var direct = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        await using var observer = new NpgsqlConnection(direct.ConnectionString);
        await observer.OpenAsync();
        await using var host = await StartW643Api(h, direct.ConnectionString, ShipmentProjectionPort("CP6_ERP_SHIPMENT_HTTP_PORT", 0));
        var actor = await Login(host, h);
        using var created = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/wms/outbound-order", new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping, WebOrderNo = h.Number, WarehouseCd = "W01", CustomerCd = "C",
            PlannedDate = DateTime.Today,
            Details = [new() { LineNo = 1, ProductCd = productCode, UnitCd = first.Unit.Profile.Code, RequiredQty = 2m }]
        }));
        Assert.True(created.StatusCode == HttpStatusCode.OK, await OperationsFailure(created));
        var createdBody = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());
        var second = createdBody.GetProperty("data").GetProperty("outboundNo").GetString()!;
        Assert.NotEqual(first.Number, second);
        foreach (var action in new[] { "confirm", "allocate" })
        {
            using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/wms/outbound-order/{second}/{action}", new { }));
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        }

        var initialFirst = await ReadW643(observer, h.Tenant, h.Number, first.Number);
        var initialSecond = await ReadW643(observer, h.Tenant, h.Number, second);
        Assert.Equal(OutboundOrderStatus.Allocated, AssetInt(Assert.Single(initialFirst.Rows("physical")), "Status"));
        Assert.Equal(OutboundOrderStatus.Allocated, AssetInt(Assert.Single(initialSecond.Rows("physical")), "Status"));
        Assert.Empty(initialFirst.Rows("events"));
        Assert.Empty(initialSecond.Rows("events"));

        JsonElement firstHttp;
        await using (var relay = new StockPostgresCommitRelay(direct.Port, direct.Username!,
            ShipmentProjectionPort("CP6_ERP_SHIPMENT_RELAY_PORT", 0)))
        {
            var relayed = new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = relay.Port };
            await using var firstApi = await StartW643Api(h, relayed.ConnectionString, ShipmentProjectionPort("CP6_ERP_SHIPMENT_RELAY_HTTP_PORT", 0));
            var firstActor = await Login(firstApi, h);
            relay.Arm();
            using var firstResponse = await Ship(firstApi, firstActor, first.Number);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(firstResponse.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(firstResponse));
            firstHttp = JsonSerializer.Deserialize<JsonElement>(await firstResponse.Content.ReadAsStringAsync());
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", firstHttp.GetRawText(), StringComparison.Ordinal);
        }

        var firstCommitted = await ReadW643(observer, h.Tenant, h.Number, first.Number);
        var firstTime = AssertPhysicalShipment(firstCommitted, 5m);
        var originalFirst = Assert.Single(firstCommitted.Rows("events"));
        Assert.Equal((IntegrationEventStatus.Pending, 0), (originalFirst.GetProperty("Status").GetString(), AssetInt(originalFirst, "Attempts")));
        CheckPurchaseTables(initialFirst, firstCommitted, "orders", "lines");

        using var secondResponse = await Ship(host, actor, second);
        Assert.True(secondResponse.StatusCode == HttpStatusCode.OK, await OperationsFailure(secondResponse));
        var secondHttp = JsonSerializer.Deserialize<JsonElement>(await secondResponse.Content.ReadAsStringAsync());
        var beforeFirstRetry = await ReadW643(observer, h.Tenant, h.Number, first.Number);
        var secondCommitted = await ReadW643(observer, h.Tenant, h.Number, second);
        var secondTime = AssertPhysicalShipment(secondCommitted, 2m);
        Assert.True(firstTime < secondTime);
        Assert.Equal(originalFirst.GetRawText(), Assert.Single(beforeFirstRetry.Rows("events")).GetRawText());
        var originalSecond = Assert.Single(secondCommitted.Rows("events"));
        AssertShipmentProjectionSuccess(originalSecond, h.Number);
        var headerBefore = Assert.Single(secondCommitted.Rows("orders"));
        var beforeLines = secondCommitted.Rows("lines");
        Assert.Equal(2, beforeLines.Length);
        Assert.Equal(2m, AssetDecimal(beforeLines[0], "ShippedQty"));
        Assert.Equal(second, beforeLines[0].GetProperty("LastOutboundNo").GetString());
        Assert.Equal(secondTime, ReadShipmentTimestamp(beforeLines[0], "LastShipDate"));
        Assert.Equal(secondTime, ReadShipmentTimestamp(headerBefore, "ActualShipDate"));

        // Preserve the separate FIN lane while this worker exercises the real original WMS-to-ERP event.
        var separateFinance = await h.Db.IntegrationEvents.Where(row => row.SourceModule == "WMS" && row.TargetModule == "FIN"
            && row.NextRetryAt != null).ExecuteUpdateAsync(update => update.SetProperty(row => row.NextRetryAt, DateTime.UtcNow.AddHours(1)));
        await using var services = ShipmentProjectionWorkers(h.Tenant);
        var worker = W620Worker(services);
        var retryObservedAt = await BackendObservationTime(observer);
        await worker.ProcessOnceAsync();
        var afterFirst = await ReadW643(observer, h.Tenant, h.Number, first.Number);
        var afterSecond = await ReadW643(observer, h.Tenant, h.Number, second);
        CheckPurchaseTables(firstCommitted, afterFirst, "physical", "physical-lines");
        CheckPurchaseTables(secondCommitted, afterSecond, "physical", "physical-lines", "events");
        var firstResult = Assert.Single(afterFirst.Rows("events"));
        AssertShipmentProjectionSuccess(firstResult, h.Number);
        foreach (var name in new[] { "Id", "TenantId", "SourceNo", "CorrelationId", "Creator", "PayloadJson" })
            Assert.Equal(originalFirst.GetProperty(name).GetRawText(), firstResult.GetProperty(name).GetRawText());
        var firstPayload = JsonSerializer.Deserialize<JsonElement>(firstResult.GetProperty("PayloadJson").GetString()!);
        Assert.Equal(1, firstPayload.GetProperty("Version").GetInt32());
        Assert.Equal(first.Id, firstPayload.GetProperty("OutboundId").GetGuid());
        var secondPayload = JsonSerializer.Deserialize<JsonElement>(originalSecond.GetProperty("PayloadJson").GetString()!);
        Assert.Equal(1, secondPayload.GetProperty("Version").GetInt32());
        Assert.Equal(AssetId(Assert.Single(secondCommitted.Rows("physical"))), secondPayload.GetProperty("OutboundId").GetGuid());

        var headerAfter = Assert.Single(afterFirst.Rows("orders"));
        var afterLines = afterFirst.Rows("lines");
        Assert.Equal(beforeLines.Select(row => AssetId(row)), afterLines.Select(row => AssetId(row)));
        Assert.Equal(new[] { 5m, 2m }, afterLines.Select(row => AssetDecimal(row, "ShippedQty")));
        Assert.Equal(new[] { 9, 5 }, afterLines.Select(row => AssetInt(row, "ShipStatus")));
        Assert.Equal(5, AssetInt(headerAfter, "ShipStatus"));
        Assert.Equal(firstTime, ReadShipmentTimestamp(afterLines[1], "LastShipDate"));
        Assert.Equal(first.Number, afterLines[1].GetProperty("LastOutboundNo").GetString());
        await worker.ProcessOnceAsync();
        Assert.Equal(afterFirst, await ReadW643(observer, h.Tenant, h.Number, first.Number));
        Assert.Equal(afterSecond, await ReadW643(observer, h.Tenant, h.Number, second));

        var actual = new
        {
            ExpectedLatest = secondTime, HeaderActual = ReadShipmentTimestamp(headerAfter, "ActualShipDate"),
            SharedLineLast = ReadShipmentTimestamp(afterLines[0], "LastShipDate"),
            SharedLineLastOutbound = afterLines[0].GetProperty("LastOutboundNo").GetString(), ExpectedLastOutbound = second,
            HeaderModifyBefore = ReadShipmentTimestamp(headerBefore, "ModifyDate"), HeaderModifyAfter = ReadShipmentTimestamp(headerAfter, "ModifyDate"),
            LineModifyBefore = ReadShipmentTimestamp(beforeLines[0], "ModifyDate"), LineModifyAfter = ReadShipmentTimestamp(afterLines[0], "ModifyDate")
        };
        await DeviceEvidence("shipment-date-order.json", new
        {
            h.Tenant, Order = h.Number, EarlyOutbound = first.Number, LateOutbound = second, FirstPhysicalTime = firstTime,
            SecondPhysicalTime = secondTime, RetryObservedAt = retryObservedAt, FirstHttp = firstHttp, SecondHttp = secondHttp,
            OriginalFirst = PurchaseState(firstCommitted), BeforeRetry = PurchaseState(secondCommitted),
            AfterRetryFirst = PurchaseState(afterFirst), AfterRetrySecond = PurchaseState(afterSecond),
            FinanceEventsRescheduled = separateFinance, RepeatScanUnchanged = true, Actual = actual
        });
        Assert.True(actual.HeaderActual == secondTime && actual.SharedLineLast == secondTime && actual.SharedLineLastOutbound == second
            && actual.HeaderModifyAfter >= retryObservedAt.LocalDateTime && actual.HeaderModifyAfter > actual.HeaderModifyBefore
            && actual.LineModifyAfter > actual.LineModifyBefore && actual.LineModifyAfter == actual.HeaderModifyAfter,
            "Earlier physical shipment overwrote the later shipment or audit date: " + JsonSerializer.Serialize(actual));

        static Task<HttpResponseMessage> Ship(AuthenticationApiProcess api, Dictionary<string, string> cookies, string outbound) =>
            api.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/wms/outbound-order/{outbound}/ship", new { }));
    }

    private static DateTime AssertPhysicalShipment(AssetRows rows, decimal shipped)
    {
        var header = Assert.Single(rows.Rows("physical"));
        Assert.Equal(OutboundOrderStatus.Completed, AssetInt(header, "Status"));
        var line = Assert.Single(rows.Rows("physical-lines"));
        Assert.Equal(shipped, AssetDecimal(line, "ShippedQty"));
        Assert.False(string.IsNullOrWhiteSpace(line.GetProperty("ShipTxnNo").GetString()));
        return ReadShipmentTimestamp(header, "ModifyDate");
    }

    private static DateTime ReadShipmentTimestamp(JsonElement row, string property) => row.GetProperty(property).GetDateTime();

    private static void AssertShipmentProjectionSuccess(JsonElement evt, string order)
    {
        Assert.Equal((IntegrationEventStatus.Success, 1), (evt.GetProperty("Status").GetString(), AssetInt(evt, "Attempts")));
        Assert.Equal(order, evt.GetProperty("TargetNo").GetString());
        Assert.Equal(JsonValueKind.Null, evt.GetProperty("NextRetryAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, evt.GetProperty("LastError").ValueKind);
    }
}
