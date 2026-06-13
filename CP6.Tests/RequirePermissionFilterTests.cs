using CP6.Core.Auth;
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
}
