using CP6.Core.Services.Wf;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;
[ApiController, Authorize, OrderOwnerResult, Route("api/oa/flow-defs/{flowKey}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FlowDraftController(FlowDraftOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) => Ok(new { code = 0, data = value });
    [HttpGet("draft-context")]
    public async Task<IActionResult> Context(string flowKey, CancellationToken ct) => Result(await owner.ContextAsync(flowKey, ct));
    // The Owner checks current edit authority inside the actual transaction. Replays need current read only.
    [HttpPost("drafts")]
    public async Task<IActionResult> Create(string flowKey, [FromBody] FlowDraftCreateV1 body, CancellationToken ct) => Result(await owner.CreateAsync(flowKey, body, ct));
    [HttpGet("draft-operations/{operation:guid}")]
    public async Task<IActionResult> Operation(string flowKey, Guid operation, CancellationToken ct) => Result(await owner.OperationAsync(flowKey, operation, ct));
}
