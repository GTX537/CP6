using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const decimal W620Qty = 12.34567891m;
    private const decimal W620Price = 100.0123m;
    private const decimal W620Net = 1234.72m;
    private const decimal W620Tax = 154.34m;
    private const decimal W620Cost = 91.23m;
    private static readonly DateTime W620Date = new(2026, 6, 15);

    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task W620_selected_shipment_retry_and_real_Main_pause_use_one_commit_order(bool cancellation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var f = await SeedW620Event(h, cancellation);
        await using var services = W620Services(h.Tenant);
        var worker = W620Worker(services);
        await using var host = await StartW620Api(h);
        var manager = await Login(host, admin);
        var connection = h.Db.Database.GetConnectionString()!;
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("w620-suspend", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await using var financeBarrier = new NpgsqlConnection(connection);
        await using var pauseBarrier = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await financeBarrier.OpenAsync();
        await pauseBarrier.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, f.Shipment);
        CheckW620LegacyIdentity(before, before, f, 1, IntegrationEventStatus.Failed);
        using var cancellationLimit = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        AssetRows afterFirstCommit;
        FenceWait blockedPause;
        FenceWait blockedFinance;
        var verb = cancellation ? "UPDATE" : "INSERT";
        if (pauseFirst)
        {
            await using var tenantHold = await pauseBarrier.BeginTransactionAsync();
            await using var tenantLock = new NpgsqlCommand("""LOCK TABLE public."Sys_Tenants" IN SHARE MODE""", pauseBarrier, tenantHold);
            await tenantLock.ExecuteNonQueryAsync();
            var pausing = Pause();
            blockedPause = await WaitForFence(observer, pauseBarrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            var retry = worker.ProcessOnceAsync(cancellationLimit.Token);
            blockedFinance = await WaitForFence(observer, blockedPause.Backend, "%cp6_authentication_v1.acquire%", null, retry);
            Assert.Equal(before, await ReadW620(observer, h.Tenant, f.Shipment));
            await tenantHold.RollbackAsync();
            using var paused = await pausing;
            var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
            CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
            Assert.False(receipt.Snapshot.Enable);
            afterFirstCommit = await ReadW620(observer, h.Tenant, f.Shipment);
            await retry;
        }
        else
        {
            await using var financeHold = await financeBarrier.BeginTransactionAsync();
            await using var financeLock = new NpgsqlCommand("""LOCK TABLE public."Fin_Sequence" IN SHARE MODE""", financeBarrier, financeHold);
            await financeLock.ExecuteNonQueryAsync();
            var retry = worker.ProcessOnceAsync(cancellationLimit.Token);
            blockedFinance = await WaitForFence(observer, financeBarrier.ProcessID, $"%{verb}%Fin_Sequence%", null, retry);
            Assert.Equal(before, await ReadW620(observer, h.Tenant, f.Shipment));

            await using var tenantHold = await pauseBarrier.BeginTransactionAsync();
            await using var tenantLock = new NpgsqlCommand("""LOCK TABLE public."Sys_Tenants" IN SHARE MODE""", pauseBarrier, tenantHold);
            await tenantLock.ExecuteNonQueryAsync();
            var pausing = Pause();
            blockedPause = await WaitForFence(observer, blockedFinance.Backend, "%cp6_authentication_v1.acquire%", null, pausing);
            Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            await financeHold.RollbackAsync();
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitForFence(observer, pauseBarrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            var financeAuthority = authority with { RootRevision = authority.RootRevision + 1 };
            Assert.Equal(financeAuthority, await ReadPauseAuthority(h, admin.User));
            afterFirstCommit = await ReadW620(observer, h.Tenant, f.Shipment);
            await tenantHold.RollbackAsync();
            using var paused = await pausing;
            var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
            CheckPause(financeAuthority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
            Assert.False(receipt.Snapshot.Enable);
        }

        var after = await ReadW620(observer, h.Tenant, f.Shipment);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "selected retry and signed Main pause", Cancellation = cancellation, PauseFirst = pauseFirst,
            h.Tenant, f.Shipment, f.Event, f.Correlation,
            WorkerStatement = verb + " Fin_Sequence", BlockedFinance = blockedFinance, BlockedPause = blockedPause,
            Before = W620Evidence(before), AfterFirstCommit = W620Evidence(afterFirstCommit), After = W620Evidence(after),
        }));
        if (pauseFirst)
        {
            Assert.Equal(before, afterFirstCommit);
            Assert.Equal(before, after);
        }
        else
        {
            CheckW620CompletedFinance(before, after, f, cancellation);
            Assert.Equal(afterFirstCommit, after);
        }

        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W620_failed_shipment_retry_recovers_original_event_and_both_accounting_legs(bool cancellation)
    {
        await using var h = await Seed();
        var f = await SeedW620Event(h, cancellation);
        if (cancellation)
        {
            await h.Db.JournalEntries.Where(j => j.Id == f.CostVoucher)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JournalStatus.PendingReview));
        }
        else
        {
            await h.Db.PostingRules.Where(r => r.EventType == "AR.Cogs")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));
        }
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, f.Shipment);
        await using var services = W620Services(h.Tenant);
        var worker = W620Worker(services);
        await worker.ProcessOnceAsync();
        var failed = await ReadW620(observer, h.Tenant, f.Shipment);
        CheckW620LegacyIdentity(before, failed, f, 2, IntegrationEventStatus.Failed);
        Assert.NotEqual(JsonValueKind.Null, failed.Row("events", f.Event).GetProperty("NextRetryAt").ValueKind);

        if (cancellation)
        {
            await h.Db.JournalEntries.Where(j => j.Id == f.CostVoucher)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JournalStatus.Posted));
        }
        else
        {
            await h.Db.PostingRules.Where(r => r.EventType == "AR.Cogs")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, true));
        }
        Assert.Equal(cancellation ? "E-FIN-120" : "E-FIN-150",
            Assert.Single(failed.Rows("events")).GetProperty("LastError").GetString());
        CheckSameW620Finance(before, failed);
        await h.Db.IntegrationEvents.Where(e => e.Id == f.Event)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextRetryAt, DateTime.UtcNow.AddMinutes(-1)));
        await worker.ProcessOnceAsync();
        var retried = await ReadW620(observer, h.Tenant, f.Shipment);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "later attempt after a finance leg failed", Cancellation = cancellation,
            h.Tenant, f.Shipment, f.Event, f.Correlation,
            Before = W620Evidence(before), FailedAttempt = W620Evidence(failed), Retried = W620Evidence(retried),
        }));

        var final = Assert.Single(retried.Rows("invoices"));
        if (cancellation) Assert.Equal(f.Invoice, AssetId(final));
        Assert.Equal(cancellation ? (int)ArInvoiceStatus.Reversed : (int)ArInvoiceStatus.Posted, AssetInt(final, "Status"));
        CheckW620LegacyIdentity(before, retried, f, 3, IntegrationEventStatus.Success);
        Assert.Single(retried.Rows("events"));
    }

    private static void CheckSameW620Finance(AssetRows before, AssetRows after)
    {
        foreach (var table in new[] { "shipments", "outboundLines", "stocks", "stockMoves", "invoices", "invoiceLines",
            "journals", "lines", "periods", "numbers", "audit" })
            Assert.Equal(before.Rows(table).Select(row => row.GetRawText()), after.Rows(table).Select(row => row.GetRawText()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W620_archived_shipment_recovers_original_partial_finance(bool cancellation)
    {
        await using var h = await Seed();
        var f = await SeedW620Event(h, cancellation);
        var seq = new FinSequenceService(h.Db);
        var journal = new JournalEntryService(h.Db, new FiscalPeriodService(h.Db, 1), seq);
        Guid originalInvoice;
        Guid originalRevenue;
        Guid knownVoucher;
        if (cancellation)
        {
            var prior = await journal.ReverseAsync(f.RevenueVoucher, "legacy-shipper", "出货取消", autoPost: true);
            Assert.True(prior.Ok, prior.Code);
            originalInvoice = f.Invoice;
            originalRevenue = f.RevenueVoucher;
            knownVoucher = await h.Db.JournalEntries.Where(j => j.ReverseOfId == f.RevenueVoucher)
                .Select(j => j.Id).SingleAsync();
        }
        else
        {
            var payload = await h.Db.IntegrationEvents.Where(e => e.Id == f.Event).Select(e => e.PayloadJson).SingleAsync();
            var request = JsonSerializer.Deserialize<FinShipmentInvoiceRequest>(payload)!;
            await h.Db.PostingRules.Where(r => r.EventType == "AR.Cogs")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));
            var ar = new ArInvoiceService(h.Db, new AutoVoucherEngine(h.Db, journal), journal, seq);
            var prior = await ar.CreateFromShipmentAsync(request, "legacy-shipper");
            Assert.Equal("E-FIN-150", prior.Result.Code);
            originalInvoice = prior.InvoiceId!.Value;
            // The historical BridgeHook SaveChanges also flushed the tracked revenue link.
            await h.Db.SaveChangesAsync();
            originalRevenue = await h.Db.ArInvoices.Where(i => i.Id == originalInvoice)
                .Select(i => i.JournalEntryId!.Value).SingleAsync();
            knownVoucher = originalRevenue;
            await h.Db.PostingRules.Where(r => r.EventType == "AR.Cogs")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, true));
        }
        await h.Db.Sys_Tenants.IgnoreQueryFilters().Where(t => t.Id == h.Tenant)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, (DateTime?)null));
        await h.Db.OutboundOrders.Where(o => o.OutboundNo == f.Shipment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsDeleted, true));
        h.Db.ChangeTracker.Clear();
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, f.Shipment);
        await using var services = W620Services(h.Tenant);
        var worker = W620Worker(services);
        await worker.ProcessOnceAsync();
        var after = await ReadW620(observer, h.Tenant, f.Shipment);
        await worker.ProcessOnceAsync();
        Assert.Equal(after, await ReadW620(observer, h.Tenant, f.Shipment));
        CheckW620LegacyIdentity(before, after, f, 2, IntegrationEventStatus.Success);
        var invoice = Assert.Single(after.Rows("invoices"));
        Assert.Equal(originalInvoice, AssetId(invoice));
        Assert.Equal(originalRevenue, AssetId(invoice, "JournalEntryId"));
        Assert.Equal(cancellation ? (int)ArInvoiceStatus.Reversed : (int)ArInvoiceStatus.Posted, AssetInt(invoice, "Status"));
        Assert.Equal(before.Row("journals", knownVoucher).GetRawText(), after.Row("journals", knownVoucher).GetRawText());
        Assert.Equal(before.Rows("lines").Where(row => AssetId(row, "EntryId") == knownVoucher).Select(row => row.GetRawText()),
            after.Rows("lines").Where(row => AssetId(row, "EntryId") == knownVoucher).Select(row => row.GetRawText()));
        Assert.Equal(Assert.Single(before.Rows("invoiceLines")).GetRawText(), Assert.Single(after.Rows("invoiceLines")).GetRawText());
        if (cancellation) AssertArReversal(before, after, f.CostVoucher, W620Cost);
        else Assert.Equal(2, CheckApJournal(after, AssetId(invoice, "CostJournalEntryId"), invoice.GetProperty("No").GetString()!,
            W620Cost, VoucherSource.Cost).Length);
        Assert.Single(after.Rows("events"));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "archived source and original partial finance", Cancellation = cancellation, f.Event, originalInvoice, knownVoucher,
            Before = W620Evidence(before), After = W620Evidence(after),
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W620_expiry_after_finance_starts_rolls_back_ledger_and_original_event()
    {
        await using var h = await Seed();
        var f = await SeedW620Event(h, false);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await using var barrier = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        await barrier.OpenAsync();
        await using var locked = await barrier.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("""LOCK TABLE public."Fin_Sequence" IN SHARE MODE""", barrier, locked);
        await hold.ExecuteNonQueryAsync();
        var expiration = DateTime.Now.AddSeconds(12);
        await h.Db.Sys_Tenants.IgnoreQueryFilters().Where(t => t.Id == h.Tenant)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, expiration));
        var before = await ReadW620(observer, h.Tenant, f.Shipment);
        await using var services = W620Services(h.Tenant);
        var retry = W620Worker(services).ProcessOnceAsync();
        var wait = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Fin_Sequence%", null, retry);
        Assert.True(DateTime.Now < expiration);
        var delay = expiration - DateTime.Now;
        if (delay > TimeSpan.Zero) await Task.Delay(delay + TimeSpan.FromMilliseconds(50));
        await locked.RollbackAsync();
        await retry;
        Assert.Equal(before, await ReadW620(observer, h.Tenant, f.Shipment));
        output.WriteLine(JsonSerializer.Serialize(new { f.Event, expiration, Wait = wait, Unchanged = true }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W620_due_pending_route_does_not_redirect_a_shipment_or_admit_another_hook()
    {
        await using var h = await Seed();
        var f = await SeedW620Event(h, false);
        var original = await h.Db.IntegrationEvents.SingleAsync(e => e.Id == f.Event);
        var json = JsonNode.Parse(original.PayloadJson)!;
        json["ShipmentId"] = f.Shipment + "-other";
        original.PayloadJson = json.ToJsonString();
        original.Status = IntegrationEventStatus.Pending;
        original.Attempts = 0;
        var unrelated = new IntegrationEvent
        {
            Id = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), SourceModule = "WMS", TargetModule = "FIN",
            SourceNo = f.Shipment, HookName = "UnrelatedHook", Status = IntegrationEventStatus.Pending,
            NextRetryAt = original.NextRetryAt, PayloadJson = original.PayloadJson, CreateDate = DateTime.Now
        };
        h.Db.IntegrationEvents.Add(unrelated);
        await h.Db.SaveChangesAsync();
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW620(observer, h.Tenant, f.Shipment);
        await using var services = W620Services(h.Tenant);
        await W620Worker(services).ProcessOnceAsync();
        var after = await ReadW620(observer, h.Tenant, f.Shipment);
        CheckSameW620Finance(before, after);
        CheckW620LegacyIdentity(before, after, f, 1, IntegrationEventStatus.Failed);
        Assert.Equal("DISPATCH-400", after.Row("events", f.Event).GetProperty("LastError").GetString());
        Assert.Equal(before.Row("events", unrelated.Id).GetRawText(), after.Row("events", unrelated.Id).GetRawText());
        Assert.Equal(2, after.Rows("events").Length);
    }

    private sealed record W620Fixture(string Shipment, string Order, Guid Event, Guid Correlation,
        Guid Invoice, Guid RevenueVoucher, Guid CostVoucher);



    private ServiceProvider W620Services(Guid tenant, string? connection = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp =>
        {
            var context = server.Context((TenantContext)sp.GetRequiredService<ITenantContext>());
            if (connection is not null) context.Database.SetConnectionString(connection);
            return context;
        });
        services.AddScoped<TenantEnumerator>();
        services.AddScoped<ITenantEnumerator>(sp => new OaEventTenantEnumerator(sp.GetRequiredService<TenantEnumerator>(), tenant));
        services.AddScoped<IFinSequenceService, FinSequenceService>();
        services.AddScoped<IFiscalPeriodService>(sp => new FiscalPeriodService(sp.GetRequiredService<CP6Context>(), 1));
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<IAutoVoucherEngine, AutoVoucherEngine>();
        services.AddScoped<IArInvoiceService, ArInvoiceService>();
        services.AddScoped<IProcessCostRateService, ProcessCostRateService>();
        services.AddScoped<ICostCollectService, CostCollectService>();
        services.AddScoped<ICostSettleService, CostSettleService>();
        services.AddScoped<IFinBridgeHook, FinBridgeHook>();
        services.AddScoped<IDeadLetterNotifier, DeadLetterNotifier>();
        services.AddScoped<IIntegrationEventDispatcher>(sp => new IntegrationEventDispatcher(
            new NoOpMesBridgeHook(), new NoOpWmsBridgeHook(), new NoOpErpBridgeHook(), new NoOpOrderCancelBridgeHook(),
            sp.GetRequiredService<IFinBridgeHook>(), new Mock<ISpaceBridgeHook>(MockBehavior.Strict).Object));
        return services.BuildServiceProvider();
    }

    private static IntegrationEventRetryWorker W620Worker(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationEventOptions { Enabled = true, MaxAttempts = 5 }),
            NullLogger<IntegrationEventRetryWorker>.Instance);

    private static Task<AuthenticationApiProcess> StartW620Api(Harness h, string? connection = null)
    {
        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:56936";
        environment["IntegrationEvent__Enabled"] = "false";
        return AuthenticationApiProcess.Start(connection ?? h.Db.Database.GetConnectionString()!, environment);
    }

