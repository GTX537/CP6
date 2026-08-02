using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceDispatchApprovalStatus
{
    public const string PendingApproval = "PendingApproval";
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Stale = "Stale";
    public const string FailedNoEffect = "FailedNoEffect";
}

public sealed record SpaceDispatchTaskAssignmentCommand(
    int Rank,
    Guid OperationId,
    string TaskId,
    string TaskType,
    int TaskContractVersion,
    int TaskExecutionVersion,
    string TaskRowVersion,
    string WarehouseCode,
    string? AreaCode,
    string AssignedTo,
    string PersonExternalId);

public sealed record SpaceDispatchTaskAdapterCommand(
    Guid ApprovalRequestId,
    string WarehouseCode,
    string ChangedBy,
    DateTime OccurredAtUtc,
    IReadOnlyList<SpaceDispatchTaskAssignmentCommand> Assignments);

public sealed record SpaceDispatchTaskAdapterReceipt(
    int Rank,
    string TaskId,
    string PersonExternalId,
    Guid OperationId,
    string Outcome);

public sealed record SpaceDispatchTaskAdapterResult(
    string AdapterId,
    IReadOnlyList<SpaceDispatchTaskAdapterReceipt> Receipts);

public sealed class SpaceDispatchTaskAdapterException(
    string code,
    bool stale = false) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public bool Stale { get; } = stale;
}

public interface ISpaceDispatchTaskAdapter
{
    string AdapterId { get; }

    Task<SpaceDispatchTaskAdapterResult> StageAssignmentsAsync(
        SpaceDispatchTaskAdapterCommand command,
        CancellationToken cancellationToken = default);
}

public interface ISpaceDispatchApprovalService
{
    Task<SubmitSpaceDispatchApprovalResponse> SubmitAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        SubmitSpaceDispatchApprovalRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceDispatchApprovalRequestDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default);
}
