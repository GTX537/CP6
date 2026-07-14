using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// WFS 三期波⑤「引擎基建六件套」画面词条 + 错误码五语种子（Sys_Lang，ZhCN/ZhTW/En/Ja/Ko）。
///
/// 键面以 cp6.web 实际 <c>t()</c> 消费为权威（波③/④口径），各任务只用键名回退键文本，i18n 落库统一归 F-T1：
///  - 年历（A-T4）：<c>oa.workcal.*</c>（views/oa/admin/WorkCalendar.vue，含动态键 <c>oa.workcal.kind.{makeup|closed|weekend|normal}</c>
///    + 含 <c>{n}</c> 具名插值键 <c>oa.workcal.imported</c>）+ 菜单导航键 <c>nav.743</c>（侧栏 MenuTreeItem.vue 走 nav.&lt;MenuId&gt;）。
///  - 连接器（D-T2）：<c>oa.connector.*</c>（views/oa/admin/WfConnectorPanel.vue/WfConnectorDialog.vue + FlowAdmin.vue 的 tab 标题）。
///  - 设计器新键：<c>oa.designer.svc.httpMethod/.httpMethodHint/.timeoutSec/.delayMode.workdays</c>（E-T1/A-T3）、
///    <c>oa.designer.timeout.errorEdge</c>（B-T2 到点动作标签）、
///    校验文案 <c>oa.designer.errHttpOverride</c>（E-T1）/<c>oa.designer.errErrorEdgeSource</c>/<c>oa.designer.errTimeoutErrorEdge</c>（B-T2）。
///  - 后端错误码：<c>E-WF-027</c>（超时走失败边节点缺失败边）/<c>E-WF-028</c>（超时配置或时区非法）。
///
/// 去重：全部键在既有 I18nOa*/I18nTenant*/I18nCn* 等 seed 中均无重复（已全库 grep 核实，SeedLangs insert-only 安全）。
/// <c>common.*</c>（edit/cancel/save）与 <c>取消/确定</c> 系跨模块既有全局键，本文件不重复放；
/// <c>platform.tenant.timeZone*</c>（E-T2）按平台域惯例落 <see cref="I18nTenantComplianceSeed"/>（platform.* 家族所在），不在本文件。
///
/// 接入：Program.cs i18n concat 链尾（<c>I18nOaInboxUxScreenSeed</c> 之后）追加 <c>.Concat(...Items)</c>。
/// </summary>
public static class I18nOaEngineInfraScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 年历管理页 oa.workcal.*（WorkCalendar.vue / workCalendarModel.ts）──
        new() { LangKey = "oa.workcal.title",          ZhCN = "工作日历",           ZhTW = "工作日曆",           En = "Work Calendar",  Ja = "稼働カレンダー", Ko = "근무 달력" },
        new() { LangKey = "oa.workcal.empty",          ZhCN = "本租户尚未维护假日日历", ZhTW = "本租戶尚未維護假日日曆", En = "No holiday calendar configured for this tenant", Ja = "このテナントの休日カレンダーは未設定です", Ko = "이 테넌트의 휴일 달력이 아직 없습니다" },
        new() { LangKey = "oa.workcal.importJp",       ZhCN = "导入日本法定假日",   ZhTW = "匯入日本法定假日",   En = "Import Japanese public holidays", Ja = "日本の祝日を取り込む", Ko = "일본 공휴일 가져오기" },
        // {n} = 已导入条数（t('oa.workcal.imported', { n }) 具名插值）
        new() { LangKey = "oa.workcal.imported",       ZhCN = "已导入 {n} 个假日",  ZhTW = "已匯入 {n} 個假日",  En = "Imported {n} holidays", Ja = "{n} 件の祝日を取り込みました", Ko = "공휴일 {n}건을 가져왔습니다" },
        // 图例
        new() { LangKey = "oa.workcal.legend.makeup",  ZhCN = "补班",               ZhTW = "補班",               En = "Make-up workday", Ja = "振替出勤", Ko = "대체 근무" },
        new() { LangKey = "oa.workcal.legend.closed",  ZhCN = "假日",               ZhTW = "假日",               En = "Holiday",        Ja = "休日",     Ko = "휴일" },
        new() { LangKey = "oa.workcal.legend.weekend", ZhCN = "周末休",             ZhTW = "週末休",             En = "Weekend off",    Ja = "週末休み", Ko = "주말 휴무" },
        // 日格态（动态键 oa.workcal.kind.{kind}；kind ∈ makeup/closed/weekend/normal，见 workCalendarModel.DayKind）
        new() { LangKey = "oa.workcal.kind.makeup",    ZhCN = "补班",               ZhTW = "補班",               En = "Make-up workday", Ja = "振替出勤", Ko = "대체 근무" },
        new() { LangKey = "oa.workcal.kind.closed",    ZhCN = "假日",               ZhTW = "假日",               En = "Holiday",        Ja = "休日",     Ko = "휴일" },
        new() { LangKey = "oa.workcal.kind.weekend",   ZhCN = "周末休",             ZhTW = "週末休",             En = "Weekend off",    Ja = "週末休み", Ko = "주말 휴무" },
        new() { LangKey = "oa.workcal.kind.normal",    ZhCN = "工作日",             ZhTW = "工作日",             En = "Workday",        Ja = "稼働日",   Ko = "근무일" },
        // 反转对话框
        new() { LangKey = "oa.workcal.dialog.title",   ZhCN = "设置日期",           ZhTW = "設定日期",           En = "Set day",        Ja = "日付を設定", Ko = "날짜 설정" },
        new() { LangKey = "oa.workcal.dialog.note",    ZhCN = "备注（可选）",       ZhTW = "備註（可選）",       En = "Note (optional)", Ja = "備考（任意）", Ko = "비고(선택)" },
        // 侧栏菜单导航键（MenuTreeItem.vue：te('nav.'+id) ? t('nav.'+id) : menuName；对应新菜单 743）
        new() { LangKey = "nav.743",                   ZhCN = "工作日历",           ZhTW = "工作日曆",           En = "Work Calendar",  Ja = "稼働カレンダー", Ko = "근무 달력" },

        // ── 连接器管理 tab oa.connector.*（WfConnectorPanel.vue / WfConnectorDialog.vue / FlowAdmin.vue）──
        new() { LangKey = "oa.connector.tab",          ZhCN = "连接器",             ZhTW = "連接器",             En = "Connectors",     Ja = "コネクタ", Ko = "커넥터" },
        new() { LangKey = "oa.connector.new",          ZhCN = "新建连接器",         ZhTW = "新增連接器",         En = "New connector",  Ja = "コネクタを追加", Ko = "커넥터 추가" },
        new() { LangKey = "oa.connector.empty",        ZhCN = "暂无连接器",         ZhTW = "暫無連接器",         En = "No connectors",  Ja = "コネクタがありません", Ko = "커넥터가 없습니다" },
        new() { LangKey = "oa.connector.authYes",      ZhCN = "已配置",             ZhTW = "已配置",             En = "Configured",     Ja = "設定済み", Ko = "설정됨" },
        new() { LangKey = "oa.connector.authNo",       ZhCN = "无",                 ZhTW = "無",                 En = "None",           Ja = "なし",     Ko = "없음" },
        // 列头
        new() { LangKey = "oa.connector.col.name",        ZhCN = "名称",            ZhTW = "名稱",            En = "Name",         Ja = "名前",       Ko = "이름" },
        new() { LangKey = "oa.connector.col.displayName", ZhCN = "显示名",          ZhTW = "顯示名",          En = "Display Name", Ja = "表示名",     Ko = "표시 이름" },
        new() { LangKey = "oa.connector.col.baseUrl",     ZhCN = "基础 URL",        ZhTW = "基礎 URL",        En = "Base URL",     Ja = "ベースURL",  Ko = "기본 URL" },
        new() { LangKey = "oa.connector.col.timeout",     ZhCN = "超时(秒)",        ZhTW = "逾時(秒)",        En = "Timeout (s)",  Ja = "タイムアウト(秒)", Ko = "시간 초과(초)" },
        new() { LangKey = "oa.connector.col.auth",        ZhCN = "凭证",            ZhTW = "憑證",            En = "Credentials",  Ja = "認証情報",   Ko = "자격 증명" },
        new() { LangKey = "oa.connector.col.enabled",     ZhCN = "启用",            ZhTW = "啟用",            En = "Enabled",      Ja = "有効",       Ko = "사용" },
        new() { LangKey = "oa.connector.col.actions",     ZhCN = "操作",            ZhTW = "操作",            En = "Actions",      Ja = "操作",       Ko = "작업" },
        // 表单
        new() { LangKey = "oa.connector.form.name",           ZhCN = "名称",        ZhTW = "名稱",        En = "Name",         Ja = "名前",     Ko = "이름" },
        new() { LangKey = "oa.connector.form.nameHint",       ZhCN = "唯一标识，创建后不可改", ZhTW = "唯一識別，建立後不可改", En = "Unique identifier; immutable after creation", Ja = "一意の識別子（作成後は変更不可）", Ko = "고유 식별자, 생성 후 변경 불가" },
        new() { LangKey = "oa.connector.form.displayName",    ZhCN = "显示名",      ZhTW = "顯示名",      En = "Display Name", Ja = "表示名",   Ko = "표시 이름" },
        new() { LangKey = "oa.connector.form.baseUrl",        ZhCN = "基础 URL",    ZhTW = "基礎 URL",    En = "Base URL",     Ja = "ベースURL", Ko = "기본 URL" },
        new() { LangKey = "oa.connector.form.auth",           ZhCN = "凭证 (JSON)", ZhTW = "憑證 (JSON)", En = "Credentials (JSON)", Ja = "認証情報 (JSON)", Ko = "자격 증명 (JSON)" },
        new() { LangKey = "oa.connector.form.authHint",       ZhCN = "加密存储，读取时不回显", ZhTW = "加密儲存，讀取時不回顯", En = "Stored encrypted; never echoed back", Ja = "暗号化して保存し、参照時は表示しません", Ko = "암호화하여 저장하며 조회 시 표시하지 않습니다" },
        new() { LangKey = "oa.connector.form.authConfigured", ZhCN = "已配置（不回显）", ZhTW = "已配置（不回顯）", En = "Configured (hidden)", Ja = "設定済み（非表示）", Ko = "설정됨(숨김)" },
        new() { LangKey = "oa.connector.form.authPlaceholder",ZhCN = "认证 JSON，如 bearer / basic", ZhTW = "認證 JSON，如 bearer / basic", En = "Auth JSON, e.g. bearer / basic", Ja = "認証JSON（例: bearer / basic）", Ko = "인증 JSON, 예: bearer / basic" },
        new() { LangKey = "oa.connector.form.timeout",        ZhCN = "超时(秒)",    ZhTW = "逾時(秒)",    En = "Timeout (s)",  Ja = "タイムアウト(秒)", Ko = "시간 초과(초)" },
        new() { LangKey = "oa.connector.form.required",       ZhCN = "名称与基础 URL 必填", ZhTW = "名稱與基礎 URL 必填", En = "Name and Base URL are required", Ja = "名前とベースURLは必須です", Ko = "이름과 기본 URL은 필수입니다" },

        // ── 设计器新键（E-T1 节点 HTTP 覆盖 / A-T3 顺延工作日 / B-T2 超时走失败边）──
        new() { LangKey = "oa.designer.svc.httpMethod",        ZhCN = "HTTP 方法（覆盖）", ZhTW = "HTTP 方法（覆蓋）", En = "HTTP Method (override)", Ja = "HTTPメソッド（上書き）", Ko = "HTTP 메서드(재정의)" },
        new() { LangKey = "oa.designer.svc.httpMethodHint",    ZhCN = "留空＝用连接器默认", ZhTW = "留空＝用連接器預設", En = "Leave empty to use connector default", Ja = "空欄＝コネクタの既定を使用", Ko = "비우면 커넥터 기본값 사용" },
        new() { LangKey = "oa.designer.svc.timeoutSec",        ZhCN = "超时（秒，覆盖）", ZhTW = "逾時（秒，覆蓋）", En = "Timeout (sec, override)", Ja = "タイムアウト（秒・上書き）", Ko = "시간 초과(초, 재정의)" },
        new() { LangKey = "oa.designer.svc.delayMode.workdays",ZhCN = "顺延工作日",   ZhTW = "順延工作日",   En = "Delay by workdays", Ja = "営業日で順延", Ko = "근무일로 연기" },
        new() { LangKey = "oa.designer.timeout.errorEdge",     ZhCN = "超时走失败边", ZhTW = "超時走失敗邊", En = "On timeout, take error edge", Ja = "タイムアウト時は失敗エッジへ", Ko = "시간 초과 시 실패 분기로" },
        new() { LangKey = "oa.designer.errHttpOverride",       ZhCN = "HTTP 方法或超时值非法", ZhTW = "HTTP 方法或逾時值非法", En = "Invalid HTTP method or timeout value", Ja = "HTTPメソッドまたはタイムアウト値が不正です", Ko = "HTTP 메서드 또는 시간 초과 값이 잘못되었습니다" },
        new() { LangKey = "oa.designer.errErrorEdgeSource",    ZhCN = "失败边来源节点类型非法", ZhTW = "失敗邊來源節點類型非法", En = "Invalid error-edge source node type", Ja = "失敗エッジの起点ノード種別が不正です", Ko = "실패 분기 시작 노드 유형이 잘못되었습니다" },
        new() { LangKey = "oa.designer.errTimeoutErrorEdge",   ZhCN = "超时走失败边的节点缺少失败边", ZhTW = "超時走失敗邊的節點缺少失敗邊", En = "Node with error-edge timeout lacks an error edge", Ja = "失敗エッジタイムアウトのノードに失敗エッジがありません", Ko = "실패 분기 타임아웃 노드에 실패 분기가 없습니다" },

        // ── 后端错误码（brief 给定文本照用）──
        new() { LangKey = "E-WF-027", ZhCN = "超时走失败边的节点缺少失败边", ZhTW = "超時走失敗邊的節點缺少失敗邊", En = "Node with errorEdge timeout action lacks an error edge", Ja = "エラー辺タイムアウトのノードに失敗辺がありません", Ko = "실패 경로 타임아웃 노드에 실패 경로가 없습니다" },
        new() { LangKey = "E-WF-028", ZhCN = "超时配置或时区非法", ZhTW = "超時配置或時區非法", En = "Invalid timeout or timezone", Ja = "タイムアウトまたはタイムゾーンが無効です", Ko = "타임아웃 또는 시간대가 잘못되었습니다" },
    };
}
