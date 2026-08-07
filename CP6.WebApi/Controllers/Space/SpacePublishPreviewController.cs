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
public sealed class SpacePublishPreviewController(
    ISpacePublishPreviewService service) : ControllerBase
{
    [HttpGet("versions/{versionId:guid}/publish-preview")]
    [SpaceAuditOperation(
        "space.publish-preview.read",
        "ModelVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePublishPreviewDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePublishPreviewDto> GetPublishPreview(
        Guid versionId,
        [FromQuery] Guid? floorLogicalId = null,
        [FromQuery] string? objectType = null,
        [FromQuery] string? action = null,
        [FromQuery] string? impactCode = null,
        [FromQuery] bool includeNoOp = false,
        [FromQuery] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetPreviewAsync(
            versionId,
            floorLogicalId,
            objectType,
            action,
            impactCode,
            includeNoOp,
            limit,
            cursor,
            cancellationToken);
}
