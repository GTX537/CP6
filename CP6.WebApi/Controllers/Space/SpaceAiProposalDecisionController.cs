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
public sealed class SpaceAiProposalDecisionController(
    ISpaceAiProposalDecisionService service) : ControllerBase
{
    [HttpGet("review")]
    [SpaceAuditOperation(
        "space.ai-proposal-review.read",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai",
        AuditRead = true)]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiGenerationReviewDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiGenerationReviewDto> GetProposalReview(
        Guid runId,
        CancellationToken cancellationToken) =>
        service.GetReviewAsync(runId, cancellationToken);

    [HttpGet("proposals")]
    [SpaceAuditOperation(
        "space.ai-proposal.list",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai",
        AuditRead = true)]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiProposalPageDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiProposalPageDto> GetGenerationProposals(
        Guid runId,
        [FromQuery] SpaceAiProposalQuery query,
        CancellationToken cancellationToken) =>
        service.GetProposalsAsync(runId, query, cancellationToken);

    [HttpGet("issues")]
    [SpaceAuditOperation(
        "space.ai-proposal-issue.list",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai",
        AuditRead = true)]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiProposalIssuePageDto>(StatusCodes.Status200OK)]
    public Task<SpaceAiProposalIssuePageDto> GetGenerationProposalIssues(
        Guid runId,
        [FromQuery] SpaceAiProposalIssueQuery query,
        CancellationToken cancellationToken) =>
        service.GetIssuesAsync(runId, query, cancellationToken);

    [HttpGet("decisions")]
    [SpaceAuditOperation(
        "space.ai-proposal-decision.list",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai",
        AuditRead = true)]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiProposalDecisionHistoryDto>(
        StatusCodes.Status200OK)]
    public Task<SpaceAiProposalDecisionHistoryDto> GetProposalDecisions(
        Guid runId,
        [FromQuery] Guid? proposalId = null,
        [FromQuery, Range(1, 500)] int limit = 100,
        CancellationToken cancellationToken = default) =>
        service.GetDecisionsAsync(
            runId,
            proposalId,
            limit,
            cancellationToken);

    [HttpPost("decisions")]
    [SpaceAuditOperation(
        "space.ai-proposal.decide",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai")]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiProposalDecisionResponse>(
        StatusCodes.Status200OK)]
    public async Task<SpaceAiProposalDecisionResponse> CreateProposalDecision(
        Guid runId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceAiProposalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateDecisionAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            response.IdempotentReplay ? "true" : "false";
        return response;
    }

    [HttpPost("decisions:batch")]
    [SpaceAuditOperation(
        "space.ai-proposal.decide-batch",
        "GenerationRun",
        ResourceIdArgument = "runId",
        PermissionCode = "space:model:review-ai")]
    [RequirePermission("space", "model:review-ai", UseProblemDetails = true)]
    [RequirePermission("space", "model:edit", UseProblemDetails = true)]
    [ProducesResponseType<SpaceAiProposalDecisionResponse>(
        StatusCodes.Status200OK)]
    public async Task<SpaceAiProposalDecisionResponse> CreateProposalBatchDecision(
        Guid runId,
        [FromHeader(Name = "Idempotency-Key"), Required]
        string idempotencyKey,
        [FromBody, Required] CreateSpaceAiProposalBatchDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateBatchDecisionAsync(
            runId,
            request,
            idempotencyKey,
            cancellationToken);
        Response.Headers["Idempotent-Replay"] =
            response.IdempotentReplay ? "true" : "false";
        return response;
    }
}
