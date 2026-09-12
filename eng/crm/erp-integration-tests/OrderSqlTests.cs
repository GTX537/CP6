using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Erp;
using CP6.Platform.EntityFramework;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OrderSqlTests(SqlDatabaseFixture database)
{
    private async Task<ErpScenario> ScenarioAsync()
    {
        var scenario = new ErpScenario(database);
        await scenario.InitializeAsync();
        return scenario;
    }

    [Fact]
    public async Task Accepted_real_quotation_creates_order_lines_origin_outbox_and_postcommit_bridge()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope);
        var journal = await s.JournalAsync(request.OpportunityId);
        Assert.True(journal.Succeeded, journal.ResultDataJson);
        await using var db = s.Db();
        var order = await db.Orders.SingleAsync();
        var line = await db.OrderDetails.SingleAsync();
        var quotation = await db.Quotations.SingleAsync();
        Assert.Equal(request.OpportunityId, order.CrmOpportunityId);
        Assert.Equal(request.AccountId, order.CrmAccountId);
        Assert.Equal(request.RequestId, order.CrmRequestId);
        Assert.Equal(request.RequestVersion, order.CrmRequestVersion);
        Assert.Equal(quotation.Id, order.CrmQuotationId);
        Assert.Equal(64, order.CrmRequestSha256!.Length);
        Assert.Equal(order.WebOrderNo, line.WebOrderNo);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal("PCS", line.QtyUnit);
        Assert.Equal("PCS", line.UnitPriceUnit);
        Assert.Equal(150m, line.IndividualUnitPrice);
        Assert.Equal(300m, line.Amount);
        Assert.Equal("JPY", order.CurrencyCd);
        Assert.Equal(1m, order.FxRate);
        Assert.True(journal.Succeeded && journal.Terminal);
        Assert.Equal(order.CrmRequestSha256, journal.InputSha256);
        var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        Assert.Equal(order.WebOrderNo, result.GetProperty("data").GetProperty("orderKey").GetString());
        Assert.Equal(300m, result.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
        Assert.Equal(envelope.MessageId, result.GetProperty("causationid").GetString());
        await using var queue = s.Queue();
        Assert.Equal(ErpInboxStatus.Processed, (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);
        var bridge = await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Equal(order.WebOrderNo, bridge.OrderKey);
        Assert.Null(bridge.CompletedAtUtc);
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Fact]
    public async Task Concurrent_duplicate_commands_create_exactly_one_order()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        var repeated = s.Envelope(request);
        var deliveries = new[] { repeated, repeated }.Concat(Enumerable.Range(0, 4).Select(_ => s.Envelope(request))).ToArray();
        await Task.WhenAll(deliveries.Select(x => s.ConsumeAsync(x)));
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
        Assert.Single(await db.OrderDetails.ToArrayAsync());
        await using var queue = s.Queue();
        Assert.Single(await queue.Requests.Where(x => x.TenantId == s.Tenant && x.Kind == ErpRequestKind.Order).ToArrayAsync());
        Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        Assert.Single((await s.ResultsAsync(ErpEventContracts.OrderCreated)).Select(x => x.GetProperty("data").GetProperty("orderKey").GetString()).Distinct());
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Fact]
    public async Task Actual_foreign_currency_rate_and_accepted_price_are_frozen_on_order()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync("USD");
        await using (var db = s.Db())
            await new FxRateService(db).CreateAsync(new()
            {
                CurrencyCd = "USD", RateDate = s.Clock.GetUtcNow().UtcDateTime.Date, Rate = 145.875m
            }, "erp-staff");
        await s.ConsumeAsync(s.Envelope(request));
        await using var verify = s.Db();
        var order = await verify.Orders.SingleAsync();
        Assert.Equal("USD", order.CurrencyCd);
        Assert.Equal(145.875m, order.FxRate);
        Assert.Equal(300m, (await verify.OrderDetails.SingleAsync()).Amount);
        var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        Assert.Equal("USD", result.GetProperty("data").GetProperty("currency").GetString());
        Assert.Equal(300m, result.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
    }

    [Fact]
    public async Task Concurrent_different_versions_for_one_opportunity_create_one_order()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        var next = request with { RequestId = Guid.NewGuid(), RequestVersion = 2 };
        await Task.WhenAll(s.ConsumeAsync(s.Envelope(request)), s.ConsumeAsync(s.Envelope(next)));
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
        Assert.Single(await db.OrderDetails.ToArrayAsync());
        await using var queue = s.Queue();
        Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        var journals = await queue.Requests.Where(x => x.TenantId == s.Tenant && x.Kind == ErpRequestKind.Order).ToArrayAsync();
        Assert.Equal(2, journals.Length);
        Assert.Single(journals, x => x.Succeeded);
        Assert.All(journals, x => Assert.True(x.Terminal));
    }

    [Fact]
    public async Task Different_request_versions_for_one_opportunity_never_create_a_second_order()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        var next = request with { RequestId = Guid.NewGuid(), RequestVersion = 2 };
        await s.ConsumeAsync(s.Envelope(next));
        await s.AssertTerminalAsync(request.OpportunityId, "C03_ORDER_ALREADY_EXISTS", true, 2);
        await s.ConsumeAsync(s.Envelope(request));
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
        Assert.Single(await db.OrderDetails.ToArrayAsync());
    }

    [Fact]
    public async Task Older_request_version_is_terminal_superseded_after_newer_request()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request with { RequestVersion = 2 }));
        await s.ConsumeAsync(s.Envelope(request with { RequestId = Guid.NewGuid() }));
        await s.AssertTerminalAsync(request.OpportunityId, "C03_REQUEST_SUPERSEDED", true);
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
    }

    [Theory]
    [InlineData("currency", "C03_CURRENCY_MISMATCH")]
    [InlineData("amount", "C03_AMOUNT_MISMATCH")]
    [InlineData("account", "C03_BUSINESS_PARTNER_ACCOUNT_MISMATCH")]
    [InlineData("customer", "C03_QUOTATION_CUSTOMER_MISMATCH")]
    [InlineData("version", "C03_QUOTATION_VERSION_CHANGED")]
    [InlineData("expired", "C03_QUOTATION_EXPIRED")]
    [InlineData("revoked", "C03_CUSTOMER_ACCEPTANCE_REQUIRED")]
    [InlineData("missing-product", "C03_QUOTATION_PRODUCT_MAPPING_INVALID")]
    [InlineData("ambiguous-product", "C03_QUOTATION_PRODUCT_MAPPING_INVALID")]
    [InlineData("unapproved-product", "C03_QUOTATION_PRODUCT_NOT_APPROVED")]
    [InlineData("frozen", "C03_BUSINESS_PARTNER_FROZEN")]
    [InlineData("cross-tenant", "C03_BUSINESS_PARTNER_NOT_FOUND")]
    public async Task Invalid_commerce_inputs_are_stable_terminal_without_order_side_effects(string fault, string code)
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        int sequenceBefore;
        await using (var db = s.Db())
        {
            sequenceBefore = await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync();
            var quote = await db.Quotations.SingleAsync();
            if (fault == "currency") request = request with { Currency = "USD" };
            if (fault == "amount") request = request with { ExpectedAmount = 301m };
            if (fault == "account") request = request with { AccountId = Guid.NewGuid() };
            if (fault == "version") request = request with { QuotationVersion = Convert.ToBase64String(new byte[8]) };
            if (fault == "expired") s.Clock.Advance(TimeSpan.FromDays(8));
            if (fault == "customer") quote.CustomerCd = "OTHER-BP";
            if (fault == "revoked")
                await new ErpCommerceAuthority(db, s.Clock).RevokeQuotationAcceptanceAsync(quote.QtnNo, new(ErpScenario.Version(quote)), "erp-staff");
            if (fault == "frozen")
            {
                var partner = await db.BusinessPartners.SingleAsync();
                await new ErpCommerceAuthority(db, s.Clock).SetBusinessPartnerProfileAsync(s.PartnerKey,
                    new(ErpScenario.Version(partner), s.Account, "JPY", true), "erp-staff");
            }
            if (fault == "missing-product") db.ProductMasters.Remove(await db.ProductMasters.SingleAsync());
            if (fault == "ambiguous-product") db.ProductMasters.Add(s.Product(quote.QtnNo));
            if (fault == "unapproved-product") (await db.ProductMasters.SingleAsync()).WfApprovalFlg = false;
            await db.SaveChangesAsync();
            if (fault is "customer" or "revoked") request = request with { QuotationVersion = Convert.ToBase64String(ErpScenario.Version(quote)) };
        }
        if (fault == "cross-tenant")
        {
            var cross = request with { TenantId = s.OtherTenant };
            await s.ConsumeAsync(s.Envelope(cross));
            await using var queue = s.Queue();
            var journal = await queue.Requests.SingleAsync(x => x.TenantId == s.OtherTenant);
            using var json = JsonDocument.Parse(journal.ResultDataJson);
            Assert.True(journal.Terminal && !journal.Succeeded);
            Assert.Equal(code, json.RootElement.GetProperty("errorCode").GetString());
            var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderFailed, s.OtherTenant));
            Assert.Equal(code, result.GetProperty("data").GetProperty("errorCode").GetString());
        }
        else
        {
            await s.ConsumeAsync(s.Envelope(request));
            await s.AssertTerminalAsync(request.OpportunityId, code, true);
            await s.ConsumeAsync(s.Envelope(request));
            await s.AssertTerminalAsync(request.OpportunityId, code, true);
        }
        await s.AssertNoOrderAsync();
        await using var verify = s.Db();
        Assert.Equal(sequenceBefore, await verify.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync());
    }

    [Fact]
    public async Task Failure_after_order_save_rolls_back_order_lines_sequence_bridge_then_same_request_retries_once()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        int before;
        await using (var db = s.Db()) before = await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync();
        var observed = 0;
        var fail = s.Handler((stage, _) =>
        {
            Assert.Equal("order-staged", stage);
            Interlocked.Increment(ref observed);
            throw new TimeoutException("C03 SQL injected post-save failure");
        });
        var envelope = s.Envelope(request);
        var result = await fail.ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.Equal(1, observed);
        Assert.Equal(Cp6InboxDisposition.RetryScheduled, result.Disposition);
        await s.AssertNoOrderAsync();
        await using (var db = s.Db()) Assert.Equal(before, await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync());
        var failed = await s.JournalAsync(request.OpportunityId);
        Assert.False(failed.Terminal);
        Assert.False(failed.Succeeded);
        using (var json = JsonDocument.Parse(failed.ResultDataJson))
        {
            Assert.Equal("C03_ERP_UNAVAILABLE", json.RootElement.GetProperty("errorCode").GetString());
            Assert.True(json.RootElement.GetProperty("retryable").GetBoolean());
        }
        await using (var queue = s.Queue())
        {
            var receipt = await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId);
            Assert.Equal(ErpInboxStatus.Processing, receipt.Status);
            Assert.True(receipt.RetryAtUtc > s.Clock.GetUtcNow());
        }
        s.Clock.Advance(TimeSpan.FromSeconds(2));
        await s.ConsumeAsync(envelope);
        await s.ConsumeAsync(envelope);
        await using (var db = s.Db())
        {
            Assert.Single(await db.Orders.ToArrayAsync());
            Assert.Single(await db.OrderDetails.ToArrayAsync());
            Assert.Equal(before + 1, await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleAsync());
        }
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        await using (var queue = s.Queue()) Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
    }

    [Fact]
    public async Task Same_business_key_with_conflicting_payload_emits_terminal_conflict_without_extra_order()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        var original = await s.JournalAsync(request.OpportunityId);
        await s.ConsumeAsync(s.Envelope(request with { ExpectedAmount = 301m }));
        var journal = await s.JournalAsync(request.OpportunityId);
        Assert.Equal(original.InputSha256, journal.InputSha256);
        Assert.True(journal.Succeeded);
        var failure = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderFailed));
        Assert.Equal("C03_IDEMPOTENCY_CONFLICT", failure.GetProperty("data").GetProperty("errorCode").GetString());
        Assert.False(failure.GetProperty("data").GetProperty("retryable").GetBoolean());
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
        Assert.Single(await db.OrderDetails.ToArrayAsync());
    }

    [Fact]
    public async Task Equal_expected_amount_with_different_decimal_scale_replays_the_prior_business_outcome()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        var original = await s.JournalAsync(request.OpportunityId);
        Assert.True(original.Succeeded, original.ResultDataJson);
        // JSON 300 and 300.00 carry the same accepted financial value and business identity.
        await s.ConsumeAsync(s.Envelope(request with { ExpectedAmount = 300.00m }));
        var failures = await s.ResultsAsync(ErpEventContracts.OrderFailed);
        Assert.True(failures.Length == 0,
            "Equivalent decimal scale produced: " + string.Join(",", failures.Select(x => x.GetProperty("data").GetProperty("errorCode").GetString())));
        var journal = await s.JournalAsync(request.OpportunityId);
        Assert.Equal(original.InputSha256, journal.InputSha256);
        Assert.Equal(original.RowVersion, journal.RowVersion);
        var successes = await s.ResultsAsync(ErpEventContracts.OrderCreated);
        Assert.Equal(2, successes.Length);
        Assert.Single(successes.Select(x => x.GetProperty("data").GetProperty("orderKey").GetString()).Distinct());
        await using var db = s.Db();
        Assert.Single(await db.Orders.ToArrayAsync());
        Assert.Single(await db.OrderDetails.ToArrayAsync());
    }

    [Fact]
    public async Task Unrelated_opportunities_and_ordinary_orders_share_a_unique_sql_sequence()
    {
        var s = await ScenarioAsync();
        var first = await s.ReadyOrderAsync();
        var second = await s.NewAcceptedQuotationAsync();
        var third = await s.NewAcceptedQuotationAsync();
        var product = "";
        await using (var db = s.Db()) product = await db.ProductMasters.Where(x => x.QuotationNo == first.QuotationKey).Select(x => x.ProductCd).SingleAsync();
        using var sqlFailures = new SqlFailureProbe();
        var envelopes = new[] { s.Envelope(first), s.Envelope(second), s.Envelope(third) };
        Task<Cp6InboxProcessingResult> Deliver(Cp6OutboxEnvelope envelope) =>
            s.Handler().ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        var crmTasks = envelopes.Select(Deliver).ToArray();
        // Ordinary creation is deliberately never retried: it does not have a CRM idempotency key.
        var ordinaryTasks = Enumerable.Range(0, 3).Select(_ => OrdinaryOrderAsync(s, product)).ToArray();
        await Task.WhenAll(crmTasks.Cast<Task>().Concat(ordinaryTasks));
        var dispositions = await Task.WhenAll(crmTasks);
        if (dispositions.Any(x => x.Disposition == Cp6InboxDisposition.RetryScheduled))
            Assert.Contains(1205, sqlFailures.Numbers); // Real SQL deadlock was the observed concurrency failure.
        for (var attempt = 0; attempt < 5 && dispositions.Any(x => x.Disposition == Cp6InboxDisposition.RetryScheduled); attempt++)
        {
            s.Clock.Advance(TimeSpan.FromSeconds(9)); // Greater than this fixture's bounded eight-second backoff.
            for (var index = 0; index < envelopes.Length; index++)
                if (dispositions[index].Disposition == Cp6InboxDisposition.RetryScheduled)
                    dispositions[index] = await Deliver(envelopes[index]); // Exact original MessageId and bytes.
        }
        Assert.All(dispositions, result => Assert.Contains(result.Disposition,
            new[] { Cp6InboxDisposition.Applied, Cp6InboxDisposition.Duplicate }));
        Assert.All(sqlFailures.Numbers, number => Assert.Contains(number, new[] { 1205, 3903 }));
        await using var verify = s.Db();
        var orders = await verify.Orders.ToArrayAsync();
        Assert.Equal(6, orders.Length);
        Assert.Equal(6, orders.Select(x => x.WebOrderNo).Distinct().Count());
        Assert.Equal(3, orders.Count(x => x.CrmOpportunityId is not null));
        Assert.Equal(6, await verify.OrderDetails.CountAsync());
        Assert.Equal(1800m, await verify.OrderDetails.SumAsync(x => x.Amount));
        Assert.All(orders, x => Assert.StartsWith("ORD", x.WebOrderNo));
        Assert.Equal(3, (await Task.WhenAll(ordinaryTasks)).Distinct().Count());
        var created = await s.ResultsAsync(ErpEventContracts.OrderCreated);
        Assert.Equal(3, created.Length);
        Assert.Equal(orders.Where(x => x.CrmOpportunityId is not null).Select(x => x.WebOrderNo).Order().ToArray(),
            created.Select(x => x.GetProperty("data").GetProperty("orderKey").GetString()!).Order().ToArray());
        foreach (var request in new[] { first, second, third })
        {
            var journal = await s.JournalAsync(request.OpportunityId);
            Assert.True(journal.Succeeded && journal.Terminal, journal.ResultDataJson);
            Assert.Equal(request.RequestId, journal.RequestId);
        }
        await using var queue = s.Queue();
        Assert.Equal(3, await queue.OrderBridges.CountAsync(x => x.TenantId == s.Tenant));
        foreach (var envelope in envelopes)
            Assert.Equal(ErpInboxStatus.Processed, (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Fact]
    public async Task Actual_sql_unique_index_rejects_second_order_for_same_tenant_opportunity()
    {
        var s = await ScenarioAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        await using var db = s.Db();
        var product = await db.ProductMasters.Select(x => x.ProductCd).SingleAsync();
        var ordinary = await OrdinaryOrderAsync(s, product);
        // Mutate a real ordinary order to collide; SQL, rather than a preflight service check, must reject it.
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.T_Order SET CrmOpportunityId={request.OpportunityId} WHERE TenantId={s.Tenant} AND WebOrderNo={ordinary}"));
        Assert.Contains(error.Number, new[] { 2601, 2627 });
        Assert.Equal(1, await db.Orders.CountAsync(x => x.CrmOpportunityId == request.OpportunityId));
        Assert.Null((await db.Orders.SingleAsync(x => x.WebOrderNo == ordinary)).CrmOpportunityId);
    }

    private static async Task<string> OrdinaryOrderAsync(ErpScenario s, string product)
    {
        await using var db = s.Db();
        var orders = new OrderService(db, s.ExternalCalls, new NoOpWmsBridgeHook(), fxRate: new FxRateService(db));
        var detail = await orders.LookupProductMasterForDetailAsync(product);
        Assert.NotNull(detail);
        detail.Quantity = 2m;
        detail.SalesPriceDiv = "1";
        detail.IndividualUnitPrice = 150m;
        detail.Amount = 300m;
        return await orders.CreateAsync(new OrderDto
        {
            CustomerCd = s.PartnerKey, OrderType = "10", OrderDate = s.Clock.GetUtcNow().UtcDateTime.Date,
            Quantity = 2m, SalesPriceDiv = "1", Details = [detail]
        }, "erp-staff");
    }
}
