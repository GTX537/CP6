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
    "{branchId:guid}/historical-datasets")]
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
public sealed class SpacePlanningDatasetController(
    ISpacePlanningDatasetService service) : ControllerBase
{
    [HttpPut("{datasetId:guid}")]
    [RequirePermission("space", "planning:dataset:create")]
    [ProducesResponseType<CreateSpacePlanningHistoricalDatasetResponse>(
        StatusCodes.Status200OK)]
    public Task<CreateSpacePlanningHistoricalDatasetResponse>
        CreateHistoricalDataset(
            Guid siteId,
            Guid branchId,
            Guid datasetId,
            [FromBody] CreateSpacePlanningHistoricalDatasetRequest request,
            CancellationToken cancellationToken = default) =>
        service.CreateAsync(
            siteId,
            branchId,
            datasetId,
            request,
            cancellationToken);

    [HttpGet("{datasetId:guid}")]
    [RequirePermission("space", "planning:dataset:read")]
    [ProducesResponseType<SpacePlanningHistoricalDatasetDto>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningHistoricalDatasetDto> GetHistoricalDataset(
        Guid siteId,
        Guid branchId,
        Guid datasetId,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            siteId,
            branchId,
            datasetId,
            cancellationToken);

    [HttpGet]
    [RequirePermission("space", "planning:dataset:read")]
    [ProducesResponseType<SpacePlanningHistoricalDatasetListResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningHistoricalDatasetListResponse>
        GetHistoricalDatasets(
            Guid siteId,
            Guid branchId,
            [FromQuery, Range(1, 100)] int limit = 50,
            CancellationToken cancellationToken = default) =>
        service.GetListAsync(
            siteId,
            branchId,
            limit,
            cancellationToken);
}
