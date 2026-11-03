using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/designer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DesignerController(IDesignerService designer) : ControllerBase
{
    [HttpGet("service-catalog")]
    public IActionResult ServiceCatalog() => Ok(new { code = 0, data = designer.GetServiceCatalog() });
    [HttpGet("list"), HttpGet("load/{flowKey}"), HttpPost("save"), HttpPost("clone")]
    public IActionResult Retired() => LegacyDefinitionResult.Gone();
}
