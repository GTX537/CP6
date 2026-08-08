using System.IdentityModel.Tokens.Jwt;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class PendingCookieTests
{
    private const string Secret = "test_secret_with_at_least_32_chars__padding";
    private const string Issuer = "CP6";
    private const string Audience = "CP6.Web";

    [Fact]
    public void GeneratePendingToken_carries_jti_and_purpose_and_tenant()
    {
        var uid = Guid.NewGuid();
        var tid = Guid.NewGuid();
        var jti = Guid.NewGuid().ToString("N");
        var jwt = JwtHelper.GeneratePendingToken(uid.ToString(), tid, "2fa_verify", jti, Secret, Issuer, Audience, 5);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        Assert.Equal(jti, token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value);
        Assert.Equal("2fa_verify", token.Claims.First(c => c.Type == "token_use").Value);
        Assert.Equal(tid.ToString(), token.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.True(token.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public void WritePendingCookies_sets_cp6_2fa_httpOnly_and_cp6_csrf_readable()
    {
        var writer = new AuthCookieWriter(Options.Create(new SecurityOptions
        {
            Cookie = new AuthCookieOptions { Secure = false, SameSite = "Strict" }
        }));
        var ctx = new DefaultHttpContext();
        writer.WritePendingCookies(ctx.Response, "pending.jwt", "csrf-token-xyz");

        var setCookies = ctx.Response.Headers["Set-Cookie"].OfType<string>().ToArray();
        Assert.Contains(setCookies, h => h.Contains("cp6_2fa=") && h.Contains("httponly", StringComparison.OrdinalIgnoreCase) && h.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookies, h => h.Contains("cp6_csrf=") && !h.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClearAuthCookies_also_clears_pending_cookie()
    {
        var writer = new AuthCookieWriter(Options.Create(new SecurityOptions
        {
            Cookie = new AuthCookieOptions { Secure = false, SameSite = "Strict" }
        }));
        var ctx = new DefaultHttpContext();
        writer.ClearAuthCookies(ctx.Response);
        var setCookies = ctx.Response.Headers["Set-Cookie"].OfType<string>().ToArray();
        Assert.Contains(setCookies, h => h.StartsWith("cp6_2fa=") && h.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }
}
