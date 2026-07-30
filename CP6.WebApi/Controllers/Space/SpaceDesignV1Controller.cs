using System.ComponentModel.DataAnnotations;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

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
    ISpaceDesignV1Service service) : ControllerBase
{
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
}
