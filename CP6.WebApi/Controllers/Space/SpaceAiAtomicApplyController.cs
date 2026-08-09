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
[Route("api/space/design/v1/generation-runs/{runId:guid}")]
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
public sealed class SpaceAiAtomicApplyController(
    ISpaceAiAtomicApplyService service,
    ISpaceAiRunRecoveryService recoveryService) : ControllerBase
{
    [HttpGet]
    [SpaceAuditOperation(
        "space.ai-generation-run.read",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai",
        AuditRead = true)]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiGenerationRunDto> GetGenerationRun(
        Guid runId,
        CancellationToken cancellationToken) =>
        service.GetRunAsync(runId, cancellationToken);

    [HttpPost("apply")]
    [SpaceAuditOperation(
        "space.ai-proposal.apply",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai")]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiAtomicApplyAcceptedDto>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SpaceAiAtomicApplyAcceptedDto>>
        ApplyGenerationProposals(
            Guid runId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] CreateSpaceAiAtomicApplyRequest request,
            CancellationToken cancellationToken)
    {
        var response = await service.QueueAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            response.IdempotentReplay ? "true" : "false";
        return AcceptedAtAction(
            nameof(GetGenerationRun),
            new { runId },
            response);
    }

    [HttpPost("cancel")]
    [SpaceAuditOperation(
        "space.ai-generation-run.cancel",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:generate-ai")]
    [RequirePermission("space", "model:generate-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunActionDto>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<SpaceAiGenerationRunActionDto>>
        CancelGenerationRun(
            Guid runId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] SpaceAiRunActionRequest request,
            CancellationToken cancellationToken)
    {
        var response = await recoveryService.CancelAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        SetReplayHeader(response.IdempotentReplay);
        return Ok(response);
    }

    [HttpPost("retry")]
    [SpaceAuditOperation(
        "space.ai-generation-run.retry",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:generate-ai")]
    [RequirePermission("space", "model:generate-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunActionDto>(
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SpaceAiGenerationRunActionDto>>
        RetryGenerationRun(
            Guid runId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] SpaceAiRunActionRequest request,
            CancellationToken cancellationToken)
    {
        var response = await recoveryService.RetryAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        SetReplayHeader(response.IdempotentReplay);
        return AcceptedAtAction(
            nameof(GetGenerationRun),
            new { runId },
            response);
    }

    [HttpPost("discard")]
    [SpaceAuditOperation(
        "space.ai-generation-run.discard",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:generate-ai")]
    [RequirePermission("space", "model:generate-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunActionDto>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<SpaceAiGenerationRunActionDto>>
        DiscardGenerationRun(
            Guid runId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] SpaceAiRunActionRequest request,
            CancellationToken cancellationToken)
    {
        var response = await recoveryService.DiscardAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        SetReplayHeader(response.IdempotentReplay);
        return Ok(response);
    }

    [HttpPost("reconcile")]
    [SpaceAuditOperation(
        "space.ai-generation-run.reconcile",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai")]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationRunActionDto>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<SpaceAiGenerationRunActionDto>>
        ReconcileGenerationRun(
            Guid runId,
            [FromHeader(Name = "Idempotency-Key"), Required]
            string idempotencyKey,
            [FromBody, Required] SpaceAiRunActionRequest request,
            CancellationToken cancellationToken)
    {
        var response = await recoveryService.ReconcileAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        SetReplayHeader(response.IdempotentReplay);
        return Ok(response);
    }

    private void SetReplayHeader(bool replay) =>
        Response.Headers["Idempotent-Replay"] = replay ? "true" : "false";
}
