using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task Inquiry_export_service_v2_authenticates_Basic_without_browser_CSRF()
    {
        await using var h = await Seed();
        var pair = await GrantPrivacy(h, scope: 5);
        var options = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        var service = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        await InstallUserSealFixture(crm, seal, h.Tenant, store);

        var env = IdentityEnvironment(h.Tenant);
        env["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        env["Startup__SkipHostedServices"] = "false";
        env["Kafka__BootstrapServers"] = "";
        env["CrmOidc__Issuer"] = options.Issuer;
        env["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        env["CrmOidc__ServiceClients__1__ClientId"] = ServiceClient;
        env["CrmOidc__ServiceClients__1__TenantId"] = h.Tenant.ToString("D");
        env["CrmOidc__ServiceClients__1__SecretSha256"] = CrmOidcCrypto.Hash(ServiceSecret);
        env["CrmOidc__ServiceClients__1__AllowedScopes__0"] = "cp6.services";
        env["CrmOidc__ServiceClients__1__Enabled"] = "true";
        var binding = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        env[binding + "StoreId"] = store.ToString("D");
        env[binding + "ConnectionString"] = seal.ConnectionString;
        env["CrmIdentity__DaprHttpEndpoint"] = Environment.GetEnvironmentVariable("CP6_W669_DAPR_HTTP")!;
        env["CrmIdentity__DaprGrpcEndpoint"] = Environment.GetEnvironmentVariable("CP6_W669_DAPR_GRPC")!;
        env["InquiryPermissionSigning__Enabled"] = "true";
        env["InquiryPermissionSigning__Issuer"] = options.Issuer;
        env["InquiryPermissionSigning__KeyId"] = options.KeyId;
        env["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        env["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        env["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        env["InquiryServicePrincipals__Enabled"] = "true";
        env["InquiryServicePrincipals__Version"] = "w669-v2-csrf";
        env["InquiryServicePrincipals__IdentityPublicationEnabled"] = "true";
        env["InquiryServicePrincipals__IdentityPublicationRevision"] = "1";
        env["InquiryServicePrincipals__Registrations__0__ClientId"] = ServiceClient;
        env["InquiryServicePrincipals__Registrations__0__SubjectId"] = service.ToString("D");
        env["InquiryServicePrincipals__Registrations__0__AuthenticationEpoch"] = epoch.ToString("D");
        env["InquiryServicePrincipals__Registrations__0__DepartmentId"] = h.Parent.ToString("D");
        env["InquiryServicePrincipals__Registrations__0__RoleIds__0"] = "71";

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, env);
        const string path = "/connect/inquiry-service-context-v2";
        const string domain = "cp6-inquiry-service-context-v2";
        const string fence = "X-CP6-Inquiry-Tenant-Fence";
        var command = new InquiryServiceContextRequestV1("privacy:export", pair.SiteId, pair.FormId, ServiceNonce());
        var basic = ServiceBasic().ToString();
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var request = Export(path, command, basic);
            using var response = await host.Send(request, startup.Token);
            var payload = await response.Content.ReadAsStringAsync(startup.Token);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                using var waiting = JsonDocument.Parse(payload);
                if (waiting.RootElement.GetProperty("error").GetString() == "inquiry_service_identity_not_published")
                {
                    await Task.Delay(125, startup.Token);
                    continue;
                }
            }
            Assert.True(response.StatusCode == HttpStatusCode.OK, payload);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var tenantSource = await h.Db.CrmIdentitySnapshots.AsNoTracking()
                .SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == $"tenant:{h.Tenant:D}");
            Assert.Equal($"{store:D}/{tenantSource.Version}", Assert.Single(response.Headers.GetValues(fence)));
            var source = await h.Db.CrmIdentitySnapshots.AsNoTracking()
                .SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == $"service-principal:{service:D}");
            var body = JsonNode.Parse(payload)!.AsObject();
            Assert.True(ContextSignature(body, options.PublicKeyBase64, domain));
            Assert.Equal(domain, body["schemaVersion"]!.GetValue<string>());
            Assert.Equal(h.Tenant, body["organizationId"]!.GetValue<Guid>());
            Assert.Equal(service, body["subjectId"]!.GetValue<Guid>());
            Assert.Equal(epoch, body["authenticationEpoch"]!.GetValue<Guid>());
            Assert.Equal(ServiceClient, body["clientId"]!.GetValue<string>());
            Assert.Equal(command.RequestNonce, body["requestNonce"]!.GetValue<string>());
            Assert.Equal(source.Version, body["sourceVersion"]!.GetValue<int>());
            Assert.Equal(source.PayloadSha256, body["sourcePayloadSha256"]!.GetValue<string>());
            using var permission = JsonDocument.Parse(body["permissionJson"]!.GetValue<string>());
            Assert.Equal("privacy:export", permission.RootElement.GetProperty("purpose").GetString());
            Assert.Equal(7, permission.RootElement.GetProperty("fieldPermissions").GetArrayLength());
            break;
        }

        await Denied(Export(path, command, ServiceBasic("wrong").ToString()), HttpStatusCode.Unauthorized, "invalid_client");
        await Denied(Export(path, command, null), HttpStatusCode.Unauthorized, "invalid_client");
        await Denied(Export(path, command, basic, trustedProxy: false), HttpStatusCode.BadRequest, "invalid_request");
        await Denied(Export(path, command with { FormId = Guid.NewGuid() }, basic), HttpStatusCode.Forbidden, "insufficient_scope");
        await Denied(Export(path + "/child", command, basic), HttpStatusCode.Forbidden, "E-SEC-010", "message");

        var cookies = await Login(host, h);
        var browserOnly = Request(HttpMethod.Post, path, cookies, command);
        browserOnly.Headers.Add("X-Forwarded-Proto", "https");
        await Denied(browserOnly, HttpStatusCode.Unauthorized, "invalid_client");
        await Denied(Request(HttpMethod.Post, "/api/auth/completion/close", cookies, new { }, csrf: false),
            HttpStatusCode.Forbidden, "E-SEC-010", "message");

        static HttpRequestMessage Export(string url, InquiryServiceContextRequestV1 body, string? authorization, bool trustedProxy = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            if (authorization is not null) request.Headers.Add("Authorization", authorization);
            if (trustedProxy) request.Headers.Add("X-Forwarded-Proto", "https");
            return request;
        }

        async Task Denied(HttpRequestMessage request, HttpStatusCode status, string reason, string property = "error")
        {
            using (request)
            using (var response = await host.Send(request))
            {
                var payload = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == status, payload);
                Assert.False(response.Headers.Contains(fence));
                using var parsed = JsonDocument.Parse(payload);
                Assert.Equal(reason, parsed.RootElement.GetProperty(property).GetString());
            }
        }
    }
}
