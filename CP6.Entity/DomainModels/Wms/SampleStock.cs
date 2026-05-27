using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// サンプル品 在庫・貸出管理（MSBBWM260）
/// </summary>
/// <remarks>
/// 仕様書 §34 顧客向け試作品・サンプルの保管＋貸出管理。
/// 業務 PK：SampleNo（SMP-YYYYMMDD-NNNN）
/// 試作・カラーサンプル等を「貸出 → 返却 / 失効」で管理。
/// 状态：0=保管中 1=貸出中 2=返却済 3=失効・破棄
/// </remarks>
[Table("T_SampleStock")]
public class SampleStock : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string SampleNo { get; set; } = string.Empty;

    /// <summary>サンプル種別（PROTO=試作 / COLOR=色見本 / DUMMY=ダミー / OTHER）</summary>
    [Required, MaxLength(10)]
    public string SampleType { get; set; } = "PROTO";

    /// <summary>関連顧客CD</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>顧客名</summary>
    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>関連製品CD</summary>
    [MaxLength(20)] public string? ProductCd { get; set; }

    /// <summary>製品名・サンプル名</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>数量</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>単位</summary>
    [Required, MaxLength(10)]
    public string UnitCd { get; set; } = "PCS";

    /// <summary>保管 倉庫CD</summary>
    [MaxLength(10)] public string? WarehouseCd { get; set; }

    /// <summary>保管 ロケーションCD</summary>
    [MaxLength(30)] public string? LocationCd { get; set; }

    /// <summary>状态：0=保管中 1=貸出中 2=返却済 3=失効・破棄</summary>
    public int Status { get; set; } = 0;

    /// <summary>貸出先（社内担当者 or 客先）</summary>
    [MaxLength(60)] public string? LentTo { get; set; }

    /// <summary>貸出日</summary>
    public DateTime? LentAt { get; set; }

    /// <summary>返却予定日</summary>
    public DateTime? ExpectedReturnDate { get; set; }

    /// <summary>実返却日</summary>
    public DateTime? ReturnedAt { get; set; }

    /// <summary>登録日</summary>
    public DateTime RegisteredAt { get; set; } = DateTime.Now;

    [MaxLength(500)] public string? Remarks { get; set; }
}
