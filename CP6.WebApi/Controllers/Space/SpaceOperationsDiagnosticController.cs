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
[Route("api/space/operations/v1/sites/{siteId:guid}/diagnostics")]
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
public sealed class SpaceOperationsDiagnosticController(
    ISpaceOperationsDiagnosticService service) : ControllerBase
{
    [HttpGet]
    [SpaceAuditOperation(
        "space.operations.diagnostics.read",
        "OperationsDiagnostics",
        ResourceIdArgument = "siteId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:diagnostics:read",
        AuditRead = true)]
    [RequirePermission(
        "space",
        "operations:diagnostics:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceOperationsDiagnosticResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceOperationsDiagnosticResponse> Get(
        Guid siteId,
        [FromQuery, Required] DateTimeOffset fromUtc,
        [FromQuery, Required] DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(siteId, fromUtc, toUtc, cancellationToken);
}
