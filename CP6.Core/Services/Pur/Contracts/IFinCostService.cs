namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 财务成本入账契约（采购侧端口，P2-D2）。外注收成品后，把成品成本（加工费 + 支給材成本并入）
/// 经本端口请财务成本会计入账——成本唯一真相在财务，采购不双写。MVP 用 <see cref="StubFinCostService"/> 桩；
/// 财务 Plan3 章06 成本会计落地后以适配器委托真实成本服务（PostSubcontractCost）替换（DI 切换）。
/// </summary>
public interface IFinCostService
{
    /// <summary>外注成品成本入账：加工费 + 支給材成本并入成品成本，结转财务成本会计。</summary>
    Task<FinCostResult> PostSubcontractCostAsync(SubcontractCostDto dto, string? operatorId);
}

/// <summary>外注成品成本入账请求（采购形状，由适配器映射到财务成本契约）。</summary>
public class SubcontractCostDto
{
    /// <summary>外注采购订单号</summary>
    public string PoNo { get; set; } = string.Empty;

    /// <summary>外注成品行号</summary>
    public int LineNo { get; set; }

    /// <summary>成品物料 CD</summary>
    public string FinishedItemId { get; set; } = string.Empty;

    /// <summary>收成品数量</summary>
    public decimal FinishedQty { get; set; }

    /// <summary>加工费（PO 行单价 × 成品数；走 AP 对外付款）</summary>
    public decimal ProcessingFee { get; set; }

    /// <summary>支給材成本（Σ ConsignQty × ConsignUnitCost；并入非另付，结转早买料的内部成本）</summary>
    public decimal ConsignCost { get; set; }

    /// <summary>成品成本 = 加工费 + 支給材成本</summary>
    public decimal FinishedCost { get; set; }
}

/// <summary>成本入账结果。</summary>
public class FinCostResult
{
    /// <summary>是否成功</summary>
    public bool Ok { get; init; }

    /// <summary>失败错误码</summary>
    public string? Code { get; init; }

    /// <summary>成本入账凭证号</summary>
    public string? CostVoucherNo { get; init; }

    /// <summary>成功。</summary>
    public static FinCostResult Pass(string? voucherNo) => new() { Ok = true, CostVoucherNo = voucherNo };

    /// <summary>失败。</summary>
    public static FinCostResult Fail(string code) => new() { Ok = false, Code = code };
}

/// <summary>
/// 财务成本入账桩（P2-D2，MVP）。返回成功 + 假凭证号 "SCST-{PoNo}-{时分秒毫秒}"，不落真实成本会计。
/// 财务 Plan3 章06 成本会计落地后以适配器委托真实成本服务替换（DI 切换）。
/// </summary>
public class StubFinCostService : IFinCostService
{
    /// <inheritdoc />
    public Task<FinCostResult> PostSubcontractCostAsync(SubcontractCostDto dto, string? operatorId)
        => Task.FromResult(FinCostResult.Pass($"SCST-{dto.PoNo}-{DateTime.Now:HHmmssfff}"));
}
