using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleMetricV1(string Id,string DimensionKey,UomProfileRefV1[] Units);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RulePrecisionV1(string Id,[property:JsonRequired]int Scale,string Rounding);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleDomainV1(string Id,string[] RuleFamilies,string[] MetricIds,string[] PrecisionIds,
    string[] ProductTypes,string[] Operations,[property:JsonRequired]bool AllowEmptyProducts,[property:JsonRequired]bool AllowEmptyOperations,
    [property:JsonRequired]bool AllowUnlimited,[property:JsonRequired]bool AllowOpenEnded,[property:JsonRequired]bool AllowPickable);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleReferenceDefinitionV1([property:JsonRequired]bool Enabled,RuleMetricV1[] Metrics,RulePrecisionV1[] Precisions,RuleDomainV1[] Domains);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleReferenceCommandV1(Guid OperationId,string ExpectedVersion,RuleReferenceDefinitionV1 Definition,string Reason);
public sealed record RuleReferenceUnitV1(UomProfileRefV1 Reference,UomProfileRefV1 BaseReference,string Code,string DimensionKey);
public sealed record RuleReferenceOriginalV1(Guid TenantId,Guid Id,Guid WarehouseId,string Version,Guid OperationId,Guid ActorId,
    DateTimeOffset RecordedAt,RuleReferenceDefinitionV1 Definition,RuleReferenceUnitV1[] Units,string Reason);
public sealed record RuleReferencePolicyV1(EngineeringSourceRefV1 Reference,RuleReferenceOriginalV1 Original,string OriginalJson);
public sealed record RuleReferenceContextV1(Guid TenantId,Guid ActorId,Guid WarehouseId,string WarehouseCode,string Version,RuleReferencePolicyV1? Policy)
{
    public string Qualification=>"CONFIGURATION_ONLY";
    public bool AdmissionGrant=>false;
}
public sealed record RuleReferenceResultV1(Guid OperationId,Guid WarehouseId,EngineeringSourceRefV1 Reference,bool Changed);
public sealed record RuleReferenceOperationV1(Guid OperationId,Guid WarehouseId,string State,RuleReferenceResultV1? Result=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleResolveV1(Guid OperationId,EngineeringSourceRefV1 ExpectedPolicy);
public sealed record RuleReferenceAuthorityV1(string Field,string Owner,string RegistryVersion,string Availability);
public sealed record RuleReferenceBindingFieldV1(string Field,string Owner,string Id,string Version,string RegistryVersion);
public sealed record RuleReferenceBindingV1(EngineeringSourceRefV1 Policy,string RegistryVersion,
    RuleReferenceBindingFieldV1[] Fields,RuleReferenceUnitV1[] Units)
{
    public bool RequiresCurrentRecheck=>true;
    public bool AdmissionGrant=>false;
}
