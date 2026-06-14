namespace CP6.Core.Services.Wf;

/// <summary>待办中心（OA 章04）。我的待办 / 我的申请 / 撤回。</summary>
public interface ITaskCenterService
{
    /// <summary>我的待办：当前用户名下待办(Pending) + 实例进行中。</summary>
    Task<List<TodoItem>> MyTodosAsync(Guid userId);

    /// <summary>我的申请：当前用户发起的全部实例（含状态/当前节点）。</summary>
    Task<List<MyApplicationItem>> MyApplicationsAsync(Guid userId);

    /// <summary>撤回：仅发起人本人 + 实例进行中可撤；置 Withdrawn + 清在途任务 + 记痕迹。否则抛 InvalidOperationException。</summary>
    Task WithdrawAsync(Guid instanceId, Guid userId);
}

/// <summary>待办项。</summary>
public record TodoItem(Guid TaskId, Guid InstanceId, string FlowKey, string NodeId, Guid StarterId, DateTime CreateDate);

/// <summary>我的申请项。</summary>
public record MyApplicationItem(Guid InstanceId, string FlowKey, string CurrentNode, int Status, DateTime CreateDate);
