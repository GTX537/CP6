using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/currency-policies/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CurrencyPolicyController(CurrencyPolicyOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("creation-context")]
    public async Task<IActionResult> CreationContext(CancellationToken ct) => Result(await owner.CreationContextAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int size = 20, CancellationToken ct = default) => Result(await owner.ListAsync(search, page, size, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpPost("commands"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Command([FromBody] CurrencyPolicyCommandV1 command, CancellationToken ct) => Result(await owner.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
    [HttpPost("quantizations"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Quantize([FromBody] CurrencyQuantizationRequestV1 request, CancellationToken ct) => Result(await owner.QuantizeAsync(request, ct));
}
