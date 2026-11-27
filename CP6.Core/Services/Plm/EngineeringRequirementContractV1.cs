using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementClauseV1(string Id, string OriginalText, string Locator,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementClauseTermsV1? Terms = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSourceV1(string Kind, string OriginDescription, string SourceLabel,
    string AcquisitionBasis, RequirementClauseV1[] Clauses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementOriginV1? Origin = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementOriginV1(EngineeringSourceRefV1? Reference, Guid? OccurrenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementActFileCoordinatesV1? ActFile = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementActFileCoordinatesV1(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ActivityId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivityRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReferenceSetVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReferenceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AttachmentId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContentSha256 = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementClarificationV1(Guid Id, EngineeringSourceRefV1 Source, string ClauseId, string Question,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementOriginV1? Reply = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementCriterionV1(string Id, string Kind, string Object, string Conditions,
    string MethodRequirement, string? Lower = null, bool? LowerInclusive = null,
    string? Upper = null, bool? UpperInclusive = null, string? ProposedUnit = null,
    string? UnitBasis = null, string? Precision = null, string? QualitativeBasis = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementEvaluationBasisV1? Evaluation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? SourceClauseIds = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementEvaluationBasisV1(string Schema, UomProfileRefV1 Unit,
    [property: JsonRequired] bool LowerUnbounded, [property: JsonRequired] bool UpperUnbounded);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementRelationV1(string Id, string Kind, EngineeringSourceRefV1 Target,
    string CoverageReason, string Uncovered, string Necessity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementCoverageLinkV1[]? Coverage = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementCoverageLinkV1(string ParentClauseId, string ChildClauseId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementDecompositionV1(EngineeringSourceRefV1 Parent, EngineeringSourceRefV1[] Children, string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementDefinitionV1(EngineeringSourceRefV1 Source, string[] ClauseIds,
    string Interpretation, string ApplicableObject, string Necessity, string[] ClarificationQuestions,
    RequirementCriterionV1[] Criteria, RequirementRelationV1[] Relations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementClarificationV1[]? Clarifications = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementDecompositionV1? Decomposition = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementCoverageEvidenceV1(EngineeringSourceRefV1 Child, string ChildClauseId, string Reason, string Scope,
    RequirementClauseTermsV1? Terms);
public sealed record RequirementClauseCoverageV1(string ClauseId, string State, RequirementCoverageEvidenceV1[] Children,
    RequirementClauseTermsV1? Terms);
public sealed record RequirementCoverageV1(EngineeringSourceRefV1 Parent, bool Complete, RequirementClauseCoverageV1[] Clauses);
public sealed record EngineeringRequirementDiffV1(EngineeringSourceRefV1 Before, EngineeringSourceRefV1 After,
    EngineeringWorkingChangeV1[] Changes);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRequirementBodyV1(string Kind, string Name,
    RequirementSourceV1? Source = null, RequirementDefinitionV1? Requirement = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRequirementCommandV1
{
    public Guid OperationId { get; init; }
    public Guid ObjectId { get; init; }
    public string Action { get; init; } = "";
    public string? ExpectedVersion { get; init; }
    public Guid? ExpectedEtag { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public string Reason { get; init; } = "";
    public EngineeringRequirementBodyV1 Body { get; init; } = null!;
}
public sealed record EngineeringRequirementSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference, Guid Etag,
    bool IsCurrent, EngineeringRequirementBodyV1 Body, Guid AuthorId, Guid ActorId, string? ParentVersion,
    string Reason, DateTimeOffset RecordedAt, string OriginalJson, string[] Missing, string State = "DRAFT_ONLY",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementCoverageV1? Coverage = null);
public sealed record EngineeringRequirementResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringRequirementOperationV1(Guid OperationId, string State, EngineeringRequirementResultV1? Result = null);
internal sealed record EngineeringRequirementStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, EngineeringRequirementBodyV1 Body, Guid OperationId, string Action, Guid ActorId, string? ParentVersion,
    string Reason, DateTimeOffset RecordedAt);
