using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 出庫の多倉庫ルーティング（Gap 4.2 / T14）。
/// </summary>
/// <remarks>
/// 引当時、1 明細につき「どの倉庫から順に在庫を探すか」を決定する。
/// 候補順：① マッチした <see cref="OutboundRoutingRule"/>（SortOrder 昇順）の優先倉庫
///         → ② 残り倉庫を <c>Warehouse.OutboundPriority</c> 昇順
///         → ③ フォールバック倉庫（ヘッダ倉庫）。
/// 各倉庫内の FEFO は <c>OutboundService</c> 側で適用する。
/// </remarks>
public interface IOutboundRoutingService
{
    /// <summary>
    /// 指定条件で引当候補倉庫CDを優先順に解決する（重複除去・順序保持）。
    /// </summary>
    /// <param name="customerCd">得意先CD（出荷時）。null 可</param>
    /// <param name="productCd">製品CD（ルールの接頭辞条件評価に使用）</param>
    /// <param name="outboundType">出庫区分（<see cref="OutboundType"/>）</param>
    /// <param name="fallbackWarehouseCd">最終フォールバック倉庫（通常ヘッダの WarehouseCd）</param>
    Task<List<string>> ResolveCandidateWarehousesAsync(
        string? customerCd,
        string productCd,
        int outboundType,
        string fallbackWarehouseCd,
        CancellationToken ct = default);

    // ───────── ルール CRUD（管理画面用） ─────────
    Task<List<OutboundRoutingRule>> ListRulesAsync(bool includeDisabled = true, CancellationToken ct = default);
    Task<OutboundRoutingRule> CreateRuleAsync(OutboundRoutingRule rule, string? userName, CancellationToken ct = default);
    Task UpdateRuleAsync(Guid id, OutboundRoutingRule rule, string? userName, CancellationToken ct = default);
    Task DeleteRuleAsync(Guid id, string? userName, CancellationToken ct = default);
}
