using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

public sealed class UploadSpaceUnderlayForm
{
    [Required]
    public required IFormFile File { get; init; }

    [Required]
    public SpaceSourceType SourceType { get; init; }
}

[ApiController]
[Authorize]
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
public sealed class SpaceDesignV1Controller(
    ISpaceDesignV1Service service,
    ISpaceUnderlayV1Service underlays,
    ISpaceWmsAdoptionService wmsAdoptions,
    ISpaceModelingTemplateService modelingTemplates) : ControllerBase
{
    private const long UnderlayUploadLimit = 100L * 1024L * 1024L;

    [HttpGet("modeling-templates/excel/standard")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType(
        typeof(FileContentResult),
        StatusCodes.Status200OK,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public IActionResult DownloadStandardExcelTemplate()
    {
        var template = modelingTemplates.CreateStandardExcelTemplate();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.Append(
            "X-Space-Template-Schema",
            template.SchemaVersion);
        Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = template.FileName,
            }.ToString();
        return File(
            template.Content,
            template.ContentType,
            template.FileName);
    }

    [HttpGet("sites/{siteId:guid}/model")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceModelDto>(StatusCodes.Status200OK)]
    public Task<SpaceModelDto> GetModel(
        Guid siteId,
        CancellationToken cancellationToken) =>
        service.GetModelAsync(siteId, cancellationToken);

    [HttpGet("sites/{siteId:guid}/versions")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpacePage<SpaceVersionDto>>(StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceVersionDto>> GetVersions(
        Guid siteId,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetVersionsAsync(
            siteId,
            status,
            limit,
            cursor,
            cancellationToken);

    [HttpPost("sites/{siteId:guid}/versions")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<CreateSpaceVersionResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateVersion(
        Guid siteId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceVersionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateVersionAsync(
            siteId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpGet("versions/{versionId:guid}")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceVersionDto>(StatusCodes.Status200OK)]
    public Task<SpaceVersionDto> GetVersion(
        Guid versionId,
        CancellationToken cancellationToken) =>
        service.GetVersionAsync(versionId, cancellationToken);

    [HttpGet("versions/{versionId:guid}/floors/{floorLogicalId:guid}/scene")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceDesignSceneDto>(StatusCodes.Status200OK)]
    public Task<SpaceDesignSceneDto> GetScene(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken) =>
        service.GetSceneAsync(
            versionId,
            floorLogicalId,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/floors/{floorLogicalId:guid}/commands")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<ApplySpaceElementCommandBatchResponse>(
        StatusCodes.Status200OK)]
    public Task<ApplySpaceElementCommandBatchResponse> ApplyElementCommands(
        Guid versionId,
        Guid floorLogicalId,
        [FromBody, Required] ApplySpaceElementCommandBatchRequest request,
        CancellationToken cancellationToken) =>
        service.ApplyElementCommandsAsync(
            versionId,
            floorLogicalId,
            request,
            cancellationToken);

    [HttpGet("assets")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpacePage<SpaceAssetDto>>(StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceAssetDto>> GetAssets(
        [FromQuery] string? scope = null,
        [FromQuery] string? category = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetAssetsAsync(
            scope,
            category,
            limit,
            cursor,
            cancellationToken);

    [HttpPost("assets")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<CreateSpaceAssetResponse>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsset(
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceAssetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAssetAsync(
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return CreatedAtAction(
            nameof(GetAssets),
            routeValues: null,
            value: result);
    }

    [HttpGet("versions/{versionId:guid}/sources")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpacePage<SpaceSourceDto>>(StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceSourceDto>> GetSources(
        Guid versionId,
        [FromQuery] string? sourceType = null,
        [FromQuery] string? state = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetSourcesAsync(
            versionId,
            sourceType,
            state,
            limit,
            cursor,
            cancellationToken);

    [HttpPost("versions/{versionId:guid}/sources")]
    [RequirePermission("space", "source:upload")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<CreateSpaceSourceResponse>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSource(
        Guid versionId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceSourceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateSourceAsync(
            versionId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return CreatedAtAction(
            nameof(GetSources),
            new { versionId },
            result);
    }

    [HttpPost("versions/{versionId:guid}/underlay-sources")]
    [Consumes("multipart/form-data")]
    [RequirePermission("space", "source:upload")]
    [RequirePermission("space", "model:edit")]
    [RequestSizeLimit(UnderlayUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UnderlayUploadLimit)]
    [ProducesResponseType<UploadSpaceUnderlayResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> UploadUnderlay(
        Guid versionId,
        [FromForm] UploadSpaceUnderlayForm request,
        CancellationToken cancellationToken)
    {
        await using var content = request.File.OpenReadStream();
        var result = await underlays.UploadAsync(
            versionId,
            new UploadSpaceUnderlayRequest(
                request.SourceType,
                request.File.FileName,
                request.File.ContentType),
            content,
            cancellationToken);
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpGet("versions/{versionId:guid}/files/{fileId:guid}")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceFileDto>(StatusCodes.Status200OK)]
    public Task<SpaceFileDto> GetFile(
        Guid versionId,
        Guid fileId,
        CancellationToken cancellationToken) =>
        underlays.GetFileAsync(
            versionId,
            fileId,
            cancellationToken);

    [HttpGet("versions/{versionId:guid}/sources/{sourceId:guid}/content")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType(
        typeof(FileStreamResult),
        StatusCodes.Status200OK,
        "application/pdf",
        "image/png",
        "image/jpeg")]
    public async Task<IActionResult> GetUnderlayContent(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var content = await underlays.OpenContentAsync(
            versionId,
            sourceId,
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("inline")
            {
                FileNameStar = content.FileName,
            }.ToString();
        return File(
            content.Content,
            content.ContentType,
            enableRangeProcessing: false);
    }

    [HttpGet(
        "versions/{versionId:guid}/sources/{sourceId:guid}/underlay-calibration")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceUnderlayCalibrationDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceUnderlayCalibrationDto> GetUnderlayCalibration(
        Guid versionId,
        Guid sourceId,
        [FromQuery, Required] Guid floorLogicalId,
        CancellationToken cancellationToken) =>
        underlays.GetCalibrationAsync(
            versionId,
            sourceId,
            floorLogicalId,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/sources/{sourceId:guid}/underlay-calibration")]
    [RequirePermission("space", "source:upload")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<SaveSpaceUnderlayCalibrationResponse>(
        StatusCodes.Status200OK)]
    public async Task<IActionResult> CalibrateUnderlay(
        Guid versionId,
        Guid sourceId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] SaveSpaceUnderlayCalibrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await underlays.CalibrateAsync(
            versionId,
            sourceId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Ok(result);
    }

    [HttpPut(
        "versions/{versionId:guid}/floors/{floorLogicalId:guid}/underlay")]
    [RequirePermission("space", "source:upload")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<AttachSpaceUnderlayResponse>(
        StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachUnderlay(
        Guid versionId,
        Guid floorLogicalId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] AttachSpaceUnderlayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await underlays.AttachAsync(
            versionId,
            floorLogicalId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Ok(result);
    }

    [HttpGet("jobs/{jobId:guid}")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceJobDto>(StatusCodes.Status200OK)]
    public Task<SpaceJobDto> GetJob(
        Guid jobId,
        CancellationToken cancellationToken) =>
        service.GetJobAsync(jobId, cancellationToken);

    [HttpGet("versions/{versionId:guid}/issues")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpacePage<SpaceIssueDto>>(StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceIssueDto>> GetIssues(
        Guid versionId,
        [FromQuery] string? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetIssuesAsync(
            versionId,
            severity,
            status,
            limit,
            cursor,
            cancellationToken);

    [HttpPost("versions/{versionId:guid}/wms-adoption/refresh")]
    [RequirePermission("space", "integration:manage")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<RefreshSpaceWmsAdoptionResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(SpaceDesignProblemDetails),
        StatusCodes.Status502BadGateway,
        "application/problem+json")]
    [ProducesResponseType(
        typeof(SpaceDesignProblemDetails),
        StatusCodes.Status503ServiceUnavailable,
        "application/problem+json")]
    public Task<RefreshSpaceWmsAdoptionResponse> RefreshWmsAdoption(
        Guid versionId,
        CancellationToken cancellationToken) =>
        wmsAdoptions.RefreshAsync(versionId, cancellationToken);

    [HttpGet("versions/{versionId:guid}/wms-adoption/locations")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpacePage<SpaceWmsAdoptionDto>>(
        StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceWmsAdoptionDto>> GetWmsAdoptionLocations(
        Guid versionId,
        [FromQuery] string? status = null,
        [FromQuery] string? differenceCode = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        wmsAdoptions.GetLocationsAsync(
            versionId,
            status,
            differenceCode,
            limit,
            cursor,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/wms-adoption/locations/{adoptionId:guid}/bind")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<SpaceWmsAdoptionCommandResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsAdoptionCommandResponse> BindWmsAdoption(
        Guid versionId,
        Guid adoptionId,
        [FromBody, Required] BindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken) =>
        wmsAdoptions.BindAsync(
            versionId,
            adoptionId,
            request,
            cancellationToken);

    [HttpPost("versions/{versionId:guid}/wms-adoption/bindings:batch")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<SpaceWmsAdoptionCommandResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsAdoptionCommandResponse> BindWmsAdoptionBatch(
        Guid versionId,
        [FromBody, Required] BatchBindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken) =>
        wmsAdoptions.BindBatchAsync(
            versionId,
            request,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/wms-adoption/locations/{adoptionId:guid}/place")]
    [RequirePermission("space", "model:edit")]
    [ProducesResponseType<SpaceWmsAdoptionCommandResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsAdoptionCommandResponse> PlaceWmsAdoption(
        Guid versionId,
        Guid adoptionId,
        [FromBody, Required] PlaceSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken) =>
        wmsAdoptions.PlaceAsync(
            versionId,
            adoptionId,
            request,
            cancellationToken);
}
