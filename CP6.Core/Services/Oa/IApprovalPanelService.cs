using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalPanelTask(
    Guid TaskId, string NodeId, IReadOnlyList<string> Actions);

public sealed record ApprovalPanelTimelineItem(
    int StepSeq, string NodeId, string? NodeName, string ExpectedHandlerName,
    string? ActualHandlerName, int Status, string? Comment, DateTime SentAt, DateTime? HandledAt);

public sealed record ApprovalPanelDto(
    string BizType, string BizId, string BusinessStatus, string ApprovalStatus,
    Guid? InstanceId, ApprovalPanelTask? MyTask,
    IReadOnlyList<ApprovalPanelTimelineItem> Timeline,
    bool CanSubmit, string? DetailRoute);

public sealed record BusinessApprovalAccess(string BusinessStatus, bool CanSubmit);

public interface IApprovalBusinessAccessAuthorizer
{
    string BizType { get; }
    Task<BusinessApprovalAccess> AuthorizeAsync(
        string bizId, UserPermissionContext permission, CancellationToken ct = default);
}

public interface IApprovalPanelService
{
    Task<ApprovalPanelDto> GetAsync(
        string bizType, string bizId, Guid actualUserId, Guid effectiveUserId,
        UserPermissionContext permission, CancellationToken ct = default);
}
