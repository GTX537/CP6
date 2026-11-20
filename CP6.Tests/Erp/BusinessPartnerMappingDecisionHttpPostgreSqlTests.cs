using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.AspNetCore;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class MappingServiceDirectory(Guid tenant) : ICrmOidcServiceDirectory
    {
        public Task<bool> IsServiceTenantActiveAsync(Guid tenantId, DateTime utcNow, CancellationToken cancellationToken = default)
            => Task.FromResult(tenantId == tenant);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_I03_and_ACC_mapping_decision_prove_distinct_wire_and_exact_authority()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00003001");
        var authority = bridge.Binding("ACC_MAPPING_AUTHORITY");
        await using (var initialization = await h.Db.Database.BeginTransactionAsync())
        {
            await BpUseScopeGate.LockAsync(h.Db, MappingLegal, true, default);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_authorities SET enabled=false WHERE tenant_id={h.Tenant}
                 AND legal_entity_key={MappingLegal} AND purpose={authority.Purpose};
                INSERT INTO main_bp.mapping_readers VALUES ({h.Tenant},{MappingLegal},'fixture-acc-reader','SNAPSHOT',true);
                INSERT INTO main_bp.mapping_readers VALUES ({h.Tenant},{MappingLegal},'fixture-acc-reader','OWNER_DECISION',true);
                """);
            await initialization.CommitAsync();
        }

        await using var app = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(app, h);
        var first = await AdoptMappingPartner(app, cookies, "000030A", "模拟快照伙伴主");
        var second = await AdoptMappingPartner(app, cookies, "000030B", "模拟决定伙伴后继");
        var observation = await SignedAccountObservation(app, cookies, account);
        var context = await ReadMappingContext(app, cookies, account, observation, MappingCriteria("000030A", "模拟快照伙伴主"));
        await ApplyMap(app, cookies, await GoodMapPreview(app, cookies, MappingInput(context, "LINK", first, "模拟旧协议第一版")), Guid.NewGuid());

        const string issuer = "https://mapping-owner-fixture.example";
        const string client = "fixture-acc-reader";
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "mapping-reader-test" };
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = issuer, ActiveKeyId = signingKey.KeyId, Keys = [new() { Kid = signingKey.KeyId, Pem = rsa.ExportRSAPrivateKeyPem() }],
            ServiceClients = [new() { ClientId = client, TenantId = h.Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }]
        };
        using var signer = new CrmOidcCrypto(oidc);
        using var dp = MappingProtection(h);
        using var outbound = new HttpClient();
        var adapter = bridge.Adapter(outbound);
        var runtime = new ErpIntegrationRuntime(new ErpIntegrationOptions { Enabled = true, Tenants = new() { [h.Tenant] = "local" },
            ReaderClientIds = [client], DaprApiToken = new string('x', 32), DaprAppToken = new string('y', 32) },
            new ErpEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp"))));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
        builder.WebHost.ConfigureKestrel(x => x.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<ICrmOidcServiceDirectory>(new MappingServiceDirectory(h.Tenant));
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext());
        builder.Services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<ErpReadService>();
        builder.Services.AddScoped(sp => MappingService(h, sp.GetRequiredService<CP6.Core.EFDbContext.CP6Context>(),
            dp.GetRequiredService<IDataProtectionProvider>(), adapter));
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile { Authority = issuer, Issuer = issuer, Audiences = ["CP6.Services"],
            ClockSkew = TimeSpan.Zero }, ErpReadController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(ErpReadController.Scheme, o =>
        {
            var config = new OpenIdConnectConfiguration { Issuer = issuer };
            config.SigningKeys.Add(signingKey);
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
            o.MapInboundClaims = false;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(ErpReadController).Assembly);
        await using var serverApp = builder.Build();
        serverApp.UseAuthentication();
        serverApp.UseAuthorization();
        serverApp.MapControllers();
        await serverApp.StartAsync();
        using var service = new HttpClient { BaseAddress = new Uri(serverApp.Urls.Single()) };

        var jti = Guid.NewGuid().ToString("D");
        var expiry = DateTimeOffset.UtcNow.AddMinutes(8);
        h.Db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord { TenantId = h.Tenant, Issuer = issuer,
            ClientId = client, Jti = jti, ExpiresAtUtc = expiry });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var jwt = signer.Sign("CP6.Services", [new("sub", "service:" + client), new("client_id", client),
            new("tenant_id", h.Tenant.ToString("D")), new("scope", "cp6.services"), new("jti", jti)], expiry, "at+jwt");
        const string snapshotRoute = "/internal/main/v1/bp/mapping-snapshots";
        const string decisionRoute = "/internal/main/v2/account-mapping-decisions";
        async Task<byte[]> Read(string path, object? body, int expected = 200, bool bearer = true)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: MappingJson);
            if (bearer) request.Headers.Authorization = new("Bearer", jwt);
            request.Headers.Add(ErpReadController.TenantHeader, h.Tenant.ToString("D"));
            request.Headers.Add(ErpReadController.CorrelationHeader, "mapping-reader-fixture");
            using var response = await service.SendAsync(request);
            var original = await response.Content.ReadAsByteArrayAsync();
            Assert.True((int)response.StatusCode == expected, response.StatusCode + " " + Encoding.UTF8.GetString(original));
            return original;
        }

        JsonElement Payload(byte[] bytes) => JsonSerializer.Deserialize<JsonElement>(bytes);
        object QuerySnapshot(Guid id) => new { legalEntityKey = MappingLegal, accountId = id, knownRevision = (string?)null };
        await Read(snapshotRoute, QuerySnapshot(account), 401, false);
        var empty = Payload(await Read(snapshotRoute, QuerySnapshot(Guid.NewGuid())));
        Assert.Equal("UNSEEN", empty.GetProperty("knowledge").GetString());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("current").ValueKind);
        Assert.Empty(empty.GetProperty("originalEvents").EnumerateArray());
        var firstEvent = Assert.Single(await StoredEvents(h, account));
        var snapshot = Payload(await Read(snapshotRoute, QuerySnapshot(account)));
        var exact = Assert.Single(snapshot.GetProperty("originalEvents").EnumerateArray());
        Assert.Equal(firstEvent.Body, Convert.FromBase64String(exact.GetProperty("bodyBase64").GetString()!));
        Assert.Equal(firstEvent.Hash, exact.GetProperty("bodyHash").GetString());
        Assert.Equal(firstEvent.Id, exact.GetProperty("eventId").GetGuid());

        var scopeId = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT scope_identity AS "Value" FROM main_bp.mapping_slots WHERE tenant_id={h.Tenant} AND account_id={account}
            """).SingleAsync();
        object MakeScope(Guid id, string epoch, Guid org) => new
        {
            mappingAuthority = authority.Authority, scopeIdentity = id, scopeEpoch = epoch, organizationId = org,
            legalEntityKey = MappingLegal, accountId = account, mappingPurpose = "ACCOUNT_BUSINESS_PARTNER"
        };
        var decisionScope = MakeScope(scopeId, authority.Epoch, authority.OrganizationId);
        var unknown = Payload(await Read(decisionRoute + "/query", new { scope = decisionScope, knownDecision = (object?)null }));
        Assert.Equal("UNKNOWN", unknown.GetProperty("knowledge").GetString());
        await InstallMappingBinding(h, authority);
        const string reason = "模拟独立决定首次登记";
        var change = await ReadMappingContext(app, cookies, account, observation, MappingCriteria("000030B", "模拟决定伙伴后继"));
        var approved = await BindMapping(h, bridge, app, cookies, account, MappingSubject(account, "REPLACE", "1", second, reason));
        await ApplyMap(app, cookies, await GoodMapPreview(app, cookies, MappingInput(change, "REPLACE", second, reason,
            binding: approved.Binding, evidence: [approved.Evidence])), Guid.NewGuid());

        var proven = Payload(await Read(decisionRoute + "/query", new { scope = decisionScope, knownDecision = (object?)null }));
        Assert.Equal("PROVEN", proven.GetProperty("knowledge").GetString());
        var decision = proven.GetProperty("decision");
        Assert.Equal("ACC-MAPPING-DECISION/1", decision.GetProperty("schemaVersion").GetString());
        Assert.Equal("COMPLETE_SNAPSHOT", decision.GetProperty("basis").GetString());
        Assert.True(decision.GetProperty("coversEntireScope").GetBoolean());
        var candidate = Assert.Single(decision.GetProperty("candidates").EnumerateArray());
        Assert.Equal("000030B", candidate.GetProperty("businessPartnerKey").GetString());
        Assert.Equal("9007199254740993", candidate.GetProperty("boundAccountBusinessVersion").GetString());
        Assert.Equal("2", candidate.GetProperty("ownerMappingRevision").GetString());
        var pointer = decision.GetProperty("decision");
        Assert.Equal("1", pointer.GetProperty("position").GetString());
        var digest = pointer.GetProperty("digest").GetString();
        Dictionary<string, object?> Fields(JsonElement element, params string[] omit) => element.EnumerateObject()
            .Where(p => !omit.Contains(p.Name, StringComparer.Ordinal)).ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
        var canonical = Fields(decision, "authorityProof");
        canonical["decision"] = Fields(pointer, "digest");
        Assert.Equal(CommercialCanonicalV1.Digest("ACC-MAPPING-DECISION-1", canonical), digest);
        Assert.Equal(CommercialCanonicalV1.Digest("ACC-MAPPING-CANDIDATE-1", Fields(candidate, "candidateDigest")),
            candidate.GetProperty("candidateDigest").GetString());
        Assert.Equal(candidate.GetProperty("candidateDigest").GetString(), decision.GetProperty("selectedCandidateDigest").GetString());

        var proofRef = decision.GetProperty("authorityProof");
        var proofBytes = await Read(decisionRoute + "/evidence/" + proofRef.GetProperty("evidenceId").GetString() +
            $"?legalEntityKey={MappingLegal}&accountId={account:D}", null);
        var proof = Payload(proofBytes);
        Assert.Equal(MappingSha(proofBytes), proofRef.GetProperty("contentDigest").GetString());
        Assert.Equal(digest, proof.GetProperty("decisionDigest").GetString());
        Assert.Equal("CRM_CURRENT_MAPPING_DECISION", proof.GetProperty("purpose").GetString());
        Assert.Equal(decision.GetProperty("completeAtWatermark").GetString(), proof.GetProperty("completeAtWatermark").GetString());
        Assert.True(PublicKey.TryImport(SignatureAlgorithm.Ed25519, authority.PublicKey, KeyBlobFormat.RawPublicKey, out var publicKey));
        Assert.True(SignatureAlgorithm.Ed25519.Verify(publicKey!,
            [.. Encoding.UTF8.GetBytes("ACC-MAPPING-AUTHORITY-PROOF-1\n"), .. CommercialCanonicalV1.Data(Fields(proof, "signatureBase64"))],
            Convert.FromBase64String(proof.GetProperty("signatureBase64").GetString()!)));

        var policy = decision.GetProperty("identityImpactPolicy");
        var policyBytes = await Read(decisionRoute + "/evidence/" + Uri.EscapeDataString(policy.GetProperty("evidenceId").GetString()!) +
            $"?legalEntityKey={MappingLegal}&accountId={account:D}", null);
        Assert.Equal(MappingSha(policyBytes), policy.GetProperty("contentDigest").GetString());
        Assert.Equal(new[] { "displayName", "aliases", "domains", "accountType", "countryCode", "province", "city", "address" },
            Payload(policyBytes).GetProperty("fields").EnumerateArray().Select(x => x.GetString()));

        var forkPointer = new { decisionId = pointer.GetProperty("decisionId"), position = "1", digest = new string('d', 64) };
        var fork = Payload(await Read(decisionRoute + "/query", new { scope = decisionScope, knownDecision = forkPointer }));
        Assert.Equal("CONFLICT", fork.GetProperty("knowledge").GetString());
        Assert.Equal("UNKNOWN", Payload(await Read(decisionRoute + "/query", new
        {
            scope = MakeScope(scopeId, authority.Epoch, Guid.NewGuid()), knownDecision = (object?)null
        })).GetProperty("knowledge").GetString());
        Assert.Equal("UNKNOWN", Payload(await Read(decisionRoute + "/query", new
        {
            scope = MakeScope(scopeId, "fixture-unregistered-epoch", authority.OrganizationId), knownDecision = (object?)null
        })).GetProperty("knowledge").GetString());
        Assert.Equal(2, (await StoredEvents(h, account)).Length);
        await DeviceEvidence("wp04-03-i03-r03.json", new { account, scopeId, absentSnapshot = "UNSEEN", snapshotBodyHash = firstEvent.Hash,
            initialUnsigned = "UNKNOWN", distinctAxes = new { mappingRevision = "2", decisionPosition = "1" },
            signedProof = proofRef.GetProperty("contentDigest"), allImpactFields = true, exactScopeMismatch = "UNKNOWN",
            samePositionDifferentDigest = "CONFLICT", decision });
    }
}
