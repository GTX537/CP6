using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>
/// 询价 RFQ REST —— 采购章06。/api/pur/rfq。
/// 价格发现全流程：从 PR 发起 → 邀供应商 → 收报价 → 比价排名 → 按行选定 → 回写价表(Source=rfq) → 选中转 PO。
/// </summary>
[ApiController]
[Route("api/pur/rfq")]
[Authorize]
public class RfqController : ControllerBase
{
    private readonly IRfqService _svc;
    public RfqController(IRfqService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RfqStatus? status)
        => Ok2(await _svc.ListAsync(status));

    [HttpGet("{rfqNo}")]
    public async Task<IActionResult> Get(string rfqNo)
    {
        var rfq = await _svc.GetAsync(rfqNo);
        return rfq == null ? Err(new InvalidOperationException("E-PUR-061")) : Ok2(rfq);
    }

    /// <summary>从 PR 发起询价（汇未定供应商的 PR 行，行级追溯）。</summary>
    [HttpPost("from-pr/{prNo}")]
    public async Task<IActionResult> CreateFromPr(string prNo)
    {
        try { return Ok2(await _svc.CreateFromPrAsync(prNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>邀请供应商（发注先，幂等，状态→邀请中）。</summary>
    [HttpPost("{rfqNo}/suppliers")]
    public async Task<IActionResult> AddSuppliers(string rfqNo, [FromBody] List<string> supplierIds)
    {
        try { return Ok2(await _svc.AddSuppliersAsync(rfqNo, supplierIds ?? new(), CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>收报价（按行 upsert 报价矩阵，状态→报价中）。</summary>
    [HttpPost("{rfqNo}/quote")]
    public async Task<IActionResult> RecordQuote(string rfqNo, [FromBody] RecordQuoteRequest req)
    {
        try { return Ok2(await _svc.RecordQuoteAsync(rfqNo, req.SupplierId, req.Lines ?? new(), CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>比价排名（按行，剔除过期，价格优先→交期；Rank 仅建议）。</summary>
    [HttpPost("{rfqNo}/rank")]
    public async Task<IActionResult> Rank(string rfqNo, [FromBody] RankRequest? req)
    {
        try { return Ok2(await _svc.RankQuotesAsync(rfqNo, req?.AsOf, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>选定（按行可拆不同供应商，一行一选，校验未过期；全行选中→已选定）。</summary>
    [HttpPost("{rfqNo}/select")]
    public async Task<IActionResult> Select(string rfqNo, [FromBody] List<RfqSelectionDto> selections)
    {
        try { return Ok2(await _svc.SelectAsync(rfqNo, selections ?? new(), CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>回写采购价表（选中报价 Source=rfq，沉淀进价表）。</summary>
    [HttpPost("{rfqNo}/writeback")]
    public async Task<IActionResult> WriteBack(string rfqNo)
    {
        try { return Ok2(await _svc.WriteBackPricesAsync(rfqNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>选中报价转 PO（按选中供应商分组，用成交价；全行转出→已转 PO）。</summary>
    [HttpPost("{rfqNo}/convert")]
    public async Task<IActionResult> Convert(string rfqNo)
    {
        try { return Ok2(await _svc.ConvertToPoAsync(rfqNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}

/// <summary>收报价请求体：一个供应商对若干行的报价。</summary>
public class RecordQuoteRequest
{
    public string SupplierId { get; set; } = string.Empty;
    public List<RfqQuoteLineDto> Lines { get; set; } = new();
}

/// <summary>比价请求体：可指定 asOf 基准日（默认今天）。</summary>
public class RankRequest
{
    public DateTime? AsOf { get; set; }
}
