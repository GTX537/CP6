using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.ErpIntegration.SqlTests;

[Collection(ErpRelationalCollection.Name)]
public sealed class ErpHandlerRelationalTests(ErpRelationalFixture database, ITestOutputHelper output)
{
    private async Task<ErpScenario> ScenarioAsync()
    {
        output.WriteLine(database.SetupSummary);
        var scenario = new ErpScenario(database);
        await scenario.InitializeAsync();
        return scenario;
    }

    [Fact]
    public async Task Missing_partner_has_stable_terminal_result_without_business_mutation()
    {
        var s = await ScenarioAsync();
        var request = s.PartnerRequest();
        output.WriteLine("Stage=handler; prerequisite=actual tenant save and valid contract envelope, no preregistration.");
        await s.ConsumeAsync(s.Envelope(request));
        await s.AssertTerminalAsync(s.Account, "C03_BUSINESS_PARTNER_NOT_FOUND", order: false);
        await s.ConsumeAsync(s.Envelope(request));
        await s.AssertTerminalAsync(s.Account, "C03_BUSINESS_PARTNER_NOT_FOUND", order: false);
        await using var db = s.Db();
        Assert.All(await db.BusinessPartners.ToArrayAsync(), partner => Assert.Equal(0, partner.Status));
        Assert.Empty(await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized));
        await s.AssertNoOrderAsync();
    }

    [Fact]
    public async Task Real_preregistration_promotes_partner_with_atomic_inbox_journal_and_outbox()
    {
        var s = await ScenarioAsync();
        output.WriteLine("Stage=real preregistration and ERP authority; failures here remain business prerequisite failures.");
        await s.RegisterPartnerAsync();
        var request = s.PartnerRequest();
        var envelope = s.Envelope(request);
        output.WriteLine("Stage=handler; prerequisite=real preregistration completed.");
        await s.ConsumeAsync(envelope);
        await using var db = s.Db();
        var partner = await db.BusinessPartners.SingleAsync();
        Assert.Equal(1, partner.Status);
        Assert.Equal(s.Account, partner.CrmAccountId);
        var journal = await s.JournalAsync(s.Account);
        Assert.True(journal.Terminal && journal.Succeeded);
        Assert.Equal(request.RequestId, journal.RequestId);
        var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized));
        Assert.Equal(s.PartnerKey, result.GetProperty("data").GetProperty("businessPartnerKey").GetString());
        Assert.Equal(envelope.MessageId, result.GetProperty("causationid").GetString());
        await using var queue = s.Queue();
        var receipt = await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId);
        Assert.Equal(ErpInboxStatus.Processed, receipt.Status);
        Assert.Equal(1, receipt.AttemptCount);
        await s.AssertNoOrderAsync();
    }
}
