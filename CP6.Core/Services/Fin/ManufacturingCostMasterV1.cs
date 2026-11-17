using System.Text.Json.Serialization;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingCostPolicySubmitV1(Guid CommandId, Guid? PreviousVersionId,
    DateOnly From, DateOnly? ToExclusive, [property: JsonRequired] int CurrencyScale, string Rounding,
    string RateDateBasis, string Valuation, string Variance, [property: JsonRequired] bool AllowZeroRate,
    string ApprovalBasisReference, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingRateBasisSubmitV1(Guid CommandId, Guid? PreviousVersionId,
    Guid PolicyVersionId, Guid WorkCenterId, Guid OperationProfileId, DateOnly From, DateOnly? ToExclusive,
    string EvidenceReference, string? ZeroJustification, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingCostMasterDecisionV1(Guid CommandId, string ExpectedRevision, string Reason);

internal static class ManufacturingCostMasterV1
{
    internal const string Versions = "api/fin/cost/owner-v1/cost-master-versions";
    internal const string View = "cost-master-view";
    internal const string Policy = "POLICY";
    internal const string RateBasis = "RATE_BASIS";

    internal static bool OwnsCommandRoute(string route) => route.StartsWith($"/{Versions}/", StringComparison.Ordinal);
}
