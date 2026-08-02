using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E12-S04 multi-scenario comparison and append-only decision text.
/// </summary>
public static class I18nSpacePlanningComparisonSeed
{
    public static readonly Sys_Lang[] Items =
    [
        L("space.planningComparison.title", "多场景比较与决策记录", "多場景比較與決策記錄", "Multi-scenario comparison and decisions", "複数シナリオの比較と意思決定", "다중 시나리오 비교 및 의사 결정"),
        L("space.planningComparison.refresh", "刷新", "重新整理", "Refresh", "更新", "새로 고침"),
        L("space.planningComparison.guard", "仅比较同源不可变仿真证据；不生成自动排名，人工决策也不会写入或发布到生产。", "僅比較同源不可變模擬證據；不產生自動排名，人工決策也不會寫入或發佈至生產。", "Only immutable simulation evidence from the same source is compared. No automatic ranking is produced, and human decisions never write or publish to production.", "同一ソースの不変なシミュレーション証跡のみを比較します。自動順位付けは行わず、人による意思決定を本番へ書き込みまたは公開しません。", "동일한 소스의 변경 불가능한 시뮬레이션 증거만 비교합니다. 자동 순위를 만들지 않으며 사람의 결정은 운영 환경에 기록되거나 게시되지 않습니다."),
        L("space.planningComparison.name", "例如：旺季方案评审", "例如：旺季方案評審", "For example: peak-season option review", "例：繁忙期案のレビュー", "예: 성수기 방안 검토"),
        L("space.planningComparison.runs", "选择 2–10 个不同场景运行", "選擇 2–10 個不同場景執行", "Select 2–10 runs from different scenarios", "異なるシナリオから2～10件の実行を選択", "서로 다른 시나리오 실행 2~10개 선택"),
        L("space.planningComparison.baseline", "人工指定基线", "人工指定基準", "Choose the baseline explicitly", "基準を明示的に選択", "기준선을 명시적으로 선택"),
        L("space.planningComparison.coverageThreshold", "最低距离覆盖率 %", "最低距離覆蓋率 %", "Minimum distance coverage %", "最小距離カバレッジ %", "최소 거리 포괄률 %"),
        L("space.planningComparison.capacityThreshold", "最高容量利用率 %", "最高容量使用率 %", "Maximum capacity utilization %", "最大容量使用率 %", "최대 용량 사용률 %"),
        L("space.planningComparison.congestionThreshold", "最高拥堵任务小时", "最高壅塞任務小時", "Maximum congested task-hours", "最大混雑タスク時間", "최대 혼잡 작업 시간"),
        L("space.planningComparison.costThreshold", "可选总成本上限", "可選總成本上限", "Optional total-cost limit", "任意の総コスト上限", "선택적 총비용 한도"),
        L("space.planningComparison.create", "固定比较证据", "固定比較證據", "Pin comparison evidence", "比較証跡を固定", "비교 증거 고정"),
        L("space.planningComparison.comparison", "比较", "比較", "Comparison", "比較", "비교"),
        L("space.planningComparison.optionCount", "方案数", "方案數", "Options", "案の数", "방안 수"),
        L("space.planningComparison.riskCount", "风险数", "風險數", "Risks", "リスク数", "위험 수"),
        L("space.planningComparison.currency", "币种", "幣別", "Currency", "通貨", "통화"),
        L("space.planningComparison.evidence", "证据", "證據", "Evidence", "証跡", "증거"),
        L("space.planningComparison.view", "查看", "檢視", "View", "表示", "보기"),
        L("space.planningComparison.empty", "尚未创建多场景比较。", "尚未建立多場景比較。", "No multi-scenario comparison has been created.", "複数シナリオの比較はまだ作成されていません。", "아직 다중 시나리오 비교가 없습니다."),
        L("space.planningComparison.hash", "比较哈希", "比較雜湊", "Comparison hash", "比較ハッシュ", "비교 해시"),
        L("space.planningComparison.invalidRanking", "存在自动排名", "存在自動排名", "Automatic ranking detected", "自動順位付けを検出", "자동 순위 감지됨"),
        L("space.planningComparison.noRanking", "无自动排名", "無自動排名", "No automatic ranking", "自動順位付けなし", "자동 순위 없음"),
        L("space.planningComparison.invalidWrite", "隔离失效", "隔離失效", "Isolation invalid", "分離が無効", "격리 무효"),
        L("space.planningComparison.noWriteback", "无生产回写", "不回寫生產", "No production writeback", "本番への書き戻しなし", "운영 환경 쓰기 없음"),
        L("space.planningComparison.option", "方案", "方案", "Option", "案", "방안"),
        L("space.planningComparison.baselineTag", "基线", "基準", "Baseline", "基準", "기준선"),
        L("space.planningComparison.distance", "距离 / Δm", "距離 / Δm", "Distance / Δm", "距離 / Δm", "거리 / Δm"),
        L("space.planningComparison.congestion", "拥堵小时 / Δ秒", "壅塞小時 / Δ秒", "Congested hours / Δseconds", "混雑時間 / Δ秒", "혼잡 시간 / Δ초"),
        L("space.planningComparison.capacity", "峰值容量 / 超载", "尖峰容量 / 超載", "Peak capacity / overloads", "ピーク容量 / 過負荷", "최대 용량 / 과부하"),
        L("space.planningComparison.throughput", "平均吞吐 / Δ", "平均吞吐 / Δ", "Average throughput / Δ", "平均スループット / Δ", "평균 처리량 / Δ"),
        L("space.planningComparison.cost", "成本 / Δ", "成本 / Δ", "Cost / Δ", "コスト / Δ", "비용 / Δ"),
        L("space.planningComparison.risks", "阈值风险", "閾值風險", "Threshold risks", "しきい値リスク", "임계값 위험"),
        L("space.planningComparison.decisionTitle", "人工决策记录", "人工決策記錄", "Human decision record", "人による意思決定記録", "사람의 의사 결정 기록"),
        L("space.planningComparison.decisionGuard", "新决策会追加并引用当前记录，不会覆盖历史，也不会触发生产操作。", "新決策會追加並引用目前記錄，不會覆寫歷史，也不會觸發生產操作。", "A new decision is appended and references the current record. It never overwrites history or triggers a production operation.", "新しい意思決定は追記され、現在の記録を参照します。履歴を上書きせず、本番操作も実行しません。", "새 결정은 현재 기록을 참조하여 추가됩니다. 이력을 덮어쓰거나 운영 작업을 실행하지 않습니다."),
        L("space.planningComparison.selected", "选择方案", "選擇方案", "Select option", "案を選択", "방안 선택"),
        L("space.planningComparison.deferred", "暂缓", "暫緩", "Defer", "保留", "보류"),
        L("space.planningComparison.rejectedAll", "全部否决", "全部否決", "Reject all", "すべて却下", "모두 거부"),
        L("space.planningComparison.selectedOption", "选择方案", "選擇方案", "Selected option", "選択する案", "선택할 방안"),
        L("space.planningComparison.rationale", "记录取舍依据、风险接受条件和后续动作", "記錄取捨依據、風險接受條件及後續動作", "Record trade-offs, risk acceptance, and follow-up actions", "トレードオフ、リスク受容条件、次のアクションを記録", "절충 근거, 위험 수용 조건 및 후속 조치 기록"),
        L("space.planningComparison.recordDecision", "追加决策记录", "追加決策記錄", "Append decision record", "意思決定記録を追記", "의사 결정 기록 추가"),
        L("space.planningComparison.supersedes", "替代", "取代", "Supersedes", "置き換え対象", "대체 대상"),
        L("space.planningComparison.loadFailed", "无法加载场景比较。", "無法載入場景比較。", "Unable to load scenario comparisons.", "シナリオ比較を読み込めません。", "시나리오 비교를 불러올 수 없습니다."),
        L("space.planningComparison.duplicate", "相同比较已存在。", "相同比較已存在。", "The same comparison already exists.", "同じ比較が既に存在します。", "동일한 비교가 이미 있습니다."),
        L("space.planningComparison.created", "比较证据已固定。", "比較證據已固定。", "Comparison evidence is pinned.", "比較証跡を固定しました。", "비교 증거가 고정되었습니다."),
        L("space.planningComparison.createFailed", "无法创建场景比较。", "無法建立場景比較。", "Unable to create the scenario comparison.", "シナリオ比較を作成できません。", "시나리오 비교를 만들 수 없습니다."),
        L("space.planningComparison.decisionDuplicate", "相同决策记录已存在。", "相同決策記錄已存在。", "The same decision record already exists.", "同じ意思決定記録が既に存在します。", "동일한 의사 결정 기록이 이미 있습니다."),
        L("space.planningComparison.decisionCreated", "人工决策已追加。", "人工決策已追加。", "The human decision was appended.", "人による意思決定を追記しました。", "사람의 의사 결정이 추가되었습니다."),
        L("space.planningComparison.decisionFailed", "无法记录人工决策。", "無法記錄人工決策。", "Unable to record the human decision.", "人による意思決定を記録できません。", "사람의 의사 결정을 기록할 수 없습니다."),
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
