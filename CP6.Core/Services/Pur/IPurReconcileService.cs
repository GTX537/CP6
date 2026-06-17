namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购对账服务（采购 章08/09 完整性收口）。完整性的"看得见"层：在硬约束（章04 三单匹配闸门、
/// 章03 收货超量挡、章07 IssuedQty 防吞料）之上，汇总 PO↔GR↔AP 三方累计量核对表，一眼看出每张 PO
/// 卡在哪、哪里有钻空子嫌疑——堵三个漏：① 虚开发票（Invoiced &gt; Accepted）② 超量/重复收货
/// （Received &gt; Ordered+容差）③ 外协吞料（外注 IssuedQty &gt; 成品反推应耗+容差，复用章07 对账）。
/// 跨模块同步走直线委托（四契约 IWmsReceive/QcQuery/Issue + IFinAp + IApproval），不走 Phase6 事件（事件仅模块内）。
/// </summary>
public interface IPurReconcileService
{
    /// <summary>对某 PO 出三方对账表（含逐行诊断 + 完整性异常汇总）。外注 PO 并入支給材防吞料对账。</summary>
    Task<PoReconcileReport> ReconcilePoAsync(string poNo);
}

/// <summary>PO 三方对账报表（章09 §5）。</summary>
public class PoReconcileReport
{
    /// <summary>采购订单号</summary>
    public string PoNo { get; init; } = string.Empty;

    /// <summary>类型：1=标准 / 2=外注</summary>
    public int Type { get; init; }

    /// <summary>供应商 CD</summary>
    public string SupplierId { get; init; } = string.Empty;

    /// <summary>是否存在完整性异常（任一行虚开/超收嫌疑，或外注吞料）→ 挂起核查信号</summary>
    public bool HasIssue { get; init; }

    /// <summary>逐行三方核对</summary>
    public List<PoReconcileLine> Lines { get; init; } = new();

    /// <summary>外注支給材防吞料对账（仅外注 PO，按行合格成品数反推；复用章07）</summary>
    public List<ConsignReconcileResult> ConsignReconciles { get; init; } = new();
}

/// <summary>PO 三方对账逐行（订购↔收货↔合格↔开票）。</summary>
public class PoReconcileLine
{
    /// <summary>行号</summary>
    public int LineNo { get; init; }

    /// <summary>物料 CD</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>订购量</summary>
    public decimal Ordered { get; init; }

    /// <summary>累计收货量</summary>
    public decimal Received { get; init; }

    /// <summary>累计合格量</summary>
    public decimal Accepted { get; init; }

    /// <summary>累计开票量</summary>
    public decimal Invoiced { get; init; }

    /// <summary>待收 = 订购 − 收货（负数即超收）</summary>
    public decimal OpenToReceive { get; init; }

    /// <summary>待开票 = 合格 − 开票</summary>
    public decimal OpenToInvoice { get; init; }

    /// <summary>诊断标签（语义 key：完成/待收/待检/待开票/虚开嫌疑/超量收货）</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>是否完整性异常（虚开/超收嫌疑，需挂起核查）</summary>
    public bool IsIssue { get; init; }
}
