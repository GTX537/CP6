using System.ComponentModel.DataAnnotations;
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
[Route("api/space/design/v1/sites/{siteId:guid}/personnel")]
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
public sealed class SpacePersonnelRuntimeController(
    ISpacePersonnelRuntimeService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePersonnelCurrentPageDto>(StatusCodes.Status200OK)]
    public Task<SpacePersonnelCurrentPageDto> GetCurrentPersonnel(
        Guid siteId,
        [FromQuery] string? sourceKind = null,
        [FromQuery] string? workState = null,
        [FromQuery] Guid? floorLogicalId = null,
        [FromQuery] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetCurrentAsync(
            siteId,
            sourceKind,
            workState,
            floorLogicalId,
            limit,
            cursor,
            cancellationToken);

    [HttpGet("trajectory")]
    [SpaceAuditOperation(
        "space.personnel.trajectory.read",
        "PersonnelTrajectory",
        ResourceIdArgument = "personExternalId",
        SiteIdArgument = "siteId",
        PermissionCode = "space-audit:read",
        AuditRead = true)]
    [RequirePermission("space-audit", "read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePersonnelTrajectoryResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePersonnelTrajectoryResponse> GetPersonnelTrajectory(
        Guid siteId,
        [FromQuery, Required] string personExternalId,
        [FromQuery, Required] string sourceId,
        [FromQuery, Required] DateTimeOffset fromUtc,
        [FromQuery, Required] DateTimeOffset toUtc,
        [FromQuery] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetTrajectoryAsync(
            siteId,
            sourceId,
            personExternalId,
            fromUtc,
            toUtc,
            limit,
            cursor,
            cancellationToken);
}
