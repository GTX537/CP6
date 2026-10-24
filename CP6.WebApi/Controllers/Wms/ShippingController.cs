using CP6.Core.Services.Wms;
using CP6.Entity.DTOs.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[OrderOwnerResult]
[Route("api/wms/shipping")]
[Authorize]
public class ShippingController(PackagingOwnerV1 owner) : ControllerBase
{
    [HttpGet("owner/v1/packages")]
    public async Task<IActionResult> SearchCurrent([FromQuery] PackagingQueryV1 query,CancellationToken ct)
        =>Ok(new{code=0,data=await owner.SearchAsync(query,ct)});
    [HttpGet("owner/v1/packages/{no}")]
    public async Task<IActionResult> GetCurrent(string no,CancellationToken ct)
        =>Ok(new{code=0,data=await owner.GetAsync(no,ct)});
    [HttpGet("packages")]
    public async Task<IActionResult> SearchPackages(CancellationToken ct)
    {await owner.RejectLegacyAsync(ct:ct);return NoContent();}
    [HttpGet("packages/{no}")]
    public async Task<IActionResult> GetPackage(string no,CancellationToken ct)
    {await owner.RejectLegacyAsync(no,ct);return NoContent();}
}
