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
