using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Persistence.PostgreSql.Tenancy;
using CP6.Platform.Contracts.Context;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using CP6.WebApi.Services.ServiceRevocationTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("ServiceRevocationPostgres", DisableParallelization = true)]
public sealed class ServiceRevocationPostgresCollection : ICollectionFixture<ServiceRevocationPostgresFixture>;

public sealed class ServiceRevocationPostgresFixture : IAsyncLifetime
{
    internal const string Database = "service_revocation";
    internal static readonly Guid A = TenantStorageContext.A, B = TenantStorageContext.B;
    internal readonly TenantStoragePostgresFixture Server = new();
    internal readonly string IssuerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal readonly string ServiceSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal readonly string BrowserSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal ServiceRevocationInstallRequest Installation = null!;
    internal string Baseline = "";
    public async Task InitializeAsync()
    {
        await Server.InitializeAsync();
        if (!TenantStoragePostgresFactAttribute.Selected) return;
        try
        {
            await using (var root = await Server.OpenOwnerAsync("postgres"))
            {
                await TenantStoragePostgresFixture.Sql(root, $"CREATE ROLE service_issuer LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{IssuerPassword}'");
                await TenantStoragePostgresFixture.Sql(root, $"CREATE DATABASE {Database}");
            }
            await using var control = await Owner();
            var oid = (uint)(await TenantStoragePostgresFixture.Scalar(control, "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
            var system = (string)(await TenantStoragePostgresFixture.Scalar(control, "SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!;
            Installation = new(Guid.NewGuid(), "127.0.0.1", TenantStoragePostgresFixture.Port, Database, oid, system,
                "service_issuer", [new(A, "tenant_a"), new(B, "tenant_b")]);
            Baseline = ServiceRevocationBaselineV1.GenerateSql();
            await ServiceRevocationInstallation.InstallAsync(control, Installation, Baseline);
            Assert.Equal(await ServiceRevocationInstallation.InstallAsync(control, Installation, Baseline),
                await ServiceRevocationInstallation.InstallAsync(control, Installation, Baseline));
            Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(control,
                "SELECT count(*) FROM pg_catalog.pg_class WHERE relname LIKE '__EFMigrationsHistory%'"));
        }
        catch { await Server.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => Server.DisposeAsync();
    internal Task<NpgsqlConnection> Owner() => Server.OpenOwnerAsync(Database);
    internal string IssueConnection => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = TenantStoragePostgresFixture.Port, Database = Database,
        Username = "service_issuer", Password = IssuerPassword, SearchPath = "public", SslMode = SslMode.Disable,
        Pooling = false, Enlist = false, Options = " ", IncludeErrorDetail = false
    }.ConnectionString;
    internal CrmOidcOptions Options(bool disabledA = false)
    {
        using var rsa = RSA.Create(2048);
        return new()
        {
            Enabled = true, Issuer = "https://127.0.0.1", ActiveKeyId = "fixture",
            Keys = [new() { Kid = "fixture", Pem = rsa.ExportRSAPrivateKeyPem() }],
            Clients = [new() { ClientId = "CP6.Web", SecretSha256 = CrmOidcCrypto.Hash(BrowserSecret), RedirectUris = ["https://127.0.0.1/callback"] }],
            ServiceClients = new[] { ("client-a", A), ("client-b", B), ("other-a", A) }.Select(t => new CrmOidcServiceClient
            { ClientId = t.Item1, TenantId = t.Item2, SecretSha256 = CrmOidcCrypto.Hash(ServiceSecret),
                Enabled = t.Item1 != "client-a" || !disabledA, AllowedScopes = ["cp6.services"] }).ToList(),
            Organizations = [new() { TenantId = A, Slug = "a", Region = "us", CrmEnabled = true }, new() { TenantId = B, Slug = "b", Region = "eu", CrmEnabled = true }]
        };
    }
    internal Dictionary<string, string?> Configuration(DateTimeOffset now)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql", ["ConnectionStrings:DefaultConnection"] = IssueConnection,
            ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + A] = "us", ["CrmIdentity:Tenants:" + B] = "eu",
            ["CrmIdentity:ProjectionReaderClientIds:0"] = "client-b", ["Security:Password:ExpiryDays"] = "0"
        };
        const string p = "CrmIdentity:ServiceRevocationTenancy:";
        foreach (var item in new Dictionary<string, string?>
        {
            ["Enabled"] = "true", ["HostId"] = "revocation-http", ["Revision"] = "r1",
            ["ValidFromUtc"] = now.AddMinutes(-1).ToString("O"), ["ValidUntilUtc"] = now.AddHours(1).ToString("O"),
            ["Server"] = "127.0.0.1", ["Port"] = TenantStoragePostgresFixture.Port.ToString(), ["Database"] = Database,
            ["DatabaseOid"] = Installation.DatabaseOid.ToString(), ["SystemIdentifier"] = Installation.SystemIdentifier,
            ["InstallationId"] = Installation.InstallationId.ToString("D"), ["SslMode"] = "Disable",
            ["Tenants:0:TenantId"] = A.ToString("D"), ["Tenants:0:Login"] = "tenant_a", ["Tenants:0:Password"] = Server.PasswordA,
            ["Tenants:0:CredentialVersion"] = "a1", ["Tenants:1:TenantId"] = B.ToString("D"), ["Tenants:1:Login"] = "tenant_b",
            ["Tenants:1:Password"] = Server.PasswordB, ["Tenants:1:CredentialVersion"] = "b1",
            ["Clients:0:ClientId"] = "client-a", ["Clients:0:RegistrationVersion"] = "a1", ["Clients:0:AllowSelfRevoke"] = "true",
            ["Clients:1:ClientId"] = "client-b", ["Clients:1:RegistrationVersion"] = "b1", ["Clients:1:AllowSelfRevoke"] = "true",
            ["Clients:2:ClientId"] = "other-a", ["Clients:2:RegistrationVersion"] = "a2", ["Clients:2:AllowSelfRevoke"] = "true"
        }) values[p + item.Key] = item.Value;
        return values;
    }
    internal async Task<RevocationHttpHost> Start(bool disabledA = false, Action<Dictionary<string, string?>>? configure = null)
    {
        var clock = new RevocationClock();
        var values = Configuration(clock.GetUtcNow()); configure?.Invoke(values);
        return await RevocationHttpHost.Start(this, Options(disabledA), values, clock);
    }
}

internal sealed class RevocationClock : TimeProvider
{
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    internal void Advance(TimeSpan interval) => Interlocked.Add(ref _ticks, interval.Ticks);
}

internal sealed class RevocationHttpHost(WebApplication app, HttpClient client, CrmOidcOptions options,
    RevocationClock clock, ServiceRevocationPostgresFixture fixture, X509Certificate2 certificate) : IAsyncDisposable
{
    internal HttpClient Client => client;
    internal CrmOidcOptions Options => options;
    internal RevocationClock Clock => clock;
    internal ServiceRevocationTenantHost Tenant => app.Services.GetRequiredService<ServiceRevocationTenantHost>();
    internal CrmIdentityRuntime Runtime => app.Services.GetRequiredService<CrmIdentityRuntime>();
    internal static async Task<RevocationHttpHost> Start(ServiceRevocationPostgresFixture fixture, CrmOidcOptions options,
        Dictionary<string, string?> configuration, RevocationClock clock)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificate = new X509Certificate2(created.Export(X509ContentType.Pfx));
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<CrmOidcCrypto>();
        builder.Services.AddScoped<CrmOidcServiceTokens>();
        builder.Services.AddSingleton<ICrmOidcServiceDirectory>(new FixtureActiveTenants());
        builder.Services.AddScoped<CrmOidcDirectory>();
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddSingleton(Mock.Of<ITokenBlacklistService>());
        builder.Services.AddSingleton(Mock.Of<IPasswordPolicyService>());
        builder.Services.AddSingleton(Mock.Of<IAuthCookieWriter>());
        builder.Services.AddSingleton(Mock.Of<ICrmOidcGrantStore>());
        builder.Services.AddDbContext<CP6Context>(b => DatabaseContextOptions.Configure(b,
            new(DatabaseProvider.PostgreSql), fixture.IssueConnection));
        builder.Services.AddCrmIdentityEvents(builder.Configuration, options, new(DatabaseProvider.PostgreSql));
        // This host exercises real controllers and production registration, excluding unrelated bootstrap/dispatch.
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.Thumbprint == certificate.Thumbprint };
        return new(app, new HttpClient(handler) { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(30) }, options, clock, fixture, certificate);
    }
    internal async Task<string> Issue(string id = "client-a")
    {
        using var request = Request("/connect/token", id, new() { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new JwtSecurityTokenHandler().ReadJwtToken(json.RootElement.GetProperty("access_token").GetString()).Id;
    }
    internal Task<HttpResponseMessage> Revoke(string jti, string id = "client-a", string? secret = null,
        Dictionary<string, string>? extra = null)
    {
        var form = new Dictionary<string, string> { ["jti"] = jti };
        if (extra is not null) foreach (var pair in extra) form[pair.Key] = pair.Value;
        return client.SendAsync(Request("/connect/service-revocations", id, form, secret));
    }
    internal HttpRequestMessage Request(string path, string id, Dictionary<string, string> form, string? secret = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(id + ":" + (secret ?? fixture.ServiceSecret))));
        return request;
    }
    public async ValueTask DisposeAsync() { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); certificate.Dispose(); }
    // Tenant-active issuance lookup is fixture support only; actual token signing and RecordAsync use production code.
    private sealed class FixtureActiveTenants : ICrmOidcServiceDirectory
    {
        public Task<bool> IsServiceTenantActiveAsync(Guid tenant, DateTime now, CancellationToken ct = default) =>
            Task.FromResult(tenant == ServiceRevocationPostgresFixture.A || tenant == ServiceRevocationPostgresFixture.B);
    }
}

