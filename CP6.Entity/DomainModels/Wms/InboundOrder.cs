using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入庫予定ヘッダ（MSBBWM030）
/// </summary>
/// <remarks>
/// 仕様書 §4 入庫予定登録。
/// 業務 PK：InboundNo（INYYYYMMDD-NNNN）。1ヘッダ → N明細。
/// ステータス：0=下書き 1=確定済 2=入庫中（部分入庫）3=完了 9=取消
/// </remarks>
[Table("T_InboundOrder")]
public class InboundOrder : BaseBizEntity, IAuditable
{
    /// <summary>入庫予定NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string InboundNo { get; set; } = string.Empty;

    /// <summary>入庫区分：1=購買入庫 2=外注戻入 3=返品入庫 9=その他</summary>
    public int InboundType { get; set; } = 1;

    /// <summary>仕入先CD（取引先マスタ参照、購買時必須）</summary>
    [MaxLength(20)] public string? SupplierCd { get; set; }

    /// <summary>仕入先名（スナップショット）</summary>
    [MaxLength(100)] public string? SupplierName { get; set; }

    /// <summary>発注書NO（PA060 連携将来用）</summary>
    [MaxLength(20)] public string? PoNo { get; set; }

    /// <summary>入荷予定日</summary>
    public DateTime ExpectedArrivalDate { get; set; }

    /// <summary>入庫倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ステータス：0~9</summary>
    public int Status { get; set; } = 0;

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    /// <summary>明細（業務キー InboundNo で紐付）</summary>
    [NotMapped]
    public List<InboundOrderDetail> Details { get; set; } = new();
}
