using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementClauseV1(string Id, string OriginalText, string Locator);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSourceV1(string Kind, string OriginDescription, string SourceLabel,
    string AcquisitionBasis, RequirementClauseV1[] Clauses);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementCriterionV1(string Id, string Kind, string Object, string Conditions,
    string MethodRequirement, string? Lower = null, bool? LowerInclusive = null,
    string? Upper = null, bool? UpperInclusive = null, string? ProposedUnit = null,
    string? UnitBasis = null, string? Precision = null, string? QualitativeBasis = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RequirementEvaluationBasisV1? Evaluation = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementEvaluationBasisV1(string Schema, UomProfileRefV1 Unit,
    [property: JsonRequired] bool LowerUnbounded, [property: JsonRequired] bool UpperUnbounded);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementRelationV1(string Id, string Kind, EngineeringSourceRefV1 Target,
    string CoverageReason, string Uncovered, string Necessity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementDefinitionV1(EngineeringSourceRefV1 Source, string[] ClauseIds,
    string Interpretation, string ApplicableObject, string Necessity, string[] ClarificationQuestions,
    RequirementCriterionV1[] Criteria, RequirementRelationV1[] Relations);
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
    string Reason, DateTimeOffset RecordedAt, string OriginalJson, string[] Missing, string State = "DRAFT_ONLY");
public sealed record EngineeringRequirementResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringRequirementOperationV1(Guid OperationId, string State, EngineeringRequirementResultV1? Result = null);
internal sealed record EngineeringRequirementStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, EngineeringRequirementBodyV1 Body, Guid OperationId, string Action, Guid ActorId, string? ParentVersion,
    string Reason, DateTimeOffset RecordedAt);
