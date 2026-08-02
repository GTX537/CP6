using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceDispatchOutcomeEvaluationService
{
    Task<SpaceDispatchOutcomeEvaluationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceDispatchEvaluationLocationAnchor(
    Guid LocationLogicalId,
    Guid FloorLogicalId,
    decimal XMillimeters,
    decimal YMillimeters);

public sealed class SpaceDispatchOutcomeEvaluationException(string message)
    : InvalidOperationException(message);

public sealed class SpaceDispatchOutcomeEvaluationEngine
{
    public const string DefinitionVersion =
        "space-dispatch-outcome-evaluation-v1";
    public const string PlannedDistanceBasis =
        "SELECTED_COHORT_STABLE_ORDER_PUBLISHED_GEOMETRY";

    public SpaceDispatchOutcomeEvaluationDto Evaluate(
        SpaceDispatchRecommendationDto recommendation,
        SpaceDispatchApprovalRequestDto approval,
        SpaceDispatchExecutionDto execution,
        IReadOnlyDictionary<Guid, SpaceDispatchEvaluationLocationAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(anchors);

        ValidateIdentity(recommendation, approval, execution);
        var selectedAssignments = ValidateAndSelectAssignments(
            recommendation, approval, execution);
        var limitations = new HashSet<string>(StringComparer.Ordinal)
        {
            "PLANNED_DISTANCE_IS_PUBLISHED_GEOMETRY_NOT_ACTUAL_ROUTE",
            "EXECUTION_METRICS_ARE_CURRENT_AS_OF_NOT_HISTORICAL_CONTROL",
            "ACTUAL_TRAVEL_DISTANCE_NOT_AVAILABLE",
            "THROUGHPUT_UPLIFT_NOT_AVAILABLE",
            "MONETARY_BENEFIT_NOT_AVAILABLE",
        };

        var funnel = BuildFunnel(recommendation, approval, execution);
        var timing = BuildTiming(approval, execution, limitations);
        var plannedDistance = BuildPlannedDistance(
            recommendation,
            selectedAssignments,
            anchors,
            limitations);

        return new SpaceDispatchOutcomeEvaluationDto(
            approval.ApprovalRequestId,
            approval.SiteId,
            approval.RecommendationId,
            approval.PublishedVersionId,
            approval.WarehouseCode,
            approval.Status,
            execution.Status,
            execution.ObservedAtUtc,
            new SpaceDispatchEvaluationEvidenceDto(
                recommendation.GeneratedAtUtc,
                approval.RequestedAtUtc,
                approval.DecidedAtUtc,
                approval.AppliedAtUtc,
                execution.ObservedAtUtc,
                recommendation.DefinitionVersion,
                DefinitionVersion,
                approval.AdapterId),
            funnel,
            timing,
            plannedDistance,
            new SpaceDispatchBenefitBoundaryDto(
                false,
                "TASK_LINKED_ROUTE_TRAJECTORY_NOT_AVAILABLE",
                false,
                "COMPARABLE_HISTORICAL_CONTROL_WINDOW_NOT_AVAILABLE",
                false,
                "LABOR_DEVICE_COST_AND_ATTRIBUTION_BASELINE_NOT_AVAILABLE"),
            limitations.Order(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateIdentity(
        SpaceDispatchRecommendationDto recommendation,
        SpaceDispatchApprovalRequestDto approval,
        SpaceDispatchExecutionDto execution)
    {
        Require(recommendation.RecommendationId != Guid.Empty &&
            recommendation.SiteId != Guid.Empty &&
            recommendation.PublishedVersionId != Guid.Empty,
            "The recommendation identity is invalid.");
        Require(approval.ApprovalRequestId != Guid.Empty &&
            approval.SiteId == recommendation.SiteId &&
            approval.RecommendationId == recommendation.RecommendationId &&
            approval.PublishedVersionId == recommendation.PublishedVersionId &&
            string.Equals(approval.WarehouseCode, recommendation.WarehouseCode,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(approval.RecommendationDefinitionVersion,
                recommendation.DefinitionVersion, StringComparison.Ordinal),
            "The approval does not match its recommendation evidence.");
        Require(execution.ApprovalRequestId == approval.ApprovalRequestId &&
            execution.SiteId == approval.SiteId &&
            execution.RecommendationId == approval.RecommendationId &&
            string.Equals(execution.ApprovalStatus, approval.Status,
                StringComparison.Ordinal) &&
            execution.ObservedAtUtc >= recommendation.GeneratedAtUtc,
            "The execution does not match its approval evidence.");
        Require(approval.RequestedAtUtc >= recommendation.GeneratedAtUtc,
            "The approval predates its recommendation.");
    }

    private static IReadOnlyList<SpaceDispatchRecommendationAssignmentDto>
        ValidateAndSelectAssignments(
            SpaceDispatchRecommendationDto recommendation,
            SpaceDispatchApprovalRequestDto approval,
            SpaceDispatchExecutionDto execution)
    {
        Require(recommendation.ReturnedAssignmentCount ==
                recommendation.Assignments.Count &&
            recommendation.ReturnedAssignmentCount > 0 &&
            recommendation.Assignments.Select(value => value.Rank)
                .Distinct().Count() == recommendation.Assignments.Count,
            "The recommendation assignment evidence is invalid.");
        Require(approval.SelectedCount == approval.Selections.Count &&
            approval.SelectedCount is >= 1 and <= 100 &&
            approval.SelectedCount <= recommendation.ReturnedAssignmentCount &&
            approval.Selections.Select(value => value.Rank)
                .Distinct().Count() == approval.Selections.Count,
            "The approval selection evidence is invalid.");
        Require(execution.TotalCount == execution.Tasks.Count &&
            execution.TotalCount == approval.SelectedCount &&
            execution.Tasks.Select(value => value.Rank).Distinct().Count() ==
                execution.Tasks.Count &&
            execution.AssignedCount == execution.Tasks.Count(value =>
                value.State == "Assigned") &&
            execution.ExecutingCount == execution.Tasks.Count(value =>
                value.State is "InProgress" or "Paused") &&
            execution.CompletedCount == execution.Tasks.Count(value =>
                value.State == "Completed") &&
            execution.AttentionCount == execution.Tasks.Count(value =>
                value.State is "Missing" or "Diverged" or "Released" or
                    "Exception" or "PartiallyCompleted" or "Cancelled"),
            "The execution task evidence is invalid.");

        var recommendationByRank = recommendation.Assignments
            .ToDictionary(value => value.Rank);
        var executionByRank = execution.Tasks.ToDictionary(value => value.Rank);
        var selected = new List<SpaceDispatchRecommendationAssignmentDto>(
            approval.SelectedCount);
        foreach (var selection in approval.Selections.OrderBy(value => value.Rank))
        {
            Require(recommendationByRank.TryGetValue(selection.Rank,
                    out var assignment) &&
                string.Equals(selection.TaskId, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selection.TaskType, assignment.TaskType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selection.PersonSourceId, assignment.PersonSourceId,
                    StringComparison.Ordinal) &&
                string.Equals(selection.PersonExternalId,
                    assignment.PersonExternalId, StringComparison.Ordinal) &&
                string.Equals(selection.TargetLocationCode,
                    assignment.TargetLocationCode,
                    StringComparison.OrdinalIgnoreCase),
                "An approval selection does not match the recommendation.");
            Require(executionByRank.TryGetValue(selection.Rank,
                    out var executionTask) &&
                string.Equals(executionTask.TaskId, selection.TaskId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(executionTask.PersonSourceId,
                    selection.PersonSourceId, StringComparison.Ordinal) &&
                string.Equals(executionTask.PersonExternalId,
                    selection.PersonExternalId, StringComparison.Ordinal),
                "An execution task does not match the approval selection.");
            selected.Add(assignment!);
        }

        Require(selected.Select(value => value.TaskId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == selected.Count &&
            selected.Select(value => (value.PersonSourceId,
                    value.PersonExternalId)).Distinct().Count() == selected.Count,
            "The selected cohort contains duplicate task or person identities.");
        ValidateReceipts(approval);
        return selected;
    }

    private static void ValidateReceipts(SpaceDispatchApprovalRequestDto approval)
    {
        Require(approval.Receipts.Count <= approval.SelectedCount &&
            approval.Receipts.Select(value => value.OperationId)
                .Distinct().Count() == approval.Receipts.Count &&
            approval.Receipts.Select(value => value.Rank)
                .Distinct().Count() == approval.Receipts.Count,
            "The assignment receipt evidence is invalid.");
        var selectionByRank = approval.Selections.ToDictionary(value => value.Rank);
        foreach (var receipt in approval.Receipts)
        {
            Require(receipt.OperationId != Guid.Empty &&
                string.Equals(receipt.Outcome, "Applied", StringComparison.Ordinal) &&
                selectionByRank.TryGetValue(receipt.Rank, out var selection) &&
                string.Equals(receipt.TaskId, selection.TaskId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(receipt.PersonExternalId,
                    selection.PersonExternalId, StringComparison.Ordinal),
                "An assignment receipt does not match its selection.");
        }
    }

    private static SpaceDispatchEvaluationFunnelDto BuildFunnel(
        SpaceDispatchRecommendationDto recommendation,
        SpaceDispatchApprovalRequestDto approval,
        SpaceDispatchExecutionDto execution)
    {
        var appliedReceipts = approval.Receipts.Count(value =>
            string.Equals(value.Outcome, "Applied", StringComparison.Ordinal));
        var started = execution.Tasks.Count(value => value.StartedAtUtc.HasValue);
        var compensated = execution.Tasks.Count(value =>
            string.Equals(value.State, "Compensated", StringComparison.Ordinal));
        return new SpaceDispatchEvaluationFunnelDto(
            recommendation.ReturnedAssignmentCount,
            approval.SelectedCount,
            appliedReceipts,
            started,
            execution.CompletedCount,
            execution.AttentionCount,
            compensated,
            Rate(approval.SelectedCount,
                recommendation.ReturnedAssignmentCount),
            Rate(appliedReceipts, approval.SelectedCount),
            Rate(started, approval.SelectedCount),
            Rate(execution.CompletedCount, approval.SelectedCount));
    }

    private static SpaceDispatchEvaluationTimingDto BuildTiming(
        SpaceDispatchApprovalRequestDto approval,
        SpaceDispatchExecutionDto execution,
        ISet<string> limitations)
    {
        var approvalLead = Duration(
            approval.RequestedAtUtc,
            approval.DecidedAtUtc,
            "APPROVAL_TIMING_INVALID",
            limitations);
        var assignmentLead = Duration(
            approval.RequestedAtUtc,
            approval.AppliedAtUtc,
            "ASSIGNMENT_TIMING_INVALID",
            limitations);

        var startDurations = new List<decimal>();
        var executionDurations = new List<decimal>();
        var completionDurations = new List<decimal>();
        foreach (var task in execution.Tasks)
        {
            if (task.StartedAtUtc.HasValue)
            {
                AddDuration(
                    approval.AppliedAtUtc,
                    task.StartedAtUtc,
                    startDurations,
                    limitations);
            }
            else if (task.DoneAtUtc.HasValue ||
                string.Equals(task.State, "Completed", StringComparison.Ordinal))
            {
                limitations.Add("TASK_TIMING_EVIDENCE_INCOMPLETE");
            }

            if (task.DoneAtUtc.HasValue)
            {
                AddDuration(
                    task.StartedAtUtc,
                    task.DoneAtUtc,
                    executionDurations,
                    limitations);
                AddDuration(
                    approval.AppliedAtUtc,
                    task.DoneAtUtc,
                    completionDurations,
                    limitations);
            }
            else if (string.Equals(task.State, "Completed", StringComparison.Ordinal))
            {
                limitations.Add("TASK_TIMING_EVIDENCE_INCOMPLETE");
            }
        }

        return new SpaceDispatchEvaluationTimingDto(
            approvalLead,
            assignmentLead,
            startDurations.Count,
            Average(startDurations),
            executionDurations.Count,
            Average(executionDurations),
            completionDurations.Count,
            Average(completionDurations));
    }

    private static SpaceDispatchPlannedDistanceComparisonDto
        BuildPlannedDistance(
            SpaceDispatchRecommendationDto recommendation,
            IReadOnlyList<SpaceDispatchRecommendationAssignmentDto> selected,
            IReadOnlyDictionary<Guid, SpaceDispatchEvaluationLocationAnchor> anchors,
            ISet<string> limitations)
    {
        if (selected.Count < 2)
            return Unavailable("COHORT_TOO_SMALL", selected.Count, limitations);
        if (selected.Any(value => !value.SameFloor ||
                !value.GeometricDistanceMeters.HasValue))
        {
            return Unavailable(
                "RECOMMENDATION_DISTANCE_NOT_FULLY_COMPARABLE",
                selected.Count,
                limitations);
        }
        if (selected.Any(value => !value.PersonLocationLogicalId.HasValue))
        {
            return Unavailable(
                "PERSON_LOCATION_ANCHOR_NOT_AVAILABLE",
                selected.Count,
                limitations);
        }

        var taskCohort = selected
            .OrderBy(value => value.TaskId, StringComparer.Ordinal)
            .ToArray();
        var personCohort = selected
            .OrderBy(value => value.PersonSourceId, StringComparer.Ordinal)
            .ThenBy(value => value.PersonExternalId, StringComparer.Ordinal)
            .ToArray();
        var baseline = 0m;
        for (var index = 0; index < taskCohort.Length; index++)
        {
            var task = taskCohort[index];
            var person = personCohort[index];
            if (!anchors.TryGetValue(task.TargetLocationLogicalId,
                    out var taskAnchor) ||
                !person.PersonLocationLogicalId.HasValue ||
                !anchors.TryGetValue(person.PersonLocationLogicalId.Value,
                    out var personAnchor))
            {
                return Unavailable(
                    "PUBLISHED_LOCATION_ANCHOR_NOT_AVAILABLE",
                    selected.Count,
                    limitations);
            }
            Require(taskAnchor.FloorLogicalId == task.TargetFloorLogicalId &&
                personAnchor.FloorLogicalId == person.PersonFloorLogicalId,
                "A Published location anchor does not match persisted evidence.");
            if (taskAnchor.FloorLogicalId != personAnchor.FloorLogicalId)
            {
                return Unavailable(
                    "STABLE_ORDER_PAIR_CROSSES_FLOORS",
                    selected.Count,
                    limitations);
            }
            var distance = Distance(personAnchor, taskAnchor);
            if (recommendation.Request.MaximumTravelDistanceMeters.HasValue &&
                distance > recommendation.Request.MaximumTravelDistanceMeters.Value)
            {
                return Unavailable(
                    "STABLE_ORDER_PAIR_DISTANCE_LIMIT_EXCEEDED",
                    selected.Count,
                    limitations);
            }
            baseline += distance;
        }

        var optimized = selected.Sum(value => value.GeometricDistanceMeters!.Value);
        baseline = Math.Round(baseline, 3, MidpointRounding.AwayFromZero);
        optimized = Math.Round(optimized, 3, MidpointRounding.AwayFromZero);
        var difference = Math.Round(
            baseline - optimized,
            3,
            MidpointRounding.AwayFromZero);
        decimal? differencePercent = baseline == 0
            ? null
            : Math.Round(
                difference * 100m / baseline,
                1,
                MidpointRounding.AwayFromZero);
        var outcome = difference > 0
            ? "Improved"
            : difference < 0
                ? "Regressed"
                : "Neutral";
        return new SpaceDispatchPlannedDistanceComparisonDto(
            "Available",
            PlannedDistanceBasis,
            selected.Count,
            baseline,
            optimized,
            difference,
            differencePercent,
            outcome,
            null);
    }

    private static SpaceDispatchPlannedDistanceComparisonDto Unavailable(
        string reason,
        int cohortCount,
        ISet<string> limitations)
    {
        limitations.Add(reason);
        return new SpaceDispatchPlannedDistanceComparisonDto(
            "Unavailable",
            PlannedDistanceBasis,
            cohortCount,
            null,
            null,
            null,
            null,
            null,
            reason);
    }

    private static decimal Distance(
        SpaceDispatchEvaluationLocationAnchor left,
        SpaceDispatchEvaluationLocationAnchor right)
    {
        var dx = left.XMillimeters - right.XMillimeters;
        var dy = left.YMillimeters - right.YMillimeters;
        return Math.Round(
            (decimal)Math.Sqrt((double)(dx * dx + dy * dy)) / 1_000m,
            3,
            MidpointRounding.AwayFromZero);
    }

    private static decimal Rate(int numerator, int denominator)
    {
        Require(numerator >= 0 && denominator > 0 && numerator <= denominator,
            "A funnel count is outside its denominator.");
        return Math.Round(
            numerator * 100m / denominator,
            1,
            MidpointRounding.AwayFromZero);
    }

    private static decimal? Duration(
        DateTimeOffset from,
        DateTimeOffset? to,
        string invalidCode,
        ISet<string> limitations)
    {
        if (!to.HasValue) return null;
        if (to.Value < from)
        {
            limitations.Add(invalidCode);
            return null;
        }
        return Seconds(to.Value - from);
    }

    private static void AddDuration(
        DateTimeOffset? from,
        DateTimeOffset? to,
        ICollection<decimal> values,
        ISet<string> limitations)
    {
        if (!from.HasValue || !to.HasValue)
        {
            limitations.Add("TASK_TIMING_EVIDENCE_INCOMPLETE");
            return;
        }
        if (to.Value < from.Value)
        {
            limitations.Add("TASK_TIMING_EVIDENCE_INVALID");
            return;
        }
        values.Add(Seconds(to.Value - from.Value));
    }

    private static decimal? Average(IReadOnlyCollection<decimal> values) =>
        values.Count == 0
            ? null
            : Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero);

    private static decimal Seconds(TimeSpan value) =>
        Math.Round((decimal)value.TotalSeconds, 1,
            MidpointRounding.AwayFromZero);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new SpaceDispatchOutcomeEvaluationException(message);
    }
}
