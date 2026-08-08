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
[Route("api/space/design/v1/versions/{versionId:guid}/generation-runs")]
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
public sealed class SpaceAiGenerationRecoveryController(
    ISpaceAiGenerationRunService service) : ControllerBase
{
    [HttpPost]
    [SpaceAuditOperation(
        "space.ai-generation-run.create",
        "ModelVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:generate-ai")]
    [RequirePermission("space", "model:generate-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunAcceptedDto>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SpaceAiGenerationRunAcceptedDto>>
        CreateGenerationRun(
            Guid versionId,
            [FromHeader(Name = "If-Match"), Required]
            string expectedVersionRowVersion,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required]
            CreateSpaceAiGenerationRunRequest request,
            CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(
            versionId,
            request,
            expectedVersionRowVersion,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            response.IdempotentReplay ? "true" : "false";
        return Accepted(
            $"/api/space/design/v1/generation-runs/" +
            $"{response.RunId}",
            response);
    }
}
