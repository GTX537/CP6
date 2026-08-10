using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Services;

/// <summary>
/// Persists workflow notification intents in the transactional outbox.
/// External delivery is performed only after commit by
/// <see cref="BackgroundServices.WfNotificationDispatchWorker"/>.
/// </summary>
public sealed class PersistentWfNotifier : IWfNotifier
{
    private readonly INotificationService _notifications;
    private readonly IPrefService _preferences;

    public PersistentWfNotifier(
        INotificationService notifications,
        IPrefService preferences)
    {
        _notifications = notifications;
        _preferences = preferences;
    }

    /// <inheritdoc />
    public async Task TodoCreatedAsync(
        Guid assigneeId,
        Guid instanceId,
        Guid taskId,
        string flowKey)
    {
        var inApp = await _preferences.IsEnabledAsync(
            assigneeId, "todoCreated", NotifyMatrix.ChannelInApp);
        var email = await _preferences.IsEnabledAsync(
            assigneeId, "todoCreated", NotifyMatrix.ChannelEmail);
        if (!inApp && !email)
            return;

        await EnqueueAsync(
            assigneeId,
            WfNotificationType.TodoCreated,
            "您有新的待办",
            $"您有新的待办：{flowKey}",
            instanceId,
            taskId,
            flowKey,
            inApp,
            email);
    }

    /// <inheritdoc />
    public async Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey)
    {
        var inApp = await _preferences.IsEnabledAsync(
            starterId, "flowApproved", NotifyMatrix.ChannelInApp);
        var email = await _preferences.IsEnabledAsync(
            starterId, "flowApproved", NotifyMatrix.ChannelEmail);
        if (!inApp && !email)
            return;

        await EnqueueAsync(
            starterId,
            WfNotificationType.FlowApproved,
            "您的申请已通过",
            $"您的申请已通过：{flowKey}",
            instanceId,
            taskId: null,
            flowKey,
            inApp,
            email);
    }

    /// <inheritdoc />
    public async Task FlowRejectedAsync(
        Guid starterId,
        Guid instanceId,
        string flowKey,
        string? comment)
    {
        var inApp = await _preferences.IsEnabledAsync(
            starterId, "flowRejected", NotifyMatrix.ChannelInApp);
        var email = await _preferences.IsEnabledAsync(
            starterId, "flowRejected", NotifyMatrix.ChannelEmail);
        if (!inApp && !email)
            return;

        var body = string.IsNullOrWhiteSpace(comment)
            ? $"您的申请被驳回：{flowKey}"
            : $"您的申请被驳回：{flowKey}（{comment}）";
        await EnqueueAsync(
            starterId,
            WfNotificationType.FlowRejected,
            "您的申请被驳回",
            body,
            instanceId,
            taskId: null,
            flowKey,
            inApp,
            email);
    }

    /// <inheritdoc />
    public async Task BranchPrunedAsync(
        Guid starterId,
        Guid instanceId,
        string flowKey,
        string nodeId,
        string? comment)
    {
        var inApp = await _preferences.IsEnabledAsync(
            starterId, "branchPruned", NotifyMatrix.ChannelInApp);
        var email = await _preferences.IsEnabledAsync(
            starterId, "branchPruned", NotifyMatrix.ChannelEmail);
        if (!inApp && !email)
            return;

        var body = string.IsNullOrWhiteSpace(comment)
            ? $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除，其余分支继续审批"
            : $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除（{comment}），其余分支继续审批";
        await EnqueueAsync(
            starterId,
            WfNotificationType.BranchPruned,
            "您的申请有分支被驳回（其余分支继续）",
            body,
            instanceId,
            taskId: null,
            flowKey,
            inApp,
            email,
            nodeId);
    }

    private Task EnqueueAsync(
        Guid userId,
        int type,
        string title,
        string body,
        Guid? instanceId,
        Guid? taskId,
        string? flowKey,
        bool inApp,
        bool email,
        string? suffix = null)
    {
        var eventKey = string.Join(
            ":",
            type,
            instanceId?.ToString("N") ?? "-",
            taskId?.ToString("N") ?? "-",
            userId.ToString("N"),
            suffix ?? "-");

        return _notifications.CreateOutboxAsync(
            userId,
            type,
            title,
            body,
            instanceId,
            taskId,
            flowKey,
            eventKey,
            inApp,
            email);
    }
}
