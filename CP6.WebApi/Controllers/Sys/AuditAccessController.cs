using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/sys/audit/{key}")]
[Authorize]
[OrderOwnerResult]
public sealed class AuditAccessController(AuditBrowseV1 audit) : ControllerBase
{
    [HttpGet("records/{id}")]
    public async Task<IActionResult> Detail(string key, string id, CancellationToken ct)
        => Ok(await audit.ContentAsync(key, id, ct));

    [HttpPost("exports")]
    public async Task<IActionResult> Export(string key, AuditExportRequestV1 input, CancellationToken ct)
        => Ok(await audit.ExportAsync(key, input, ct));

    [HttpGet("exports/{id:guid}/download")]
    public async Task<IActionResult> Download(string key, Guid id, CancellationToken ct)
        => File(await audit.DownloadAsync(key, id, ct), "application/json", $"audit-{id:D}.json");
}
