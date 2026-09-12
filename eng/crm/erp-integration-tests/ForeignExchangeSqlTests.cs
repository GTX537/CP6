using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ForeignExchangeSqlTests(SqlDatabaseFixture database)
{
    [Theory]
    [InlineData(false, "C03_FX_RATE_REQUIRED")]
    [InlineData(true, "C03_FX_RATE_INVALID")]
    public async Task Missing_or_nonpositive_fx_is_terminal_and_repaired_master_requires_new_request_version(
        bool nonpositive, string expectedCode)
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync("USD");
        Guid? rateId = null;
        int sequenceBefore;
        await using (var db = s.Db())
        {
            sequenceBefore = await db.DocSequences.Where(x => x.FuncCode == "ORD")
                .Select(x => x.LastSeq).SingleOrDefaultAsync();
            Assert.Empty(await db.FxRates.ToArrayAsync());
            if (nonpositive)
            {
                var rate = await new FxRateService(db).CreateAsync(new()
                {
                    CurrencyCd = "USD", RateDate = s.Clock.GetUtcNow().UtcDateTime.Date, Rate = 150m
                }, "erp-staff");
                rateId = rate.Id;
                // Deliberately persist invalid ERP master data to exercise the consumer's guard.
                // The normal ERP CRUD service correctly rejects this value at its own boundary.
                rate.Rate = 0m;
                await db.SaveChangesAsync();
            }
        }

        var envelope = s.Envelope(request);
        var disposition = await s.Handler().ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        var original = await s.JournalAsync(request.OpportunityId);
        using (var result = JsonDocument.Parse(original.ResultDataJson))
            Assert.Equal(expectedCode, result.RootElement.GetProperty("errorCode").GetString());
        await s.AssertTerminalAsync(request.OpportunityId, expectedCode, true);
        Assert.Equal(Cp6InboxDisposition.Applied, disposition.Disposition);
        await s.AssertNoOrderAsync();
        await using (var db = s.Db())
            Assert.Equal(sequenceBefore, await db.DocSequences.Where(x => x.FuncCode == "ORD")
                .Select(x => x.LastSeq).SingleOrDefaultAsync());
        await using (var queue = s.Queue())
            Assert.Equal(ErpInboxStatus.Processed,
                (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);

        // Repair through the actual ERP master service after the terminal result is committed.
        await using (var db = s.Db())
        {
            var rates = new FxRateService(db);
            var repaired = new CP6.Entity.DomainModels.Erp.FxRate
            {
                CurrencyCd = "USD", RateDate = s.Clock.GetUtcNow().UtcDateTime.Date, Rate = 150m
            };
            if (rateId is { } id) await rates.UpdateAsync(id, repaired, "erp-staff");
            else await rates.CreateAsync(repaired, "erp-staff");
        }

        await s.ConsumeAsync(envelope);
        await s.ConsumeAsync(s.Envelope(request));
        await s.AssertTerminalAsync(request.OpportunityId, expectedCode, true);
        var repeated = await s.JournalAsync(request.OpportunityId);
        Assert.Equal(original.InputSha256, repeated.InputSha256);
        Assert.Equal(original.ResultVersion, repeated.ResultVersion);
        Assert.Equal(original.ResultDataJson, repeated.ResultDataJson);
        await s.AssertNoOrderAsync();

        var corrected = request with { RequestId = Guid.NewGuid(), RequestVersion = 2 };
        var correctedEnvelope = s.Envelope(corrected);
        await s.ConsumeAsync(correctedEnvelope);
        await s.ConsumeAsync(correctedEnvelope);
        var success = await s.JournalAsync(request.OpportunityId, 2);
        Assert.True(success.Terminal && success.Succeeded, success.ResultDataJson);
        await s.AssertTerminalAsync(request.OpportunityId, expectedCode, true);
        await using (var db = s.Db())
        {
            var order = await db.Orders.SingleAsync();
            Assert.Equal(corrected.RequestId, order.CrmRequestId);
            Assert.Equal(2, order.CrmRequestVersion);
            Assert.Equal("USD", order.CurrencyCd);
            Assert.Equal(150m, order.FxRate);
            Assert.Equal(300m, (await db.OrderDetails.SingleAsync()).Amount);
            Assert.Equal(sequenceBefore + 1, await db.DocSequences.Where(x => x.FuncCode == "ORD")
                .Select(x => x.LastSeq).SingleAsync());
            var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
            Assert.Equal(order.WebOrderNo, result.GetProperty("data").GetProperty("orderKey").GetString());
            Assert.Equal(correctedEnvelope.MessageId, result.GetProperty("causationid").GetString());
        }
        await using (var queue = s.Queue())
            Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        Assert.Equal(0, s.ExternalCalls.Count);
    }
}
