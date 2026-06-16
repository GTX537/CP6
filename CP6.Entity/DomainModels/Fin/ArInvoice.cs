using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 应收发票（章04 §1，镜像 ApInvoice）。对客户开具的发票；过账生成"借应收/贷收入+销项税"凭证，
/// 出货自动开票时另生成"借COGS/贷FG"成本结转凭证（估算成本，F2-D2）。余额由 AR_CONTROL 控制科目勾稽。
/// 全外币（F2-D1）：销售金额存原币 + 销售汇率 FxRate；成本 CostAmount 为本位币（我方成本）。
/// </summary>
[Table("Fin_ArInvoice")]
public class ArInvoice : BaseTenantEntity
{
    /// <summary>系统发票号（采番 AR-yyyyMM-nnnn）</summary>
    [MaxLength(30)] public string No { get; set; } = string.Empty;

    /// <summary>客户编码（BusinessPartner.BpCd）</summary>
    [Required, MaxLength(20)] public string CustomerId { get; set; } = string.Empty;

    /// <summary>发票日期</summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>到期日</summary>
    public DateTime DueDate { get; set; }

    // —— 全外币 ——
    [MaxLength(3)] public string? CurrencyCd { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal FxRate { get; set; } = 1m;

    /// <summary>净额（原币，不含税）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal NetAmount { get; set; }
    /// <summary>税额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    /// <summary>价税合计（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal GrossAmount { get; set; }
    /// <summary>已收款金额（原币，含尾差核销）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>成本结转金额（本位币，估算成本起步 F2-D2）。出货自动开票时算，供 COGS 凭证用</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal CostAmount { get; set; }

    /// <summary>状态机</summary>
    public ArInvoiceStatus Status { get; set; } = ArInvoiceStatus.Draft;

    /// <summary>收入确认凭证 Id</summary>
    public Guid? JournalEntryId { get; set; }
    /// <summary>成本结转凭证 Id</summary>
    public Guid? CostJournalEntryId { get; set; }

    // —— 出货自动开票来源（C-2，幂等键 ShipmentId）——
    /// <summary>来源出货号（WMS 出库，幂等防重开票）</summary>
    [MaxLength(30)] public string? ShipmentId { get; set; }
    /// <summary>来源订单号</summary>
    [MaxLength(30)] public string? OrderId { get; set; }

    // —— 红字（销售退货，接 CreditNote，F2-D6）——
    public bool IsCreditMemo { get; set; }
    public Guid? OriginInvoiceId { get; set; }
    /// <summary>关联销售退货单 CreditNote.Id（复用现有，不新建销售红字实体）</summary>
    public Guid? CreditNoteId { get; set; }

    public List<ArInvoiceLine> Lines { get; set; } = new();
}

/// <summary>应收发票明细行（章04 §1，镜像 ApInvoiceLine）。每行收入科目（贷方）+ 可选税码。</summary>
[Table("Fin_ArInvoiceLine")]
public class ArInvoiceLine : BaseTenantEntity
{
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    [MaxLength(40)] public string? ItemId { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal UnitPrice { get; set; }
    /// <summary>行净额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public Guid? TaxCodeId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    /// <summary>收入科目 Id（可空，默认走 REVENUE 角色；预留按行分收入）</summary>
    public Guid? RevenueAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
}

/// <summary>应收发票状态机（章04，镜像 ApInvoiceStatus）。</summary>
public enum ArInvoiceStatus
{
    Draft = 0,
    Posted = 1,
    PartiallySettled = 2,
    Settled = 3,
    Reversed = 4,
}
