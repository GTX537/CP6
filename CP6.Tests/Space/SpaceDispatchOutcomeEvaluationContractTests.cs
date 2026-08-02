using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Tests.Space;

public sealed class SpaceDispatchOutcomeEvaluationContractTests
{
    [Fact]
    public void Public_contract_exposes_aggregate_evidence_without_identity_or_cost_claims()
    {
        var evaluation = Properties<SpaceDispatchOutcomeEvaluationDto>();
        Assert.Contains("Evidence", evaluation);
        Assert.Contains("Funnel", evaluation);
        Assert.Contains("Timing", evaluation);
        Assert.Contains("PlannedDistance", evaluation);
        Assert.Contains("BenefitBoundary", evaluation);
        Assert.DoesNotContain("AssignedTo", evaluation);
        Assert.DoesNotContain("UserId", evaluation);
        Assert.DoesNotContain("People", evaluation);
        Assert.DoesNotContain("Tasks", evaluation);

        Assert.Equal(
            [
                "ActualTravelDistanceAvailable",
                "ActualTravelDistanceReason",
                "MonetaryBenefitAvailable",
                "MonetaryBenefitReason",
                "ThroughputUpliftAvailable",
                "ThroughputUpliftReason",
            ],
            Properties<SpaceDispatchBenefitBoundaryDto>().Order());
    }

    [Fact]
    public void Service_surface_is_read_only_and_frozen()
    {
        var method = Assert.Single(typeof(ISpaceDispatchOutcomeEvaluationService)
            .GetMethods());
        Assert.Equal("GetAsync", method.Name);
        Assert.Equal(typeof(Task<SpaceDispatchOutcomeEvaluationDto>),
            method.ReturnType);
        Assert.Equal(
            [typeof(Guid), typeof(Guid), typeof(Guid), typeof(CancellationToken)],
            method.GetParameters().Select(value => value.ParameterType));
        Assert.Equal(
            "SPACE_DISPATCH_EVALUATION_EVIDENCE_INVALID",
            SpaceErrorCodes.DispatchEvaluationEvidenceInvalid);
    }

    private static HashSet<string> Properties<T>() =>
        typeof(T).GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
}
