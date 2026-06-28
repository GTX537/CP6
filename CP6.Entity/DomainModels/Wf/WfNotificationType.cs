namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// Wf_Notification.Type 常量（OA Phase D-1）。
/// 整型存库，接口/前端按 type 值映射 i18n 文案。
/// </summary>
public static class WfNotificationType
{
    /// <summary>新待办产生 → 推送给处理人。</summary>
    public const int TodoCreated = 1;

    /// <summary>流程签核完成（整体通过）→ 推送给发起人。</summary>
    public const int FlowApproved = 2;

    /// <summary>流程被驳回 → 推送给发起人。</summary>
    public const int FlowRejected = 3;

    /// <summary>超时提醒 → 推送给处理人/上级。</summary>
    public const int Timeout = 4;
}
