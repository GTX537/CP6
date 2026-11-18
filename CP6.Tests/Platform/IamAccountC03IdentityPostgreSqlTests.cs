using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Messaging;
using CP6.Tests.Erp;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Account_C03_registered_service_actual_TLS_publication_rotation_and_removal()
    {
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        var inquirySigning = InquiryPermissionSigningTests.Options();
        using var inquiryKey = InquiryPermissionSigningTests.Signer(inquirySigning);
        var (inquirySource, oidc) = ServiceRegistration(h, Guid.NewGuid());
        oidc.Issuer = inquirySigning.Issuer;
        using var privateKey = RSA.Create(2048);
        oidc.ActiveKeyId = "owned-account-c03";
        oidc.Keys.Add(new() { Kid = oidc.ActiveKeyId, Pem = privateKey.ExportRSAPrivateKeyPem() });
        using var crypto = new CrmOidcCrypto(oidc);
        inquirySource.IdentityPublicationEnabled = true;
        inquirySource.IdentityPublicationRevision = 1;
        inquirySource.Registrations[0].AuthenticationEpoch = Guid.NewGuid();
        var inquiry = new InquiryServicePrincipalRegistryV1(inquirySource, oidc);
        const string accountClient = "account-c03-ingest";
        var secret = CrmOidcCrypto.RandomToken();
        var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(accountClient + ":" + secret));
        var subject = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        oidc.ServiceClients.Add(new() { ClientId = accountClient, TenantId = h.Tenant, Enabled = true,
            SecretSha256 = CrmOidcCrypto.Hash(secret), AllowedScopes = ["cp6.services"] });
        var accountSource = new InquiryServicePrincipalOptionsV1 { Enabled = true, IdentityPublicationEnabled = true,
            Version = "owned-account-1", IdentityPublicationRevision = 1, Registrations = [new() {
                ClientId = accountClient, DepartmentId = h.Parent, SubjectId = subject,
                AuthenticationEpoch = epoch, RoleIds = [71] }] };
        var account = new AccountC03ServicePrincipalRegistryV1(accountSource, oidc, inquiry);
        var options = new CrmIdentityOptions { Enabled = true, Issuer = oidc.Issuer,
            AccountServiceSources = true, ProjectionReaderClientIds = ["fixture-reader"] };
        options.Tenants.Add(h.Tenant, "us");
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        var runtime = new CrmIdentityRuntime(options, new IdentityEventValidator(bundle, oidc.Issuer, true));
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var publisherTenant = new TenantContext();
        await using (var db = server.Context(publisherTenant, runtime))
            await new InquiryServiceIdentityPublisherV1(db, publisherTenant, inquiry, runtime).PublishAsync(h.Tenant);
        async Task Publish(AccountC03ServicePrincipalRegistryV1 registration)
        {
            await using var db = server.Context(publisherTenant, runtime);
            await new AccountC03ServiceIdentityPublisher(db, publisherTenant, registration, runtime).PublishAsync(h.Tenant);
        }

        using var tlsKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=owned-account-c03", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(new Uri(oidc.Issuer).Host);
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(15));
        using var certificate = new X509Certificate2(created.Export(X509ContentType.Pfx));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<CP6Context>(sp => server.Context(sp.GetRequiredService<TenantContext>(), runtime));
        builder.Services.AddScoped(sp => new AccountC03ServiceIdentityIssuer(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<TenantContext>(), account, runtime, oidc, crypto));
        builder.Services.AddScoped(sp => new InquiryServicePermissionIssuerV1(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<TenantContext>(), inquiry, inquiryKey, () => IntakeCatalog(), runtime));
        var discovery = ResultDiscovery(oidc.Issuer, certificate.Thumbprint);
        using var resultBackchannel = discovery.Client;
        ConfigureResultProvider(builder, h, accountClient, account, runtime, oidc, crypto, resultBackchannel);
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new AccountResultControllers())).AddControllersAsServices();
        builder.Services.RemoveAll<CrmOidcController>();
        builder.Services.AddTransient(sp => new CrmOidcController(oidc, crypto, null!, null!, builder.Configuration, null!, null!,
            serviceTokens: sp.GetRequiredService<CrmOidcServiceTokens>(),
            inquiryServicePermissions: sp.GetRequiredService<InquiryServicePermissionIssuerV1>()));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        CP6.WebApi.Configuration.ErpIntegrationConfiguration.MapErpIntegrationEvents(app);
        await app.StartAsync();
        try
        {
            var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            discovery.Port.SetResult(new Uri(origin).Port);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == certificate.Thumbprint }) { BaseAddress = new(origin) };
            const string route = "/connect/account-c03-service-identity";
            const string purpose = AccountC03ServiceIdentityIssuer.Purpose;
            var nonce = ServiceNonce();
            var body = new { purpose, requestNonce = nonce };
            async Task<HttpResponseMessage> Send(string path, object value, string authorization)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
                message.Headers.Add("Authorization", authorization);
                return await http.SendAsync(message);
            }
            async Task Expect(HttpStatusCode status, string path, object value, string authorization)
            {
                using var response = await Send(path, value, authorization);
                Assert.Equal(status, response.StatusCode);
            }
            await Expect(HttpStatusCode.ServiceUnavailable, route, body, basic);
            await Publish(account);
            var source = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x =>
                x.TenantId == h.Tenant && x.EventType == IdentityEventContracts.AccountServicePrincipalChanged);
            Assert.Equal("service-principal:" + subject.ToString("D"), source.AggregateId);
            Assert.Equal(1, source.Version);
            Assert.False(source.IsDeleted);
            using (var good = await Send(route, body, basic))
            {
                Assert.Equal(HttpStatusCode.OK, good.StatusCode);
                Assert.True(good.Headers.CacheControl?.NoStore);
                using var response = JsonDocument.Parse(await good.Content.ReadAsStringAsync());
                var token = response.RootElement.GetProperty("identityToken").GetString()!;
                var principal = crypto.Validate(token, AccountC03ServiceIdentityIssuer.Audience, AccountC03ServiceIdentityIssuer.Contract);
                Assert.Equal(subject.ToString("D"), principal.FindFirst("sub")?.Value);
                Assert.Equal(accountClient, principal.FindFirst("client_id")?.Value);
                Assert.Equal(h.Tenant.ToString("D"), principal.FindFirst("tenant_id")?.Value);
                Assert.Equal(epoch.ToString("D"), principal.FindFirst("authentication_epoch")?.Value);
                Assert.Equal(nonce, principal.FindFirst("nonce")?.Value);
                Assert.Equal(purpose, principal.FindFirst("purpose")?.Value);
                Assert.Equal(IdentityEventContracts.AccountServiceProfile, principal.FindFirst("source_profile")?.Value);
                Assert.Equal(source.PayloadSha256, principal.FindFirst("source_sha256")?.Value);
                Assert.Equal(runtime.ContractBundleSha256, principal.FindFirst("bundle_sha256")?.Value);
                Assert.Equal("1", principal.FindFirst("source_version")?.Value);
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalSeconds, 1, 60);
            }
            var inquiryCommand = new InquiryServiceContextRequestV1("intake:release", pair.SiteId, pair.FormId, ServiceNonce());
            await Expect(HttpStatusCode.OK, "/connect/inquiry-service-context", inquiryCommand, ServiceBasic().ToString());
            await Expect(HttpStatusCode.Unauthorized, "/connect/inquiry-service-context", inquiryCommand, basic);
            await Expect(HttpStatusCode.Unauthorized, route, body, ServiceBasic().ToString());
            await Expect(HttpStatusCode.BadRequest, route, new { purpose = "intake:release", requestNonce = nonce }, basic);
            await Expect(HttpStatusCode.BadRequest, route, new { purpose, requestNonce = nonce, tenantId = h.Tenant }, basic);
            await Expect(HttpStatusCode.BadRequest, route, new { purpose, requestNonce = "bad" }, basic);
            await Expect(HttpStatusCode.BadRequest, route + "?tenant=x", body, basic);
            // 仅向同机指定联验子进程传递测试凭据。业务样本取自实际发布的数据库行。
            var samples = await CheckResultProviderAsync(app, http, h, basic, accountClient, subject, epoch);
            await RunAccountC03ConsumerIfConfigured(h, oidc.Issuer, origin, certificate.Thumbprint,
                accountClient, secret, subject, epoch, samples);
            accountSource.Version = "owned-account-2";
            accountSource.IdentityPublicationRevision = 2;
            accountSource.Registrations.Single().AuthenticationEpoch = Guid.NewGuid();
            var rotated = new AccountC03ServicePrincipalRegistryV1(accountSource, oidc, inquiry);
            await Publish(rotated);
            await Expect(HttpStatusCode.Forbidden, route, body, basic);
            account = rotated;
            await Expect(HttpStatusCode.OK, route, body, basic);
            accountSource.Version = "owned-account-3";
            accountSource.IdentityPublicationRevision = 3;
            oidc.ServiceClients.Single(x => x.ClientId == accountClient).Enabled = false;
            var staleEpoch = new AccountC03ServicePrincipalRegistryV1(accountSource, oidc, inquiry);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(staleEpoch));
            Assert.Equal("C02_SERVICE_REGISTRATION_REQUIRES_NEW_EPOCH_AND_VERSION", failure.Message);
            accountSource.Registrations.Single().AuthenticationEpoch = Guid.NewGuid();
            var disabled = new AccountC03ServicePrincipalRegistryV1(accountSource, oidc, inquiry);
            await Publish(disabled);
            await Expect(HttpStatusCode.Forbidden, route, body, basic);
            account = disabled;
            await Expect(HttpStatusCode.Unauthorized, route, body, basic);
            accountSource.Version = "owned-account-4";
            accountSource.IdentityPublicationRevision = 4;
            accountSource.Registrations.Clear();
            await Publish(new AccountC03ServicePrincipalRegistryV1(accountSource, oidc, inquiry));
            await Expect(HttpStatusCode.Unauthorized, route, body, basic);
            var removed = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.AggregateId == source.AggregateId);
            Assert.True(removed.IsDeleted);
            Assert.Equal(4, removed.Version);
            await Expect(HttpStatusCode.OK, "/connect/inquiry-service-context", inquiryCommand, ServiceBasic().ToString());
        }
        finally { await app.StopAsync(); }
    }

    private static async Task RunAccountC03ConsumerIfConfigured(Harness h, string issuer, string origin,
        string thumbprint, string clientId, string clientSecret, Guid subject, Guid epoch, ResultSample[] samples)
    {
        var project = Environment.GetEnvironmentVariable("CP6_ACCOUNT_C03_CROSS_PROJECT");
        if (string.IsNullOrEmpty(project)) return;
        var output = Environment.GetEnvironmentVariable("CP6_ACCOUNT_C03_CROSS_OUTPUT")!;
        var artifacts = Environment.GetEnvironmentVariable("CP6_CRM_CROSS_ARTIFACTS")!;
        var rows = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == h.Tenant).OrderBy(x => x.AggregateId).ToArrayAsync();
        var file = Path.Combine(output, "account-c03-source-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { issuer, origin, thumbprint, tenantId = h.Tenant,
            departmentId = h.Parent, clientId, subject, epoch, samples, rows = rows.Select(x => new { x.AggregateId, x.EventType,
                x.Version, x.PayloadSha256, x.IsDeleted, data = JsonDocument.Parse(x.PayloadJson).RootElement }) }, IdentityEventContracts.Json));
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["CP6_ACCOUNT_C03_MAIN_SOURCE"] = file;
        start.Environment["CP6_ACCOUNT_C03_FIXTURE_SECRET"] = clientSecret;
        var name = "account-c03-cross-" + Guid.NewGuid().ToString("N") + ".trx";
        foreach (var arg in new[] { "test", project, "--no-restore", "--no-build", "--artifacts-path", artifacts,
            "--filter", "FullyQualifiedName~Account_C03_actual_Main_TLS_and_native_account", "--logger",
            "trx;LogFileName=" + name, "--results-directory", output }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Assert.Equal(0, process.ExitCode);
        var counters = System.Xml.Linq.XDocument.Load(Path.Combine(output, name)).Descendants().Single(x => x.Name.LocalName == "Counters");
        Assert.Equal("1", counters.Attribute("total")?.Value);
        Assert.Equal("1", counters.Attribute("passed")?.Value);
    }
}
