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
public sealed class SpaceCadProviderController(
    ISpaceCadProviderCapabilityService service) : ControllerBase
{
    [HttpGet("cad-capability")]
    [SpaceAuditOperation(
        "space.cad-provider.capability.read",
        "Site",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceCadSiteCapabilityDto>(StatusCodes.Status200OK)]
    public Task<SpaceCadSiteCapabilityDto> GetCapability(
        Guid siteId,
        CancellationToken cancellationToken) =>
        service.GetAsync(siteId, cancellationToken);

    [HttpPut("cad-provider-configuration")]
    [SpaceAuditOperation(
        "space.cad-provider.configuration.replace",
        "Site",
        PermissionCode = "space:model:provider:manage")]
    [RequirePermission(
        "space",
        "model:provider:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<ReplaceSpaceCadProviderConfigurationResponse>(
        StatusCodes.Status200OK)]
    public async Task<ReplaceSpaceCadProviderConfigurationResponse>
        ReplaceProviderConfiguration(
        Guid siteId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody, Required] ReplaceSpaceCadProviderConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ReplaceAsync(
            siteId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return result;
    }
}
