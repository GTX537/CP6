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
