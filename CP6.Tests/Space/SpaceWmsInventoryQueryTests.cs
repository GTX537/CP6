using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Space;

// Real Kestrel/JWT/MVC/RequirePermission/CSRF transport. Runtime and permission
// data are controlled fixtures; real runtime scope behavior is tested in Space.IntegrationTests.
public sealed class SpaceWmsInventoryQueryTests
{
    [Fact]
    public async Task Post_preserves_exact_selection_empty_and_duplicate_inputs_and_get_compatibility()
    {
        await using var host = await Host.Start();
        foreach (var count in new[] { 0, 101, 10_000 })
        {
            var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            var repeated = ids.Concat(ids).ToArray();
            using var response = await host.Send(JsonSerializer.Serialize(new { locationLogicalIds = repeated }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(repeated, host.Calls.Last().Ids);
            Assert.Equal(host.Tenant, host.Calls.Last().Tenant);
            var body = JsonSerializer.Deserialize<SpaceWmsRuntimeInventoryResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(host.Published, body.PublishedVersionId);
            Assert.Equal("fixture-source", body.Source.DataSourceId);
        }
        var id = Guid.NewGuid();
        using var get = await host.Send(null, get: true, query: $"?locationLogicalId={id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(new[] { id }, host.Calls.Last().Ids);
        using var unfiltered = await host.Send(null, get: true);
        Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
        Assert.Null(host.Calls.Last().Ids); // Existing GET default remains explicit compatibility.
    }

    [Fact]
    public async Task Post_enforces_authentication_permission_csrf_and_json_binding_before_runtime()
    {
        await using var host = await Host.Start();
        foreach (var invalid in new[] { "", "null", "{}", "{\"locationLogicalIds\":null}", "{\"locationLogicalIds\":[\"bad-guid\"]}", "{\"locationLogicalIds\":[null]}" })
        {
            using var response = await host.Send(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var anonymous = await host.Send("{\"locationLogicalIds\":[]}", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var denied = await host.Send("{\"locationLogicalIds\":[]}", permission: false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var csrf in new string?[] { null, "wrong" })
        {
            using var response = await host.Send("{\"locationLogicalIds\":[]}", csrf: csrf);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Empty(host.Calls);
        using var allowed = await host.Send("{\"locationLogicalIds\":[]}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Single(host.Calls);
    }

    [Fact]
    public async Task Post_body_and_server_request_line_have_hard_byte_boundaries()
    {
        await using var host = await Host.Start();
        const string json = "{\"locationLogicalIds\":[]}";
        var exact = json.PadRight(SpaceWmsRuntimeInventoryQueryRequest.MaxBodyBytes);
        using var accepted = await host.Send(exact);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var rejected = await host.Send(exact + " ");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        using var chunked = await host.Send(exact + " ", chunked: true);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        Assert.Single(host.Calls);

        // Default Kestrel request-line bound (includes CRLF); no existing server is contacted.
        Assert.Contains("401", await host.RawGet(8_192));
        Assert.Contains("414", await host.RawGet(8_193));
    }

    private sealed class OnlyRuntime : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { feature.Controllers.Clear(); feature.Controllers.Add(typeof(SpaceWmsRuntimeController).GetTypeInfo()); }
    }

    private sealed class StreamingJson(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }

    private sealed class Host : IAsyncDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid Site { get; } = Guid.NewGuid();
        public Guid Published { get; } = Guid.NewGuid();
        public List<(Guid[]? Ids, Guid Tenant)> Calls { get; } = [];
        private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private WebApplication app = null!;
        private HttpClient client = null!;
        private string Route => $"/api/space/design/v1/sites/{Site}/runtime/inventory";

        public static async Task<Host> Start()
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "InventoryTransportTests", Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:18232");
            var services = builder.Services;
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, TenantContext>();
            services.Configure<SecurityOptions>(o => o.Csrf.Enabled = true);
            var localizer = new Mock<IStringLocalizer>();
            localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
            services.AddSingleton(localizer.Object);
            services.AddScoped<IPermissionService>(sp => {
                var mock = new Mock<IPermissionService>();
                mock.Setup(p => p.HasActionAsync("space", "model:read")).ReturnsAsync(() =>
                    sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.HasClaim("inventory_read", "true"));
                return mock.Object;
            });
            services.AddScoped<ISpaceWmsRuntimeService>(sp => {
                var mock = new Mock<ISpaceWmsRuntimeService>(MockBehavior.Strict);
                mock.Setup(s => s.QueryInventoryAsync(host.Site, It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
                    .Returns((Guid site, IReadOnlyCollection<Guid>? ids, CancellationToken ct) => {
                        host.Calls.Add((ids?.ToArray(), sp.GetRequiredService<ITenantContext>().CurrentTenantId));
                        var now = DateTimeOffset.UtcNow;
                        return Task.FromResult(new SpaceWmsRuntimeInventoryResponse(site, host.Published, "fixture",
                            new("Simulated", "fixture-adapter", "fixture-source", now, now, 0, 0, true, true), []));
                    });
                return mock.Object;
            });
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
                o.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = "bug132", ValidAudience = "bug132", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(host.secret)) };
                o.Events = new JwtBearerEvents { OnMessageReceived = c => { c.Token = c.Request.Cookies[AuthCookieWriter.AccessCookie]; return Task.CompletedTask; } };
            });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(SpaceWmsRuntimeController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyRuntime()));
            host.app = builder.Build();
            host.app.UseRouting(); host.app.UseAuthentication(); host.app.UseMiddleware<TenantMiddleware>();
            host.app.UseMiddleware<BizExceptionMiddleware>(); host.app.UseMiddleware<CsrfMiddleware>();
            host.app.UseAuthorization(); host.app.MapControllers();
            try { await host.app.StartAsync(); }
            catch { await host.app.DisposeAsync(); throw; }
            host.client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri("http://127.0.0.1:18232") };
            return host;
        }

        public async Task<HttpResponseMessage> Send(string? body, bool authenticated = true, bool permission = true, string? csrf = "match", bool get = false, string query = "", bool chunked = false)
        {
            using var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, Route + query);
            var jwt = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("bug132", "bug132",
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("tenant_id", Tenant.ToString()), new Claim("inventory_read", permission ? "true" : "false")],
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));
            request.Headers.Add("Cookie", $"{AuthCookieWriter.CsrfCookie}=match" + (authenticated ? $"; {AuthCookieWriter.AccessCookie}={jwt}" : ""));
            if (csrf != null) request.Headers.Add("X-CSRF-Token", csrf);
            if (!get)
            {
                request.Content = chunked ? new StreamingJson(body ?? "") : new StringContent(body ?? "", Encoding.UTF8, "application/json");
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            return await client.SendAsync(request);
        }

        public async Task<string> RawGet(int lineBytes)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, 18232);
            var prefix = $"GET {Route}?padding=";
            const string suffix = " HTTP/1.1\r\n";
            var line = prefix + new string('x', lineBytes - prefix.Length - suffix.Length) + suffix;
            await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(line + "Host: 127.0.0.1:18232\r\nConnection: close\r\n\r\n"));
            using var reader = new StreamReader(tcp.GetStream());
            return (await reader.ReadLineAsync())!;
        }

        public async ValueTask DisposeAsync()
        { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }
    }
}
