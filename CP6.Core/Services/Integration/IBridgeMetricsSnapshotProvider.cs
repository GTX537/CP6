using CP6.Entity.DTOs;

namespace CP6.Core.Services.Integration;

/// <summary>
/// IntegrationEvent テーブルから Prometheus 指標スナップショットを集計する（T15 / Gap 2.3）。
/// </summary>
/// <remarks>
/// WebApi 層の Prometheus コレクタが scrape 毎に呼ぶ薄いクエリサービス。
/// prometheus-net への依存を Core に持ち込まないため、集計ロジックのみをここに分離（単体テスト可能）。
/// </remarks>
public interface IBridgeMetricsSnapshotProvider
{
    /// <summary>
    /// 論理削除を除く全 IntegrationEvent を (HookName, Status) で集計し、
    /// リトライ待ち件数・死信件数を併せて返す。
    /// </summary>
    Task<BridgeMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
