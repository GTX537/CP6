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
[Route("api/space/planning/v1/sites/{siteId:guid}/comparisons")]
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
public sealed class SpacePlanningComparisonController(
    ISpacePlanningComparisonService service) : ControllerBase
{
    [HttpPut("{comparisonId:guid}")]
    [RequirePermission("space", "planning:comparison:create")]
    [ProducesResponseType<CreateSpacePlanningComparisonResponse>(
        StatusCodes.Status200OK)]
    public Task<CreateSpacePlanningComparisonResponse> CreateComparison(
        Guid siteId,
        Guid comparisonId,
        [FromBody] CreateSpacePlanningComparisonRequest request,
        CancellationToken cancellationToken = default) =>
        service.CreateComparisonAsync(
            siteId,
            comparisonId,
            request,
            cancellationToken);

    [HttpGet("{comparisonId:guid}")]
    [RequirePermission("space", "planning:comparison:read")]
    [ProducesResponseType<SpacePlanningComparisonDto>(StatusCodes.Status200OK)]
    public Task<SpacePlanningComparisonDto> GetComparison(
        Guid siteId,
        Guid comparisonId,
        CancellationToken cancellationToken = default) =>
        service.GetComparisonAsync(siteId, comparisonId, cancellationToken);

    [HttpGet]
    [RequirePermission("space", "planning:comparison:read")]
    [ProducesResponseType<SpacePlanningComparisonListResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningComparisonListResponse> GetComparisons(
        Guid siteId,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default) =>
        service.GetComparisonsAsync(siteId, limit, cancellationToken);

    [HttpPut("{comparisonId:guid}/decisions/{decisionId:guid}")]
    [RequirePermission("space", "planning:decision:create")]
    [ProducesResponseType<CreateSpacePlanningDecisionResponse>(
        StatusCodes.Status200OK)]
    public Task<CreateSpacePlanningDecisionResponse> CreateDecision(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        [FromBody] CreateSpacePlanningDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        service.CreateDecisionAsync(
            siteId,
            comparisonId,
            decisionId,
            request,
            cancellationToken);

    [HttpGet("{comparisonId:guid}/decisions/{decisionId:guid}")]
    [RequirePermission("space", "planning:decision:read")]
    [ProducesResponseType<SpacePlanningDecisionDto>(StatusCodes.Status200OK)]
    public Task<SpacePlanningDecisionDto> GetDecision(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        CancellationToken cancellationToken = default) =>
        service.GetDecisionAsync(
            siteId,
            comparisonId,
            decisionId,
            cancellationToken);

    [HttpGet("{comparisonId:guid}/decisions")]
    [RequirePermission("space", "planning:decision:read")]
    [ProducesResponseType<SpacePlanningDecisionListResponse>(
        StatusCodes.Status200OK)]
    public Task<SpacePlanningDecisionListResponse> GetDecisions(
        Guid siteId,
        Guid comparisonId,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default) =>
        service.GetDecisionsAsync(
            siteId,
            comparisonId,
            limit,
            cancellationToken);
}
