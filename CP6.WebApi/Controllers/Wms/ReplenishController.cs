using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM120 — 補充指示 Web API</summary>
[ApiController]
[Route("api/wms/replenish")]
[Authorize]
public class ReplenishController : ControllerBase
{
    private readonly IReplenishService _svc;
    public ReplenishController(IReplenishService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] ReplenishSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var o = await _svc.GetAsync(no);
        if (o == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = o });
    }

    [HttpPost]
    [RequirePermission("wms-replenish", "add")]
    public async Task<IActionResult> Create([FromBody] ReplenishOrderDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { replenishNo = no } });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-replenish", "add")]
    public async Task<IActionResult> Update(
        string no,
        [FromBody] ReplenishOrderDto dto)
    {
        try
        {
            await _svc.UpdateAsync(no, dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>バッチ：指定倉庫の MinQty 割れ製品に対し補充指示を一括生成</summary>
    [HttpPost("generate-batch")]
    [RequirePermission("wms-replenish", "generate")]
    public async Task<IActionResult> GenerateBatch([FromBody] GenerateBatchRequest req)
    {
        try
        {
            var n = await _svc.GenerateBatchAsync(req.WarehouseCd, req.MinQty, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { generated = n } });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/execute")]
    [RequirePermission("wms-replenish", "execute")]
    public async Task<IActionResult> Execute(string no)
    {
        try
        {
            var taskNo = await _svc.ExecuteAsync(no, CurrentUser);
            return Ok(new
            {
                code = 0,
                message = "WM-MSG-071",
                data = new { taskNo }
            });
        }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-replenish", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try { await _svc.CancelAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (WmsAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class GenerateBatchRequest
    {
        public string WarehouseCd { get; set; } = string.Empty;
        public decimal MinQty { get; set; } = 10m;
    }
}
