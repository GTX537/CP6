using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/flow-admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FlowAdminController : ControllerBase
{
    [HttpGet("list"), HttpGet("{flowKey}"), HttpPost("enable")]
    public IActionResult Retired() => LegacyDefinitionResult.Gone();
}
