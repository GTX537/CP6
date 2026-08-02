using System.Reflection;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Tests.Space;

public sealed class SpacePlanningComparisonContractTests
{
    [Fact]
    public void Comparison_request_is_explicit_and_has_no_weighted_ranking()
    {
        var properties = typeof(CreateSpacePlanningComparisonRequest)
            .GetProperties()
            .Select(value => value.Name)
            .ToArray();

        Assert.Equal(
            [
                "Name",
                "BaselineRunId",
                "RunIds",
                "MinimumDistanceCoveragePercent",
                "MaximumPeakCapacityUtilizationPercent",
                "MaximumCongestionTaskHours",
                "MaximumTotalCost",
            ],
            properties);
        Assert.DoesNotContain(properties, value =>
            value.Contains("Weight", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Rank", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Winner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Comparison_response_exposes_deltas_risks_and_guardrails()
    {
        var comparison = typeof(SpacePlanningComparisonDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToArray();
        var entry = typeof(SpacePlanningComparisonEntryDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToArray();

        Assert.Contains("BaselineRunId", comparison);
        Assert.Contains("ComparisonHash", comparison);
        Assert.Contains("AutomatedRanking", comparison);
        Assert.Contains("ProductionWriteAllowed", comparison);
        Assert.Contains("DeltaFromBaseline", entry);
        Assert.Contains("Risks", entry);
        Assert.DoesNotContain(entry, value =>
            value.Contains("Rank", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Winner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Decision_records_are_append_only_human_evidence()
    {
        Assert.Equal(
            ["Selected", "Deferred", "RejectedAll"],
            Enum.GetNames<SpacePlanningDecisionOutcome>());
        Assert.All(
            typeof(SpacePlanningDecisionRecord).GetProperties(),
            property => Assert.Null(property.SetMethod?.IsPublic == true
                ? property.SetMethod
                : null));
        var response = typeof(SpacePlanningDecisionDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToArray();
        Assert.Contains("SupersedesDecisionId", response);
        Assert.Contains("HumanDecision", response);
        Assert.Contains("AutomatedRecommendation", response);
        Assert.Contains("ProductionWriteAllowed", response);
        Assert.DoesNotContain(response, value =>
            value.Contains("Publish", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Apply", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(
            "SPACE_PLANNING_COMPARISON_",
            SpaceErrorCodes.PlanningComparisonEvidenceInvalid,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "SPACE_PLANNING_DECISION_",
            SpaceErrorCodes.PlanningDecisionInvalid,
            StringComparison.Ordinal);
    }
}
