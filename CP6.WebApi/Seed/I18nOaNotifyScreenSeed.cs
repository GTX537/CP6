using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA Phase D-1 通知中心（N-T10）五语词条：
///   - 铃铛组件：oa.notify.title / empty / markAllRead / newArrived（NotificationBell.vue）
///   - 通知设定：oa.notify.settings.*（InboxSettings.vue Tab 3）
///
/// ⚠️ Phase B seed（I18nOaInboxScreenSeed）已含：E-WF-001~008 / oa.formto.* / oa.inst.* /
///    nav.740/733/734 / oa.inbox.* / oa.pending.* / oa.running.* / oa.done.* /
///    oa.draft.* / oa.detail.* / oa.col.* / oa.dashboard.* / oa.flowadmin.*
/// ⚠️ Phase C seed（I18nOaAdvancedScreenSeed）已含：nav.735/736/737 / oa.catalog.* /
///    oa.initiate.* / oa.settings.* / oa.transfer.*
/// ⚠️ Phase C′ seed（I18nOaDesignerScreenSeed）已含：nav.738 / oa.designer.*
/// 本 seed 中所有键均为新增（oa.notify.*），无重复。
/// </summary>
public static class I18nOaNotifyScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 铃铛 / 通知面板（NotificationBell.vue）──
        new Sys_Lang { LangKey = "oa.notify.title",       ZhCN = "通知",             ZhTW = "通知",             En = "Notifications",        Ja = "通知",                   Ko = "알림" },
        new Sys_Lang { LangKey = "oa.notify.empty",       ZhCN = "暂无通知",         ZhTW = "暫無通知",         En = "No notifications",     Ja = "通知はありません",       Ko = "알림이 없습니다" },
        new Sys_Lang { LangKey = "oa.notify.markAllRead", ZhCN = "全部已读",         ZhTW = "全部已讀",         En = "Mark all as read",     Ja = "すべて既読にする",       Ko = "모두 읽음으로 표시" },
        new Sys_Lang { LangKey = "oa.notify.newArrived",  ZhCN = "您有新的通知",     ZhTW = "您有新的通知",     En = "You have a new notification", Ja = "新しい通知があります", Ko = "새 알림이 있습니다" },

        // ── 通知设定（InboxSettings.vue Tab 3 — oa.notify.settings.*）──
        new Sys_Lang { LangKey = "oa.notify.settings.tab",        ZhCN = "通知设定",   ZhTW = "通知設定",   En = "Notification Settings", Ja = "通知設定",               Ko = "알림 설정" },
        new Sys_Lang { LangKey = "oa.notify.settings.email",      ZhCN = "邮件通知",   ZhTW = "郵件通知",   En = "Email Notifications",   Ja = "メール通知",             Ko = "이메일 알림" },
        new Sys_Lang { LangKey = "oa.notify.settings.eventTitle", ZhCN = "事件类型",   ZhTW = "事件類型",   En = "Event Types",           Ja = "イベント種別",           Ko = "이벤트 유형" },
        new Sys_Lang { LangKey = "oa.notify.settings.todo",       ZhCN = "新待办",     ZhTW = "新待辦",     En = "New Todo",               Ja = "新しい待処理",           Ko = "새 할 일" },
        new Sys_Lang { LangKey = "oa.notify.settings.approved",   ZhCN = "签核完成",   ZhTW = "簽核完成",   En = "Flow Approved",          Ja = "承認完了",               Ko = "서명 완료" },
        new Sys_Lang { LangKey = "oa.notify.settings.rejected",   ZhCN = "被驳回",     ZhTW = "被駁回",     En = "Flow Rejected",          Ja = "却下された",             Ko = "반려됨" },
        new Sys_Lang { LangKey = "oa.notify.settings.timeout",    ZhCN = "超时提醒",   ZhTW = "逾時提醒",   En = "Timeout Reminder",       Ja = "タイムアウトリマインダー", Ko = "시간 초과 알림" },
    };
}
