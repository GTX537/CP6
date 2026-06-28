namespace CP6.Core.Services.Oa;

/// <summary>
/// 站内通知列表项 DTO（OA Phase D-1 N-T2）。
/// 对应 Wf_Notification 实体，返回给控制器 / 前端铃铛。
/// </summary>
public record NotificationItem(
    Guid       Id,
    int        Type,
    string     Title,
    string     Body,
    Guid?      InstanceId,
    Guid?      TaskId,
    string?    FlowKey,
    bool       IsRead,
    DateTime   CreateDate);

/// <summary>
/// 用户通知偏好（N-T3）。
/// 从 Wf_InboxPref.PrefsJson 的 <c>notify</c> 子对象解析；缺省全 true（开启）。
/// </summary>
/// <param name="Todo">待办产生时推送。</param>
/// <param name="Approved">流程整体通过时推送发起人。</param>
/// <param name="Rejected">流程被驳回时推送发起人。</param>
/// <param name="Timeout">超时提醒推送。</param>
/// <param name="Email">同时发邮件（叠加到站内通知上，由 PersistentWfNotifier 判断）。</param>
public record NotificationPrefs(
    bool Todo,
    bool Approved,
    bool Rejected,
    bool Timeout,
    bool Email)
{
    /// <summary>全部开启的默认偏好（无偏好行 / 无 notify 键时使用）。</summary>
    public static readonly NotificationPrefs Default = new(true, true, true, true, true);
}
