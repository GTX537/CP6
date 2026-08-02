using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpacePlanningScenarioContractTests
{
    [Fact]
    public void Endpoints_use_internal_planning_permissions()
    {
        var controller = typeof(SpacePlanningScenarioController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/planning/v1/sites/{siteId:guid}/scenario-branches",
            route.Template);

        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningScenarioController.CreateBranch))!,
            typeof(HttpPutAttribute),
            "{branchId:guid}",
            "planning:scenario:create");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningScenarioController.GetBranch))!,
            typeof(HttpGetAttribute),
            "{branchId:guid}",
            "planning:scenario:read");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningScenarioController.GetBranches))!,
            typeof(HttpGetAttribute),
            null,
            "planning:scenario:read");
    }

    [Fact]
    public void Create_request_can_only_select_current_base_and_name()
    {
        var names =
            typeof(CreateSpacePlanningScenarioBranchRequest)
                .GetProperties()
                .Select(value => value.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            new HashSet<string>(
                ["BasePublishedVersionId", "Name"],
                StringComparer.OrdinalIgnoreCase),
            names);
        Assert.DoesNotContain("TenantId", names);
        Assert.DoesNotContain("ScenarioVersionId", names);
        Assert.DoesNotContain("ProductionIsolated", names);
    }

    [Fact]
    public void Branch_response_exposes_pinned_lineage_and_isolation_evidence()
    {
        var names = typeof(SpacePlanningScenarioBranchDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("BasePublishedVersionId", names);
        Assert.Contains("ScenarioVersionId", names);
        Assert.Contains("CloneJobId", names);
        Assert.Contains("ProductionIsolated", names);
        Assert.Contains("Limitations", names);
        Assert.Contains("DefinitionVersion", names);
    }

    private static void AssertEndpoint(
        MethodInfo method,
        Type verbAttributeType,
        string? template,
        string action)
    {
        var verb = Assert.Single(
            method.GetCustomAttributes(),
            value => value.GetType() == verbAttributeType);
        var actualTemplate = verb switch
        {
            HttpPutAttribute value => value.Template,
            HttpGetAttribute value => value.Template,
            _ => null,
        };
        Assert.Equal(template, actualTemplate);

        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value =>
                value.AttributeType ==
                typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(action, permission.ConstructorArguments[1].Value);
    }
}
