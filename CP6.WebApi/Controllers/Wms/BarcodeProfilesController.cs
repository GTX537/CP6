using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/barcode-profiles")]
[Authorize]
public sealed class BarcodeProfilesController : ControllerBase
{
    private readonly IBarcodeProfileService _service;
    public BarcodeProfilesController(IBarcodeProfileService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "barcode-manage")]
    public Task<IReadOnlyList<BarcodeProfileDto>> Get(CancellationToken ct)
        => _service.GetAsync(ct);

    [HttpPost]
    [RequirePermission("wms-mobile", "barcode-manage")]
    public async Task<ActionResult<BarcodeProfileDto>> Upsert(
        UpsertBarcodeProfileRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.UpsertAsync(
                request, User.Identity?.Name, ct));
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    [HttpPost("parse")]
    [RequirePermission("wms-mobile", "scan")]
    public async Task<ActionResult<CompoundBarcodeResult>> Parse(
        ParseCompoundBarcodeRequest request, CancellationToken ct)
    {
        try { return Ok(await _service.ParseAsync(request, ct)); }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or TimeoutException)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }
}
