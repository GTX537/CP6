using System.Reflection;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CP6.Tests.Space;

public sealed class SpaceAiSecurityContractTests
{
    [Fact]
    public void Every_ai_endpoint_has_explicit_audit_metadata()
    {
        var controllerTypes = new[]
        {
            typeof(SpaceAiAdministrationController),
            typeof(SpaceAiAtomicApplyController),
            typeof(SpaceAiGenerationRecoveryController),
            typeof(SpaceAiProposalDecisionController),
        };
        var endpoints = controllerTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Select(method => new
            {
                Method = method,
                Http = method.GetCustomAttributes<HttpMethodAttribute>()
                    .SingleOrDefault(),
                Audit = method.GetCustomAttribute<SpaceAuditOperationAttribute>(),
            })
            .Where(endpoint => endpoint.Http is not null)
            .ToArray();

        Assert.Equal(16, endpoints.Length);
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotNull(endpoint.Audit);
            Assert.StartsWith("space.ai-", endpoint.Audit!.Action);
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Audit.ResourceType));
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Audit.PermissionCode));
        });
        Assert.Equal(
            endpoints.Length,
            endpoints.Select(endpoint => endpoint.Audit!.Action)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void Every_ai_read_endpoint_is_marked_for_denial_audit()
    {
        var controllerTypes = new[]
        {
            typeof(SpaceAiAdministrationController),
            typeof(SpaceAiAtomicApplyController),
            typeof(SpaceAiProposalDecisionController),
        };
        var reads = controllerTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Where(method => method
                .GetCustomAttributes<HttpMethodAttribute>()
                .SingleOrDefault()?.HttpMethods.Contains(
                    "GET",
                    StringComparer.Ordinal) == true)
            .ToArray();

        Assert.Equal(7, reads.Length);
        Assert.All(reads, method => Assert.True(
            method.GetCustomAttribute<SpaceAuditOperationAttribute>()?.AuditRead == true,
            $"{method.DeclaringType?.Name}.{method.Name} must audit denied reads."));
    }
}
