using System.ComponentModel.DataAnnotations;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
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
    [HttpPost("inventory")]
    [InventoryBodyLimitProblem]
    [RequestSizeLimit(SpaceWmsRuntimeInventoryQueryRequest.MaxBodyBytes)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceWmsRuntimeInventoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public Task<SpaceWmsRuntimeInventoryResponse> QueryInventory(
        Guid siteId,
        [FromBody, Required] SpaceWmsRuntimeInventoryQueryRequest request,
        CancellationToken cancellationToken = default) =>
        runtime.QueryInventoryAsync(siteId, request.LocationLogicalIds, cancellationToken);

    [HttpGet("inventory")]
    [RequirePermission(
        "space",
        "model:read",
        UseProblemDetails = true)]
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

    [HttpGet("inventory/locate")]
    [RequirePermission(
        "space",
        "model:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceWmsRuntimeInventoryLocateResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventory(
        Guid siteId,
        [FromQuery] string? materialNumber = null,
        [FromQuery] string? lotNumber = null,
        [FromQuery] string? containerNumber = null,
        [FromQuery] string? ownerId = null,
        CancellationToken cancellationToken = default) =>
        runtime.LocateInventoryAsync(
            siteId,
            new SpaceWmsInventoryLocateCriteria(
                materialNumber,
                lotNumber,
                containerNumber,
                ownerId),
            cancellationToken);

    [HttpGet("overview")]
    [RequirePermission(
        "space",
        "model:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceWmsRuntimeWarehouseOverviewResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeWarehouseOverviewResponse> GetWarehouseOverview(
        Guid siteId,
        [FromQuery] int abcWindowDays = 90,
        CancellationToken cancellationToken = default) =>
        runtime.GetWarehouseOverviewAsync(
            siteId,
            abcWindowDays,
            cancellationToken);

    [HttpGet("tasks")]
    [RequirePermission(
        "space",
        "model:read",
        UseProblemDetails = true)]
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

    [HttpGet("tasks/path")]
    [RequirePermission(
        "space",
        "model:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceWmsRuntimeTaskPathResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeTaskPathResponse> GetTaskPath(
        Guid siteId,
        [FromQuery] string taskId,
        CancellationToken cancellationToken = default) =>
        runtime.GetTaskPathAsync(
            siteId,
            taskId,
            cancellationToken);
}

// Model binding reads the body before the action runs. Keep the transport
// rejection local to this endpoint instead of letting the Space catch-all
// middleware turn Kestrel's 413 into an internal server error.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class InventoryBodyLimitProblemAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not BadHttpRequestException
            { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || context.HttpContext.Response.HasStarted)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Request body is too large.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.14",
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }
}
