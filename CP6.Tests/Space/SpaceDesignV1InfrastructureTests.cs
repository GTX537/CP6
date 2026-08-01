using System.Text.Json;
using CP6.Core.Services.Space.Compatibility;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.WebApi.Middleware;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Space;

public sealed class SpaceDesignV1InfrastructureTests
{
    [Fact]
    public async Task Design_problem_middleware_emits_the_stable_problem_shape()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/space/design/v1/sites/not-a-guid/model";
        context.Response.Body = new MemoryStream();
        context.Response.Headers["X-Correlation-ID"] = "correlation";
        context.Response.Headers["X-Trace-ID"] = "trace";
        var middleware = new SpaceDesignProblemDetailsMiddleware(
            _ => throw new SpaceProblemException(
                SpaceErrorCodes.RequestInvalid,
                400,
                "Invalid",
                "siteId is invalid",
                "correct-request"),
            NullLogger<SpaceDesignProblemDetailsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            SpaceErrorCodes.RequestInvalid,
            body.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "correlation",
            body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(
            "trace",
            body.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(
            "correct-request",
            body.RootElement.GetProperty("recovery")
                .GetProperty("action")
                .GetString());
    }

    [Fact]
    public async Task Problem_middleware_does_not_change_legacy_space_behavior()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/space/site";
        var middleware = new SpaceDesignProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("legacy"),
            NullLogger<SpaceDesignProblemDetailsMiddleware>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));

        Assert.Equal("legacy", error.Message);
    }

    [Fact]
    public async Task External_organization_path_uses_the_same_safe_problem_shape()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/space/external-organization/test";
        context.Response.Body = new MemoryStream();
        var middleware = new SpaceDesignProblemDetailsMiddleware(
            _ => throw new SpaceExternalAccessStateException("closed"),
            NullLogger<SpaceDesignProblemDetailsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            SpaceErrorCodes.ExternalAccessStateInvalid,
            body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/space/field-policy/test")]
    [InlineData("/api/space/portal/v1/sites")]
    public async Task External_policy_and_portal_paths_use_safe_problem_details(
        string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var middleware = new SpaceDesignProblemDetailsMiddleware(
            _ => throw new SpaceProblemException(
                SpaceErrorCodes.ExternalScopeDenied,
                404,
                "Not found"),
            NullLogger<SpaceDesignProblemDetailsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            SpaceErrorCodes.ExternalScopeDenied,
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Compatibility_gate_requires_global_and_verified_site_cutover()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var evaluator = new CompatibilitySpaceDesignAccessEvaluator(
            new ExecutionContext(tenantId, Guid.NewGuid()),
            Options.Create(new SpaceCompatibilityOptions
            {
                DesignApiEnabled = true,
                Sites =
                [
                    new SpaceSiteCompatibilityOptions
                    {
                        TenantId = tenantId,
                        SiteId = siteId,
                        Mode = SpaceSiteMode.DesignV1,
                        CutoverState = SpaceCutoverState.DesignV1,
                        Evidence = new SpaceCutoverEvidence
                        {
                            BootstrapVerified = true,
                            RuntimeHashVerified = true,
                            WmsIdentityVerified = true,
                        },
                    },
                ],
            }));

        evaluator.EnsureSiteAccess(siteId, write: false);

        var denied = Assert.Throws<SpaceProblemException>(
            () => evaluator.EnsureSiteAccess(Guid.NewGuid(), write: true));
        Assert.Equal(SpaceErrorCodes.DesignApiDisabled, denied.Code);
        Assert.Equal(404, denied.StatusCode);
    }

    [Fact]
    public void Cursor_is_bound_to_tenant_actor_and_filter()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var accessor = new SpaceExecutionContextAccessor();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };
        http.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(
                    "space_grant_version",
                    "grant-1"),
            ],
            "test"));
        var codec = new DataProtectionSpaceCursorCodec(
            new EphemeralDataProtectionProvider(),
            accessor,
            http,
            new FixedClock());

        string cursor;
        using (accessor.Push(SpaceExecutionContext.ForUser(
                   tenantId,
                   actorId.ToString(),
                   "actor",
                   Guid.NewGuid(),
                   "0123456789abcdef0123456789abcdef")))
        {
            cursor = codec.Encode(
                new SpaceCursorState("versions", "filter-a", 50));
            Assert.Equal(
                50,
                codec.Decode(cursor, "versions", "filter-a").Offset);
            var mismatch = Assert.Throws<SpaceProblemException>(
                () => codec.Decode(cursor, "versions", "filter-b"));
            Assert.Equal(
                SpaceErrorCodes.CursorScopeMismatch,
                mismatch.Code);
        }

        using (accessor.Push(SpaceExecutionContext.ForUser(
                   tenantId,
                   Guid.NewGuid().ToString(),
                   "other",
                   Guid.NewGuid(),
                   "fedcba9876543210fedcba9876543210")))
        {
            var mismatch = Assert.Throws<SpaceProblemException>(
                () => codec.Decode(cursor, "versions", "filter-a"));
            Assert.Equal(
                SpaceErrorCodes.CursorScopeMismatch,
                mismatch.Code);
        }
    }

    private sealed record ExecutionContext(
        Guid TenantId,
        Guid ActorId) : CP6.Space.Application.ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow =>
            new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    }
}