[Collection("ServiceRevocationPostgres")]
public sealed class ServiceRevocationPostgresTests(ServiceRevocationPostgresFixture postgres)
{
    [TenantStoragePostgresFact]
    public async Task Replay_rejects_changed_directory_rows_and_installation_state()
    {
        await using var control = await postgres.Owner();
        foreach (var (change, restore) in new[]
        {
            ("UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'",
                $"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{ServiceRevocationPostgresFixture.A}' WHERE role_name='tenant_a'"),
            ("UPDATE cp6_tenancy_v1.login_tenant SET role_name='unknown_login' WHERE role_name='tenant_a'",
                "UPDATE cp6_tenancy_v1.login_tenant SET role_name='tenant_a' WHERE role_name='unknown_login'"),
            ("UPDATE cp6_tenancy_v1.login_tenant SET role_oid=1 WHERE role_name='tenant_a'",
                "UPDATE cp6_tenancy_v1.login_tenant SET role_oid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname='tenant_a') WHERE role_name='tenant_a'"),
            ("UPDATE cp6_service_revocation_v1.installation SET profile='unsupported'",
                "UPDATE cp6_service_revocation_v1.installation SET profile='service-revocation-v1'"),
            ("UPDATE cp6_service_revocation_v1.installation SET database_oid=1",
                "UPDATE cp6_service_revocation_v1.installation SET database_oid=(SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database())"),
            ("UPDATE cp6_service_revocation_v1.installation SET system_identifier='1'",
                "UPDATE cp6_service_revocation_v1.installation SET system_identifier=(SELECT system_identifier::text FROM pg_catalog.pg_control_system())")
        })
        {
            await TenantStoragePostgresFixture.Sql(control, change);
            try
            {
                // OID replacement also changes the seal's role set; name/tenant edits require the row check.
                if (change.StartsWith("UPDATE cp6_tenancy_v1", StringComparison.Ordinal) && !change.Contains("SET role_oid", StringComparison.Ordinal))
                {
                    var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline));
                    Assert.Equal("Known tenant directory v1 validation rejected.", rejected.Message);
                }
                else await Assert.ThrowsAsync<TenantStorageException>(() => ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline));
            }
            finally { await TenantStoragePostgresFixture.Sql(control, restore); }
        }
        Assert.Equal(postgres.Installation.InstallationId,
            (await ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline)).InstallationId);
    }

    [TenantStoragePostgresFact]
    public async Task Replay_rejects_issuance_role_privilege_membership_and_identity_drift()
    {
        await using var control = await postgres.Owner();
        foreach (var (change, restore) in new[]
        {
            ("ALTER ROLE service_issuer SUPERUSER", "ALTER ROLE service_issuer NOSUPERUSER"),
            ("GRANT pg_read_all_data TO service_issuer", "REVOKE pg_read_all_data FROM service_issuer"),
            ("GRANT SELECT ON pg_catalog.pg_authid TO service_issuer", "REVOKE SELECT ON pg_catalog.pg_authid FROM service_issuer"),
            ("GRANT SELECT ON crm_identity.\"ServiceToken\" TO service_issuer WITH GRANT OPTION", "REVOKE GRANT OPTION FOR SELECT ON crm_identity.\"ServiceToken\" FROM service_issuer"),
            ("ALTER ROLE service_issuer SET search_path=public", "ALTER ROLE service_issuer RESET search_path"),
            ("ALTER ROLE service_issuer NOLOGIN", "ALTER ROLE service_issuer LOGIN"),
            ("UPDATE cp6_service_revocation_v1.installation SET issuer_oid=1",
                "UPDATE cp6_service_revocation_v1.installation SET issuer_oid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname='service_issuer')"),
            ("UPDATE cp6_service_revocation_v1.installation SET issuer_name='unknown_issuer'",
                "UPDATE cp6_service_revocation_v1.installation SET issuer_name='service_issuer'"),
            ("ALTER ROLE service_issuer RENAME TO changed_issuer", "ALTER ROLE changed_issuer RENAME TO service_issuer")
        })
        {
            await TenantStoragePostgresFixture.Sql(control, change);
            try { await Assert.ThrowsAsync<TenantStorageException>(() => ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline)); }
            finally { await TenantStoragePostgresFixture.Sql(control, restore); }
        }
        Assert.Equal(postgres.Installation.InstallationId,
            (await ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline)).InstallationId);
    }

    [TenantStoragePostgresFact]
    public async Task Production_verifier_reauthenticates_and_authority_rejects_forgery_and_previous_host_source()
    {
        await using var host = await postgres.Start();
        var input = new ServiceRevocationTenantHost.SourceInput(
            ["Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-a:" + postgres.ServiceSecret))], "client-a", ServiceRevocationPostgresFixture.A);
        var verifier = new ServiceRevocationTenantHost.SourceVerifier(host.Tenant);
        var authority = new ServiceRevocationTenantHost.BindingAuthority(host.Tenant);
        var verified = (await verifier.VerifyAsync(input))!;
        Assert.Equal(ContextActorKindV1.Service, verified.Source.Actor.Kind); Assert.Null(verified.Source.Actor.UserId);
        Assert.NotNull(await authority.ResolveCurrentAsync(verified.Operation, verified.Source));
        Assert.Null(await verifier.VerifyAsync(input with { Tenant = ServiceRevocationPostgresFixture.B }));
        Assert.Null(await verifier.VerifyAsync(input with { Authorization = ["Basic Zm9yZ2VkOnByb29m"] }));
        Assert.Null(await authority.ResolveCurrentAsync(verified.Operation, verified.Source with { SourceIdentityVersion = "forged" }));
        Assert.Null(await authority.ResolveCurrentAsync(verified.Operation, verified.Source with
        { Actor = verified.Source.Actor with { Kind = ContextActorKindV1.Human, UserId = Guid.NewGuid() } }));
        Assert.Null(await authority.ResolveCurrentAsync(verified.Operation with { Purpose = "write-anything" }, verified.Source));
        await using var replacement = await postgres.Start();
        Assert.Null(await new ServiceRevocationTenantHost.BindingAuthority(replacement.Tenant).ResolveCurrentAsync(verified.Operation, verified.Source));
        Assert.Equal(0, host.Tenant.PoolSnapshot.Pools); Assert.Equal(0, replacement.Tenant.PoolSnapshot.Pools);
    }

    [TenantStoragePostgresFact]
    public async Task Http_real_issuance_revoke_repeat_concurrency_and_cross_client_isolation()
    {
        await using var host = await postgres.Start();
        foreach (var client in new[] { "client-a", "client-b" })
        {
            var jti = await host.Issue(client);
            Assert.Equal((0L, 0L, 0L), await State(jti));
            Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti, client == "client-a" ? "client-b" : "client-a")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti, "other-a")).StatusCode);
            Assert.Equal((0L, 0L, 0L), await State(jti));
            var requests = await Task.WhenAll(host.Revoke(jti, client), host.Revoke(jti, client));
            Assert.All(requests, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.Equal((1L, 1L, 1L), await State(jti));
            var persisted = await Envelope(jti);
            Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti, client)).StatusCode);
            Assert.Equal(persisted, await Envelope(jti));
        }
        Assert.Equal(HttpStatusCode.OK, (await host.Revoke(Guid.NewGuid().ToString("D"))).StatusCode);
        Assert.Equal(2, host.Tenant.PoolSnapshot.Pools);
        Assert.Equal(0, host.Tenant.PoolSnapshot.Borrowers);
    }

    [TenantStoragePostgresFact]
    public async Task Http_invalid_auth_never_opens_runtime_database_and_disabled_client_can_self_revoke()
    {
        string issued;
        await using (var issuer = await postgres.Start()) issued = await issuer.Issue();
        await using var host = await postgres.Start(disabledA: true);
        foreach (var (id, secret) in new[] { ("client-a", "wrong"), ("CP6.Web", postgres.BrowserSecret), ("unknown", postgres.ServiceSecret) })
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Revoke(issued, id, secret)).StatusCode);
        Assert.Equal(0, host.Tenant.PoolSnapshot.Pools);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Revoke(issued, extra: new() { ["tenant_id"] = ServiceRevocationPostgresFixture.B.ToString() })).StatusCode);
        Assert.Equal(0, host.Tenant.PoolSnapshot.Pools);
        Assert.Equal(HttpStatusCode.OK, (await host.Revoke(issued)).StatusCode);
        Assert.Equal((1L, 1L, 1L), await State(issued));
    }

    [TenantStoragePostgresFact]
    public async Task Before_commit_failure_rolls_back_all_writes_and_lost_response_reconciles_original_event()
    {
        await using var host = await postgres.Start();
        var jti = await host.Issue();
        var generation = await Generation(ServiceRevocationPostgresFixture.A);
        host.Tenant.Checkpoint = point => point == ServiceRevocationCheckpoint.AfterWriteBeforeCommit
            ? Task.FromException(new InvalidOperationException("injected-before-commit")) : Task.CompletedTask;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(jti)).StatusCode);
        Assert.Equal((0L, 0L, 0L), await State(jti));
        Assert.Equal(generation, await Generation(ServiceRevocationPostgresFixture.A));
        host.Tenant.Checkpoint = point => point == ServiceRevocationCheckpoint.AfterCommit
            ? Task.FromException(new InvalidOperationException("injected-lost-response")) : Task.CompletedTask;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(jti)).StatusCode);
        Assert.Equal((1L, 1L, 1L), await State(jti));
        Assert.Equal(generation + 1, await Generation(ServiceRevocationPostgresFixture.A));
        var persisted = await Envelope(jti);
        host.Tenant.Checkpoint = null;
        Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti)).StatusCode);
        Assert.Equal(persisted, await Envelope(jti));
        Assert.Equal(generation + 1, await Generation(ServiceRevocationPostgresFixture.A));
    }

    [TenantStoragePostgresFact]
    public async Task Final_authority_expiry_fence_rolls_back_and_legacy_runtime_mutation_cannot_redirect_writer()
    {
        await using var host = await postgres.Start();
        var jti = await host.Issue();
        host.Tenant.Checkpoint = point =>
        {
            if (point == ServiceRevocationCheckpoint.AfterWriteBeforeCommit) host.Clock.Advance(TimeSpan.FromMinutes(2));
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(jti)).StatusCode);
        Assert.Equal((0L, 0L, 0L), await State(jti));
        host.Tenant.Checkpoint = null;
        host.Runtime.Options.Issuer = "https://forged.example";
        host.Runtime.Options.Tenants[ServiceRevocationPostgresFixture.A] = "forged";
        Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti)).StatusCode);
        var persisted = await Envelope(jti);
        Assert.Contains("https://127.0.0.1", persisted);
        Assert.DoesNotContain("forged", persisted);
    }

    [TenantStoragePostgresFact]
    public async Task Runtime_has_only_limited_table_rights_and_cannot_create_or_edit_directory()
    {
        foreach (var (role, password, tenant) in new[] { ("tenant_a", postgres.Server.PasswordA, ServiceRevocationPostgresFixture.A),
                     ("tenant_b", postgres.Server.PasswordB, ServiceRevocationPostgresFixture.B) })
        {
            await using var connection = await TenantStoragePostgresFixture.OpenRawAsync(role, password, ServiceRevocationPostgresFixture.Database);
            Assert.Equal(tenant, await TenantStoragePostgresFixture.Scalar(connection, "SELECT cp6_tenancy_v1.current_tenant_id()"));
            foreach (var sql in new[] { "SELECT * FROM cp6_tenancy_v1.login_tenant", "DELETE FROM crm_identity.\"ServiceToken\"",
                         "UPDATE crm_identity.\"ServiceToken\" SET \"ClientId\"='forged'", "CREATE TABLE public.forbidden(id int)",
                         "CREATE TEMP TABLE forbidden(id int)", "CREATE SCHEMA forbidden", "SET ROLE cp6_tenant_lookup_v1",
                         "UPDATE crm_identity_priority.\"Cp6_OutboxMessage\" SET \"Status\"=1" })
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => TenantStoragePostgresFixture.Sql(connection, sql))).SqlState);
            Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(connection,
                $"SELECT count(*) FROM crm_identity.\"ServiceToken\" WHERE \"TenantId\"<>'{tenant}'"));
        }
    }

    [TenantStoragePostgresFact]
    public async Task Mapping_is_pinned_through_commit_then_drift_fails_closed()
    {
        await using var host = await postgres.Start();
        var jti = await host.Issue();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Tenant.Checkpoint = async point => { if (point == ServiceRevocationCheckpoint.AfterWriteBeforeCommit) { reached.SetResult(); await resume.Task; } };
        var revoke = host.Revoke(jti); await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var control = await postgres.Owner();
        var drift = TenantStoragePostgresFixture.Sql(control,
            "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'");
        try
        {
            await Task.Delay(150); Assert.False(drift.IsCompleted);
            resume.SetResult(); Assert.Equal(HttpStatusCode.OK, (await revoke).StatusCode); await drift;
            host.Tenant.Checkpoint = null;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(Guid.NewGuid().ToString("D"))).StatusCode);
            Assert.Equal((1L, 1L, 1L), await State(jti));
        }
        finally
        {
            resume.TrySetResult(); await drift;
            await TenantStoragePostgresFixture.Sql(control,
                $"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{ServiceRevocationPostgresFixture.A}' WHERE role_name='tenant_a'");
        }
        Assert.Equal(HttpStatusCode.OK, (await host.Revoke(jti)).StatusCode);
    }

    [TenantStoragePostgresFact]
    public async Task Wrong_physical_identity_unknown_layout_and_missing_permission_do_not_fallback()
    {
        await using (var host = await postgres.Start(configure: c => c["CrmIdentity:ServiceRevocationTenancy:InstallationId"] = Guid.NewGuid().ToString("D")))
        {
            var jti = await host.Issue();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(jti)).StatusCode);
            Assert.Equal((0L, 0L, 0L), await State(jti));
        }
        await using (var host = await postgres.Start(configure: c => c["CrmIdentity:ServiceRevocationTenancy:Clients:0:AllowSelfRevoke"] = "false"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(Guid.NewGuid().ToString("D"))).StatusCode);
            Assert.Equal(0, host.Tenant.PoolSnapshot.Pools);
        }
        await using var control = await postgres.Owner();
        await TenantStoragePostgresFixture.Sql(control, "CREATE TABLE public.unexpected(id integer)");
        try
        {
            await Assert.ThrowsAsync<TenantStorageException>(() => ServiceRevocationInstallation.InstallAsync(control, postgres.Installation, postgres.Baseline));
            await using var host = await postgres.Start();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Revoke(Guid.NewGuid().ToString("D"))).StatusCode);
        }
        finally { await TenantStoragePostgresFixture.Sql(control, "DROP TABLE public.unexpected"); }
    }

    private async Task<(long Revoked, long Snapshot, long Outbox)> State(string jti)
    {
        await using var connection = await postgres.Owner();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM crm_identity."ServiceToken" WHERE "Jti"=CAST(@jti AS bpchar) AND "RevokedAtUtc" IS NOT NULL),
              (SELECT count(*) FROM crm_identity."Snapshot" WHERE "PayloadJson"::text LIKE '%' || @jti || '%' AND "Version"=1),
              (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE convert_from("Payload",'UTF8') LIKE '%' || @jti || '%')
            """, connection);
        command.Parameters.AddWithValue("jti", jti);
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }
    private async Task<long> Generation(Guid tenant)
    {
        await using var connection = await postgres.Owner();
        return Convert.ToInt64(await TenantStoragePostgresFixture.Scalar(connection,
            $"SELECT coalesce((SELECT \"Generation\" FROM public.\"CrmIdentityTenantGenerations\" WHERE \"TenantId\"='{tenant}'),0)"));
    }
    private async Task<string> Envelope(string jti)
    {
        await using var connection = await postgres.Owner();
        return (string)(await TenantStoragePostgresFixture.Scalar(connection,
            $"SELECT row_to_json(o)::text || convert_from(\"Payload\",'UTF8') FROM crm_identity_priority.\"Cp6_OutboxMessage\" o WHERE convert_from(\"Payload\",'UTF8') LIKE '%{jti}%'"))!;
    }
}
