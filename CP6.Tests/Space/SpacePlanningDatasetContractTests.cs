using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpacePlanningDatasetContractTests
{
    [Fact]
    public void Endpoints_use_dedicated_internal_planning_permissions()
    {
        var controller = typeof(SpacePlanningDatasetController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/planning/v1/sites/{siteId:guid}/scenario-branches/" +
            "{branchId:guid}/historical-datasets",
            route.Template);

        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningDatasetController
                    .CreateHistoricalDataset))!,
            typeof(HttpPutAttribute),
            "{datasetId:guid}",
            "planning:dataset:create");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningDatasetController
                    .GetHistoricalDataset))!,
            typeof(HttpGetAttribute),
            "{datasetId:guid}",
            "planning:dataset:read");
        AssertEndpoint(
            controller.GetMethod(
                nameof(SpacePlanningDatasetController
                    .GetHistoricalDatasets))!,
            typeof(HttpGetAttribute),
            null,
            "planning:dataset:read");
    }

    [Fact]
    public void Import_contract_has_no_raw_business_identifier_fields()
    {
        var datasetNames =
            typeof(CreateSpacePlanningHistoricalDatasetRequest)
                .GetProperties()
                .Select(value => value.Name)
                .ToArray();
        var taskNames =
            typeof(CreateSpacePlanningHistoricalTaskRequest)
                .GetProperties()
                .Select(value => value.Name)
                .ToArray();
        var names = datasetNames.Concat(taskNames).ToArray();

        Assert.Contains("TaskToken", names);
        Assert.Contains("WorkerToken", names);
        Assert.Contains("ConfirmDeidentified", names);
        Assert.DoesNotContain(
            names,
            value =>
                value.Contains("OrderId", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("WorkerId", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("MaterialId", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Sku", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dataset_response_exposes_clock_and_production_write_guard()
    {
        var names = typeof(SpacePlanningHistoricalDatasetDto)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Deidentified", names);
        Assert.Contains("ProductionWriteAllowed", names);
        Assert.Contains("ReplayClock", names);
        Assert.Contains("Limitations", names);
        Assert.Contains("DeidentificationVersion", names);
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
