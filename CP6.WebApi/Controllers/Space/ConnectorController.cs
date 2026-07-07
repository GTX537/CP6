using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public class ConnectorController : ControllerBase
{
    private readonly IConnectorService _svc;
    public ConnectorController(IConnectorService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    [HttpGet("site/{siteId:guid}/connector")]
    public async Task<IActionResult> ListBySite(Guid siteId) => Ok2(await _svc.ListBySiteAsync(siteId));

    [HttpPost("connector")]
    public async Task<IActionResult> Create([FromBody] ConnectorDto d)
    {
        return Ok2(new { id = await _svc.CreateAsync(d, CurrentUser) });
    }

    [HttpPut("connector/{id:guid}/stop")]
    public async Task<IActionResult> UpsertStop(Guid id, [FromBody] ConnectorStopDto d)
    {
        await _svc.UpsertStopAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("connector/{id:guid}/stop/{floorId:guid}")]
    public async Task<IActionResult> DeleteStop(Guid id, Guid floorId) { await _svc.DeleteStopAsync(id, floorId); return Ok2(); }

    [HttpPut("connector/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConnectorUpdateDto d)
    {
        await _svc.UpdateAsync(id, d, CurrentUser); return Ok2();
    }

    [HttpDelete("connector/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) { await _svc.DeleteAsync(id); return Ok2(); }
}
