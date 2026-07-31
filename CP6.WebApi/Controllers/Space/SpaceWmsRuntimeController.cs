using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class SpaceDesignV1ContractControllerAttribute :
    Attribute,
    Microsoft.AspNetCore.Mvc.ApplicationModels.IControllerModelConvention
{
    public void Apply(
        Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel controller) =>
        controller.ControllerName = "SpaceDesignV1";
}

[ApiController]
[Authorize]
[SpaceDesignV1ContractController]
[Route("api/space/design/v1/sites/{siteId:guid}/runtime")]
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
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status502BadGateway,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status503ServiceUnavailable,
    "application/problem+json")]
public sealed class SpaceWmsRuntimeController(
    ISpaceWmsRuntimeService runtime) : ControllerBase
{
    [HttpGet("inventory")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceWmsRuntimeInventoryResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeInventoryResponse> GetInventory(
        Guid siteId,
        [FromQuery(Name = "locationLogicalId")] Guid[]?
            locationLogicalIds = null,
        CancellationToken cancellationToken = default) =>
        runtime.QueryInventoryAsync(
            siteId,
            locationLogicalIds,
            cancellationToken);

    [HttpGet("tasks")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceWmsRuntimeTaskResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeTaskResponse> GetTasks(
        Guid siteId,
        [FromQuery(Name = "locationLogicalId")] Guid[]?
            locationLogicalIds = null,
        CancellationToken cancellationToken = default) =>
        runtime.QueryTasksAsync(
            siteId,
            locationLogicalIds,
            cancellationToken);
}
