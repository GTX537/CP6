using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/admin/client-devices")]
[Authorize]
public sealed class ClientDeviceAdminController : ControllerBase
{
    private readonly IClientDeviceService _service;
    private readonly CP6Context _db;
    private readonly IPasswordHasher _hasher;
    public ClientDeviceAdminController(
        IClientDeviceService service,
        CP6Context db,
        IPasswordHasher hasher)
    {
        _service = service;
        _db = db;
        _hasher = hasher;
    }

    [HttpGet]
    [RequirePermission("wms-mobile", "device-manage")]
    public Task<PagedResult<ClientDeviceDto>> Get(
        string? warehouseCd,
        string? areaCd,
        string? status,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _service.GetDevicesAsync(
            warehouseCd, areaCd, status, page, pageSize, ct);

    [HttpPost]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<DeviceActivationTicket>> CreateActivation(
        CreateDeviceActivationRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _service.CreateActivationAsync(
                request, User.Identity?.Name, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    [HttpPatch("{deviceId}")]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<ClientDeviceDto>> Update(
        string deviceId,
        UpdateClientDeviceRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _service.UpdateAsync(
                deviceId, request, User.Identity?.Name, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { code = ex.Message, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    [HttpPost("quick-pin")]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<IActionResult> SetQuickPin(
        SetWarehouseQuickPinRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrWhiteSpace(request.BadgeNo)
            || request.Pin.Length != 6
            || request.Pin.Any(ch => !char.IsDigit(ch)))
            return BadRequest(new { code = "WM-DEVICE-QUICK-PIN-DATA" });
        var user = await _db.Sys_Users.FirstOrDefaultAsync(
            x => x.UserName == request.UserName, ct);
        if (user is null)
            return NotFound(new { code = "WM-DEVICE-USER-NOT-FOUND" });
        user.BadgeNo = request.BadgeNo.Trim();
        user.QuickPinHash = _hasher.Hash(request.Pin);
        user.Modifier = User.Identity?.Name;
        user.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(new { code = 0, message = "OK" });
    }
}
