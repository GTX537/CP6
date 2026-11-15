using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const decimal W636Price = 12.3456m;
    private const decimal W636Amount = 24.69m;
    private const string W636ReceiptPath = "/api/wms/inbound-receipt";

    [CurrentPermissionPostgresFact]
    public async Task W636_receipt_commits_stock_and_intent_then_posts_original_unrounded_valuation()
    {
        await using var h = await Seed();
        var book = await SeedW636Book(h);
        await GrantProductionInbound(h);
        await using var host = await StartW636Api(h);
        var actor = await Login(host, h);
        const decimal originalPrice = 1.00249m;
        var receipt = await PrepareW636Receipt(host, actor, originalPrice);
        var connection = h.Db.Database.GetConnectionString();
        await using var observer = new NpgsqlConnection(connection);
        await using var staging = new NpgsqlConnection(connection);
        await using var finance = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await staging.OpenAsync();
        await finance.OpenAsync();
        var before = await ReadW636(observer, h.Tenant);
        await using var stagingHold = await staging.BeginTransactionAsync();
        await using var financeHold = await finance.BeginTransactionAsync();
        await using var eventLock = new NpgsqlCommand("""LOCK TABLE public."T_IntegrationEvent" IN SHARE MODE""", staging, stagingHold);
        await using var journalLock = new NpgsqlCommand("""LOCK TABLE public."Fin_JournalEntry" IN SHARE MODE""", finance, financeHold);
        await eventLock.ExecuteNonQueryAsync();
        await journalLock.ExecuteNonQueryAsync();

        var receiving = ReceiveW636(host, actor, receipt);
        var stagingWait = await WaitForFence(observer, staging.ProcessID, "%INSERT%T_IntegrationEvent%", null, receiving);
        Assert.Equal(before, await ReadW636(observer, h.Tenant));
        await stagingHold.RollbackAsync();

        var financeWait = await WaitForFence(observer, finance.ProcessID, "%INSERT%Fin_JournalEntry%", null, receiving);
        var pending = await ReadW636(observer, h.Tenant);
        var movement = CheckW636Receipt(pending, 1.0025m);
        var evt = CheckW636Event(pending, movement, IntegrationEventStatus.Pending, 0);
        using (var payload = JsonDocument.Parse(evt.GetProperty("PayloadJson").GetString()!))
        {
            Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(AssetId(movement), payload.RootElement.GetProperty("stockTransactionId").GetGuid());
            Assert.Equal(originalPrice, payload.RootElement.GetProperty("UnitPrice").GetDecimal());
        }
        Assert.Empty(pending.Rows("journals"));
        Assert.Empty(pending.Rows("fin-sequence"));

        await financeHold.RollbackAsync();
        using var result = await receiving;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        var final = await ReadW636(observer, h.Tenant);
        CheckW636Identity(pending, final);
        CheckW636Event(final, movement, IntegrationEventStatus.Success, 1, "Inventory.Received");
        CheckW636Voucher(final, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], 2.00m);
        Assert.Equal(1, AssetInt(Assert.Single(final.Rows("fin-sequence")), "CurrentValue"));
        output.WriteLine(JsonSerializer.Serialize(new { stagingWait, financeWait, Original = W636State(before),
            Committed = W636State(pending), Final = W636State(final) }));
    }

    [TenantManagementPostgresTheory]
    [InlineData("new")]
    [InlineData("legacy")]
    [InlineData("already-posted")]
    [InlineData("reversed")]
    public async Task W636_worker_recovers_original_stock_event_and_honors_original_ledger(string history)
    {
        await using var h = await Seed();
        var posted = history is "already-posted" or "reversed";
        var book = await SeedW636Book(h, closed: !posted);
        await GrantProductionInbound(h);
        if (history == "reversed")
        {
            await GrantLegacyAuthorityAction(h, "fin-journal", "reverse");
            await GrantLegacyAuthorityAction(h, "fin-journal", "post");
            await SeedW618Checker(h);
        }
        await using var host = await StartW636Api(h);
        var actor = await Login(host, h);
        var request = await PrepareW636Receipt(host, actor);
        using var response = await ReceiveW636(host, actor, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var first = await ReadW636(observer, h.Tenant);
        var movement = CheckW636Receipt(first, W636Price);
        var stock = await h.Db.StockTransactions.AsNoTracking().SingleAsync();
        var evt = CheckW636Event(first, movement, posted ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed, 1,
            posted ? "Inventory.Received" : null, posted ? null : "E-FIN-112");
        if (posted)
            CheckW636Voucher(first, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
        else
        {
            Assert.Empty(first.Rows("journals"));
            Assert.Empty(first.Rows("fin-sequence"));
        }
        if (history == "reversed")
        {
            var originalId = AssetId(Assert.Single(first.Rows("journals")));
            using var reversed = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/fin/journal/{originalId}/reverse",
                new { Reason = "simulated original reversal" }));
            Assert.True(reversed.StatusCode == HttpStatusCode.OK, await OperationsFailure(reversed));
            var reversalId = await h.Db.JournalEntries.AsNoTracking().Where(row => row.ReverseOfId == originalId).Select(row => row.Id).SingleAsync();
            var checker = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
            using var approval = await host.Send(OperationsRequest(checker, HttpMethod.Post, $"/api/fin/journal/{reversalId}/post"));
            Assert.True(approval.StatusCode == HttpStatusCode.OK, await OperationsFailure(approval));
            Assert.Equal(JournalStatus.Posted, await h.Db.JournalEntries.AsNoTracking()
                .Where(row => row.Id == reversalId).Select(row => row.Status).SingleAsync());
        }

        await h.Db.FiscalPeriods.Where(p => p.Id == book.Period).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, posted ? PeriodStatus.Closed : PeriodStatus.Open));
        await SetW636Due(h, AssetId(evt), history == "new" ? null : W636LegacyPayload(stock));
        var ready = await ReadW636(observer, h.Tenant);
        await using var services = W620Services(h.Tenant);
        var worker = W620Worker(services);
        await worker.ProcessOnceAsync();

        var final = await ReadW636(observer, h.Tenant);
        CheckW636Identity(ready, final);
        if (history == "reversed")
        {
            CheckW636Event(final, movement, IntegrationEventStatus.Failed, 2, error: "DISPATCH-400");
            Assert.Equal(2, final.Rows("journals").Length);
        }
        else
        {
            CheckW636Event(final, movement, IntegrationEventStatus.Success, 2, "Inventory.Received");
            CheckW636Voucher(final, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
            Assert.Equal(1, AssetInt(Assert.Single(final.Rows("fin-sequence")), "CurrentValue"));
        }
        if (posted) CheckW636Tables(ready, final, "journals", "journal-lines", "fin-sequence");
        await worker.ProcessOnceAsync();
        Assert.Equal(final, await ReadW636(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { history, First = W636State(first),
            Ready = W636State(ready), Recovered = W636State(final) }));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W636_selected_stock_finance_worker_and_signed_Main_pause_share_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var book = await SeedW636Book(h, closed: true);
        await GrantProductionInbound(h);
        await using var host = await StartW636Api(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        var request = await PrepareW636Receipt(host, actor);
        using var response = await ReceiveW636(host, actor, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        await h.Db.FiscalPeriods.Where(p => p.Id == book.Period)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PeriodStatus.Open));
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(e => e.HookName == "OnStockMovedAsync");
        await SetW636Due(h, evt.Id);

        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var current = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        var suspend = new TenantManagementCommandV1("stock-finance-pause", current.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await using var finance = new NpgsqlConnection(connection);
        await using var directory = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await finance.OpenAsync();
        await directory.OpenAsync();
        var before = await ReadW636(observer, h.Tenant);
        var movement = CheckW636Receipt(before, W636Price);
        CheckW636Event(before, movement, IntegrationEventStatus.Failed, 1, error: "E-FIN-112");
        await using var services = W620Services(h.Tenant);
        var worker = W620Worker(services);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var pauseHold = await directory.BeginTransactionAsync();
        await using var holdTenant = new NpgsqlCommand("""LOCK TABLE public."Sys_Tenants" IN SHARE MODE""", directory, pauseHold);
        await holdTenant.ExecuteNonQueryAsync();

        FenceWait firstWait;
        FenceWait rootWait;
        Task<HttpResponseMessage> pausing;
        AssetRows afterFirst;
        if (pauseFirst)
        {
            pausing = Pause();
            firstWait = await WaitForFence(observer, directory.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            var retry = worker.ProcessOnceAsync(deadline.Token);
            rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire%", null, retry);
            Assert.Equal(before, await ReadW636(observer, h.Tenant));
            await pauseHold.RollbackAsync();
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            afterFirst = await ReadW636(observer, h.Tenant);
            Assert.Equal(before, afterFirst);
        }
        else
        {
            await using var financeHold = await finance.BeginTransactionAsync();
            await using var holdFinance = new NpgsqlCommand("""LOCK TABLE public."Fin_JournalEntry" IN SHARE MODE""", finance, financeHold);
            await holdFinance.ExecuteNonQueryAsync();
            var retry = worker.ProcessOnceAsync(deadline.Token);
            firstWait = await WaitForFence(observer, finance.ProcessID, "%INSERT%Fin_JournalEntry%", null, retry);
            pausing = Pause();
            rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire%", null, pausing);
            Assert.Equal(before, await ReadW636(observer, h.Tenant));
            Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            await financeHold.RollbackAsync();
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitForFence(observer, directory.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            afterFirst = await ReadW636(observer, h.Tenant);
            CheckW636Event(afterFirst, movement, IntegrationEventStatus.Success, 2, "Inventory.Received");
            CheckW636Voucher(afterFirst, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
            authority = authority with { RootRevision = authority.RootRevision + 1 };
            await pauseHold.RollbackAsync();
        }

        using var paused = await pausing;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        Assert.Equal(afterFirst, await ReadW636(observer, h.Tenant));
        if (pauseFirst)
        {
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
            await worker.ProcessOnceAsync();
        }
        var final = await ReadW636(observer, h.Tenant);
        CheckW636Identity(before, final);
        CheckW636Event(final, movement, IntegrationEventStatus.Success, 2, "Inventory.Received");
        CheckW636Voucher(final, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
        output.WriteLine(JsonSerializer.Serialize(new { pauseFirst, firstWait, rootWait, Original = W636State(before),
            AfterFirst = W636State(afterFirst), Final = W636State(final) }));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), suspend));
    }

    [CurrentPermissionPostgresFact]
    public async Task W636_lost_stock_and_finance_commit_receipts_recover_once()
    {
        await using var h = await Seed();
        var book = await SeedW636Book(h);
        await GrantProductionInbound(h);
        var connection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        await using var observer = new NpgsqlConnection(connection.ConnectionString);
        await observer.OpenAsync();
        await using (var relay = new StockPostgresCommitRelay(connection.Port, connection.Username!, W636RelayPort(57114)))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Port = relay.Port };
            await using var host = await StartW636Api(h, forwarded.ConnectionString);
            var actor = await Login(host, h);
            var request = await PrepareW636Receipt(host, actor);
            relay.Arm();
            using var response = await ReceiveW636(host, actor, request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await OperationsFailure(response), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }

        var original = await ReadW636(observer, h.Tenant);
        var movement = CheckW636Receipt(original, W636Price);
        CheckW636Event(original, movement, IntegrationEventStatus.Pending, 0);
        Assert.Empty(original.Rows("journals"));
        await using (var relay = new StockPostgresCommitRelay(connection.Port, connection.Username!, W636RelayPort(57115)))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Port = relay.Port };
            await using var services = W620Services(h.Tenant, forwarded.ConnectionString);
            relay.Arm();
            await W620Worker(services).ProcessOnceAsync();
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }
        var completed = await ReadW636(observer, h.Tenant);
        CheckW636Identity(original, completed);
        CheckW636Event(completed, movement, IntegrationEventStatus.Success, 1, "Inventory.Received");
        CheckW636Voucher(completed, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
        Assert.Equal(1, AssetInt(Assert.Single(completed.Rows("fin-sequence")), "CurrentValue"));
        await using var fresh = W620Services(h.Tenant);
        await W620Worker(fresh).ProcessOnceAsync();
        Assert.Equal(completed, await ReadW636(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { OriginalAckLost = true, FinanceAckLost = true,
            Original = W636State(original), Completed = W636State(completed) }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W636_disabled_stock_finance_preserves_original_stock_and_due_financial_history()
    {
        await using var h = await Seed();
        var book = await SeedW636Book(h);
        await GrantProductionInbound(h);
        await using var host = await StartW636Api(h, enabled: false);
        var actor = await Login(host, h);
        var request = await PrepareW636Receipt(host, actor);
        using var received = await ReceiveW636(host, actor, request);
        Assert.True(received.StatusCode == HttpStatusCode.OK, await OperationsFailure(received));
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var disabled = await ReadW636(observer, h.Tenant);
        var movement = CheckW636Receipt(disabled, W636Price);
        Assert.Empty(disabled.Rows("events"));
        Assert.Empty(disabled.Rows("journals"));

        var original = await h.Db.StockTransactions.AsNoTracking().SingleAsync();
        h.Db.IntegrationEvents.Add(new IntegrationEvent
        {
            TenantId = h.Tenant, SourceModule = "WMS", TargetModule = "FIN", HookName = "OnStockMovedAsync",
            SourceNo = original.TxnNo, PayloadJson = W636LegacyPayload(original), Creator = "historical-fixture",
            Status = IntegrationEventStatus.Failed, Attempts = 1, NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            LastError = "E-FIN-112", CorrelationId = Guid.NewGuid(),
        });
        await h.Db.SaveChangesAsync();
        var due = await ReadW636(observer, h.Tenant);
        await using var disabledServices = W620Services(h.Tenant, stockFinanceEnabled: false);
        await W620Worker(disabledServices).ProcessOnceAsync();
        Assert.Equal(due, await ReadW636(observer, h.Tenant));

        await using var enabledServices = W620Services(h.Tenant);
        await W620Worker(enabledServices).ProcessOnceAsync();
        var completed = await ReadW636(observer, h.Tenant);
        CheckW636Identity(due, completed);
        CheckW636Event(completed, movement, IntegrationEventStatus.Success, 2, "Inventory.Received");
        CheckW636Voucher(completed, movement, book.Accounts["INVENTORY"], book.Accounts["GRNI"], W636Amount);
    }

    [CurrentPermissionPostgresFact]
    public async Task W636_signed_stocktake_approval_posts_gain_and_loss_for_each_original_adjustment()
    {
        await using var h = await Seed();
        var book = await SeedW636Book(h);
        await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        await using var host = await StartW636Api(h);
        var actor = await Login(host, h);
        using var plan = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/wms/stock-take/plan",
            new { TargetWarehouseCd = "W01", PlannedDate = DateTime.Today }));
        var data = await ReadW636WmsSuccess(plan);
        var path = $"/api/wms/stock-take/{data.GetProperty("stockTakeNo").GetString()}";
        await Send(HttpMethod.Post, "start-count");
        await Send(HttpMethod.Put, "counts", new[]
        {
            new { LineNo = 1, CountedQty = 11.35802467m, DiffReasonCd = "COUNT" },
            new { LineNo = 2, CountedQty = 18.75m, DiffReasonCd = "COUNT" },
        });
        await Send(HttpMethod.Post, "submit");
        await Send(HttpMethod.Post, "approve");
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var final = await ReadW636(observer, h.Tenant);
        var gain = Assert.Single(final.Rows("stock-moves"), move => AssetDecimal(move, "Qty") > 0m);
        var loss = Assert.Single(final.Rows("stock-moves"), move => AssetDecimal(move, "Qty") < 0m);
        Assert.Equal(2, final.Rows("events").Length);
        Assert.Equal(2, final.Rows("journals").Length);
        Assert.Equal(2, AssetInt(Assert.Single(final.Rows("fin-sequence")), "CurrentValue"));
        Assert.Equal((1.23456789m, -1.25m), (AssetDecimal(gain, "Qty"), AssetDecimal(loss, "Qty")));
        Assert.Equal(StockTakeStatus.Completed, AssetInt(Assert.Single(final.Rows("stock-takes")), "Status"));
        Assert.Equal(new[] { 11.35802467m, 18.75m }, final.Rows("stock").OrderBy(row => row.GetProperty("ProductCd").GetString())
            .Select(row => AssetDecimal(row, "PhysicalQty")));
        CheckAdjustment(gain, "Inventory.AdjustGain", book.Accounts["INVENTORY"], book.Accounts["NON_OP_INCOME"], 3.70m);
        CheckAdjustment(loss, "Inventory.AdjustLoss", book.Accounts["PENDING_PROPERTY_LOSS"], book.Accounts["INVENTORY"], 6.25m);
        output.WriteLine(JsonSerializer.Serialize(W636State(final)));

        void CheckAdjustment(JsonElement move, string eventType, Guid debit, Guid credit, decimal amount)
        {
            Assert.Equal("STOCKTAKE", move.GetProperty("RelatedType").GetString());
            var txnNo = move.GetProperty("TxnNo").GetString();
            Assert.Single(final.Rows("stock-take-lines"), row => row.GetProperty("AdjustTxnNo").GetString() == txnNo);
            CheckW636Event(final, move, IntegrationEventStatus.Success, 1, eventType);
            CheckW636Voucher(final, move, debit, credit, amount);
        }

        async Task Send(HttpMethod method, string action, object? body = null)
        {
            using var response = await host.Send(OperationsRequest(actor, method, $"{path}/{action}", body));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W636_core_generic_stock_movement_keeps_scrap_and_unpriced_receipt_mappings()
    {
        await using var h = await Seed();
        var book = await SeedW636Book(h);
        await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        await using var host = await StartW636Api(h);
        var actor = await Login(host, h);
        var scrap = InventoryAdjustment();
        scrap.RelatedType = "SCRAP";
        scrap.Qty = -2.5m;
        using var scrapped = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/wms/stock/apply", scrap));
        Assert.True(scrapped.StatusCode == HttpStatusCode.OK, await OperationsFailure(scrapped));
        var noPrice = InventoryAdjustment();
        noPrice.TxnType = WmsTxnType.IN;
        noPrice.RelatedType = "INBOUND";
        noPrice.UnitPrice = null;
        noPrice.Qty = 3m;
        using var unpriced = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/wms/stock/apply", noPrice));
        Assert.True(unpriced.StatusCode == HttpStatusCode.OK, await OperationsFailure(unpriced));
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var final = await ReadW636(observer, h.Tenant);
        var scrapTxn = Assert.Single(final.Rows("stock-moves"), m => m.GetProperty("RelatedType").GetString() == "SCRAP");
        var unpricedTxn = Assert.Single(final.Rows("stock-moves"), m => m.GetProperty("RelatedType").GetString() == "INBOUND");
        CheckW636Event(final, scrapTxn, IntegrationEventStatus.Success, 1, "Inventory.Scrapped");
        CheckW636Event(final, unpricedTxn, IntegrationEventStatus.Skipped, 1, error: "单价缺失，无法计价过账");
        CheckW636Voucher(final, scrapTxn, book.Accounts["NON_OP_EXPENSE"], book.Accounts["INVENTORY"], 7.50m);
        Assert.Single(final.Rows("journals"));
        var physical = Assert.Single(final.Rows("stock"), row => row.GetProperty("ProductCd").GetString() == "P1");
        Assert.Equal(10.62345678m, AssetDecimal(physical, "PhysicalQty"));
        output.WriteLine(JsonSerializer.Serialize(W636State(final)));
    }

    private sealed record W636Book(Guid Period, Dictionary<string, Guid> Accounts);

    private static async Task<W636Book> SeedW636Book(Harness h, bool closed = false)
    {
        var db = h.Db;
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "simulated-inventory-book");
        await PostingRuleSeed.EnsureSeededAsync(db);
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(DateTime.Today, "simulated-inventory-book");
        if (closed)
        {
            period.Status = PeriodStatus.Closed;
            await db.SaveChangesAsync();
        }
        var accounts = await db.GlAccounts.AsNoTracking().Where(row => row.Role == "INVENTORY" || row.Role == "GRNI"
            || row.Role == "NON_OP_INCOME" || row.Role == "PENDING_PROPERTY_LOSS" || row.Role == "NON_OP_EXPENSE")
            .ToDictionaryAsync(row => row.Role!, row => row.Id);
        db.ChangeTracker.Clear();
        return new(period.Id, accounts);
    }

    private static async Task<InboundReceiptDto> PrepareW636Receipt(AuthenticationApiProcess host,
        Dictionary<string, string> actor, decimal price = W636Price)
    {
        var plan = Plan();
        plan.InboundType = 1;
        plan.SupplierCd = "W636-SIMULATED";
        plan.Details[0].UnitPrice = price;
        using var created = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/wms/inbound-order", plan));
        var data = await ReadW636WmsSuccess(created);
        var inbound = data.GetProperty("inboundNo").GetString()!;
        using var confirmed = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/wms/inbound-order/{inbound}/confirm"));
        Assert.True(confirmed.StatusCode == HttpStatusCode.OK, await OperationsFailure(confirmed));

        var receipt = ReceiptRequest();
        receipt.InboundNo = inbound;
        receipt.Details.RemoveAt(1);
        receipt.Details[0].RefOrderLineNo = 1;
        receipt.Details[0].UnitPrice = price;
        return receipt;
    }

    private static Task<HttpResponseMessage> ReceiveW636(AuthenticationApiProcess host, Dictionary<string, string> actor,
        InboundReceiptDto body) => host.Send(OperationsRequest(actor, HttpMethod.Post, W636ReceiptPath, body));

    private static async Task<JsonElement> ReadW636WmsSuccess(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("WM-MSG-071", document.RootElement.GetProperty("message").GetString());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static Task<int> SetW636Due(Harness h, Guid id, string? legacyPayload = null)
    {
        var query = h.Db.IntegrationEvents.Where(row => row.Id == id);
        var due = DateTime.UtcNow.AddMinutes(-1);
        return legacyPayload is null
            ? query.ExecuteUpdateAsync(set => set.SetProperty(row => row.NextRetryAt, due))
            : query.ExecuteUpdateAsync(set => set
                .SetProperty(row => row.PayloadJson, legacyPayload).SetProperty(row => row.NextRetryAt, due)
                .SetProperty(row => row.TargetNo, (string?)null).SetProperty(row => row.Status, IntegrationEventStatus.Failed)
                .SetProperty(row => row.LastError, "DISPATCH-404: unknown route WMS->FIN.OnStockMovedAsync"));
    }

    private static string W636LegacyPayload(StockTransaction row) => JsonSerializer.Serialize(new
    {
        txnNo = row.TxnNo, row.TxnType, relatedType = row.RelatedType, row.Qty, row.UnitPrice,
    });

    private static Task<AuthenticationApiProcess> StartW636Api(Harness h, string? connection = null, bool enabled = true)
    {
        var env = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["StockFinBridge__Enabled"] = enabled.ToString(),
            ["IntegrationEvent__Enabled"] = "false",
        };
        var port = Environment.GetEnvironmentVariable("CP6_STOCK_FINANCE_HTTP_PORT");
        if (port != null) env["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(connection ?? h.Db.Database.GetConnectionString()!, env);
    }

    private static int W636RelayPort(int port) => Environment.GetEnvironmentVariable("CP6_STOCK_FINANCE_HTTP_PORT") == "57117" ? port : 0;

    private static async Task<AssetRows> ReadW636(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'inbound', ARRAY(SELECT to_jsonb(r) FROM public."T_InboundOrder" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'inbound-lines', ARRAY(SELECT to_jsonb(r) FROM public."T_InboundOrderDetail" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'receipts', ARRAY(SELECT to_jsonb(r) FROM public."T_InboundReceipt" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'receipt-lines', ARRAY(SELECT to_jsonb(r) FROM public."T_InboundReceiptDetail" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'stock', ARRAY(SELECT to_jsonb(r) FROM public."T_Stock" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'stock-moves', ARRAY(SELECT to_jsonb(r) FROM public."T_StockTransaction" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'stock-takes', ARRAY(SELECT to_jsonb(r) FROM public."T_StockTake" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'stock-take-lines', ARRAY(SELECT to_jsonb(r) FROM public."T_StockTakeDetail" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'stock-sequence', ARRAY(SELECT to_jsonb(r) FROM public."T_WmsSequence" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'events', ARRAY(SELECT to_jsonb(r) FROM public."T_IntegrationEvent" r WHERE r."TenantId"=@t
                AND r."SourceModule"='WMS' AND r."TargetModule"='FIN' AND r."HookName"='OnStockMovedAsync' ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'journal-lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@t ORDER BY r."Id"),
              'fin-sequence', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@t ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("t", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static JsonElement W636State(AssetRows rows) => JsonSerializer.Deserialize<JsonElement>(rows.Data);

    private static JsonElement CheckW636Receipt(AssetRows state, decimal stockPrice)
    {
        var txn = Assert.Single(state.Rows("stock-moves"));
        var receipt = Assert.Single(state.Rows("receipts"));
        var line = Assert.Single(state.Rows("receipt-lines"));
        var stock = Assert.Single(state.Rows("stock"));
        Assert.Equal((WmsTxnType.IN, "INBOUND"), (txn.GetProperty("TxnType").GetString(), txn.GetProperty("RelatedType").GetString()));
        Assert.Equal(txn.GetProperty("TxnNo").GetString(), line.GetProperty("StockTxnNo").GetString());
        Assert.Equal(receipt.GetProperty("ReceiptNo").GetString(), txn.GetProperty("RelatedNo").GetString());
        Assert.Equal((2m, stockPrice, 2m), (AssetDecimal(txn, "Qty"), AssetDecimal(txn, "UnitPrice"), AssetDecimal(stock, "PhysicalQty")));
        Assert.Equal(InboundReceiptStatus.Confirmed, AssetInt(receipt, "Status"));
        return txn;
    }

    private static JsonElement CheckW636Event(AssetRows state, JsonElement movement, string status, int attempts,
        string? target = null, string? error = null)
    {
        var txnNo = movement.GetProperty("TxnNo").GetString();
        var evt = Assert.Single(state.Rows("events"), row => row.GetProperty("SourceNo").GetString() == txnNo);
        Assert.Equal(AssetId(movement, "TenantId"), AssetId(evt, "TenantId"));
        Assert.Equal((status, attempts, target, error), (evt.GetProperty("Status").GetString(), AssetInt(evt, "Attempts"),
            evt.GetProperty("TargetNo").GetString(), evt.GetProperty("LastError").GetString()));
        var retry = evt.GetProperty("NextRetryAt").ValueKind;
        Assert.Equal(status is IntegrationEventStatus.Pending or IntegrationEventStatus.Failed, retry != JsonValueKind.Null);
        return evt;
    }

    private static void CheckW636Voucher(AssetRows state, JsonElement movement, Guid debit, Guid credit, decimal amount)
    {
        var entry = Assert.Single(state.Rows("journals"), row => row.GetProperty("SourceDocNo").GetString() == movement.GetProperty("TxnNo").GetString());
        Assert.Equal(AssetId(movement, "TenantId"), AssetId(entry, "TenantId"));
        Assert.Equal(movement.GetProperty("TxnDateTime").GetDateTime(), entry.GetProperty("VoucherDate").GetDateTime());
        Assert.Equal((int)VoucherSource.Inventory, AssetInt(entry, "Source"));
        Assert.Equal((int)JournalStatus.Posted, AssetInt(entry, "Status"));
        Assert.True(entry.GetProperty("AutoPosted").GetBoolean());
        var legs = state.Rows("journal-lines").Where(line => AssetId(line, "EntryId") == AssetId(entry)).ToArray();
        Assert.Equal(2, legs.Length);
        Assert.Equal((amount, 0m), Amounts(debit));
        Assert.Equal((0m, amount), Amounts(credit));

        (decimal, decimal) Amounts(Guid account)
        {
            var line = Assert.Single(legs, l => AssetId(l, "AccountId") == account);
            return (AssetDecimal(line, "Debit"), AssetDecimal(line, "Credit"));
        }
    }

    private static void CheckW636Identity(AssetRows before, AssetRows after)
    {
        CheckW636Tables(before, after, "inbound", "inbound-lines", "receipts", "receipt-lines", "stock", "stock-moves", "stock-sequence");
        var original = Assert.Single(before.Rows("events"));
        var current = Assert.Single(after.Rows("events"));
        foreach (var field in new[] { "Id", "CorrelationId", "TenantId", "SourceNo", "PayloadJson", "Creator" })
            Assert.Equal(original.GetProperty(field).GetRawText(), current.GetProperty(field).GetRawText());
    }

    private static void CheckW636Tables(AssetRows before, AssetRows after, params string[] tables)
    {
        foreach (var table in tables)
            Assert.Equal(before.Rows(table).Select(row => row.GetRawText()), after.Rows(table).Select(row => row.GetRawText()));
    }
}
