using CP6.Core.Services.Plm;

namespace CP6.Tests.Erp;

public sealed class Wp0508RoutingValuesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wp0508_alternative_group_rejects_dependency_in_either_direction(bool primaryDependsOnSubstitute)
    {
        var digest = new string('a', 64);
        var primary = new RoutingOperationV1(Guid.NewGuid(), "CUT", "模拟主工序", 10, [], "SIMULATED-cut",
            "APPLIES", "", "", [], [], [], "");
        var rule = new EngineeringSourceRefV1(ConfigurationRuleValuesV1.Owner, Guid.NewGuid(), "1", digest);
        var substitute = primary with
        {
            OccurrenceId = Guid.NewGuid(), Name = "模拟替代工序", SemanticOrder = 20,
            Alternative = new(primary.OccurrenceId, rule, new("Finish", "STRING", "Black"))
        };
        var predecessor = primaryDependsOnSubstitute ? substitute : primary;
        var dependent = (primaryDependsOnSubstitute ? primary : substitute) with
        {
            SemanticOrder = predecessor.SemanticOrder + 10, DependsOn = [predecessor.OccurrenceId]
        };
        var body = new EngineeringRoutingBodyV1("模拟替代路线",
            new(EngineeringRevisionValuesV1.RevisionOwner, Guid.NewGuid(), "1", digest),
            new(EngineeringProductValuesV1.Owner, Guid.NewGuid(), "1", digest), null, "", [predecessor, dependent]);

        var findings = EngineeringRoutingValuesV1.Validate(body);

        Assert.DoesNotContain(findings, finding => finding.StartsWith("DEPENDENCY_", StringComparison.Ordinal));
        Assert.Equal($"ALTERNATIVE_RELATION_CONFLICT:{dependent.OccurrenceId}", Assert.Single(findings,
            finding => finding.StartsWith("ALTERNATIVE_RELATION_CONFLICT:", StringComparison.Ordinal)));
    }
}
