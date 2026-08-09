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
[Route("api/space/planning/v1/sites/{siteId:guid}/scenario-branches")]
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
public sealed class SpacePlanningScenarioController(
    ISpacePlanningScenarioService service) : ControllerBase
{
    [HttpPut("{branchId:guid}")]
    [RequirePermission("space", "planning:scenario:create")]
    [ProducesResponseType<CreateSpacePlanningScenarioBranchResponse>(
        StatusCodes.Status200OK)]
    public Task<CreateSpacePlanningScenarioBranchResponse> CreateBranch(
        Guid siteId,
        Guid branchId,
        [FromBody] CreateSpacePlanningScenarioBranchRequest request,
        CancellationToken cancellationToken = default) =>
        service.CreateBranchAsync(
            siteId,
            branchId,
            request,
            cancellationToken);

    [HttpGet("{branchId:guid}")]
    [RequirePermission("space", "planning:scenario:read")]
    [ProducesResponseType<SpacePlanningScenarioBranchDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningScenarioBranchDto> GetBranch(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default) =>
        service.GetBranchAsync(siteId, branchId, cancellationToken);

    [HttpGet]
    [RequirePermission("space", "planning:scenario:read")]
    [ProducesResponseType<SpacePlanningScenarioBranchListResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningScenarioBranchListResponse> GetBranches(
        Guid siteId,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default) =>
        service.GetBranchesAsync(siteId, limit, cancellationToken);
}
