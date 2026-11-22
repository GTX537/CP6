using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ContactExportRoute = "/internal/main/v1/contact-history-exports";
    private const string ExportIssuer = "https://iam.contact-history-fixture.invalid";
    private const string ExportClient = "owned-contact-history-service";
    private const string ExportAccess = "owned-g05-permission/1";
    private const string ExportCut = "2026-09-29T08:30:00.000Z";
    private static readonly string[] ExportFields = ["contact.displayName", "contact.lifecycle", "relationships.periods", "activities.summary"];
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web);

    [CurrentPermissionPostgresFact]
    public async Task Contact_history_G05_dataset_requires_authenticated_TLS_service()
    {
        await using var h = await Seed();
        await using var setup = await ExportServiceFixture.Create(h);
        await using var app = await setup.Start();
        Assert.Equal("https", app.Origin.Scheme);
        using var request = setup.Http("/dataset", bearer: false);
        using var response = await app.Send(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var dataset = await setup.Json(app, "/dataset");
        Assert.Equal("CONTACT_HISTORY", dataset.GetProperty("dataset").GetString());
        Assert.Equal("CONTACT-HISTORY-SCHEMA/1", dataset.GetProperty("schemaVersion").GetString());
        Assert.Equal("CSV_VIEW", dataset.GetProperty("format").GetString());
        Assert.Equal(2097152, dataset.GetProperty("maximumArtifactBytes").GetInt32());
        Assert.Equal(ExportFields, dataset.GetProperty("fieldIds").EnumerateArray().Select(x => x.GetString()));
    }

    private sealed class ExportServiceFixture(Harness harness, X509Certificate2 certificate, Dictionary<string, string> settings,
        Dictionary<Guid, string> tokens) : IAsyncDisposable
    {
        internal Guid Tenant => harness.Tenant;

        internal static async Task<ExportServiceFixture> Create(Harness h, int windowSeconds = 4, Harness? other = null)
        {
            using var rsa = RSA.Create(2048);
            using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var certificate = ExportCertificate(rsa);
            var environment = ExportTlsEnvironment(certificate);
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["CrmOidc__Enabled"] = "true", ["CrmOidc__Issuer"] = ExportIssuer, ["CrmOidc__ActiveKeyId"] = "owned-service",
                ["CrmOidc__Keys__0__Kid"] = "owned-service", ["CrmOidc__Keys__0__Pem"] = rsa.ExportRSAPrivateKeyPem(),
                ["CrmOidc__Clients__0__ClientId"] = "CP6.Web", ["CrmOidc__Clients__0__SecretSha256"] = new string('a', 64),
                ["CrmOidc__Clients__0__RedirectUris__0"] = "https://owned-crm.invalid/auth/callback",
                ["ContactHistoryExport__Enabled"] = "true",
                ["ContactHistoryExport__Epoch"] = "owned-g05-epoch-1", ["ContactHistoryExport__KeyId"] = "owned-g05-signing-key",
                ["ContactHistoryExport__PrivateKeyPem"] = signing.ExportPkcs8PrivateKeyPem(),
                ["ContactHistoryExport__DownloadWindowSeconds"] = windowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }) environment[key] = value;
            var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
            using var issuer = new CrmOidcCrypto(new() { Enabled = true, Issuer = ExportIssuer, ActiveKeyId = "owned-service",
                Keys = [new() { Kid = "owned-service", Pem = rsa.ExportRSAPrivateKeyPem() }] });
            var tokens = new Dictionary<Guid, string>();
            await Register(h, 0, ExportClient);
            if (other is not null) await Register(other, 1, ExportClient + "-other");
            return new(h, certificate, environment, tokens);

            async Task Register(Harness scope, int index, string client)
            {
                var tenantId = scope.Tenant.ToString("D");
                var organization = $"CrmOidc__Organizations__{index}__";
                environment[organization + "TenantId"] = tenantId;
                environment[organization + "Slug"] = $"fixture-export-{index}";
                environment[organization + "CrmEnabled"] = "true";
                environment[organization + "Region"] = "us";
                var service = $"CrmOidc__ServiceClients__{index}__";
                environment[service + "TenantId"] = tenantId;
                environment[service + "ClientId"] = client;
                environment[service + "Enabled"] = "true";
                environment[service + "SecretSha256"] = new string('b', 64);
                environment[service + "AllowedScopes__0"] = "cp6.services";
                var registration = $"ContactHistoryExport__Clients__{index}__";
                environment[registration + "TenantId"] = tenantId;
                environment[registration + "ClientId"] = client;
                environment[registration + "CanReconcile"] = index == 0 ? "true" : "false";
                var jti = Guid.NewGuid().ToString("D");
                scope.Db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord { TenantId = scope.Tenant, Issuer = ExportIssuer,
                    ClientId = client, Jti = jti, ExpiresAtUtc = expiry });
                await scope.Db.SaveChangesAsync();
                scope.Db.ChangeTracker.Clear();
                tokens.Add(scope.Tenant, issuer.Sign("CP6.Services", [new Claim("sub", "service:" + client), new("client_id", client),
                    new("tenant_id", tenantId), new("jti", jti), new("scope", "cp6.services")], expiry, "at+jwt"));
            }
        }

        internal Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(harness.Db.Database.GetConnectionString()!, settings,
            allowAutoRedirect: false, serverCertificateThumbprint: certificate.Thumbprint);

        internal HttpRequestMessage Http(string path, object? body = null, bool bearer = true, Guid? tenant = null, Guid? credential = null)
        {
            var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, ContactExportRoute + path);
            request.Headers.Add("tenantid", (tenant ?? Tenant).ToString("D"));
            request.Headers.Add("correlationid", "owned-g05-fixture");
            if (bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[credential ?? Tenant]);
            if (body is not null) request.Content = JsonContent.Create(body, options: ExportJson);
            return request;
        }

        internal async Task<JsonElement> Json(AuthenticationApiProcess app, string path, object? body = null, int expected = 200,
            Guid? tenant = null, Guid? credential = null)
        {
            using var request = Http(path, body, tenant: tenant, credential: credential);
            using var response = await app.Send(request);
            var raw = await response.Content.ReadAsByteArrayAsync();
            Assert.True((int)response.StatusCode == expected, $"{(int)response.StatusCode} {Encoding.UTF8.GetString(raw)}");
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var parsed = JsonDocument.Parse(raw);
            return parsed.RootElement.Clone();
        }

        public ValueTask DisposeAsync() { certificate.Dispose(); return ValueTask.CompletedTask; }
    }

    private static X509Certificate2 ExportCertificate(RSA rsa)
    {
        var request = new CertificateRequest("CN=CP6-WP02-31-owned", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(2));
    }

    private static Dictionary<string, string> ExportTlsEnvironment(X509Certificate2 certificate)
    {
        var directory = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!;
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pfx");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return new()
        {
            ["ASPNETCORE_URLS"] = "https://127.0.0.1:0",
            ["Kestrel__Certificates__Default__Path"] = path,
            ["Kestrel__Certificates__Default__Password"] = password
        };
    }
}
