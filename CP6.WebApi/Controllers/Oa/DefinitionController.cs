using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

// These unversioned contracts cannot carry the v2 actor-bound command, preview and exact source.
[ApiController, Authorize, Route("api/oa")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DefinitionController : ControllerBase
{
    [HttpGet("flow-defs/{flowKey}/draft"), HttpPut("flow-defs/{flowKey}/draft")]
    [HttpPost("flow-defs/{flowKey}/publish")]
    [HttpGet("flow-defs/{flowKey}/versions"), HttpGet("flow-defs/{flowKey}/versions/{version:int}")]
    [HttpGet("form-defs/{formKey}/draft"), HttpPut("form-defs/{formKey}/draft")]
    [HttpPost("form-defs/{formKey}/publish")]
    [HttpGet("form-defs/{formKey}/versions"), HttpGet("form-defs/{formKey}/versions/{version:int}")]
    public IActionResult Retired() => LegacyDefinitionResult.Gone();
}
