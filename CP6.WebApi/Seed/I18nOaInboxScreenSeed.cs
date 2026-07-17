using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA 电子表单信箱（Phase B）五语词条：
///   - 画面词条：oa.inbox.* / oa.pending.* / oa.running.* / oa.done.* / oa.draft.*
///               oa.detail.* / oa.col.* / oa.dashboard.* / oa.flowadmin.*
///   - 状态标签：oa.formto.{7态} + oa.inst.{6态}（inboxModel 助手发出）
///   - 错误码：E-WF-001 ~ E-WF-008（BizException 码即 key，与 I18nSecScreenSeed 同体例）
///   - 菜单导航：nav.733（电子表单信箱）/ nav.734（流程管理）/ nav.740（OA工作流 父组）
/// </summary>
public static class I18nOaInboxScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 错误码 E-WF-001~E-WF-008 ──
        new Sys_Lang { LangKey = "E-WF-001", ZhCN = "工作流操作失败", ZhTW = "工作流操作失敗", En = "Workflow operation failed", Ja = "ワークフロー操作に失敗しました", Ko = "워크플로우 작업에 실패했습니다" },
        new Sys_Lang { LangKey = "E-WF-002", ZhCN = "无权限执行此操作", ZhTW = "無權限執行此操作", En = "You do not have permission for this action", Ja = "この操作の権限がありません", Ko = "이 작업에 대한 권한이 없습니다" },
        new Sys_Lang { LangKey = "E-WF-003", ZhCN = "草稿越权：该草稿不属于当前用户", ZhTW = "草稿越權：該草稿不屬於目前使用者", En = "Unauthorized access to draft: draft does not belong to current user", Ja = "草稿への不正アクセス：草稿は現在のユーザーのものではありません", Ko = "초안 무단 접근: 초안이 현재 사용자의 것이 아닙니다" },
        new Sys_Lang { LangKey = "E-WF-004", ZhCN = "批量操作包含无效任务", ZhTW = "批次操作包含無效任務", En = "Batch operation contains invalid tasks", Ja = "一括操作に無効なタスクが含まれています", Ko = "일괄 작업에 유효하지 않은 작업이 포함되어 있습니다" },
        new Sys_Lang { LangKey = "E-WF-005", ZhCN = "流程操作条件未满足", ZhTW = "流程操作條件未滿足", En = "Workflow condition not satisfied", Ja = "ワークフローの条件が満たされていません", Ko = "워크플로우 조건이 충족되지 않았습니다" },
        new Sys_Lang { LangKey = "E-WF-006", ZhCN = "流程不存在或已停用", ZhTW = "流程不存在或已停用", En = "Flow does not exist or is disabled", Ja = "フローが存在しないか無効化されています", Ko = "플로우가 존재하지 않거나 비활성화되어 있습니다" },
        new Sys_Lang { LangKey = "E-WF-007", ZhCN = "实例不存在", ZhTW = "實例不存在", En = "Instance does not exist", Ja = "インスタンスが存在しません", Ko = "인스턴스가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "E-WF-008", ZhCN = "该表单已绑定其他启用流程", ZhTW = "該表單已綁定其他啟用流程", En = "This form is already bound to another active flow", Ja = "このフォームは別の有効なフローに紐付けられています", Ko = "이 양식은 이미 다른 활성 플로우에 바인딩되어 있습니다" },
        new Sys_Lang { LangKey = "E-WF-029", ZhCN = "非本人待办，无权办理", ZhTW = "非本人待辦，無權辦理", En = "You are not the assignee of this task", Ja = "本人の未処理タスクではないため操作できません", Ko = "본인의 대기 작업이 아니므로 처리할 수 없습니다" },

        // ── oa.formto.* 传签状态（七态）──
        new Sys_Lang { LangKey = "oa.formto.pending",     ZhCN = "未處理",   ZhTW = "未處理",   En = "Pending",     Ja = "未処理",       Ko = "미처리" },
        new Sys_Lang { LangKey = "oa.formto.approved",    ZhCN = "已同意",   ZhTW = "已同意",   En = "Approved",    Ja = "承認済",       Ko = "승인됨" },
        new Sys_Lang { LangKey = "oa.formto.rejected",    ZhCN = "已拒绝",   ZhTW = "已拒絕",   En = "Rejected",    Ja = "却下済",       Ko = "반려됨" },
        new Sys_Lang { LangKey = "oa.formto.transferred", ZhCN = "已转签",   ZhTW = "已轉簽",   En = "Transferred", Ja = "転送済",       Ko = "이관됨" },
        new Sys_Lang { LangKey = "oa.formto.addsigned",   ZhCN = "已加签",   ZhTW = "已加簽",   En = "Add-signed",  Ja = "追加署名済",   Ko = "추가서명됨" },
        new Sys_Lang { LangKey = "oa.formto.skipped",     ZhCN = "已略过",   ZhTW = "已略過",   En = "Skipped",     Ja = "スキップ済",   Ko = "건너뜀" },
        new Sys_Lang { LangKey = "oa.formto.voided",      ZhCN = "已作废",   ZhTW = "已作廢",   En = "Voided",      Ja = "無効化済",     Ko = "무효화됨" },

        // ── oa.inst.* 实例状态（六态）──
        new Sys_Lang { LangKey = "oa.inst.running",    ZhCN = "在途",   ZhTW = "在途",   En = "Running",    Ja = "進行中",     Ko = "진행중" },
        new Sys_Lang { LangKey = "oa.inst.approved",   ZhCN = "已通过", ZhTW = "已通過", En = "Approved",   Ja = "承認済",     Ko = "승인됨" },
        new Sys_Lang { LangKey = "oa.inst.rejected",   ZhCN = "已驳回", ZhTW = "已駁回", En = "Rejected",   Ja = "却下済",     Ko = "반려됨" },
        new Sys_Lang { LangKey = "oa.inst.withdrawn",  ZhCN = "已撤回", ZhTW = "已撤回", En = "Withdrawn",  Ja = "取り下げ済", Ko = "철회됨" },
        new Sys_Lang { LangKey = "oa.inst.suspended",  ZhCN = "已挂起", ZhTW = "已掛起", En = "Suspended",  Ja = "一時停止中", Ko = "보류됨" },
        new Sys_Lang { LangKey = "oa.inst.draft",      ZhCN = "草稿",   ZhTW = "草稿",   En = "Draft",      Ja = "下書き",     Ko = "임시저장" },

        // ── 菜单导航标签 ──
        new Sys_Lang { LangKey = "nav.740", ZhCN = "OA工作流",    ZhTW = "OA工作流",    En = "OA Workflow",       Ja = "OAワークフロー",    Ko = "OA 워크플로우" },
        new Sys_Lang { LangKey = "nav.733", ZhCN = "电子表单信箱", ZhTW = "電子表單信箱", En = "e-Form Inbox",      Ja = "電子フォーム受信箱", Ko = "전자양식 수신함" },
        new Sys_Lang { LangKey = "nav.734", ZhCN = "流程管理",    ZhTW = "流程管理",    En = "Flow Management",   Ja = "フロー管理",        Ko = "플로우 관리" },

        // ── oa.inbox.* 信箱主容器 ──
        new Sys_Lang { LangKey = "oa.inbox.title",       ZhCN = "电子表单信箱", ZhTW = "電子表單信箱", En = "e-Form Inbox",       Ja = "電子フォーム受信箱", Ko = "전자양식 수신함" },
        new Sys_Lang { LangKey = "oa.inbox.pending",     ZhCN = "未处理",       ZhTW = "未處理",       En = "Pending",            Ja = "未処理",             Ko = "미처리" },
        new Sys_Lang { LangKey = "oa.inbox.running",     ZhCN = "在途",         ZhTW = "在途",         En = "In Progress",        Ja = "進行中",             Ko = "진행중" },
        new Sys_Lang { LangKey = "oa.inbox.done",        ZhCN = "已处理",       ZhTW = "已處理",       En = "Done",               Ja = "処理済",             Ko = "처리됨" },
        new Sys_Lang { LangKey = "oa.inbox.draft",       ZhCN = "草稿",         ZhTW = "草稿",         En = "Drafts",             Ja = "下書き",             Ko = "임시저장" },
        new Sys_Lang { LangKey = "oa.inbox.dashboard",   ZhCN = "统计",         ZhTW = "統計",         En = "Dashboard",          Ja = "統計",               Ko = "통계" },
        new Sys_Lang { LangKey = "oa.inbox.flowAdmin",   ZhCN = "流程管理",     ZhTW = "流程管理",     En = "Flow Admin",         Ja = "フロー管理",         Ko = "플로우 관리" },
        new Sys_Lang { LangKey = "oa.inbox.newBtn",      ZhCN = "新建表单",     ZhTW = "新建表單",     En = "New Form",           Ja = "新規フォーム",       Ko = "새 양식" },
        new Sys_Lang { LangKey = "oa.inbox.detailTitle", ZhCN = "表单详情",     ZhTW = "表單詳情",     En = "Form Detail",        Ja = "フォーム詳細",       Ko = "양식 상세" },
        new Sys_Lang { LangKey = "oa.inbox.close",       ZhCN = "关闭",         ZhTW = "關閉",         En = "Close",              Ja = "閉じる",             Ko = "닫기" },

        // ── oa.pending.* 待处理面板 ──
        new Sys_Lang { LangKey = "oa.pending.empty",       ZhCN = "没有待处理的任务", ZhTW = "沒有待處理的任務", En = "No pending tasks",            Ja = "処理待ちのタスクはありません",  Ko = "처리 대기 중인 작업이 없습니다" },
        new Sys_Lang { LangKey = "oa.pending.toReview",    ZhCN = "去审批",           ZhTW = "去審批",           En = "Review",                      Ja = "審査する",                     Ko = "검토하기" },
        new Sys_Lang { LangKey = "oa.pending.approve",     ZhCN = "同意",             ZhTW = "同意",             En = "Approve",                     Ja = "承認",                         Ko = "승인" },
        new Sys_Lang { LangKey = "oa.pending.reject",      ZhCN = "拒绝",             ZhTW = "拒絕",             En = "Reject",                      Ja = "却下",                         Ko = "반려" },
        new Sys_Lang { LangKey = "oa.pending.cc",          ZhCN = "抄送",             ZhTW = "抄送",             En = "CC",                          Ja = "CC",                           Ko = "참조" },
        new Sys_Lang { LangKey = "oa.pending.ccEmpty",     ZhCN = "没有抄送消息",     ZhTW = "沒有抄送消息",     En = "No CC messages",              Ja = "CCメッセージはありません",     Ko = "참조 메시지가 없습니다" },
        new Sys_Lang { LangKey = "oa.pending.commentHint", ZhCN = "请输入审批意见（可选）", ZhTW = "請輸入審批意見（可選）", En = "Comment (optional)", Ja = "審査コメントを入力（任意）", Ko = "의견을 입력하세요 (선택사항)" },

        // ── oa.running.* 在途面板 ──
        new Sys_Lang { LangKey = "oa.running.empty", ZhCN = "没有在途的流程", ZhTW = "沒有在途的流程", En = "No running flows", Ja = "進行中のフローはありません", Ko = "진행 중인 플로우가 없습니다" },

        // ── oa.done.* 已处理面板 ──
        new Sys_Lang { LangKey = "oa.done.empty",     ZhCN = "没有已处理记录", ZhTW = "沒有已處理記錄", En = "No completed records",   Ja = "処理済の記録はありません", Ko = "완료된 기록이 없습니다" },
        new Sys_Lang { LangKey = "oa.done.mine",      ZhCN = "我处理的",       ZhTW = "我處理的",       En = "Handled by me",          Ja = "自分が処理したもの",       Ko = "내가 처리한 항목" },
        new Sys_Lang { LangKey = "oa.done.all",       ZhCN = "全部",           ZhTW = "全部",           En = "All",                    Ja = "全件",                     Ko = "전체" },
        new Sys_Lang { LangKey = "oa.done.cc",        ZhCN = "抄送",           ZhTW = "抄送",           En = "CC",                     Ja = "CC",                       Ko = "참조" },
        new Sys_Lang { LangKey = "oa.done.allMonths", ZhCN = "所有月份",       ZhTW = "所有月份",       En = "All Months",             Ja = "全月",                     Ko = "전체 월" },

        // ── oa.draft.* 草稿面板 ──
        new Sys_Lang { LangKey = "oa.draft.empty",     ZhCN = "没有草稿",           ZhTW = "沒有草稿",           En = "No drafts",              Ja = "下書きはありません",         Ko = "임시저장이 없습니다" },
        new Sys_Lang { LangKey = "oa.draft.edit",      ZhCN = "继续编辑",           ZhTW = "繼續編輯",           En = "Continue Editing",       Ja = "編集を続ける",               Ko = "계속 편집" },
        new Sys_Lang { LangKey = "oa.draft.editTitle", ZhCN = "编辑草稿",           ZhTW = "編輯草稿",           En = "Edit Draft",             Ja = "下書きを編集",               Ko = "초안 편집" },
        new Sys_Lang { LangKey = "oa.draft.delete",    ZhCN = "删除",               ZhTW = "刪除",               En = "Delete",                 Ja = "削除",                       Ko = "삭제" },
        new Sys_Lang { LangKey = "oa.draft.save",      ZhCN = "保存草稿",           ZhTW = "儲存草稿",           En = "Save Draft",             Ja = "下書きを保存",               Ko = "임시저장" },
        new Sys_Lang { LangKey = "oa.draft.submit",    ZhCN = "提交",               ZhTW = "提交",               En = "Submit",                 Ja = "送信",                       Ko = "제출" },
        new Sys_Lang { LangKey = "oa.draft.cancel",    ZhCN = "取消",               ZhTW = "取消",               En = "Cancel",                 Ja = "キャンセル",                 Ko = "취소" },
        new Sys_Lang { LangKey = "oa.draft.varsHint",  ZhCN = "填写表单变量",       ZhTW = "填寫表單變數",       En = "Fill in form variables", Ja = "フォーム変数を入力",         Ko = "양식 변수 입력" },

        // ── oa.detail.* 详情/审批 抽屉 ──
        new Sys_Lang { LangKey = "oa.detail.approve",      ZhCN = "同意",           ZhTW = "同意",           En = "Approve",             Ja = "承認",                   Ko = "승인" },
        new Sys_Lang { LangKey = "oa.detail.reject",       ZhCN = "拒绝",           ZhTW = "拒絕",           En = "Reject",              Ja = "却下",                   Ko = "반려" },
        new Sys_Lang { LangKey = "oa.detail.cc",           ZhCN = "抄送",           ZhTW = "抄送",           En = "CC",                  Ja = "CC",                     Ko = "참조" },
        new Sys_Lang { LangKey = "oa.detail.approveOk",    ZhCN = "审批成功",       ZhTW = "審批成功",       En = "Approved",            Ja = "承認しました",           Ko = "승인되었습니다" },
        new Sys_Lang { LangKey = "oa.detail.rejectOk",     ZhCN = "已拒绝",         ZhTW = "已拒絕",         En = "Rejected",            Ja = "却下しました",           Ko = "반려되었습니다" },
        new Sys_Lang { LangKey = "oa.detail.actionFailed", ZhCN = "操作失败",       ZhTW = "操作失敗",       En = "Action failed",       Ja = "操作に失敗しました",     Ko = "작업에 실패했습니다" },
        new Sys_Lang { LangKey = "oa.detail.commentHint",  ZhCN = "请输入审批意见（可选）", ZhTW = "請輸入審批意見（可選）", En = "Comment (optional)", Ja = "審査コメントを入力（任意）", Ko = "의견을 입력하세요 (선택사항)" },
        new Sys_Lang { LangKey = "oa.detail.formPanel",    ZhCN = "表单内容",       ZhTW = "表單內容",       En = "Form Content",        Ja = "フォーム内容",           Ko = "양식 내용" },
        new Sys_Lang { LangKey = "oa.detail.timeline",     ZhCN = "审批进度",       ZhTW = "審批進度",       En = "Approval Progress",   Ja = "審査進捗",               Ko = "승인 진행 상황" },
        new Sys_Lang { LangKey = "oa.detail.loadFailed",   ZhCN = "加载失败",       ZhTW = "載入失敗",       En = "Load failed",         Ja = "読み込みに失敗しました", Ko = "로드에 실패했습니다" },
        new Sys_Lang { LangKey = "oa.detail.noFormData",   ZhCN = "暂无表单数据",   ZhTW = "暫無表單資料",   En = "No form data",        Ja = "フォームデータがありません", Ko = "양식 데이터가 없습니다" },

        // ── oa.col.* 列表列头 ──
        new Sys_Lang { LangKey = "oa.col.flowName",    ZhCN = "流程",     ZhTW = "流程",     En = "Flow",         Ja = "フロー",       Ko = "플로우" },
        new Sys_Lang { LangKey = "oa.col.starter",     ZhCN = "发起人",   ZhTW = "發起人",   En = "Starter",      Ja = "起票者",       Ko = "발기인" },
        new Sys_Lang { LangKey = "oa.col.status",      ZhCN = "状态",     ZhTW = "狀態",     En = "Status",       Ja = "ステータス",   Ko = "상태" },
        new Sys_Lang { LangKey = "oa.col.currentNode", ZhCN = "当前节点", ZhTW = "目前節點", En = "Current Node", Ja = "現在のノード", Ko = "현재 노드" },
        new Sys_Lang { LangKey = "oa.col.atNode",      ZhCN = "所在节点", ZhTW = "所在節點", En = "At Node",      Ja = "在ノード",     Ko = "위치한 노드" },
        new Sys_Lang { LangKey = "oa.col.handlers",    ZhCN = "处理人",   ZhTW = "處理人",   En = "Handlers",     Ja = "処理者",       Ko = "처리자" },
        new Sys_Lang { LangKey = "oa.col.createDate",  ZhCN = "发起时间", ZhTW = "發起時間", En = "Created At",   Ja = "発起日時",     Ko = "생성일시" },
        new Sys_Lang { LangKey = "oa.col.sentAt",      ZhCN = "送达时间", ZhTW = "送達時間", En = "Sent At",      Ja = "送信日時",     Ko = "전송일시" },
        new Sys_Lang { LangKey = "oa.col.doneAt",      ZhCN = "处理时间", ZhTW = "處理時間", En = "Done At",      Ja = "処理日時",     Ko = "처리일시" },
        new Sys_Lang { LangKey = "oa.col.actions",     ZhCN = "操作",     ZhTW = "操作",     En = "Actions",      Ja = "操作",         Ko = "작업" },

        // ── oa.dashboard.* 仪表盘 ──
        new Sys_Lang { LangKey = "oa.dashboard.pending",       ZhCN = "待处理",     ZhTW = "待處理",     En = "Pending",          Ja = "処理待ち",         Ko = "처리 대기" },
        new Sys_Lang { LangKey = "oa.dashboard.running",       ZhCN = "在途",       ZhTW = "在途",       En = "In Progress",      Ja = "進行中",           Ko = "진행중" },
        new Sys_Lang { LangKey = "oa.dashboard.rejected",      ZhCN = "已驳回",     ZhTW = "已駁回",     En = "Rejected",         Ja = "却下済",           Ko = "반려됨" },
        new Sys_Lang { LangKey = "oa.dashboard.doneThisMonth", ZhCN = "本月处理",   ZhTW = "本月處理",   En = "Done This Month",  Ja = "今月の処理件数",   Ko = "이번 달 처리" },
        new Sys_Lang { LangKey = "oa.dashboard.recentPending", ZhCN = "近期待处理", ZhTW = "近期待處理", En = "Recent Pending",   Ja = "最近の処理待ち",   Ko = "최근 처리 대기" },
        new Sys_Lang { LangKey = "oa.dashboard.trend",         ZhCN = "趋势",       ZhTW = "趨勢",       En = "Trend",            Ja = "トレンド",         Ko = "추세" },
        new Sys_Lang { LangKey = "oa.dashboard.noPending",     ZhCN = "暂无待处理", ZhTW = "暫無待處理", En = "No pending items", Ja = "処理待ちなし",     Ko = "대기 항목 없음" },
        new Sys_Lang { LangKey = "oa.dashboard.noTrend",       ZhCN = "暂无趋势数据", ZhTW = "暫無趨勢資料", En = "No trend data",  Ja = "トレンドデータなし", Ko = "추세 데이터 없음" },

        // ── oa.flowadmin.* 流程管理 ──
        new Sys_Lang { LangKey = "oa.flowadmin.title",          ZhCN = "流程管理",       ZhTW = "流程管理",       En = "Flow Management",     Ja = "フロー管理",           Ko = "플로우 관리" },
        new Sys_Lang { LangKey = "oa.flowadmin.empty",          ZhCN = "没有流程定义",   ZhTW = "沒有流程定義",   En = "No flows defined",    Ja = "フロー定義がありません", Ko = "정의된 플로우 없음" },
        new Sys_Lang { LangKey = "oa.flowadmin.enabled",        ZhCN = "已启用",         ZhTW = "已啟用",         En = "Enabled",             Ja = "有効",                 Ko = "활성화됨" },
        new Sys_Lang { LangKey = "oa.flowadmin.disabled",       ZhCN = "已停用",         ZhTW = "已停用",         En = "Disabled",            Ja = "無効",                 Ko = "비활성화됨" },
        new Sys_Lang { LangKey = "oa.flowadmin.uniqueHint",     ZhCN = "每表单只能绑定一个启用流程", ZhTW = "每表單只能綁定一個啟用流程", En = "Only one active flow per form", Ja = "フォームごとに有効なフローは1つのみです", Ko = "양식당 하나의 활성 플로우만 가능합니다" },
        new Sys_Lang { LangKey = "oa.flowadmin.col.flowName",   ZhCN = "流程名称",       ZhTW = "流程名稱",       En = "Flow Name",           Ja = "フロー名",             Ko = "플로우 이름" },
        new Sys_Lang { LangKey = "oa.flowadmin.col.flowKey",    ZhCN = "流程Key",        ZhTW = "流程Key",        En = "Flow Key",            Ja = "フローキー",           Ko = "플로우 키" },
        new Sys_Lang { LangKey = "oa.flowadmin.col.formKey",    ZhCN = "绑定表单Key",    ZhTW = "綁定表單Key",    En = "Bound Form Key",      Ja = "バインドフォームキー", Ko = "연결된 양식 키" },
        new Sys_Lang { LangKey = "oa.flowadmin.col.version",    ZhCN = "版本",           ZhTW = "版本",           En = "Version",             Ja = "バージョン",           Ko = "버전" },
        new Sys_Lang { LangKey = "oa.flowadmin.col.enable",     ZhCN = "启用",           ZhTW = "啟用",           En = "Enable",              Ja = "有効化",               Ko = "활성화" },
    };
}
