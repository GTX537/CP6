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
[Route("api/space/design/v1/sites/{siteId:guid}")]
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
public sealed class SpaceDeviceEventsController(
    ISpaceDeviceEventService service) : ControllerBase
{
    [HttpGet("device-mappings")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceDeviceMappingPageDto>(StatusCodes.Status200OK)]
    public Task<SpaceDeviceMappingPageDto> GetDeviceMappings(
        Guid siteId,
        [FromQuery] string? sourceId = null,
        [FromQuery] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetMappingsAsync(
            siteId,
            sourceId,
            limit,
            cursor,
            cancellationToken);

    [HttpPost("device-mappings")]
    [SpaceAuditOperation(
        "space.device-mapping.create",
        "DeviceMapping",
        SiteIdArgument = "siteId",
        PermissionCode = "space:integration:manage")]
    [RequirePermission("space", "integration:manage", UseProblemDetails = true)]
    [ProducesResponseType<SpaceDeviceMappingDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SpaceDeviceMappingDto>> CreateDeviceMapping(
        Guid siteId,
        [FromBody, Required] CreateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateMappingAsync(
            siteId,
            request,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetDeviceMappings),
            new { siteId },
            response);
    }

    [HttpPut("device-mappings/{mappingId:guid}")]
    [SpaceAuditOperation(
        "space.device-mapping.update",
        "DeviceMapping",
        ResourceIdArgument = "mappingId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:integration:manage")]
    [RequirePermission("space", "integration:manage", UseProblemDetails = true)]
    [ProducesResponseType<SpaceDeviceMappingDto>(StatusCodes.Status200OK)]
    public Task<SpaceDeviceMappingDto> UpdateDeviceMapping(
        Guid siteId,
        Guid mappingId,
        [FromBody, Required] UpdateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateMappingAsync(
            siteId,
            mappingId,
            request,
            cancellationToken);

    [HttpPost("device-events")]
    [SpaceAuditOperation(
        "space.device-events.ingest",
        "DeviceEventBatch",
        SiteIdArgument = "siteId",
        PermissionCode = "space:integration:manage")]
    [RequirePermission("space", "integration:manage", UseProblemDetails = true)]
    [ProducesResponseType<IngestSpaceDeviceEventsResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<IngestSpaceDeviceEventsResponse>>
        IngestDeviceEvents(
            Guid siteId,
            [FromBody, Required] IngestSpaceDeviceEventsRequest request,
            CancellationToken cancellationToken)
    {
        var response = await service.IngestAsync(
            siteId,
            request,
            cancellationToken);
        return Accepted(response);
    }
}
