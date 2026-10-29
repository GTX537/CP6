using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPolicyReasonV1([property: JsonRequired] string Code, [property: JsonRequired] string Label);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPolicyDefinitionV1([property: JsonRequired] string Name, [property: JsonRequired] bool Enabled,
    [property: JsonRequired] RmaPolicyReasonV1[] ReasonOptions, [property: JsonRequired] string[] AllowedOutcomes,
    [property: JsonRequired] int? ReturnWindowDaysAfterDelivery, [property: JsonRequired] bool RequireIndependentApprover);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPolicyPublishV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef,
    [property: JsonRequired] FulfilmentRefV1? ExpectedPolicyRef, [property: JsonRequired] RmaPolicyDefinitionV1 Policy,
    [property: JsonRequired] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPolicyReadV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef, [property: JsonRequired] FulfilmentRefV1? Reference);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaPolicyCommandQueryV1([property: JsonRequired] FulfilmentRefV1 ScopeOptionRef, [property: JsonRequired] Guid CommandId);
public sealed record RmaPolicyVersionV1(FulfilmentRefV1 Reference, RmaTrustedScopeV1 Scope, RmaPolicyDefinitionV1 Policy,
    DateTimeOffset PublishedAt, FulfilmentRefV1 PublishedBy, string Reason, FulfilmentRefV1? PreviousRef);
public sealed record RmaPolicyLookupV1(string State, RmaPolicyVersionV1? Value);
public sealed record RmaPolicyCommandResultV1(string Status, RmaPolicyVersionV1? Value, bool CanInferNoEffect = false);
