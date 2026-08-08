using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDispatchApprovalService(
    SpaceContext space,
    CP6Context core,
    ISpaceDispatchRecommendationService recommendations,
    ISpaceDispatchTaskAdapter taskAdapter,
    IApprovalService approvals,
    ITaskCenterService taskCenter,
    IWmsAccessScopeProvider accessScopes,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access,
    SpacePersonnelRuntimeOptions personnelOptions)
    : ISpaceDispatchApprovalService, ISpaceDispatchExecutionService
{
    public const string ApprovalBizType = "SPACE_DISPATCH_ASSIGNMENT";
    private const int MaxRetryAttempts = 3;
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task<SubmitSpaceDispatchApprovalResponse> SubmitAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        SubmitSpaceDispatchApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        EnsureIdentity(approvalRequestId, "approvalRequestId");
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);
        personnelOptions.Validate();

        var normalized = Normalize(request);
        var payloadHash = PayloadHash(siteId, recommendationId, normalized);
        var existing = await core.SpaceDispatchApprovalRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == approvalRequestId,
                cancellationToken);
        if (existing is not null)
            return Duplicate(existing, siteId, recommendationId, payloadHash);

        if (await core.SpaceDispatchApprovalRequests.AsNoTracking().AnyAsync(
                value => value.SiteId == siteId &&
                    value.RecommendationId == recommendationId &&
                    value.Status == SpaceDispatchApprovalStatus.PendingApproval,
                cancellationToken))
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalActive,
                409,
                "A dispatch approval request is already active.",
                "wait-for-or-cancel-active-approval");
        }

        var recommendation = await recommendations.GetAsync(
            siteId,
            recommendationId,
            cancellationToken);
        if (!string.Equals(recommendation.Outcome, "AssignmentsGenerated",
                StringComparison.Ordinal) || recommendation.Assignments.Count == 0)
        {
            throw Invalid("The recommendation does not contain assignments.");
        }
        var recommendationRow = await space.DispatchRecommendations
            .AsNoTracking()
            .SingleAsync(value => value.Id == recommendationId && value.SiteId == siteId,
                cancellationToken);
        await EnsurePublishedAsync(siteId, recommendation.PublishedVersionId,
            cancellationToken);

        var byRank = recommendation.Assignments.ToDictionary(value => value.Rank);
        var selected = new List<SpaceDispatchApprovalSelectionSnapshot>(
            normalized.SelectedRanks.Count);
        var scope = await accessScopes.GetCurrentAsync(cancellationToken);
        var now = UtcNow();
        foreach (var rank in normalized.SelectedRanks)
        {
            if (!byRank.TryGetValue(rank, out var assignment))
                throw Invalid($"Selected rank {rank} is not present in the recommendation.");
            selected.Add(await BuildSnapshotAsync(
                siteId,
                approvalRequestId,
                assignment,
                recommendation.WarehouseCode,
                scope,
                now,
                cancellationToken));
        }

        var requester = await core.Sys_Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == execution.ActorId && value.Enable,
                cancellationToken)
            ?? throw Problem(
                SpaceErrorCodes.DispatchApprovalInvalid,
                422,
                "The dispatch approval requester is not an enabled internal user.",
                "use-enabled-internal-user");
        var flowInstanceId = Guid.NewGuid();
        var row = new SpaceDispatchApprovalRequest
        {
            Id = approvalRequestId,
            SiteId = siteId,
            RecommendationId = recommendationId,
            PublishedVersionId = recommendation.PublishedVersionId,
            WarehouseCode = recommendation.WarehouseCode,
            RecommendationDefinitionVersion = recommendation.DefinitionVersion,
            RecommendationRequestHash = recommendationRow.RequestHash,
            PayloadHash = payloadHash,
            SelectionJson = JsonSerializer.Serialize(selected, Json),
            Reason = normalized.Reason,
            Status = SpaceDispatchApprovalStatus.PendingApproval,
            RequestedById = execution.ActorId,
            RequestedAtUtc = now,
            FlowInstanceId = flowInstanceId,
            AdapterId = taskAdapter.AdapterId,
            ResultJson = "[]",
            Creator = requester.UserName,
            CreateDate = now,
        };
        core.SpaceDispatchApprovalRequests.Add(row);
        try
        {
            await approvals.SubmitAsync(
                ApprovalBizType,
                row.Id.ToString("D"),
                execution.ActorId,
                new
                {
                    row.SiteId,
                    row.RecommendationId,
                    row.PublishedVersionId,
                    row.WarehouseCode,
                    row.RecommendationDefinitionVersion,
                    selectedRanks = normalized.SelectedRanks,
                    selectedCount = selected.Count,
                    row.Reason,
                    row.AdapterId,
                },
                flowInstanceId);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.StartsWith("E-WF-", StringComparison.Ordinal))
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalFlowUnavailable,
                503,
                "The dispatch approval workflow is unavailable.",
                "configure-published-dispatch-approval-flow",
                retryable: true);
        }

        return new SubmitSpaceDispatchApprovalResponse("Submitted", Map(row));
    }

    public async Task<SpaceDispatchApprovalRequestDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        EnsureIdentity(approvalRequestId, "approvalRequestId");
        access.EnsureSiteAccess(siteId, write: false);
        var row = await RequiredAsync(
            siteId, recommendationId, approvalRequestId, tracking: false,
            cancellationToken);
        return Map(row);
    }

    public async Task CancelAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        EnsureIdentity(approvalRequestId, "approvalRequestId");
        access.EnsureSiteAccess(siteId, write: true);
        var row = await RequiredAsync(
            siteId, recommendationId, approvalRequestId, tracking: true,
            cancellationToken);
        if (row.RequestedById != execution.ActorId)
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalCancelForbidden,
                403,
                "Only the requester can cancel this dispatch approval.",
                "ask-requester-to-cancel");
        }
        if (row.Status != SpaceDispatchApprovalStatus.PendingApproval)
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalNotPending,
                409,
                "The dispatch approval is no longer pending.",
                "refresh-approval-status");
        }

        row.Status = SpaceDispatchApprovalStatus.Cancelled;
        row.Modifier = execution.ActorId.ToString("D");
        row.ModifyDate = UtcNow();
        await taskCenter.WithdrawAsync(row.FlowInstanceId, execution.ActorId);
    }

    public async Task<SpaceDispatchExecutionDto> GetExecutionAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        EnsureIdentity(approvalRequestId, "approvalRequestId");
        access.EnsureSiteAccess(siteId, write: false);
        var row = await RequiredAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            tracking: false,
            cancellationToken);
        return await BuildExecutionAsync(row, cancellationToken);
    }

    public Task<SpaceDispatchExecutionActionResponse> RetryAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        Guid actionId,
        SubmitSpaceDispatchExecutionActionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            actionId,
            SpaceDispatchExecutionActionType.RetryAssignment,
            request,
            cancellationToken);

    public Task<SpaceDispatchExecutionActionResponse> CompensateAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        Guid actionId,
        SubmitSpaceDispatchExecutionActionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            actionId,
            SpaceDispatchExecutionActionType.CompensateAssignment,
            request,
            cancellationToken);

    private async Task<SpaceDispatchExecutionActionResponse> ExecuteActionAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        Guid actionId,
        string actionType,
        SubmitSpaceDispatchExecutionActionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureInternalExecution();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(recommendationId, "recommendationId");
        EnsureIdentity(approvalRequestId, "approvalRequestId");
        EnsureIdentity(actionId, "actionId");
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);

        var reason = NormalizeActionReason(request.Reason);
        var payloadHash = ActionPayloadHash(
            siteId,
            recommendationId,
            approvalRequestId,
            actionType,
            reason);
        var existing = await core.SpaceDispatchExecutionActions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == actionId,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.SiteId != siteId ||
                existing.RecommendationId != recommendationId ||
                existing.ApprovalRequestId != approvalRequestId ||
                !string.Equals(existing.ActionType, actionType,
                    StringComparison.Ordinal) ||
                !string.Equals(existing.PayloadHash, payloadHash,
                    StringComparison.Ordinal))
            {
                throw ExecutionProblem(
                    SpaceErrorCodes.DispatchExecutionConflict,
                    409,
                    "The dispatch execution action id is already used.",
                    "use-a-new-action-id");
            }
            var replayApproval = await RequiredAsync(
                siteId,
                recommendationId,
                approvalRequestId,
                tracking: false,
                cancellationToken);
            return new SpaceDispatchExecutionActionResponse(
                "Duplicate",
                MapAction(existing),
                await BuildExecutionAsync(replayApproval, cancellationToken));
        }

        var approval = await RequiredAsync(
            siteId,
            recommendationId,
            approvalRequestId,
            tracking: true,
            cancellationToken);
        return actionType == SpaceDispatchExecutionActionType.RetryAssignment
            ? await ExecuteRetryAsync(
                approval,
                actionId,
                reason,
                payloadHash,
                cancellationToken)
            : await ExecuteCompensationAsync(
                approval,
                actionId,
                reason,
                payloadHash,
                cancellationToken);
    }

    private async Task<SpaceDispatchExecutionActionResponse> ExecuteRetryAsync(
        SpaceDispatchApprovalRequest approval,
        Guid actionId,
        string reason,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        if (approval.Status != SpaceDispatchApprovalStatus.FailedNoEffect)
        {
            throw ExecutionProblem(
                SpaceErrorCodes.DispatchExecutionRetryUnavailable,
                409,
                "Only a failed-no-effect dispatch assignment can be retried.",
                "refresh-execution-status");
        }
        if (approval.RetryAttemptCount >= MaxRetryAttempts)
        {
            throw ExecutionProblem(
                SpaceErrorCodes.DispatchExecutionRetryLimit,
                409,
                "The dispatch assignment retry limit has been reached.",
                "review-failure-and-create-a-new-recommendation");
        }

        var now = UtcNow();
        var selections = Selections(approval);
        var action = NewAction(
            approval,
            actionId,
            SpaceDispatchExecutionActionType.RetryAssignment,
            reason,
            payloadHash,
            now);
        approval.RetryAttemptCount++;
        approval.Modifier = execution.ActorId.ToString("D");
        approval.ModifyDate = now;

        var validationFailure = await ValidateForApplicationAsync(
            approval,
            selections,
            now,
            cancellationToken);
        if (validationFailure is not null)
        {
            approval.Status = SpaceDispatchApprovalStatus.Stale;
            approval.FailureCode = validationFailure;
            action.Status = SpaceDispatchExecutionActionStatus.RejectedNoEffect;
            action.FailureCode = validationFailure;
        }
        else
        {
            try
            {
                var result = await taskAdapter.StageAssignmentsAsync(
                    new SpaceDispatchTaskAdapterCommand(
                        approval.Id,
                        approval.WarehouseCode,
                        execution.ActorId.ToString("D"),
                        now,
                        selections.Select(ToCommand).ToArray()),
                    cancellationToken);
                ValidateAssignmentResult(approval, selections, result);
                approval.ResultJson = JsonSerializer.Serialize(result.Receipts, Json);
                approval.Status = SpaceDispatchApprovalStatus.Applied;
                approval.AppliedAtUtc = now;
                approval.FailureCode = null;
                action.Status = SpaceDispatchExecutionActionStatus.Applied;
                action.ReceiptJson = JsonSerializer.Serialize(result.Receipts, Json);
            }
            catch (SpaceDispatchTaskAdapterException exception)
            {
                approval.Status = exception.Stale
                    ? SpaceDispatchApprovalStatus.Stale
                    : SpaceDispatchApprovalStatus.FailedNoEffect;
                approval.FailureCode = exception.Code;
                action.Status = exception.Stale
                    ? SpaceDispatchExecutionActionStatus.RejectedNoEffect
                    : SpaceDispatchExecutionActionStatus.FailedNoEffect;
                action.FailureCode = exception.Code;
            }
        }

        core.SpaceDispatchExecutionActions.Add(action);
        await core.SaveChangesAsync(cancellationToken);
        return new SpaceDispatchExecutionActionResponse(
            "Executed",
            MapAction(action),
            await BuildExecutionAsync(approval, cancellationToken));
    }

    private async Task<SpaceDispatchExecutionActionResponse>
        ExecuteCompensationAsync(
            SpaceDispatchApprovalRequest approval,
            Guid actionId,
            string reason,
            string payloadHash,
            CancellationToken cancellationToken)
    {
        if (approval.Status != SpaceDispatchApprovalStatus.Applied)
        {
            throw ExecutionProblem(
                SpaceErrorCodes.DispatchExecutionCompensationUnavailable,
                409,
                "Only an applied dispatch assignment can be compensated.",
                "refresh-execution-status");
        }

        var now = UtcNow();
        var selections = Selections(approval);
        var action = NewAction(
            approval,
            actionId,
            SpaceDispatchExecutionActionType.CompensateAssignment,
            reason,
            payloadHash,
            now);
        try
        {
            var result = await taskAdapter.StageCompensationAsync(
                new SpaceDispatchTaskCompensationCommand(
                    approval.Id,
                    actionId,
                    approval.WarehouseCode,
                    execution.ActorId.ToString("D"),
                    now,
                    selections.Select(value => ToCompensationCommand(
                        actionId,
                        value)).ToArray()),
                cancellationToken);
            ValidateCompensationResult(actionId, approval, selections, result);
            approval.Status = SpaceDispatchApprovalStatus.Compensated;
            approval.CompensatedById = execution.ActorId;
            approval.CompensatedAtUtc = now;
            approval.CompensationReason = reason;
            approval.FailureCode = null;
            approval.Modifier = execution.ActorId.ToString("D");
            approval.ModifyDate = now;
            action.Status = SpaceDispatchExecutionActionStatus.Applied;
            action.ReceiptJson = JsonSerializer.Serialize(result.Receipts, Json);
        }
        catch (SpaceDispatchTaskAdapterException exception)
        {
            action.Status = exception.Stale
                ? SpaceDispatchExecutionActionStatus.RejectedNoEffect
                : SpaceDispatchExecutionActionStatus.FailedNoEffect;
            action.FailureCode = exception.Code;
        }

        core.SpaceDispatchExecutionActions.Add(action);
        await core.SaveChangesAsync(cancellationToken);
        return new SpaceDispatchExecutionActionResponse(
            "Executed",
            MapAction(action),
            await BuildExecutionAsync(approval, cancellationToken));
    }

    public async Task ApplyApprovedAsync(
        Guid approvalRequestId,
        ApprovalCallbackContext context,
        CancellationToken cancellationToken = default)
    {
        var row = await RequiredCallbackAsync(
            approvalRequestId, context, cancellationToken);
        EnsureApproverSeparation(row, context);
        if (row.Status != SpaceDispatchApprovalStatus.PendingApproval)
        {
            if (row.DecidedById == context.DecidedById &&
                row.Status is SpaceDispatchApprovalStatus.Applied or
                    SpaceDispatchApprovalStatus.Stale or
                    SpaceDispatchApprovalStatus.FailedNoEffect or
                    SpaceDispatchApprovalStatus.Compensated)
            {
                return;
            }
            throw new InvalidOperationException(
                SpaceErrorCodes.DispatchApprovalNotPending);
        }
        var now = UtcNow();
        row.DecidedById = context.DecidedById;
        row.DecidedAtUtc = now;
        row.Modifier = context.DecidedById!.Value.ToString("D");
        row.ModifyDate = now;

        var selections = Selections(row);
        var validationFailure = await ValidateForApplicationAsync(
            row, selections, now, cancellationToken);
        if (validationFailure is not null)
        {
            row.Status = SpaceDispatchApprovalStatus.Stale;
            row.FailureCode = validationFailure;
            return;
        }

        try
        {
            var result = await taskAdapter.StageAssignmentsAsync(
                new SpaceDispatchTaskAdapterCommand(
                    row.Id,
                    row.WarehouseCode,
                    context.DecidedById.Value.ToString("D"),
                    now,
                    selections.Select(ToCommand).ToArray()),
                cancellationToken);
            ValidateAssignmentResult(row, selections, result);
            row.ResultJson = JsonSerializer.Serialize(result.Receipts, Json);
            row.Status = SpaceDispatchApprovalStatus.Applied;
            row.AppliedAtUtc = now;
            row.FailureCode = null;
        }
        catch (SpaceDispatchTaskAdapterException exception)
        {
            row.Status = exception.Stale
                ? SpaceDispatchApprovalStatus.Stale
                : SpaceDispatchApprovalStatus.FailedNoEffect;
            row.FailureCode = exception.Code;
        }
    }

    public async Task ApplyRejectedAsync(
        Guid approvalRequestId,
        ApprovalCallbackContext context,
        CancellationToken cancellationToken = default)
    {
        var row = await RequiredCallbackAsync(
            approvalRequestId, context, cancellationToken);
        EnsureApproverSeparation(row, context);
        if (row.Status != SpaceDispatchApprovalStatus.PendingApproval)
        {
            if (row.Status == SpaceDispatchApprovalStatus.Rejected &&
                row.DecidedById == context.DecidedById)
            {
                return;
            }
            throw new InvalidOperationException(
                SpaceErrorCodes.DispatchApprovalNotPending);
        }
        var now = UtcNow();
        row.Status = SpaceDispatchApprovalStatus.Rejected;
        row.DecidedById = context.DecidedById;
        row.DecidedAtUtc = now;
        row.FailureCode = null;
        row.Modifier = context.DecidedById!.Value.ToString("D");
        row.ModifyDate = now;
    }

    private async Task<SpaceDispatchApprovalSelectionSnapshot> BuildSnapshotAsync(
        Guid siteId,
        Guid approvalRequestId,
        SpaceDispatchRecommendationAssignmentDto assignment,
        string warehouseCode,
        WmsAccessScope scope,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(assignment.PersonSourceKind, "Real",
                StringComparison.Ordinal))
            throw Invalid("Simulated personnel cannot be submitted for approval.");
        var state = await space.PersonnelStates.AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.SiteId == siteId &&
                value.SourceId == assignment.PersonSourceId &&
                value.PersonExternalId == assignment.PersonExternalId,
                cancellationToken)
            ?? throw Invalid("The selected personnel state is no longer available.");
        if (!PersonnelMatches(state, assignment, now) || !state.UserId.HasValue)
            throw Invalid("The selected personnel evidence is stale or not assignable.");

        var user = await core.Sys_Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == state.UserId.Value && value.Enable,
                cancellationToken)
            ?? throw Invalid("The selected personnel user is not enabled.");
        if (string.IsNullOrWhiteSpace(user.UserName) || user.UserName.Trim().Length > 20)
            throw Invalid("The selected personnel user cannot be represented by MobileTask.");

        var task = await core.MobileTasks.AsNoTracking()
            .SingleOrDefaultAsync(value =>
                !value.IsDeleted && value.MobileTaskNo == assignment.TaskId,
                cancellationToken)
            ?? throw Invalid("The selected task is no longer available.");
        if (!TaskMatches(task, assignment, warehouseCode) ||
            !scope.Allows(task.WarehouseCd, task.AreaCd))
            throw Invalid("The selected task evidence is stale or outside WMS scope.");

        return new SpaceDispatchApprovalSelectionSnapshot(
            assignment.Rank,
            OperationId(approvalRequestId, assignment.Rank),
            assignment.TaskId,
            assignment.TaskType,
            assignment.TaskContractVersion,
            assignment.TaskExecutionVersion,
            assignment.TaskRowVersion,
            warehouseCode,
            task.AreaCd,
            assignment.TargetLocationCode,
            assignment.PersonSourceId,
            assignment.PersonSourceKind,
            assignment.PersonExternalId,
            state.UserId.Value,
            user.UserName.Trim(),
            assignment.PersonPositionOccurredAtUtc,
            assignment.PersonPositionReceivedAtUtc,
            assignment.PersonWorkStateOccurredAtUtc,
            assignment.PersonWorkStateReceivedAtUtc);
    }

    private async Task<string?> ValidateForApplicationAsync(
        SpaceDispatchApprovalRequest row,
        IReadOnlyList<SpaceDispatchApprovalSelectionSnapshot> selections,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var recommendation = await space.DispatchRecommendations.AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.Id == row.RecommendationId && value.SiteId == row.SiteId,
                cancellationToken);
        if (recommendation is null ||
            recommendation.PublishedVersionId != row.PublishedVersionId ||
            !string.Equals(recommendation.WarehouseCode, row.WarehouseCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(recommendation.DefinitionVersion,
                row.RecommendationDefinitionVersion, StringComparison.Ordinal) ||
            !string.Equals(recommendation.RequestHash,
                row.RecommendationRequestHash, StringComparison.Ordinal))
        {
            return "SPACE_DISPATCH_RECOMMENDATION_STALE";
        }

        var model = await space.Models.AsNoTracking()
            .SingleOrDefaultAsync(value => value.SiteId == row.SiteId,
                cancellationToken);
        if (model?.CurrentPublishedVersionId != row.PublishedVersionId)
            return "SPACE_DISPATCH_PUBLISHED_STALE";
        if (selections.Count is < 1 or > 100 ||
            selections.Any(value => !string.Equals(
                value.PersonSourceKind, "Real", StringComparison.Ordinal)))
            return "SPACE_DISPATCH_SELECTION_INVALID";

        foreach (var selection in selections)
        {
            var state = await space.PersonnelStates.AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.SiteId == row.SiteId &&
                    value.SourceId == selection.PersonSourceId &&
                    value.PersonExternalId == selection.PersonExternalId,
                    cancellationToken);
            if (state is null || !PersonnelMatches(state, selection, now) ||
                state.UserId != selection.PersonUserId)
                return "SPACE_DISPATCH_PERSON_STALE";
            var user = await core.Sys_Users.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == selection.PersonUserId && value.Enable,
                    cancellationToken);
            if (user is null || !string.Equals(user.UserName, selection.AssignedTo,
                    StringComparison.Ordinal))
                return "SPACE_DISPATCH_ASSIGNEE_STALE";
        }
        return null;
    }

    private async Task EnsurePublishedAsync(
        Guid siteId,
        Guid publishedVersionId,
        CancellationToken cancellationToken)
    {
        var current = await space.Models.AsNoTracking()
            .Where(value => value.SiteId == siteId)
            .Select(value => value.CurrentPublishedVersionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (current != publishedVersionId)
            throw Invalid("The recommendation is not based on the current Published version.");
    }

    private bool PersonnelMatches(
        SpacePersonnelCurrentState state,
        SpaceDispatchRecommendationAssignmentDto assignment,
        DateTime now) =>
        state.SourceKind == SpacePersonnelSourceKind.Real &&
        state.WorkState == SpacePersonnelWorkState.Idle &&
        Fresh(state, now) &&
        Offset(state.PositionOccurredAtUtc) == assignment.PersonPositionOccurredAtUtc &&
        Offset(state.PositionReceivedAtUtc) == assignment.PersonPositionReceivedAtUtc &&
        Offset(state.WorkStateOccurredAtUtc) == assignment.PersonWorkStateOccurredAtUtc &&
        Offset(state.WorkStateReceivedAtUtc) == assignment.PersonWorkStateReceivedAtUtc;

    private bool PersonnelMatches(
        SpacePersonnelCurrentState state,
        SpaceDispatchApprovalSelectionSnapshot selection,
        DateTime now) =>
        state.SourceKind == SpacePersonnelSourceKind.Real &&
        state.WorkState == SpacePersonnelWorkState.Idle &&
        Fresh(state, now) &&
        Offset(state.PositionOccurredAtUtc) == selection.PositionOccurredAtUtc &&
        Offset(state.PositionReceivedAtUtc) == selection.PositionReceivedAtUtc &&
        Offset(state.WorkStateOccurredAtUtc) == selection.WorkStateOccurredAtUtc &&
        Offset(state.WorkStateReceivedAtUtc) == selection.WorkStateReceivedAtUtc;

    private bool Fresh(SpacePersonnelCurrentState state, DateTime now)
    {
        var threshold = personnelOptions.CurrentFreshness;
        return state.PositionOccurredAtUtc.HasValue &&
            state.PositionReceivedAtUtc.HasValue &&
            state.WorkStateOccurredAtUtc.HasValue &&
            state.WorkStateReceivedAtUtc.HasValue &&
            state.PositionOccurredAtUtc.Value <= now &&
            state.PositionReceivedAtUtc.Value <= now &&
            state.WorkStateOccurredAtUtc.Value <= now &&
            state.WorkStateReceivedAtUtc.Value <= now &&
            now - state.PositionOccurredAtUtc.Value <= threshold &&
            now - state.PositionReceivedAtUtc.Value <= threshold &&
            now - state.WorkStateOccurredAtUtc.Value <= threshold &&
            now - state.WorkStateReceivedAtUtc.Value <= threshold;
    }

    private static bool TaskMatches(
        MobileTask task,
        SpaceDispatchRecommendationAssignmentDto assignment,
        string warehouseCode) =>
        task.Status == MobileTaskStatus.Pending &&
        string.IsNullOrWhiteSpace(task.AssignedTo) &&
        string.Equals(task.WarehouseCd, warehouseCode,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(task.TaskType, assignment.TaskType,
            StringComparison.OrdinalIgnoreCase) &&
        task.ContractVersion == assignment.TaskContractVersion &&
        task.ExecutionVersion == assignment.TaskExecutionVersion &&
        RowVersionEquals(task.RowVersion, assignment.TaskRowVersion);

    private static bool RowVersionEquals(byte[]? current, string supplied)
    {
        try
        {
            return current is { Length: > 0 } &&
                current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static SubmitSpaceDispatchApprovalRequest Normalize(
        SubmitSpaceDispatchApprovalRequest request)
    {
        if (request.SelectedRanks is null || request.SelectedRanks.Count is < 1 or > 100)
            throw Invalid("Between 1 and 100 assignment ranks must be selected.");
        var ranks = request.SelectedRanks.Order().ToArray();
        if (ranks.Any(value => value < 1) || ranks.Distinct().Count() != ranks.Length)
            throw Invalid("Selected assignment ranks must be unique positive integers.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl))
            throw Invalid("A reason of at most 500 characters is required.");
        return new SubmitSpaceDispatchApprovalRequest(ranks, reason);
    }

    private SubmitSpaceDispatchApprovalResponse Duplicate(
        SpaceDispatchApprovalRequest existing,
        Guid siteId,
        Guid recommendationId,
        string payloadHash)
    {
        if (existing.SiteId != siteId || existing.RecommendationId != recommendationId ||
            !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalConflict,
                409,
                "The dispatch approval request identity is already in use.",
                "use-new-approval-request-id");
        }
        return new SubmitSpaceDispatchApprovalResponse("Duplicate", Map(existing));
    }

    private async Task<SpaceDispatchApprovalRequest> RequiredAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = core.SpaceDispatchApprovalRequests
            .Where(value => value.Id == approvalRequestId &&
                value.SiteId == siteId && value.RecommendationId == recommendationId);
        if (!tracking) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw Problem(
                SpaceErrorCodes.DispatchApprovalNotFound,
                404,
                "The dispatch approval request was not found.",
                "refresh-approval-request");
    }

    private async Task<SpaceDispatchApprovalRequest> RequiredCallbackAsync(
        Guid approvalRequestId,
        ApprovalCallbackContext callback,
        CancellationToken cancellationToken)
    {
        var row = await core.SpaceDispatchApprovalRequests
            .SingleOrDefaultAsync(value => value.Id == approvalRequestId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                SpaceErrorCodes.DispatchApprovalNotFound);
        if (row.FlowInstanceId != callback.InstanceId)
            throw new InvalidOperationException(
                "SPACE_DISPATCH_APPROVAL_INSTANCE_MISMATCH");
        return row;
    }

    private static void EnsureApproverSeparation(
        SpaceDispatchApprovalRequest row,
        ApprovalCallbackContext callback)
    {
        if (!callback.DecidedById.HasValue ||
            callback.DecidedById.Value == row.RequestedById)
            throw new InvalidOperationException(
                "SPACE_DISPATCH_APPROVER_SEPARATION");
    }

    private static SpaceDispatchTaskAssignmentCommand ToCommand(
        SpaceDispatchApprovalSelectionSnapshot value) =>
        new(
            value.Rank,
            value.OperationId,
            value.TaskId,
            value.TaskType,
            value.TaskContractVersion,
            value.TaskExecutionVersion,
            value.TaskRowVersion,
            value.WarehouseCode,
            value.AreaCode,
            value.AssignedTo,
            value.PersonExternalId);

    private static SpaceDispatchTaskCompensationItem ToCompensationCommand(
        Guid actionId,
        SpaceDispatchApprovalSelectionSnapshot value) =>
        new(
            value.Rank,
            value.OperationId,
            CompensationOperationId(actionId, value.Rank),
            value.TaskId,
            value.TaskType,
            value.TaskExecutionVersion,
            value.WarehouseCode,
            value.AreaCode,
            value.AssignedTo,
            value.PersonExternalId);

    private static void ValidateAssignmentResult(
        SpaceDispatchApprovalRequest approval,
        IReadOnlyList<SpaceDispatchApprovalSelectionSnapshot> selections,
        SpaceDispatchTaskAdapterResult result)
    {
        if (!string.Equals(result.AdapterId, approval.AdapterId,
                StringComparison.Ordinal) ||
            result.Receipts.Count != selections.Count)
        {
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_ADAPTER_RESULT_INVALID");
        }
        var byRank = selections.ToDictionary(value => value.Rank);
        if (result.Receipts.Select(value => value.Rank).Distinct().Count() !=
            result.Receipts.Count)
        {
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_ADAPTER_RESULT_INVALID");
        }
        foreach (var receipt in result.Receipts)
        {
            if (!byRank.TryGetValue(receipt.Rank, out var selection) ||
                !string.Equals(receipt.TaskId, selection.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.PersonExternalId, selection.PersonExternalId,
                    StringComparison.Ordinal) ||
                receipt.OperationId != selection.OperationId ||
                !string.Equals(receipt.Outcome, "Applied", StringComparison.Ordinal))
            {
                throw new SpaceDispatchTaskAdapterException(
                    "SPACE_DISPATCH_ADAPTER_RESULT_INVALID");
            }
        }
    }

    private static void ValidateCompensationResult(
        Guid actionId,
        SpaceDispatchApprovalRequest approval,
        IReadOnlyList<SpaceDispatchApprovalSelectionSnapshot> selections,
        SpaceDispatchTaskAdapterResult result)
    {
        if (!string.Equals(result.AdapterId, approval.AdapterId,
                StringComparison.Ordinal) ||
            result.Receipts.Count != selections.Count)
        {
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_COMPENSATION_RESULT_INVALID");
        }
        var byRank = selections.ToDictionary(value => value.Rank);
        if (result.Receipts.Select(value => value.Rank).Distinct().Count() !=
            result.Receipts.Count)
        {
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_COMPENSATION_RESULT_INVALID");
        }
        foreach (var receipt in result.Receipts)
        {
            if (!byRank.TryGetValue(receipt.Rank, out var selection) ||
                !string.Equals(receipt.TaskId, selection.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.PersonExternalId, selection.PersonExternalId,
                    StringComparison.Ordinal) ||
                receipt.OperationId != CompensationOperationId(actionId, receipt.Rank) ||
                !string.Equals(receipt.Outcome, "Compensated",
                    StringComparison.Ordinal))
            {
                throw new SpaceDispatchTaskAdapterException(
                    "SPACE_DISPATCH_COMPENSATION_RESULT_INVALID");
            }
        }
    }

    private SpaceDispatchExecutionAction NewAction(
        SpaceDispatchApprovalRequest approval,
        Guid actionId,
        string actionType,
        string reason,
        string payloadHash,
        DateTime now) =>
        new()
        {
            Id = actionId,
            TenantId = execution.TenantId,
            ApprovalRequestId = approval.Id,
            SiteId = approval.SiteId,
            RecommendationId = approval.RecommendationId,
            ActionType = actionType,
            PayloadHash = payloadHash,
            Reason = reason,
            Status = SpaceDispatchExecutionActionStatus.RejectedNoEffect,
            RequestedById = execution.ActorId,
            RequestedAtUtc = now,
            AdapterId = approval.AdapterId,
            ReceiptJson = "[]",
            Creator = execution.ActorId.ToString("D"),
            CreateDate = now,
        };

    private async Task<SpaceDispatchExecutionDto> BuildExecutionAsync(
        SpaceDispatchApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        var observedAt = UtcNow();
        var selections = Selections(approval).OrderBy(value => value.Rank).ToArray();
        var taskIds = selections.Select(value => value.TaskId).ToArray();
        var tasks = await core.MobileTasks.AsNoTracking()
            .Where(value => !value.IsDeleted && taskIds.Contains(value.MobileTaskNo))
            .ToArrayAsync(cancellationToken);
        var byTask = tasks.ToDictionary(
            value => value.MobileTaskNo,
            StringComparer.OrdinalIgnoreCase);
        var events = await core.MobileTaskEvents.AsNoTracking()
            .Where(value => taskIds.Contains(value.TaskNo))
            .OrderByDescending(value => value.OccurredAt)
            .ThenByDescending(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var latestEvents = events
            .GroupBy(value => value.TaskNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                value => value.Key,
                value => value.First(),
                StringComparer.OrdinalIgnoreCase);

        var itemDtos = new List<SpaceDispatchExecutionTaskDto>(selections.Length);
        var states = new List<string>(selections.Length);
        foreach (var selection in selections)
        {
            byTask.TryGetValue(selection.TaskId, out var task);
            latestEvents.TryGetValue(selection.TaskId, out var latestEvent);
            var state = TaskExecutionState(approval.Status, selection, task);
            states.Add(state);
            itemDtos.Add(new SpaceDispatchExecutionTaskDto(
                selection.Rank,
                selection.TaskId,
                selection.PersonSourceId,
                selection.PersonExternalId,
                selection.OperationId,
                task?.Status ?? -1,
                state,
                task?.ExecutionVersion ?? selection.TaskExecutionVersion,
                Offset(task?.StartedAt),
                Offset(task?.DoneAt),
                latestEvent?.EventType,
                Offset(latestEvent?.OccurredAt)));
        }

        var actionRows = await core.SpaceDispatchExecutionActions.AsNoTracking()
            .Where(value => value.ApprovalRequestId == approval.Id &&
                value.SiteId == approval.SiteId)
            .OrderBy(value => value.RequestedAtUtc)
            .ThenBy(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var assignmentReceiptsValid = await AssignmentReceiptsValidAsync(
            selections,
            approval.AdapterId,
            cancellationToken);
        var compensationBlockCode = CompensationBlockCode(
            approval.Status,
            states,
            assignmentReceiptsValid);

        var assignedCount = states.Count(value => value == "Assigned");
        var executingCount = states.Count(value => value is "InProgress" or "Paused");
        var completedCount = states.Count(value => value == "Completed");
        var attentionCount = states.Count(IsAttentionState);
        return new SpaceDispatchExecutionDto(
            approval.Id,
            approval.SiteId,
            approval.RecommendationId,
            approval.Status,
            AggregateExecutionStatus(
                approval.Status,
                states,
                assignedCount,
                executingCount,
                completedCount,
                attentionCount),
            Utc(observedAt),
            states.Count,
            assignedCount,
            executingCount,
            completedCount,
            attentionCount,
            approval.Status == SpaceDispatchApprovalStatus.FailedNoEffect &&
                approval.RetryAttemptCount < MaxRetryAttempts,
            approval.RetryAttemptCount,
            Math.Max(0, MaxRetryAttempts - approval.RetryAttemptCount),
            compensationBlockCode is null,
            compensationBlockCode,
            Offset(approval.CompensatedAtUtc),
            itemDtos,
            actionRows.Select(MapAction).ToArray());
    }

    private async Task<bool> AssignmentReceiptsValidAsync(
        IReadOnlyList<SpaceDispatchApprovalSelectionSnapshot> selections,
        string adapterId,
        CancellationToken cancellationToken)
    {
        var operationIds = selections.Select(value => value.OperationId).ToArray();
        var receipts = await core.TaskCommandReceipts.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId))
            .ToArrayAsync(cancellationToken);
        if (receipts.Length != selections.Count) return false;
        var byOperation = receipts.ToDictionary(value => value.OperationId);
        foreach (var selection in selections)
        {
            if (!byOperation.TryGetValue(selection.OperationId, out var receipt) ||
                !string.Equals(receipt.TaskNo, selection.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.CommandName, "space-dispatch-assign",
                    StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                var evidence = JsonSerializer.Deserialize<DispatchReceiptEvidence>(
                    receipt.ResultJson,
                    Json);
                if (evidence is null ||
                    !string.Equals(evidence.TaskNo, selection.TaskId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(evidence.AssignedTo, selection.AssignedTo,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(evidence.Outcome, "Applied", StringComparison.Ordinal) ||
                    !string.Equals(evidence.Source, adapterId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }
        return true;
    }

    private static string TaskExecutionState(
        string approvalStatus,
        SpaceDispatchApprovalSelectionSnapshot selection,
        MobileTask? task)
    {
        if (task is null) return "Missing";
        if (task.ExecutionVersion != selection.TaskExecutionVersion)
            return "Diverged";
        if (task.Status == MobileTaskStatus.Pending)
        {
            if (approvalStatus == SpaceDispatchApprovalStatus.Compensated &&
                string.IsNullOrWhiteSpace(task.AssignedTo))
                return "Compensated";
            if (string.IsNullOrWhiteSpace(task.AssignedTo)) return "Released";
            return string.Equals(task.AssignedTo, selection.AssignedTo,
                StringComparison.OrdinalIgnoreCase)
                ? "Assigned"
                : "Diverged";
        }
        if (!string.Equals(task.AssignedTo, selection.AssignedTo,
                StringComparison.OrdinalIgnoreCase))
            return "Diverged";
        return task.Status switch
        {
            MobileTaskStatus.InProgress => "InProgress",
            MobileTaskStatus.Paused => "Paused",
            MobileTaskStatus.Exception => "Exception",
            MobileTaskStatus.Completed => "Completed",
            MobileTaskStatus.PartiallyCompleted => "PartiallyCompleted",
            MobileTaskStatus.Cancelled => "Cancelled",
            _ => "Diverged",
        };
    }

    private static string? CompensationBlockCode(
        string approvalStatus,
        IReadOnlyList<string> states,
        bool assignmentReceiptsValid)
    {
        if (approvalStatus != SpaceDispatchApprovalStatus.Applied)
            return "SPACE_DISPATCH_COMPENSATION_STATUS";
        var unsafeState = states.FirstOrDefault(value => value != "Assigned");
        if (unsafeState is not null)
            return string.Concat(
                "SPACE_DISPATCH_COMPENSATION_",
                unsafeState.ToUpperInvariant());
        return assignmentReceiptsValid
            ? null
            : "SPACE_DISPATCH_ASSIGNMENT_RECEIPT_INVALID";
    }

    private static bool IsAttentionState(string state) =>
        state is "Missing" or "Diverged" or "Released" or "Exception" or
            "PartiallyCompleted" or "Cancelled";

    private static string AggregateExecutionStatus(
        string approvalStatus,
        IReadOnlyList<string> states,
        int assignedCount,
        int executingCount,
        int completedCount,
        int attentionCount)
    {
        if (approvalStatus == SpaceDispatchApprovalStatus.PendingApproval)
            return "PendingApproval";
        if (approvalStatus == SpaceDispatchApprovalStatus.Rejected)
            return "Rejected";
        if (approvalStatus == SpaceDispatchApprovalStatus.Cancelled)
            return "Cancelled";
        if (approvalStatus == SpaceDispatchApprovalStatus.Stale)
            return "Stale";
        if (approvalStatus == SpaceDispatchApprovalStatus.FailedNoEffect)
            return "AssignmentFailed";
        if (approvalStatus == SpaceDispatchApprovalStatus.Compensated)
            return states.All(value => value == "Compensated")
                ? "Compensated"
                : "AttentionRequired";
        if (approvalStatus != SpaceDispatchApprovalStatus.Applied ||
            states.Count == 0 || attentionCount > 0)
            return "AttentionRequired";
        if (completedCount == states.Count) return "Completed";
        if (executingCount > 0 || completedCount > 0) return "Executing";
        return assignedCount == states.Count ? "Assigned" : "AttentionRequired";
    }

    private static SpaceDispatchExecutionActionDto MapAction(
        SpaceDispatchExecutionAction action)
    {
        var receipts = Deserialize<SpaceDispatchTaskAdapterReceipt[]>(
            action.ReceiptJson,
            "execution action receipt");
        if (receipts.Any(value => value.OperationId == Guid.Empty) ||
            receipts.Select(value => value.OperationId).Distinct().Count() !=
                receipts.Length ||
            receipts.Select(value => value.Rank).Distinct().Count() != receipts.Length)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DispatchExecutionEvidenceInvalid,
                502,
                "The persisted dispatch execution action evidence is invalid.",
                recoveryAction: "contact-support");
        }
        return new SpaceDispatchExecutionActionDto(
            action.Id,
            action.ActionType,
            action.Status,
            action.Reason,
            action.RequestedById,
            Utc(action.RequestedAtUtc),
            action.AdapterId,
            receipts.Select(value => new SpaceDispatchTaskAdaptationReceiptDto(
                value.Rank,
                value.TaskId,
                value.PersonExternalId,
                value.OperationId,
                value.Outcome)).ToArray(),
            action.FailureCode);
    }

    private static SpaceDispatchApprovalRequestDto Map(
        SpaceDispatchApprovalRequest row)
    {
        var selections = Selections(row);
        var receipts = Deserialize<SpaceDispatchTaskAdapterReceipt[]>(
            row.ResultJson, "result");
        if (selections.Count is < 1 or > 100 ||
            selections.Select(value => value.Rank).Distinct().Count() != selections.Count ||
            receipts.Select(value => value.Rank).Distinct().Count() != receipts.Length ||
            receipts.Any(value => selections.All(item => item.Rank != value.Rank)))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsRuntimeContractViolation,
                502,
                "The persisted dispatch approval evidence is invalid.",
                recoveryAction: "contact-support");
        }
        return new SpaceDispatchApprovalRequestDto(
            row.Id,
            row.SiteId,
            row.RecommendationId,
            row.PublishedVersionId,
            row.WarehouseCode,
            row.RecommendationDefinitionVersion,
            row.Status,
            row.Reason,
            row.RequestedById,
            Utc(row.RequestedAtUtc),
            row.FlowInstanceId,
            row.DecidedById,
            Offset(row.DecidedAtUtc),
            Offset(row.AppliedAtUtc),
            row.AdapterId,
            selections.Count,
            selections.Select(value => new SpaceDispatchApprovalSelectionDto(
                value.Rank,
                value.TaskId,
                value.TaskType,
                value.PersonSourceId,
                value.PersonExternalId,
                value.TargetLocationCode)).ToArray(),
            receipts.Select(value => new SpaceDispatchTaskAdaptationReceiptDto(
                value.Rank,
                value.TaskId,
                value.PersonExternalId,
                value.OperationId,
                value.Outcome)).ToArray(),
            row.FailureCode);
    }

    private static IReadOnlyList<SpaceDispatchApprovalSelectionSnapshot> Selections(
        SpaceDispatchApprovalRequest row) =>
        Deserialize<SpaceDispatchApprovalSelectionSnapshot[]>(
            row.SelectionJson, "selection");

    private static T Deserialize<T>(string value, string field)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value, Json)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsRuntimeContractViolation,
                502,
                $"The persisted dispatch approval {field} evidence is invalid.",
                recoveryAction: "contact-support");
        }
    }

    private void EnsureInternalExecution()
    {
        if (execution.IsExternal)
        {
            throw Problem(
                SpaceErrorCodes.DispatchApprovalInternalOnly,
                403,
                "Dispatch approvals are available to internal principals only.",
                "use-internal-operations-principal");
        }
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty ||
            execution.TenantId != space.CurrentTenantId ||
            execution.TenantId != core.CurrentTenantId)
        {
            throw Problem(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                "reauthenticate");
        }
    }

    private DateTime UtcNow()
    {
        var value = clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return value;
    }

    private static Guid OperationId(Guid requestId, int rank)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(requestId.ToString("D"), ":", rank.ToString(CultureInfo.InvariantCulture))));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private static Guid CompensationOperationId(Guid actionId, int rank)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(
            "space-dispatch-compensation:",
            actionId.ToString("D"),
            ":",
            rank.ToString(CultureInfo.InvariantCulture))));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private static string PayloadHash(
        Guid siteId,
        Guid recommendationId,
        SubmitSpaceDispatchApprovalRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            "\n",
            siteId.ToString("D"),
            recommendationId.ToString("D"),
            string.Join(",", request.SelectedRanks.Select(value =>
                value.ToString(CultureInfo.InvariantCulture))),
            request.Reason)))).ToLowerInvariant();

    private static string ActionPayloadHash(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        string actionType,
        string reason) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            "\n",
            siteId.ToString("D"),
            recommendationId.ToString("D"),
            approvalRequestId.ToString("D"),
            actionType,
            reason)))).ToLowerInvariant();

    private static string NormalizeActionReason(string reason)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 500 || normalized.Any(char.IsControl))
        {
            throw ExecutionProblem(
                SpaceErrorCodes.DispatchExecutionInvalid,
                422,
                "A dispatch execution action reason between 1 and 500 characters is required.",
                "review-action-reason");
        }
        return normalized;
    }

    private static void EnsureIdentity(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw Invalid($"{name} is required.");
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Offset(DateTime? value) =>
        value.HasValue ? Utc(value.Value) : null;

    private static SpaceProblemException Invalid(string detail) =>
        Problem(
            SpaceErrorCodes.DispatchApprovalInvalid,
            422,
            "The dispatch approval request is invalid.",
            "refresh-recommendation-and-review-selection",
            detail);

    private static SpaceProblemException Problem(
        string code,
        int status,
        string title,
        string recovery,
        string? detail = null,
        bool retryable = false) =>
        new(code, status, title, detail, recovery, retryable);

    private static SpaceProblemException ExecutionProblem(
        string code,
        int status,
        string title,
        string recovery,
        string? detail = null) =>
        new(code, status, title, detail, recovery);

    private sealed record SpaceDispatchApprovalSelectionSnapshot(
        int Rank,
        Guid OperationId,
        string TaskId,
        string TaskType,
        int TaskContractVersion,
        int TaskExecutionVersion,
        string TaskRowVersion,
        string WarehouseCode,
        string? AreaCode,
        string TargetLocationCode,
        string PersonSourceId,
        string PersonSourceKind,
        string PersonExternalId,
        Guid PersonUserId,
        string AssignedTo,
        DateTimeOffset PositionOccurredAtUtc,
        DateTimeOffset PositionReceivedAtUtc,
        DateTimeOffset WorkStateOccurredAtUtc,
        DateTimeOffset WorkStateReceivedAtUtc);

    private sealed record DispatchReceiptEvidence(
        string TaskNo,
        string? AssignedTo,
        string Outcome,
        string Source);
}
