using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

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
public sealed class SpacePublishController(
    ISpacePublishOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("versions/{versionId:guid}/publish-attempts")]
    [SpaceAuditOperation(
        "space.publish.start",
        "ModelVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:publish")]
    [RequirePermission(
        "space",
        "model:publish",
        UseProblemDetails = true)]
    [ProducesResponseType<CreateSpacePublishAttemptResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreatePublishAttempt(
        Guid versionId,
        [FromBody] CreateSpacePublishAttemptRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.StartAsync(
            versionId,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return AcceptedAtAction(
            nameof(GetPublishAttempt),
            new { attemptId = result.Attempt.Id },
            result);
    }

    [HttpGet("publish-attempts/{attemptId:guid}")]
    [SpaceAuditOperation(
        "space.publish.read",
        "PublishAttempt",
        ResourceIdArgument = "attemptId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePublishAttemptDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePublishAttemptDto> GetPublishAttempt(
        Guid attemptId,
        CancellationToken cancellationToken) =>
        orchestrator.GetAsync(attemptId, cancellationToken);
}
