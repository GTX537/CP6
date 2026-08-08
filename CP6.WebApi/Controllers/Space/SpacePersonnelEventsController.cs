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
[Route("api/space/design/v1/sites/{siteId:guid}/personnel-events")]
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
public sealed class SpacePersonnelEventsController(
    ISpacePersonnelEventService service) : ControllerBase
{
    [HttpPost]
    [SpaceAuditOperation(
        "space.personnel-events.ingest",
        "PersonnelEventBatch",
        PermissionCode = "space:integration:manage")]
    [RequirePermission("space", "integration:manage", UseProblemDetails = true)]
    [ProducesResponseType<IngestSpacePersonnelEventsResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<IngestSpacePersonnelEventsResponse>>
        IngestPersonnelEvents(
            Guid siteId,
            [FromBody, Required] IngestSpacePersonnelEventsRequest request,
            CancellationToken cancellationToken)
    {
        var response = await service.IngestAsync(
            siteId,
            request,
            cancellationToken);
        return Accepted(response);
    }
}
