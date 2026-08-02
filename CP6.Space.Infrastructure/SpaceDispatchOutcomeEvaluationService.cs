using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDispatchOutcomeEvaluationService(
    SpaceContext context,
    ISpaceDispatchRecommendationService recommendations,
    ISpaceDispatchApprovalService approvals,
    ISpaceDispatchExecutionService executions,
    SpaceDispatchOutcomeEvaluationEngine engine)
    : ISpaceDispatchOutcomeEvaluationService
{
    public async Task<SpaceDispatchOutcomeEvaluationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        var recommendation = await recommendations.GetAsync(
            siteId, recommendationId, cancellationToken);
        var approval = await approvals.GetAsync(
            siteId, recommendationId, approvalRequestId, cancellationToken);
        var execution = await executions.GetExecutionAsync(
            siteId, recommendationId, approvalRequestId, cancellationToken);

        try
        {
            var anchors = await LoadAnchorsAsync(
                recommendation,
                approval,
                cancellationToken);
            return engine.Evaluate(
                recommendation,
                approval,
                execution,
                anchors);
        }
        catch (SpaceDispatchOutcomeEvaluationException exception)
        {
            throw EvidenceInvalid(exception.Message);
        }
        catch (OverflowException)
        {
            throw EvidenceInvalid(
                "Published geometry is outside the supported evaluation range.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid,
        SpaceDispatchEvaluationLocationAnchor>> LoadAnchorsAsync(
            SpaceDispatchRecommendationDto recommendation,
            SpaceDispatchApprovalRequestDto approval,
            CancellationToken cancellationToken)
    {
        var selectedRanks = approval.Selections
            .Select(value => value.Rank)
            .ToHashSet();
        var selected = recommendation.Assignments
            .Where(value => selectedRanks.Contains(value.Rank))
            .ToArray();
        var locationIds = selected
            .Select(value => (Guid?)value.TargetLocationLogicalId)
            .Concat(selected.Select(value => value.PersonLocationLogicalId))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (locationIds.Length == 0)
        {
            return new Dictionary<Guid,
                SpaceDispatchEvaluationLocationAnchor>();
        }

        var locations = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == recommendation.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                locationIds.Contains(value.LogicalId))
            .Select(value => new LocationRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.RackLogicalId,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo))
            .ToArrayAsync(cancellationToken);
        var rackIds = locations
            .Where(value => value.RackLogicalId.HasValue)
            .Select(value => value.RackLogicalId!.Value)
            .Distinct()
            .ToArray();
        var racks = await context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == recommendation.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                rackIds.Contains(value.LogicalId))
            .Select(value => new RackRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.X,
                value.Y,
                value.RotationZ))
            .ToArrayAsync(cancellationToken);
        var levels = await context.RackLevelRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == recommendation.PublishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                rackIds.Contains(value.RackLogicalId))
            .Select(value => new RackLevelRow(
                value.RackLogicalId,
                value.LevelNo,
                value.CellWidth,
                value.CellDepth))
            .ToArrayAsync(cancellationToken);

        var rackById = racks.ToDictionary(value => value.LogicalId);
        var levelByPosition = levels.ToDictionary(
            value => (value.RackLogicalId, value.LevelNo));
        var result = new Dictionary<Guid,
            SpaceDispatchEvaluationLocationAnchor>();
        foreach (var location in locations)
        {
            if (!location.RackLogicalId.HasValue)
                continue;
            if (!rackById.TryGetValue(location.RackLogicalId.Value,
                    out var rack) ||
                rack.FloorLogicalId != location.FloorLogicalId ||
                !levelByPosition.TryGetValue(
                    (rack.LogicalId, location.LevelNo), out var level))
            {
                throw new SpaceDispatchOutcomeEvaluationException(
                    "A Published location has inconsistent rack or level geometry.");
            }

            var angle = (double)rack.RotationZ * Math.PI / 180d;
            var localX = (location.ColumnNo - 0.5m) * level.CellWidth;
            var localY = (location.DepthNo - 0.5m) * level.CellDepth;
            var x = rack.X + localX * (decimal)Math.Cos(angle) -
                localY * (decimal)Math.Sin(angle);
            var y = rack.Y + localX * (decimal)Math.Sin(angle) +
                localY * (decimal)Math.Cos(angle);
            result.Add(
                location.LogicalId,
                new SpaceDispatchEvaluationLocationAnchor(
                    location.LogicalId,
                    location.FloorLogicalId,
                    x,
                    y));
        }
        return result;
    }

    private static SpaceProblemException EvidenceInvalid(string detail) =>
        new(
            SpaceErrorCodes.DispatchEvaluationEvidenceInvalid,
            502,
            "The dispatch outcome evaluation evidence is invalid.",
            detail,
            "refresh-dispatch-evidence-or-contact-support");

    private sealed record LocationRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        int ColumnNo,
        int LevelNo,
        int DepthNo);

    private sealed record RackRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        decimal X,
        decimal Y,
        decimal RotationZ);

    private sealed record RackLevelRow(
        Guid RackLogicalId,
        int LevelNo,
        int CellWidth,
        int CellDepth);
}
