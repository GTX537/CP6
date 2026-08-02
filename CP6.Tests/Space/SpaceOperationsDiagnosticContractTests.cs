using System.Reflection;
using CP6.Core.Auth;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpaceOperationsDiagnosticContractTests
{
    [Fact]
    public void Endpoint_is_internal_audited_read_only_and_not_design_v1()
    {
        var controller = typeof(SpaceOperationsDiagnosticController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/operations/v1/sites/{siteId:guid}/diagnostics",
            route.Template);
        Assert.Empty(controller.GetCustomAttributes<SpaceDesignV1ContractAttribute>());

        var method = controller.GetMethod(
            nameof(SpaceOperationsDiagnosticController.Get));
        Assert.NotNull(method);
        Assert.Single(method!.GetCustomAttributes<HttpGetAttribute>());
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value => value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(
            "operations:diagnostics:read",
            permission.ConstructorArguments[1].Value);
        var optIn = Assert.Single(
            permission.NamedArguments,
            value => value.MemberName == "UseProblemDetails");
        Assert.True((bool)optIn.TypedValue.Value!);

        var audit = Assert.Single(
            method.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal("space.operations.diagnostics.read", audit.Action);
        Assert.Equal("OperationsDiagnostics", audit.ResourceType);
        Assert.Equal("siteId", audit.SiteIdArgument);
        Assert.Equal("space:operations:diagnostics:read", audit.PermissionCode);
        Assert.True(audit.AuditRead);
    }

    [Fact]
    public void Request_surface_does_not_accept_authority_identity_or_thresholds()
    {
        var method = typeof(SpaceOperationsDiagnosticController)
            .GetMethod(nameof(SpaceOperationsDiagnosticController.Get))!;
        var parameters = method.GetParameters()
            .Select(value => value.Name ?? string.Empty)
            .ToArray();

        Assert.Equal(
            ["siteId", "fromUtc", "toUtc", "cancellationToken"],
            parameters);
        Assert.DoesNotContain("tenantId", parameters);
        Assert.DoesNotContain("publishedVersionId", parameters);
        Assert.DoesNotContain("personExternalId", parameters);
        Assert.DoesNotContain("thresholds", parameters);
    }

    [Fact]
    public void Response_exposes_versioned_evidence_and_honest_capacity_fields()
    {
        var response = Properties<SpaceOperationsDiagnosticResponse>();
        Assert.All(
            new[]
            {
                "DefinitionVersion",
                "Thresholds",
                "PersonnelSource",
                "Path",
                "Congestion",
                "Dwell",
                "Capacity",
                "Limitations",
            },
            value => Assert.Contains(value, response));

        var capacity = Properties<SpaceOperationsCapacityDiagnosisDto>();
        Assert.Contains("LocationOccupancyPercent", capacity);
        Assert.Contains("LocationOccupancyPressure", capacity);
        Assert.Contains("CapacityUtilizationPercent", capacity);
        Assert.Contains("CapacityUtilizationStatus", capacity);
        Assert.Contains("CapacityUtilizationReason", capacity);

        var finding = Properties<SpaceOperationsBacktrackFindingDto>();
        Assert.DoesNotContain("PersonKey", finding);
        Assert.DoesNotContain("PersonExternalId", finding);
        Assert.DoesNotContain("UserId", finding);
        Assert.Equal(
            "space-operations-diagnostics-v1",
            SpaceOperationsDiagnosticService.DefinitionVersion);
        Assert.Equal(100_000, SpaceOperationsDiagnosticService.MaximumEvidenceEventCount);
    }

    [Fact]
    public void Service_interface_has_one_bounded_read_method()
    {
        var method = Assert.Single(typeof(ISpaceOperationsDiagnosticService).GetMethods());

        Assert.Equal("GetAsync", method.Name);
        Assert.Equal(
            [typeof(Guid), typeof(DateTimeOffset), typeof(DateTimeOffset), typeof(CancellationToken)],
            method.GetParameters().Select(value => value.ParameterType));
        Assert.Equal(
            typeof(Task<SpaceOperationsDiagnosticResponse>),
            method.ReturnType);
    }

    private static HashSet<string> Properties<T>() =>
        typeof(T).GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
}
