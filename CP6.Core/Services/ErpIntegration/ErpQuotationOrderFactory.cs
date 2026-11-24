using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Erp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.ErpIntegration;

internal sealed record ErpOrderOrigin(Guid TenantId, Guid OpportunityId, Guid AccountId, Guid RequestId,
    int RequestVersion, Guid QuotationId, string QuotationVersion, string InputSha256, string Currency);

/// <summary>Creates actual ERP order rows from a locked, accepted ERP quotation and approved master snapshots.</summary>
internal sealed class ErpQuotationOrderFactory(CP6Context db, OrderService orders, TimeProvider clock,
    IDataProtectionProvider? partnerProtection = null, IErpOrderPartnerUseSource? useSource = null)
{
    public async Task<(string OrderKey, decimal Amount)> CreateAsync(ErpOrderRequested request, string inputHash, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null || !(db.Database.IsSqlServer() || db.Database.IsNpgsql()) ||
            db.CurrentTenantId != request.TenantId)
            throw new InvalidOperationException("C03_ORDER_REQUIRES_TENANT_SQL_TRANSACTION");
        BpUseReceipt? use = null;
        string? quoteBase = null;
        BusinessPartner? bp;
        if (await BpUseScopeGate.IsCutoverAsync(db, ct))
        {
            quoteBase = (await db.Quotations.AsNoTracking().Where(row => row.QtnNo == request.QuotationKey && !row.IsDeleted)
                .Select(row => row.BaseCd).SingleOrDefaultAsync(ct))?.TrimEnd()
                ?? throw new ErpCommerceException("C03_QUOTATION_NOT_FOUND");
            use = await EnlistPartnerAsync(request, inputHash, ct);
            if (!await BusinessPartnerWorkspaceV1.CurrentUseBaseAsync(db, use.Bp.LegalEntityKey, quoteBase, ct))
                throw new ErpCommerceException("C03_OWNER_PARTICIPANT_UNPROVEN");
            bp = await db.BusinessPartners.AsNoTracking().SingleAsync(row => row.Id == use.Bp.BpId, ct);
            // 先固定 EST 和已批准产品来源，再进入 QTN 根与明细。
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE public."T_EstimateCalc",public."T_EstimateCalcProcess",public."T_ProductMaster",
                    public."T_ProductProcess",public."T_ProductMaterial" IN SHARE MODE
                """, ct);
        }
        else
        {
            var bpQuery = db.Database.IsNpgsql()
                ? db.BusinessPartners.FromSqlInterpolated($"""SELECT * FROM public."T_WebBusinessPartner" WHERE "TenantId"={request.TenantId} AND "BpCd"=CAST({request.BusinessPartnerKey} AS bpchar) FOR UPDATE""")
                : db.BusinessPartners.FromSqlInterpolated($"SELECT * FROM dbo.T_WebBusinessPartner WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND BpCd={request.BusinessPartnerKey}");
            bp = await bpQuery.SingleOrDefaultAsync(ct);
            RequireBusinessPartner(bp, request.AccountId, allowPreRegistered: false);
        }
        var quotationQuery = db.Database.IsNpgsql()
            ? db.Quotations.FromSqlInterpolated($"""SELECT * FROM public."T_Quotation" WHERE "TenantId"={request.TenantId} AND "QtnNo"=CAST({request.QuotationKey} AS bpchar) FOR UPDATE""")
            : db.Quotations.FromSqlInterpolated($"SELECT * FROM dbo.T_Quotation WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND QtnNo={request.QuotationKey}");
        var quotation = await quotationQuery.SingleOrDefaultAsync(ct)
            ?? throw new ErpCommerceException("C03_QUOTATION_NOT_FOUND");
        var detailsQuery = db.Database.IsNpgsql()
            ? db.QuotationDetails.FromSqlInterpolated($"""SELECT * FROM public."T_QuotationDetail" WHERE "TenantId"={request.TenantId} AND "QtnNo"=CAST({request.QuotationKey} AS bpchar) FOR UPDATE""")
            : db.QuotationDetails.FromSqlInterpolated($"SELECT * FROM dbo.T_QuotationDetail WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={request.TenantId} AND QtnNo={request.QuotationKey}");
        quotation.Details = await detailsQuery.Where(d => !d.IsDeleted).OrderBy(d => d.DetailNo).ToListAsync(ct);
        if (use is not null)
        {
            if (quotation.BaseCd.TrimEnd() != quoteBase)
                throw new ErpCommerceException("C03_LOCK_PLAN_UNPROVEN");
            var estimates = quotation.Details.Select(row => row.QtnCalcNo).OfType<string>().Distinct().ToArray();
            if (await db.EstimateCalcs.CountAsync(row => estimates.Contains(row.QtnCalcNo) && !row.IsDeleted, ct) != estimates.Length)
                throw new ErpCommerceException("C03_OWNER_PARTICIPANT_UNPROVEN");
        }
        if (quotation.CustomerCd != bp!.BpCd) throw new ErpCommerceException("C03_QUOTATION_CUSTOMER_MISMATCH");
        if (quotation.RowVersion is null || Convert.ToBase64String(quotation.RowVersion) != request.QuotationVersion)
            throw new ErpCommerceException("C03_QUOTATION_VERSION_CHANGED");
        ErpQuotationAcceptance.Validate(quotation, clock.GetUtcNow());
        if (!ErpQuotationAcceptance.IsCurrent(quotation, clock.GetUtcNow()))
            throw new ErpCommerceException("C03_CUSTOMER_ACCEPTANCE_REQUIRED");
        if (quotation.TotalAmount != request.ExpectedAmount) throw new ErpCommerceException("C03_AMOUNT_MISMATCH");
        var bpCurrency = use?.Currency ?? (string.IsNullOrEmpty(bp.CurrencyCd) ? FxConstants.BaseCurrency : bp.CurrencyCd);
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
            request.RequestVersion, quotation.Id, request.QuotationVersion, inputHash, quotation.CurrencyCd!);
        var key = await orders.CreateForIntegrationAsync(dto, origin);
        if (use is not null) await RecordUseAuditAsync(request, key, use, ct);
        return (key, quotation.TotalAmount!.Value);
    }

    private async Task<BpUseReceipt> EnlistPartnerAsync(ErpOrderRequested request, string inputHash, CancellationToken ct)
    {
        try
        {
            var tx = db.Database.CurrentTransaction?.GetDbTransaction();
            BpUseScopeGate.RequireTransaction(db, tx, serializable: true);
            // 旧事件保留原 wire，可信消费身份来自本地 C03 和已核对的原指纹。
            var coordinate = await BusinessPartnerWorkspaceV1.FindUseCoordinatesAsync(db, request.BusinessPartnerKey, ct)
                ?? throw BpUseScopeGate.Unproven();
            await BpUseScopeGate.RequireBeforeEstimateAndQuoteAsync(db, ct);
            await BpUseScopeGate.HoldIamAsync(db, ct);
            await BpUseScopeGate.LockAsync(db, coordinate.Legal, false, ct);
            var verified = useSource is null ? null : await useSource.ReadAsync(db, tx!, request, coordinate.Id, coordinate.Legal, ct);
            var participant = new BpUseParticipant(db, partnerProtection ?? throw BpUseScopeGate.Unproven(), clock);
            const string purpose = "SALES_NEW_SOURCE";
            var consumer = new BpUseSourceRef(BpUseParticipant.SalesOwner, request.OpportunityId.ToString("D"),
                request.RequestVersion.ToString(CultureInfo.InvariantCulture), inputHash, purpose);
            var enlisted = await participant.EnlistAsync(tx!, new(request.TenantId, coordinate.Legal, coordinate.Id,
                request.AccountId, purpose, consumer, coordinate.Revision, verified?.ExpectedRevision, request.RequestId,
                "crm-integration", request.RequestId.ToString("D"), verified), ct);
            // 已提交意图应已在 C03 原命令分流。若账不一致，不能把原凭证用于重建订单。
            if (enlisted.Replayed) throw BpUseScopeGate.Unproven();
            return enlisted.Receipt;
        }
        catch (OrderPriceCorrectionException error)
        {
            throw new ErpCommerceException("C03_" + error.Message);
        }
    }

    private async Task RecordUseAuditAsync(ErpOrderRequested request, string key, BpUseReceipt receipt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        db.Sys_OperLogs.Add(new()
        {
            TenantId = request.TenantId, UserName = "crm-integration", Controller = "ErpRequestHandler",
            Action = "CreateOrderWithPartnerUse", StatusCode = 200, CreateDate = now.UtcDateTime, AuditAtUtc = now,
            RequestBody = JsonSerializer.Serialize(new { orderKey = key, useKey = receipt.UseKey }),
            AuditEvidenceJson = new AuditEvidenceV1("ERP_OWNER_UTC", "REQUIRED_AUDIT", "SERVICE",
                Service: "crm-integration", OperationId: request.RequestId, Outcome: "COMMITTED",
                EntityVersion: receipt.Consumer.Version, RecordedAtUtc: now, DataPolicy: "BUSINESS_REFERENCE_ONLY",
                Action: "SALES_NEW_SOURCE", OriginalOperationDigest: receipt.Consumer.Digest).Encode()
        });
        await db.SaveChangesAsync(ct);
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
