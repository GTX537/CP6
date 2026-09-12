using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Erp;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OrderAmountBoundarySqlTests(SqlDatabaseFixture database)
{
    private const decimal FirstOverflowingAmount = 10_000_000_000_000m;
    private const decimal LargestRepresentableQuotationAmount = 9_999_999_999_999.99m;

    [Fact]
    public async Task Customer_acceptance_rejects_a_quote_amount_that_cannot_fit_an_order_line()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        await s.RegisterPartnerAsync();
        var key = await CreateQuotationAsync(s, 100_000_000m, 100_000m, FirstOverflowingAmount);
        var sequenceBefore = await OrderSequenceAsync(s);

        await using (var db = s.Db())
        {
            var quote = await db.Quotations.Include(x => x.Details).SingleAsync(x => x.QtnNo == key);
            var error = await Assert.ThrowsAsync<ErpCommerceException>(() =>
                new ErpCommerceAuthority(db, s.Clock).AcceptQuotationAsync(key,
                    new(ErpScenario.Version(quote), "customer-overflow-boundary"), "erp-staff"));
            Assert.Equal("C03_QUOTATION_AMOUNT_INVALID", error.Code);
        }

        await using (var db = s.Db())
        {
            var quote = await db.Quotations.SingleAsync(x => x.QtnNo == key);
            Assert.Equal(FirstOverflowingAmount, quote.TotalAmount);
            Assert.Null(quote.CustomerAcceptedAtUtc);
            Assert.Null(quote.AcceptedContentSha256);
        }
        Assert.Equal(sequenceBefore, await OrderSequenceAsync(s));
        Assert.Empty(await s.ResultsAsync());
        await s.AssertNoOrderAsync();
    }

    [Fact]
    public async Task Largest_representable_quote_amount_creates_an_exact_order_and_result()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        await s.RegisterPartnerAsync();
        await s.ConsumeAsync(s.Envelope(s.PartnerRequest()));
        // Both operands fit the actual source decimal columns and multiply exactly to 10^13 - 0.01.
        var key = await CreateQuotationAsync(s, 99_999m, 100_001_000.01m, LargestRepresentableQuotationAmount);
        await using (var db = s.Db())
        {
            var quote = await db.Quotations.SingleAsync(x => x.QtnNo == key);
            await new ErpCommerceAuthority(db, s.Clock).AcceptQuotationAsync(key,
                new(ErpScenario.Version(quote), "customer-maximum-boundary"), "erp-staff");
            db.ProductMasters.Add(s.Product(key));
            await db.SaveChangesAsync();
        }
        var request = await RequestAsync(s, key, LargestRepresentableQuotationAmount);
        var sequenceBefore = await OrderSequenceAsync(s);
        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope);

        var journal = await s.JournalAsync(request.OpportunityId);
        Assert.True(journal.Terminal && journal.Succeeded, journal.ResultDataJson);
        await using (var db = s.Db())
        {
            var order = await db.Orders.SingleAsync();
            var detail = await db.OrderDetails.SingleAsync();
            Assert.Equal(request.RequestId, order.CrmRequestId);
            Assert.Equal(99_999m, detail.Quantity);
            Assert.Equal(100_001_000.01m, detail.IndividualUnitPrice);
            Assert.Equal(LargestRepresentableQuotationAmount, detail.Amount);
            var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
            Assert.Equal(order.WebOrderNo, result.GetProperty("data").GetProperty("orderKey").GetString());
            Assert.Equal(LargestRepresentableQuotationAmount, result.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
            Assert.Equal(envelope.MessageId, result.GetProperty("causationid").GetString());
        }
        Assert.Equal(sequenceBefore + 1, await OrderSequenceAsync(s));
        await using (var queue = s.Queue())
            Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Fact]
    public async Task Legacy_accepted_oversized_quote_is_terminal_before_order_sql_and_replays_unchanged()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        await s.RegisterPartnerAsync();
        await s.ConsumeAsync(s.Envelope(s.PartnerRequest()));
        var key = await CreateQuotationAsync(s, 100_000_000m, 100_000m, FirstOverflowingAmount);
        await using (var db = s.Db())
        {
            var quote = await db.Quotations.Include(x => x.Details).SingleAsync(x => x.QtnNo == key);
            // Persist the valid hash/evidence an earlier application could accept before the destination bound existed.
            // Quote values themselves came through the real ERP creation and terms operations above.
            quote.CustomerAcceptedAtUtc = s.Clock.GetUtcNow();
            quote.CustomerAcceptedBy = "erp-staff";
            quote.CustomerAcceptanceReference = "legacy-customer-overflow-boundary";
            quote.AcceptedContentSha256 = ErpQuotationAcceptance.ContentHash(quote);
            db.ProductMasters.Add(s.Product(key));
            await db.SaveChangesAsync();
        }
        var request = await RequestAsync(s, key, FirstOverflowingAmount);
        var sequenceBefore = await OrderSequenceAsync(s);
        var envelope = s.Envelope(request);
        using var sqlFailures = new SqlFailureProbe();
        var result = await s.Handler().ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.True(result.Disposition == Cp6InboxDisposition.Applied,
            $"Expected terminal business outcome, got {result.Disposition}/{result.ErrorCode}; SQL numbers: {string.Join(',', sqlFailures.Numbers)}");
        await s.AssertTerminalAsync(request.OpportunityId, "C03_QUOTATION_AMOUNT_INVALID", true);
        var original = await s.JournalAsync(request.OpportunityId);
        await s.ConsumeAsync(envelope);
        await s.ConsumeAsync(s.Envelope(request));
        var repeated = await s.JournalAsync(request.OpportunityId);
        Assert.Equal(original.ResultDataJson, repeated.ResultDataJson);
        Assert.Equal(original.ResultVersion, repeated.ResultVersion);
        Assert.Equal(original.RowVersion, repeated.RowVersion);
        Assert.Empty(sqlFailures.Numbers);
        Assert.Equal(sequenceBefore, await OrderSequenceAsync(s));
        await s.AssertNoOrderAsync();
        await using var queue = s.Queue();
        Assert.Equal(ErpInboxStatus.Processed,
            (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);
    }

    private static async Task<string> CreateQuotationAsync(ErpScenario s, decimal quantity, decimal price, decimal amount)
    {
        string key;
        await using (var db = s.Db())
            key = await new QuotationService(db).CreateAsync(new()
            {
                BaseCd = "B01", StaffCd = "S01", CustomerCd = s.PartnerKey, CustomerName = "ERP amount boundary customer",
                EstimateCheckFlg = 9,
                Details = [new() { DetailNo = 1, ItemName1 = "Amount boundary packaging", Quantity = quantity, UnitPrice = price, Unit = "PCS" }]
            }, "erp-staff");
        // Read fresh SQL values: this proves the source precision accepted both operands and their exact product.
        await using (var db = s.Db())
        {
            var quote = await db.Quotations.Include(x => x.Details).SingleAsync(x => x.QtnNo == key);
            var line = Assert.Single(quote.Details);
            Assert.Equal(quantity, line.Quantity);
            Assert.Equal(price, line.UnitPrice);
            Assert.Equal(amount, line.Amount);
            Assert.Equal(amount, quote.TotalAmount);
            await new ErpCommerceAuthority(db, s.Clock).SetQuotationTermsAsync(key,
                new(ErpScenario.Version(quote), "JPY", s.Clock.GetUtcNow().AddDays(7), "10",
                    s.Clock.GetUtcNow().AddDays(3).UtcDateTime), "erp-staff");
        }
        return key;
    }

    private static async Task<ErpOrderRequested> RequestAsync(ErpScenario s, string key, decimal amount)
    {
        await using var db = s.Db();
        var quote = await db.Quotations.SingleAsync(x => x.QtnNo == key);
        return new(s.Tenant, Guid.NewGuid(), s.Account, 1, Guid.NewGuid(), s.PartnerKey, key,
            Convert.ToBase64String(ErpScenario.Version(quote)), amount, "JPY");
    }

    private static async Task<int> OrderSequenceAsync(ErpScenario s)
    {
        await using var db = s.Db();
        return await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync();
    }
}
