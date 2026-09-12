using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

internal sealed class ErpScenario(SqlDatabaseFixture database)
{
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid OtherTenant { get; } = Guid.NewGuid();
    public Guid Account { get; } = Guid.NewGuid();
    public string PartnerKey { get; } = "BP" + Guid.NewGuid().ToString("N")[..10];
    public TestClock Clock { get; } = new();
    public ForbiddenExternalCalls ExternalCalls { get; } = new();
    public ErpIntegrationRuntime Runtime { get; private set; } = null!;

    public async Task InitializeAsync(int maxInboxAttempts = 10)
    {
        await using var db = Db();
        foreach (var tenant in new[] { Tenant, OtherTenant })
            db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = "c03-" + tenant.ToString("N"), TenantName = "C03 SQL acceptance", Enable = true });
        await db.SaveChangesAsync();
        Runtime = new(new()
        {
            Enabled = true, Tenants = new() { [Tenant] = "local", [OtherTenant] = "local" },
            ReaderClientIds = ["c03-sql-reader"], DaprAppToken = new string('t', 48), DaprApiToken = new string('a', 48),
            InitialRetrySeconds = 1, MaximumRetrySeconds = 8, MaxInboxAttempts = maxInboxAttempts
        }, new(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp"))), Clock);
    }

    public CP6Context Db(Guid? tenant = null) => database.CreateBusinessContext(tenant ?? Tenant);
    public ErpIntegrationContext Queue() => database.CreateDbContext();
    public ErpRequestHandler Handler(Func<string, CancellationToken, Task>? stage = null) =>
        new(database, Runtime, db => new(db, ExternalCalls, ExternalCalls, mesBridge: ExternalCalls, fxRate: new FxRateService(db)), Clock, stage);

    public async Task RegisterPartnerAsync(bool frozen = false, Guid? tenant = null, string currency = "JPY")
    {
        await using var db = Db(tenant);
        await new BusinessPartnerService(db).CreateAsync(new()
        {
            BpCd = PartnerKey, BpName = "ERP SQL customer", BaseCd = "B01", CustomerFlg = true,
            AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02"
        }, "erp-staff", preRegister: true);
        var partner = await db.BusinessPartners.SingleAsync(x => x.BpCd == PartnerKey);
        Assert.Equal(0, partner.Status);
        Assert.Equal(8, Version(partner).Length);
        await new ErpCommerceAuthority(db, Clock).SetBusinessPartnerProfileAsync(PartnerKey,
            new(Version(partner), Account, currency, frozen), "erp-staff");
    }

    public ErpBusinessPartnerRequested PartnerRequest(int version = 1, Guid? tenant = null) =>
        new(tenant ?? Tenant, Guid.NewGuid(), Account, version);

    public Cp6OutboxEnvelope Envelope(ErpBusinessPartnerRequested request, int? aggregateVersion = null) =>
        ErpEventContracts.Create(request.TenantId, request.AccountId.ToString("D"), aggregateVersion ?? request.RequestVersion,
            ErpEventContracts.BusinessPartnerRequested, request, "local", Guid.NewGuid().ToString("N"), "c03-sql-test", Clock);

    public Cp6OutboxEnvelope Envelope(ErpOrderRequested request, int? aggregateVersion = null) =>
        ErpEventContracts.Create(request.TenantId, request.OpportunityId.ToString("D"), aggregateVersion ?? request.RequestVersion,
            ErpEventContracts.OrderRequested, request, "local", Guid.NewGuid().ToString("N"), "c03-sql-test", Clock);

    public async Task ConsumeAsync(Cp6OutboxEnvelope envelope, ErpRequestHandler? handler = null)
    {
        var result = await (handler ?? Handler()).ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.Contains(result.Disposition, new[] { Cp6InboxDisposition.Applied, Cp6InboxDisposition.Duplicate });
    }

    public async Task<ErpOrderRequested> ReadyOrderAsync(string currency = "JPY", string unit = "PCS")
    {
        await RegisterPartnerAsync(currency: currency);
        await ConsumeAsync(Envelope(PartnerRequest()));
        return await NewAcceptedQuotationAsync(currency, unit);
    }

    public async Task<ErpOrderRequested> NewAcceptedQuotationAsync(string currency = "JPY", string unit = "PCS")
    {
        await using var db = Db();
        var key = await new QuotationService(db).CreateAsync(new()
        {
            BaseCd = "B01", StaffCd = "S01", CustomerCd = PartnerKey, CustomerName = "ERP SQL customer",
            EstimateCheckFlg = 9,
            Details = [new() { DetailNo = 1, ItemName1 = "Accepted packaging", Quantity = 2m, UnitPrice = 150m, Unit = unit }]
        }, "erp-staff");
        var quote = await db.Quotations.Include(q => q.Details).SingleAsync(q => q.QtnNo == key);
        var authority = new ErpCommerceAuthority(db, Clock);
        await authority.SetQuotationTermsAsync(key,
            new(Version(quote), currency, Clock.GetUtcNow().AddDays(7), "10", Clock.GetUtcNow().AddDays(3).UtcDateTime), "erp-staff");
        await db.Entry(quote).ReloadAsync();
        await authority.AcceptQuotationAsync(key, new(Version(quote), "customer-confirmation-" + Guid.NewGuid().ToString("N")), "erp-staff");
        await db.Entry(quote).ReloadAsync();
        Assert.True(ErpQuotationAcceptance.IsCurrent(quote, Clock.GetUtcNow()));
        db.ProductMasters.Add(Product(key));
        await db.SaveChangesAsync();
        return new(Tenant, Guid.NewGuid(), Account, 1, Guid.NewGuid(), PartnerKey, key,
            Convert.ToBase64String(Version(quote)), 300m, currency);
    }

    public ProductMaster Product(string quotation) => new()
    {
        ProductCd = "P" + Guid.NewGuid().ToString("N")[..15], ItemCd = "SQLITEM", SetProductCd = "SQLSET",
        CustomerCd = PartnerKey, QuotationNo = quotation, Branch1 = "0001", SalesPriceDiv = "2",
        Status = 1, WfApprovalFlg = true, CpItemName1 = "Approved packaging", QtyUnit = "PCS", UnitPriceUnit = "PCS"
    };

    public async Task<ErpIntegrationRequest> JournalAsync(Guid aggregate, int version = 1)
    {
        await using var queue = Queue();
        return await queue.Requests.AsNoTracking().SingleAsync(x => x.TenantId == Tenant && x.AggregateId == aggregate && x.RequestVersion == version);
    }

    public static byte[] Version(CP6.Entity.BaseBizEntity entity)
    {
        Assert.NotNull(entity.RowVersion);
        Assert.Equal(8, entity.RowVersion.Length);
        return entity.RowVersion.ToArray();
    }

    public async Task<JsonElement[]> ResultsAsync(string? type = null, Guid? tenant = null)
    {
        await using var queue = Queue();
        var tenantId = tenant ?? Tenant;
        var rows = await queue.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenantId).ToArrayAsync();
        return rows.Select(row =>
        {
            using var document = JsonDocument.Parse(row.Payload);
            return document.RootElement.Clone();
        }).Where(root => type is null || root.GetProperty("type").GetString() == type).ToArray();
    }

    public async Task AssertNoOrderAsync()
    {
        foreach (var tenant in new[] { Tenant, OtherTenant })
        {
            await using var db = Db(tenant);
            Assert.Empty(await db.Orders.ToArrayAsync());
            Assert.Empty(await db.OrderDetails.ToArrayAsync());
            Assert.Empty(await db.OrderProcesses.ToArrayAsync());
            Assert.Empty(await db.OrderMaterials.ToArrayAsync());
            Assert.Empty(await ResultsAsync(ErpEventContracts.OrderCreated, tenant));
        }
        await using var queue = Queue();
        Assert.Empty(await queue.OrderBridges.Where(x => x.TenantId == Tenant || x.TenantId == OtherTenant).ToArrayAsync());
        Assert.Equal(0, ExternalCalls.Count);
    }

    public async Task AssertTerminalAsync(Guid aggregate, string code, bool order, int version = 1)
    {
        var journal = await JournalAsync(aggregate, version);
        Assert.True(journal.Terminal);
        Assert.False(journal.Succeeded);
        using var document = JsonDocument.Parse(journal.ResultDataJson);
        Assert.Equal(code, document.RootElement.GetProperty("errorCode").GetString());
        Assert.False(document.RootElement.GetProperty("retryable").GetBoolean());
        Assert.Contains(await ResultsAsync(order ? ErpEventContracts.OrderFailed : ErpEventContracts.BusinessPartnerFailed),
            x => x.GetProperty("data").GetProperty("errorCode").GetString() == code);
    }
}

internal sealed class TestClock : TimeProvider
{
    private long ticks = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero).Ticks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
}

/// <summary>These boundaries must never execute during the integration command transaction.</summary>
internal sealed class ForbiddenExternalCalls : IPowerEggWorkflowService, IWmsBridgeHook, IMesBridgeHook
{
    private int count;
    public int Count => Volatile.Read(ref count);
    private Exception Forbidden() { Interlocked.Increment(ref count); return new InvalidOperationException("External service called during C03 command"); }
    public Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? actor, CancellationToken ct = default) => throw Forbidden();
    public Task<WmsBridgeResult> OnOrderCreatedAsync(string order, string? actor) => throw Forbidden();
    Task<MesBridgeResult> IMesBridgeHook.OnOrderCreatedAsync(string order, string? actor) => throw Forbidden();
    public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string order, string? actor) => throw Forbidden();
    public Task<WmsBridgeResult> OnProductionCompletedAsync(string order, decimal quantity, string? actor) => throw Forbidden();
}
