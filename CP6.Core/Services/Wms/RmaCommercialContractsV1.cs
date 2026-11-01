using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaCommercialRequestV1(FulfilmentRefV1 ExpectedRmaHeadRef, FulfilmentRefV1 ExpectedAuthorizationRef,
    FulfilmentRefV1[] ReturnShareRefs, string RequestedOutcome, string ReasonCode);
public sealed record RmaCommercialLineV1(FulfilmentRefV1 ShareRef, RmaReturnShareV1 Share, RmaSourceCoordinateV1 Source);
public sealed record RmaCommercialOriginalV1(Guid RequestId, Guid TenantId, Guid ApplicationId, RmaTrustedScopeV1 Scope,
    FulfilmentRefV1 ApplicationRef, FulfilmentRefV1 AuthorizationRef, RmaCommercialLineV1[] Lines,
    string RequestedOutcome, string ReasonCode, FulfilmentRefV1[] PhysicalReceiptRefs, DateTimeOffset RequestedAt);
public sealed record RmaCommercialWorkV1(FulfilmentRefV1 RequestRef, RmaCommercialOriginalV1 Request, int Revision,
    string ObservationState, JsonElement? CommercialOutcome, JsonElement? FinancialOutcome, DateTimeOffset ObservedAt,
    string[] ReasonCodes, string EvidenceMode)
{
    public bool CanInferNoEffect => false;
    public bool FinancialSourceCurrent => FinancialOutcome==null || FinancialOutcome.Value.GetProperty("axes").EnumerateArray()
        .All(x=>x.GetProperty("commercialOutcomeRef").ValueKind==JsonValueKind.Null || CommercialOutcome!=null
            && x.GetProperty("commercialOutcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)==CommercialOutcome.Value.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json));
}
public sealed record RmaCommercialContextV1(FulfilmentRefV1 ApplicationRef, RmaAuthorizationDecisionV1? Authorization,
    FulfilmentRefV1[] ShareRefs, RmaCommercialWorkV1[] Requests, string[] Capabilities);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaCommercialQueryV1([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] Guid CommandId);
public sealed record RmaCommercialCommandResultV1(string Status, RmaCommercialWorkV1? Value)
{
    public bool CanInferNoEffect => false;
}
// External Owner boundary. The browser cannot submit an approval, price, posting receipt or observation.
public sealed record RmaCommercialOwnerRequestV1(FulfilmentRefV1 RequestRef, RmaCommercialOriginalV1 Request);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaCommercialObservationV1(Guid TenantId, FulfilmentRefV1 RequestRef, string State,
    JsonElement? CommercialOutcome, JsonElement? FinancialOutcome, DateTimeOffset ObservedAt, string[] ReasonCodes, string EvidenceMode);
public interface IRmaCommercialGatewayV1
{
    Task<RmaCommercialObservationV1> ObserveAsync(RmaCommercialOwnerRequestV1 request, bool submit, CancellationToken ct);
}
public sealed class RmaCommercialGatewayOptionsV1
{
    public string? BaseUrl { get; set; }
    public string? BearerToken { get; set; }
}
