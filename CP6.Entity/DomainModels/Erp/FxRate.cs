using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 為替レートマスタ（多通貨 Gap 4.3）。
/// </summary>
/// <remarks>
/// <see cref="Rate"/> は「外貨1単位あたりの基軸通貨額」。基軸通貨は <see cref="FxConstants.BaseCurrency"/>（JPY）。
/// 例：USD の Rate=150 → 1 USD = 150 JPY。基軸通貨換算額 = 外貨額 × Rate。
/// 受注時に得意先通貨の当日（基準日以前の最新）レートを <see cref="Order.FxRate"/> へ凍結する。
/// </remarks>
[Table("T_FxRate")]
public class FxRate : BaseBizEntity
{
    /// <summary>通貨CD（ISO 4217、例 USD/EUR/CNY）</summary>
    [Required, MaxLength(3)]
    public string CurrencyCd { get; set; } = string.Empty;

    /// <summary>レート適用日（この日以降、次のレート登録日まで有効）</summary>
    public DateTime RateDate { get; set; }

    /// <summary>外貨1単位あたりの基軸通貨額（JPY/外貨）</summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal Rate { get; set; }

    /// <summary>備考</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}

/// <summary>多通貨の基軸・定数（Gap 4.3）。</summary>
public static class FxConstants
{
    /// <summary>基軸通貨（日本企業のため JPY）。得意先通貨が未設定/JPY の場合は凍結レート 1.0。</summary>
    public const string BaseCurrency = "JPY";

    public static bool IsBase(string? currencyCd)
        => string.IsNullOrWhiteSpace(currencyCd)
           || string.Equals(currencyCd, BaseCurrency, StringComparison.OrdinalIgnoreCase);
}
