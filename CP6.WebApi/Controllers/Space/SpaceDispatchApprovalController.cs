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
[Route("api/space/operations/v1/sites/{siteId:guid}/dispatch-recommendations/{recommendationId:guid}/approval-requests")]
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
    StatusCodes.Status503ServiceUnavailable,
    "application/problem+json")]
public sealed class SpaceDispatchApprovalController(
    ISpaceDispatchApprovalService service) : ControllerBase
{
    [HttpPut("{approvalRequestId:guid}")]
    [SpaceAuditOperation(
        "space.operations.dispatch-approval.submit",
        "DispatchApprovalRequest",
        ResourceIdArgument = "approvalRequestId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:dispatch:submit")]
    [RequirePermission(
        "space",
        "operations:dispatch:submit",
        UseProblemDetails = true)]
    [ProducesResponseType<SubmitSpaceDispatchApprovalResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SubmitSpaceDispatchApprovalResponse>> Submit(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        [FromBody, Required] SubmitSpaceDispatchApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        Accepted(await service.SubmitAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            request,
            cancellationToken));

    [HttpGet("{approvalRequestId:guid}")]
    [SpaceAuditOperation(
        "space.operations.dispatch-approval.read",
        "DispatchApprovalRequest",
        ResourceIdArgument = "approvalRequestId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:dispatch:read",
        AuditRead = true)]
    [RequirePermission(
        "space",
        "operations:dispatch:read",
        UseProblemDetails = true)]
    [ProducesResponseType<SpaceDispatchApprovalRequestDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceDispatchApprovalRequestDto> Get(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            cancellationToken);

    [HttpPost("{approvalRequestId:guid}/cancel")]
    [SpaceAuditOperation(
        "space.operations.dispatch-approval.cancel",
        "DispatchApprovalRequest",
        ResourceIdArgument = "approvalRequestId",
        SiteIdArgument = "siteId",
        PermissionCode = "space:operations:dispatch:cancel")]
    [RequirePermission(
        "space",
        "operations:dispatch:cancel",
        UseProblemDetails = true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        await service.CancelAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            cancellationToken);
        return NoContent();
    }
}
