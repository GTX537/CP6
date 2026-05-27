using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM230 — インキ・接着剤管理 Web API</summary>
[ApiController]
[Route("api/wms/ink")]
[Authorize]
public class InkController : ControllerBase
{
    private readonly IInkService _svc;
    public InkController(IInkService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet("lots")]
    public async Task<IActionResult> SearchLots([FromQuery] InkLotSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchLotsAsync(q) });

    [HttpGet("lots/{no}")]
    public async Task<IActionResult> GetLot(string no)
    {
        var r = await _svc.GetLotAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost("lots")]
    public async Task<IActionResult> CreateLot([FromBody] InkLotDto dto)
    {
        try
        {
            var no = await _svc.CreateLotAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { inkLotNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("lots/{no}/open")]
    public async Task<IActionResult> OpenLot(string no, [FromBody] OpenRequest req)
    {
        try { await _svc.OpenLotAsync(no, req?.NewExpiryDate, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("lots/mix")]
    public async Task<IActionResult> Mix([FromBody] MixInkRequest req)
    {
        try
        {
            var no = await _svc.MixLotsAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { newLotNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("matches")]
    public async Task<IActionResult> SearchMatches([FromQuery] string? customerCd, [FromQuery] string? colorCode)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchMatchesAsync(customerCd, colorCode) });

    [HttpPost("matches")]
    public async Task<IActionResult> RecordMatch([FromBody] InkColorMatchDto dto)
    {
        try
        {
            var no = await _svc.RecordMatchAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { matchNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class OpenRequest { public DateTime? NewExpiryDate { get; set; } }
}
