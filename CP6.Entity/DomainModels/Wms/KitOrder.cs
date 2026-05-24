using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// キット組立/バラシ指示（MSBBWM140）
/// </summary>
/// <remarks>
/// 業務 PK：KitOrderNo（KITYYYYMMDD-NNNN）
/// 状态：0=下書き → 1=実行済 → 9=取消
/// Direction：ASSEMBLE（部品 OUT + キット IN）／ DISASSEMBLE（キット OUT + 部品 IN）
/// </remarks>
[Table("T_KitOrder")]
public class KitOrder : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string KitOrderNo { get; set; } = string.Empty;

    /// <summary>キット品 SKU CD（FK → T_KitMaster.KitSku）</summary>
    [Required, MaxLength(20)]
    public string KitSku { get; set; } = string.Empty;

    /// <summary>キット品名（スナップショット）</summary>
    [MaxLength(100)] public string? KitName { get; set; }

    /// <summary>組立 or バラシ数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>方向：ASSEMBLE / DISASSEMBLE</summary>
    [Required, MaxLength(20)]
    public string Direction { get; set; } = "ASSEMBLE";

    /// <summary>倉庫CD（部品もキットも同一倉庫）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>キット品配置/取出ロケーションCD</summary>
    [Required, MaxLength(30)]
    public string KitLocationCd { get; set; } = string.Empty;

    /// <summary>キット品ロットNO（ASSEMBLE で IN するロット、DISASSEMBLE で OUT するロット）</summary>
    [MaxLength(30)] public string? KitLotNo { get; set; }

    /// <summary>ステータス：0=下書き 1=実行済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>担当者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    /// <summary>実行結果トランザクションNO（部品 + キットの全 TXN を ; 区切り）</summary>
    [MaxLength(2000)] public string? ExecutedTxnNos { get; set; }

    /// <summary>実行日時</summary>
    public DateTime? ExecutedAt { get; set; }
}
