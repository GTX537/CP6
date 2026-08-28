using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.WebApi.Localization;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Tests.Space;

public sealed class SpaceReleaseRehearsalHttpSecurityTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Issuer = "cp6-space-ga-rehearsal";
    private const string Audience = "cp6-space-ga-http";
    private static readonly SymmetricSecurityKey ControlledSigningKey =
        new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Signed_external_tokens_fail_closed_over_real_http()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "ControlledAcceptance",
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ITenantContext, TenantContext>();
        builder.Services.AddSingleton<SpaceExecutionContextAccessor>();
        builder.Services.AddSingleton<ISpaceExecutionContextAccessor>(service =>
            service.GetRequiredService<SpaceExecutionContextAccessor>());
        builder.Services.AddSingleton<ISpaceExecutionContextManager>(service =>
            service.GetRequiredService<SpaceExecutionContextAccessor>());
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    IssuerSigningKey = SigningKey(),
                };
            });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseMiddleware<TenantMiddleware>();
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (BizException exception)
            {
                context.Response.StatusCode = exception.HttpStatus;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = exception.Code,
                });
            }
        });
        app.UseMiddleware<SpaceExecutionContextMiddleware>();
        app.UseAuthorization();
        app.MapGet(
                "/api/space/design/v1/rehearsal-probe",
                () => Results.Ok(new { status = "internal-only" }))
            .RequireAuthorization();
        app.MapGet(
                "/api/space/portal/v1/sites",
                () => Results.Ok(new { status = "published-only" }))
            .RequireAuthorization();

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient
            {
                BaseAddress = new Uri(address),
            };

            foreach (var role in new[] { "Customer", "Supplier", "3PL" })
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/api/space/design/v1/rehearsal-probe");
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    Token("external", role, OrganizationId));
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var payload = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                Assert.Equal(
                    "SPACE_EXTERNAL_SUBJECT_DENIED",
                    payload.RootElement.GetProperty("code").GetString());
            }

            using (var portal = new HttpRequestMessage(
                       HttpMethod.Get,
                       "/api/space/portal/v1/sites"))
            {
                portal.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    Token("external", "Customer", OrganizationId));
                using var response = await client.SendAsync(portal);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var portalWrite = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/space/portal/v1/sites"))
            {
                portalWrite.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    Token("external", "Customer", OrganizationId));
                using var response = await client.SendAsync(portalWrite);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var payload = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                Assert.Equal(
                    "SPACE_EXTERNAL_PORTAL_READ_ONLY",
                    payload.RootElement.GetProperty("code").GetString());
            }

            using (var internalRequest = new HttpRequestMessage(
                       HttpMethod.Get,
                       "/api/space/design/v1/rehearsal-probe"))
            {
                internalRequest.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        Token("internal", "Administrator", null));
                using var response = await client.SendAsync(internalRequest);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string Token(
        string subjectType,
        string role,
        Guid? organizationId)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", TenantId.ToString()),
            new(ClaimTypes.NameIdentifier, ActorId.ToString()),
            new(ClaimTypes.Name, "BUBAO.GAO"),
            new(ClaimTypes.Role, role),
            new("subject_type", subjectType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (organizationId.HasValue)
        {
            claims.Add(new Claim(
                "organization_context_id",
                organizationId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                SigningKey(),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SymmetricSecurityKey SigningKey() =>
        ControlledSigningKey;
}
