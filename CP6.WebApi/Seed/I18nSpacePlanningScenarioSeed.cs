using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E12-S01 production-isolated planning scenario screen text.
/// </summary>
public static class I18nSpacePlanningScenarioSeed
{
    public static readonly Sys_Lang[] Items =
    [
        L("space.planningScenario.pageTitle", "规划方案", "規劃方案", "Planning scenarios", "計画シナリオ", "계획 시나리오"),
        L("space.planningScenario.chooseSite", "选择站点", "選擇站點", "Select a site", "サイトを選択", "사이트 선택"),
        L("space.planningScenario.chooseSiteFirst", "请先选择一个站点。", "請先選擇一個站點。", "Select a site first.", "先にサイトを選択してください。", "먼저 사이트를 선택하세요."),
        L("space.planningScenario.title", "生产隔离的规划分支", "生產隔離的規劃分支", "Production-isolated planning branches", "本番環境から分離された計画ブランチ", "운영 환경과 격리된 계획 브랜치"),
        L("space.planningScenario.refresh", "刷新", "重新整理", "Refresh", "更新", "새로 고침"),
        L("space.planningScenario.isolation", "场景固定在当前生产快照，但不会占用生产草稿，也不能进入生产发布流程。", "場景固定在目前生產快照，但不會占用生產草稿，也不能進入生產發布流程。", "The scenario is pinned to the current production snapshot, but never takes the production draft slot or enters the production publish lifecycle.", "シナリオは現在の本番スナップショットに固定されますが、本番ドラフト枠を使用せず、本番公開ライフサイクルにも入りません。", "시나리오는 현재 운영 스냅샷에 고정되지만 운영 초안 슬롯을 사용하거나 운영 게시 수명 주기에 진입하지 않습니다."),
        L("space.planningScenario.name", "例如：旺季容量方案", "例如：旺季容量方案", "For example: peak-season capacity", "例：繁忙期の容量計画", "예: 성수기 용량 계획"),
        L("space.planningScenario.create", "创建场景分支", "建立場景分支", "Create scenario branch", "シナリオブランチを作成", "시나리오 브랜치 만들기"),
        L("space.planningScenario.noBase", "站点尚无可固定的当前生产版本。", "站點尚無可固定的目前生產版本。", "The site has no current production version to pin.", "固定できる現在の本番バージョンがありません。", "고정할 현재 운영 버전이 없습니다."),
        L("space.planningScenario.branch", "场景", "場景", "Scenario", "シナリオ", "시나리오"),
        L("space.planningScenario.lineage", "固定来源", "固定來源", "Pinned source", "固定元", "고정 원본"),
        L("space.planningScenario.version", "场景版本", "場景版本", "Scenario version", "シナリオバージョン", "시나리오 버전"),
        L("space.planningScenario.status", "状态", "狀態", "Status", "状態", "상태"),
        L("space.planningScenario.cloneJob", "克隆任务", "複製工作", "Clone job", "クローンジョブ", "복제 작업"),
        L("space.planningScenario.guard", "生产隔离", "生產隔離", "Production isolation", "本番分離", "운영 격리"),
        L("space.planningScenario.history", "历史数据", "歷史資料", "Historical data", "履歴データ", "과거 데이터"),
        L("space.planningScenario.openHistory", "打开", "開啟", "Open", "開く", "열기"),
        L("space.planningScenario.collapseHistory", "收起", "收合", "Collapse", "閉じる", "접기"),
        L("space.planningScenario.empty", "尚未创建规划场景。", "尚未建立規劃場景。", "No planning scenario has been created.", "計画シナリオはまだ作成されていません。", "아직 생성된 계획 시나리오가 없습니다."),
        L("space.planningScenario.loadFailed", "无法加载规划场景。", "無法載入規劃場景。", "Unable to load planning scenarios.", "計画シナリオを読み込めません。", "계획 시나리오를 불러올 수 없습니다."),
        L("space.planningScenario.duplicate", "已存在相同场景分支。", "已存在相同場景分支。", "The same scenario branch already exists.", "同じシナリオブランチが既に存在します。", "동일한 시나리오 브랜치가 이미 있습니다."),
        L("space.planningScenario.created", "规划场景已进入克隆队列。", "規劃場景已進入複製佇列。", "The planning scenario has entered the clone queue.", "計画シナリオをクローンキューに追加しました。", "계획 시나리오가 복제 대기열에 추가되었습니다."),
        L("space.planningScenario.createFailed", "无法创建规划场景。", "無法建立規劃場景。", "Unable to create the planning scenario.", "計画シナリオを作成できません。", "계획 시나리오를 만들 수 없습니다."),
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
