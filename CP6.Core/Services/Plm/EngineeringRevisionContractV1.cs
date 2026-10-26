using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRevisionRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRevisionOriginV1(string Type, string? NoParentReason = null,
    EngineeringSourceRefV1? ParentBaselineRef = null, EngineeringSourceRefV1? ChangeRef = null, EngineeringSourceRefV1? LegacyRef = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringOptionV1(string Name, string Type, string? Text = null, string? Decimal = null, bool? Boolean = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringConfigurationInputV1(EngineeringOptionV1[] Options, string Applicability, string Reason,
    EngineeringSourceRefV1? RuleRef = null);
public sealed record EngineeringConfigurationDraftV1(EngineeringRevisionRefV1 Reference, EngineeringConfigurationInputV1 Input, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConfigurationOptionCheckV1? OptionCheck = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringComponentDraftV1(Guid OccurrenceId, string Kind, string Path, int SemanticOrder,
    string Applicability, string Reason, string Note, EngineeringSourceRefV1? SourceRef = null);
public sealed record EngineeringWorkingDraftV1(EngineeringProductRefV1? ProductRef, string Goal,
    EngineeringConfigurationDraftV1? Configuration, EngineeringComponentDraftV1[] Components);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringDraftPatchV1
{
    public string Op { get; init; } = "";
    public string? Goal { get; init; }
    public EngineeringProductRefV1? ProductRef { get; init; }
    public EngineeringConfigurationInputV1? Configuration { get; init; }
    public EngineeringComponentDraftV1? Component { get; init; }
    public Guid? OccurrenceId { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRevisionCommandV1
{
    public Guid OperationId { get; init; }
    public Guid RevisionId { get; init; }
    public string Action { get; init; } = "";
    public string? ExpectedVersion { get; init; }
    public Guid? ExpectedEtag { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public EngineeringRevisionOriginV1? Origin { get; init; }
    public EngineeringProductRefV1? ProductRef { get; init; }
    public string? Goal { get; init; }
    public string Reason { get; init; } = "";
    public EngineeringDraftPatchV1[]? Patches { get; init; }
}
public sealed record EngineeringRevisionScopeV1(Guid TenantId, Guid ActorId);
public sealed record EngineeringRevisionDepartmentV1(Guid Id, string Code, string Name);
public sealed record EngineeringRevisionCreationContextV1(Guid TenantId, Guid ActorId, string AuthorityVersion,
    bool AllowsUnbound, EngineeringRevisionDepartmentV1[] Departments);
public sealed record EngineeringNumberAllocationV1(Guid RuleId, string BizKey, string Prefix, string? DateFormat,
    int SeqLength, int ResetCycle, string? Period, int AllocatedValue, DateTimeOffset IssuedAt);
internal sealed record EngineeringRevisionStoredV1(Guid TenantId, Guid Id, string Version, string Code, Guid CreatorId,
    Guid CandidateId, Guid Etag, EngineeringRevisionOriginV1 Origin, EngineeringNumberAllocationV1 Numbering,
    EngineeringWorkingDraftV1 Draft, string Reason, string? ParentVersion, DateTimeOffset RecordedAt);
public sealed record EngineeringRevisionSnapshotV1(Guid TenantId, EngineeringRevisionRefV1 Reference, string RevisionCode,
    Guid Etag, EngineeringRevisionRefV1 CandidateRef, bool IsCurrent, string State, EngineeringRevisionOriginV1 Origin,
    EngineeringWorkingDraftV1 Draft, string Reason, string? ParentWorkingVersion, DateTimeOffset RecordedAt,
    string OriginalJson, string Readiness = "DRAFT_ONLY", string[]? Missing = null,
    string Review = "NOT_REQUESTED", string Freeze = "NOT_REQUESTED");
public sealed record EngineeringRevisionResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record EngineeringRevisionOperationV1(Guid OperationId, string State, EngineeringRevisionResultV1? Result = null);
public sealed record EngineeringWorkingChangeV1(string Path, JsonElement? Before, JsonElement? After);
public sealed record EngineeringWorkingDiffV1(EngineeringRevisionRefV1 Before, EngineeringRevisionRefV1 After, EngineeringWorkingChangeV1[] Changes);
