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

public sealed class SpacePutawayRecommendationContractTests
{
    [Fact]
    public void Endpoints_are_internal_audited_and_not_design_v1()
    {
        var controller = typeof(SpacePutawayRecommendationController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/operations/v1/sites/{siteId:guid}/putaway-recommendations",
            route.Template);
        Assert.Empty(
            controller.GetCustomAttributes<SpaceDesignV1ContractAttribute>());

        AssertEndpoint(
            nameof(SpacePutawayRecommendationController.Generate),
            "operations:recommendations:generate",
            "space.operations.putaway-recommendation.generate",
            auditRead: false);
        AssertEndpoint(
            nameof(SpacePutawayRecommendationController.Get),
            "operations:recommendations:read",
            "space.operations.putaway-recommendation.read",
            auditRead: true);
    }

    [Fact]
    public void Request_and_response_expose_bounded_explainable_evidence()
    {
        var request = Properties<GenerateSpacePutawayRecommendationRequest>();
        Assert.Contains("MaterialNumber", request);
        Assert.Contains("InboundQuantity", request);
        Assert.Contains("MaximumCandidates", request);
        Assert.DoesNotContain("TenantId", request);
        Assert.DoesNotContain("PublishedVersionId", request);
        Assert.DoesNotContain("Approve", request);
        Assert.DoesNotContain("Execute", request);

        var response = Properties<SpacePutawayRecommendationDto>();
        Assert.Contains("Sources", response);
        Assert.Contains("Exclusions", response);
        Assert.Contains("ExclusionSamples", response);
        Assert.Contains("ExclusionSamplesTruncated", response);
        Assert.Contains("Candidates", response);
        Assert.Contains("Limitations", response);
        Assert.Equal(
            "space-putaway-v1",
            SpacePutawayRecommendationService.DefinitionVersion);
        Assert.Equal(50, SpacePutawayRecommendationService.MaximumCandidateCount);
        Assert.Equal(
            100,
            SpacePutawayRecommendationEngine.MaximumExclusionSampleCount);
    }

    [Fact]
    public void Service_surface_is_idempotent_generate_plus_get()
    {
        var methods = typeof(ISpacePutawayRecommendationService)
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
                typeof(GenerateSpacePutawayRecommendationRequest),
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
        var method = typeof(SpacePutawayRecommendationController)
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
        Assert.Equal("PutawayRecommendation", audit.ResourceType);
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
