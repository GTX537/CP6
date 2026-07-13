using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>流程触发器画面五语（WFS 波③ 事件触发 wfs-trigger；E-WF-022~024 错误码同表）。
/// 键面以 cp6.web/src/views/oa/admin 下三新文件实际 t()/labelKey 引用为权威
/// （FlowAdmin.vue 新增 tab 键 / FlowTriggerPanel.vue / FlowTriggerDialog.vue / flowTriggerModel.ts）——
/// 双向 grep 对账零缺零孤儿（49 oa.flowtrigger.* + oa.flowadmin.tab.flows + E-WF-022~024）。
/// 去重：oa.flowadmin.* 其余 10 键（title/uniqueHint/empty/col.*/enabled/disabled）已在
/// I18nOaInboxScreenSeed 播种，本 seed 只补新增的 oa.flowadmin.tab.flows；common.*（add/save/ok/edit/cancel）
/// 属既有通用键，不在本 seed 重复。SeedLangs 对既有键 insert-only，故本 seed 全为新键，部署库洁净套用。</summary>
public static class I18nOaFlowTriggerScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        new() { LangKey = "oa.flowadmin.tab.flows", ZhCN = "流程", ZhTW = "流程", En = "Flows", Ja = "フロー", Ko = "플로우" },
        new() { LangKey = "oa.flowtrigger.tab", ZhCN = "触发器", ZhTW = "觸發器", En = "Triggers", Ja = "トリガー", Ko = "트리거" },
        new() { LangKey = "oa.flowtrigger.new", ZhCN = "新建触发器", ZhTW = "新建觸發器", En = "New Trigger", Ja = "トリガー作成", Ko = "트리거 생성" },
        new() { LangKey = "oa.flowtrigger.empty", ZhCN = "暂无触发器", ZhTW = "暫無觸發器", En = "No triggers", Ja = "トリガーなし", Ko = "트리거 없음" },
        new() { LangKey = "oa.flowtrigger.col.type", ZhCN = "类型", ZhTW = "類型", En = "Type", Ja = "種別", Ko = "유형" },
        new() { LangKey = "oa.flowtrigger.col.flowKey", ZhCN = "目标流程", ZhTW = "目標流程", En = "Target Flow", Ja = "対象フロー", Ko = "대상 플로우" },
        new() { LangKey = "oa.flowtrigger.col.eventKey", ZhCN = "事件键", ZhTW = "事件鍵", En = "Event Key", Ja = "イベントキー", Ko = "이벤트 키" },
        new() { LangKey = "oa.flowtrigger.col.enabled", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "사용" },
        new() { LangKey = "oa.flowtrigger.col.nextDue", ZhCN = "下次触发", ZhTW = "下次觸發", En = "Next Due", Ja = "次回実行", Ko = "다음 실행" },
        new() { LangKey = "oa.flowtrigger.col.lastFired", ZhCN = "上次触发", ZhTW = "上次觸發", En = "Last Fired", Ja = "前回実行", Ko = "마지막 실행" },
        new() { LangKey = "oa.flowtrigger.col.actions", ZhCN = "操作", ZhTW = "操作", En = "Actions", Ja = "操作", Ko = "작업" },
        new() { LangKey = "oa.flowtrigger.type.timer", ZhCN = "定时", ZhTW = "定時", En = "Timer", Ja = "タイマー", Ko = "타이머" },
        new() { LangKey = "oa.flowtrigger.type.event", ZhCN = "事件", ZhTW = "事件", En = "Event", Ja = "イベント", Ko = "이벤트" },
        new() { LangKey = "oa.flowtrigger.type.message", ZhCN = "外呼", ZhTW = "外呼", En = "Message", Ja = "メッセージ", Ko = "메시지" },
        new() { LangKey = "oa.flowtrigger.manualFire", ZhCN = "试发", ZhTW = "試發", En = "Test Fire", Ja = "テスト実行", Ko = "테스트 실행" },
        new() { LangKey = "oa.flowtrigger.fires", ZhCN = "流水", ZhTW = "流水", En = "Fire Log", Ja = "実行履歴", Ko = "실행 이력" },
        new() { LangKey = "oa.flowtrigger.resetKey", ZhCN = "重置密钥", ZhTW = "重置密鑰", En = "Reset Key", Ja = "キー再発行", Ko = "키 재설정" },
        new() { LangKey = "oa.flowtrigger.resetKeyConfirm", ZhCN = "重置后旧密钥立即失效，确认？", ZhTW = "重置後舊密鑰立即失效，確認？", En = "The old key becomes invalid immediately. Continue?", Ja = "再発行すると旧キーは即時無効になります。続行しますか？", Ko = "재설정하면 기존 키가 즉시 무효화됩니다. 계속하시겠습니까?" },
        new() { LangKey = "oa.flowtrigger.keyTitle", ZhCN = "API 密钥", ZhTW = "API 密鑰", En = "API Key", Ja = "API キー", Ko = "API 키" },
        new() { LangKey = "oa.flowtrigger.keyOnce", ZhCN = "密钥仅此一次显示，请立即妥善保存", ZhTW = "密鑰僅此一次顯示，請立即妥善保存", En = "This key is shown only once. Store it securely now.", Ja = "このキーは一度しか表示されません。今すぐ安全に保管してください。", Ko = "이 키는 한 번만 표시됩니다. 지금 안전하게 보관하세요." },
        new() { LangKey = "oa.flowtrigger.keyCreateHint", ZhCN = "保存后将生成并显示一次 API 密钥", ZhTW = "保存後將生成並顯示一次 API 密鑰", En = "An API key will be generated and shown once after saving", Ja = "保存後に API キーが生成され一度だけ表示されます", Ko = "저장 후 API 키가 생성되어 한 번만 표시됩니다" },
        new() { LangKey = "oa.flowtrigger.fired", ZhCN = "已发起", ZhTW = "已發起", En = "Fired", Ja = "起動済み", Ko = "실행됨" },
        new() { LangKey = "oa.flowtrigger.fire.time", ZhCN = "时间", ZhTW = "時間", En = "Time", Ja = "時刻", Ko = "시각" },
        new() { LangKey = "oa.flowtrigger.fire.result", ZhCN = "结果", ZhTW = "結果", En = "Result", Ja = "結果", Ko = "결과" },
        new() { LangKey = "oa.flowtrigger.fire.instance", ZhCN = "实例", ZhTW = "實例", En = "Instance", Ja = "インスタンス", Ko = "인스턴스" },
        new() { LangKey = "oa.flowtrigger.fire.error", ZhCN = "错误", ZhTW = "錯誤", En = "Error", Ja = "エラー", Ko = "오류" },
        new() { LangKey = "oa.flowtrigger.fire.ok", ZhCN = "成功", ZhTW = "成功", En = "OK", Ja = "成功", Ko = "성공" },
        new() { LangKey = "oa.flowtrigger.fire.fail", ZhCN = "失败", ZhTW = "失敗", En = "Failed", Ja = "失敗", Ko = "실패" },
        new() { LangKey = "oa.flowtrigger.fire.pending", ZhCN = "进行中", ZhTW = "進行中", En = "Pending", Ja = "処理中", Ko = "진행 중" },
        new() { LangKey = "oa.flowtrigger.form.type", ZhCN = "触发器类型", ZhTW = "觸發器類型", En = "Trigger Type", Ja = "トリガー種別", Ko = "트리거 유형" },
        new() { LangKey = "oa.flowtrigger.form.flowKey", ZhCN = "目标流程", ZhTW = "目標流程", En = "Target Flow", Ja = "対象フロー", Ko = "대상 플로우" },
        new() { LangKey = "oa.flowtrigger.form.starter", ZhCN = "名义发起人", ZhTW = "名義發起人", En = "Nominal Starter", Ja = "名義起動者", Ko = "명의 시작자" },
        new() { LangKey = "oa.flowtrigger.form.starterHint", ZhCN = "用户 Id（审批人 starter.* 解析依赖它）", ZhTW = "用戶 Id（審批人 starter.* 解析依賴它）", En = "User Id (starter.* approver resolution depends on it)", Ja = "ユーザー Id（starter.* 承認者解決が依存）", Ko = "사용자 Id (starter.* 결재자 해석에 사용)" },
        new() { LangKey = "oa.flowtrigger.form.cron", ZhCN = "cron 表达式", ZhTW = "cron 表達式", En = "Cron Expression", Ja = "cron 式", Ko = "cron 식" },
        new() { LangKey = "oa.flowtrigger.form.cronPreset", ZhCN = "常用预设", ZhTW = "常用預設", En = "Presets", Ja = "プリセット", Ko = "프리셋" },
        new() { LangKey = "oa.flowtrigger.form.previewTz", ZhCN = "下次触发预览（服务器默认时区）", ZhTW = "下次觸發預覽（伺服器默認時區）", En = "Next occurrences (server default timezone)", Ja = "次回実行プレビュー（サーバー既定タイムゾーン）", Ko = "다음 실행 미리보기 (서버 기본 시간대)" },
        new() { LangKey = "oa.flowtrigger.form.varsJson", ZhCN = "初始流程变量", ZhTW = "初始流程變量", En = "Initial Variables", Ja = "初期変数", Ko = "초기 변수" },
        new() { LangKey = "oa.flowtrigger.form.eventKey", ZhCN = "事件键", ZhTW = "事件鍵", En = "Event Key", Ja = "イベントキー", Ko = "이벤트 키" },
        new() { LangKey = "oa.flowtrigger.form.varsMap", ZhCN = "变量映射", ZhTW = "變量映射", En = "Variable Mapping", Ja = "変数マッピング", Ko = "변수 매핑" },
        new() { LangKey = "oa.flowtrigger.form.varName", ZhCN = "变量名", ZhTW = "變量名", En = "Variable", Ja = "変数名", Ko = "변수명" },
        new() { LangKey = "oa.flowtrigger.form.varsSchema", ZhCN = "白名单字段", ZhTW = "白名單欄位", En = "Allowed Fields", Ja = "許可フィールド", Ko = "허용 필드" },
        new() { LangKey = "oa.flowtrigger.form.varsSchemaHint", ZhCN = "逗号分隔；不在名单的负载键将被丢弃", ZhTW = "逗號分隔；不在名單的負載鍵將被丟棄", En = "Comma separated; payload keys not listed are dropped", Ja = "カンマ区切り；リスト外のキーは破棄", Ko = "쉼표 구분; 목록에 없는 키는 삭제됨" },
        new() { LangKey = "oa.flowtrigger.preset.daily", ZhCN = "每日 9 点", ZhTW = "每日 9 點", En = "Daily 09:00", Ja = "毎日 9 時", Ko = "매일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.monday", ZhCN = "每周一 9 点", ZhTW = "每週一 9 點", En = "Monday 09:00", Ja = "毎週月曜 9 時", Ko = "매주 월요일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.day25", ZhCN = "每月 25 日 9 点", ZhTW = "每月 25 日 9 點", En = "25th 09:00", Ja = "毎月 25 日 9 時", Ko = "매월 25일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.monthEnd", ZhCN = "每月末（按 28 日近似）", ZhTW = "每月末（按 28 日近似）", En = "Month end (approx. 28th)", Ja = "月末（28 日で近似）", Ko = "월말 (28일로 근사)" },
        new() { LangKey = "oa.flowtrigger.err.flowKey", ZhCN = "请填写目标流程", ZhTW = "請填寫目標流程", En = "Target flow is required", Ja = "対象フローを入力してください", Ko = "대상 플로우를 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.starter", ZhCN = "请填写名义发起人", ZhTW = "請填寫名義發起人", En = "Nominal starter is required", Ja = "名義起動者を入力してください", Ko = "명의 시작자를 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.cron", ZhCN = "请填写 cron 表达式", ZhTW = "請填寫 cron 表達式", En = "Cron expression is required", Ja = "cron 式を入力してください", Ko = "cron 식을 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.eventKey", ZhCN = "请填写事件键", ZhTW = "請填寫事件鍵", En = "Event key is required", Ja = "イベントキーを入力してください", Ko = "이벤트 키를 입력하세요" },
        new() { LangKey = "E-WF-022", ZhCN = "触发器配置无效", ZhTW = "觸發器配置無效", En = "Invalid trigger configuration", Ja = "トリガー設定が無効です", Ko = "트리거 구성이 잘못되었습니다" },
        new() { LangKey = "E-WF-023", ZhCN = "目标流程不可发起", ZhTW = "目標流程不可發起", En = "Target flow cannot be started", Ja = "対象フローを起動できません", Ko = "대상 플로우를 시작할 수 없습니다" },
        new() { LangKey = "E-WF-024", ZhCN = "触发发起失败", ZhTW = "觸發發起失敗", En = "Trigger fire failed", Ja = "トリガー起動に失敗しました", Ko = "트리거 실행에 실패했습니다" },
    };
}
