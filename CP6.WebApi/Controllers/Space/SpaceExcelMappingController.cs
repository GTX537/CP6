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
[Route("api/space/design/v1/mapping-profiles/excel")]
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
public sealed class SpaceExcelMappingController(
    ISpaceExcelMappingService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceExcelMappingProfileDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceExcelMappingProfileDto>> GetProfiles(
        CancellationToken cancellationToken) =>
        service.GetProfilesAsync(cancellationToken);

    [HttpGet("{profileId:guid}")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceExcelMappingProfileDto>(StatusCodes.Status200OK)]
    public Task<SpaceExcelMappingProfileDto> GetProfile(
        Guid profileId,
        [FromQuery] int? version,
        CancellationToken cancellationToken) =>
        service.GetProfileAsync(profileId, version, cancellationToken);

    [HttpPost("preview")]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceExcelMappingPreviewDto>(StatusCodes.Status200OK)]
    public SpaceExcelMappingPreviewDto Preview(
        [FromBody, Required] PreviewSpaceExcelMappingRequest request) =>
        service.Preview(request);

    [HttpPost]
    [SpaceAuditOperation(
        "space.excel-mapping-profile.save",
        "ExcelMappingProfile",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SaveSpaceExcelMappingProfileResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<SaveSpaceExcelMappingProfileResponse>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> SaveProfile(
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] SaveSpaceExcelMappingProfileRequest request,
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
            nameof(GetProfile),
            new { profileId = result.Profile.Id },
            result);
    }
}
