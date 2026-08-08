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
[Route("api/space/field-policy")]
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
public sealed class SpaceFieldPolicyController(
    ISpaceFieldPolicyService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission("space", "external:read", UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceFieldPolicyDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceFieldPolicyDto>> GetFieldPolicies(
        [FromQuery] string? audienceType = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        service.GetPoliciesAsync(audienceType, status, cancellationToken);

    [HttpGet("{policyId:guid}")]
    [RequirePermission("space", "external:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceFieldPolicyDto>(StatusCodes.Status200OK)]
    public Task<SpaceFieldPolicyDto> GetFieldPolicy(
        Guid policyId,
        CancellationToken cancellationToken) =>
        service.GetPolicyAsync(policyId, cancellationToken);

    [HttpPost]
    [SpaceAuditOperation(
        "space.external.field-policy.create",
        "FieldPolicy",
        PermissionCode = "space:external:manage")]
    [RequirePermission("space", "external:manage", UseProblemDetails = true)]
    [ProducesResponseType<SpaceFieldPolicyDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFieldPolicy(
        [FromBody, Required] CreateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreatePolicyAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetFieldPolicy),
            new { policyId = result.Id },
            result);
    }

    [HttpPut("{policyId:guid}")]
    [SpaceAuditOperation(
        "space.external.field-policy.update",
        "FieldPolicy",
        ResourceIdArgument = "policyId",
        PermissionCode = "space:external:manage")]
    [RequirePermission("space", "external:manage", UseProblemDetails = true)]
    [ProducesResponseType<SpaceFieldPolicyDto>(StatusCodes.Status200OK)]
    public Task<SpaceFieldPolicyDto> UpdateFieldPolicy(
        Guid policyId,
        [FromBody, Required] UpdateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken) =>
        service.UpdatePolicyAsync(policyId, request, cancellationToken);
}
