using CP6.Core.EFDbContext;
using CP6.Entity;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>ERP staff operations; the CRM read/service-token API cannot invoke these writes.</summary>
public sealed class ErpCommerceAuthority(CP6Context db, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task SetBusinessPartnerProfileAsync(string key, ErpBusinessPartnerProfile input, string actor,
        CancellationToken ct = default)
    {
        RequireActor(actor);
        if (!ErpQuotationAcceptance.IsCurrency(input.Currency) || input.CrmAccountId == Guid.Empty)
            throw new ErpCommerceException("C03_COMMERCE_TERMS_INVALID");
        var partner = await db.BusinessPartners.SingleOrDefaultAsync(x => x.BpCd == key && !x.IsDeleted, ct)
            ?? throw new ErpCommerceException("C03_BUSINESS_PARTNER_NOT_FOUND");
        RequireVersion(partner, input.RowVersion);
        if (partner.CrmAccountId is not null && partner.CrmAccountId != input.CrmAccountId)
            throw new ErpCommerceException("C03_ACCOUNT_BINDING_IMMUTABLE");
        if (input.CrmAccountId is not null && await db.BusinessPartners.AnyAsync(x =>
                x.Id != partner.Id && x.CrmAccountId == input.CrmAccountId, ct))
            throw new ErpCommerceException("C03_ACCOUNT_ALREADY_BOUND");
        partner.CrmAccountId = input.CrmAccountId;
        partner.CurrencyCd = input.Currency;
        partner.IsFrozen = input.IsFrozen;
        Touch(partner, actor);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql &&
            sql.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627 &&
                e.Message.Contains("IX_T_WebBusinessPartner_TenantId_CrmAccountId", StringComparison.Ordinal)))
        {
            // Two different partners can pass the friendly precheck concurrently.
            // Only this named uniqueness violation is the account-binding business conflict.
            throw new ErpCommerceException("C03_ACCOUNT_ALREADY_BOUND");
        }
    }

    public async Task SetQuotationTermsAsync(string key, ErpQuotationTerms input, string actor, CancellationToken ct = default)
    {
        RequireActor(actor);
        if (!ErpQuotationAcceptance.IsCurrency(input.Currency) || input.ValidUntilUtc.Offset != TimeSpan.Zero ||
            input.ValidUntilUtc <= clock.GetUtcNow() || input.OrderType is not { Length: > 0 and <= 4 } ||
            input.OrderType.Any(c => c is < '0' or > '9'))
            throw new ErpCommerceException("C03_COMMERCE_TERMS_INVALID");
        var quotation = await FindQuotationAsync(key, ct);
        RequireVersion(quotation, input.RowVersion);
        quotation.CurrencyCd = input.Currency;
        quotation.ValidUntilUtc = input.ValidUntilUtc;
        quotation.OrderType = input.OrderType;
        quotation.OrderDeliveryDate = input.OrderDeliveryDate?.Date;
        ErpQuotationAcceptance.Clear(quotation);
        Touch(quotation, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptQuotationAsync(string key, ErpQuotationAccept input, string actor, CancellationToken ct = default)
    {
        RequireActor(actor);
        if (string.IsNullOrWhiteSpace(input.AcceptanceReference) || input.AcceptanceReference.Length > 100 ||
            input.AcceptanceReference.Any(char.IsControl))
            throw new ErpCommerceException("C03_ACCEPTANCE_EVIDENCE_REQUIRED");
        var quotation = await FindQuotationAsync(key, ct);
        RequireVersion(quotation, input.RowVersion);
        var now = clock.GetUtcNow();
        ErpQuotationAcceptance.Validate(quotation, now);
        quotation.CustomerAcceptedAtUtc = now;
        quotation.CustomerAcceptanceReference = input.AcceptanceReference;
        quotation.CustomerAcceptedBy = actor;
        quotation.AcceptedContentSha256 = ErpQuotationAcceptance.ContentHash(quotation);
        Touch(quotation, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeQuotationAcceptanceAsync(string key, ErpQuotationWithdraw input, string actor,
        CancellationToken ct = default)
    {
        RequireActor(actor);
        var quotation = await FindQuotationAsync(key, ct);
        RequireVersion(quotation, input.RowVersion);
        ErpQuotationAcceptance.Clear(quotation);
        Touch(quotation, actor);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Quotation> FindQuotationAsync(string key, CancellationToken ct)
        => await db.Quotations.Include(x => x.Details.Where(d => !d.IsDeleted))
               .SingleOrDefaultAsync(x => x.QtnNo == key && !x.IsDeleted, ct)
           ?? throw new ErpCommerceException("C03_QUOTATION_NOT_FOUND");

    private void RequireVersion(BaseBizEntity entity, byte[]? version)
    {
        if (version is not { Length: 8 }) throw new ErpCommerceException("C03_PRECONDITION_REQUIRED");
        if (entity.RowVersion is null || !entity.RowVersion.AsSpan().SequenceEqual(version))
            throw new ErpCommerceException("C03_CONCURRENCY_CONFLICT");
        db.Entry(entity).Property(x => x.RowVersion).OriginalValue = version;
    }

    private static void RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100 || actor.Any(char.IsControl))
            throw new ErpCommerceException("C03_ACTOR_REQUIRED");
    }

    private void Touch(BaseBizEntity entity, string actor)
    {
        entity.Modifier = actor;
        entity.ModifyDate = clock.GetUtcNow().UtcDateTime;
    }
}
