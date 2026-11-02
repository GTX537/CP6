using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormRoleV2(int RoleId, Dictionary<string, string> Fields);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormPolicyInputV2(Guid OperationId, string ExpectedVersion, bool Enabled, bool AllowsUnbound,
    PersonalFormRoleV2[] Roles, string RetentionPolicyRef, int RetentionDays, bool AllowRestore, string Reason);
public sealed record PersonalFormPolicyV2(Guid Version, bool Enabled, bool AllowsUnbound, PersonalFormRoleV2[] Roles,
    string RetentionPolicyRef, int RetentionDays, bool AllowRestore);
public sealed record PersonalFormContextV2(string FormKey, Guid FormVersionId, int FormVersion, string Name, string SchemaJson,
    string ActivationRevision, string? BindingRevision, Guid? FlowVersionId, int? FlowVersion, string? FlowActivationRevision,
    bool AllowsUnbound, string[] Capabilities, string ContextVersion, Dictionary<string,string> FieldMask, string TitleAccess, string EvidenceState = "EXACT_PIN");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormCreateV2(Guid FormVersionId, JsonElement Data, string? Title);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormSaveV2(JsonElement Data, string? Title);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormDirectV2(string Kind, Guid ExpectedFormVersionId, string ExpectedActivationRevision,
    [property: JsonRequired] string? ExpectedBindingRevision, [property: JsonRequired] Guid? ExpectedFlowVersionId,
    [property: JsonRequired] string? ExpectedFlowActivationRevision, JsonElement Data);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormDraftSubmitV2(string Kind, Guid ExpectedFormVersionId, string ExpectedActivationRevision,
    [property: JsonRequired] string? ExpectedBindingRevision, [property: JsonRequired] Guid? ExpectedFlowVersionId,
    [property: JsonRequired] string? ExpectedFlowActivationRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormReasonV2(string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormRebasePreviewInputV2(Guid TargetVersionId, string ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormRebaseInputV2(Guid PreviewId, string PreviewDigest, Guid TargetVersionId, bool ConfirmRemovedValues);
public sealed record PersonalFormDraftV2(Guid DraftId, string FormKey, Guid FormVersionId, int FormVersion, int LatestPublishedVersion,
    string Name, string SchemaJson, string DataJson, string? Title, string RowVersion, int Status, bool Stale,
    DateTimeOffset UpdatedAtUtc, Dictionary<string,string> FieldMask, string[] Capabilities, Guid LatestPublishedVersionId, string TitleAccess);
public sealed record PersonalFormDraftItemV2(Guid DraftId, string FormKey, string Name, int FormVersion, int LatestPublishedVersion,
    string? Title, string RowVersion, DateTimeOffset UpdatedAtUtc, bool Stale);
public sealed record PersonalFormPageV2(PersonalFormDraftItemV2[] Items, int Total, int Page, int PageSize);
public sealed record PersonalFormSubmissionV2(Guid FormDataId, Guid FormDefVersionId, int FormVersion,
    Guid? FlowInstanceId, Guid? FlowDefVersionId, int? FlowVersion, string ReceiptRef, string CommandId,
    string? BindingRevision, string SubmissionState);
public sealed record PersonalFormResultV2(string CommandId, string Outcome, Guid? ResourceId, string? ResourceVersion,
    JsonElement? Result, DateTimeOffset? RecordedAtUtc, string? CorrelationId);
public sealed record PersonalFormRebaseChangeV2(string Path, string Kind, bool HasValue);
public sealed record PersonalFormRebasePreviewV2(Guid PreviewId, string Digest, Guid SourceVersionId, Guid TargetVersionId,
    string ExpectedVersion, DateTimeOffset ExpiresAtUtc, PersonalFormRebaseChangeV2[] Changes, string[] ValidationErrors, bool RequiresConfirmation);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormPreviewInputV2(Guid FormVersionId, JsonElement Data);
public sealed record PersonalFormPreviewV2(string DataJson, string[] HiddenFields, bool Valid, string[] Errors);
