using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA Phase C′ 流程设计器（基础版）五语词条：
///   - 菜单导航：nav.738（流程设计器）
///   - 画布/工具条/调色板：oa.designer.* （DesignerCanvas.vue / DesignerView.vue）
///   - 属性面板：oa.designer.nodeProps / edgeProps / strategy.* / countersign.* / timeoutAction.*
///   - 校验消息：oa.designer.errNoStart / errNoEnd / errDanglingEdge / errNoStrategy
///   - 克隆对话框：oa.designer.clone* / oa.designer.newFlow* / oa.designer.stateCode / stateName
///
/// ⚠️ Phase B seed（I18nOaInboxScreenSeed）已含：E-WF-001~008 / oa.formto.* / oa.inst.* /
///    nav.740/733/734 / oa.inbox.* / oa.pending.* / oa.running.* / oa.done.* /
///    oa.draft.* / oa.detail.* / oa.col.* / oa.dashboard.* / oa.flowadmin.*
/// ⚠️ Phase C seed（I18nOaAdvancedScreenSeed）已含：nav.735/736/737 / oa.catalog.* /
///    oa.initiate.* / oa.settings.* / oa.transfer.* / oa.detail.transfer
/// ⚠️ 旧 WF 设计器（I18nWfDesignerSeed）使用中文字符串为 LangKey，无 oa.designer.* 键冲突。
/// 本 seed 中所有键均为新增，无重复。
/// </summary>
public static class I18nOaDesignerScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单导航 nav.738 ──
        new Sys_Lang { LangKey = "nav.738", ZhCN = "流程设计器", ZhTW = "流程設計器", En = "Flow Designer", Ja = "フローデザイナー", Ko = "플로우 디자이너" },

        // ── 工具条 / 主操作 ──
        new Sys_Lang { LangKey = "oa.designer.selectFlow",    ZhCN = "选择流程",     ZhTW = "選擇流程",     En = "Select Flow",         Ja = "フローを選択",       Ko = "플로우 선택" },
        new Sys_Lang { LangKey = "oa.designer.newFlow",       ZhCN = "新建流程",     ZhTW = "新建流程",     En = "New Flow",            Ja = "新規フロー",         Ko = "새 플로우" },
        new Sys_Lang { LangKey = "oa.designer.save",          ZhCN = "保存",         ZhTW = "儲存",         En = "Save",                Ja = "保存",               Ko = "저장" },
        new Sys_Lang { LangKey = "oa.designer.saveOk",        ZhCN = "保存成功",     ZhTW = "儲存成功",     En = "Saved successfully",  Ja = "保存しました",       Ko = "저장되었습니다" },
        new Sys_Lang { LangKey = "oa.designer.validate",      ZhCN = "校验",         ZhTW = "校驗",         En = "Validate",            Ja = "検証",               Ko = "유효성 검사" },
        new Sys_Lang { LangKey = "oa.designer.validateOk",    ZhCN = "校验通过",     ZhTW = "校驗通過",     En = "Validation passed",   Ja = "検証に合格しました", Ko = "유효성 검사 통과" },
        new Sys_Lang { LangKey = "oa.designer.clone",         ZhCN = "另存副本",     ZhTW = "另存副本",     En = "Clone",               Ja = "コピーとして保存",   Ko = "복사본으로 저장" },
        new Sys_Lang { LangKey = "oa.designer.undo",          ZhCN = "撤销",         ZhTW = "復原",         En = "Undo",                Ja = "元に戻す",           Ko = "실행 취소" },
        new Sys_Lang { LangKey = "oa.designer.redo",          ZhCN = "重做",         ZhTW = "重做",         En = "Redo",                Ja = "やり直し",           Ko = "다시 실행" },
        new Sys_Lang { LangKey = "oa.designer.deleteSelected",ZhCN = "删除选中",     ZhTW = "刪除選中",     En = "Delete Selected",     Ja = "選択を削除",         Ko = "선택 삭제" },
        new Sys_Lang { LangKey = "oa.designer.autoLayout",    ZhCN = "自动布局",     ZhTW = "自動佈局",     En = "Auto Layout",         Ja = "自動レイアウト",     Ko = "자동 레이아웃" },
        new Sys_Lang { LangKey = "oa.designer.showGrid",      ZhCN = "显示网格",     ZhTW = "顯示格線",     En = "Show Grid",           Ja = "グリッドを表示",     Ko = "그리드 표시" },
        new Sys_Lang { LangKey = "oa.designer.hideGrid",      ZhCN = "隐藏网格",     ZhTW = "隱藏格線",     En = "Hide Grid",           Ja = "グリッドを非表示",   Ko = "그리드 숨기기" },
        new Sys_Lang { LangKey = "oa.designer.toggleGrid",    ZhCN = "切换网格",     ZhTW = "切換格線",     En = "Toggle Grid",         Ja = "グリッド切替",       Ko = "그리드 전환" },
        new Sys_Lang { LangKey = "oa.designer.selectHint",    ZhCN = "选择节点或连线后在此编辑属性", ZhTW = "選擇節點或連線後在此編輯屬性", En = "Select a node or edge to edit its properties", Ja = "ノードまたは接続を選択してプロパティを編集", Ko = "노드 또는 연결을 선택하여 속성 편집" },

        // ── 流程元数据字段 ──
        new Sys_Lang { LangKey = "oa.designer.flowKey",       ZhCN = "流程标识",     ZhTW = "流程標識",     En = "Flow Key",            Ja = "フローキー",         Ko = "플로우 식별자" },
        new Sys_Lang { LangKey = "oa.designer.flowName",      ZhCN = "流程名称",     ZhTW = "流程名稱",     En = "Flow Name",           Ja = "フロー名",           Ko = "플로우 이름" },
        new Sys_Lang { LangKey = "oa.designer.formKey",       ZhCN = "表单标识",     ZhTW = "表單標識",     En = "Form Key",            Ja = "フォームキー",       Ko = "양식 식별자" },
        new Sys_Lang { LangKey = "oa.designer.functionId",    ZhCN = "功能 ID",      ZhTW = "功能 ID",      En = "Function ID",         Ja = "機能 ID",            Ko = "기능 ID" },
        new Sys_Lang { LangKey = "oa.designer.flowCode",      ZhCN = "流程编号",     ZhTW = "流程編號",     En = "Flow Code",           Ja = "フローコード",       Ko = "플로우 코드" },
        new Sys_Lang { LangKey = "oa.designer.flowKeyRequired",   ZhCN = "请填写流程标识", ZhTW = "請填寫流程標識", En = "Flow key is required",   Ja = "フローキーを入力してください",   Ko = "플로우 식별자를 입력하세요" },
        new Sys_Lang { LangKey = "oa.designer.flowNameRequired",  ZhCN = "请填写流程名称", ZhTW = "請填寫流程名稱", En = "Flow name is required",   Ja = "フロー名を入力してください",     Ko = "플로우 이름을 입력하세요" },

        // ── 调色板（节点类型）──
        new Sys_Lang { LangKey = "oa.designer.palette",       ZhCN = "调色板",       ZhTW = "調色板",       En = "Palette",             Ja = "パレット",           Ko = "팔레트" },
        new Sys_Lang { LangKey = "oa.designer.nodeType",      ZhCN = "节点类型",     ZhTW = "節點類型",     En = "Node Type",           Ja = "ノード種別",         Ko = "노드 유형" },

        // ── 属性面板 ── 节点属性 ──
        new Sys_Lang { LangKey = "oa.designer.nodeProps",     ZhCN = "节点属性",     ZhTW = "節點屬性",     En = "Node Properties",     Ja = "ノードのプロパティ", Ko = "노드 속성" },
        new Sys_Lang { LangKey = "oa.designer.basicParams",   ZhCN = "基本参数",     ZhTW = "基本參數",     En = "Basic Params",        Ja = "基本パラメータ",     Ko = "기본 매개변수" },
        new Sys_Lang { LangKey = "oa.designer.advancedParams",ZhCN = "高级参数",     ZhTW = "高級參數",     En = "Advanced Params",     Ja = "詳細パラメータ",     Ko = "고급 매개변수" },

        // 审批人策略
        new Sys_Lang { LangKey = "oa.designer.approverType",     ZhCN = "审批人策略",   ZhTW = "審批人策略",   En = "Approver Strategy",   Ja = "承認者戦略",         Ko = "승인자 전략" },
        new Sys_Lang { LangKey = "oa.designer.approverUser",     ZhCN = "指定审批人",   ZhTW = "指定審批人",   En = "Specified Approver",  Ja = "指定承認者",         Ko = "지정 승인자" },
        new Sys_Lang { LangKey = "oa.designer.approverRole",     ZhCN = "审批角色",     ZhTW = "審批角色",     En = "Approval Role",       Ja = "承認ロール",         Ko = "승인 역할" },
        new Sys_Lang { LangKey = "oa.designer.approverLevels",   ZhCN = "上级层数",     ZhTW = "上級層數",     En = "Manager Levels",      Ja = "上位レベル数",       Ko = "상위 레벨 수" },
        new Sys_Lang { LangKey = "oa.designer.userHint",         ZhCN = "请输入用户ID（逗号分隔）", ZhTW = "請輸入使用者ID（逗號分隔）", En = "Enter user IDs (comma-separated)", Ja = "ユーザーID（カンマ区切り）を入力", Ko = "사용자 ID 입력 (쉼표 구분)" },

        // 策略枚举
        new Sys_Lang { LangKey = "oa.designer.strategy.specified",     ZhCN = "指定人员",   ZhTW = "指定人員",   En = "Specified User",    Ja = "指定ユーザー",       Ko = "지정 사용자" },
        new Sys_Lang { LangKey = "oa.designer.strategy.role",          ZhCN = "指定角色",   ZhTW = "指定角色",   En = "By Role",           Ja = "ロール指定",         Ko = "역할 지정" },
        new Sys_Lang { LangKey = "oa.designer.strategy.directManager", ZhCN = "直属上级",   ZhTW = "直屬上級",   En = "Direct Manager",    Ja = "直属の上司",         Ko = "직속 상관" },
        new Sys_Lang { LangKey = "oa.designer.strategy.deptLeader",    ZhCN = "部门负责人", ZhTW = "部門負責人", En = "Dept Leader",       Ja = "部門リーダー",       Ko = "부서장" },
        new Sys_Lang { LangKey = "oa.designer.strategy.starter",       ZhCN = "发起人自审", ZhTW = "發起人自審", En = "Starter Self-review",Ja = "申請者自己審査",     Ko = "발기인 자체 검토" },

        // 会签规则
        new Sys_Lang { LangKey = "oa.designer.countersign",     ZhCN = "会签规则",   ZhTW = "會簽規則",   En = "Countersign Rule",  Ja = "合議ルール",         Ko = "공동 서명 규칙" },
        new Sys_Lang { LangKey = "oa.designer.countersign.all", ZhCN = "全部同意",   ZhTW = "全部同意",   En = "All Approve",       Ja = "全員承認",           Ko = "전원 동의" },
        new Sys_Lang { LangKey = "oa.designer.countersign.any", ZhCN = "任一同意",   ZhTW = "任一同意",   En = "Any Approve",       Ja = "いずれか承認",       Ko = "하나라도 동의" },
        new Sys_Lang { LangKey = "oa.designer.countersign.veto",ZhCN = "一票否决",   ZhTW = "一票否決",   En = "Single Veto",       Ja = "一票否決",           Ko = "한 표 거부" },

        // 超时
        new Sys_Lang { LangKey = "oa.designer.timeoutDays",              ZhCN = "超时(天)",     ZhTW = "逾時(天)",     En = "Timeout (days)",      Ja = "タイムアウト(日)",   Ko = "시간 초과(일)" },
        new Sys_Lang { LangKey = "oa.designer.timeoutAction",            ZhCN = "超时动作",     ZhTW = "逾時動作",     En = "Timeout Action",      Ja = "タイムアウト動作",   Ko = "시간 초과 동작" },
        new Sys_Lang { LangKey = "oa.designer.timeoutAction.remind",     ZhCN = "催办",         ZhTW = "催辦",         En = "Remind",              Ja = "催促",               Ko = "독촉" },
        new Sys_Lang { LangKey = "oa.designer.timeoutAction.approve",    ZhCN = "自动通过",     ZhTW = "自動通過",     En = "Auto Approve",        Ja = "自動承認",           Ko = "자동 승인" },
        new Sys_Lang { LangKey = "oa.designer.timeoutAction.reject",     ZhCN = "自动驳回",     ZhTW = "自動駁回",     En = "Auto Reject",         Ja = "自動却下",           Ko = "자동 반려" },
        new Sys_Lang { LangKey = "oa.designer.timeoutAction.escalate",   ZhCN = "升级",         ZhTW = "升級",         En = "Escalate",            Ja = "エスカレーション",   Ko = "에스컬레이션" },

        // 允许退回
        new Sys_Lang { LangKey = "oa.designer.allowReject",   ZhCN = "允许退回",     ZhTW = "允許退回",     En = "Allow Reject",        Ja = "差し戻し許可",       Ko = "반려 허용" },

        // 抄送
        new Sys_Lang { LangKey = "oa.designer.ccSection",     ZhCN = "抄送",         ZhTW = "副本",         En = "CC",                  Ja = "CC",                 Ko = "참조" },
        new Sys_Lang { LangKey = "oa.designer.ccUsers",       ZhCN = "抄送人",       ZhTW = "副本人",       En = "CC Users",            Ja = "CC ユーザー",        Ko = "참조 사용자" },

        // ── 属性面板 ── 连线属性 ──
        new Sys_Lang { LangKey = "oa.designer.edgeProps",          ZhCN = "连线属性",     ZhTW = "連線屬性",     En = "Edge Properties",     Ja = "接続のプロパティ",   Ko = "연결 속성" },
        new Sys_Lang { LangKey = "oa.designer.pathType",           ZhCN = "路径类型",     ZhTW = "路徑類型",     En = "Path Type",           Ja = "パスタイプ",         Ko = "경로 유형" },
        new Sys_Lang { LangKey = "oa.designer.conditionNone",      ZhCN = "无条件",       ZhTW = "無條件",       En = "Unconditional",       Ja = "無条件",             Ko = "무조건" },
        new Sys_Lang { LangKey = "oa.designer.conditionCond",      ZhCN = "条件表达式",   ZhTW = "條件表達式",   En = "Condition Expression",Ja = "条件式",             Ko = "조건식" },
        new Sys_Lang { LangKey = "oa.designer.conditionExpr",      ZhCN = "表达式",       ZhTW = "表達式",       En = "Expression",          Ja = "式",                 Ko = "표현식" },
        new Sys_Lang { LangKey = "oa.designer.conditionPlaceholder",ZhCN = "如 days <= 3", ZhTW = "如 days <= 3", En = "e.g. days <= 3",     Ja = "例：days <= 3",      Ko = "예: days <= 3" },

        // ── 校验消息 ──
        new Sys_Lang { LangKey = "oa.designer.errNoStart",      ZhCN = "必须有且仅有一个开始节点", ZhTW = "必須有且僅有一個開始節點", En = "Must have exactly one start node",    Ja = "開始ノードが1つだけ必要です",   Ko = "시작 노드가 정확히 하나 있어야 합니다" },
        new Sys_Lang { LangKey = "oa.designer.errNoEnd",        ZhCN = "必须有至少一个结束节点",   ZhTW = "必須有至少一個結束節點",   En = "Must have at least one end node",     Ja = "終了ノードが少なくとも1つ必要", Ko = "종료 노드가 하나 이상 있어야 합니다" },
        new Sys_Lang { LangKey = "oa.designer.errDanglingEdge", ZhCN = "存在引用不存在节点的连线", ZhTW = "存在引用不存在節點的連線", En = "Edge references non-existent node",   Ja = "存在しないノードを参照する接続があります", Ko = "존재하지 않는 노드를 참조하는 연결이 있습니다" },
        new Sys_Lang { LangKey = "oa.designer.errNoStrategy",   ZhCN = "审批节点必须设置审批人策略", ZhTW = "審批節點必須設置審批人策略", En = "Approval node must have approver strategy", Ja = "承認ノードに承認者戦略が必要です", Ko = "승인 노드에 승인자 전략이 있어야 합니다" },

        // ── 克隆对话框 ──
        new Sys_Lang { LangKey = "oa.designer.cloneTitle",          ZhCN = "另存为副本",   ZhTW = "另存為副本",   En = "Save as Clone",       Ja = "コピーとして保存",   Ko = "복사본으로 저장" },
        new Sys_Lang { LangKey = "oa.designer.cloneConfirm",        ZhCN = "确认另存",     ZhTW = "確認另存",     En = "Confirm Clone",       Ja = "コピーを確認",       Ko = "복사본 확인" },
        new Sys_Lang { LangKey = "oa.designer.cloneOk",             ZhCN = "副本已创建",   ZhTW = "副本已建立",   En = "Clone created",       Ja = "コピーを作成しました", Ko = "복사본이 생성되었습니다" },
        new Sys_Lang { LangKey = "oa.designer.cloneFieldsRequired", ZhCN = "请填写新流程标识和名称", ZhTW = "請填寫新流程標識和名稱", En = "New flow key and name are required", Ja = "新しいフローキーと名前を入力してください", Ko = "새 플로우 식별자와 이름을 입력하세요" },
        new Sys_Lang { LangKey = "oa.designer.newFlowKey",          ZhCN = "新流程标识",   ZhTW = "新流程標識",   En = "New Flow Key",        Ja = "新しいフローキー",   Ko = "새 플로우 식별자" },
        new Sys_Lang { LangKey = "oa.designer.newFlowName",         ZhCN = "新流程名称",   ZhTW = "新流程名稱",   En = "New Flow Name",       Ja = "新しいフロー名",     Ko = "새 플로우 이름" },

        // ── 状态搜索字段（FlowAdmin 复用 / 流程列表顶部）──
        new Sys_Lang { LangKey = "oa.designer.searchState",  ZhCN = "状态",         ZhTW = "狀態",         En = "Status",              Ja = "ステータス",         Ko = "상태" },
        new Sys_Lang { LangKey = "oa.designer.stateCode",    ZhCN = "状态码",       ZhTW = "狀態碼",       En = "State Code",          Ja = "状態コード",         Ko = "상태 코드" },
        new Sys_Lang { LangKey = "oa.designer.stateName",    ZhCN = "状态名称",     ZhTW = "狀態名稱",     En = "State Name",          Ja = "状態名",             Ko = "상태 이름" },
    };
}
