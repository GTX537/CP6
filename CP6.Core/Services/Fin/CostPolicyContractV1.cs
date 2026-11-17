using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CostPolicyRefV1(string Owner, string Id, string Version, string Digest, string Purpose);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CostQualitySourceV1(string Kind, CostPolicyRefV1 Reference);

// V1 的既有计算参数仍由原请求提供；此扩展补齐 COST-POLICY-1，身份和批准字段由 Finance Owner 生成。
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CostPolicyDefinitionV1(string Jurisdiction, string Standard,
    [property: JsonRequired] int UnitCostScale, string MaterialMethod, string WipMethod,
    string AllocationMethod, string Residual, string TrialTreatment, string ZeroTreatment,
    string CostCenterRequirement, CostPolicyRefV1 CostCenterRuleRef, string[] AllowedClasses,
    string[] RequiredQualityKinds, CostPolicyRefV1 CapitalizationRuleRef, CostPolicyRefV1 ScrapRuleRef,
    [property: JsonRequired] CostPolicyRefV1? ProvisionalRuleRef, CostPolicyRefV1 GlAccountingPolicyRef,
    CostPolicyRefV1 SourceContractRef, CostPolicyRefV1 ApprovalBasisRef, CostQualitySourceV1[] QualitySources);

public sealed record CostPolicyIntervalV1(DateOnly From, DateOnly? ToExclusive);
public sealed record CostPolicyV1(string Schema, FinanceScopeV1 Scope, string PolicyId, string Version,
    CostPolicyIntervalV1 Interval, string Jurisdiction, string Standard, string Currency, int CurrencyScale,
    int UnitCostScale, string Rounding, string MaterialMethod, string RateDateBasis, string ValuationMethod,
    string WipMethod, string AllocationMethod, string Residual, string VarianceTreatment, string TrialTreatment,
    string ZeroTreatment, string RateZeroPermission, string CostCenterRequirement, CostPolicyRefV1 CostCenterRuleRef,
    string[] AllowedClasses, string[] RequiredQualityKinds, CostPolicyRefV1 CapitalizationRuleRef,
    CostPolicyRefV1 ScrapRuleRef, CostPolicyRefV1? ProvisionalRuleRef, CostPolicyRefV1 GlAccountingPolicyRef,
    CostPolicyRefV1 SourceContractRef, CostPolicyRefV1 ApprovalBasisRef, string Issuer,
    CostPolicyRefV1 QualificationRef, string State, string ControlRevision);

public sealed record CostPolicySourceOriginalV1(CostPolicyRefV1 Reference, FinanceScopeV1 Scope,
    string Role, string OriginalJson);
public sealed record CostPolicyAdoptionV1(CostPolicyV1 Policy, CostQualitySourceV1[] QualitySources,
    string QualificationOriginal, CostPolicySourceOriginalV1[] Sources, string PolicyOriginalJson);

// 外域参与者负责原合同、当前权限及资格；采用时必须持有原 Owner 的数据库门至外层事务结束。
// 不能以 HTTP 观察、客户端提交正文或独立连接的读取实现本接口。未安装 Owner 直接拒绝采用。
public sealed record CostPolicySourceContextV1(CP6Context Database, FinanceScopeV1 Scope, Guid ActorId,
    DateOnly BusinessDate, bool ForAdoption, Action<Func<CancellationToken, Task>> AtCommit);
public interface ICostPolicyExternalSourceV1
{
    string Owner { get; }
    Task<CostPolicySourceOriginalV1> ReadInUowAsync(CostPolicySourceContextV1 context,
        CostPolicyRefV1 reference, string role, CancellationToken ct);
}
