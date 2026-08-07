using System.ComponentModel.DataAnnotations;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

public sealed class UploadSpaceCadSourceForm
{
    [Required]
    public required string SourceFormat { get; init; }

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
public sealed class SpaceCadParseController(
    ISpaceCadParseService service) : ControllerBase
{
    private const long CadUploadLimit = 100L * 1024L * 1024L;

    [HttpPost("versions/{versionId:guid}/cad-sources")]
    [Consumes("multipart/form-data")]
    [SpaceAuditOperation(
        "space.cad-source.upload",
        "ModelSource",
        PermissionCode = "space:source:upload")]
    [RequirePermission("space", "source:upload", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [RequestSizeLimit(CadUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = CadUploadLimit)]
    [ProducesResponseType<UploadSpaceCadSourceResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> UploadCadSource(
        Guid versionId,
        [FromForm] UploadSpaceCadSourceForm request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SpaceCadSourceFormat>(
                request.SourceFormat,
                ignoreCase: true,
                out var sourceFormat) ||
            !Enum.IsDefined(sourceFormat))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CadParseInvalid,
                422,
                "The CAD source format is invalid.",
                "SourceFormat must be Dwg or Dxf.",
                "correct-cad-source-format");
        }

        await using var content = request.File.OpenReadStream();
        var result = await service.UploadAsync(
            versionId,
            sourceFormat,
            request.File.FileName,
            request.File.ContentType,
            content,
            cancellationToken);
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpPost(
        "versions/{versionId:guid}/sources/{sourceId:guid}/cad-parses")]
    [SpaceAuditOperation(
        "space.cad-parse.start",
        "ModelSource",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "source:upload", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<StartSpaceCadParseResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartParse(
        Guid versionId,
        Guid sourceId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] StartSpaceCadParseRequest request,
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
        "cad-parses/{jobId:guid}")]
    [ProducesResponseType<SpaceCadParseDto>(StatusCodes.Status200OK)]
    public Task<SpaceCadParseDto> GetParse(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        service.GetAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/sources/{sourceId:guid}/" +
        "cad-parses/{jobId:guid}:cancel")]
    [SpaceAuditOperation(
        "space.cad-parse.cancel",
        "Job",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceCadParseActionResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CancelParse(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var result = await service.CancelAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpPost(
        "versions/{versionId:guid}/sources/{sourceId:guid}/" +
        "cad-parses/{jobId:guid}:retry")]
    [SpaceAuditOperation(
        "space.cad-parse.retry",
        "Job",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceCadParseActionResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RetryParse(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await service.RetryAsync(
            versionId,
            sourceId,
            jobId,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Accepted(result.JobStatusUrl, result);
    }
}
