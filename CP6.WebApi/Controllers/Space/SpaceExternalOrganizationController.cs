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
[Route("api/space/external-organization")]
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
public sealed class SpaceExternalOrganizationController(
    ISpaceExternalOrganizationService service,
    ISpaceExternalGrantService grants) : ControllerBase
{
    [HttpGet]
    [RequirePermission(
        "space",
        "external:read",
        UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceExternalOrganizationDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceExternalOrganizationDto>> GetOrganizations(
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        service.GetOrganizationsAsync(type, status, cancellationToken);

    [HttpGet("{organizationId:guid}")]
    [RequirePermission(
        "space",
        "external:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalOrganizationDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceExternalOrganizationDto> GetOrganization(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        service.GetOrganizationAsync(organizationId, cancellationToken);

    [HttpPost]
    [SpaceAuditOperation(
        "space.external.organization.create",
        "ExternalOrganization",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalOrganizationDto>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrganization(
        [FromBody, Required] CreateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateOrganizationAsync(
            request,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetOrganization),
            new { organizationId = result.Id },
            result);
    }

    [HttpPut("{organizationId:guid}")]
    [SpaceAuditOperation(
        "space.external.organization.update",
        "ExternalOrganization",
        ResourceIdArgument = "organizationId",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalOrganizationDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceExternalOrganizationDto> UpdateOrganization(
        Guid organizationId,
        [FromBody, Required] UpdateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateOrganizationAsync(
            organizationId,
            request,
            cancellationToken);

    [HttpGet("{organizationId:guid}/membership")]
    [RequirePermission(
        "space",
        "external:read",
        UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceExternalMembershipDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceExternalMembershipDto>> GetMemberships(
        Guid organizationId,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        service.GetMembershipsAsync(
            organizationId,
            status,
            cancellationToken);

    [HttpPost("{organizationId:guid}/membership")]
    [SpaceAuditOperation(
        "space.external.membership.create",
        "ExternalMembership",
        ResourceIdArgument = "organizationId",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalMembershipDto>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMembership(
        Guid organizationId,
        [FromBody, Required] CreateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateMembershipAsync(
            organizationId,
            request,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{organizationId:guid}/membership/{membershipId:guid}")]
    [SpaceAuditOperation(
        "space.external.membership.update",
        "ExternalMembership",
        ResourceIdArgument = "membershipId",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalMembershipDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceExternalMembershipDto> UpdateMembership(
        Guid organizationId,
        Guid membershipId,
        [FromBody, Required] UpdateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateMembershipAsync(
            organizationId,
            membershipId,
            request,
            cancellationToken);

    [HttpGet("{organizationId:guid}/grant")]
    [RequirePermission(
        "space",
        "external:read",
        UseProblemDetails = true)]
    [ProducesResponseType<IReadOnlyList<SpaceExternalGrantDto>>(
        StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SpaceExternalGrantDto>> GetGrants(
        Guid organizationId,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        grants.GetGrantsAsync(
            organizationId,
            status,
            cancellationToken);

    [HttpGet("{organizationId:guid}/grant/{grantId:guid}")]
    [RequirePermission(
        "space",
        "external:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalGrantDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceExternalGrantDto> GetGrant(
        Guid organizationId,
        Guid grantId,
        CancellationToken cancellationToken) =>
        grants.GetGrantAsync(
            organizationId,
            grantId,
            cancellationToken);

    [HttpPost("{organizationId:guid}/grant")]
    [SpaceAuditOperation(
        "space.external.grant.create",
        "ExternalGrant",
        ResourceIdArgument = "organizationId",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalGrantDto>(
        StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGrant(
        Guid organizationId,
        [FromBody, Required] CreateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken)
    {
        var result = await grants.CreateGrantAsync(
            organizationId,
            request,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetGrant),
            new
            {
                organizationId,
                grantId = result.Id,
            },
            result);
    }

    [HttpPut("{organizationId:guid}/grant/{grantId:guid}")]
    [SpaceAuditOperation(
        "space.external.grant.update",
        "ExternalGrant",
        ResourceIdArgument = "grantId",
        PermissionCode = "space:external:manage")]
    [RequirePermission(
        "space",
        "external:manage",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceExternalGrantDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceExternalGrantDto> UpdateGrant(
        Guid organizationId,
        Guid grantId,
        [FromBody, Required] UpdateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken) =>
        grants.UpdateGrantAsync(
            organizationId,
            grantId,
            request,
            cancellationToken);
}
