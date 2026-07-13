using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>服务任务(serviceTask)画面词条：oa.designer.svc.*（D-T2/D-T3 前端引用）+ 前端校验 oa.designer.errServiceConfig + 后端错误码 E-WF-016/017/018。
/// 键面以 cp6.web/src/views/oa/designer 实际引用为权威（ServiceTaskNode.vue / NodePropertyPanel.vue / EdgePropertyPanel.vue / designerModel.ts）。
/// 去重：本文件全部 30 个 svc.*（含票7 reloadCatalog + 票8 timerActionKind 四键）+ errServiceConfig + 3 错误码在既有 I18nOaInbox/Advanced/Designer/SerialSign/Approver seed 中均无重复(已 grep 核实)。</summary>
public static class I18nOaServiceTaskScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 面板标题 / 服务类型 (ServiceTaskNode.vue + NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.title",              ZhCN = "服务任务",       ZhTW = "服務任務",       En = "Service Task",   Ja = "サービスタスク",   Ko = "서비스 작업" },
        new() { LangKey = "oa.designer.svc.kind",               ZhCN = "服务类型",       ZhTW = "服務類型",       En = "Service Type",   Ja = "サービス種別",     Ko = "서비스 유형" },
        new() { LangKey = "oa.designer.svc.kind.dataWriteback", ZhCN = "数据回写",       ZhTW = "資料回寫",       En = "Data Writeback", Ja = "データ書き戻し",   Ko = "데이터 쓰기" },
        new() { LangKey = "oa.designer.svc.kind.webApi",        ZhCN = "接口调用",       ZhTW = "介面呼叫",       En = "API Call",       Ja = "API呼び出し",      Ko = "API 호출" },
        new() { LangKey = "oa.designer.svc.kind.timer",         ZhCN = "定时器",         ZhTW = "計時器",         En = "Timer",          Ja = "タイマー",         Ko = "타이머" },

        // ── 数据回写：动作 / 模式 / 参数模板 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.action",             ZhCN = "动作",           ZhTW = "動作",           En = "Action",         Ja = "アクション",       Ko = "동작" },
        new() { LangKey = "oa.designer.svc.mode",               ZhCN = "执行模式",       ZhTW = "執行模式",       En = "Execution Mode", Ja = "実行モード",       Ko = "실행 모드" },
        new() { LangKey = "oa.designer.svc.mode.sync",          ZhCN = "同步",           ZhTW = "同步",           En = "Synchronous",    Ja = "同期",             Ko = "동기" },
        new() { LangKey = "oa.designer.svc.mode.async",         ZhCN = "异步",           ZhTW = "非同步",         En = "Asynchronous",   Ja = "非同期",           Ko = "비동기" },
        new() { LangKey = "oa.designer.svc.params",             ZhCN = "参数模板",       ZhTW = "參數範本",       En = "Parameter Template", Ja = "パラメータテンプレート", Ko = "파라미터 템플릿" },
        new() { LangKey = "oa.designer.svc.paramsHint",         ZhCN = "JSON 参数模板，支持表单字段占位", ZhTW = "JSON 參數範本，支援表單欄位佔位", En = "JSON parameter template; supports form-field placeholders", Ja = "JSONパラメータテンプレート（フォーム項目のプレースホルダに対応）", Ko = "JSON 파라미터 템플릿, 양식 필드 자리표시자 지원" },

        // ── 接口调用：连接器 / 路径 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.connector",          ZhCN = "连接器",         ZhTW = "連接器",         En = "Connector",      Ja = "コネクタ",         Ko = "커넥터" },
        new() { LangKey = "oa.designer.svc.path",               ZhCN = "路径",           ZhTW = "路徑",           En = "Path",           Ja = "パス",             Ko = "경로" },
        new() { LangKey = "oa.designer.svc.pathHint",           ZhCN = "接口相对路径，如 /api/v1/orders", ZhTW = "介面相對路徑，如 /api/v1/orders", En = "API relative path, e.g. /api/v1/orders", Ja = "APIの相対パス（例 /api/v1/orders）", Ko = "API 상대 경로, 예 /api/v1/orders" },

        // ── 定时器：延时模式 / 延时值 / 到点动作 (NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.delayMode",          ZhCN = "延时模式",       ZhTW = "延時模式",       En = "Delay Mode",     Ja = "遅延モード",       Ko = "지연 모드" },
        new() { LangKey = "oa.designer.svc.delayMode.duration", ZhCN = "时长",           ZhTW = "時長",           En = "Duration",       Ja = "期間",             Ko = "기간" },
        new() { LangKey = "oa.designer.svc.delayMode.untilDate",ZhCN = "到指定时间",     ZhTW = "到指定時間",     En = "Until Date",     Ja = "指定日時まで",     Ko = "지정 일시까지" },
        new() { LangKey = "oa.designer.svc.delayMode.untilExpr",ZhCN = "到表达式时间",   ZhTW = "到運算式時間",   En = "Until Expression", Ja = "式で指定した時刻まで", Ko = "표현식 시각까지" },
        new() { LangKey = "oa.designer.svc.delayValue",         ZhCN = "延时值",         ZhTW = "延時值",         En = "Delay Value",    Ja = "遅延値",           Ko = "지연 값" },
        new() { LangKey = "oa.designer.svc.delayValueHint",     ZhCN = "如 3d/2h/30m，或日期/表达式", ZhTW = "如 3d/2h/30m，或日期/運算式", En = "e.g. 3d/2h/30m, or a date/expression", Ja = "例 3d/2h/30m、または日付/式", Ko = "예 3d/2h/30m 또는 날짜/표현식" },
        new() { LangKey = "oa.designer.svc.timerAction",        ZhCN = "到点动作",       ZhTW = "到點動作",       En = "On-Fire Action", Ja = "発火時アクション", Ko = "실행 시 동작" },
        // 票8：到点动作类型（none / 数据回写 / webApi 连接器）——补 spec §5.3「定时到点发 webApi」缺口
        new() { LangKey = "oa.designer.svc.timerActionKind",       ZhCN = "到点动作类型",   ZhTW = "到點動作類型",   En = "On-Fire Action Type", Ja = "発火時アクション種別", Ko = "실행 시 액션 유형" },
        new() { LangKey = "oa.designer.svc.timerActionKind.none",  ZhCN = "无（纯等待）",   ZhTW = "無（純等待）",   En = "None (pure wait)",    Ja = "なし（待機のみ）",     Ko = "없음(대기만)" },
        new() { LangKey = "oa.designer.svc.timerActionKind.write", ZhCN = "数据回写动作",   ZhTW = "資料回寫動作",   En = "Data-writeback action", Ja = "データ書き戻しアクション", Ko = "데이터 기록 액션" },
        new() { LangKey = "oa.designer.svc.timerActionKind.api",   ZhCN = "接口调用",       ZhTW = "介面呼叫",       En = "API call",            Ja = "API呼び出し",          Ko = "API 호출" },

        // ── 重试（三 kind 共用）(NodePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.maxRetries",         ZhCN = "最大重试次数",   ZhTW = "最大重試次數",   En = "Max Retries",    Ja = "最大リトライ回数", Ko = "최대 재시도 횟수" },
        new() { LangKey = "oa.designer.svc.backoff",            ZhCN = "重试间隔（秒）", ZhTW = "重試間隔（秒）", En = "Retry Backoff (sec)", Ja = "リトライ間隔（秒）", Ko = "재시도 간격(초)" },
        new() { LangKey = "oa.designer.svc.reloadCatalog",      ZhCN = "重新加载服务目录", ZhTW = "重新載入服務目錄", En = "Reload service catalog", Ja = "サービスカタログを再読み込み", Ko = "서비스 카탈로그 다시 불러오기" },

        // ── 失败边 (EdgePropertyPanel.vue) ──
        new() { LangKey = "oa.designer.svc.errorEdge",          ZhCN = "失败边",         ZhTW = "失敗邊",         En = "Error Edge",     Ja = "失敗エッジ",       Ko = "실패 분기" },
        new() { LangKey = "oa.designer.svc.errorEdgeHint",      ZhCN = "服务任务重试耗尽后沿此边流转", ZhTW = "服務任務重試耗盡後沿此邊流轉", En = "Taken when the service task exhausts its retries", Ja = "サービスタスクのリトライを使い切ったときにこのエッジを通ります", Ko = "서비스 태스크가 재시도를 모두 소진하면 이 분기로 진행됩니다" },

        // ── 前端校验消息 (designerModel.ts validateClient) ──
        new() { LangKey = "oa.designer.errServiceConfig",       ZhCN = "服务任务配置不完整", ZhTW = "服務任務配置不完整", En = "Service task config incomplete", Ja = "サービスタスクの設定が不完全です", Ko = "서비스 태스크 구성이 불완전합니다" },

        // ── 后端错误码 (FlowSchemaValidator / DesignerService / Executors) ──
        new() { LangKey = "E-WF-016", ZhCN = "服务任务节点配置不完整", ZhTW = "服務任務節點配置不完整", En = "Incomplete service task node configuration", Ja = "サービスタスクノードの設定が不完全です", Ko = "서비스 태스크 노드 구성이 불완전합니다" },
        new() { LangKey = "E-WF-017", ZhCN = "错误出边配置非法",       ZhTW = "錯誤出邊配置非法",       En = "Invalid error edge configuration",           Ja = "エラーエッジの設定が不正です",           Ko = "오류 엣지 설정이 잘못되었습니다" },
        new() { LangKey = "E-WF-018", ZhCN = "服务动作或连接器未注册", ZhTW = "服務動作或連接器未註冊", En = "Service action or connector not registered", Ja = "サービスアクションまたはコネクタが未登録です", Ko = "서비스 액션 또는 커넥터가 등록되지 않았습니다" },
    };
}
