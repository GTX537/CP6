using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController]
[Route("api/mes/work-center")]
[Authorize]
public class WorkCenterController : ControllerBase
{
    private readonly IWorkCenterService _svc;
    public WorkCenterController(IWorkCenterService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet] public async Task<IActionResult> List([FromQuery] string? keyword) => Ok2(await _svc.ListAsync(keyword));
    [HttpGet("{wgCd}")] public async Task<IActionResult> Get(string wgCd) => Ok2(await _svc.GetAsync(wgCd));

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] WorkCenter dto)
    { try { await _svc.UpsertAsync(dto, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }

    [HttpDelete("{wgCd}")]
    public async Task<IActionResult> Delete(string wgCd)
    { try { await _svc.DeleteAsync(wgCd, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return Err(e); } }
}
