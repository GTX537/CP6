namespace CP6.Core.Services.Erp;

public sealed record BusinessPartnerMappingAuthorityBindingV1(Guid TenantId, string LegalEntityKey, Guid OrganizationId,
    string Purpose, string Authority, string Epoch, string KeyId, byte[] PublicKey, string RouteKey);

public sealed record BusinessPartnerMappingOriginalV1(byte[] Body, byte[] ReceivedTrust);

public sealed record BusinessPartnerMappingDeliveryResultV1(int Status, string? ResponseHash, byte[]? ResponseBody);

/// <summary>固定 Owner 读取桥、七字段投递和本域决定签发共用已登记的信任边界。</summary>
public abstract class BusinessPartnerMappingAuthorityV1
{
    public abstract Task<BusinessPartnerMappingOriginalV1?> ReadAsync(BusinessPartnerMappingAuthorityBindingV1 binding,
        string operation, byte[] requestBody, CancellationToken ct);

    public abstract Task<BusinessPartnerMappingDeliveryResultV1?> DeliverAsync(BusinessPartnerMappingAuthorityBindingV1 binding,
        byte[] originalBody, string bodyHash, CancellationToken ct);

    public abstract byte[]? SignDecision(BusinessPartnerMappingAuthorityBindingV1 binding, byte[] signingBytes);

    public abstract bool VerifyDecision(BusinessPartnerMappingAuthorityBindingV1 binding, byte[] signingBytes, byte[] signature);
}
