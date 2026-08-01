using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
[Route("api/space/portal/v1")]
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
    StatusCodes.Status502BadGateway,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status503ServiceUnavailable,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status500InternalServerError,
    "application/problem+json")]
public sealed class SpaceExternalPortalController(
    ISpaceExternalPortalService portal) : ControllerBase
{
    [HttpGet("organizations")]
    [ProducesResponseType<IReadOnlyList<SpacePortalOrganizationDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpacePortalOrganizationDto>> GetPortalOrganizations(
        CancellationToken cancellationToken) =>
        portal.GetOrganizationsAsync(cancellationToken);

    [HttpGet("sites")]
    [ProducesResponseType<IReadOnlyList<SpacePortalSiteDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpacePortalSiteDto>> GetPortalSites(
        CancellationToken cancellationToken) =>
        portal.GetSitesAsync(cancellationToken);

    [HttpGet("sites/{siteId:guid}/published-scene")]
    [ProducesResponseType<SpacePortalPublishedSceneDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePortalPublishedSceneDto> GetPortalPublishedScene(
        Guid siteId,
        CancellationToken cancellationToken) =>
        portal.GetPublishedSceneAsync(siteId, cancellationToken);

    [HttpGet("sites/{siteId:guid}/stock")]
    [ProducesResponseType<SpacePortalStockResponse>(StatusCodes.Status200OK)]
    public Task<SpacePortalStockResponse> GetPortalStock(
        Guid siteId,
        CancellationToken cancellationToken) =>
        portal.GetStockAsync(siteId, cancellationToken);

    [HttpGet("sites/{siteId:guid}/tasks")]
    [ProducesResponseType<SpacePortalTaskResponse>(StatusCodes.Status200OK)]
    public Task<SpacePortalTaskResponse> GetPortalTasks(
        Guid siteId,
        CancellationToken cancellationToken) =>
        portal.GetTasksAsync(siteId, cancellationToken);
}
