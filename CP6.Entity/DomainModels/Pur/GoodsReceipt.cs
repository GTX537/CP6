using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 收货单头（GR，采购 章03）。采购单据，不写物理库存——库存唯一真相在 WMS，
/// 经 <see cref="CP6.Core.Services.Pur.Contracts.IWmsReceiveService"/> 单向委托落库，回填 <see cref="WmsInboundNo"/>。
/// </summary>
/// <remarks>双基准（<see cref="PostingBasis"/> 从 PO 快照）：着荷=收货即验收；检收=收货待检，QC 通过才验收。</remarks>
[Table("Pur_GoodsReceipt")]
public class GoodsReceipt : BaseBizEntity
{
    /// <summary>收货单号（采番 "GR"+yyyyMMdd+流水，同租户唯一）</summary>
    [Required, MaxLength(20)]
    public string GrNo { get; set; } = string.Empty;

    /// <summary>来源采购订单号</summary>
    [Required, MaxLength(20)]
    public string PoNo { get; set; } = string.Empty;

    /// <summary>供应商 CD</summary>
    [Required, MaxLength(20)]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>收货日期</summary>
    public DateTime ReceiptDate { get; set; }

    /// <summary>状态（<see cref="GrStatus"/>）</summary>
    public GrStatus Status { get; set; } = GrStatus.Draft;

    /// <summary>WMS 入库单号（委托入库回填）</summary>
    [MaxLength(20)]
    public string? WmsInboundNo { get; set; }

    /// <summary>入账基准（从 PO 快照）：1=着荷 / 2=检收</summary>
    [MaxLength(4)]
    public string PostingBasis { get; set; } = "2";

    /// <summary>入库仓库 CD</summary>
    [MaxLength(10)]
    public string? WarehouseCd { get; set; }

    /// <summary>备注</summary>
    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>明细行（业务键 GrNo 关联，级联）</summary>
    public List<GoodsReceiptLine> Lines { get; set; } = new();
}

/// <summary>收货单行（采购 章03）。</summary>
[Table("Pur_GoodsReceiptLine")]
public class GoodsReceiptLine : BaseBizEntity
{
    /// <summary>所属收货单号</summary>
    [Required, MaxLength(20)]
    public string GrNo { get; set; } = string.Empty;

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>对应 PO 行号</summary>
    public int PoLineNo { get; set; }

    /// <summary>物料 CD</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>本次收货量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ReceivedQty { get; set; }

    /// <summary>本次验收合格量（着荷=收货量；检收=QC 通过后填）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AcceptedQty { get; set; }

    /// <summary>本次不良量（检收 QC 不良）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RejectedQty { get; set; }

    /// <summary>检收状态：NONE=免检(着荷) / PENDING=待检 / PASS=合格 / FAIL=不良</summary>
    [MaxLength(20)]
    public string QcStatus { get; set; } = "PENDING";

    /// <summary>WMS 入库明细引用键</summary>
    [MaxLength(40)]
    public string? WmsReceiptDetailRef { get; set; }
}

/// <summary>收货单状态（采购 章03）。</summary>
public enum GrStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>已收货（着荷=直接完成；检收=转检验中）</summary>
    Received = 1,
    /// <summary>检验中（检收基准待 QC）</summary>
    Inspecting = 2,
    /// <summary>完成（验收已确认）</summary>
    Completed = 3,
    /// <summary>取消</summary>
    Cancelled = 9,
}
