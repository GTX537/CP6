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
[SpaceDesignV1Contract]
[Route(
    "api/space/planning/v1/sites/{siteId:guid}/scenario-branches/" +
    "{branchId:guid}/simulation-runs")]
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
public sealed class SpacePlanningSimulationController(
    ISpacePlanningSimulationService service) : ControllerBase
{
    [HttpPut("{runId:guid}")]
    [RequirePermission("space", "planning:simulation:create")]
    [ProducesResponseType<CreateSpacePlanningSimulationRunResponse>(
        StatusCodes.Status200OK)]
    public Task<CreateSpacePlanningSimulationRunResponse> CreateSimulationRun(
        Guid siteId,
        Guid branchId,
        Guid runId,
        [FromBody] CreateSpacePlanningSimulationRunRequest request,
        CancellationToken cancellationToken = default) =>
        service.CreateAsync(
            siteId,
            branchId,
            runId,
            request,
            cancellationToken);

    [HttpGet("{runId:guid}")]
    [RequirePermission("space", "planning:simulation:read")]
    [ProducesResponseType<SpacePlanningSimulationRunDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningSimulationRunDto> GetSimulationRun(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            siteId,
            branchId,
            runId,
            cancellationToken);

    [HttpGet]
    [RequirePermission("space", "planning:simulation:read")]
    [ProducesResponseType<SpacePlanningSimulationRunListResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningSimulationRunListResponse> GetSimulationRuns(
        Guid siteId,
        Guid branchId,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default) =>
        service.GetListAsync(siteId, branchId, limit, cancellationToken);
}
