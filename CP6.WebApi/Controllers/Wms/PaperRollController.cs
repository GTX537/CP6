using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM200 — 原紙ロール管理 Web API</summary>
[ApiController]
[Route("api/wms/paper-roll")]
[Authorize]
public class PaperRollController : ControllerBase
{
    private readonly IPaperRollService _svc;
    public PaperRollController(IPaperRollService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] PaperRollSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost]
    [RequirePermission("wms-paper-roll", "add")]
    public async Task<IActionResult> Create([FromBody] PaperRollDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { rollNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/consume")]
    [RequirePermission("wms-paper-roll", "consume")]
    public async Task<IActionResult> Consume(string no, [FromBody] ConsumeRequest req)
    {
        try
        {
            await _svc.ConsumeAsync(no, req.LengthM, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("match")]
    public async Task<IActionResult> Match([FromQuery] string paperGrade, [FromQuery] int widthMm,
        [FromQuery] string grainDirection = "T", [FromQuery] decimal requiredLengthM = 0)
    {
        var r = await _svc.MatchAsync(paperGrade, widthMm, grainDirection, requiredLengthM);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost("slit")]
    [RequirePermission("wms-paper-roll", "slit")]
    public async Task<IActionResult> Slit([FromBody] SlitRequest req)
    {
        try
        {
            var rolls = await _svc.SlitAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { createdRolls = rolls } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/dispose")]
    [RequirePermission("wms-paper-roll", "dispose")]
    public async Task<IActionResult> Dispose(string no)
    {
        try { await _svc.DisposeAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class ConsumeRequest { public decimal LengthM { get; set; } }
}
