using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/serials")]
[Authorize]
public sealed class SerialsController : ControllerBase
{
    private readonly ISerialInventoryService _service;
    public SerialsController(ISerialInventoryService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "view")]
    public Task<PagedResult<StockSerialDto>> Get(
        string? productCd,
        string? serialNo,
        string? warehouseCd,
        string? locationCd,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _service.GetAsync(
            productCd, serialNo, warehouseCd, locationCd, page, pageSize, ct);

    [HttpPost("enable-tracking")]
    [RequirePermission("wms-mobile", "serial-manage")]
    public Task<ActionResult<SerialOperationResult>> EnableTracking(
        EnableSerialTrackingRequest request,
        CancellationToken ct)
        => Execute(() => _service.EnableTrackingAsync(
            request, User.Identity?.Name, ct));

    [HttpPost]
    [RequirePermission("wms-mobile", "serial-manage")]
    public Task<ActionResult<SerialOperationResult>> Post(
        SerialLifecycleRequest request,
        CancellationToken ct)
        => Execute(() => _service.PostAsync(request, User.Identity?.Name, ct));

    private static async Task<ActionResult<SerialOperationResult>> Execute(
        Func<Task<SerialOperationResult>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (MobileTaskConflictException ex)
        {
            return new ConflictObjectResult(new { code = ex.Code, message = ex.Code });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new BadRequestObjectResult(new { code = ex.Message, message = ex.Message });
        }
    }
}
