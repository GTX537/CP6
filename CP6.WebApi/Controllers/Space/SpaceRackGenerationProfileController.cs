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
public sealed class SpaceRackGenerationProfileController(
    ISpaceRackGenerationProfileService service) : ControllerBase
{
    [HttpGet("rack-generation-profiles")]
    [SpaceAuditOperation(
        "space.rack-generation-profile.list",
        "RackGenerationProfile",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpacePage<SpaceRackGenerationProfileDto>>(
        StatusCodes.Status200OK)]
    public Task<SpacePage<SpaceRackGenerationProfileDto>>
        GetRackGenerationProfiles(
        [FromQuery] string? scope = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetProfilesAsync(scope, limit, cursor, cancellationToken);

    [HttpGet("rack-generation-profile-versions/{versionId:guid}")]
    [SpaceAuditOperation(
        "space.rack-generation-profile-version.read",
        "RackGenerationProfileVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceRackGenerationProfileVersionDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceRackGenerationProfileVersionDto>
        GetRackGenerationProfileVersion(
        Guid versionId,
        CancellationToken cancellationToken) =>
        service.GetVersionAsync(versionId, cancellationToken);

    [HttpPost("rack-generation-profiles")]
    [SpaceAuditOperation(
        "space.rack-generation-profile.create",
        "RackGenerationProfile",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<CreateSpaceRackGenerationProfileResponse>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRackGenerationProfile(
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceRackGenerationProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return CreatedAtAction(
            nameof(GetRackGenerationProfileVersion),
            new { versionId = result.Profile.LatestVersion.Id },
            result);
    }
}
