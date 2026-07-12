using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController]
[Route("api/mes/process-cost-rate")]
[Authorize]
public class ProcessCostRateController : ControllerBase
{
    private readonly IProcessCostRateService _svc;
    public ProcessCostRateController(IProcessCostRateService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet] public async Task<IActionResult> List([FromQuery] string? wgCd) => Ok2(await _svc.ListAsync(wgCd));
    [HttpGet("resolve")] public async Task<IActionResult> Resolve([FromQuery] string wgCd, [FromQuery] DateTime onDate)
        => Ok2(await _svc.ResolveAsync(wgCd, onDate));
    [HttpPost("upsert")]
    [RequirePermission("mes-process-cost-rate", "edit")]
    public async Task<IActionResult> Upsert([FromBody] ProcessCostRate dto)
    { try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
    [HttpDelete("{id:Guid}")]
    [RequirePermission("mes-process-cost-rate", "del")]
    public async Task<IActionResult> Delete(Guid id)
    { try { await _svc.DeleteAsync(id, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
}
