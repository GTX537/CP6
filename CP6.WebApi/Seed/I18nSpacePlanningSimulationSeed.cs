using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E12-S03 deterministic planning simulation screen text.
/// </summary>
public static class I18nSpacePlanningSimulationSeed
{
    public static readonly Sys_Lang[] Items =
    [
        L("space.planningSimulation.title", "场景仿真", "場景模擬", "Scenario simulation", "シナリオシミュレーション", "시나리오 시뮬레이션"),
        L("space.planningSimulation.refresh", "刷新", "重新整理", "Refresh", "更新", "새로 고침"),
        L("space.planningSimulation.guard", "结果仅用于隔离规划：直线几何不是通道路线，仿真永不写入或发布到生产。", "結果僅用於隔離規劃：直線幾何不是通道路線，模擬永不寫入或發佈至生產。", "Results are for isolated planning only: straight-line geometry is not an aisle route, and simulations never write or publish to production.", "結果は分離された計画専用です。直線距離は通路経路ではなく、シミュレーションを本番へ書き込みまたは公開しません。", "결과는 격리된 계획 전용입니다. 직선 기하는 통로 경로가 아니며 시뮬레이션은 운영 환경에 기록되거나 게시되지 않습니다."),
        L("space.planningSimulation.name", "例如：旺季容量基线", "例如：旺季容量基準", "For example: peak-season capacity baseline", "例：繁忙期の容量ベースライン", "예: 성수기 용량 기준선"),
        L("space.planningSimulation.dataset", "历史数据集", "歷史資料集", "Historical dataset", "履歴データセット", "과거 데이터 세트"),
        L("space.planningSimulation.quantityCapacity", "默认数量容量", "預設數量容量", "Default quantity capacity", "既定の数量容量", "기본 수량 용량"),
        L("space.planningSimulation.concurrentCapacity", "默认并发任务容量", "預設並行任務容量", "Default concurrent-task capacity", "既定の同時タスク容量", "기본 동시 작업 용량"),
        L("space.planningSimulation.windowMinutes", "吞吐窗口（分钟）", "吞吐視窗（分鐘）", "Throughput window (minutes)", "スループット枠（分）", "처리량 창(분)"),
        L("space.planningSimulation.currency", "币种，例如 CNY", "幣別，例如 CNY", "Currency, for example CNY", "通貨（例：CNY）", "통화(예: CNY)"),
        L("space.planningSimulation.distanceRate", "每米成本", "每公尺成本", "Cost per meter", "1メートル当たりのコスト", "미터당 비용"),
        L("space.planningSimulation.laborRate", "每工时成本", "每工時成本", "Cost per labor hour", "1作業時間当たりのコスト", "노동 시간당 비용"),
        L("space.planningSimulation.congestionRate", "每拥堵任务小时成本", "每壅塞任務小時成本", "Cost per congested task-hour", "混雑タスク時間当たりのコスト", "혼잡 작업 시간당 비용"),
        L("space.planningSimulation.capacityOverrides", "可选位置容量覆盖 JSON：[{\"locationLogicalId\":\"...\",\"quantityCapacity\":100,\"concurrentTaskCapacity\":2}]", "可選位置容量覆寫 JSON：[{\"locationLogicalId\":\"...\",\"quantityCapacity\":100,\"concurrentTaskCapacity\":2}]", "Optional location-capacity override JSON: [{\"locationLogicalId\":\"...\",\"quantityCapacity\":100,\"concurrentTaskCapacity\":2}]", "任意のロケーション容量上書き JSON：[{\"locationLogicalId\":\"...\",\"quantityCapacity\":100,\"concurrentTaskCapacity\":2}]", "선택적 위치 용량 재정의 JSON: [{\"locationLogicalId\":\"...\",\"quantityCapacity\":100,\"concurrentTaskCapacity\":2}]"),
        L("space.planningSimulation.create", "运行并固定证据", "執行並固定證據", "Run and pin evidence", "実行して証跡を固定", "실행 및 증거 고정"),
        L("space.planningSimulation.run", "仿真运行", "模擬執行", "Simulation run", "シミュレーション実行", "시뮬레이션 실행"),
        L("space.planningSimulation.distance", "距离", "距離", "Distance", "距離", "거리"),
        L("space.planningSimulation.overloaded", "超载位置", "超載位置", "Overloaded locations", "過負荷ロケーション", "과부하 위치"),
        L("space.planningSimulation.throughput", "平均吞吐", "平均吞吐", "Average throughput", "平均スループット", "평균 처리량"),
        L("space.planningSimulation.cost", "总成本", "總成本", "Total cost", "総コスト", "총비용"),
        L("space.planningSimulation.evidence", "证据", "證據", "Evidence", "証跡", "증거"),
        L("space.planningSimulation.view", "查看", "檢視", "View", "表示", "보기"),
        L("space.planningSimulation.empty", "该场景尚无仿真运行。", "此場景尚無模擬執行。", "This scenario has no simulation runs yet.", "このシナリオにはシミュレーション実行がまだありません。", "이 시나리오에는 아직 시뮬레이션 실행이 없습니다."),
        L("space.planningSimulation.invalidGuard", "隔离失效", "隔離失效", "Isolation invalid", "分離が無効", "격리 무효"),
        L("space.planningSimulation.noWriteback", "无生产回写", "不回寫生產", "No production writeback", "本番への書き戻しなし", "운영 환경 쓰기 없음"),
        L("space.planningSimulation.coverage", "覆盖", "覆蓋", "coverage", "カバレッジ", "포괄률"),
        L("space.planningSimulation.congestion", "拥堵", "壅塞", "Congestion", "混雑", "혼잡"),
        L("space.planningSimulation.peakConcurrent", "峰值并发", "尖峰並行", "Peak concurrency", "ピーク同時数", "최대 동시 수"),
        L("space.planningSimulation.capacity", "容量", "容量", "Capacity", "容量", "용량"),
        L("space.planningSimulation.peak", "峰值", "尖峰", "peak", "ピーク", "최대"),
        L("space.planningSimulation.estimate", "参数化估算", "參數化估算", "Parameterized estimate", "パラメータ化された見積り", "매개변수 기반 추정"),
        L("space.planningSimulation.location", "位置", "位置", "Location", "ロケーション", "위치"),
        L("space.planningSimulation.utilization", "容量利用率", "容量使用率", "Capacity utilization", "容量使用率", "용량 사용률"),
        L("space.planningSimulation.congestionSeconds", "拥堵秒数", "壅塞秒數", "Congestion seconds", "混雑秒数", "혼잡 초"),
        L("space.planningSimulation.state", "状态", "狀態", "Status", "状態", "상태"),
        L("space.planningSimulation.overload", "超载", "超載", "Overloaded", "過負荷", "과부하"),
        L("space.planningSimulation.withinCapacity", "容量内", "容量內", "Within capacity", "容量内", "용량 이내"),
        L("space.planningSimulation.boundary", "直线货架格口距离、历史任务窗口重叠和调用方容量/费率共同构成结果；不代表通道寻路、高精度物理或财务实际值。", "直線貨架格口距離、歷史任務視窗重疊與呼叫方容量/費率共同構成結果；不代表通道尋路、高精度物理或財務實際值。", "Results combine straight-line rack-cell distance, historical task-window overlap, and caller-provided capacities and rates; they are not aisle routing, high-precision physics, or financial actuals.", "結果はラックセル間の直線距離、履歴タスク期間の重なり、呼び出し元の容量と単価で構成され、通路経路、高精度物理、財務実績を示しません。", "결과는 랙 셀 직선 거리, 과거 작업 창 중첩, 호출자가 제공한 용량과 요율을 결합하며 통로 경로, 고정밀 물리 또는 실제 재무 값을 의미하지 않습니다."),
        L("space.planningSimulation.loadFailed", "无法加载仿真运行。", "無法載入模擬執行。", "Unable to load simulation runs.", "シミュレーション実行を読み込めません。", "시뮬레이션 실행을 불러올 수 없습니다."),
        L("space.planningSimulation.duplicate", "相同仿真运行已存在。", "相同模擬執行已存在。", "The same simulation run already exists.", "同じシミュレーション実行が既に存在します。", "동일한 시뮬레이션 실행이 이미 있습니다."),
        L("space.planningSimulation.created", "仿真证据已固定。", "模擬證據已固定。", "Simulation evidence is pinned.", "シミュレーション証跡を固定しました。", "시뮬레이션 증거가 고정되었습니다."),
        L("space.planningSimulation.invalid", "仿真参数或位置容量 JSON 无效。", "模擬參數或位置容量 JSON 無效。", "Simulation parameters or location-capacity JSON are invalid.", "シミュレーションパラメータまたはロケーション容量 JSON が無効です。", "시뮬레이션 매개변수 또는 위치 용량 JSON이 유효하지 않습니다."),
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
