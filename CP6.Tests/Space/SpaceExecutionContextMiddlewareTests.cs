using System.Diagnostics;
using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.WebApi.Localization;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CP6.Tests.Space;

public class SpaceExecutionContextMiddlewareTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DefaultHttpContext Context(
        string path = "/api/space/site",
        bool authenticated = true,
        string? tenant = "11111111-1111-1111-1111-111111111111",
        string? actor = "22222222-2222-2222-2222-222222222222",
        params Claim[] extra)
    {
        var claims = new List<Claim>();
        if (tenant is not null)
            claims.Add(new Claim("tenant_id", tenant));
        if (actor is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, actor));
        claims.Add(new Claim(ClaimTypes.Name, "alice"));
        claims.AddRange(extra);

        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuth")
            : new ClaimsIdentity(claims);

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        return context;
    }

    private static SpaceExecutionContextMiddleware MakeMiddleware(RequestDelegate next)
        => new(next, NullLogger<SpaceExecutionContextMiddleware>.Instance);

    private static Task Invoke(
        SpaceExecutionContextMiddleware middleware,
        DefaultHttpContext context,
        SpaceExecutionContextAccessor? accessor = null,
        Guid? tenantContextId = null)
    {
        var tenant = new TenantContext();
        var claim = context.User.FindFirstValue("tenant_id");
        if (tenantContextId.HasValue)
        {
            tenant.CurrentTenantId = tenantContextId.Value;
        }
        else if (Guid.TryParse(claim, out var parsed) && parsed != Guid.Empty)
        {
            tenant.CurrentTenantId = parsed;
        }

        accessor ??= new SpaceExecutionContextAccessor();
        return middleware.InvokeAsync(context, tenant, accessor);
    }

    [Fact]
    public async Task Non_space_request_passes_through_without_headers_or_context_changes()
    {
        var context = Context(path: "/api/orders");
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForSystem(
            TenantId,
            "space-worker:test",
            Guid.NewGuid(),
            "outer-trace");
        var called = false;

        using (accessor.Push(outer))
        {
            await Invoke(
                MakeMiddleware(_ =>
                {
                    called = true;
                    Assert.Same(outer, accessor.Current);
                    return Task.CompletedTask;
                }),
                context,
                accessor);

            Assert.Same(outer, accessor.Current);
        }

        Assert.True(called);
        Assert.False(context.Response.Headers.ContainsKey("X-Correlation-ID"));
        Assert.False(context.Response.Headers.ContainsKey("X-Trace-ID"));
    }

    public static TheoryData<bool, string?, string?, string, int> InvalidBoundaries => new()
    {
        {
            false,
            TenantId.ToString(),
            ActorId.ToString(),
            "SPACE_AUTHENTICATION_REQUIRED",
            StatusCodes.Status401Unauthorized
        },
        {
            true,
            null,
            ActorId.ToString(),
            "SPACE_TENANT_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            string.Empty,
            ActorId.ToString(),
            "SPACE_TENANT_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            "bad",
            ActorId.ToString(),
            "SPACE_TENANT_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            Guid.Empty.ToString(),
            ActorId.ToString(),
            "SPACE_TENANT_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            TenantId.ToString(),
            null,
            "SPACE_ACTOR_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            TenantId.ToString(),
            string.Empty,
            "SPACE_ACTOR_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            TenantId.ToString(),
            "bad",
            "SPACE_ACTOR_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
        {
            true,
            TenantId.ToString(),
            Guid.Empty.ToString(),
            "SPACE_ACTOR_CONTEXT_REQUIRED",
            StatusCodes.Status403Forbidden
        },
    };

    [Theory]
    [MemberData(nameof(InvalidBoundaries))]
    public async Task Invalid_boundary_fails_closed_with_correlation_header(
        bool authenticated,
        string? tenant,
        string? actor,
        string code,
        int status)
    {
        var context = Context(
            authenticated: authenticated,
            tenant: tenant,
            actor: actor);
        var called = false;

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(
                MakeMiddleware(_ =>
                {
                    called = true;
                    return Task.CompletedTask;
                }),
                context));

        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.HttpStatus);
        Assert.False(called);
        AssertValidGeneratedCorrelation(context);
        AssertW3CTraceHeader(context);
    }

    [Fact]
    public async Task Tenant_claim_must_match_the_resolved_tenant_context()
    {
        var context = Context();
        var called = false;

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(
                MakeMiddleware(_ =>
                {
                    called = true;
                    return Task.CompletedTask;
                }),
                context,
                tenantContextId: Guid.NewGuid()));

        Assert.Equal("SPACE_TENANT_CONTEXT_REQUIRED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        Assert.False(called);
        AssertValidGeneratedCorrelation(context);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("33333333-3333-3333-3333-333333333333")]
    public async Task Duplicate_or_conflicting_authenticated_tenant_claims_are_denied(
        string duplicateTenant)
    {
        var context = Context();
        context.User.AddIdentity(Identity(
            authenticated: true,
            new Claim("tenant_id", duplicateTenant)));

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));

        Assert.Equal("SPACE_TENANT_CONTEXT_REQUIRED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(context);
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("44444444-4444-4444-4444-444444444444")]
    public async Task Duplicate_or_conflicting_authenticated_actor_claims_are_denied(
        string duplicateActor)
    {
        var context = Context();
        context.User.AddIdentity(Identity(
            authenticated: true,
            new Claim(ClaimTypes.NameIdentifier, duplicateActor)));

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));

        Assert.Equal("SPACE_ACTOR_CONTEXT_REQUIRED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(context);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("EXTERNAL")]
    public async Task External_subject_is_denied_even_with_valid_identity(string subjectType)
    {
        var context = Context(extra: new[]
        {
            new Claim("subject_type", subjectType),
        });

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));

        Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(context);
    }

    [Theory]
    [InlineData("internal", "external")]
    [InlineData("external", "internal")]
    [InlineData("internal", "internal")]
    public async Task Multiple_authenticated_subject_claims_are_denied_regardless_of_order(
        string first,
        string second)
    {
        var context = Context(extra: new[]
        {
            new Claim("subject_type", first),
        });
        context.User.AddIdentity(Identity(
            authenticated: true,
            new Claim("subject_type", second)));

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));

        Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(context);
    }

    [Fact]
    public async Task Multiple_authenticated_organization_claims_are_denied()
    {
        var context = Context(extra: new[]
        {
            new Claim("subject_type", "internal"),
            new Claim("organization_context_id", "org-1"),
        });
        context.User.AddIdentity(Identity(
            authenticated: true,
            new Claim("organization_context_id", "org-1")));

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), context));

        Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(context);
    }

    [Fact]
    public async Task Claims_from_unauthenticated_identity_are_ignored()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/space/site";
        context.Request.Method = HttpMethods.Get;
        context.User = new ClaimsPrincipal(new[]
        {
            Identity(
                authenticated: false,
                new Claim("tenant_id", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, "mallory"),
                new Claim("subject_type", "external"),
                new Claim("organization_context_id", "untrusted-org")),
            Identity(
                authenticated: true,
                new Claim("tenant_id", TenantId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()),
                new Claim(ClaimTypes.Name, "alice")),
        });
        var accessor = new SpaceExecutionContextAccessor();
        ISpaceExecutionContext? seen = null;

        await Invoke(
            MakeMiddleware(_ =>
            {
                seen = accessor.Current;
                return Task.CompletedTask;
            }),
            context,
            accessor,
            tenantContextId: TenantId);

        Assert.NotNull(seen);
        Assert.Equal(TenantId, seen.TenantId);
        Assert.Equal(ActorId.ToString(), seen.ActorId);
        Assert.Equal("alice", seen.ActorName);
        Assert.Null(seen.OrganizationContextId);
    }

    [Fact]
    public async Task Unauthenticated_identity_cannot_inject_actor_name()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/space/site";
        context.Request.Method = HttpMethods.Get;
        context.User = new ClaimsPrincipal(new[]
        {
            Identity(
                authenticated: true,
                new Claim("tenant_id", TenantId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())),
            Identity(
                authenticated: false,
                new Claim(ClaimTypes.Name, "mallory")),
        });
        var accessor = new SpaceExecutionContextAccessor();
        ISpaceExecutionContext? seen = null;

        await Invoke(
            MakeMiddleware(_ =>
            {
                seen = accessor.Current;
                return Task.CompletedTask;
            }),
            context,
            accessor);

        Assert.NotNull(seen);
        Assert.Null(seen.ActorName);
    }

    [Fact]
    public async Task Organization_context_requires_explicit_internal_subject()
    {
        var denied = Context(extra: new[]
        {
            new Claim("organization_context_id", "org-1"),
        });

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(MakeMiddleware(_ => Task.CompletedTask), denied));

        Assert.Equal("SPACE_EXTERNAL_SUBJECT_DENIED", error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, error.HttpStatus);
        AssertValidGeneratedCorrelation(denied);

        var allowed = Context(extra: new[]
        {
            new Claim("organization_context_id", "org-1"),
            new Claim("subject_type", "INTERNAL"),
        });

        await Invoke(MakeMiddleware(_ => Task.CompletedTask), allowed);
        AssertValidGeneratedCorrelation(allowed);
    }

    public static TheoryData<StringValues> InvalidCorrelations => new()
    {
        new StringValues(string.Empty),
        new StringValues("not-a-guid"),
        new StringValues(Guid.Empty.ToString()),
        new StringValues(new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }),
    };

    [Theory]
    [MemberData(nameof(InvalidCorrelations))]
    public async Task Invalid_inbound_correlation_returns_safe_generated_id(StringValues value)
    {
        var context = Context();
        context.Request.Headers["X-Correlation-ID"] = value;
        var called = false;

        var error = await Assert.ThrowsAsync<BizException>(
            () => Invoke(
                MakeMiddleware(_ =>
                {
                    called = true;
                    return Task.CompletedTask;
                }),
                context));

        Assert.Equal("SPACE_CORRELATION_ID_INVALID", error.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, error.HttpStatus);
        Assert.False(called);
        AssertValidGeneratedCorrelation(context);
    }

    [Fact]
    public async Task Missing_correlation_is_generated_and_valid_correlation_is_propagated()
    {
        var generatedContext = Context();
        var generatedAccessor = new SpaceExecutionContextAccessor();
        ISpaceExecutionContext? generatedSnapshot = null;
        await Invoke(
            MakeMiddleware(_ =>
            {
                generatedSnapshot = generatedAccessor.Current;
                return Task.CompletedTask;
            }),
            generatedContext,
            generatedAccessor);

        var generated = AssertValidGeneratedCorrelation(generatedContext);
        Assert.Equal(generated, generatedSnapshot!.CorrelationId);

        var expected = Guid.NewGuid();
        var propagatedContext = Context();
        propagatedContext.Request.Headers["X-Correlation-ID"] = expected.ToString();
        var accessor = new SpaceExecutionContextAccessor();
        ISpaceExecutionContext? propagatedSnapshot = null;

        await Invoke(
            MakeMiddleware(_ =>
            {
                propagatedSnapshot = accessor.Current;
                return Task.CompletedTask;
            }),
            propagatedContext,
            accessor);

        Assert.Equal(expected.ToString(), propagatedContext.Response.Headers["X-Correlation-ID"]);
        Assert.Equal(expected, propagatedSnapshot!.CorrelationId);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Context_maps_user_organization_tenant_and_w3c_trace_for_next()
    {
        var correlation = Guid.NewGuid();
        var context = Context(extra: new[]
        {
            new Claim("subject_type", "internal"),
            new Claim("organization_context_id", "org-safe"),
        });
        context.Request.Headers["X-Correlation-ID"] = correlation.ToString();
        var accessor = new SpaceExecutionContextAccessor();
        ISpaceExecutionContext? seen = null;
        string? currentTrace = null;
        ActivityIdFormat? idFormat = null;

        await Invoke(
            MakeMiddleware(_ =>
            {
                seen = accessor.Current;
                currentTrace = Activity.Current?.TraceId.ToHexString();
                idFormat = Activity.Current?.IdFormat;
                return Task.CompletedTask;
            }),
            context,
            accessor);

        Assert.NotNull(seen);
        Assert.Equal(TenantId, seen.TenantId);
        Assert.Equal(SpaceExecutionContext.UserActor, seen.ActorType);
        Assert.Equal(ActorId.ToString(), seen.ActorId);
        Assert.Equal("alice", seen.ActorName);
        Assert.Equal("org-safe", seen.OrganizationContextId);
        Assert.Equal(correlation, seen.CorrelationId);
        Assert.Equal(ActivityIdFormat.W3C, idFormat);
        Assert.Equal(currentTrace, seen.TraceId);
        Assert.Equal(currentTrace, context.Response.Headers["X-Trace-ID"]);
        AssertW3CTraceHeader(context);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Context_restores_previous_value_after_normal_completion()
    {
        var context = Context();
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForSystem(
            TenantId,
            "space-worker:test",
            Guid.NewGuid(),
            "outer-trace");

        using (accessor.Push(outer))
        {
            await Invoke(
                MakeMiddleware(_ =>
                {
                    Assert.NotSame(outer, accessor.Current);
                    Assert.Equal(SpaceExecutionContext.UserActor, accessor.Current!.ActorType);
                    return Task.CompletedTask;
                }),
                context,
                accessor);

            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Context_and_owned_activity_are_restored_when_next_throws()
    {
        Assert.Null(Activity.Current);
        var context = Context();
        var accessor = new SpaceExecutionContextAccessor();
        Activity? captured = null;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke(
                MakeMiddleware(_ =>
                {
                    captured = Activity.Current;
                    Assert.NotNull(accessor.Current);
                    throw new InvalidOperationException("expected");
                }),
                context,
                accessor));

        Assert.Equal("expected", error.Message);
        Assert.NotNull(captured);
        Assert.Null(Activity.Current);
        Assert.Null(accessor.Current);
        Assert.NotEqual(TimeSpan.Zero, captured!.Duration);
        AssertValidGeneratedCorrelation(context);
        AssertW3CTraceHeader(context);
    }

    [Fact]
    public async Task Existing_activity_is_reused_and_not_stopped()
    {
        Assert.Null(Activity.Current);
        using var parent = new Activity("existing")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var context = Context();
        var accessor = new SpaceExecutionContextAccessor();

        await Invoke(
            MakeMiddleware(_ =>
            {
                Assert.Same(parent, Activity.Current);
                Assert.Equal(
                    parent.TraceId.ToHexString(),
                    accessor.Current!.TraceId);
                return Task.CompletedTask;
            }),
            context,
            accessor);

        Assert.Same(parent, Activity.Current);
        Assert.Equal(TimeSpan.Zero, parent.Duration);
        Assert.Equal(
            parent.TraceId.ToHexString(),
            context.Response.Headers["X-Trace-ID"]);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Hierarchical_activity_is_not_reused_and_is_restored_unstopped()
    {
        Assert.Null(Activity.Current);
        using var parent = new Activity("hierarchical")
            .SetIdFormat(ActivityIdFormat.Hierarchical)
            .Start();
        var context = Context();
        var accessor = new SpaceExecutionContextAccessor();
        Activity? ownedW3C = null;
        string? seenTrace = null;

        await Invoke(
            MakeMiddleware(_ =>
            {
                ownedW3C = Activity.Current;
                seenTrace = accessor.Current!.TraceId;
                Assert.NotSame(parent, ownedW3C);
                Assert.Equal(ActivityIdFormat.W3C, ownedW3C!.IdFormat);
                Assert.NotEqual(default, ownedW3C.TraceId);
                Assert.Equal(ownedW3C.TraceId.ToHexString(), seenTrace);
                return Task.CompletedTask;
            }),
            context,
            accessor);

        Assert.NotNull(ownedW3C);
        Assert.NotEqual(TimeSpan.Zero, ownedW3C!.Duration);
        Assert.Same(parent, Activity.Current);
        Assert.Equal(TimeSpan.Zero, parent.Duration);
        Assert.Equal(seenTrace, context.Response.Headers["X-Trace-ID"]);
        AssertW3CTraceHeader(context);
        Assert.Null(accessor.Current);
    }

    private static Guid AssertValidGeneratedCorrelation(DefaultHttpContext context)
    {
        Assert.True(
            Guid.TryParse(
                context.Response.Headers["X-Correlation-ID"],
                out var generated));
        Assert.NotEqual(Guid.Empty, generated);
        return generated;
    }

    private static void AssertW3CTraceHeader(DefaultHttpContext context)
    {
        var trace = context.Response.Headers["X-Trace-ID"].ToString();
        Assert.Matches("^[a-f0-9]{32}$", trace);
        Assert.NotEqual(new string('0', 32), trace);
    }

    private static ClaimsIdentity Identity(
        bool authenticated,
        params Claim[] claims)
        => authenticated
            ? new ClaimsIdentity(claims, "TestAuth")
            : new ClaimsIdentity(claims);

}
