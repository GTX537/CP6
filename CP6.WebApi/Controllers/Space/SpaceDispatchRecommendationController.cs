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
[Route("api/space/operations/v1/sites/{siteId:guid}/dispatch-recommendations")]
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
public sealed class SpaceDispatchRecommendationController(
    ISpaceDispatchRecommendationService service) : ControllerBase
{
    [HttpPut("{recommendationId:guid}")]
    [SpaceAuditOperation(
        "space.operations.dispatch-recommendation.generate",
        "DispatchRecommendation",
        ResourceIdArgument = "recommendationId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:recommendations:generate")]
    [RequirePermission(
        "space",
        "operations:recommendations:generate",
        UseProblemDetails = true)]
    [ProducesResponseType<GenerateSpaceDispatchRecommendationResponse>(
        StatusCodes.Status200OK)]
    public Task<GenerateSpaceDispatchRecommendationResponse> Generate(
        Guid siteId,
        Guid recommendationId,
        [FromBody, Required] GenerateSpaceDispatchRecommendationRequest request,
        CancellationToken cancellationToken = default) =>
        service.GenerateAsync(
            siteId,
            recommendationId,
            request,
            cancellationToken);

    [HttpGet("{recommendationId:guid}")]
    [SpaceAuditOperation(
        "space.operations.dispatch-recommendation.read",
        "DispatchRecommendation",
        ResourceIdArgument = "recommendationId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:recommendations:read",
        AuditRead = true)]
    [RequirePermission(
        "space",
        "operations:recommendations:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceDispatchRecommendationDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceDispatchRecommendationDto> Get(
        Guid siteId,
        Guid recommendationId,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            siteId,
            recommendationId,
            cancellationToken);
}
