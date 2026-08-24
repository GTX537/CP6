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
    StatusCodes.Status503ServiceUnavailable,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status500InternalServerError,
    "application/problem+json")]
public sealed class SpaceExcelCadMatchController(
    ISpaceExcelCadMatchService service,
    ISpaceExcelCadApplyService applyService) : ControllerBase
{
    [HttpPost("versions/{versionId:guid}/excel-cad-matches")]
    [SpaceAuditOperation(
        "space.excel-cad-match.start",
        "ModelVersion",
        ResourceIdArgument = "versionId",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<StartSpaceExcelCadMatchResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartMatch(
        Guid versionId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] StartSpaceExcelCadMatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.StartAsync(
            versionId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpGet("versions/{versionId:guid}/excel-cad-matches/{jobId:guid}")]
    [SpaceAuditOperation(
        "space.excel-cad-match.read",
        "Job",
        ResourceIdArgument = "jobId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceExcelCadMatchDto>(StatusCodes.Status200OK)]
    public Task<SpaceExcelCadMatchDto> GetMatch(
        Guid versionId,
        Guid jobId,
        [FromQuery] string? disposition = null,
        [FromQuery] string? rackCode = null,
        [FromQuery] string? sourceRef = null,
        [FromQuery] bool onlyLocatable = false,
        [FromQuery] int limit = 0,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(
            versionId,
            jobId,
            disposition,
            rackCode,
            sourceRef,
            onlyLocatable,
            limit,
            cursor,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/excel-cad-matches/{matchJobId:guid}/confirmations")]
    [SpaceAuditOperation(
        "space.excel-cad-match.confirm",
        "Job",
        ResourceIdArgument = "matchJobId",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<ConfirmSpaceExcelCadMatchResponse>(
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ConfirmMatch(
        Guid versionId,
        Guid matchJobId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] ConfirmSpaceExcelCadMatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await applyService.ConfirmAsync(
            versionId,
            matchJobId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Accepted(result.JobStatusUrl, result);
    }

    [HttpGet(
        "versions/{versionId:guid}/excel-cad-matches/{matchJobId:guid}/" +
        "confirmations/{applyJobId:guid}")]
    [SpaceAuditOperation(
        "space.excel-cad-match.confirmation.read",
        "Job",
        ResourceIdArgument = "applyJobId",
        PermissionCode = "space:model:read",
        AuditRead = true)]
    [RequirePermission("space", "model:read", UseProblemDetails = true)]
    [ProducesResponseType<SpaceExcelCadApplyDto>(StatusCodes.Status200OK)]
    public Task<SpaceExcelCadApplyDto> GetConfirmation(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CancellationToken cancellationToken = default) =>
        applyService.GetAsync(
            versionId,
            matchJobId,
            applyJobId,
            cancellationToken);

    [HttpPost(
        "versions/{versionId:guid}/excel-cad-matches/{matchJobId:guid}/" +
        "confirmations/{applyJobId:guid}:compensate")]
    [SpaceAuditOperation(
        "space.excel-cad-match.confirmation.compensate",
        "Job",
        ResourceIdArgument = "applyJobId",
        PermissionCode = "space:model:edit")]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<CompensateSpaceExcelCadApplyResponse>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<CompensateSpaceExcelCadApplyResponse>>
        CompensateConfirmation(
            Guid versionId,
            Guid matchJobId,
            Guid applyJobId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] CompensateSpaceExcelCadApplyRequest request,
            CancellationToken cancellationToken)
    {
        var result = await applyService.CompensateAsync(
            versionId,
            matchJobId,
            applyJobId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            result.IdempotentReplay ? "true" : "false";
        return Ok(result);
    }
}
