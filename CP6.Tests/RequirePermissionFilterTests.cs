using CP6.Core.Auth;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests;

public class RequirePermissionFilterTests
{
    private sealed class StubPerm : IPermissionService
    {
        private readonly bool _ok;
        public StubPerm(bool ok) => _ok = ok;
        public Task<bool> HasActionAsync(string menu, string action) => Task.FromResult(_ok);
        public Task<bool> HasMenuAsync(string menu) => Task.FromResult(_ok);
    }

    private static AuthorizationFilterContext MakeContext(bool hasAction, bool registerSvc = true)
    {
        var services = new ServiceCollection();
        if (registerSvc) services.AddSingleton<IPermissionService>(new StubPerm(hasAction));
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public async Task NoPermission_Sets403()
    {
        var ctx = MakeContext(hasAction: false);
        await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task HasPermission_DoesNotSetResult()
    {
        var ctx = MakeContext(hasAction: true);
        await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);   // 放行：不设 Result
    }

    [Fact]
    public async Task ServiceMissing_Sets500()
    {
        var ctx = MakeContext(hasAction: true, registerSvc: false);
        await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task Audit_read_denial_returns_stable_code_and_safe_event()
    {
        var writer = new RecordingWriter { Result = false };
        var ctx = MakeSpaceContext(
            writer,
            HttpMethods.Get,
            "/api/space/audit/events");

        await new RequirePermissionAttribute(
            "space-audit",
            "read").OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(
            "SPACE_AUDIT_READ_FORBIDDEN",
            ReadMessage(result));
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("space.permission.check", audit.Action);
        Assert.Equal(SpaceAuditOutcome.Denied, audit.Outcome);
        Assert.Equal("SPACE_AUDIT_READ_FORBIDDEN", audit.ReasonCode);
        Assert.Equal("space-audit:read", audit.Evidence!.PermissionCode);
        Assert.Equal("Denied", audit.Evidence.AuthorizationResult);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Space_denial_writer_false_or_throw_does_not_change_403(
        bool writerThrows)
    {
        var writer = new RecordingWriter
        {
            Result = false,
            Throw = writerThrows,
        };
        var ctx = MakeSpaceContext(
            writer,
            HttpMethods.Post,
            "/api/space/floor");

        await new RequirePermissionAttribute(
            "space-floor",
            "add").OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(
            "无权限：space-floor:add",
            ReadMessage(result));
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("SPACE_PERMISSION_DENIED", audit.ReasonCode);
    }

    [Fact]
    public async Task Aborted_request_during_denial_audit_still_has_403()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var writer = new RecordingWriter { CancelWhenRequested = true };
        var ctx = MakeSpaceContext(
            writer,
            HttpMethods.Get,
            "/api/space/audit/events",
            aborted.Token);

        await new RequirePermissionAttribute(
            "space-audit",
            "read").OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var token = Assert.Single(writer.Tokens);
        Assert.True(token.IsCancellationRequested);
    }

    private static AuthorizationFilterContext MakeSpaceContext(
        ISpaceAuditWriter writer,
        string method,
        string path,
        CancellationToken requestAborted = default)
    {
        var services = new ServiceCollection()
            .AddSingleton<IPermissionService>(new StubPerm(false))
            .AddSingleton(writer)
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = requestAborted,
        };
        http.Request.Method = method;
        http.Request.Path = path;
        var actionContext = new ActionContext(
            http,
            new RouteData(),
            new ActionDescriptor
            {
                DisplayName = "SpaceAuditController.Query",
            });
        return new AuthorizationFilterContext(
            actionContext,
            new List<IFilterMetadata>());
    }

    private static string? ReadMessage(ObjectResult result) =>
        result.Value?.GetType().GetProperty("message")?.GetValue(
            result.Value) as string;

    private sealed class RecordingWriter : ISpaceAuditWriter
    {
        public bool Result { get; init; } = true;
        public bool Throw { get; init; }
        public bool CancelWhenRequested { get; init; }
        public List<SpaceAuditEventInput> Inputs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            Tokens.Add(ct);
            if (CancelWhenRequested)
                ct.ThrowIfCancellationRequested();
            if (Throw)
                throw new InvalidOperationException(
                    "secret audit backend detail");
            return Task.FromResult(Result);
        }
    }
}
