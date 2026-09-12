using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class BusinessPartnerSqlTests(SqlDatabaseFixture database)
{
    private async Task<ErpScenario> ScenarioAsync()
    {
        var scenario = new ErpScenario(database);
        await scenario.InitializeAsync();
        return scenario;
    }

    [Fact]
    public async Task Real_preregistration_promotes_partner_with_atomic_inbox_journal_and_outbox()
    {
        var s = await ScenarioAsync();
        await s.RegisterPartnerAsync();
        var request = s.PartnerRequest();
        var envelope = s.Envelope(request);
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

    [Fact]
    public async Task Same_message_and_new_message_same_input_replay_without_rewriting_master()
    {
        var s = await ScenarioAsync();
        await s.RegisterPartnerAsync();
        var request = s.PartnerRequest();
        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope);
        await using var db = s.Db();
        var before = await db.BusinessPartners.AsNoTracking().SingleAsync();
        await s.ConsumeAsync(envelope);
        Assert.Single(await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized));
        await s.ConsumeAsync(s.Envelope(request));
        var after = await db.BusinessPartners.AsNoTracking().SingleAsync();
        Assert.Equal(before.RowVersion, after.RowVersion);
        await using var queue = s.Queue();
        Assert.Single(await queue.Requests.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        Assert.Equal(2, await queue.Inbox.CountAsync(x => x.TenantId == s.Tenant));
        var results = await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized);
        Assert.Equal(2, results.Length);
        Assert.Single(results.Select(x => x.GetProperty("data").GetRawText()).Distinct());
    }

    [Theory]
    [InlineData("missing", "C03_BUSINESS_PARTNER_NOT_FOUND")]
    [InlineData("frozen", "C03_BUSINESS_PARTNER_FROZEN")]
    [InlineData("cross-tenant", "C03_BUSINESS_PARTNER_NOT_FOUND")]
    [InlineData("incomplete", "C03_BUSINESS_PARTNER_MASTER_DATA_REQUIRED")]
    public async Task Invalid_master_has_stable_terminal_result_and_no_business_mutation(string fault, string code)
    {
        var s = await ScenarioAsync();
        if (fault != "missing") await s.RegisterPartnerAsync(frozen: fault == "frozen", tenant: fault == "cross-tenant" ? s.OtherTenant : null);
        if (fault == "incomplete")
        {
            await using var bad = s.Db();
            var partner = await bad.BusinessPartners.SingleAsync();
            partner.AccountsReceivableCd = null;
            await bad.SaveChangesAsync();
        }
        var request = s.PartnerRequest();
        await s.ConsumeAsync(s.Envelope(request));
        await s.AssertTerminalAsync(s.Account, code, order: false);
        await s.ConsumeAsync(s.Envelope(request));
        await s.AssertTerminalAsync(s.Account, code, order: false);
        await using var db = s.Db(fault == "cross-tenant" ? s.OtherTenant : null);
        Assert.All(await db.BusinessPartners.ToArrayAsync(), p => Assert.Equal(0, p.Status));
        Assert.Empty(await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized));
        await s.AssertNoOrderAsync();
    }

    [Fact]
    public async Task Sql_rowversion_prevents_stale_authority_and_account_binding_is_tenant_isolated()
    {
        var s = await ScenarioAsync();
        await s.RegisterPartnerAsync();
        await using var db = s.Db();
        var partner = await db.BusinessPartners.SingleAsync();
        var staleVersion = ErpScenario.Version(partner);
        var authority = new ErpCommerceAuthority(db, s.Clock);
        await authority.SetBusinessPartnerProfileAsync(s.PartnerKey, new(staleVersion, s.Account, "JPY", true), "erp-staff");
        Assert.NotEqual(staleVersion, partner.RowVersion);
        var stale = await Assert.ThrowsAsync<ErpCommerceException>(() => authority.SetBusinessPartnerProfileAsync(
            s.PartnerKey, new(staleVersion, s.Account, "JPY", false), "erp-staff"));
        Assert.Equal("C03_CONCURRENCY_CONFLICT", stale.Code);
        var immutable = await Assert.ThrowsAsync<ErpCommerceException>(() => authority.SetBusinessPartnerProfileAsync(
            s.PartnerKey, new(ErpScenario.Version(partner), Guid.NewGuid(), "JPY", false), "erp-staff"));
        Assert.Equal("C03_ACCOUNT_BINDING_IMMUTABLE", immutable.Code);
        await using var other = s.Db(s.OtherTenant);
        var missing = await Assert.ThrowsAsync<ErpCommerceException>(() => new ErpCommerceAuthority(other, s.Clock)
            .SetBusinessPartnerProfileAsync(s.PartnerKey, new(ErpScenario.Version(partner), s.Account, "JPY", false), "erp-staff"));
        Assert.Equal("C03_BUSINESS_PARTNER_NOT_FOUND", missing.Code);
        await s.RegisterPartnerAsync(tenant: s.OtherTenant);
        Assert.Equal(s.Account, (await other.BusinessPartners.SingleAsync()).CrmAccountId);
    }

    [Fact]
    public async Task Actual_sql_constraints_reject_duplicate_account_binding_and_request_identities()
    {
        var s = await ScenarioAsync();
        await s.RegisterPartnerAsync();
        var first = s.PartnerRequest();
        await s.ConsumeAsync(s.Envelope(first));
        var secondKey = "BP" + Guid.NewGuid().ToString("N")[..10];
        var secondAccount = Guid.NewGuid();
        await using var db = s.Db();
        await new BusinessPartnerService(db).CreateAsync(new()
        {
            BpCd = secondKey, BpName = "Second SQL customer", BaseCd = "B01", CustomerFlg = true,
            AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02"
        }, "erp-staff", preRegister: true);
        var binding = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.T_WebBusinessPartner SET CrmAccountId={s.Account} WHERE TenantId={s.Tenant} AND BpCd={secondKey}"));
        Assert.Contains(binding.Number, new[] { 2601, 2627 });
        var secondPartner = await db.BusinessPartners.SingleAsync(x => x.BpCd == secondKey);
        Assert.Null(secondPartner.CrmAccountId);
        await new ErpCommerceAuthority(db, s.Clock).SetBusinessPartnerProfileAsync(secondKey,
            new(ErpScenario.Version(secondPartner), secondAccount, "JPY", false), "erp-staff");
        var second = new ErpBusinessPartnerRequested(s.Tenant, Guid.NewGuid(), secondAccount, 1);
        await s.ConsumeAsync(s.Envelope(second));
        await using var queue = s.Queue();
        var requestId = await Assert.ThrowsAsync<SqlException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE erp_integration.Request SET RequestId={first.RequestId} WHERE TenantId={s.Tenant} AND Kind=1 AND AggregateId={secondAccount}"));
        Assert.Contains(requestId.Number, new[] { 2601, 2627 });
        var businessKey = await Assert.ThrowsAsync<SqlException>(() => queue.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE erp_integration.Request SET AggregateId={s.Account} WHERE TenantId={s.Tenant} AND Kind=1 AND AggregateId={secondAccount}"));
        Assert.Contains(businessKey.Number, new[] { 2601, 2627 });
        Assert.Equal(2, await queue.Requests.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(second.RequestId, (await queue.Requests.SingleAsync(x => x.TenantId == s.Tenant && x.AggregateId == secondAccount)).RequestId);
        await s.AssertNoOrderAsync();
    }
}
