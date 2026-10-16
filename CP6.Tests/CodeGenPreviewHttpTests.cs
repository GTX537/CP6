using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using CP6.WebApi.Controllers.Pub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CP6.Tests;

public class CodeGenPreviewHttpTests
{
    [Fact]
    public async Task Real_routes_enforce_authentication_view_permission_compatibility_and_no_saves()
    {
        await using var host = await PreviewHost.Start();
        var input = CodeGenPreviewTests.Input();
        var body = new CodeGenController.GenSaveReq(input.Table, input.Columns);
        foreach (var path in new[] { "preview", "preview/v1", $"{host.MetadataId}/preview" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Send(path, body, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(path, body, "save")).StatusCode);
            using var response = await host.Send(path, body, "view");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("code").GetInt32());
            var data = json.RootElement.GetProperty("data");
            if (path == "preview/v1")
            {
                Assert.False(data.GetProperty("applied").GetBoolean());
                Assert.Equal(5, data.GetProperty("outputs").GetArrayLength());
                Assert.Equal(new CodeGenService().GeneratePreview(input.Table, input.Columns).InputDigest, data.GetProperty("inputDigest").GetString());
            }
            else Assert.Contains("class Demo", data.GetProperty("Demo.cs").GetString());
        }
        using var save = await host.Send("save", body, "view");
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        using var db = new GuardedContext(host.Options, host.Writes);
        Assert.Equal(1, await db.Pub_GenTables.CountAsync());
        Assert.Equal(1, await db.Pub_GenColumns.CountAsync());
        Assert.Equal(0, host.Writes.Attempts);
    }

    [Fact]
    public async Task Invalid_inline_and_persisted_input_return_safe_errors_including_binding_failures()
    {
        await using var host = await PreviewHost.Start(invalidStored: true);
        var (table, columns) = CodeGenPreviewTests.Input(); columns[0].ClrType = "System.IO.FileInfo";
        foreach (var path in new[] { "preview", "preview/v1", $"{host.MetadataId}/preview" })
        {
            using var response = await host.Send(path, new CodeGenController.GenSaveReq(table, columns), "view");
            await SafeBadRequest(response);
        }
        foreach (var raw in new[] { "null", "{}", "{broken", "{\"table\":null,\"columns\":null}",
                     "{\"table\":{},\"columns\":[null]}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/pub/codegen/preview/v1")
            { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Test-Permission", "view");
            using var response = await host.Client.SendAsync(request);
            await SafeBadRequest(response);
        }
        // An ignored JSON property must still count toward the transport limit.
        var valid = CodeGenPreviewTests.Input();
        using var oversized = await host.Send("preview/v1", new { table = valid.Table, columns = valid.Columns,
            padding = new string('x', CodeGenService.PreviewMaxRequestBytes + 1) }, "view");
        Assert.Contains(oversized.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(0, host.Writes.Attempts);
    }

    private static async Task SafeBadRequest(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("code").GetInt32());
        var message = json.RootElement.GetProperty("message").GetString()!;
        Assert.InRange(message.Length, 1, 100);
        Assert.DoesNotContain("System.IO", message);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), message);
    }

    private sealed class Writes { public int Attempts; }
    private sealed class GuardedContext(DbContextOptions<CP6Context> options, Writes writes, bool seed = false) : CP6Context(options)
    {
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            if (!seed) { writes.Attempts++; throw new InvalidOperationException("Preview attempted persistence"); }
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (!seed) { writes.Attempts++; throw new InvalidOperationException("Preview attempted persistence"); }
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class OnlyCodeGen : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear(); feature.Controllers.Add(typeof(CodeGenController).GetTypeInfo());
        }
    }

    // Test identity/permission sources; actual MVC routing, Authorize and RequirePermission run unchanged.
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-Permission")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "preview-test")], "test")), "test"))
                : AuthenticateResult.NoResult());
    }
    private sealed class TestPermissions(IHttpContextAccessor accessor) : IPermissionService
    {
        public Task<bool> HasActionAsync(string menu, string action) => Task.FromResult(menu == "pub-codegen" && accessor.HttpContext!.Request.Headers["X-Test-Permission"] == action);
        public Task<bool> HasMenuAsync(string menu) => Task.FromResult(false);
    }

    private sealed class PreviewHost : IAsyncDisposable
    {
        private WebApplication App = null!;
        public HttpClient Client = null!;
        public DbContextOptions<CP6Context> Options = null!;
        public readonly Writes Writes = new();
        public Guid MetadataId;
        public static async Task<PreviewHost> Start(bool invalidStored = false)
        {
            var host = new PreviewHost();
            host.Options = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase($"codegen-{Guid.NewGuid()}").Options;
            using (var db = new GuardedContext(host.Options, host.Writes, seed: true))
            {
                var (table, columns) = CodeGenPreviewTests.Input();
                table.Id = Guid.NewGuid(); host.MetadataId = table.Id;
                if (invalidStored) columns[0].ClrType = "System.IO.FileInfo";
                columns[0].GenTableId = table.Id;
                db.Add(table); db.AddRange(columns); await db.SaveChangesAsync();
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "PreviewTests", Args = [] });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped<CP6Context>(_ => new GuardedContext(host.Options, host.Writes));
            builder.Services.AddSingleton<CodeGenService>(); builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IPermissionService, TestPermissions>();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(CodeGenController).Assembly)
                .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new OnlyCodeGen()));
            host.App = builder.Build(); host.App.UseAuthentication(); host.App.UseAuthorization(); host.App.MapControllers();
            try
            {
                await host.App.StartAsync();
                host.Client = new HttpClient { BaseAddress = new Uri(host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(15) };
                return host;
            }
            catch { await host.App.DisposeAsync(); throw; }
        }
        public Task<HttpResponseMessage> Send(string path, object body, string? permission)
        {
            var get = path.EndsWith("/preview");
            var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, "api/pub/codegen/" + path);
            if (!get) request.Content = JsonContent.Create(body);
            if (permission is not null) request.Headers.Add("X-Test-Permission", permission);
            return Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
    }
}
