using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

internal sealed record ErpOrderOrigin(Guid TenantId, Guid OpportunityId, Guid AccountId, Guid RequestId,
    int RequestVersion, Guid QuotationId, string InputSha256, string Currency);

/// <summary>Creates actual ERP order rows from a locked, accepted ERP quotation and approved master snapshots.</summary>
internal sealed class ErpQuotationOrderFactory(CP6Context db, OrderService orders, TimeProvider clock)
{
    public async Task<(string OrderKey, decimal Amount)> CreateAsync(ErpOrderRequested request, string inputHash, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null || !db.Database.IsSqlServer() || db.CurrentTenantId != request.TenantId)
            throw new InvalidOperationException("C03_ORDER_REQUIRES_TENANT_SQL_TRANSACTION");
        var bp = await db.BusinessPartners.FromSqlInterpolated($"SELECT * FROM dbo.T_WebBusinessPartner WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND BpCd={request.BusinessPartnerKey}")
            .SingleOrDefaultAsync(ct);
        RequireBusinessPartner(bp, request.AccountId, allowPreRegistered: false);
        var quotation = await db.Quotations.FromSqlInterpolated($"SELECT * FROM dbo.T_Quotation WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND QtnNo={request.QuotationKey}")
            .SingleOrDefaultAsync(ct) ?? throw new ErpCommerceException("C03_QUOTATION_NOT_FOUND");
        quotation.Details = await db.QuotationDetails.FromSqlInterpolated($"SELECT * FROM dbo.T_QuotationDetail WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND QtnNo={request.QuotationKey}")
            .Where(d => !d.IsDeleted).OrderBy(d => d.DetailNo).ToListAsync(ct);
        if (quotation.CustomerCd != bp!.BpCd) throw new ErpCommerceException("C03_QUOTATION_CUSTOMER_MISMATCH");
        if (quotation.RowVersion is null || Convert.ToBase64String(quotation.RowVersion) != request.QuotationVersion)
            throw new ErpCommerceException("C03_QUOTATION_VERSION_CHANGED");
        ErpQuotationAcceptance.Validate(quotation, clock.GetUtcNow());
        if (!ErpQuotationAcceptance.IsCurrent(quotation, clock.GetUtcNow()))
            throw new ErpCommerceException("C03_CUSTOMER_ACCEPTANCE_REQUIRED");
        if (quotation.TotalAmount != request.ExpectedAmount) throw new ErpCommerceException("C03_AMOUNT_MISMATCH");
        var bpCurrency = string.IsNullOrEmpty(bp.CurrencyCd) ? FxConstants.BaseCurrency : bp.CurrencyCd;
        if (quotation.CurrencyCd != request.Currency || bpCurrency != request.Currency)
            throw new ErpCommerceException("C03_CURRENCY_MISMATCH");
        if (await db.Orders.AnyAsync(x => x.CrmOpportunityId == request.OpportunityId, ct))
            throw new ErpCommerceException("C03_ORDER_ALREADY_EXISTS");

        var orderDate = clock.GetUtcNow().UtcDateTime.Date;
        // Validate required pricing master data before allocating an order number. These
        // reads share the serializable command transaction with OrderService's rate snapshot.
        var rate = await new FxRateService(db).ResolveRateAsync(request.Currency, orderDate, ct);
        if (rate is null) throw new ErpCommerceException("C03_FX_RATE_REQUIRED");
        if (rate <= 0) throw new ErpCommerceException("C03_FX_RATE_INVALID");

