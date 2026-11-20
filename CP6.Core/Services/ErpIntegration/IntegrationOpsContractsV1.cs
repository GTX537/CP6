using System.Text;
using System.Data.Common;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationQueueRefV1(string OwnerId, string NativeType, string NativeId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationDigestV1(string Domain, string CodecVersion, string Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationReplayRequestV1(Guid ReplayOperationId, string AllowedAction, IntegrationQueueRefV1 NativeQueueRef,
    string ExpectedNativeVersion, IntegrationDigestV1 ExpectedDigest, string ReasonCode, string EligibilityRef,
    string InputContractVersion);

public sealed record IntegrationRequestRefV1(Guid RequestId, string RequestVersion);

public sealed record IntegrationReplayReceiptV1(Guid ReplayOperationId, string AllowedAction, IntegrationQueueRefV1? NativeQueueRef,
    IntegrationDigestV1? ExpectedDigest, IntegrationRequestRefV1? OriginalRequestRef, string? OriginalMessageRef,
    DateTimeOffset AcceptedAtUtc, Guid? AcceptedBy, string? ScheduledAttemptRef, string? NativeVersionBefore,
    string? NativeVersionAfter, string Status, string OwnerOutcome, string? SafeCode, Guid AuditRef, string ContractVersion,
    string? RouteEpoch = null);

public sealed record IntegrationFailureV1(string FailureRef, string OwnerId, string NativeType, string? NativeId,
    string RouteEpoch, string NativeStatus, string? NativeVersion, int TimePartition, DateTimeOffset? OccurredAtUtc,
    string SafeCode, int AttemptCount, string DeliveryState, string OwnerOutcome, string EvidenceState,
    string? OriginalMessageRef = null, IntegrationRequestRefV1? OriginalRequestRef = null, string? EnvelopeRef = null,
    string? ResultVersion = null, IntegrationReplayEvidenceV1? ReplayEvidence = null);

public sealed record IntegrationReplayEvidenceV1(string AllowedAction, IntegrationQueueRefV1 NativeQueueRef,
    IntegrationDigestV1 ExpectedDigest);

public sealed record IntegrationFailurePageV1(IntegrationFailureV1[] Items, bool Partial, string[] UnavailableOwners,
    string[] SourceSet, DateTimeOffset SnapshotAt, string? Cursor);

public sealed record IntegrationEligibilityV1(string Status, string? SafeCode, string? EligibilityRef,
    DateTimeOffset ExpiresAtUtc, string RouteEpoch, IntegrationReplayEvidenceV1? ReplayEvidence, string? NativeVersion,
    Guid? ReplayOperationId = null);

public sealed class IntegrationOpsExceptionV1(string code, int status) : InvalidOperationException(code)
{
    public int Status { get; } = status;
}

public static class IntegrationOpsContractV1
{
    public const string Owner = "Main.ERP.C03", Contract = "ReplayRequest.v1", Receipt = "ReplayReceipt.v1";
    public const string Inbox = "ErpCommandInbox", Outbox = "ErpResultOutbox", Bridge = "ErpOrderBridgeDispatch";
    public const string Replay = "REPLAY_ORIGINAL_REQUEST", Deliver = "REDELIVER_ORIGINAL_RESULT", Resume = "RESUME_POSTCOMMIT_BRIDGE";
    private const string RawDomain = "cp6.cloud-event.raw-envelope.sha256", RawCodec = "raw-utf8-v1";
    private const string BridgeDomain = "cp6.erp.postcommit-bridge.identity.sha256", BridgeCodec = "c03-bridge-key-v1";

    public static string? Action(string? type) => type switch { Inbox => Replay, Outbox => Deliver, Bridge => Resume, _ => null };
    public static IntegrationDigestV1 Digest(string type, string hash) => type == Bridge
        ? new(BridgeDomain, BridgeCodec, hash) : new(RawDomain, RawCodec, hash);

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static IntegrationOpsExceptionV1 Error(string code, int status = 409) => new(code, status);
    internal static bool IsUnavailable(Exception error) => error is DbException or DbUpdateException or TimeoutException ||
        error is InvalidOperationException { InnerException: DbException or TimeoutException };
}

public sealed record IntegrationFailureKeyV1(int TimePartition, DateTimeOffset? OccurredAtUtc, string OwnerId,
    string NativeType, string NativeId, string FailureKey) : IComparable<IntegrationFailureKeyV1>
{
    public int CompareTo(IntegrationFailureKeyV1? other)
    {
        if (other is null) return 1;
        var compare = TimePartition.CompareTo(other.TimePartition);
        if (compare == 0) compare = Nullable.Compare(OccurredAtUtc, other.OccurredAtUtc);
        if (compare == 0) compare = CompareText(OwnerId, other.OwnerId);
        if (compare == 0) compare = CompareText(NativeType, other.NativeType);
        if (compare == 0) compare = CompareText(NativeId, other.NativeId);
        return compare != 0 ? compare : CompareText(FailureKey, other.FailureKey);
    }

    internal static int CompareText(string first, string second) =>
        Encoding.UTF8.GetBytes(first).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(second));
}

public sealed record IntegrationFailureRecordV1(IntegrationFailureKeyV1 Key, string NativeStatus, string? NativeVersion,
    string? SafeCode, int AttemptCount);

public sealed record IntegrationFailureWindowV1(DateTimeOffset FromUtc, DateTimeOffset UntilUtc, DateTimeOffset SnapshotAt);

// 每个来源只提供自己当前可读的行，按同一键序返回有界切片。null 明确表示当前不可用。
public interface IIntegrationOpsSourceV1
{
    string OwnerId { get; }
    string RouteEpoch { get; }
    Task<bool> CanReadAsync(Guid tenant, Guid actor, CancellationToken ct);
    Task<IReadOnlyList<IntegrationFailureRecordV1>?> ReadAsync(Guid tenant, Guid actor, IntegrationFailureWindowV1 window,
        IntegrationFailureKeyV1? after, int take, CancellationToken ct);
}

public sealed record IntegrationEffectIntentV1(Guid TenantId, Guid ActorId, Guid ReplayOperationId, string RouteEpoch,
    string AllowedAction, IntegrationQueueRefV1 NativeQueueRef, IntegrationRequestRefV1 OriginalRequestRef,
    IntegrationDigestV1 OriginalDigest, string NativeVersion);

public sealed record IntegrationEffectHoldV1(Guid TransactionId, string RouteEpoch, string FenceRef,
    string SourceProtectionRef, string QualificationRef);

// 业务 Owner 在同一物理事务内完成原源确认、当前资格与安全门。无已采用实现时保持阻塞。
public interface IErpIntegrationEffectOwnerV1
{
    Task<IntegrationEffectHoldV1?> EvaluateAndHoldAsync(CP6Context db, IntegrationEffectIntentV1 intent,
        bool executing, CancellationToken ct);

    // 只允许在调用者的事务内 staged 真实未完成步骤；外部效果另由该 Owner 负责。
    Task<bool> StageBridgeAsync(CP6Context db, IntegrationEffectIntentV1 intent, CancellationToken ct);
}
