using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/client/devices")]
public sealed class ClientDevicesController : ControllerBase
{
    private readonly IClientDeviceService _service;
    public ClientDevicesController(IClientDeviceService service) => _service = service;

    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivatedClientDeviceDto>> Activate(
        ActivateClientDeviceRequest request,
        CancellationToken ct)
    {
        try { return Ok(await _service.ActivateAsync(request, ct)); }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    [HttpPost("heartbeat")]
    [Authorize]
    public async Task<ActionResult<ClientDeviceDto>> Heartbeat(
        ClientDeviceHeartbeatRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _service.HeartbeatAsync(
                request, User.Identity?.Name, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { code = ex.Message, message = ex.Message });
        }
    }
}
