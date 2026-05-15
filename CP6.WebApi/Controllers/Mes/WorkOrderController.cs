using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>
/// MSBBME020 / 030 — 製造指図入力・一覧 Web API
/// </summary>
[ApiController]
[Route("api/mes/work-orders")]
[Authorize]
public class WorkOrderController : ControllerBase
{
    private readonly IWorkOrderService _service;

    public WorkOrderController(IWorkOrderService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>採番取得 — GET /api/mes/work-orders/next-seq</summary>
    [HttpGet("next-seq")]
    public async Task<IActionResult> GetNextSeq()
    {
        var seq = await _service.NextSequenceAsync();
        return Ok(new { code = 0, message = "OK", data = new { sequence = seq } });
    }

    /// <summary>ME030 — 一覧検索 GET /api/mes/work-orders</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] WorkOrderSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>ME020 — 単条 GET /api/mes/work-orders/{no}</summary>
    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _service.GetByNoAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "ME-MSG-040" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>ME020 — 新建 POST /api/mes/work-orders</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WorkOrderDto dto)
    {
        try
        {
            var no = await _service.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { workOrderNo = no } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME020 — 訂正 PUT /api/mes/work-orders/{no}</summary>
    [HttpPut("{no}")]
    public async Task<IActionResult> Update(string no, [FromBody] WorkOrderDto dto)
    {
        try
        {
            await _service.UpdateAsync(no, dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME030 — 削除 DELETE /api/mes/work-orders/{no}</summary>
    [HttpDelete("{no}")]
    public async Task<IActionResult> Delete(string no, [FromBody] DeleteRequest? body = null)
    {
        try
        {
            await _service.DeleteAsync(no, body?.RowVersion, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME020 — 指図発行 POST /api/mes/work-orders/{no}/issue</summary>
    [HttpPost("{no}/issue")]
    public async Task<IActionResult> Issue(string no)
    {
        try
        {
            await _service.IssueAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME020 — 受注 → 指図 自動展開 POST /api/mes/work-orders/expand-from-order</summary>
    [HttpPost("expand-from-order")]
    public async Task<IActionResult> ExpandFromOrder([FromBody] ExpandFromOrderRequest req)
    {
        try
        {
            var nos = await _service.ExpandFromOrderAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { workOrderNos = nos } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    public class DeleteRequest
    {
        public byte[]? RowVersion { get; set; }
    }
}
