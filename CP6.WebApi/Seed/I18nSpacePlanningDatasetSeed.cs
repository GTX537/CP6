using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E12-S02 de-identified historical task and replay clock screen text.
/// </summary>
public static class I18nSpacePlanningDatasetSeed
{
    public static readonly Sys_Lang[] Items =
    [
        L("space.planningDataset.title", "脱敏历史任务数据集", "去識別化歷史任務資料集", "De-identified historical task datasets", "匿名化された履歴タスクデータセット", "비식별 과거 작업 데이터 세트"),
        L("space.planningDataset.refresh", "刷新", "重新整理", "Refresh", "更新", "새로 고침"),
        L("space.planningDataset.guard", "只接受上游 SHA-256 token；数据集与回放结果永不写入生产。", "僅接受上游 SHA-256 token；資料集與回放結果永不寫入生產。", "Only upstream SHA-256 tokens are accepted; datasets and replay results never write to production.", "上流で生成した SHA-256 トークンのみ受け付け、データセットとリプレイ結果を本番へ書き込みません。", "업스트림 SHA-256 토큰만 허용하며 데이터 세트와 재생 결과는 운영 환경에 쓰지 않습니다."),
        L("space.planningDataset.dataset", "数据集", "資料集", "Dataset", "データセット", "데이터 세트"),
        L("space.planningDataset.taskCount", "任务数", "任務數", "Tasks", "タスク数", "작업 수"),
        L("space.planningDataset.window", "历史窗口", "歷史視窗", "Historical window", "履歴期間", "과거 기간"),
        L("space.planningDataset.replay", "确定性回放", "確定性回放", "Deterministic replay", "決定論的リプレイ", "결정론적 재생"),
        L("space.planningDataset.productionWrite", "生产写入", "生產寫入", "Production write", "本番書き込み", "운영 쓰기"),
        L("space.planningDataset.denied", "禁止", "禁止", "Denied", "禁止", "금지"),
        L("space.planningDataset.evidence", "证据", "證據", "Evidence", "証跡", "증거"),
        L("space.planningDataset.view", "查看", "檢視", "View", "表示", "보기"),
        L("space.planningDataset.empty", "该场景尚未导入历史任务数据集。", "此場景尚未匯入歷史任務資料集。", "No historical task dataset has been imported for this scenario.", "このシナリオには履歴タスクデータセットがまだありません。", "이 시나리오에는 아직 과거 작업 데이터 세트가 없습니다."),
        L("space.planningDataset.clock", "回放时钟", "回放時鐘", "Replay clock", "リプレイクロック", "재생 시계"),
        L("space.planningDataset.invalidGuard", "隔离失效", "隔離失效", "Isolation invalid", "分離が無効", "격리 무효"),
        L("space.planningDataset.noWriteback", "无生产回写", "不回寫生產", "No production writeback", "本番への書き戻しなし", "운영 환경 쓰기 없음"),
        L("space.planningDataset.importJson", "导入 JSON", "匯入 JSON", "Import JSON", "JSON をインポート", "JSON 가져오기"),
        L("space.planningDataset.importHint", "任务和人员标识必须在上传前转换为 64 位 SHA-256 token；最多 10,000 条任务。", "任務與人員識別碼必須在上傳前轉為 64 位 SHA-256 token；最多 10,000 筆任務。", "Task and worker identifiers must be converted to 64-character SHA-256 tokens before upload; maximum 10,000 tasks.", "タスクと作業者の識別子はアップロード前に64文字の SHA-256 トークンへ変換し、最大10,000件までです。", "작업 및 작업자 식별자는 업로드 전에 64자 SHA-256 토큰으로 변환해야 하며 최대 10,000개입니다."),
        L("space.planningDataset.chooseFile", "选择 JSON 文件", "選擇 JSON 檔案", "Choose JSON file", "JSON ファイルを選択", "JSON 파일 선택"),
        L("space.planningDataset.attestation", "我确认 taskToken / workerToken 已不可逆脱敏，内容不含订单、人员、物料或 SKU 原始标识。", "我確認 taskToken / workerToken 已不可逆去識別化，內容不含訂單、人員、物料或 SKU 原始識別碼。", "I confirm taskToken and workerToken were irreversibly de-identified and contain no raw order, person, material, or SKU identifiers.", "taskToken と workerToken が不可逆に匿名化され、注文・個人・資材・SKU の元識別子を含まないことを確認します。", "taskToken 및 workerToken이 비가역적으로 비식별화되었고 주문, 개인, 자재 또는 SKU 원본 식별자가 없음을 확인합니다."),
        L("space.planningDataset.create", "导入并固定回放时钟", "匯入並固定回放時鐘", "Import and pin replay clock", "インポートしてリプレイクロックを固定", "가져오기 및 재생 시계 고정"),
        L("space.planningDataset.created", "历史数据集已固定。", "歷史資料集已固定。", "The historical dataset is pinned.", "履歴データセットを固定しました。", "과거 데이터 세트를 고정했습니다."),
        L("space.planningDataset.duplicate", "相同数据集已存在。", "相同資料集已存在。", "The same dataset already exists.", "同じデータセットが既に存在します。", "동일한 데이터 세트가 이미 있습니다."),
        L("space.planningDataset.loadFailed", "无法加载历史数据集。", "無法載入歷史資料集。", "Unable to load historical datasets.", "履歴データセットを読み込めません。", "과거 데이터 세트를 불러올 수 없습니다."),
        L("space.planningDataset.invalidJson", "JSON 无效或不符合数据集契约。", "JSON 無效或不符合資料集契約。", "The JSON is invalid or does not match the dataset contract.", "JSON が無効か、データセット契約に一致しません。", "JSON이 유효하지 않거나 데이터 세트 계약과 일치하지 않습니다."),
        L("space.planningDataset.readFailed", "无法读取所选文件。", "無法讀取所選檔案。", "Unable to read the selected file.", "選択したファイルを読み込めません。", "선택한 파일을 읽을 수 없습니다."),
    ];

    private static Sys_Lang L(
        string key,
        string zhCN,
        string zhTW,
        string en,
        string ja,
        string ko) =>
        new()
        {
            LangKey = key,
            ZhCN = zhCN,
            ZhTW = zhTW,
            En = en,
            Ja = ja,
            Ko = ko,
        };
}
