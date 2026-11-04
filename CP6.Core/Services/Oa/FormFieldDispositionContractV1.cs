using System.Text.Json.Serialization;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDispositionRequestV1(Guid OperationId,Guid DraftId,Guid ExpectedDraftVersion,Guid TargetVersionId,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDispositionCommandV1(Guid OperationId,Guid ExpectedVersion,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDispositionHoldCommandV1(Guid OperationId,[property:JsonRequired] Guid? ExpectedVersion,[property:JsonRequired] bool Active,string Basis,string Reason);
public sealed record FormDispositionRefV1(Guid RequestId,Guid ReceiptVersion);
public sealed record FormDispositionHoldV1(Guid Version,bool Active);
public sealed record FormDispositionViewV1(Guid RequestId,Guid DraftId,Guid SourceVersionId,Guid TargetVersionId,
    Guid Version,string State,DateTimeOffset RecordedAtUtc,FormDispositionHoldV1? Hold,
    FormDispositionRefV1? DispositionRef,DateTimeOffset? RetainUntilUtc);
public sealed record FormDispositionOperationV1(Guid OperationId,string State,FormDispositionViewV1? Request);
public sealed record FormDispositionLossV1(string Field,string Path,string Kind,string ValueJson);
public sealed record FormDispositionEventV1(Guid Version,Guid? PreviousVersion,string State,Guid ActorId,string Reason,
    DateTimeOffset RecordedAtUtc,DateTimeOffset? RetainUntilUtc);
public sealed record FormDispositionDetailV1(FormDispositionViewV1 Request,Guid? HistoryId,string? RetentionPolicyRef,
    FormDispositionLossV1[]? Losses,FormDispositionEventV1[]? History);

// Native FORM source authority. Values stay in its immutable history, never in the disposition journal.
internal sealed record FormDispositionScopeV1(Guid EnvironmentId,Guid TenantId,Guid DraftId,Guid RequesterId,Guid HeadId,
    Guid HistoryId,Guid DraftVersion,Guid SourceVersionId,Guid TargetVersionId,string SourceSchemaDigest,
    string TargetSchemaDigest,Guid PolicyVersion,string RetentionPolicyRef,int RetentionDays);
