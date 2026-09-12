namespace CP6.ErpLive.Fixture;

internal sealed class FixtureException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal sealed record LiveInput(string CoreOrigin, string CrmOrigin, string DaprHttpEndpoint,
    string DaprGrpcEndpoint, string DaprApiToken, string DaprAppToken,
    string CertificatePath, string CertificatePassword);

internal sealed record DatabaseOwnership(string SchemaId, string Database, string DataSource, string OwnerToken,
    DateTimeOffset CreatedAtUtc, Guid[] TenantIds);

// This document is private: it intentionally contains local database and service/user credentials.
internal sealed record LiveFixtureDocument(string SchemaId, string Database, string Connection, string CoreContentRoot,
    string CoreOrigin, string CrmOrigin, string CoreAppId, string BrowserSecret, LiveTenant[] Tenants,
    BaselineAuthority Authority);

internal sealed record LiveTenant(Guid Id, string Slug, string Region, Guid AccountId, Guid DepartmentId,
    string ReaderClientId, string ReaderSecret, LiveUser[] Users, LiveBusinessPartner BusinessPartner,
    LiveQuotation Quotation, LiveProduct Product);

internal sealed record LiveUser(Guid Id, string Label, string UserName, string Password, int RoleId, Guid DepartmentId,
    string BaseCode, string SalesStaffCode, string BusinessStaffCode);

internal sealed record LiveBusinessPartner(Guid Id, string Key, string RowVersion, int Status, Guid AccountId,
    bool Frozen, string Currency);

internal sealed record LiveQuotation(Guid Id, string Key, string RowVersion, string BusinessPartnerKey,
    decimal TotalAmount, string Currency, DateTimeOffset ValidUntilUtc, DateTimeOffset AcceptedAtUtc,
    string AcceptedContentSha256, int DetailNo, decimal Quantity, decimal UnitPrice);

internal sealed record LiveProduct(Guid Id, string Key, string RowVersion, string QuotationKey, string Branch,
    int Status, bool WorkflowApproved);

internal sealed record BaselineAuthority(string Kind, string CoreAssemblySha256, string ApiAssemblySha256,
    string ErpContractBundleSha256, string IdentityContractBundleSha256, string[] AppliedMigrations,
    string DatabaseBaselineSha256, DateTimeOffset SeededAtUtc);
