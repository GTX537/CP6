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
public sealed class SpacePublishActivityController(
    ISpacePublishActivityService activity) : ControllerBase
{
    [HttpGet("sites/{siteId:guid}/publish-attempts")]
    [SpaceAuditOperation(
        "space.publish.activity.read",
        "Site",
        ResourceIdArgument = "siteId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePage<SpacePublishAttemptSummaryDto>>(
        StatusCodes.Status200OK)]
    public Task<SpacePage<SpacePublishAttemptSummaryDto>> GetPublishAttempts(
        Guid siteId,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        activity.GetBySiteAsync(
            siteId,
            status,
            limit,
            cursor,
            cancellationToken);
}
