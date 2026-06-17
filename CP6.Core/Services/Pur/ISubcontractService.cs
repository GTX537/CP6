using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 外注加工服务（采购 章07）。外注 PO 复用 <see cref="PurchaseOrder"/>（Type=2，行 UnitPrice=加工费），
/// 本服务管外注独有的两处：① 发支給材（委托 WMS 出库 + 追踪 <see cref="PoConsignMaterial.IssuedQty"/> 防吞料）；
/// ② 收成品成本核算（加工费 + 支給材成本并入，接财务 06）+ 防吞料对账。收成品/三单匹配复用章03/04。
/// </summary>
public interface ISubcontractService
{
    /// <summary>
    /// 登记外注成品行的支給材（章07 §2）：按成品 BOM 算的应发料挂到 PO 行下。
    /// 校验 PO 存在且为外注（Type=2）、成品行存在；按 (PoNo,LineNo,ConsignItemId) 幂等 upsert。
    /// </summary>
    Task<List<PoConsignMaterial>> AddConsignAsync(string poNo, int lineNo, IEnumerable<ConsignMaterialDto> items, string? userName);

    /// <summary>列出外注 PO（Type=2，含成品行），供外注台选单。</summary>
    Task<List<PurchaseOrder>> ListOrdersAsync();

    /// <summary>取某外注 PO（可指定行）的支給材清单。</summary>
    Task<List<PoConsignMaterial>> GetConsignAsync(string poNo, int? lineNo = null);

    /// <summary>
    /// 发支給材（章07 §4）：同步委托 <see cref="Contracts.IWmsIssueService"/> 出库（Purpose=subcontract），
    /// 按实出累加 <see cref="PoConsignMaterial.IssuedQty"/> + 记 WmsIssueNo。
    /// <paramref name="issuances"/> 为 null → 发各支給材的剩余应发量（ConsignQty−IssuedQty，一次发齐）；
    /// 非 null → 仅发指定支給材的指定量（分批/补发）。出库不确认消耗/收入（资产位移非交易）。
    /// </summary>
    Task<List<PoConsignMaterial>> IssueConsignAsync(string poNo, int lineNo, IEnumerable<ConsignIssueDto>? issuances, string? userName);

    /// <summary>
    /// 收成品成本核算（章07 §5）：成品成本 = 加工费（PO 行 UnitPrice × 成品数）+ 支給材成本（Σ ConsignQty × ConsignUnitCost）。
    /// 支給材成本"并入"非"另付"（结转早买料的内部成本，不产生新对外付款）；结果调 <see cref="Contracts.IFinCostService"/> 接财务 06 成本会计。
    /// </summary>
    Task<SubcontractCostResult> CalcFinishedCostAsync(string poNo, int lineNo, decimal finishedQty, string? userName);

    /// <summary>
    /// 防吞料对账（章07 §6）：成品反推应耗（单耗 = ConsignQty ÷ PO 行计划成品数；应耗 = 单耗 × 实收成品数）
    /// 对比实发 <see cref="PoConsignMaterial.IssuedQty"/>。差异（实发 − 应耗）超损耗容差 → 标记异常挂起核查
    /// （外协多领未用 / 私吞 / 报废未报）。返回逐料对账报表，<see cref="ConsignReconcileResult.HasAnomaly"/> 即挂起信号。
    /// </summary>
    Task<ConsignReconcileResult> ReconcileConsignAsync(string poNo, int lineNo, decimal finishedQty, decimal tolerancePct, string? userName);
}

/// <summary>防吞料对账报表（章07 §6）。</summary>
public class ConsignReconcileResult
{
    /// <summary>外注采购订单号</summary>
    public string PoNo { get; init; } = string.Empty;

    /// <summary>外注成品行号</summary>
    public int LineNo { get; init; }

    /// <summary>实收成品数（反推应耗的基数）</summary>
    public decimal FinishedQty { get; init; }

    /// <summary>损耗容差（占应耗比例，如 0.05=5%）</summary>
    public decimal TolerancePct { get; init; }

    /// <summary>是否有任一支給材超容差（挂起核查信号）</summary>
    public bool HasAnomaly { get; init; }

    /// <summary>逐料对账明细</summary>
    public List<ConsignReconcileLine> Lines { get; init; } = new();
}

/// <summary>防吞料对账逐料明细。</summary>
public class ConsignReconcileLine
{
    /// <summary>支給材物料 CD</summary>
    public string ConsignItemId { get; init; } = string.Empty;

    /// <summary>实发量（IssuedQty）</summary>
    public decimal IssuedQty { get; init; }

    /// <summary>应耗量 = 单耗 × 实收成品数</summary>
    public decimal ExpectedQty { get; init; }

    /// <summary>差异 = 实发 − 应耗（正=多发，含合理损耗）</summary>
    public decimal Variance { get; init; }

    /// <summary>容许差异 = 应耗 × 容差</summary>
    public decimal AllowedVariance { get; init; }

    /// <summary>是否超容差（实发多领超损耗 → 异常）</summary>
    public bool IsAnomaly { get; init; }
}

/// <summary>外注成品成本核算结果。</summary>
public class SubcontractCostResult
{
    /// <summary>加工费 = PO 行单价 × 成品数（走 AP 对外付款）</summary>
    public decimal ProcessingFee { get; init; }

    /// <summary>支給材成本 = Σ ConsignQty × ConsignUnitCost（并入非另付）</summary>
    public decimal ConsignCost { get; init; }

    /// <summary>成品成本 = 加工费 + 支給材成本</summary>
    public decimal FinishedCost { get; init; }

    /// <summary>财务成本入账凭证号（接财务 06）</summary>
    public string? CostVoucherNo { get; init; }
}

/// <summary>登记支給材入参。</summary>
public class ConsignMaterialDto
{
    /// <summary>支給材物料 CD（原纸/油墨等）</summary>
    public string ConsignItemId { get; set; } = string.Empty;

    /// <summary>应发数量（成品 BOM 单耗 × 成品数）</summary>
    public decimal ConsignQty { get; set; }

    /// <summary>支給材单位成本（内部入库成本，非售价）</summary>
    public decimal ConsignUnitCost { get; set; }
}

/// <summary>分批发料入参（指定发哪种支給材、发多少）。</summary>
public class ConsignIssueDto
{
    /// <summary>支給材物料 CD</summary>
    public string ConsignItemId { get; set; } = string.Empty;

    /// <summary>本次发料量</summary>
    public decimal Qty { get; set; }
}
