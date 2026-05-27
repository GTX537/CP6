using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM210 — 残材 Web API</summary>
[ApiController]
[Route("api/wms/remnant")]
[Authorize]
public class RemnantController : ControllerBase
{
    private readonly IRemnantService _svc;
    public RemnantController(IRemnantService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] RemnantSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RemnantDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { remnantNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    public async Task<IActionResult> Update(string no, [FromBody] RemnantDto dto)
    {
        try { await _svc.UpdateAsync(no, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("match")]
    public async Task<IActionResult> Match([FromQuery] string materialType, [FromQuery] int minWidthMm, [FromQuery] int minLengthMm)
        => Ok(new { code = 0, message = "OK", data = await _svc.MatchAsync(materialType, minWidthMm, minLengthMm) });

    [HttpPost("{no}/reserve")]
    public async Task<IActionResult> Reserve(string no, [FromBody] ReserveRequest req)
    {
        try { await _svc.ReserveAsync(no, req.ReservedFor, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/unreserve")]
    public async Task<IActionResult> Unreserve(string no)
    {
        try { await _svc.UnreserveAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/use")]
    public async Task<IActionResult> MarkUsed(string no)
    {
        try { await _svc.MarkUsedAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/dispose")]
    public async Task<IActionResult> Dispose(string no)
    {
        try { await _svc.DisposeAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{no}")]
    public async Task<IActionResult> Delete(string no)
    {
        try { await _svc.DeleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class ReserveRequest { public string ReservedFor { get; set; } = string.Empty; }
}
