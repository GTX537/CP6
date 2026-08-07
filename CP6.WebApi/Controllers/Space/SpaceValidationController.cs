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
public sealed class SpaceValidationController(
    ISpaceValidationService service) : ControllerBase
{
    [HttpPost("versions/{versionId:guid}/validations")]
    [SpaceAuditOperation(
        "space.validation.start",
        "ModelVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:validate")]
    [RequirePermission(
        "space",
        "model:validate",
        UseProblemDetails = true)]
    [ProducesResponseType<CreateSpaceValidationResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateValidation(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestValidationAsync(
            versionId,
            cancellationToken);
        return AcceptedAtAction(
            nameof(GetValidation),
            new { validationId = result.Validation.Id },
            result);
    }

    [HttpGet("validations/{validationId:guid}")]
    [SpaceAuditOperation(
        "space.validation.read",
        "ValidationRun",
        ResourceIdArgument = "validationId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceValidationRunDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceValidationRunDto> GetValidation(
        Guid validationId,
        CancellationToken cancellationToken) =>
        service.GetValidationAsync(validationId, cancellationToken);
}
