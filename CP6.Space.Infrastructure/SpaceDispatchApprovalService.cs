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
    : ISpaceDispatchApprovalService
{
    public const string ApprovalBizType = "SPACE_DISPATCH_ASSIGNMENT";
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

    public async Task ApplyApprovedAsync(
        Guid approvalRequestId,
        ApprovalCallbackContext context,
        CancellationToken cancellationToken = default)
    {
        var row = await RequiredPendingCallbackAsync(
            approvalRequestId, context, cancellationToken);
        EnsureApproverSeparation(row, context);
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
            if (!string.Equals(result.AdapterId, row.AdapterId,
                    StringComparison.Ordinal) ||
                result.Receipts.Count != selections.Count)
            {
                throw new SpaceDispatchTaskAdapterException(
                    "SPACE_DISPATCH_ADAPTER_RESULT_INVALID");
            }
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
        var row = await RequiredPendingCallbackAsync(
            approvalRequestId, context, cancellationToken);
        EnsureApproverSeparation(row, context);
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

    private async Task<SpaceDispatchApprovalRequest> RequiredPendingCallbackAsync(
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
        if (row.Status != SpaceDispatchApprovalStatus.PendingApproval)
            throw new InvalidOperationException(
                SpaceErrorCodes.DispatchApprovalNotPending);
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
}
