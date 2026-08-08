using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>
/// 复合工作流通知器（OA Phase D-1 N-T4）：持久化站内通知 + SignalR 实时 + 邮件三渠道。
/// <para>
/// 铁律：<br/>
/// ① 持久化走引擎共享 <see cref="CP6Context"/>（DI Scoped，与引擎同实例），
///    <see cref="INotificationService.CreateAsync"/> 仅 Add、不 SaveChanges，
///    随引擎 SaveChanges 一起落库（仿 Phase A 读模型钩子），<b>绝不在此处对 context 调 SaveChanges</b>。<br/>
/// ② SignalR + 邮件是 best-effort 副作用，各自独立 try/catch 吞异常，
///    任一失败绝不冒泡、绝不破坏审批流。<br/>
/// ③ 偏好按 收件人×类型×通道 独立生效（矩阵，A-T1/A-T2）：
///    每方法逐通道查 <see cref="IPrefService.IsEnabledAsync"/>——
///    inApp 关 → 跳过持久化+SignalR；email 关 → 跳过邮件；两者皆关 → 整个事件跳过。
/// </para>
/// </summary>
public class PersistentWfNotifier : IWfNotifier
{
    private readonly CP6Context _db;
    private readonly INotificationService _notif;
    private readonly IPrefService _pref;
    private readonly IEmailSender _email;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly ILogger<PersistentWfNotifier> _logger;

    public PersistentWfNotifier(
        CP6Context db,
        INotificationService notif,
        IPrefService pref,
        IEmailSender email,
        IHubContext<NotifyHub> hub,
        ILogger<PersistentWfNotifier> logger)
    {
        _db     = db;
        _notif  = notif;
        _pref   = pref;
        _email  = email;
        _hub    = hub;
        _logger = logger;
    }

    // ── TodoCreatedAsync ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey)
    {
        // 1. 逐收件人 × 逐通道查矩阵偏好（per-request 缓存在 IPrefService 内）
        var inApp = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您有新的待办";
        var body = $"您有新的待办：{flowKey}";
        if (await EnqueueAsync(assigneeId, WfNotificationType.TodoCreated, title, body,
                instanceId, taskId, flowKey, inApp, email)) return;

        if (inApp)
        {
            // 2. 持久化（仅 Add，不 SaveChanges）
            await _notif.CreateAsync(
                assigneeId, WfNotificationType.TodoCreated,
                title, body, instanceId, taskId, flowKey);

            // 3. SignalR（best-effort）
            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.TodoCreated,
                    userId     = assigneeId,
                    instanceId,
                    taskId,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(TodoCreated) 失败，忽略（用户 {UserId}）", assigneeId);
            }
        }

        // 4. 邮件（best-effort，独立通道）
        if (email)
            await TrySendEmailAsync(assigneeId, title, body);
    }

    // ── FlowApprovedAsync ───────────────────────────────────────────────

    /// <inheritdoc />
    public async Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey)
    {
        var inApp = await _pref.IsEnabledAsync(starterId, "flowApproved", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(starterId, "flowApproved", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您的申请已通过";
        var body = $"您的申请已通过：{flowKey}";
        if (await EnqueueAsync(starterId, WfNotificationType.FlowApproved, title, body,
                instanceId, null, flowKey, inApp, email)) return;

        if (inApp)
        {
            await _notif.CreateAsync(
                starterId, WfNotificationType.FlowApproved,
                title, body, instanceId, taskId: null, flowKey);

            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.FlowApproved,
                    userId     = starterId,
                    instanceId,
                    taskId     = (Guid?)null,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(FlowApproved) 失败，忽略（用户 {UserId}）", starterId);
            }
        }

        if (email)
            await TrySendEmailAsync(starterId, title, body);
    }

    // ── FlowRejectedAsync ───────────────────────────────────────────────

    /// <inheritdoc />
    public async Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment)
    {
        var inApp = await _pref.IsEnabledAsync(starterId, "flowRejected", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(starterId, "flowRejected", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您的申请被驳回";
        var body = string.IsNullOrWhiteSpace(comment)
            ? $"您的申请被驳回：{flowKey}"
            : $"您的申请被驳回：{flowKey}（{comment}）";
        if (await EnqueueAsync(starterId, WfNotificationType.FlowRejected, title, body,
                instanceId, null, flowKey, inApp, email)) return;

        if (inApp)
        {
            await _notif.CreateAsync(
                starterId, WfNotificationType.FlowRejected,
                title, body, instanceId, taskId: null, flowKey);

            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.FlowRejected,
                    userId     = starterId,
                    instanceId,
                    taskId     = (Guid?)null,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(FlowRejected) 失败，忽略（用户 {UserId}）", starterId);
            }
        }

        if (email)
            await TrySendEmailAsync(starterId, title, body);
    }

    // ── BranchPrunedAsync（内核 hardening）────────────────────────────────

    /// <inheritdoc />
    public async Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
    {
        // 偏好矩阵（A-T3）：branchPruned 独立类型键，双通道有效（NotifyMatrix.Support）。
        var inApp = await _pref.IsEnabledAsync(starterId, "branchPruned", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(starterId, "branchPruned", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您的申请有分支被驳回（其余分支继续）";
        var body = string.IsNullOrWhiteSpace(comment)
            ? $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除，其余分支继续审批"
            : $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除（{comment}），其余分支继续审批";
        if (await EnqueueAsync(starterId, WfNotificationType.BranchPruned, title, body,
                instanceId, null, flowKey, inApp, email, nodeId)) return;

        if (inApp)
        {
            await _notif.CreateAsync(
                starterId, WfNotificationType.BranchPruned,
                title, body, instanceId, taskId: null, flowKey);

            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.BranchPruned,
                    userId     = starterId,
                    instanceId,
                    taskId     = (Guid?)null,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(BranchPruned) 失败，忽略（用户 {UserId}）", starterId);
            }
        }

        if (email)
            await TrySendEmailAsync(starterId, title, body);
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<bool> EnqueueAsync(
        Guid userId, int type, string title, string body, Guid? instanceId, Guid? taskId,
        string? flowKey, bool inApp, bool email, string? suffix = null)
    {
        var eventKey = string.Join(":", type, instanceId?.ToString("N") ?? "-",
            taskId?.ToString("N") ?? "-", userId.ToString("N"), suffix ?? "-");
        await _notif.CreateOutboxAsync(userId, type, title, body, instanceId, taskId, flowKey,
            eventKey, inApp, email);
        return true; // external delivery is exclusively performed by the post-commit worker
    }

    /// <summary>
    /// 查用户邮箱并发送，全 best-effort：任何异常吞掉+记 Warning，不冒泡。
    /// </summary>
    private async Task TrySendEmailAsync(Guid userId, string subject, string body)
    {
        try
        {
            var userEmail = await _db.Sys_Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(userEmail))
                await _email.SendAsync(userEmail, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "邮件通知发送失败，忽略（用户 {UserId}）", userId);
        }
    }
}
