using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;

namespace CP6.Tests.ErpIntegration;

public class ErpCommerceAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0, 1];

    [Fact]
    public async Task Internal_approval_does_not_supply_customer_acceptance_or_terms()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var quotation = Quote();
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => service.AcceptQuotationAsync(
            quotation.QtnNo, new(Version, "customer-confirmation-1"), "erp-staff"));
        Assert.Equal("C03_QUOTATION_TERMS_REQUIRED", error.Code);
        Assert.Null(quotation.CustomerAcceptedAtUtc);
    }

    [Fact]
    public async Task Explicit_acceptance_binds_real_terms_and_lines_and_detects_later_change()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var quotation = Quote();
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        await service.SetQuotationTermsAsync(quotation.QtnNo,
            new(Version, "JPY", Now.AddDays(7), "10", Now.AddDays(3).UtcDateTime), "erp-staff");
        Assert.Null(quotation.CustomerAcceptedAtUtc);
        await service.AcceptQuotationAsync(quotation.QtnNo, new(Version, "customer-confirmation-1"), "erp-staff");
        Assert.Equal(Now, quotation.CustomerAcceptedAtUtc);
        Assert.True(ErpQuotationAcceptance.IsCurrent(quotation, Now));
        quotation.Details[0].UnitPrice = 151m;
        Assert.False(ErpQuotationAcceptance.IsCurrent(quotation, Now));
    }

    [Fact]
    public async Task Expiry_and_withdrawal_invalidate_accepted_quotation()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var quotation = Quote();
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        await service.SetQuotationTermsAsync(quotation.QtnNo,
            new(Version, "JPY", Now.AddDays(7), "10", Now.AddDays(3).UtcDateTime), "erp-staff");
        await service.AcceptQuotationAsync(quotation.QtnNo, new(Version, "customer-confirmation-1"), "erp-staff");
        Assert.False(ErpQuotationAcceptance.IsCurrent(quotation, Now.AddDays(7)));
        await service.RevokeQuotationAcceptanceAsync(quotation.QtnNo, new(Version), "erp-staff");
        Assert.False(ErpQuotationAcceptance.IsCurrent(quotation, Now));
        Assert.Null(quotation.AcceptedContentSha256);
    }

    [Fact]
    public async Task Financial_inconsistency_cannot_be_accepted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var quotation = Quote();
        quotation.TotalAmount = 1m;
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        await service.SetQuotationTermsAsync(quotation.QtnNo,
            new(Version, "JPY", Now.AddDays(7), "10", Now.AddDays(3).UtcDateTime), "erp-staff");
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => service.AcceptQuotationAsync(
            quotation.QtnNo, new(Version, "customer-confirmation-1"), "erp-staff"));
        Assert.Equal("C03_QUOTATION_AMOUNT_INVALID", error.Code);
        Assert.Null(quotation.CustomerAcceptedAtUtc);
    }

    [Fact]
    public async Task Business_partner_binding_is_explicit_immutable_and_survives_ordinary_edits()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var partnerService = new BusinessPartnerService(db);
        var dto = new BusinessPartnerDto
        {
            BpCd = "BP001", BpName = "ERP registered customer", BaseCd = "B01", CustomerFlg = true,
            AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02"
        };
        await partnerService.CreateAsync(dto, "erp-staff", preRegister: true);
        var partner = db.BusinessPartners.Single();
        partner.RowVersion = Version;
        await db.SaveChangesAsync();
        var account = Guid.NewGuid();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        await service.SetBusinessPartnerProfileAsync("BP001", new(Version, account, "USD", true), "erp-staff");
        // Existing clients omit the new authority fields; ordinary master edits retain them.
        dto.BpName = "Corrected legal customer";
        await partnerService.UpdateAsync("BP001", dto, "erp-staff");
        Assert.Equal(account, partner.CrmAccountId);
        Assert.Equal("USD", partner.CurrencyCd);
        Assert.True(partner.IsFrozen);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => service.SetBusinessPartnerProfileAsync(
            "BP001", new(Version, Guid.NewGuid(), "USD", false), "erp-staff"));
        Assert.Equal("C03_ACCOUNT_BINDING_IMMUTABLE", error.Code);
    }

    [Fact]
    public async Task Missing_or_stale_row_version_cannot_change_authority()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var quotation = Quote();
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();
        var service = new ErpCommerceAuthority(db, new FixedClock());
        var missing = await Assert.ThrowsAsync<ErpCommerceException>(() => service.SetQuotationTermsAsync(
            quotation.QtnNo, new([], "JPY", Now.AddDays(7), "10", null), "erp-staff"));
        Assert.Equal("C03_PRECONDITION_REQUIRED", missing.Code);
        var stale = await Assert.ThrowsAsync<ErpCommerceException>(() => service.SetQuotationTermsAsync(
            quotation.QtnNo, new([0, 0, 0, 0, 0, 0, 0, 9], "JPY", Now.AddDays(7), "10", null), "erp-staff"));
        Assert.Equal("C03_CONCURRENCY_CONFLICT", stale.Code);
    }

    [Fact]
    public void Acceptance_hash_survives_sql_decimal_scale_and_datetime_kind_roundtrip()
    {
        var quote = Quote();
        quote.OrderDeliveryDate = Now.AddDays(3).UtcDateTime.Date;
        quote.ValidUntilUtc = Now.AddDays(7);
        var before = ErpQuotationAcceptance.ContentHash(quote);
        quote.TotalAmount = 300.00m;
        quote.Details[0].Quantity = 2.00m;
        quote.Details[0].UnitPrice = 150.0000m;
        quote.Details[0].Amount = 300.00m;
        quote.OrderDeliveryDate = DateTime.SpecifyKind(quote.OrderDeliveryDate.Value, DateTimeKind.Unspecified);
        Assert.Equal(before, ErpQuotationAcceptance.ContentHash(quote));
        quote.OrderDeliveryDate = quote.OrderDeliveryDate.Value.AddDays(1);
        Assert.NotEqual(before, ErpQuotationAcceptance.ContentHash(quote));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Customer_acceptance_respects_the_ERP_order_detail_limit(int count, bool accepted)
    {
        var quote = Quote();
        quote.CurrencyCd = "JPY";
        quote.ValidUntilUtc = Now.AddDays(7);
        quote.OrderType = "10";
        quote.Details = Enumerable.Range(1, count).Select(n => new QuotationDetail
        {
            TenantId = quote.TenantId, QtnNo = quote.QtnNo, DetailNo = n,
            Quantity = 1m, UnitPrice = 1m, Amount = 1m
        }).ToList();
        quote.TotalAmount = count;
        if (accepted) ErpQuotationAcceptance.Validate(quote, Now);
        else Assert.Equal("C03_QUOTATION_LINES_INVALID",
            Assert.Throws<ErpCommerceException>(() => ErpQuotationAcceptance.Validate(quote, Now)).Code);
    }

    private static Quotation Quote() => new()
    {
        QtnNo = "QTN2026090001-01", BaseCd = "B01", StaffCd = "S01", CustomerCd = "BP001",
        EstimateCheckFlg = 9, MasterConfirmFlg = 0, TotalAmount = 300m, RowVersion = Version,
        Details = [new() { QtnNo = "QTN2026090001-01", DetailNo = 1, Quantity = 2m, UnitPrice = 150m, Amount = 300m, Unit = "piece" }]
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
