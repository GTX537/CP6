using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入庫実績ヘッダ（MSBBWM040）
/// </summary>
/// <remarks>
/// 仕様書 §5 入庫実績入力。
/// 業務 PK：ReceiptNo（RCYYYYMMDD-NNNN）。
/// InboundNo（参照モード）or 直入モード（InboundNo=null）。
/// 1ヘッダ → N明細。
/// 確定処理で StockMovementService.ApplyAsync(IN) を明細件数分実行する。
/// </remarks>
[Table("T_InboundReceipt")]
public class InboundReceipt : BaseBizEntity
{
    /// <summary>入庫実績NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string ReceiptNo { get; set; } = string.Empty;

    /// <summary>関連入庫予定NO（直入時は null）</summary>
    [MaxLength(20)] public string? InboundNo { get; set; }

    /// <summary>入庫元区分：PURCHASE / PRODUCTION / RMA / MANUAL</summary>
    [MaxLength(20)] public string SourceType { get; set; } = "PURCHASE";

    /// <summary>関連製造指図NO（PRODUCTION 時、WM060 製品入庫連携）</summary>
    [MaxLength(20)] public string? WorkOrderNo { get; set; }

    /// <summary>受入日時</summary>
    public DateTime ReceiveDateTime { get; set; } = DateTime.Now;

    /// <summary>作業者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    /// <summary>入庫倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ステータス：0=下書き 1=確定済（在庫反映済） 9=取消（逆仕訳済）</summary>
    public int Status { get; set; } = 0;

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped]
    public List<InboundReceiptDetail> Details { get; set; } = new();
}
