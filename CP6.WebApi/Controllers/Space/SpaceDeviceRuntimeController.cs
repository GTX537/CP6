using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
[Route("api/space/design/v1/sites/{siteId:guid}")]
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
public sealed class SpaceDeviceRuntimeController(
    ISpaceDeviceRuntimeService service) : ControllerBase
{
    [HttpGet("devices")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceDeviceCurrentPageDto>(StatusCodes.Status200OK)]
    public Task<SpaceDeviceCurrentPageDto> GetCurrentDevices(
        Guid siteId,
        [FromQuery] string? sourceKind = null,
        [FromQuery] string? deviceKind = null,
        [FromQuery] string? operatingState = null,
        [FromQuery] Guid? floorLogicalId = null,
        [FromQuery] bool? hasActiveAlarm = null,
        [FromQuery] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetCurrentAsync(
            siteId,
            sourceKind,
            deviceKind,
            operatingState,
            floorLogicalId,
            hasActiveAlarm,
            limit,
            cursor,
            cancellationToken);
}
