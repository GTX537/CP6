using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E11-S02 上架/库位推荐画面词条。由 Program.cs 按租户幂等补齐。
/// </summary>
public static class I18nSpacePutawayRecommendationSeed
{
    public static readonly Sys_Lang[] Items =
    [
        new() { LangKey = "上架推荐", ZhCN = "上架推荐", ZhTW = "上架推薦", En = "Putaway recommendations", Ja = "棚入れ推奨", Ko = "적치 추천" },
        new() { LangKey = "生成推荐", ZhCN = "生成推荐", ZhTW = "產生推薦", En = "Generate recommendations", Ja = "推奨を生成", Ko = "추천 생성" },
        new() { LangKey = "生成中", ZhCN = "生成中", ZhTW = "產生中", En = "Generating", Ja = "生成中", Ko = "생성 중" },
        new() { LangKey = "物料", ZhCN = "物料", ZhTW = "物料", En = "Material", Ja = "品目", Ko = "자재" },
        new() { LangKey = "货主", ZhCN = "货主", ZhTW = "貨主", En = "Owner", Ja = "荷主", Ko = "화주" },
        new() { LangKey = "批次", ZhCN = "批次", ZhTW = "批次", En = "Lot", Ja = "ロット", Ko = "로트" },
        new() { LangKey = "入库数量", ZhCN = "入库数量", ZhTW = "入庫數量", En = "Inbound quantity", Ja = "入庫数量", Ko = "입고 수량" },
        new() { LangKey = "宽度要求（毫米）", ZhCN = "宽度要求（毫米）", ZhTW = "寬度要求（毫米）", En = "Required width (mm)", Ja = "必要幅（mm）", Ko = "필요 너비(mm)" },
        new() { LangKey = "高度要求（毫米）", ZhCN = "高度要求（毫米）", ZhTW = "高度要求（毫米）", En = "Required height (mm)", Ja = "必要高さ（mm）", Ko = "필요 높이(mm)" },
        new() { LangKey = "深度要求（毫米）", ZhCN = "深度要求（毫米）", ZhTW = "深度要求（毫米）", En = "Required depth (mm)", Ja = "必要奥行（mm）", Ko = "필요 깊이(mm)" },
        new() { LangKey = "最大承载要求", ZhCN = "最大承载要求", ZhTW = "最大承載要求", En = "Required maximum load", Ja = "必要最大荷重", Ko = "필요 최대 하중" },
        new() { LangKey = "最大候选数", ZhCN = "最大候选数", ZhTW = "最大候選數", En = "Maximum candidates", Ja = "最大候補数", Ko = "최대 후보 수" },
        new() { LangKey = "仅当前楼层", ZhCN = "仅当前楼层", ZhTW = "僅目前樓層", En = "Current floor only", Ja = "現在のフロアのみ", Ko = "현재 층만" },
        new() { LangKey = "允许精确库存合并", ZhCN = "允许精确库存合并", ZhTW = "允許精確庫存合併", En = "Allow exact stock consolidation", Ja = "完全一致在庫への統合を許可", Ko = "정확 일치 재고 병합 허용" },
        new() { LangKey = "推荐不会预留库位、移动库存或创建任务", ZhCN = "推荐不会预留库位、移动库存或创建任务", ZhTW = "推薦不會預留儲位、移動庫存或建立任務", En = "Recommendations do not reserve locations, move inventory, or create tasks", Ja = "推奨はロケーション予約、在庫移動、タスク作成を行いません", Ko = "추천은 로케이션 예약, 재고 이동 또는 작업 생성을 수행하지 않습니다" },
        new() { LangKey = "正在更新，当前显示上次成功推荐", ZhCN = "正在更新，当前显示上次成功推荐", ZhTW = "正在更新，目前顯示上次成功推薦", En = "Updating; showing the last successful recommendations", Ja = "更新中です。前回成功した推奨を表示しています", Ko = "업데이트 중이며 마지막 성공 추천을 표시합니다" },
        new() { LangKey = "尚无推荐结果", ZhCN = "尚无推荐结果", ZhTW = "尚無推薦結果", En = "No recommendation result yet", Ja = "推奨結果はまだありません", Ko = "추천 결과가 아직 없습니다" },
        new() { LangKey = "来源时点", ZhCN = "来源时点", ZhTW = "來源時點", En = "Source observation times", Ja = "ソース観測時刻", Ko = "소스 관측 시각" },
        new() { LangKey = "库存来源", ZhCN = "库存来源", ZhTW = "庫存來源", En = "Inventory source", Ja = "在庫ソース", Ko = "재고 소스" },
        new() { LangKey = "活动任务来源", ZhCN = "活动任务来源", ZhTW = "活動任務來源", En = "Active-task source", Ja = "アクティブタスクソース", Ko = "활성 작업 소스" },
        new() { LangKey = "候选库位", ZhCN = "候选库位", ZhTW = "候選儲位", En = "Candidate locations", Ja = "候補ロケーション", Ko = "후보 로케이션" },
        new() { LangKey = "当前数量", ZhCN = "当前数量", ZhTW = "目前數量", En = "Current quantity", Ja = "現在数量", Ko = "현재 수량" },
        new() { LangKey = "几何距离", ZhCN = "几何距离", ZhTW = "幾何距離", En = "Geometric distance", Ja = "幾何距離", Ko = "기하 거리" },
        new() { LangKey = "没有符合硬约束的候选库位", ZhCN = "没有符合硬约束的候选库位", ZhTW = "沒有符合硬性約束的候選儲位", En = "No candidate location satisfies the hard constraints", Ja = "必須制約を満たす候補ロケーションはありません", Ko = "필수 제약을 충족하는 후보 로케이션이 없습니다" },
        new() { LangKey = "候选结果已截断", ZhCN = "候选结果已截断", ZhTW = "候選結果已截斷", En = "Candidate results are truncated", Ja = "候補結果は省略されています", Ko = "후보 결과가 잘렸습니다" },
        new() { LangKey = "排除统计", ZhCN = "排除统计", ZhTW = "排除統計", En = "Exclusion counts", Ja = "除外集計", Ko = "제외 통계" },
        new() { LangKey = "排除样例", ZhCN = "排除样例", ZhTW = "排除範例", En = "Exclusion examples", Ja = "除外例", Ko = "제외 예시" },
        new() { LangKey = "排除样例已截断", ZhCN = "排除样例已截断", ZhTW = "排除範例已截斷", En = "Exclusion examples are truncated", Ja = "除外例は省略されています", Ko = "제외 예시가 잘렸습니다" },
        new() { LangKey = "限制说明", ZhCN = "限制说明", ZhTW = "限制說明", En = "Limitations", Ja = "制約事項", Ko = "제한 사항" },
        new() { LangKey = "ConsolidateExactStockIdentity", ZhCN = "精确库存身份合并", ZhTW = "精確庫存身分合併", En = "Exact stock-identity consolidation", Ja = "完全一致在庫への統合", Ko = "정확 재고 식별 병합" },
        new() { LangKey = "EmptyNearExistingStock", ZhCN = "靠近现有匹配库存的空库位", ZhTW = "鄰近既有匹配庫存的空儲位", En = "Empty location near matching stock", Ja = "一致在庫に近い空きロケーション", Ko = "일치 재고 인근 빈 로케이션" },
        new() { LangKey = "EmptyLocation", ZhCN = "空库位", ZhTW = "空儲位", En = "Empty location", Ja = "空きロケーション", Ko = "빈 로케이션" },
        new() { LangKey = "MISSING_SPATIAL_METADATA", ZhCN = "空间元数据缺失", ZhTW = "空間中繼資料缺失", En = "Spatial metadata missing", Ja = "空間メタデータ不足", Ko = "공간 메타데이터 누락" },
        new() { LangKey = "OUTSIDE_REQUESTED_SCOPE", ZhCN = "超出请求范围", ZhTW = "超出請求範圍", En = "Outside requested scope", Ja = "要求範囲外", Ko = "요청 범위 밖" },
        new() { LangKey = "ACTIVE_TASK_AT_OBSERVATION", ZhCN = "观测时存在活动任务", ZhTW = "觀測時存在活動任務", En = "Active task at observation time", Ja = "観測時にアクティブタスクあり", Ko = "관측 시 활성 작업 존재" },
        new() { LangKey = "INVALID_INVENTORY_QUANTITY", ZhCN = "库存数量无效", ZhTW = "庫存數量無效", En = "Invalid inventory quantity", Ja = "在庫数量が不正", Ko = "잘못된 재고 수량" },
        new() { LangKey = "WMS_SPACE_LOCATION_CODE_MISMATCH", ZhCN = "WMS 与 Space 库位代码不一致", ZhTW = "WMS 與 Space 儲位代碼不一致", En = "WMS and Space location codes differ", Ja = "WMS と Space のロケーションコード不一致", Ko = "WMS와 Space 로케이션 코드 불일치" },
        new() { LangKey = "OCCUPIED_WITH_INCOMPATIBLE_STOCK", ZhCN = "已有不兼容库存", ZhTW = "已有不相容庫存", En = "Occupied by incompatible stock", Ja = "不一致在庫で占有", Ko = "호환되지 않는 재고가 점유" },
        new() { LangKey = "PUBLISHED_DIMENSION_TOO_SMALL", ZhCN = "Published 尺寸不足", ZhTW = "Published 尺寸不足", En = "Published dimensions are too small", Ja = "Published 寸法が不足", Ko = "Published 치수 부족" },
        new() { LangKey = "PUBLISHED_MAX_LOAD_UNAVAILABLE", ZhCN = "Published 最大承载未知", ZhTW = "Published 最大承載未知", En = "Published maximum load is unavailable", Ja = "Published 最大荷重が不明", Ko = "Published 최대 하중 알 수 없음" },
        new() { LangKey = "PUBLISHED_MAX_LOAD_INSUFFICIENT", ZhCN = "Published 最大承载不足", ZhTW = "Published 最大承載不足", En = "Published maximum load is insufficient", Ja = "Published 最大荷重が不足", Ko = "Published 최대 하중 부족" },
        new() { LangKey = "上架推荐生成失败，保留上次成功结果", ZhCN = "上架推荐生成失败，保留上次成功结果", ZhTW = "上架推薦產生失敗，保留上次成功結果", En = "Putaway recommendation generation failed; keeping the last successful result", Ja = "棚入れ推奨の生成に失敗しました。前回成功した結果を保持します", Ko = "적치 추천 생성에 실패하여 마지막 성공 결과를 유지합니다" },
    ];
}
