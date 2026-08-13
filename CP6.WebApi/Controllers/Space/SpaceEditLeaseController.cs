using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
[Route("api/space/design/v1/versions/{versionId:guid}/floors/{floorLogicalId:guid}")]
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
public sealed class SpaceEditLeaseController(
    ISpaceEditLeaseService service) : ControllerBase
{
    [HttpGet("lease")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceEditLeaseDto>(StatusCodes.Status200OK)]
    public Task<SpaceEditLeaseDto> GetEditLease(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken) =>
        service.GetAsync(versionId, floorLogicalId, cancellationToken);

    [HttpPost("lease")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceEditLeaseDto>(StatusCodes.Status200OK)]
    public Task<SpaceEditLeaseDto> AcquireEditLease(
        Guid versionId,
        Guid floorLogicalId,
        [FromBody, Required] AcquireSpaceEditLeaseRequest request,
        CancellationToken cancellationToken) =>
        service.AcquireAsync(versionId, floorLogicalId, request, cancellationToken);

    [HttpPost("lease/{leaseId:guid}:renew")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceEditLeaseDto>(StatusCodes.Status200OK)]
    public Task<SpaceEditLeaseDto> RenewEditLease(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        [FromBody, Required] ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken) =>
        service.RenewAsync(
            versionId,
            floorLogicalId,
            leaseId,
            request,
            cancellationToken);

    [HttpPost("lease/{leaseId:guid}:release")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceEditLeaseDto>(StatusCodes.Status200OK)]
    public Task<SpaceEditLeaseDto> ReleaseEditLease(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        [FromBody, Required] ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken) =>
        service.ReleaseAsync(
            versionId,
            floorLogicalId,
            leaseId,
            request,
            cancellationToken);

    [HttpPost("lease:takeover")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [RequirePermission(
        "space",
        "model:lease:takeover",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceEditLeaseDto>(StatusCodes.Status200OK)]
    public Task<SpaceEditLeaseDto> TakeoverEditLease(
        Guid versionId,
        Guid floorLogicalId,
        [FromBody, Required] TakeoverSpaceEditLeaseRequest request,
        CancellationToken cancellationToken) =>
        service.TakeoverAsync(versionId, floorLogicalId, request, cancellationToken);
}
