using CP6.Core.Services.Pub;
using CP6.Core.Services.Plm;
using CP6.WebApi.Controllers.Erp;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pub;
[ApiController, Authorize, Route("api/file-content/owner-v1"), OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileContentController(FileContentOwnerV1 owner) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    [HttpGet("scope")]
    public async Task<IActionResult> Scope(CancellationToken ct) => Result(await owner.ScopeAsync(ct));
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid parentId, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await owner.ListAsync(parentId, page, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? stateVersion, CancellationToken ct) => Result(await owner.ReadAsync(id, stateVersion, ct));
    [HttpPost("uploads"), RequestSizeLimit(20971520)]
    public async Task<IActionResult> Upload([FromHeader(Name = "X-File-Command")] string encoded, CancellationToken ct)
    {
        FileUploadV1? command;
        try
        {
            if (encoded.Length > 32768) throw new FormatException();
            command = JsonSerializer.Deserialize<FileUploadV1>(Convert.FromBase64String(encoded), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        { throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("FILE_UPLOAD_INVALID", 400); }
        if (command == null) throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("FILE_UPLOAD_INVALID", 400);
        return Result(await owner.UploadAsync(command, Request.Body, ct));
    }
    [HttpPost("commands")]
    public async Task<IActionResult> Command([FromBody] FileContentCommandV1 input, CancellationToken ct) => Result(await owner.CommandAsync(input, ct));
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] string version, [FromQuery] string digest, CancellationToken ct)
    {
        var value = await owner.DownloadAsync(id, version, digest, ct);
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Content-SHA256"] = value.ContentRef.Digest;
        // Original bytes only, attachment disposition; no active inline preview or public storage path.
        return File(value.Bytes, "application/octet-stream", value.FileName, enableRangeProcessing: false);
    }
    [HttpGet("operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
