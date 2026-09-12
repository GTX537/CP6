namespace CP6.Entity.DTOs.Erp;

/// <summary>ERP staff explicitly maintains these fields through the dedicated authority operation.</summary>
public sealed record ErpBusinessPartnerProfile(byte[] RowVersion, Guid? CrmAccountId, string Currency, bool IsFrozen);
public sealed record ErpQuotationTerms(byte[] RowVersion, string Currency, DateTimeOffset ValidUntilUtc,
    string OrderType, DateTime? OrderDeliveryDate);
public sealed record ErpQuotationAccept(byte[] RowVersion, string AcceptanceReference);
public sealed record ErpQuotationWithdraw(byte[] RowVersion);
