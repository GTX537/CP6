namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 入库桩（P-D1，MVP）。返回假入库号 "PURIN-{PoNo}-{时分秒毫秒}" + 逐行引用，不落真实库存。
/// WMS 落地后以适配器委托真实 InboundService 替换（DI 切换）。
/// </summary>
public class StubWmsReceiveService : IWmsReceiveService
{
    /// <inheritdoc />
    public Task<WmsReceiveResult> ReceiveAsync(WmsReceiveRequest request, string? userName)
        => Task.FromResult(new WmsReceiveResult
        {
            WmsInboundNo = $"PURIN-{request.PoNo}-{DateTime.Now:HHmmssfff}",
            LineRefs = request.Lines
                .Select(l => new WmsReceiveLineRef { PoLineNo = l.PoLineNo, WmsReceiptDetailRef = $"D{l.PoLineNo}" })
                .ToList(),
        });
}

/// <summary>
/// WMS 检收桩（P-D1，MVP）。返回空判定列表——GR 服务据此默认"全合格"（收货量全部计入验收）。
/// WMS 落地后以适配器委托真实 QcInspectionService 返回逐行合格/不良。
/// </summary>
public class StubWmsQcQuery : IWmsQcQuery
{
    /// <inheritdoc />
    public Task<List<WmsQcVerdict>> QueryByReceiptAsync(string wmsInboundNo)
        => Task.FromResult(new List<WmsQcVerdict>());
}

/// <summary>
/// WMS 出库桩（P2-D3，MVP）。返回假出库号 "PUROUT-{RefNo}-{时分秒毫秒}" + 全额实出，不落真实库存。
/// WMS 落地后以适配器委托真实 OutboundService（Purpose=subcontract 记"在外协"）替换（DI 切换）。
/// </summary>
public class StubWmsIssueService : IWmsIssueService
{
    /// <inheritdoc />
    public Task<WmsIssueResult> IssueAsync(WmsIssueRequest request, string? userName)
        => Task.FromResult(new WmsIssueResult
        {
            IssueNo = $"PUROUT-{request.RefNo}-{DateTime.Now:HHmmssfff}",
            IssuedQty = request.Qty,
        });
}
