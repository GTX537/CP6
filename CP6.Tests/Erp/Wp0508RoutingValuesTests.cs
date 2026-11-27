using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;

namespace CP6.Tests.Erp;

public sealed class Wp0508RoutingValuesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wp0508_alternative_group_rejects_dependency_in_either_direction(bool primaryDependsOnSubstitute)
    {
        var (primary, substitute) = Alternatives();
        var predecessor = primaryDependsOnSubstitute ? substitute : primary;
        var dependent = (primaryDependsOnSubstitute ? primary : substitute) with
        {
            SemanticOrder = predecessor.SemanticOrder + 10, DependsOn = [predecessor.OccurrenceId]
        };
        AssertConflict(Validate(predecessor, dependent), dependent);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Wp0508_alternative_group_rejects_transitive_dependencies(bool primaryDependsOnSubstitute, int outsideSteps)
    {
        var (primary, substitute) = Alternatives();
        var first = primaryDependsOnSubstitute ? substitute : primary;
        var operations = new List<RoutingOperationV1> { first };
        for (var i = 0; i < outsideSteps; i++)
            operations.Add(OutsideStep(operations[^1]));
        var dependent = (primaryDependsOnSubstitute ? primary : substitute) with
        {
            SemanticOrder = operations[^1].SemanticOrder + 10, DependsOn = [operations[^1].OccurrenceId]
        };

        AssertConflict(Validate([dependent, .. operations]), dependent);
    }

    [Fact]
    public void Wp0508_two_substitutes_in_one_group_cannot_require_each_other_transitively()
    {
        var (primary, substitute) = Alternatives();
        var middle = OutsideStep(substitute);
        var other = substitute with
        {
            OccurrenceId = Guid.NewGuid(), SemanticOrder = 40, DependsOn = [middle.OccurrenceId],
            Alternative = substitute.Alternative! with { When = new("Finish", "STRING", "White") }
        };

        AssertConflict(Validate(primary, substitute, middle, other), other);
    }

    [Fact]
    public void Wp0508_alternative_members_can_share_an_outside_prerequisite()
    {
        var (primary, substitute) = Alternatives();
        var shared = primary with { OccurrenceId = Guid.NewGuid(), AlternativeGroup = null, SemanticOrder = 0 };
        var result = Validate(primary with { DependsOn = [shared.OccurrenceId] },
            substitute with { DependsOn = [shared.OccurrenceId] }, shared);

        Assert.Equal("PASS", result.State);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("NOT_APPLICABLE")]
    [InlineData("UNKNOWN")]
    public void Wp0508_unconfirmed_intermediate_keeps_its_applicability_finding(string applicability)
    {
        var (primary, substitute) = Alternatives();
        var middle = OutsideStep(primary) with { Applicability = applicability, Reason = "模拟适用性依据" };
        var dependent = substitute with { SemanticOrder = 30, DependsOn = [middle.OccurrenceId] };

        var result = Validate(primary, middle, dependent);
        Assert.Equal("UNKNOWN", result.State);
        Assert.Equal($"APPLICABILITY_OWNER_REQUIRED:{middle.OccurrenceId}", Assert.Single(result.Findings));
        var direct = dependent with { Applicability = applicability, Reason = middle.Reason, DependsOn = [primary.OccurrenceId] };
        AssertConflict(Validate(primary, direct), direct);
    }

    private static (RoutingOperationV1 Primary, RoutingOperationV1 Substitute) Alternatives()
    {
        var digest = new string('a', 64);
        var machine = new ResourceSourceRefV1(ResourceSourceStoreV1.Owner, "MACHINE", Guid.NewGuid(), "1", digest);
        var primary = new RoutingOperationV1(Guid.NewGuid(), "CUT", "模拟主工序", 10, [], "SIMULATED-cut",
            "APPLIES", "", "", [machine], [], [], "");
        var rule = new EngineeringSourceRefV1(ConfigurationRuleValuesV1.Owner, Guid.NewGuid(), "1", digest);
        return (primary, primary with
        {
            OccurrenceId = Guid.NewGuid(), Name = "模拟替代工序", SemanticOrder = 20,
            Alternative = new(primary.OccurrenceId, rule, new("Finish", "STRING", "Black"))
        });
    }

    private static RoutingOperationV1 OutsideStep(RoutingOperationV1 predecessor) => predecessor with
    {
        OccurrenceId = Guid.NewGuid(), Name = "模拟组外工序", SemanticOrder = predecessor.SemanticOrder + 10,
        AlternativeGroup = null, Alternative = null, DependsOn = [predecessor.OccurrenceId]
    };

    private static RoutingInputValidationV1 Validate(params RoutingOperationV1[] operations)
    {
        var digest = new string('a', 64);
        var body = new EngineeringRoutingBodyV1("模拟替代路线",
            new(EngineeringRevisionValuesV1.RevisionOwner, Guid.NewGuid(), "1", digest),
            new(EngineeringProductValuesV1.Owner, Guid.NewGuid(), "1", digest),
            new(EngineeringProductValuesV1.SiteOwner, Guid.NewGuid(), "1", digest), "", operations);
        return EngineeringRoutingOwnerV1.InputValidation([.. EngineeringRoutingValuesV1.Validate(body)], []);
    }

    private static void AssertConflict(RoutingInputValidationV1 result, RoutingOperationV1 dependent)
    {
        Assert.Equal("BLOCK", result.State);
        Assert.DoesNotContain(result.Findings, finding => finding.StartsWith("DEPENDENCY_", StringComparison.Ordinal));
        Assert.Equal($"ALTERNATIVE_RELATION_CONFLICT:{dependent.OccurrenceId}", Assert.Single(result.Findings,
            finding => finding.StartsWith("ALTERNATIVE_RELATION_CONFLICT:", StringComparison.Ordinal)));
    }
}
