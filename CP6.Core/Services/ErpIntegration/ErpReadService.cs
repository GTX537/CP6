using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

public sealed record ErpBusinessPartnerRead(Guid TenantId, Guid Id, string Key, Guid? AccountId, string DisplayName,
    bool Enabled, bool Frozen, string Currency, string Version);
public sealed record ErpQuotationRead(Guid TenantId, Guid Id, string Key, string BusinessPartnerKey, Guid? AccountId,
    string ApprovalStatus, decimal? Amount, string? Currency, bool CustomerAccepted,
    DateTimeOffset? AcceptedAtUtc, DateTimeOffset? ValidUntilUtc, string Version);
public sealed record ErpOrderRead(Guid TenantId, Guid Id, string Key, string BusinessPartnerKey,
    Guid? OpportunityId, Guid? AccountId, Guid? RequestId, int? RequestVersion, Guid? QuotationId,
    decimal BookedAmount, string Currency, string Status, string Version);

/// <summary>All reads explicitly scope SQL by the independently authenticated service tenant.</summary>
public sealed class ErpReadService(CP6Context db, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<ErpBusinessPartnerRead?> BusinessPartnerAsync(Guid tenant, string key, CancellationToken ct = default)
    {
        var row = await db.BusinessPartners.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.BpCd == key && !x.IsDeleted, ct);
        if (row is null || row.BpCd != key) return null;
        return new(tenant, row.Id, row.BpCd, row.CrmAccountId, row.BpName, row.Status == 1 && row.CustomerFlg,
            row.IsFrozen, string.IsNullOrEmpty(row.CurrencyCd) ? FxConstants.BaseCurrency : row.CurrencyCd, Version(row.RowVersion));
    }

    public async Task<ErpQuotationRead?> QuotationAsync(Guid tenant, string key, CancellationToken ct = default)
    {
        var row = await db.Quotations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.QtnNo == key && !x.IsDeleted, ct);
        if (row is null || row.QtnNo != key) return null;
        row.Details = await db.QuotationDetails.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.QtnNo == key && !x.IsDeleted).ToListAsync(ct);
        var account = await db.BusinessPartners.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.BpCd == row.CustomerCd && !x.IsDeleted)
            .Select(x => x.CrmAccountId).SingleOrDefaultAsync(ct);
        return new(tenant, row.Id, row.QtnNo, row.CustomerCd, account, row.EstimateCheckFlg == 9 ? "Approved" : "Pending",
            row.TotalAmount, row.CurrencyCd, ErpQuotationAcceptance.IsCurrent(row, clock.GetUtcNow()),
            row.CustomerAcceptedAtUtc, row.ValidUntilUtc, Version(row.RowVersion));
    }

    public async Task<ErpOrderRead?> OrderAsync(Guid tenant, string key, CancellationToken ct = default)
    {
        var row = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.WebOrderNo == key && !x.IsDeleted, ct);
        if (row is null || row.WebOrderNo != key) return null;
        var amount = await db.OrderDetails.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.WebOrderNo == key && !x.IsDeleted)
            .SumAsync(x => x.Amount ?? 0m, ct);
        return new(tenant, row.Id, row.WebOrderNo, row.CustomerCd, row.CrmOpportunityId, row.CrmAccountId,
            row.CrmRequestId, row.CrmRequestVersion, row.CrmQuotationId, amount, row.CurrencyCd, row.OrderStatus, Version(row.RowVersion));
    }

    private static string Version(byte[]? value) => value is { Length: 8 } ? Convert.ToBase64String(value) : "";
}
