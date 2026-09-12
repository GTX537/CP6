using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class DeliveryLeadTimeSqlTests(SqlDatabaseFixture database)
{
    [Fact]
    public async Task Accepted_quotation_with_insufficient_delivery_lead_time_is_terminal_without_order_side_effects()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        int sequenceBefore;
        await using (var db = s.Db())
        {
            var quotation = await db.Quotations.Include(x => x.Details).SingleAsync();
            var authority = new ErpCommerceAuthority(db, s.Clock);
            var yesterday = s.Clock.GetUtcNow().AddDays(-1).UtcDateTime.Date;
            await authority.SetQuotationTermsAsync(quotation.QtnNo,
                new(ErpScenario.Version(quotation), "JPY", s.Clock.GetUtcNow().AddDays(7), "10", yesterday),
                "erp-staff");
            await db.Entry(quotation).ReloadAsync();
            Assert.Null(quotation.CustomerAcceptedAtUtc);
            await authority.AcceptQuotationAsync(quotation.QtnNo,
                new(ErpScenario.Version(quotation), "customer-confirmed-past-delivery"), "erp-staff");
            await db.Entry(quotation).ReloadAsync();
            Assert.True(ErpQuotationAcceptance.IsCurrent(quotation, s.Clock.GetUtcNow()));
            Assert.Equal(yesterday, quotation.OrderDeliveryDate);
            request = request with { QuotationVersion = Convert.ToBase64String(ErpScenario.Version(quotation)) };
            var product = await db.ProductMasters.SingleAsync();
            var ordinaryService = new OrderService(db, s.ExternalCalls, s.ExternalCalls, fxRate: new FxRateService(db));
            Assert.False((await ordinaryService.CheckIsEditableAsync("10", null, null)).IsEditable);
            var leadTime = await ordinaryService.CheckDeliveryLeadTimeAsync(product.ProductCd,
                s.Clock.GetUtcNow().UtcDateTime.Date, yesterday);
            Assert.False(leadTime.Ok);
            Assert.Equal(0, leadTime.LtSumDays);
            sequenceBefore = await db.DocSequences.Where(x => x.FuncCode == "ORD")
                .Select(x => x.LastSeq).SingleOrDefaultAsync();
        }

        var envelope = s.Envelope(request);
        var disposition = await s.Handler().ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        var original = await s.JournalAsync(request.OpportunityId);
        using (var result = JsonDocument.Parse(original.ResultDataJson))
            Assert.Equal("C03_ORDER_DELIVERY_INVALID", result.RootElement.GetProperty("errorCode").GetString());
        await s.AssertTerminalAsync(request.OpportunityId, "C03_ORDER_DELIVERY_INVALID", true);
        Assert.Equal(Cp6InboxDisposition.Applied, disposition.Disposition);
        await s.ConsumeAsync(envelope);
        await s.ConsumeAsync(s.Envelope(request));
        Assert.Equal(original.ResultDataJson, (await s.JournalAsync(request.OpportunityId)).ResultDataJson);
        await s.AssertNoOrderAsync();
        await using (var db = s.Db())
            Assert.Equal(sequenceBefore, await db.DocSequences.Where(x => x.FuncCode == "ORD")
                .Select(x => x.LastSeq).SingleOrDefaultAsync());
        await using (var queue = s.Queue())
            Assert.Equal(ErpInboxStatus.Processed,
                (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);
    }
}
