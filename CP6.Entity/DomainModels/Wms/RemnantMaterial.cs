using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 残材（端材）管理（MSBBWM210）
/// </summary>
/// <remarks>
/// 仕様書 §30 端材・残材の再利用管理。
/// 業務 PK：RemnantNo（REM-YYYYMMDD-NNNN）
/// 原紙ロール残米、印刷後の余白、断裁端材等、再利用可能サイズの素材を管理。
/// "サイズ範囲(幅×長さ)" + "材質" で類似検索 → 小ロット注文へ転用。
/// 状态：0=利用可 1=予約済 2=使用済 3=廃棄
/// </remarks>
[Table("T_RemnantMaterial")]
public class RemnantMaterial : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string RemnantNo { get; set; } = string.Empty;

    /// <summary>素材区分（PAPER / FILM / OTHER）</summary>
    [Required, MaxLength(10)]
    public string MaterialType { get; set; } = "PAPER";

    /// <summary>紙質（連量）</summary>
    [MaxLength(20)] public string? MaterialGrade { get; set; }

    /// <summary>幅(mm)</summary>
    public int WidthMm { get; set; }

    /// <summary>長さ(mm)</summary>
    public int LengthMm { get; set; }

    /// <summary>厚み(μm)（任意）</summary>
    public int? ThicknessUm { get; set; }

    /// <summary>枚数 / 数量</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }

    /// <summary>単位（SHT=枚 / M=メートル 等）</summary>
    [Required, MaxLength(10)]
    public string UnitCd { get; set; } = "SHT";

    /// <summary>由来：どの製造指図・どの工程から出たか</summary>
    [MaxLength(25)] public string? SourceWorkOrderNo { get; set; }

    /// <summary>由来：親ロールNO（原紙残米の場合）</summary>
    [MaxLength(25)] public string? SourceRollNo { get; set; }

    /// <summary>保管 倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>保管 ロケーションCD</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>状态：0=利用可 1=予約済 2=使用済 3=廃棄</summary>
    public int Status { get; set; } = 0;

    /// <summary>予約された場合の予約者・予約用途</summary>
    [MaxLength(30)] public string? ReservedFor { get; set; }

    /// <summary>登録日</summary>
    public DateTime RegisteredAt { get; set; } = DateTime.Now;

    [MaxLength(500)] public string? Remarks { get; set; }
}
