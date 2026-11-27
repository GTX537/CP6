using System.Security.Cryptography;
using System.Text.Json;
using CP6.WebApi.Controllers.Erp;
using CP6.Core.Services.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;

[ApiController, Authorize, Route("api/engineering-requirements/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EngineeringRequirementController(EngineeringRequirementService owner) : ControllerBase
{
    private IActionResult Result<T>(T value)
    { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet("creation-context")]
    public async Task<IActionResult> CreationContext(CancellationToken ct) => Result(await owner.CreationContextAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? kind, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await owner.ListAsync(kind, search, page, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? version, CancellationToken ct) => Result(await owner.ReadAsync(id, version, ct));
    [HttpGet("{id:guid}/diff")]
    public async Task<IActionResult> Diff(Guid id, [FromQuery] string before, [FromQuery] string after, CancellationToken ct)
        => Result(await owner.DiffAsync(id, before, after, ct));
    [HttpGet("{id:guid}/original")]
    public async Task<IActionResult> Original(Guid id, [FromQuery] string version, [FromQuery] Guid? question, CancellationToken ct)
    {
        var original = await owner.DownloadOriginalAsync(id, version, question, ct);
        Response.Headers["X-Content-SHA256"] = Convert.ToHexStringLower(SHA256.HashData(original.Bytes));
        return Attachment(original.Bytes, "application/octet-stream", original.FileName);
    }
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? kind, [FromQuery] string? search, [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var rows = await owner.ExportAsync(kind, search, page, ct);
        return Attachment(JsonSerializer.SerializeToUtf8Bytes(rows, JsonSerializerOptions.Web), "application/json", "工程需求.json");
    }
    private IActionResult Attachment(byte[] bytes, string type, string name)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, type, name, enableRangeProcessing: false);
    }
    [HttpPost("commands"), RequestSizeLimit(2097152)]
    public async Task<IActionResult> Command([FromBody] EngineeringRequirementCommandV1 command, CancellationToken ct) => Result(await owner.CommandAsync(command, ct));
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
