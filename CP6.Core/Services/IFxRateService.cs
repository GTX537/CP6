using CP6.Entity.DomainModels;

namespace CP6.Core.Services;

/// <summary>
/// 為替レート（多通貨 Gap 4.3）。受注時のレート凍結と為替マスタ CRUD。
/// </summary>
public interface IFxRateService
{
    /// <summary>
    /// 得意先の取引通貨と、基準日時点の凍結レートを解決する。
    /// 通貨が基軸(JPY)/未設定なら (JPY, 1.0)。外貨かつ該当レート未登録は例外。
    /// </summary>
    Task<(string CurrencyCd, decimal Rate)> ResolveForCustomerAsync(string customerCd, DateTime asOf, CancellationToken ct = default);

    /// <summary>指定通貨・基準日以前の最新レートを取得（基軸は 1.0、未登録は null）。</summary>
    Task<decimal?> ResolveRateAsync(string currencyCd, DateTime asOf, CancellationToken ct = default);

    // ───────── マスタ CRUD ─────────
    Task<List<FxRate>> ListAsync(string? currencyCd = null, CancellationToken ct = default);
    Task<FxRate> CreateAsync(FxRate rate, string? userName, CancellationToken ct = default);
    Task UpdateAsync(Guid id, FxRate rate, string? userName, CancellationToken ct = default);
    Task DeleteAsync(Guid id, string? userName, CancellationToken ct = default);
}
