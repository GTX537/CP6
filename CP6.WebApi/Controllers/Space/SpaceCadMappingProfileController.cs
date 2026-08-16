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
[Route("api/space/design/v1/mapping-profiles/cad")]
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
public sealed class SpaceCadMappingProfileController(
    ISpaceCadMappingProfileService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceCadMappingProfileDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceCadMappingProfileDto>> GetCadMappingProfiles(
        CancellationToken cancellationToken) =>
        service.GetProfilesAsync(cancellationToken);

    [HttpGet("{profileId:guid}")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceCadMappingProfileDto>(StatusCodes.Status200OK)]
    public Task<SpaceCadMappingProfileDto> GetCadMappingProfile(
        Guid profileId,
        [FromQuery] int? version,
        CancellationToken cancellationToken) =>
        service.GetProfileAsync(profileId, version, cancellationToken);

    [HttpPost]
    [SpaceAuditOperation(
        "space.cad-mapping-profile.save",
        "CadMappingProfile",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SaveSpaceCadMappingProfileResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<SaveSpaceCadMappingProfileResponse>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> SaveCadMappingProfile(
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] SaveSpaceCadMappingProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveProfileAsync(
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        if (!result.Created)
            return Ok(result);
        return CreatedAtAction(
            nameof(GetCadMappingProfile),
            new { profileId = result.Profile.Id },
            result);
    }
}
