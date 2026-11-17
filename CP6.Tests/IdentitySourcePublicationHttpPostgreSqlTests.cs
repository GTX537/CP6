using System.Net;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Approved_source_publication_keeps_tenant_exact_directory_and_original_command_over_http()
    {
        await using var h = await TenantManagementSeed();
        var environment = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        environment["CrmOidc__ServiceClients__0__SecretSha256"] = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes("Source1!Secret"));
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["JWT__Secret"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var old = new IdentitySourceBindingV1(h.Tenant, "iam-fixture", "https://iam-writer.example.test", "us",
            ["fixture-reader"], ["fixture-reader"], IdentityEventContracts.Hash(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "contracts/events/platform", Cp6ContractBundle.IndexFileName))));
        var runtime = SourceRuntime(old);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);
        var cookies = await Login(host, h);
        using var initialToken = await SourceToken(host, "fixture-reader");
        Assert.True(initialToken.StatusCode == HttpStatusCode.OK, await initialToken.Content.ReadAsStringAsync());
        using var initialGrant = await SourceAuthorize(host, cookies);
        Assert.Equal(HttpStatusCode.Redirect, initialGrant.StatusCode);
        Assert.Contains("code=", initialGrant.Headers.Location!.Query);
        var path = $"api/platform/tenant/{h.Tenant:D}/identity-source-v1";
        using var before = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
        Assert.True(before.StatusCode == HttpStatusCode.OK, await before.Content.ReadAsStringAsync());
        using var current = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        var next = old with { Slug = "approved-next-", Issuer = "https://approved-source.example.test", Region = "eu",
            ServiceClientIds = ["next-reader"], ReaderClientIds = ["next-reader"] };
        var command = new
        {
            OperationRef = "身份来源/批准原命令", ExpectedRevision = "0",
            ExpectedSourceSha256 = current.RootElement.GetProperty("sourceSha256").GetString(), Next = next,
            OwnerApprovalRef = "模拟租户Owner批准/保留全部原事实",
            EffectiveAfterUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ApprovalValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            CompatibilityWindowSeconds = 0, SessionDisposition = "REAUTHENTICATE_ALL", RegionDataDisposition = "KEEP_EXISTING_STORAGE"
        };
        using var csrf = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/commands", command, false));
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using var applied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/commands", command));
        Assert.True(applied.StatusCode == HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var bytes = await applied.Content.ReadAsByteArrayAsync();
        using var result = JsonDocument.Parse(bytes);
        Assert.Equal("identity-source-publication-v1", result.RootElement.GetProperty("contract").GetString());
        Assert.Equal(h.Tenant, result.RootElement.GetProperty("current").GetProperty("tenantId").GetGuid());
        Assert.Equal("https://approved-source.example.test", result.RootElement.GetProperty("current").GetProperty("issuer").GetString());
        Assert.Equal("1", result.RootElement.GetProperty("revision").GetString());
        var items = result.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(await h.Db.CrmIdentitySnapshots.CountAsync(x => x.TenantId == h.Tenant), items.Length);
        Assert.Contains(items, item => item.GetProperty("item").GetProperty("aggregateId").GetString() == "tenant:" + h.Tenant.ToString("D"));
        using var replay = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/commands", command));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(bytes, await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal(h.Tenant, await h.Db.Sys_Users.Where(x => x.Id == h.User).Select(x => x.TenantId).SingleAsync());
        var revoked = items.Where(item => IdentityEventContracts.IsTokenRevoked(item.GetProperty("item").GetProperty("eventType").GetString())).ToArray();
        Assert.Equal(2, revoked.Length);
        Assert.All(revoked, item => Assert.Equal(old.Issuer, item.GetProperty("source").GetProperty("issuer").GetString()));
        Assert.NotEmpty(result.RootElement.GetProperty("retiredBrowserFamilies").EnumerateArray());
        Assert.True(await h.Db.CrmServiceTokenRecords.Where(x => x.TenantId == h.Tenant).AllAsync(x => x.RevokedAtUtc != null));
        using var rejectedOld = await SourceToken(host, "fixture-reader");
        Assert.NotEqual(HttpStatusCode.OK, rejectedOld.StatusCode);
        var nextEnvironment = new Dictionary<string, string>(environment)
        {
            ["CrmOidc__Issuer"] = next.Issuer, ["CrmOidc__Organizations__0__Slug"] = next.Slug,
            ["CrmOidc__Organizations__0__Region"] = next.Region, ["CrmIdentity__Tenants__" + h.Tenant.ToString("D")] = next.Region,
            ["CrmOidc__ServiceClients__0__ClientId"] = "next-reader", ["CrmIdentity__ProjectionReaderClientIds__0"] = "next-reader"
        };
        await using var nextHost = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, nextEnvironment, allowAutoRedirect: false);
        using var oldNativeProfile = await nextHost.Send(OperationsRequest(cookies, HttpMethod.Get, "api/auth/profile"));
        Assert.Equal(HttpStatusCode.OK, oldNativeProfile.StatusCode);
        using var retiredGrant = await SourceAuthorize(nextHost, cookies);
        Assert.Equal(HttpStatusCode.Redirect, retiredGrant.StatusCode);
        Assert.Contains("error=login_required", retiredGrant.Headers.Location!.Query);
        var nextCookies = await Login(nextHost, h);
        using var freshGrant = await SourceAuthorize(nextHost, nextCookies);
        Assert.Equal(HttpStatusCode.Redirect, freshGrant.StatusCode);
        Assert.Contains("code=", freshGrant.Headers.Location!.Query);
        using var nextCurrent = await nextHost.Send(OperationsRequest(nextCookies, HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.OK, nextCurrent.StatusCode);
        using var metadata = JsonDocument.Parse(await nextCurrent.Content.ReadAsStringAsync());
        Assert.Equal("1", metadata.RootElement.GetProperty("revision").GetString());
        Assert.Equal(result.RootElement.GetProperty("currentSourceSha256").GetString(), metadata.RootElement.GetProperty("sourceSha256").GetString());
        using var nextToken = await SourceToken(nextHost, "next-reader");
        Assert.True(nextToken.StatusCode == HttpStatusCode.OK, await nextToken.Content.ReadAsStringAsync());
        using var tokenBody = JsonDocument.Parse(await nextToken.Content.ReadAsStringAsync());
        var accessToken = tokenBody.RootElement.GetProperty("access_token").GetString()!;
        // 独占 .example issuer 不提供网络 JWKS；真实 C02 HTTP 组合宿主只替换测试公钥来源。
        await using var readerHost = await SourceReaderHttpHost.Start(server, nextEnvironment, h.Db.Database.GetConnectionString()!);
        foreach (var version in new[] { 1, 2 })
        {
            using var read = new HttpRequestMessage(HttpMethod.Get, "internal/crm/identity/source/v1/publications/" + version);
            read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await readerHost.Client.SendAsync(read);
            Assert.Equal(version == 1 ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (version == 1) Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        }
        using var original = await nextHost.Send(OperationsRequest(nextCookies, HttpMethod.Post, path + "/commands/read", new { command.OperationRef }));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(bytes, await original.Content.ReadAsByteArrayAsync());
        await using var readDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var approvedReader = new IdentitySnapshotReader(readDb, SourceRuntime(next), new EphemeralDataProtectionProvider());
        Assert.Equal(bytes, await approvedReader.ReadPublicationAsync(h.Tenant, 1));
        Assert.Null(await approvedReader.ReadPublicationAsync(h.Tenant, 2));
        var oldReader = new IdentitySnapshotReader(readDb, runtime, new EphemeralDataProtectionProvider());
        Assert.Equal("C02_SOURCE_NOT_APPROVED", (await Assert.ThrowsAsync<IdentityReadException>(() => oldReader.ReadVersionsAsync(h.Tenant, null))).Message);
        using var conflict = await nextHost.Send(OperationsRequest(nextCookies, HttpMethod.Post, path + "/commands", new IdentitySourceCommandV1(
            command.OperationRef, "0", command.ExpectedSourceSha256!, next, "不同批准", command.EffectiveAfterUtc,
            command.ApprovalValidUntilUtc, 0, "REAUTHENTICATE_ALL", "KEEP_EXISTING_STORAGE")));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var backupAdministrator = await TenantManagementSeed();
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        using var revokedAdmin = await nextHost.Send(OperationsRequest(nextCookies, HttpMethod.Post, path + "/commands/read", new { command.OperationRef }));
        Assert.True(revokedAdmin.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    private sealed class SourceReaderHttpHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        internal HttpClient Client { get; } = client;
        internal static async Task<SourceReaderHttpHost> Start(TenantManagementPostgresFixture postgres,
            IReadOnlyDictionary<string, string> environment, string connection)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders(); builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(environment.ToDictionary(x => x.Key.Replace("__", ":"), x => (string?)x.Value));
            builder.Configuration["ConnectionStrings:DefaultConnection"] = connection;
            builder.Configuration["Database:Provider"] = "PostgreSql";
            var options = builder.Configuration.GetSection("CrmOidc").Get<CrmOidcOptions>()!;
            options.Validate(true);
            builder.Services.AddSingleton(options);
            builder.Services.AddScoped<ITenantContext>(_ => new TenantContext());
            builder.Services.AddScoped(sp => postgres.Context(sp.GetRequiredService<ITenantContext>()));
            builder.Services.AddScoped<ICrmOidcServiceDirectory, CrmOidcDirectory>();
            builder.Services.AddSingleton(Mock.Of<ITokenBlacklistService>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IPasswordPolicyService>(MockBehavior.Strict));
            builder.Services.AddCrmIdentityEvents(builder.Configuration, options, new(DatabaseProvider.PostgreSql));
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            using var rsa = System.Security.Cryptography.RSA.Create();
            var active = options.Keys.Single(k => k.Kid == options.ActiveKeyId);
            rsa.ImportFromPem(active.Pem);
            var key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = active.Kid };
            builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, bearer =>
            {
                var signing = new OpenIdConnectConfiguration { Issuer = options.Issuer };
                signing.SigningKeys.Add(key);
                bearer.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(signing);
                bearer.MapInboundClaims = false;
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(CrmIdentityController).Assembly);
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            try
            {
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                return new(app, new HttpClient { BaseAddress = new(address) });
            }
            catch { await app.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }
    }

    private static Task<HttpResponseMessage> SourceToken(AuthenticationApiProcess host, string client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "connect/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client + ":Source1!Secret")));
        request.Headers.Add("X-Forwarded-Proto", "https");
        return host.Send(request);
    }

    private static Task<HttpResponseMessage> SourceAuthorize(AuthenticationApiProcess host, Dictionary<string, string> cookies)
        => host.Send(OperationsRequest(cookies, HttpMethod.Get,
            "api/auth/crm-authorize?client_id=CP6.Web&redirect_uri=https%3A%2F%2Fiam.example.test%2Fcallback&response_type=code" +
            "&code_challenge_method=S256&code_challenge=" + new string('A', 43) +
            "&state=source-test-state-1&nonce=source-test-nonce-1&scope=openid%20profile%20crm&prompt=none"));

    [TenantManagementPostgresTheory]
    [InlineData("revision", "C02_SOURCE_VERSION_CHANGED")]
    [InlineData("hash", "C02_SOURCE_VERSION_CHANGED")]
    [InlineData("missing", "C02_SOURCE_DIRECTORY_INCOMPLETE")]
    [InlineData("expired", "C02_SOURCE_APPROVAL_NOT_CURRENT")]
    [InlineData("foreign", "C02_SOURCE_COMMAND_INVALID")]
    [InlineData("client", "C02_SOURCE_COMMAND_INVALID")]
    [InlineData("unsafe-client", "C02_SOURCE_COMMAND_INVALID")]
    [InlineData("browser-client", "C02_SOURCE_COMMAND_INVALID")]
    [InlineData("display", "C02_SOURCE_UNCHANGED")]
    public async Task Source_publication_rejects_incomplete_unapproved_or_stale_input_atomically(string fault, string code)
    {
        await using var h = await TenantManagementSeed();
        var old = new IdentitySourceBindingV1(h.Tenant, "source-" + h.Tenant.ToString("N"), "https://iam-writer.example.test", "us",
            [h.Tenant.ToString("N")], [h.Tenant.ToString("N")], "");
        var runtime = SourceRuntime(old); old = runtime.Options.SourceBindings[h.Tenant];
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        if (fault == "display")
            await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.TenantName, "只修改展示名称"));
        if (fault == "missing")
            await h.Db.CrmIdentitySnapshots.Where(x => x.TenantId == h.Tenant && x.AggregateId == "user:" + h.User.ToString("D")).ExecuteDeleteAsync();
        var before = await h.Db.CrmIdentityTenantGenerations.Where(x => x.TenantId == h.Tenant).Select(x => x.Generation).SingleAsync();
        var next = old with { Issuer = "https://approved.example.test", TenantId = fault == "foreign" ? Guid.NewGuid() : h.Tenant,
            ReaderClientIds = fault == "client" ? ["unregistered-reader"] : old.ReaderClientIds };
        if (fault == "display") next = old;
        if (fault is "unsafe-client" or "browser-client")
        {
            var client = fault == "unsafe-client" ? "unloadable:client" : "CP6.Web";
            next = next with { ServiceClientIds = [client], ReaderClientIds = [client] };
        }
        var command = new IdentitySourceCommandV1("模拟批准/" + fault, fault == "revision" ? "100" : "0",
            fault == "hash" ? new string('0', 64) : IdentityEventContracts.Hash(JsonSerializer.SerializeToUtf8Bytes(old, IdentityEventContracts.Json)),
            next, "模拟Owner批准", DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(fault == "expired" ? -1 : 5), 0, "REAUTHENTICATE_ALL", "KEEP_EXISTING_STORAGE");
        var owner = new IdentitySourceAdministrationV1(h.Db, runtime, new HttpContextAccessor { HttpContext = h.Http });
        var session = new TenantManagementSessionV1(_ => Task.FromResult(h.User), _ => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => owner.PublishAsync(h.Tenant, command, session));
        Assert.Equal(code, error.Message);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(before, await h.Db.CrmIdentityTenantGenerations.Where(x => x.TenantId == h.Tenant).Select(x => x.Generation).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<long>($"SELECT revision AS \"Value\" FROM cp6_identity_source_v1.registrations WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_identity_source_v1.publications WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    private static CrmIdentityRuntime SourceRuntime(IdentitySourceBindingV1 source)
    {
        var options = new CrmIdentityOptions { Enabled = true, Issuer = source.Issuer,
            Tenants = { [source.TenantId] = source.Region }, ProjectionReaderClientIds = source.ReaderClientIds };
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        var runtime = new CrmIdentityRuntime(options, new IdentityEventValidator(bundle, source.Issuer));
        options.SourceBindings.Add(source.TenantId, source with { ContractBundleSha256 = runtime.ContractBundleSha256 });
        return runtime;
    }
}
