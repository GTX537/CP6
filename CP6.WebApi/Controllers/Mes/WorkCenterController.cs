using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController]
[Route("api/mes/work-center")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WorkCenterController : ControllerBase
{
    private readonly IWorkCenterService _svc;
    public WorkCenterController(IWorkCenterService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet, RequirePermission("mes-work-center", "query")]
    public async Task<IActionResult> List([FromQuery] string? keyword) => Ok2(await _svc.ListAsync(keyword));
    [HttpGet("{wgCd}"), RequirePermission("mes-work-center", "query")]
    public async Task<IActionResult> Get(string wgCd) => Ok2(await _svc.GetAsync(wgCd));

    [HttpGet("owner-v1"), RequirePermission("mes-work-center", "query")]
    public async Task<IActionResult> ListOwner([FromQuery] string? keyword) => Ok2(await _svc.ListOwnerAsync(keyword));
    [HttpGet("owner-v1/{wgCd}"), RequirePermission("mes-work-center", "query")]
    public async Task<IActionResult> GetOwner(string wgCd) => Ok2(await _svc.GetOwnerAsync(wgCd));
    [HttpPost("owner-v1/upsert"), RequirePermission("mes-work-center", "edit")]
    public async Task<IActionResult> SaveOwner([FromBody] WorkCenterOwnerV1 input)
    { await _svc.SaveOwnerAsync(input); return Ok2(); }
    [HttpPost("owner-v1/delete"), RequirePermission("mes-work-center", "del")]
    public async Task<IActionResult> DeleteOwner([FromBody] MesMasterDeleteV1 input)
    { await _svc.DeleteOwnerAsync(input.Id, input.RowVersion); return Ok2(); }

    [HttpPost("upsert")]
    [RequirePermission("mes-work-center", "edit")]
    public async Task<IActionResult> Upsert([FromBody] WorkCenter dto)
    { try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); } catch (InvalidOperationException e) when (e is not OrderPriceCorrectionException) { return Err(e); } }

    [HttpDelete("{wgCd}")]
    [RequirePermission("mes-work-center", "del")]
    public async Task<IActionResult> Delete(string wgCd)
    { try { await _svc.DeleteAsync(wgCd, CurrentUser); return Ok2(); } catch (InvalidOperationException e) when (e is not OrderPriceCorrectionException) { return Err(e); } }
}
