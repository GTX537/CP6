using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// WCS（倉庫制御システム）タスク（MSBBWM310）
/// </summary>
/// <remarks>
/// 仕様書 §40 コンベヤ・AGV・自動倉庫への作業指示。
/// 業務 PK：TaskNo（WCS-YYYYMMDD-NNNN）
/// 状态遷移：0=Created → 1=Dispatched → 2=Executing → 3=Completed (or 9=Failed)
/// </remarks>
[Table("T_WcsTask")]
public class WcsTask : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string TaskNo { get; set; } = string.Empty;

    /// <summary>タスク種別：MOVE/PICK/PUT/COUNT</summary>
    [Required, MaxLength(10)]
    public string TaskType { get; set; } = "MOVE";

    /// <summary>優先度（1=通常 / 2=高 / 3=緊急）</summary>
    public int Priority { get; set; } = 1;

    /// <summary>状态：0=Created 1=Dispatched 2=Executing 3=Completed 9=Failed</summary>
    public int Status { get; set; } = 0;

    /// <summary>担当装置（AGV01 / CONV-A / ASRS-1 等）</summary>
    [MaxLength(20)] public string? DeviceCd { get; set; }

    /// <summary>関連伝票NO（OutboundNo / InboundNo 等）</summary>
    [MaxLength(25)] public string? RelatedNo { get; set; }

    /// <summary>関連区分（OUTBOUND/INBOUND/MOVE/STOCKTAKE）</summary>
    [MaxLength(20)] public string? RelatedType { get; set; }

    /// <summary>From 倉庫CD</summary>
    [MaxLength(10)] public string? FromWarehouseCd { get; set; }
    /// <summary>From ロケCD</summary>
    [MaxLength(30)] public string? FromLocationCd { get; set; }

    /// <summary>To 倉庫CD</summary>
    [MaxLength(10)] public string? ToWarehouseCd { get; set; }
    /// <summary>To ロケCD</summary>
    [MaxLength(30)] public string? ToLocationCd { get; set; }

    /// <summary>製品CD</summary>
    [MaxLength(20)] public string? ProductCd { get; set; }

    /// <summary>ロットNO</summary>
    [MaxLength(30)] public string? LotNo { get; set; }

    /// <summary>数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? Qty { get; set; }

    /// <summary>単位</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>払出日時（Dispatched 移行）</summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>開始日時（Executing 移行）</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>完了日時（Completed 移行）</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>失敗理由（Failed 時）</summary>
    [MaxLength(500)] public string? ErrorMessage { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
