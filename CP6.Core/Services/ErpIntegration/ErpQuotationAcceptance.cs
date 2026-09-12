using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.ErpIntegration;

public sealed class ErpCommerceException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Customer acceptance binds the offered content, independently of ERP internal approval.</summary>
public static class ErpQuotationAcceptance
{
    // OrderDetail.Amount is decimal(21,8), narrower in integer digits than the quotation's decimal(18,2).
    private const decimal OrderLineAmountExclusiveLimit = 10000000000000m;

    public static bool IsCurrency(string? value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');

    public static void Validate(Quotation quotation, DateTimeOffset now)
    {
        if (quotation.IsDeleted || quotation.EstimateCheckFlg != 9)
            throw new ErpCommerceException("C03_QUOTATION_APPROVAL_REQUIRED");
        if (!IsCurrency(quotation.CurrencyCd) || quotation.ValidUntilUtc is null ||
            string.IsNullOrWhiteSpace(quotation.OrderType))
            throw new ErpCommerceException("C03_QUOTATION_TERMS_REQUIRED");
        if (quotation.ValidUntilUtc <= now) throw new ErpCommerceException("C03_QUOTATION_EXPIRED");
        var lines = quotation.Details.Where(d => !d.IsDeleted).ToArray();
        if (lines.Length == 0 || lines.Length > Erp.OrderService.MaxDetailLimit ||
            lines.Select(d => d.DetailNo).Distinct().Count() != lines.Length ||
            lines.Any(d => d.DetailNo <= 0 || d.QtnNo != quotation.QtnNo || d.TenantId != quotation.TenantId))
            throw new ErpCommerceException("C03_QUOTATION_LINES_INVALID");
        try
        {
            if (lines.Any(d => d.Quantity is null or <= 0 || d.UnitPrice is null or < 0 || d.Amount is null or < 0 ||
                    d.Amount >= OrderLineAmountExclusiveLimit ||
                    d.Amount != decimal.Round(d.Quantity.Value * d.UnitPrice.Value, 2, MidpointRounding.AwayFromZero)) ||
                quotation.TotalAmount != lines.Sum(d => d.Amount!.Value))
                throw new ErpCommerceException("C03_QUOTATION_AMOUNT_INVALID");
        }
        catch (OverflowException) { throw new ErpCommerceException("C03_QUOTATION_AMOUNT_INVALID"); }
    }

    public static bool IsCurrent(Quotation quotation, DateTimeOffset now)
    {
        if (quotation.CustomerAcceptedAtUtc is null || quotation.CustomerAcceptedAtUtc > now ||
            string.IsNullOrWhiteSpace(quotation.CustomerAcceptanceReference) ||
            string.IsNullOrWhiteSpace(quotation.CustomerAcceptedBy) || quotation.AcceptedContentSha256 is null) return false;
        try { Validate(quotation, now); }
        catch (ErpCommerceException) { return false; }
        return quotation.AcceptedContentSha256 == ContentHash(quotation);
    }

    public static void Clear(Quotation quotation)
    {
        quotation.CustomerAcceptedAtUtc = null;
        quotation.CustomerAcceptanceReference = null;
        quotation.CustomerAcceptedBy = null;
        quotation.AcceptedContentSha256 = null;
    }

    public static string ContentHash(Quotation q)
    {
        // Explicit stable representation excludes audit/printing/internal-confirmation timestamps.
        // All offered customer, price, line, fulfilment and payment content is included.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            q.TenantId, q.QtnNo, q.BaseCd, q.StaffCd, q.CustomerCd, q.CustomerName,
            q.ProjectNoParent, q.ProjectNoChild, q.ProjectNoMaterial, q.FscMgmtNo,
            q.ContactPerson, q.DeliveryLocation, q.DeliveryDeadline, q.Freight, q.PaymentCondition,
            q.ValidityPeriod, q.CurrencyCd,
            ValidUntilUtc = q.ValidUntilUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            q.OrderType, OrderDeliveryDate = q.OrderDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalAmount = Number(q.TotalAmount), q.PrintTotalFlg,
            Notes = new[] { q.QtnNote01, q.QtnNote02, q.QtnNote03, q.QtnNote04, q.QtnNote05,
                q.QtnNote06, q.QtnNote07, q.QtnNote08, q.QtnNote09, q.QtnNote10, q.QtnNote11,
                q.QtnNote12, q.QtnNote13, q.QtnNote14, q.QtnNote15 },
            q.DimensionPrint,
            CalcNotes = new[] { q.CalcNote01, q.CalcNote02, q.CalcNote03, q.CalcNote04,
                q.CalcNote05, q.CalcNote06, q.CalcNote07, q.CalcNote08 },
            Lines = q.Details.Where(d => !d.IsDeleted).OrderBy(d => d.DetailNo).Select(d => new
            {
                d.TenantId, d.QtnNo, d.DetailNo, d.ItemName1, d.ItemName2,
                Quantity = Number(d.Quantity), UnitPrice = Number(d.UnitPrice),
                d.Unit, Amount = Number(d.Amount), d.PrintTotalFlg, d.QtnCalcNo
            })
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // SQL restores the declared decimal scale and drops DateTime.Kind. Neither changes
    // the offer, so hashes must bind numeric values and the delivery calendar date.
    private static string? Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);
}
