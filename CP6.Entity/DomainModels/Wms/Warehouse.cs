using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 倉庫マスタ（MSBBWM010）
/// </summary>
/// <remarks>
/// 仕様書 §2 倉庫マスタ。
/// 業務 PK：WarehouseCd。1倉庫 → N ロケーション → N 在庫。
/// </remarks>
[Table("T_Warehouse")]
public class Warehouse : BaseBizEntity
{
    /// <summary>倉庫CD（業務 PK、3 桁英数字）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>倉庫名</summary>
    [Required, MaxLength(100)]
    public string WarehouseName { get; set; } = string.Empty;

    /// <summary>倉庫区分（<see cref="WarehouseType"/>）</summary>
    public int WarehouseType { get; set; } = 1;

    /// <summary>所属拠点CD</summary>
    [MaxLength(10)] public string? BaseCd { get; set; }

    /// <summary>住所</summary>
    [MaxLength(200)] public string? AddressText { get; set; }

    /// <summary>責任者（担当者CD）</summary>
    [MaxLength(20)] public string? ManagerCd { get; set; }

    /// <summary>マイナス在庫許可（既定 false）</summary>
    public bool AllowNegative { get; set; } = false;

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
