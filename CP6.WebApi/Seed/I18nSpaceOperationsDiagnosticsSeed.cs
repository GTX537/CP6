using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// Space E11-S01 运营诊断画面词条。由 Program.cs 按租户幂等补齐。
/// </summary>
public static class I18nSpaceOperationsDiagnosticsSeed
{
    public static readonly Sys_Lang[] Items =
    [
        new() { LangKey = "最近 1 小时", ZhCN = "最近 1 小时", ZhTW = "最近 1 小時", En = "Last 1 hour", Ja = "直近1時間", Ko = "최근 1시간" },
        new() { LangKey = "最近 8 小时", ZhCN = "最近 8 小时", ZhTW = "最近 8 小時", En = "Last 8 hours", Ja = "直近8時間", Ko = "최근 8시간" },
        new() { LangKey = "最近 24 小时", ZhCN = "最近 24 小时", ZhTW = "最近 24 小時", En = "Last 24 hours", Ja = "直近24時間", Ko = "최근 24시간" },
        new() { LangKey = "分层占用", ZhCN = "分层占用", ZhTW = "分層佔用", En = "Occupancy by floor", Ja = "フロア別占有率", Ko = "층별 점유" },
        new() { LangKey = "次", ZhCN = "次", ZhTW = "次", En = "occurrences", Ja = "回", Ko = "회" },
        new() { LangKey = "尚无诊断结果", ZhCN = "尚无诊断结果", ZhTW = "尚無診斷結果", En = "No diagnostic result yet", Ja = "診断結果はまだありません", Ko = "진단 결과가 아직 없습니다" },
        new() { LangKey = "真实容量利用率", ZhCN = "真实容量利用率", ZhTW = "真實容量利用率", En = "Physical capacity utilization", Ja = "実容量使用率", Ko = "실제 용량 이용률" },
        new() { LangKey = "人", ZhCN = "人", ZhTW = "人", En = "people", Ja = "人", Ko = "명" },
        new() { LangKey = "人重叠观测", ZhCN = "人重叠观测", ZhTW = "人員重疊觀測", En = "overlapping people observed", Ja = "人の重複観測", Ko = "인원 중첩 관측" },
        new() { LangKey = "人员最后观测", ZhCN = "人员最后观测", ZhTW = "人員最後觀測", En = "Last personnel observation", Ja = "人員の最終観測", Ko = "인원 최종 관측" },
        new() { LangKey = "人员证据", ZhCN = "人员证据", ZhTW = "人員證據", En = "Personnel evidence", Ja = "人員エビデンス", Ko = "인원 근거" },
        new() { LangKey = "正在更新，当前显示上次成功结果", ZhCN = "正在更新，当前显示上次成功结果", ZhTW = "正在更新，目前顯示上次成功結果", En = "Updating; showing the last successful result", Ja = "更新中です。前回成功した結果を表示しています", Ko = "업데이트 중이며 마지막 성공 결과를 표시합니다" },
        new() { LangKey = "正在计算运营诊断", ZhCN = "正在计算运营诊断", ZhTW = "正在計算營運診斷", En = "Calculating operations diagnostics", Ja = "運用診断を計算しています", Ko = "운영 진단을 계산하고 있습니다" },
        new() { LangKey = "折返", ZhCN = "折返", ZhTW = "折返", En = "Backtracks", Ja = "折り返し", Ko = "되돌아감" },
        new() { LangKey = "折返证据", ZhCN = "折返证据", ZhTW = "折返證據", En = "Backtrack evidence", Ja = "折り返しエビデンス", Ko = "되돌아감 근거" },
        new() { LangKey = "停留", ZhCN = "停留", ZhTW = "停留", En = "Dwell", Ja = "滞留", Ko = "체류" },
        new() { LangKey = "停留热点", ZhCN = "停留热点", ZhTW = "停留熱點", En = "Dwell hotspots", Ja = "滞留ホットスポット", Ko = "체류 핫스팟" },
        new() { LangKey = "排除当前模型外事件", ZhCN = "排除当前模型外事件", ZhTW = "排除目前模型外事件", En = "Excluded events outside the current model", Ja = "現行モデル外のイベントを除外", Ko = "현재 모델 외 이벤트 제외" },
        new() { LangKey = "排除模拟事件", ZhCN = "排除模拟事件", ZhTW = "排除模擬事件", En = "Excluded simulated events", Ja = "シミュレーションイベントを除外", Ko = "시뮬레이션 이벤트 제외" },
        new() { LangKey = "分析", ZhCN = "分析", ZhTW = "分析", En = "Analyze", Ja = "分析", Ko = "분석" },
        new() { LangKey = "峰值", ZhCN = "峰值", ZhTW = "峰值", En = "Peak", Ja = "ピーク", Ko = "최대" },
        new() { LangKey = "未知段", ZhCN = "未知段", ZhTW = "未知區段", En = "unknown segments", Ja = "不明区間", Ko = "알 수 없는 구간" },
        new() { LangKey = "有效真实事件", ZhCN = "有效真实事件", ZhTW = "有效真實事件", En = "eligible real events", Ja = "有効な実イベント", Ko = "유효한 실제 이벤트" },
        new() { LangKey = "已知段", ZhCN = "已知段", ZhTW = "已知區段", En = "known segments", Ja = "既知区間", Ko = "알려진 구간" },
        new() { LangKey = "窗口内无重叠库位观测", ZhCN = "窗口内无重叠库位观测", ZhTW = "視窗內無重疊儲位觀測", En = "No overlapping location observations in this window", Ja = "期間内にロケーションの重複観測はありません", Ko = "기간 내 중첩 로케이션 관측이 없습니다" },
        new() { LangKey = "窗口内无达到阈值的停留", ZhCN = "窗口内无达到阈值的停留", ZhTW = "視窗內無達到閾值的停留", En = "No dwell reached the threshold in this window", Ja = "期間内にしきい値以上の滞留はありません", Ko = "기간 내 임계값에 도달한 체류가 없습니다" },
        new() { LangKey = "库位", ZhCN = "库位", ZhTW = "儲位", En = "locations", Ja = "ロケーション", Ko = "로케이션" },
        new() { LangKey = "库位占用不等于体积、重量或托盘容量", ZhCN = "库位占用不等于体积、重量或托盘容量", ZhTW = "儲位佔用不等於體積、重量或棧板容量", En = "Location occupancy is not volume, weight, or pallet capacity", Ja = "ロケーション占有率は容積・重量・パレット容量ではありません", Ko = "로케이션 점유율은 부피, 중량 또는 팔레트 용량이 아닙니다" },
        new() { LangKey = "库位占用压力", ZhCN = "库位占用压力", ZhTW = "儲位佔用壓力", En = "Location occupancy pressure", Ja = "ロケーション占有圧力", Ko = "로케이션 점유 압력" },
        new() { LangKey = "库存最后观测", ZhCN = "库存最后观测", ZhTW = "庫存最後觀測", En = "Last inventory observation", Ja = "在庫の最終観測", Ko = "재고 최종 관측" },
        new() { LangKey = "拥堵热点", ZhCN = "拥堵热点", ZhTW = "壅塞熱點", En = "Congestion hotspots", Ja = "混雑ホットスポット", Ko = "혼잡 핫스팟" },
        new() { LangKey = "拥堵观测", ZhCN = "拥堵观测", ZhTW = "壅塞觀測", En = "Observed congestion", Ja = "混雑観測", Ko = "혼잡 관측" },
        new() { LangKey = "观测路径", ZhCN = "观测路径", ZhTW = "觀測路徑", En = "Observed path", Ja = "観測経路", Ko = "관측 경로" },
        new() { LangKey = "运营诊断", ZhCN = "运营诊断", ZhTW = "營運診斷", En = "Operations diagnostics", Ja = "運用診断", Ko = "운영 진단" },
        new() { LangKey = "运营诊断加载失败，保留上次成功结果", ZhCN = "运营诊断加载失败，保留上次成功结果", ZhTW = "營運診斷載入失敗，保留上次成功結果", En = "Operations diagnostics failed; keeping the last successful result", Ja = "運用診断の取得に失敗しました。前回成功した結果を保持します", Ko = "운영 진단 로드에 실패하여 마지막 성공 결과를 유지합니다" },
    ];
}
