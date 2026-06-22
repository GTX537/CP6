using System.Security.Claims;
using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// CSRF 双提交 + 强制改密中间件（S 类认证加固 T6）。直接构造 DefaultHttpContext + 假 next 单元测，
/// 不起 TestServer（更轻，符合房屋直 new 风格）。
/// </summary>
public class SecurityMiddlewareTests
{
    private static (RequestDelegate next, Func<bool> wasCalled) FakeNext()
    {
        var called = false;
        RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
        return (next, () => called);
    }

    // ─────────────── CsrfMiddleware ───────────────
    private static CsrfMiddleware Csrf(RequestDelegate next, bool enabled = true)
        => new(next, Options.Create(new SecurityOptions { Csrf = new CsrfOptions { Enabled = enabled } }));

    private static DefaultHttpContext Ctx(string method, string path, string? csrfCookie = null, string? csrfHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (csrfCookie != null) ctx.Request.Headers["Cookie"] = $"{AuthCookieWriter.CsrfCookie}={csrfCookie}";
        if (csrfHeader != null) ctx.Request.Headers["X-CSRF-Token"] = csrfHeader;
        return ctx;
    }

    [Fact]
    public async Task Csrf_unsafe_method_without_token_is_rejected()
    {
        var (next, called) = FakeNext();
        var ex = await Assert.ThrowsAsync<BizException>(() => Csrf(next).Invoke(Ctx("POST", "/api/order")));
        Assert.Equal("E-SEC-010", ex.Code);
        Assert.False(called());   // 未放行
    }

    [Fact]
    public async Task Csrf_matching_cookie_and_header_passes()
    {
        var (next, called) = FakeNext();
        await Csrf(next).Invoke(Ctx("POST", "/api/order", csrfCookie: "tok-abc", csrfHeader: "tok-abc"));
        Assert.True(called());
    }

    [Fact]
    public async Task Csrf_mismatched_token_is_rejected()
    {
        var (next, _) = FakeNext();
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            Csrf(next).Invoke(Ctx("POST", "/api/order", csrfCookie: "tok-abc", csrfHeader: "tok-DIFFERENT")));
        Assert.Equal("E-SEC-010", ex.Code);
    }

    [Fact]
    public async Task Csrf_login_endpoint_is_exempt()
    {
        var (next, called) = FakeNext();
        await Csrf(next).Invoke(Ctx("POST", "/api/auth/login"));   // 无 csrf 也放行
        Assert.True(called());
    }

    [Fact]
    public async Task Csrf_refresh_endpoint_is_NOT_exempt()
    {
        var (next, _) = FakeNext();
        await Assert.ThrowsAsync<BizException>(() => Csrf(next).Invoke(Ctx("POST", "/api/auth/refresh")));
    }

    [Fact]
    public async Task Csrf_safe_method_passes_without_token()
    {
        var (next, called) = FakeNext();
        await Csrf(next).Invoke(Ctx("GET", "/api/order"));
        Assert.True(called());
    }

    [Fact]
    public async Task Csrf_disabled_passes_unsafe_without_token()
    {
        var (next, called) = FakeNext();
        await Csrf(next, enabled: false).Invoke(Ctx("POST", "/api/order"));
        Assert.True(called());
    }

    // ─────────────── MustChangePasswordMiddleware ───────────────
    private static DefaultHttpContext AuthCtx(string path, bool authenticated, bool mustChange)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        var claims = new List<Claim> { new("must_change_password", mustChange ? "true" : "false") };
        // 给 authenticationType 才使 IsAuthenticated=true
        var identity = authenticated ? new ClaimsIdentity(claims, "TestAuth") : new ClaimsIdentity(claims);
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public async Task MustChange_blocks_non_allowlisted_path()
    {
        var (next, called) = FakeNext();
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            new MustChangePasswordMiddleware(next).Invoke(AuthCtx("/api/order", authenticated: true, mustChange: true)));
        Assert.Equal("E-SEC-009", ex.Code);
        Assert.False(called());
    }

    [Theory]
    [InlineData("/api/auth/change-password")]
    [InlineData("/api/auth/logout")]
    public async Task MustChange_allows_change_password_and_logout(string path)
    {
        var (next, called) = FakeNext();
        await new MustChangePasswordMiddleware(next).Invoke(AuthCtx(path, authenticated: true, mustChange: true));
        Assert.True(called());
    }

    [Fact]
    public async Task MustChange_passes_when_flag_false()
    {
        var (next, called) = FakeNext();
        await new MustChangePasswordMiddleware(next).Invoke(AuthCtx("/api/order", authenticated: true, mustChange: false));
        Assert.True(called());
    }

    [Fact]
    public async Task MustChange_passes_when_not_authenticated()
    {
        var (next, called) = FakeNext();
        await new MustChangePasswordMiddleware(next).Invoke(AuthCtx("/api/order", authenticated: false, mustChange: true));
        Assert.True(called());
    }
}
