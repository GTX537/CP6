using CP6.WebApi.Controllers.Erp;
using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[OrderOwnerResult]
[Route("api/v2/admin/wms-features")]
[Authorize]
public sealed class WmsFeatureFlagsController : ControllerBase
{
    private readonly IWmsFeatureFlagChangeService _service;
    public WmsFeatureFlagsController(IWmsFeatureFlagChangeService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "device-manage")]
    public Task<IReadOnlyList<WmsFeatureFlagDto>> Get(CancellationToken ct)
        => _service.GetFeaturesAsync(ct);

    [HttpPut("{warehouseCd}")]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<WmsFeatureFlagDto>> Put(
        string warehouseCd,
        UpdateWmsFeatureFlagRequest request,
        CancellationToken ct)
    {
        _ = warehouseCd;
        _ = request;
        _ = ct;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "WM-FEATURE-APPROVAL-REQUIRED",
            message = "Warehouse production flags must be changed through OA approval."
        });
    }
}
