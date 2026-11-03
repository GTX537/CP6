using System.Text.Json.Serialization;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeTransferBatchFilterV2(Guid FromUserId,Guid ToUserId,string Reason,string? FlowKey,DateTimeOffset? BeforeUtc,Guid[]? ExplicitTaskIds);
public sealed record NativeTransferBatchItemV2(Guid TaskId,Guid InstanceId,string ContextKind,string FlowKey,
    string TaskVersion,string InstanceVersion,string ItemCommandId);
public sealed record NativeTransferBatchPreviewV2(Guid BatchId,DateTimeOffset PreviewedAt,string PreviewDigest,
    NativeTransferBatchFilterV2 Filter,NativeTransferBatchItemV2[] Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeTransferBatchConfirmV2(Guid BatchId,string PreviewDigest);
public sealed record NativeTransferBatchItemResultV2(Guid TaskId,string ItemCommandId,string State,string? Error,PersonalFormResultV2? Result);
public sealed record NativeTransferBatchReportV2(Guid BatchId,NativeTransferBatchItemResultV2[] Items);
