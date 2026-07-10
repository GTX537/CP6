using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
// ERP にも同名 PlateMold（版型/木型マスタ）が存在するため WMS 側へ明示エイリアス
using IPlateMoldService = CP6.Core.Services.Wms.IPlateMoldService;
using PlateMoldDto = CP6.Core.Services.Wms.PlateMoldDto;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM220 — 印版・木型 Web API</summary>
[ApiController]
[Route("api/wms/plate-mold")]
[Authorize]
public class PlateMoldController : ControllerBase
{
    private readonly IPlateMoldService _svc;
    public PlateMoldController(IPlateMoldService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] PlateMoldSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost]
    [RequirePermission("wms-plate-mold", "add")]
    public async Task<IActionResult> Create([FromBody] PlateMoldDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { plateNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-plate-mold", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] PlateMoldDto dto)
    {
        try { await _svc.UpdateAsync(no, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/use")]
    [RequirePermission("wms-plate-mold", "use")]
    public async Task<IActionResult> RecordUsage(string no, [FromBody] UsageRequest req)
    {
        try { await _svc.RecordUsageAsync(no, req.Shots, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/maintenance/start")]
    [RequirePermission("wms-plate-mold", "maintenance")]
    public async Task<IActionResult> StartMaintenance(string no, [FromBody] MaintRequest req)
    {
        try { await _svc.StartMaintenanceAsync(no, req?.NextMaintenanceDate, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/maintenance/complete")]
    [RequirePermission("wms-plate-mold", "maintenance")]
    public async Task<IActionResult> CompleteMaintenance(string no)
    {
        try { await _svc.CompleteMaintenanceAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/discard")]
    [RequirePermission("wms-plate-mold", "dispose")]
    public async Task<IActionResult> Discard(string no)
    {
        try { await _svc.DiscardAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("warnings")]
    public async Task<IActionResult> Warnings([FromQuery] decimal? threshold = 0.9m)
        => Ok(new { code = 0, message = "OK", data = await _svc.WarningListAsync(threshold ?? 0.9m) });

    [HttpDelete("{no}")]
    [RequirePermission("wms-plate-mold", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try { await _svc.DeleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class UsageRequest { public int Shots { get; set; } }
    public class MaintRequest { public DateTime? NextMaintenanceDate { get; set; } }
}
