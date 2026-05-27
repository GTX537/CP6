using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// VMI 月次保管料計算結果（MSBBWM250）
/// </summary>
/// <remarks>
/// 仕様書 §34 客先預り在庫（VMI）— 月次バッチで保管料を集計記録。
/// 業務 PK：BillingNo（VMIYYYYMM-NNNN）
/// 客先別 × 年月 で 1 件。
/// VMI 在庫自体は既存 T_Stock の OwnerType=CUSTOMER + OwnerCd=客先CD で識別済み。
/// </remarks>
[Table("T_VmiBilling")]
public class VmiBilling : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string BillingNo { get; set; } = string.Empty;

    /// <summary>客先CD</summary>
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>年月（YYYYMM）</summary>
    [Required, MaxLength(6)]
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>対象 SKU 数</summary>
    public int SkuCount { get; set; }

    /// <summary>月初在庫数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal BeginQty { get; set; }

    /// <summary>月末在庫数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal EndQty { get; set; }

    /// <summary>平均在庫数量（簡易：(BEG+END)/2）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AvgQty { get; set; }

    /// <summary>保管料単価（円/単位/日）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal DailyStorageRate { get; set; }

    /// <summary>請求金額（AvgQty × Rate × 日数）</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BillingAmount { get; set; }

    /// <summary>計算日</summary>
    public DateTime CalculatedAt { get; set; } = DateTime.Now;

    /// <summary>確定済フラグ（true=客先請求送付済）</summary>
    public bool Confirmed { get; set; } = false;

    [MaxLength(500)] public string? Remarks { get; set; }
}
