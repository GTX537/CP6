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
public sealed class SpaceDesignCodingController(
    ISpaceDesignCodingService service) : ControllerBase
{
    [HttpPost(
        "versions/{versionId:guid}/floors/{floorLogicalId:guid}/location-codes:preview")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<PreviewSpaceLocationCodesResponse>(
        StatusCodes.Status200OK)]
    public Task<PreviewSpaceLocationCodesResponse> PreviewLocationCodes(
        Guid versionId,
        Guid floorLogicalId,
        [FromBody, Required] PreviewSpaceLocationCodesRequest request,
        CancellationToken cancellationToken) =>
        service.PreviewLocationCodesAsync(
            versionId,
            floorLogicalId,
            request,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/floors/{floorLogicalId:guid}/location-codes:apply")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<ApplySpaceLocationCodesResponse>(
        StatusCodes.Status200OK)]
    public Task<ApplySpaceLocationCodesResponse> ApplyLocationCodes(
        Guid versionId,
        Guid floorLogicalId,
        [FromBody, Required] ApplySpaceLocationCodesRequest request,
        CancellationToken cancellationToken) =>
        service.ApplyLocationCodesAsync(
            versionId,
            floorLogicalId,
            request,
            cancellationToken);
}
