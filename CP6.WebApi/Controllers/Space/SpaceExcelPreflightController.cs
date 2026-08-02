using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

public sealed class UploadSpaceExcelSourceForm
{
    [Required]
    public required IFormFile File { get; init; }
}

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
[Route("api/space/design/v1")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status400BadRequest,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status401Unauthorized,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status403Forbidden,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status404NotFound,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status409Conflict,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status422UnprocessableEntity,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status500InternalServerError,
    "application/problem+json")]
public sealed class SpaceExcelPreflightController(
    ISpaceExcelPreflightService service) : ControllerBase
{
    private const long ExcelUploadLimit = 50L * 1024L * 1024L;

    [HttpPost("versions/{versionId:guid}/excel-sources")]
    [Consumes("multipart/form-data")]
    [SpaceAuditOperation(
        "space.excel-source.upload",
        "ModelSource",
        PermissionCode = "space:source:upload")]
    [RequirePermission("space", "source:upload", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [RequestSizeLimit(ExcelUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ExcelUploadLimit)]
    [ProducesResponseType<UploadSpaceExcelSourceResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> UploadExcelSource(
        Guid versionId,
        [FromForm] UploadSpaceExcelSourceForm request,
        CancellationToken cancellationToken)
    {
        await using var content = request.File.OpenReadStream();
        var result = await service.UploadAsync(
            versionId,
            request.File.FileName,
            request.File.ContentType,
            content,
            cancellationToken);
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpPost(
        "versions/{versionId:guid}/sources/{sourceId:guid}/excel-preflights")]
    [SpaceAuditOperation(
        "space.excel-preflight.start",
        "ModelSource",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "source:upload", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<StartSpaceExcelPreflightResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartPreflight(
        Guid versionId,
        Guid sourceId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] StartSpaceExcelPreflightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.StartAsync(
            versionId,
            sourceId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpGet(
        "versions/{versionId:guid}/sources/{sourceId:guid}/" +
        "excel-preflights/{jobId:guid}")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceExcelPreflightDto>(StatusCodes.Status200OK)]
    public Task<SpaceExcelPreflightDto> GetPreflight(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        [FromQuery] int issueLimit = 200,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            versionId,
            sourceId,
            jobId,
            issueLimit,
            cancellationToken);

    [HttpGet(
        "versions/{versionId:guid}/sources/{sourceId:guid}/" +
        "excel-preflights/{jobId:guid}/report")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType(
        typeof(FileStreamResult),
        StatusCodes.Status200OK,
        "text/csv")]
    public async Task<IActionResult> DownloadErrorReport(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var report = await service.OpenErrorReportAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = report.FileName,
            }.ToString();
        return File(report.Content, report.ContentType);
    }
}
