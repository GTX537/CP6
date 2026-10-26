using System.Text.Json.Serialization;

namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommercialProductUnitCommandV1
{
    public Guid OperationId { get; init; }
    public Guid ProductId { get; init; }
    public string ExpectedBindingVersion { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public UomProfileRefV1? ProfileRef { get; init; }
}

public sealed record CommercialProductUnitChoiceV1(Guid Id, string ProductCd, string ProductVersion, string? UnitCode);
public sealed record CommercialProductUnitEditorV1(Guid ProductId, string ProductVersion, string? UnitCode,
    bool ProductActive, string ExpectedBindingVersion, UomProfileRefV1? BoundProfile);
public sealed record CommercialProductUnitVersionV1(Guid TenantId, Guid ProductId, string Version, string Digest,
    string ProductVersion, string ProductDigest, string UnitCode, UomProfileRefV1 ProfileRef,
    bool IsLatestBinding, bool CurrentApplicable, string[] Reasons, string OriginalJson,
    string FormalAdoption = "REQUIRED_FENCED", string[]? Missing = null);
internal sealed record CommercialProductUnitStoredV1(Guid TenantId, Guid ProductId, string Version,
    string ProductVersion, string ProductDigest, string UnitCode, UomProfileRefV1 ProfileRef);
