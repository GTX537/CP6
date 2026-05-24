using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 返品管理（RMA）ヘッダ（MSBBWM150）
/// </summary>
/// <remarks>
/// 仕様書 §25 返品管理。
/// 業務 PK：RmaNo（RMAYYYYMMDD-NNNN）
/// 5 段階ライフサイクル：
///   0=申請受付 → 1=RMA番号発行 → 2=返品入庫 → 3=検査中 → 4=判定済 → 5=後処理完了 → 9=取消
/// </remarks>
[Table("T_RmaHeader")]
public class RmaHeader : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string RmaNo { get; set; } = string.Empty;

    /// <summary>客先CD</summary>
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    /// <summary>客先名（スナップショット）</summary>
    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>元出荷指示NO（任意、FK → T_OutboundOrder.OutboundNo）</summary>
    [MaxLength(20)] public string? OriginalShippingNo { get; set; }

    /// <summary>返品理由</summary>
    [MaxLength(500)] public string? ReturnReason { get; set; }

    /// <summary>申請日</summary>
    public DateTime AppliedDate { get; set; } = DateTime.Today;

    /// <summary>返品入庫倉庫CD（既定は不良品倉庫）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ステータス：0=申請受付 1=RMA発行済 2=返品入庫済 3=検査中 4=判定済 5=後処理完了 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>担当者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped] public List<RmaDetail> Details { get; set; } = new();
}
