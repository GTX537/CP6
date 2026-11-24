using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommercialProductQualificationQueryV1(EngineeringSourceRefV1 ProductRef,
    string? UnitBindingVersion = null, CurrencyPolicyRefV1? CurrencyPolicyRef = null,
    EngineeringMappingRefV1? MappingRef = null);

/// <summary>按各原 Owner 的准确版本组合读取。显式选择的币种政策只适用于该次读取上下文。</summary>
public sealed record CommercialProductQualificationV1(CommercialProductIdentityV1 Product,
    CommercialProductUnitVersionV1? Unit, CurrencyPolicyVersionV1? SelectedCurrencyPolicy,
    EngineeringMappingVersionV1? Mapping, string[] Missing, string FormalAdoption = "REQUIRED_FENCED")
{
    public bool CanAdoptIdentitySite => Product.IsCurrent
        && Mapping is { CanAdoptIdentitySite: true, ConfirmedIdentitySite: not null };
}
