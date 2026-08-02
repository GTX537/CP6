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
public sealed class SpaceAiAdministrationController(
    ISpaceAiAdministrationService service) : ControllerBase
{
    [HttpGet("ai-policy")]
    [RequirePermission("space-ai-admin", "read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiPolicyDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiPolicyDto> GetPolicy(
        CancellationToken cancellationToken) =>
        service.GetPolicyAsync(cancellationToken);

    [HttpPut("ai-policy")]
    [SpaceAuditOperation(
        "space.ai-policy.update",
        "AiTenantPolicy",
        PermissionCode = "space-ai-admin:manage")]
    [RequirePermission("space-ai-admin", "manage", UseProblemDetails = true)]
    [ProducesResponseType<UpdateSpaceAiPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<UpdateSpaceAiPolicyResponse> UpdatePolicy(
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] UpdateSpaceAiPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdatePolicyAsync(
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return result;
    }

    [HttpGet("ai-usage")]
    [RequirePermission("space-ai-admin", "read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiUsagePageDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiUsagePageDto> GetUsage(
        [FromQuery] SpaceAiUsageQuery query,
        CancellationToken cancellationToken) =>
        service.GetUsageAsync(query, cancellationToken);
}