private static async Task<W620Fixture> SeedW620Event(Harness h, bool cancellation)
    {
        var accounts = await SeedAr(h, "invoice-create");
        var db = h.Db;
        var shipment = "OB620-" + h.Tenant.ToString("N")[..8];
        await db.OrderDetails.Where(d => d.WebOrderNo == h.Number && d.WebOrderDetailNo == 1)
            .ExecuteUpdateAsync(update => update.SetProperty(d => d.Quantity, W620Qty)
                .SetProperty(d => d.IndividualUnitPrice, W620Price).SetProperty(d => d.Amount, W620Net));
        db.OutboundOrders.Add(new OutboundOrder
        {
            Id = Guid.NewGuid(), OutboundNo = shipment, WebOrderNo = h.Number, CustomerCd = "C",
            WarehouseCd = "W01", OutboundType = OutboundType.Shipping, PlannedDate = W620Date,
            Status = cancellation ? OutboundOrderStatus.Cancelled : OutboundOrderStatus.Completed,
            Creator = "legacy-shipper",
        });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail
        {
            Id = Guid.NewGuid(), OutboundNo = shipment, LineNo = 1, ProductCd = "P1", UnitCd = "EA", WarehouseCd = "W01",
            RequiredQty = W620Qty, AllocatedQty = W620Qty, ShippedQty = W620Qty, UnitPrice = W620Price, ShipTxnNo = "OUT617-1",
            Creator = "legacy-shipper",
        });
        await db.SaveChangesAsync();
        var request = new FinShipmentInvoiceRequest
        {
            ShipmentId = shipment, OrderId = h.Number, CustomerId = "C", InvoiceDate = W620Date, DueDate = W620Date.AddDays(30),
            CurrencyCd = "USD", FxRate = 1.25m, EstimatedCost = W620Cost,
            Lines =
            [
                new FinShipmentInvoiceLine { ItemId = "P1", Qty = W620Qty, UnitPrice = W620Price, TaxCodeId = accounts.Tax },
            ],
        };
        var invoice = Guid.Empty;
        var revenue = Guid.Empty;
        var cost = Guid.Empty;
        if (cancellation)
        {
            var seq = new FinSequenceService(db);
            var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), seq);
            var ar = new ArInvoiceService(db, new AutoVoucherEngine(db, journal), journal, seq);
            var posted = await ar.CreateFromShipmentAsync(request, "legacy-shipper");
            Assert.True(posted.Result.Ok, posted.Result.Code);
            var saved = await db.ArInvoices.AsNoTracking().SingleAsync(x => x.Id == posted.InvoiceId);
            invoice = saved.Id;
            revenue = saved.JournalEntryId!.Value;
            cost = saved.CostJournalEntryId!.Value;
        }

        var evt = new IntegrationEvent
        {
            Id = Guid.NewGuid(), SourceModule = "WMS", TargetModule = "FIN", SourceNo = shipment, CorrelationId = Guid.NewGuid(),
            HookName = cancellation ? nameof(IFinBridgeHook.OnShipmentCancelledAsync) : nameof(IFinBridgeHook.OnShipmentConfirmedAsync),
            Status = IntegrationEventStatus.Failed, Attempts = 1, LastError = "historical FIN attempt failed",
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1), Creator = "historical-operator", CreateDate = DateTime.Now,
            PayloadJson = cancellation
                ? JsonSerializer.Serialize(new { shipmentId = shipment, userName = "legacy-shipper" })
                : JsonSerializer.Serialize(request),
        };
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(shipment, h.Number, evt.Id, evt.CorrelationId, invoice, revenue, cost);
    }

    private static async Task<AssetRows> ReadW620(NpgsqlConnection observer, Guid tenant, string shipment)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'shipments', ARRAY(SELECT to_jsonb(r) FROM public."T_OutboundOrder" r WHERE r."TenantId"=@tenant AND r."OutboundNo"=@shipment ORDER BY r."Id"),
              'outboundLines', ARRAY(SELECT to_jsonb(r) FROM public."T_OutboundOrderDetail" r WHERE r."TenantId"=@tenant AND r."OutboundNo"=@shipment ORDER BY r."Id"),
              'stockMoves', ARRAY(SELECT to_jsonb(r) FROM public."T_StockTransaction" r WHERE r."TenantId"=@tenant AND r."RelatedNo"=@shipment ORDER BY r."Id"),
              'stocks', ARRAY(SELECT to_jsonb(r) FROM public."T_Stock" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'invoices', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ArInvoice" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'invoiceLines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ArInvoiceLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'events', ARRAY(SELECT to_jsonb(r) FROM public."T_IntegrationEvent" r WHERE r."TenantId"=@tenant AND r."SourceModule"='WMS'
                AND r."TargetModule"='FIN' AND r."SourceNo"=@shipment ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('ArInvoice','ArInvoiceLine','JournalEntry','JournalLine','FiscalPeriod','FinSequence','IntegrationEvent')
                ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("shipment", shipment);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static JsonElement W620Evidence(AssetRows rows)
    {
        using var document = JsonDocument.Parse(rows.Data);
        return document.RootElement.Clone();
    }

    private static void CheckW620LegacyIdentity(AssetRows before, AssetRows after, W620Fixture f, int attempts, string status)
    {
        var original = before.Row("events", f.Event);
        var result = after.Row("events", f.Event);
        Assert.Equal(f.Correlation, AssetId(result, "CorrelationId"));
        Assert.Equal(f.Shipment, result.GetProperty("SourceNo").GetString());
        Assert.Equal((attempts, status), (AssetInt(result, "Attempts"), result.GetProperty("Status").GetString()));
        foreach (var field in new[] { "Id", "CorrelationId", "SourceModule", "TargetModule", "HookName", "SourceNo", "PayloadJson", "Creator", "TenantId" })
            Assert.Equal(original.GetProperty(field).GetRawText(), result.GetProperty(field).GetRawText());
    }

    private static void CheckW620CompletedFinance(AssetRows before, AssetRows after, W620Fixture f, bool cancellation)
    {
        CheckW620LegacyIdentity(before, after, f, 2, IntegrationEventStatus.Success);
        Assert.Equal(JsonValueKind.Null, after.Row("events", f.Event).GetProperty("NextRetryAt").ValueKind);
        var invoice = Assert.Single(after.Rows("invoices"));
        Assert.Equal((f.Shipment, f.Order), (invoice.GetProperty("ShipmentId").GetString(), invoice.GetProperty("OrderId").GetString()));
        Assert.Equal((W620Net, W620Tax, W620Net + W620Tax, W620Cost),
            (AssetDecimal(invoice, "NetAmount"), AssetDecimal(invoice, "TaxAmount"), AssetDecimal(invoice, "GrossAmount"), AssetDecimal(invoice, "CostAmount")));
        var line = Assert.Single(after.Rows("invoiceLines"));
        Assert.Equal((AssetId(invoice), W620Qty, W620Price, W620Net, W620Tax),
            (AssetId(line, "InvoiceId"), AssetDecimal(line, "Qty"), AssetDecimal(line, "UnitPrice"),
                AssetDecimal(line, "Amount"), AssetDecimal(line, "TaxAmount")));
        var shipment = Assert.Single(after.Rows("shipments"));
        Assert.Equal(AssetId(shipment, "TenantId"), AssetId(invoice, "TenantId"));
        Assert.Equal(AssetId(invoice, "TenantId"), AssetId(line, "TenantId"));
        Assert.Equal(W620Qty, AssetDecimal(Assert.Single(after.Rows("outboundLines")), "ShippedQty"));
        foreach (var table in new[] { "shipments", "outboundLines", "stocks", "stockMoves", "audit" })
            Assert.Equal(before.Rows(table).Select(r => r.GetRawText()), after.Rows(table).Select(r => r.GetRawText()));
        var sequence = ApSequences(after);
        if (cancellation)
        {
            Assert.Equal(f.Invoice, AssetId(invoice));
            Assert.Equal((int)ArInvoiceStatus.Reversed, AssetInt(invoice, "Status"));
            Assert.Equal(Assert.Single(before.Rows("invoiceLines")).GetRawText(), line.GetRawText());
            AssertArReversal(before, after, f.RevenueVoucher, 1736.33m);
            AssertArReversal(before, after, f.CostVoucher, W620Cost);
            Assert.Equal((1, 4), (sequence["AR"], sequence["GL"]));
        }
        else
        {
            Assert.Equal((int)ArInvoiceStatus.Posted, AssetInt(invoice, "Status"));
            var no = invoice.GetProperty("No").GetString()!;
            Assert.Equal("system", invoice.GetProperty("Creator").GetString());
            Assert.Equal(3, CheckApJournal(after, AssetId(invoice, "JournalEntryId"), no, 1736.33m, VoucherSource.AR).Length);
            Assert.Equal(2, CheckApJournal(after, AssetId(invoice, "CostJournalEntryId"), no, W620Cost, VoucherSource.Cost).Length);
            Assert.Equal((1, 2), (sequence["AR"], sequence["GL"]));
        }

        var original = Assert.Single(after.Rows("events"));
        Assert.Equal(invoice.GetProperty("No").GetString(), original.GetProperty("TargetNo").GetString());
        Assert.Equal(JsonValueKind.Null, original.GetProperty("LastError").ValueKind);
    }
}
