using System.Text.Json.Serialization;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringCompositionPolicyV1(string Kind, string Applicability, int MinimumOccurrences, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringCheckPolicyV1(string Schema, string ProductCategory, string Manufacturing,
    string ConfigurationApplicability, string Reason, EngineeringCompositionPolicyV1[] Composition);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringSourceCheckCommandV1(Guid OperationId, EngineeringRevisionRefV1 Revision,
    EngineeringRevisionRefV1 Candidate, Guid ExpectedEtag, EngineeringSourceRefV1 Policy, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EngineeringSourceRefV1? OutputRelations = null);
public sealed record EngineeringCheckItemV1(string Path, string State, string Code, string Reason,
    EngineeringSourceRefV1? Source = null);
public sealed record EngineeringSourcePinV1(string Path, EngineeringSourceRefV1 Reference, string Provider,
    string NativeSchema, string Capability, string Watermark, string OriginalJson, string[] Findings);
public sealed record EngineeringCheckManifestV1(string Schema, string Canonicalization, string Algorithm,
    string OriginalJson, string Digest);
public sealed record EngineeringCheckRunV1(Guid TenantId, Guid Id, Guid ActorId, DateTimeOffset CheckedAt,
    EngineeringSourceCheckCommandV1 Command, EngineeringWorkingDraftV1 Input, ConfigurationRuleBodyV1 PolicyBody,
    string InputDigest, EngineeringCheckManifestV1? Manifest, EngineeringCheckItemV1[] Items,
    EngineeringSourcePinV1[] Sources, string State, string AuthorityWatermark,
    string Purpose = "TECHNICAL_PRECHECK_ONLY");
public sealed record EngineeringCheckViewV1(EngineeringSourceRefV1 Reference, EngineeringCheckRunV1 Original,
    string OriginalJson, string Freshness, string[] Changes, string Freeze = "NOT_REQUESTED");
public sealed record EngineeringSourceCheckResultV1(Guid OperationId, Guid CheckRunId, Guid RevisionId, string Digest);
public sealed record EngineeringSourceCheckOperationV1(Guid OperationId, string State, EngineeringSourceCheckResultV1? Result = null);
