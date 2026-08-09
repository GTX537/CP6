using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpaceDispatchRecommendationContractTests
{
    [Fact]
    public void Endpoints_are_internal_audited_and_not_design_v1()
    {
        var controller = typeof(SpaceDispatchRecommendationController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/operations/v1/sites/{siteId:guid}/dispatch-recommendations",
            route.Template);
        Assert.Empty(
            controller.GetCustomAttributes<SpaceDesignV1ContractAttribute>());

        AssertEndpoint(
            nameof(SpaceDispatchRecommendationController.Generate),
            "operations:recommendations:generate",
            "space.operations.dispatch-recommendation.generate",
            auditRead: false);
        AssertEndpoint(
            nameof(SpaceDispatchRecommendationController.Get),
            "operations:recommendations:read",
            "space.operations.dispatch-recommendation.read",
            auditRead: true);
    }

    [Fact]
    public void Contract_exposes_concurrency_and_independent_personnel_evidence()
    {
        var request = Properties<GenerateSpaceDispatchRecommendationRequest>();
        Assert.Contains("AllowCrossFloor", request);
        Assert.Contains("MaximumTravelDistanceMeters", request);
        Assert.Contains("IncludeSimulatedPersonnel", request);
        Assert.Contains("MaximumAssignments", request);
        Assert.DoesNotContain("TenantId", request);
        Assert.DoesNotContain("GeneratedBy", request);
        Assert.DoesNotContain("Approve", request);
        Assert.DoesNotContain("Assign", request);

        var assignment =
            Properties<SpaceDispatchRecommendationAssignmentDto>();
        Assert.Contains("TaskContractVersion", assignment);
        Assert.Contains("TaskExecutionVersion", assignment);
        Assert.Contains("TaskRowVersion", assignment);
        Assert.Contains("PersonPositionOccurredAtUtc", assignment);
        Assert.Contains("PersonPositionReceivedAtUtc", assignment);
        Assert.Contains("PersonWorkStateOccurredAtUtc", assignment);
        Assert.Contains("PersonWorkStateReceivedAtUtc", assignment);
        Assert.Contains("RuleHits", assignment);
        Assert.DoesNotContain("Command", assignment);
        Assert.DoesNotContain("Approved", assignment);
        Assert.DoesNotContain("ExecutionStatus", assignment);
    }

    [Fact]
    public void Contract_has_explicit_caps_and_idempotent_generate_plus_get()
    {
        Assert.Equal(
            "space-dispatch-v1",
            SpaceDispatchRecommendationService.DefinitionVersion);
        Assert.Equal(100, SpaceDispatchRecommendationService.MaximumAssignmentCount);
        Assert.Equal(
            100_000,
            SpaceDispatchRecommendationEngine.MaximumEvaluatedPairCount);
        Assert.Equal(
            100,
            SpaceDispatchRecommendationEngine.MaximumExclusionSampleCount);

        var methods = typeof(ISpaceDispatchRecommendationService)
            .GetMethods()
            .OrderBy(value => value.Name)
            .ToArray();
        Assert.Equal(2, methods.Length);
        Assert.Equal("GenerateAsync", methods[0].Name);
        Assert.Equal("GetAsync", methods[1].Name);
        Assert.Equal(
            [
                typeof(Guid),
                typeof(Guid),
                typeof(GenerateSpaceDispatchRecommendationRequest),
                typeof(CancellationToken),
            ],
            methods[0].GetParameters().Select(value => value.ParameterType));
        Assert.Equal(
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)],
            methods[1].GetParameters().Select(value => value.ParameterType));
    }

    private static void AssertEndpoint(
        string methodName,
        string permissionAction,
        string auditAction,
        bool auditRead)
    {
        var method = typeof(SpaceDispatchRecommendationController)
            .GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method!),
            value => value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(permissionAction, permission.ConstructorArguments[1].Value);
        var optIn = Assert.Single(
            permission.NamedArguments,
            value => value.MemberName == "UseProblemDetails");
        Assert.True((bool)optIn.TypedValue.Value!);

        var audit = Assert.Single(
            method!.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal(auditAction, audit.Action);
        Assert.Equal("DispatchRecommendation", audit.ResourceType);
        Assert.Equal("recommendationId", audit.ResourceIdArgument);
        Assert.Equal("siteId", audit.SiteIdArgument);
        Assert.Equal($"space:{permissionAction}", audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
    }

    private static HashSet<string> Properties<T>() =>
        typeof(T).GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
}
