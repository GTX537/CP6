using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpacePlanningSimulationContractTests
{
    [Fact]
    public void Endpoints_use_dedicated_internal_planning_permissions()
    {
        var controller = typeof(SpacePlanningSimulationController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/planning/v1/sites/{siteId:guid}/scenario-branches/" +
            "{branchId:guid}/simulation-runs",
            route.Template);

        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningSimulationController
                    .CreateSimulationRun))!,
            typeof(HttpPutAttribute),
            "{runId:guid}",
            "planning:simulation:create");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningSimulationController
                    .GetSimulationRun))!,
            typeof(HttpGetAttribute),
            "{runId:guid}",
            "planning:simulation:read");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningSimulationController
                    .GetSimulationRuns))!,
            typeof(HttpGetAttribute),
            null,
            "planning:simulation:read");
    }

    [Fact]
    public void Response_exposes_all_metrics_evidence_and_safety_boundaries()
    {
        var names = typeof(SpacePlanningSimulationRunDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ScenarioContentRevision", names);
        Assert.Contains("DatasetRequestHash", names);
        Assert.Contains("ResultHash", names);
        Assert.Contains("ProductionWriteAllowed", names);
        Assert.Contains("HighPrecisionPhysicalSimulation", names);
        Assert.Contains("Distance", names);
        Assert.Contains("Congestion", names);
        Assert.Contains("Capacity", names);
        Assert.Contains("Throughput", names);
        Assert.Contains("Cost", names);
        Assert.Contains("LocationResults", names);
        Assert.Contains("LocationResultsTruncated", names);
        Assert.Contains("Limitations", names);
    }

    [Fact]
    public void Request_uses_explicit_capacity_and_cost_units()
    {
        var names = typeof(CreateSpacePlanningSimulationRunRequest)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DatasetId", names);
        Assert.Contains("DefaultQuantityCapacity", names);
        Assert.Contains("DefaultConcurrentTaskCapacity", names);
        Assert.Contains("ThroughputWindowMinutes", names);
        Assert.Contains("DistanceCostPerMeter", names);
        Assert.Contains("LaborCostPerHour", names);
        Assert.Contains("CongestionCostPerTaskHour", names);
        Assert.Contains("CurrencyCode", names);
        Assert.Contains("LocationCapacities", names);
        Assert.DoesNotContain("Publish", names);
        Assert.DoesNotContain("Production", names);
    }

    private static void AssertEndpoint(
        MethodInfo method,
        Type httpAttribute,
        string? template,
        string action)
    {
        var http = Assert.Single(
            method.GetCustomAttributes(),
            value => value.GetType() == httpAttribute);
        var actualTemplate = http switch
        {
            HttpPutAttribute value => value.Template,
            HttpGetAttribute value => value.Template,
            _ => null,
        };
        Assert.Equal(template, actualTemplate);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value =>
                value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(action, permission.ConstructorArguments[1].Value);
    }
}