        var dto = new OrderDto
        {
            CustomerCd = bp.BpCd, OrderType = quotation.OrderType!, OrderDepartment = quotation.BaseCd,
            OrderDate = orderDate, CustomerDeliveryDate = quotation.OrderDeliveryDate,
            Quantity = quotation.Details.Sum(d => d.Quantity), SalesPriceDiv = "1"
        };
        foreach (var line in quotation.Details)
        {
            var branch = line.DetailNo.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            // All active matches are counted before checking status: an ambiguous line never picks the first product.
            var candidates = await db.ProductMasters.Where(p => p.QuotationNo == quotation.QtnNo &&
                    p.Branch1 == branch && !p.IsDeleted).ToListAsync(ct);
            if (candidates.Count != 1) throw new ErpCommerceException("C03_QUOTATION_PRODUCT_MAPPING_INVALID");
            var product = candidates[0];
            if (product.TenantId != request.TenantId || product.CustomerCd != bp.BpCd ||
                product.Status is not (1 or 9) || !product.WfApprovalFlg ||
                (line.QtnCalcNo is not null && product.EstimateCalcNo != line.QtnCalcNo))
                throw new ErpCommerceException("C03_QUOTATION_PRODUCT_NOT_APPROVED");
            if (product.SalesPriceDiv is not ("1" or "2"))
                throw new ErpCommerceException("C03_PRODUCT_PRICE_MODE_UNSUPPORTED");
            // Quotation and master units are free text; no authoritative conversion or
            // alias map exists. Preserve accepted quantity/price only with exact units.
            if (string.IsNullOrWhiteSpace(line.Unit) ||
                !string.Equals(line.Unit, product.QtyUnit, StringComparison.Ordinal) ||
                !string.Equals(line.Unit, product.UnitPriceUnit, StringComparison.Ordinal))
                throw new ErpCommerceException("C03_QUOTATION_UNIT_MISMATCH");
            var detail = await orders.LookupProductMasterForDetailAsync(product.ProductCd)
                ?? throw new ErpCommerceException("C03_QUOTATION_PRODUCT_MAPPING_INVALID");
            detail.Quantity = line.Quantity;
            // Product master: 1=set, 2=individual. Order: 1=individual, 2=set.
            detail.SalesPriceDiv = product.SalesPriceDiv == "1" ? "2" : "1";
            detail.IndividualUnitPrice = detail.SalesPriceDiv == "1" ? line.UnitPrice : null;
            detail.SetUnitPrice = detail.SalesPriceDiv == "2" ? line.UnitPrice : null;
            detail.Amount = line.Amount;
            detail.CustomerDeliveryDate = quotation.OrderDeliveryDate;
            detail.DeliveryCd = bp.BpCd;
            detail.OrderType = quotation.OrderType;
            detail.Processes = await orders.LookupProductProcessesAsync(product.ProductCd);
            detail.Materials = await orders.LookupProductMaterialsAsync(product.ProductCd);
            if (detail.Processes.Any(p => string.IsNullOrWhiteSpace(p.OperationCd) || string.IsNullOrWhiteSpace(p.ProcessCd)) ||
                detail.Materials.Any(m => string.IsNullOrWhiteSpace(m.MaterialCd)))
                throw new ErpCommerceException("C03_PRODUCT_ROUTING_INVALID");
            dto.Details.Add(detail);
        }
        var origin = new ErpOrderOrigin(request.TenantId, request.OpportunityId, request.AccountId, request.RequestId,
            request.RequestVersion, quotation.Id, inputHash, quotation.CurrencyCd!);
        var key = await orders.CreateForIntegrationAsync(dto, origin);
        return (key, quotation.TotalAmount!.Value);
    }

    public static void RequireBusinessPartner(BusinessPartner? bp, Guid accountId, bool allowPreRegistered)
    {
        if (bp is null || bp.IsDeleted || bp.Status == 9) throw new ErpCommerceException("C03_BUSINESS_PARTNER_NOT_FOUND");
        if (bp.IsFrozen) throw new ErpCommerceException("C03_BUSINESS_PARTNER_FROZEN");
        if (bp.CrmAccountId != accountId) throw new ErpCommerceException("C03_BUSINESS_PARTNER_ACCOUNT_MISMATCH");
        if (!bp.CustomerFlg || string.IsNullOrWhiteSpace(bp.BpName) || string.IsNullOrWhiteSpace(bp.BaseCd) ||
            string.IsNullOrWhiteSpace(bp.AccountsReceivableCd) || string.IsNullOrWhiteSpace(bp.SalesStaffCd) ||
            string.IsNullOrWhiteSpace(bp.BusinessStaffCd) || (bp.Status != 1 && !(allowPreRegistered && bp.Status == 0)))
            throw new ErpCommerceException("C03_BUSINESS_PARTNER_MASTER_DATA_REQUIRED");
    }
}
