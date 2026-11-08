using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
public sealed class PrivacyRestoreCandidateOptionsV1
{
    [JsonIgnore] public string ConnectionString{get;set;}="";
    public Guid RegistrationVersion{get;set;}
    public string BackupSha256{get;set;}="";
    public string DisplayName{get;set;}="";
}
public sealed class PrivacyRestoreOptionsV1
{
    public Dictionary<Guid,PrivacyRestoreCandidateOptionsV1> Candidates{get;set;}=[];
    [JsonIgnore] public string ControlConnectionString{get;set;}="";
}
public sealed record PrivacyRestoreDatabaseV1(string Name,string Address,int Port,long Oid);
public sealed record PrivacyRestoreCandidateRefV1(Guid Id,Guid RegistrationVersion,string BackupSha256,string DisplayName);
public sealed record PrivacyRestoreContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,Guid SourceVersion,string[] Actions,PrivacyRestoreCandidateRefV1[] Candidates);
public sealed record PrivacyRestoreScopeV1(Guid EnvironmentId,Guid CandidateId,Guid RegistrationVersion,string BackupSha256,Guid SourceVersion,
    PrivacyRestoreDatabaseV1 Source,PrivacyRestoreDatabaseV1 Target,int TombstoneCount,string[] UncoveredOwners,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int ExportPayloadTombstoneCount=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int SecurityFieldTombstoneCount=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int TenantPayloadTombstoneCount=0);
public sealed record PrivacyRestorePreviewV1(PrivacyRestoreScopeV1 Scope,string ScopeDigest,string[] Blockers);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyRestorePlanRequestV1(Guid OperationId,Guid CandidateId,Guid RequestRef,string ExpectedScopeDigest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyRestoreExecuteV1(Guid OperationId,Guid ExpectedVersion,string ExpectedScopeDigest,Guid ConfirmationRef);
public sealed record PrivacyRestoreMetaOperationV1(Guid OperationId,Guid? PlanId,Guid? Version,string State);
public sealed record PrivacyRestoreUnitV1(Guid SourcePlanId,Guid TargetTenantId,Guid SubjectId,string Outcome,Guid? BeforeVersion,Guid? AfterVersion,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Owner=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] Guid? ArtifactId=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? RemovedBytes=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] Guid? LogId=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? ClearedValueCount=null);
public sealed record PrivacyRestoreResultV1(Guid OperationId,string State,Guid? PlanId,Guid? CandidateId,Guid? SourceVersion,string? ScopeDigest,
    PrivacyRestoreUnitV1[]? Units,string[]? UncoveredOwners,DateTimeOffset? At,string AggregateState="PARTIAL",
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] PrivacyRestoreTenantPayloadUnitV1[]? TenantPayloadUnits=null);
public sealed record PrivacyRestorePlanV1(Guid PlanId,Guid RequestRef,Guid RequesterId,Guid Version,string State,Guid? ApproverId,
    PrivacyRestoreScopeV1 Scope,string ScopeDigest,bool ApprovalCurrent,string[] CurrentBlockers,PrivacyRestoreResultV1? Outcome);

public sealed record PrivacyRestoreTenantPayloadUnitV1(Guid SourcePlanId,Guid TargetTenantId,Guid ArtifactId,string Owner,string Outcome,string BeforeState,Guid? BeforeVersion,long RemovedBytes);
