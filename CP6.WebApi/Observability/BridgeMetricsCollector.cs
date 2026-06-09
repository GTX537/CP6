using CP6.Core.Services;
using Prometheus;

namespace CP6.WebApi.Observability;

/// <summary>
/// ブリッジ／IntegrationEvent の業務指標を Prometheus へ公開する（T15 / Gap 2.3）。
/// </summary>
/// <remarks>
/// 設計：
///   - 値の真実は <c>T_IntegrationEvent</c> テーブル。scrape 毎に <see cref="IBridgeMetricsSnapshotProvider"/>
///     で集計し直すため、プロセス再起動でカウンタがリセットされない（DB が持久化担当）。
///   - prometheus-net の BeforeCollect コールバックで遅延評価。Scrape が来た時だけ DB を叩く。
///   - 集計失敗（DB 断など）は飲み込み、/metrics 全体は 200 を返す（他の HTTP 指標は維持）。
///
/// 公開指標：
///   - <c>cp6_bridge_hook_total{hook,status}</c>        … Hook×状態 ごとのイベント件数（Gauge）
///   - <c>cp6_bridge_retry_queue_depth</c>              … FAILED（リトライ待ち）件数
///   - <c>cp6_integration_event_dead_letter_total</c>   … DEAD（死信）件数
/// </remarks>
public sealed class BridgeMetricsCollector
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BridgeMetricsCollector> _logger;

    private readonly Gauge _hookTotal;
    private readonly Gauge _retryQueueDepth;
    private readonly Gauge _deadLetterTotal;

    // 前回 scrape で公開した (hook,status) ラベル集合。今回消えた組合せを 0 に落とすため保持。
    private readonly HashSet<(string Hook, string Status)> _publishedLabels = new();
    private readonly object _sync = new();

    public BridgeMetricsCollector(
        IServiceScopeFactory scopeFactory,
        ILogger<BridgeMetricsCollector> logger,
        IMetricFactory? metricFactory = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var factory = metricFactory ?? Metrics.DefaultFactory;

        _hookTotal = factory.CreateGauge(
            "cp6_bridge_hook_total",
            "跨模块 Bridge Hook イベント件数（Hook 名×終端状態ごと）",
            new GaugeConfiguration { LabelNames = new[] { "hook", "status" } });

        _retryQueueDepth = factory.CreateGauge(
            "cp6_bridge_retry_queue_depth",
            "リトライ待ち（Status=FAILED かつ NextRetryAt 設定済）IntegrationEvent 件数");

        _deadLetterTotal = factory.CreateGauge(
            "cp6_integration_event_dead_letter_total",
            "死信（Status=DEAD）転落 IntegrationEvent 件数。> 0 は人手介入が必要");
    }

    /// <summary>
    /// 既定レジストリの BeforeCollect に集計コールバックを登録する。
    /// Program.cs の起動時に 1 回だけ呼ぶ。
    /// </summary>
    public void Register()
    {
        Metrics.DefaultRegistry.AddBeforeCollectCallback(async ct =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var provider = scope.ServiceProvider.GetRequiredService<IBridgeMetricsSnapshotProvider>();
                var snapshot = await provider.GetSnapshotAsync(ct);

                lock (_sync)
                {
                    var current = new HashSet<(string, string)>();
                    foreach (var row in snapshot.HookStatusCounts)
                    {
                        _hookTotal.WithLabels(row.Hook, row.Status).Set(row.Count);
                        current.Add((row.Hook, row.Status));
                    }

                    // 今回 DB に存在しなくなったラベル組合せは 0 にして古い値の残留を防ぐ。
                    foreach (var stale in _publishedLabels)
                    {
                        if (!current.Contains(stale))
                        {
                            _hookTotal.WithLabels(stale.Hook, stale.Status).Set(0);
                        }
                    }

                    _publishedLabels.Clear();
                    _publishedLabels.UnionWith(current);
                }

                _retryQueueDepth.Set(snapshot.RetryQueueDepth);
                _deadLetterTotal.Set(snapshot.DeadLetterCount);
            }
            catch (Exception ex)
            {
                // /metrics 自体は壊さない（他の HTTP 指標は配信継続）。
                _logger.LogWarning(ex, "Bridge metrics scrape aggregation failed");
            }
        });
    }
}
