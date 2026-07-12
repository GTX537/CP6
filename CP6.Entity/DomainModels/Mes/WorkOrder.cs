using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 製造指図ヘッダ（MSBBME020）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.1 T_WorkOrder
/// 業務 PK：WORK_ORDER_NO（WOYYYYMMDD-NNNN 形式、シーケンス自動採番）
/// 1指図 → N工程(T_WorkOrderProcess) / N材料(T_WorkOrderMaterial) / N実績(T_ProductionResult)
/// </remarks>
[Table("T_WorkOrder")]
public class WorkOrder : BaseBizEntity, IAuditable
{
    /// <summary>製造指図NO（業務 PK、WOYYYYMMDD-NNNN）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>指図ステータス：0=下書き 1=確定済 2=発行済 3=着手中 4=完了 5=中断中 6=検査済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>受注NO（PA070 → MES展開元、null=手動作成）</summary>
    [MaxLength(20)] public string? OrderNo1 { get; set; }
    [MaxLength(20)] public string? OrderNo2 { get; set; }
    [MaxLength(20)] public string? OrderNo3 { get; set; }

    /// <summary>受注Web NO（T_Order.WebOrderNo へのソフト参照）</summary>
    [MaxLength(20)] public string? WebOrderNo { get; set; }

    /// <summary>得意先CD</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>製品CD（FK → T_ProductMaster）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>製品名</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>生産数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ProductionQty { get; set; }

    /// <summary>完了数量（累計良品数）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal CompletedQty { get; set; } = 0;

    /// <summary>不良数量（累計）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal DefectQty { get; set; } = 0;

    /// <summary>客先納期</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>計画開始日</summary>
    public DateTime? PlanStartDate { get; set; }

    /// <summary>計画完了日</summary>
    public DateTime? PlanEndDate { get; set; }

    /// <summary>実績開始日</summary>
    public DateTime? ActualStartDate { get; set; }

    /// <summary>実績完了日</summary>
    public DateTime? ActualEndDate { get; set; }

    /// <summary>優先度：1=通常 2=急ぎ 3=特急</summary>
    public int Priority { get; set; } = 1;

    /// <summary>ロットNO</summary>
    [MaxLength(30)] public string? LotNo { get; set; }

    /// <summary>拠点CD</summary>
    [MaxLength(10)] public string? BaseCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    // ───── ナビゲーション ─────
    public List<WorkOrderProcess> Processes { get; set; } = new();
    public List<WorkOrderMaterial> Materials { get; set; } = new();
}

/// <summary>
/// <see cref="WorkOrder.Status"/> 指図ステータス 取値常量（Phase 6 整理 — 既存約定を Code 化）
/// </summary>
public static class WorkOrderStatus
{
    /// <summary>下書き（CreateAsync 初期、未確定）</summary>
    public const int Draft = 0;

    /// <summary>確定済（受注展開直後）</summary>
    public const int Confirmed = 1;

    /// <summary>発行済（IssueAsync 後、工場が着手可能）</summary>
    public const int Issued = 2;

    /// <summary>着手中（少なくとも 1 工程の実績登録あり）</summary>
    public const int InProgress = 3;

    /// <summary>完了（全工程完了）</summary>
    public const int Completed = 4;

    /// <summary>中断中</summary>
    public const int Interrupted = 5;

    /// <summary>検査済</summary>
    public const int Inspected = 6;

    /// <summary>取消（受注取消連動 / 手動取消）</summary>
    public const int Cancelled = 9;

    /// <summary>取消可能か判定：Draft/Confirmed/Issued のみ取消可（InProgress 以降は不可）</summary>
    public static bool IsCancellable(int status) =>
        status == Draft || status == Confirmed || status == Issued;
}
