using System.Text.Json.Serialization;
namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationOptionDefinitionV1(string Name, string Type, bool? Required, bool? AllowAnyValue, EngineeringOptionV1[] AllowedValues);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationConstraintV1(string Id, string Kind, EngineeringOptionV1 When, EngineeringOptionV1 Then, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationRuleBodyV1(string Scope, string Name, EngineeringProductRefV1 ProductRef,
    ConfigurationOptionDefinitionV1[] Definitions, ConfigurationConstraintV1[] Constraints,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EngineeringCheckPolicyV1? EngineeringChecks = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationRuleCommandV1
{
    public Guid OperationId { get; init; }
    public Guid RuleId { get; init; }
    public string Action { get; init; } = "";
    public string? ExpectedVersion { get; init; }
    public Guid? ExpectedEtag { get; init; }
    public EngineeringSourceRefV1? ExpectedActiveRef { get; init; }
    public string? ExpectedAuthorityVersion { get; init; }
    public Guid? DepartmentId { get; init; }
    public ConfigurationRuleBodyV1? Body { get; init; }
    public string Reason { get; init; } = "";
}
public sealed record ConfigurationRuleScopeV1(Guid TenantId, Guid ActorId);
public sealed record ConfigurationRuleDepartmentV1(Guid Id, string Code, string Name);
public sealed record ConfigurationRuleCreationContextV1(Guid TenantId, Guid ActorId, string AuthorityVersion, bool AllowsUnbound, ConfigurationRuleDepartmentV1[] Departments);
internal sealed record ConfigurationRuleStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId, Guid Etag,
    string State, ConfigurationRuleBodyV1 Body, string Action, string Reason, string? ParentVersion, EngineeringSourceRefV1? PreviousActiveRef, Guid ActorId, DateTimeOffset RecordedAt);
public sealed record ConfigurationRuleSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference, Guid Etag, bool IsCurrent, bool IsActive,
    EngineeringSourceRefV1? ActiveRef, string State, ConfigurationRuleBodyV1 Body, string Action, string Reason,
    string? ParentVersion, Guid ActorId, DateTimeOffset RecordedAt, string OriginalJson, string[]? Missing = null);
public sealed record ConfigurationRuleResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record ConfigurationRuleOperationV1(Guid OperationId, string State, ConfigurationRuleResultV1? Result = null);
public sealed record ConfigurationOptionFindingV1(string Code, string Name, string? ConstraintId = null);
public sealed record ConfigurationOptionCheckV1(string Scope, EngineeringSourceRefV1 RuleRef, string OptionsDigest,
    string Status, ConfigurationOptionFindingV1[] Findings, DateTimeOffset ObservedAt);
