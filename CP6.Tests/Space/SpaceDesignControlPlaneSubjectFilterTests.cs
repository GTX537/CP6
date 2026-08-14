using System.Reflection;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace CP6.Tests.Space;

public sealed class SpaceDesignControlPlaneSubjectFilterTests
{
    public static IEnumerable<object[]> ExternalControlPlaneMatrix()
    {
        var roles = new[]
        {
            ("Customer", Guid.Parse(
                "bbbbbbbb-0000-0000-0000-000000000001")),
            ("Supplier", Guid.Parse(
                "bbbbbbbb-0000-0000-0000-000000000002")),
            ("3PL", Guid.Parse(
                "bbbbbbbb-0000-0000-0000-000000000003")),
        };
        var surfaces = new[]
        {
            (typeof(SpaceDesignV1Controller),
                nameof(SpaceDesignV1Controller.GetScene), "Draft"),
            (typeof(SpaceDesignV1Controller),
                nameof(SpaceDesignV1Controller.GetSources), "Source"),
            (typeof(SpaceDesignV1Controller),
                nameof(SpaceDesignV1Controller.CreateSource), "Upload"),
            (typeof(SpaceEditLeaseController),
                nameof(SpaceEditLeaseController.GetEditLease), "Lease"),
            (typeof(SpaceValidationController),
                nameof(SpaceValidationController.CreateValidation),
                "Validate"),
            (typeof(SpacePublishPreviewController),
                nameof(SpacePublishPreviewController.GetPublishPreview),
                "PublishPreview"),
            (typeof(SpacePublishController),
                nameof(SpacePublishController.CreatePublishAttempt),
                "Publish"),
            (typeof(SpaceAiAtomicApplyController),
                nameof(SpaceAiAtomicApplyController.GetGenerationRun), "AI"),
        };

        foreach (var role in roles)
        foreach (var surface in surfaces)
        {
            yield return
            [
                role.Item1,
                role.Item2,
                surface.Item1,
                surface.Item2,
                surface.Item3,
            ];
        }
    }

    [Theory]
    [MemberData(nameof(ExternalControlPlaneMatrix))]
    public async Task External_subjects_are_denied_before_control_plane_action(
        string role,
        Guid organizationId,
        Type controllerType,
        string methodName,
        string surface)
    {
        var audit = new RecordingAuditWriter();
        var filter = new SpaceDesignControlPlaneSubjectFilter(
            new ExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                IsExternal: true,
                organizationId),
            audit);
        var context = ActionContext(controllerType, methodName);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            filter.OnAuthorizationAsync(context));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Equal("use-published-portal", error.RecoveryAction);
        Assert.True(
            error.Detail?.Contains("Published-only", StringComparison.Ordinal)
                == true,
            $"{role} did not receive the Published-only recovery boundary " +
            $"for {surface}.");
        var denied = Assert.Single(audit.Inputs);
        Assert.Equal("space.external.control-plane.denied", denied.Action);
        Assert.Equal(SpaceAuditOutcome.Denied, denied.Outcome);
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, denied.ReasonCode);
        Assert.Equal(organizationId, denied.Evidence?.OrganizationId);
    }

    [Fact]
    public async Task Internal_subject_continues_to_design_action()
    {
        var audit = new RecordingAuditWriter();
        var filter = new SpaceDesignControlPlaneSubjectFilter(
            new ExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                IsExternal: false,
                OrganizationContextId: null),
            audit);
        var context = ActionContext(
            typeof(SpacePublishController),
            nameof(SpacePublishController.CreatePublishAttempt));

        var error = await Record.ExceptionAsync(() =>
            filter.OnAuthorizationAsync(context));

        Assert.Null(error);
        Assert.Equal(-900, filter.Order);
        Assert.Empty(audit.Inputs);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Supplier")]
    [InlineData("3PL")]
    public async Task External_subject_continues_only_to_published_portal(
        string role)
    {
        var audit = new RecordingAuditWriter();
        var filter = new SpaceDesignControlPlaneSubjectFilter(
            new ExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                IsExternal: true,
                Guid.NewGuid()),
            audit);
        var context = ActionContext(
            typeof(SpaceExternalPortalController),
            nameof(SpaceExternalPortalController.GetPortalPublishedScene));

        var error = await Record.ExceptionAsync(() =>
            filter.OnAuthorizationAsync(context));

        Assert.True(error is null, $"{role} could not reach the portal action.");
        Assert.Empty(audit.Inputs);
    }

    [Fact]
    public void Published_portal_is_the_only_external_subject_exemption()
    {
        var contractControllers = typeof(SpaceDesignV1Controller).Assembly
            .GetTypes()
            .Where(type =>
                typeof(ControllerBase).IsAssignableFrom(type) &&
                type.IsDefined(
                    typeof(SpaceDesignV1ContractAttribute),
                    inherit: true))
            .ToArray();
        Assert.NotEmpty(contractControllers);

        var controllerExemptions = contractControllers
            .Where(type => type.IsDefined(
                typeof(AllowSpaceExternalSubjectAttribute),
                inherit: true))
            .ToArray();
        var actionExemptions = contractControllers
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(
                typeof(AllowSpaceExternalSubjectAttribute),
                inherit: true))
            .ToArray();

        Assert.Equal([typeof(SpaceExternalPortalController)],
            controllerExemptions);
        Assert.Empty(actionExemptions);
    }

    [Fact]
    public async Task Audit_sink_failure_does_not_bypass_external_denial()
    {
        var filter = new SpaceDesignControlPlaneSubjectFilter(
            new ExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                IsExternal: true,
                Guid.NewGuid()),
            new ThrowingAuditWriter());
        var context = ActionContext(
            typeof(SpacePublishController),
            nameof(SpacePublishController.CreatePublishAttempt));

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            filter.OnAuthorizationAsync(context));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
    }

    private static AuthorizationFilterContext ActionContext(
        Type controllerType,
        string methodName)
    {
        var method = controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        var action = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controllerType.GetTypeInfo(),
            MethodInfo = method!,
            ControllerName = controllerType.Name,
            ActionName = methodName,
        };
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/space/design/v1/security-matrix";
        var actionContext = new ActionContext(
            http,
            new RouteData(),
            action,
            new ModelStateDictionary());
        return new AuthorizationFilterContext(
            actionContext,
            []);
    }

    private sealed record ExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal,
        Guid? OrganizationContextId) :
        CP6.Space.Application.ISpaceExecutionContext;

    private sealed class RecordingAuditWriter : ISpaceAuditWriter
    {
        public List<SpaceAuditEventInput> Inputs { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingAuditWriter : ISpaceAuditWriter
    {
        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("audit unavailable");
    }
}
