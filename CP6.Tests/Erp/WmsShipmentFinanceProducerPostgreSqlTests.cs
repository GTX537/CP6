using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("ship")]
    [InlineData("wms-cancel")]
    [InlineData("erp-cancel")]
    public async Task W620_producer_uses_signed_owner_route_and_real_FIN_hook(string operation)
    {
        await using var h = await Seed();
        var shipment = await AllocateFulfilment(h, onlyLastLine: true);
        await SeedAr(h, "invoice-create");
        if (operation == "erp-cancel") await GrantLegacyAuthorityAction(h, "erp-order", "cancel");
        await using var host = await StartW620Api(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, shipment.Number);
        Assert.Equal(OutboundOrderStatus.Allocated, AssetInt(Assert.Single(before.Rows("shipments")), "Status"));
        Assert.Empty(before.Rows("events"));

        var route = operation switch
        {
            "ship" => $"/api/wms/outbound-order/{shipment.Number}/ship",
            "wms-cancel" => $"/api/wms/outbound-order/{shipment.Number}/cancel",
            "erp-cancel" => $"/api/orders/{h.Number}/cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        object? body = operation switch
        {
            "ship" => new { captureReturnSource = true },
            "erp-cancel" => new { Reason = "Verified pre-shipment cancellation", Force = true },
            _ => null,
        };
        using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, route, body));
        var returned = await result.Content.ReadFromJsonAsync<JsonElement>();
        var after = await ReadW620(observer, h.Tenant, shipment.Number);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "signed producer route", Operation = operation, Route = route, h.Tenant, Shipment = shipment.Number,
            Status = (int)result.StatusCode, Response = returned, Before = W620Evidence(before), After = W620Evidence(after),
        }));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var evt = Assert.Single(after.Rows("events"));
        Assert.Equal("price-owner", evt.GetProperty("Creator").GetString());
        Assert.Equal(shipment.Number, evt.GetProperty("SourceNo").GetString());
        Assert.Equal(1, AssetInt(evt, "Attempts"));
        var header = Assert.Single(after.Rows("shipments"));
        var oldMoves = before.Rows("stockMoves").Select(row => AssetId(row)).ToHashSet();
        var move = Assert.Single(after.Rows("stockMoves"), row => !oldMoves.Contains(AssetId(row)));
        Assert.Equal(operation == "ship" ? "OUT" : "UNRSV", move.GetProperty("TxnType").GetString());
        if (operation == "ship")
        {
            Assert.Equal(OutboundOrderStatus.Completed, AssetInt(header, "Status"));
            var doc = Assert.Single(after.Rows("invoices"));
            Assert.Equal((int)ArInvoiceStatus.Posted, AssetInt(doc, "Status"));
            Assert.Equal(shipment.Number, doc.GetProperty("ShipmentId").GetString());
            Assert.Equal(h.Number, doc.GetProperty("OrderId").GetString());
            Assert.Equal(6.37m, AssetDecimal(doc, "GrossAmount"));
            var line = Assert.Single(after.Rows("invoiceLines"));
            Assert.Equal((2.12345678m, 3m, 6.37m),
                (AssetDecimal(line, "Qty"), AssetDecimal(line, "UnitPrice"), AssetDecimal(line, "Amount")));
            CheckApJournal(after, AssetId(doc, "JournalEntryId"), doc.GetProperty("No").GetString()!, 6.37m, VoucherSource.AR);
            Assert.Equal(JsonValueKind.Null, doc.GetProperty("CostJournalEntryId").ValueKind);
            Assert.Equal(nameof(IFinBridgeHook.OnShipmentConfirmedAsync), evt.GetProperty("HookName").GetString());
            Assert.Equal(IntegrationEventStatus.Success, evt.GetProperty("Status").GetString());
            Assert.Equal(doc.GetProperty("No").GetString(), evt.GetProperty("TargetNo").GetString());
            using var finPayload = JsonDocument.Parse(evt.GetProperty("PayloadJson").GetString()!);
            Assert.Equal(shipment.Number, finPayload.RootElement.GetProperty("ShipmentId").GetString());
            Assert.Equal(0m, finPayload.RootElement.GetProperty("EstimatedCost").GetDecimal());
            using var cannotCancel = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/wms/outbound-order/{shipment.Number}/cancel"));
            var cancellation = await cannotCancel.Content.ReadFromJsonAsync<JsonElement>();
            output.WriteLine(JsonSerializer.Serialize(new { Scenario = "post-shipment ordinary WMS cancel", Status = (int)cannotCancel.StatusCode, Response = cancellation }));
            Assert.False(cannotCancel.IsSuccessStatusCode);
            Assert.Equal(after, await ReadW620(observer, h.Tenant, shipment.Number));
        }
        else
        {
            Assert.Equal(OutboundOrderStatus.Cancelled, AssetInt(header, "Status"));
            Assert.Empty(after.Rows("invoices"));
            Assert.Empty(after.Rows("journals"));
            Assert.Equal(nameof(IFinBridgeHook.OnShipmentCancelledAsync), evt.GetProperty("HookName").GetString());
            Assert.Equal(IntegrationEventStatus.Skipped, evt.GetProperty("Status").GetString());
            Assert.Equal("no invoice for shipment", evt.GetProperty("LastError").GetString());
            using var finPayload = JsonDocument.Parse(evt.GetProperty("PayloadJson").GetString()!);
            Assert.Equal(shipment.Number, finPayload.RootElement.GetProperty("shipmentId").GetString());
            Assert.Equal("price-owner", finPayload.RootElement.GetProperty("userName").GetString());
            if (operation == "wms-cancel")
            {
                using var replay = await host.Send(OperationsRequest(cookies, HttpMethod.Post, route));
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Equal(after, await ReadW620(observer, h.Tenant, shipment.Number));
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W620_producer_generated_failed_ship_retries_same_intent_without_partial_ledger()
    {
        await using var h = await Seed();
        var shipment = await AllocateFulfilment(h, onlyLastLine: true);
        await SeedAr(h, "invoice-create");
        await h.Db.PostingRules.Where(r => r.EventType == "AR.Revenue")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));
        await using var host = await StartW620Api(h);
        var cookies = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        using var shipped = await host.Send(OperationsRequest(cookies, HttpMethod.Post,
            $"/api/wms/outbound-order/{shipment.Number}/ship", new { captureReturnSource = true }));
        Assert.True(shipped.StatusCode == HttpStatusCode.OK, await OperationsFailure(shipped));
        var before = await ReadW620(observer, h.Tenant, shipment.Number);
        Assert.Equal(OutboundOrderStatus.Completed, AssetInt(Assert.Single(before.Rows("shipments")), "Status"));
        var original = Assert.Single(before.Rows("events"));
        Assert.Equal((IntegrationEventStatus.Failed, "E-FIN-150", 1),
            (original.GetProperty("Status").GetString(), original.GetProperty("LastError").GetString(), AssetInt(original, "Attempts")));
        Assert.Empty(before.Rows("invoices"));
        Assert.Empty(before.Rows("journals"));
        var f = new W620Fixture(shipment.Number, h.Number, AssetId(original), AssetId(original, "CorrelationId"),
            Guid.Empty, Guid.Empty, Guid.Empty);

        await h.Db.PostingRules.Where(r => r.EventType == "AR.Revenue")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, true));
        await h.Db.IntegrationEvents.Where(e => e.Id == f.Event)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextRetryAt, DateTime.UtcNow.AddMinutes(-1)));
        await using var services = W620Services(h.Tenant);
        await W620Worker(services).ProcessOnceAsync();
        var after = await ReadW620(observer, h.Tenant, shipment.Number);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "real producer failure followed by the real retry", h.Tenant, f.Shipment, f.Event, f.Correlation,
            ShipStatus = (int)shipped.StatusCode, Before = W620Evidence(before), After = W620Evidence(after),
        }));
        CheckW620LegacyIdentity(before, after, f, 2, IntegrationEventStatus.Success);
        var posted = Assert.Single(after.Rows("invoices"));
        Assert.Equal(shipment.Number, posted.GetProperty("ShipmentId").GetString());
        Assert.Equal((int)ArInvoiceStatus.Posted, AssetInt(posted, "Status"));
        Assert.Single(after.Rows("events"));
    }

    [CurrentPermissionPostgresFact]
    public async Task W620_producer_intent_commits_with_stock_before_FIN_execution()
    {
        await using var h = await Seed();
        var shipment = await AllocateFulfilment(h, onlyLastLine: true);
        await SeedAr(h, "invoice-create");
        await using var host = await StartW620Api(h);
        var cookies = await Login(host, h);
        var connection = h.Db.Database.GetConnectionString();
        await using var observer = new NpgsqlConnection(connection);
        await using var intentBarrier = new NpgsqlConnection(connection);
        await using var financeBarrier = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await intentBarrier.OpenAsync();
        await financeBarrier.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, shipment.Number);
        await using var intentHold = await intentBarrier.BeginTransactionAsync();
        await using var financeHold = await financeBarrier.BeginTransactionAsync();
        await using var intentLock = new NpgsqlCommand("""LOCK TABLE public."T_IntegrationEvent" IN SHARE MODE""", intentBarrier, intentHold);
        await using var financeLock = new NpgsqlCommand("""LOCK TABLE public."Fin_Sequence" IN SHARE MODE""", financeBarrier, financeHold);
        await intentLock.ExecuteNonQueryAsync();
        await financeLock.ExecuteNonQueryAsync();

        var running = host.Send(OperationsRequest(cookies, HttpMethod.Post,
            $"/api/wms/outbound-order/{shipment.Number}/ship", new { captureReturnSource = true }));
        var staged = await WaitForFence(observer, intentBarrier.ProcessID, "%INSERT%T_IntegrationEvent%", null, running);
        Assert.Equal(before, await ReadW620(observer, h.Tenant, shipment.Number));
        await intentHold.RollbackAsync();
        var blockedFinance = await WaitForFence(observer, financeBarrier.ProcessID, "%INSERT%Fin_Sequence%", null, running);
        var committed = await ReadW620(observer, h.Tenant, shipment.Number);
        Assert.Equal(OutboundOrderStatus.Completed, AssetInt(Assert.Single(committed.Rows("shipments")), "Status"));
        Assert.Single(committed.Rows("stockMoves"), row => row.GetProperty("TxnType").GetString() == "OUT");
        var evt = Assert.Single(committed.Rows("events"));
        Assert.Equal((IntegrationEventStatus.Pending, 0), (evt.GetProperty("Status").GetString(), AssetInt(evt, "Attempts")));
        Assert.Empty(committed.Rows("invoices"));
        await financeHold.RollbackAsync();
        using var result = await running;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        var completed = await ReadW620(observer, h.Tenant, shipment.Number);
        var f = new W620Fixture(shipment.Number, h.Number, AssetId(evt), AssetId(evt, "CorrelationId"), Guid.Empty, Guid.Empty, Guid.Empty);
        CheckW620LegacyIdentity(committed, completed, f, 1, IntegrationEventStatus.Success);
        Assert.Single(completed.Rows("events"));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            f.Event, Staged = staged, Finance = blockedFinance, Before = W620Evidence(before),
            ProducerCommitted = W620Evidence(committed), FinanceCommitted = W620Evidence(completed)
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W620_lost_producer_and_FIN_commit_acknowledgements_recover_one_event()
    {
        await using var h = await Seed();
        var shipment = await AllocateFulfilment(h, onlyLastLine: true);
        await SeedAr(h, "invoice-create");
        var connection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        await using var observer = new NpgsqlConnection(connection.ConnectionString);
        await observer.OpenAsync();
        var route = $"/api/wms/outbound-order/{shipment.Number}/ship";
        await using (var relay = new StockPostgresCommitRelay(connection.Port, connection.Username!, 56937))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Port = relay.Port };
            await using var host = await StartW620Api(h, forwarded.ConnectionString);
            var cookies = await Login(host, h);
            relay.Arm();
            using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Post, route, new { captureReturnSource = true }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await OperationsFailure(result), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }

        var original = await ReadW620(observer, h.Tenant, shipment.Number);
        Assert.Equal(OutboundOrderStatus.Completed, AssetInt(Assert.Single(original.Rows("shipments")), "Status"));
        Assert.Empty(original.Rows("invoices"));
        var evt = Assert.Single(original.Rows("events"));
        Assert.Equal((IntegrationEventStatus.Pending, 0), (evt.GetProperty("Status").GetString(), AssetInt(evt, "Attempts")));
        var f = new W620Fixture(shipment.Number, h.Number, AssetId(evt), AssetId(evt, "CorrelationId"), Guid.Empty, Guid.Empty, Guid.Empty);
        await using (var relay = new StockPostgresCommitRelay(connection.Port, connection.Username!, 56938))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Port = relay.Port };
            await using var services = W620Services(h.Tenant, forwarded.ConnectionString);
            relay.Arm();
            await W620Worker(services).ProcessOnceAsync();
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }
        var committed = await ReadW620(observer, h.Tenant, shipment.Number);
        CheckW620LegacyIdentity(original, committed, f, 1, IntegrationEventStatus.Success);
        var invoice = Assert.Single(committed.Rows("invoices"));
        Assert.Equal("price-owner", invoice.GetProperty("Creator").GetString());
        Assert.Equal(invoice.GetProperty("No").GetString(), Assert.Single(committed.Rows("events")).GetProperty("TargetNo").GetString());
        await using var freshServices = W620Services(h.Tenant);
        await W620Worker(freshServices).ProcessOnceAsync();
        Assert.Equal(committed, await ReadW620(observer, h.Tenant, shipment.Number));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            f.Event, f.Correlation, ProducerCommitAcknowledgementLost = true, FinanceCommitAcknowledgementLost = true,
            Original = W620Evidence(original), Committed = W620Evidence(committed)
        }));
    }
}
