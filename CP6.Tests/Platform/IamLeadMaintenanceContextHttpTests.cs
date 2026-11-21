using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Middleware;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private const string MaintenanceRoute = "/connect/lead-maintenance-context-v1";
    private static readonly JsonSerializerOptions MaintenanceJson = new(JsonSerializerDefaults.Web);

    [TenantManagementPostgresTheory]
    [InlineData("INDEPENDENT_INBOUND")]
    [InlineData("MANUAL_LEAD")]
    public async Task Lead_maintenance_actual_TLS_authenticates_the_current_service(string kind)
    {
        await using var h = await Seed();
        await GrantRetention(h);
        var options = InquiryPermissionSigningTests.Options();
        using var signing = InquiryPermissionSigningTests.Signer(options);
        var runtime = ContextRuntime(h.Tenant, options.Issuer);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var registration = ServiceRegistration(h, Guid.NewGuid());
        registration.Oidc.Issuer = options.Issuer;
        registration.Source.IdentityPublicationEnabled = true;
        registration.Source.IdentityPublicationRevision = 9007199254740993;
        var entry = registration.Source.Registrations[0];
        entry.AuthenticationEpoch = Guid.NewGuid();
        var registry = new InquiryServicePrincipalRegistryV1(registration.Source, registration.Oidc);
        await PublishIdentity(registry);

        var inbound = kind == "INDEPENDENT_INBOUND";
        var target = new MaintenanceTarget(h.Tenant, kind, inbound ? "sim.inbound.source" : "sim.lead.authority", Guid.NewGuid());
        var message = inbound ? "original-source-message-403" : null;
        const string policyOwner = "sim.priv.policy";
        const string holdOwner = "sim.priv.hold";
        const string policyVersion = "opaque/policy:2026.01-7";
        const string keyPolicyVersion = "opaque/key-policy:11";
        using var sourceKey = Key.Create(SignatureAlgorithm.Ed25519);
        using var policyKey = Key.Create(SignatureAlgorithm.Ed25519);
        using var holdKey = Key.Create(SignatureAlgorithm.Ed25519);
        var trusted = new[]
        {
            Provider("SOURCE", target.Authority, sourceKey), Provider("POLICY", policyOwner, policyKey),
            Provider("HOLD", holdOwner, holdKey)
        };
        var trust = new LeadMaintenanceTrustV1(new() { Enabled = true, ProviderKeys = [.. trusted] });
        var dbNow = await h.Db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        var received = inbound ? dbNow.AddDays(-3) : new DateTimeOffset(2024, 2, 29, 10, 30, 0, TimeSpan.Zero);
        var source = Encode("SOURCE", sourceKey, new
        {
            target, providerAuthority = target.Authority, version = "bounded/original-2", sequence = "1",
            sourceMessageId = message, receivedAtUtc = received, policyAuthority = policyOwner
        });
        var policy = MakePolicy(1, policyVersion, dbNow.AddDays(inbound ? -2 : 3));
        var hold = MakeHold(1, dbNow, [], EvidenceHash(policy.Payload));
        var policyRequest = new MaintenanceRequest(h.Tenant, kind, target.Authority, target.ObjectId, message, "privacy:policy", ServiceNonce());
        var holdRequest = policyRequest with { Purpose = "privacy:hold", RequestNonce = ServiceNonce() };
        var maintainRequest = policyRequest with { Purpose = "privacy:maintain", RequestNonce = ServiceNonce() };
        foreach (var request in new[] { policyRequest, holdRequest, maintainRequest })
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_lead_maintenance_v1.scope_grants
                VALUES ({h.Tenant},{kind},{target.ObjectId},{target.Authority},{entry.SubjectId},{entry.AuthenticationEpoch},
                    {request.Purpose},'grant:one',{dbNow.AddDays(-1)},{dbNow.AddDays(1)},true)
                """);

        using var rsa = RSA.Create(2048);
        var identity = new CertificateRequest("CN=owned-WP02-30-Main", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = identity.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(created.Export(X509ContentType.Pfx));
        var dropped = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientHandler = new HttpClientHandler
        {
            UseCookies = false, UseProxy = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == certificate.Thumbprint
        };
        using var http = new HttpClient(clientHandler) { Timeout = TimeSpan.FromSeconds(30) };
        string initialUrl;
        JsonElement originalPolicy;
        JsonElement originalHold;
        JsonElement originalMaintain;
        JsonElement lostScope;
        JsonElement exportDenied, wrongAuthorityDenied, activeHoldDenied;
        MaintenanceRequest holdReplayRequest;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using (var app = await StartHost(registry))
        {
            initialUrl = Origin(app);
            await Expect(initialUrl, policyRequest, HttpStatusCode.Unauthorized, "invalid_client",
                auth: ServiceBasic("incorrect").ToString());
            await Expect(initialUrl, policyRequest with { TenantId = Guid.NewGuid() }, HttpStatusCode.Forbidden, "insufficient_scope");
            wrongAuthorityDenied = await Expect(initialUrl, policyRequest with { Authority = "sim.wrong.authority" }, HttpStatusCode.Forbidden, "insufficient_scope");
            await Expect(initialUrl, policyRequest with { ObjectId = Guid.NewGuid() }, HttpStatusCode.Forbidden, "insufficient_scope");
            exportDenied = await Expect(initialUrl, maintainRequest with { Purpose = "privacy:export" },
                HttpStatusCode.BadRequest, "unsupported_projection_purpose");
            foreach (var wrongPurpose in new[] { "privacy:read", "privacy:download", "privacy:legal" })
                await Expect(initialUrl, policyRequest with { Purpose = wrongPurpose }, HttpStatusCode.BadRequest, "unsupported_projection_purpose");

            var withUnexpected = JsonSerializer.Serialize(policyRequest, MaintenanceJson).Insert(1, "\"siteId\":\"not-an-inquiry\",");
            await Expect(initialUrl, withUnexpected, HttpStatusCode.BadRequest, "invalid_request");
            var duplicate = JsonSerializer.Serialize(policyRequest, MaintenanceJson).Insert(1, "\"purpose\":\"privacy:export\",");
            await Expect(initialUrl, duplicate, HttpStatusCode.BadRequest, "invalid_request");
            await Expect(initialUrl, policyRequest with { RequestNonce = "forged" }, HttpStatusCode.BadRequest, "invalid_request");
            await Expect(initialUrl, policyRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");

            await PublishEvidence("SOURCE", 1, source);
            await Expect(initialUrl, policyRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            if (inbound)
            {
                await Expect(initialUrl, policyRequest with { SourceMessageId = "another-message" }, HttpStatusCode.Forbidden, "insufficient_scope");
                await Assert.ThrowsAsync<PostgresException>(async () => await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_lead_maintenance_v1.evidence
                        (tenant_id,target_kind,object_id,component,object_authority,source_message_id,sequence,payload,signature)
                    VALUES ({h.Tenant},{kind},{Guid.NewGuid()},'SOURCE',{target.Authority},{message},1,{source.Payload},{source.Signature})
                    """));
            }
            await PublishEvidence("POLICY", 1, policy);
            originalPolicy = await Expect(initialUrl, policyRequest, HttpStatusCode.OK);
            var policyPayload = CheckContext(originalPolicy, policyRequest, ["SOURCE", "POLICY"]);
            var deadline = inbound ? received.AddSeconds(3600) : new DateTimeOffset(2026, 2, 28, 10, 30, 0, TimeSpan.Zero);
            Assert.Equal(deadline, policyPayload.GetProperty("purgeAtUtc").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, policyPayload.GetProperty("holdActive").ValueKind);
            var policyBody = ReadWirePayload(policyPayload.GetProperty("evidence")[1]);
            Assert.Equal(policyVersion, policyBody.GetProperty("version").GetString());
            Assert.Equal(holdOwner, policyBody.GetProperty("holdAuthority").GetString());
            Assert.Equal(keyPolicyVersion, policyBody.GetProperty("keyPolicyVersion").GetString());
            Assert.Equal(EvidenceHash(Encoding.UTF8.GetBytes("模拟保留政策-v7")), policyBody.GetProperty("contentDigest").GetString());
            Assert.Equal(dbNow.AddDays(-4), policyBody.GetProperty("effectiveFromUtc").GetDateTimeOffset());
            Assert.Equal(dbNow.AddDays(inbound ? -2 : 3), policyBody.GetProperty("effectiveToUtc").GetDateTimeOffset());
            Assert.Equal(inbound ? 3600 : 24, policyBody.GetProperty(inbound ? "retainSeconds" : "retainCalendarMonths").GetInt32());

            await Expect(initialUrl, holdRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            await PublishEvidence("HOLD", 1, hold with { Signature = source.Signature });
            await Expect(initialUrl, holdRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            await PublishEvidence("HOLD", 1, hold);
            originalHold = await Expect(initialUrl, holdRequest, HttpStatusCode.OK);
            var holdPayload = CheckContext(originalHold, holdRequest, ["SOURCE", "HOLD"]);
            Assert.Equal(JsonValueKind.Null, holdPayload.GetProperty("purgeAtUtc").ValueKind);
            Assert.False(holdPayload.GetProperty("holdActive").GetBoolean());
            Assert.Equal(holdOwner, ReadWirePayload(holdPayload.GetProperty("evidence")[1]).GetProperty("providerAuthority").GetString());

            originalMaintain = await Expect(initialUrl, maintainRequest, HttpStatusCode.OK);
            lostScope = CheckContext(originalMaintain, maintainRequest, ["SOURCE", "POLICY", "HOLD"]);
            Assert.False(lostScope.GetProperty("holdActive").GetBoolean());
            Assert.Equal(deadline, lostScope.GetProperty("purgeAtUtc").GetDateTimeOffset());
            using var inject = new HttpRequestMessage(HttpMethod.Post, initialUrl + MaintenanceRoute)
                { Content = JsonContent.Create(maintainRequest) };
            inject.Headers.Add("Authorization", ServiceBasic().ToString());
            inject.Headers.Add("X-Owned-Drop", "1");
            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                using var lost = await http.SendAsync(inject, ct);
                _ = await lost.Content.ReadAsByteArrayAsync(ct);
            });
            Assert.Equal(200, await dropped.Task.WaitAsync(ct));
        }

        // 用新宿主和新注册实例走原范围，复核当前入站材料并重新签发。
        var coldRegistry = new InquiryServicePrincipalRegistryV1(registration.Source, registration.Oidc);
        await PublishIdentity(coldRegistry);
        await using (var app = await StartHost(coldRegistry))
        {
            var origin = Origin(app);
            var coldRequest = maintainRequest with { RequestNonce = ServiceNonce() };
            holdReplayRequest = coldRequest;
            var coldResponse = await Expect(origin, coldRequest, HttpStatusCode.OK);
            var cold = CheckContext(coldResponse, coldRequest, ["SOURCE", "POLICY", "HOLD"]);
            Assert.Equal(lostScope.GetProperty("scopeDigest").GetString(), cold.GetProperty("scopeDigest").GetString());
            Assert.Equal(lostScope.GetProperty("target").GetRawText(), cold.GetProperty("target").GetRawText());
            Assert.NotEqual(lostScope.GetProperty("requestNonce").GetString(), cold.GetProperty("requestNonce").GetString());
            Assert.Equal(lostScope.GetProperty("evidence").GetRawText(), cold.GetProperty("evidence").GetRawText());

            dbNow = await h.Db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
            var held = MakeHold(2, dbNow, [new(dbNow.AddSeconds(-3), null)], EvidenceHash(policy.Payload));
            await using (var publisherTx = await h.Db.Database.BeginTransactionAsync(ct))
            {
                await PublishEvidence("HOLD", 2, held);
                var during = Expect(origin, coldRequest, HttpStatusCode.Conflict, "active_hold");
                await WaitForMaintenanceAdvisoryLock(ct);
                Assert.False(during.IsCompleted);
                await publisherTx.CommitAsync(ct);
                activeHoldDenied = await during;
            }
            var activeProof = CheckContext(await Expect(origin, holdRequest, HttpStatusCode.OK), holdRequest, ["SOURCE", "HOLD"]);
            Assert.True(activeProof.GetProperty("holdActive").GetBoolean());
            Assert.Equal("hold:2", ReadWirePayload(activeProof.GetProperty("evidence")[1]).GetProperty("version").GetString());

            var expiredHold = MakeHold(3, dbNow.AddMinutes(-8), [], EvidenceHash(policy.Payload));
            await PublishEvidence("HOLD", 3, expiredHold);
            await Expect(origin, holdRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            var wrongPolicy = MakeHold(4, dbNow, [], new string('0', 64));
            await PublishEvidence("HOLD", 4, wrongPolicy);
            await Expect(origin, coldRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            var futureStart = dbNow.AddSeconds(45);
            await PublishEvidence("HOLD", 5, MakeHold(5, dbNow, [new(futureStart, null)], EvidenceHash(policy.Payload)));
            var future = CheckContext(await Expect(origin, coldRequest, HttpStatusCode.OK), coldRequest, ["SOURCE", "POLICY", "HOLD"]);
            Assert.Equal(futureStart, future.GetProperty("expiresAtUtc").GetDateTimeOffset());

            var oldHoldWrite = await Assert.ThrowsAsync<PostgresException>(async () => await PublishEvidence("HOLD", 1, hold));
            Assert.Equal("23514", oldHoldWrite.SqlState);
            if (inbound)
            {
                var replacedFrozen = await Assert.ThrowsAsync<PostgresException>(async () => await PublishEvidence("POLICY", 2, policy));
                Assert.Equal("23514", replacedFrozen.SqlState);
            }
            if (!inbound)
            {
                await PublishEvidence("POLICY", 2, MakePolicy(2, "manual:expired-2", dbNow.AddSeconds(-1)));
                await Expect(origin, policyRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
                var currentPolicy = MakePolicy(3, "manual:current-3", dbNow.AddDays(2));
                await PublishEvidence("POLICY", 3, currentPolicy);
                await Expect(origin, coldRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
                await PublishEvidence("HOLD", 6, MakeHold(6, dbNow, [], EvidenceHash(currentPolicy.Payload)));
                var current = CheckContext(await Expect(origin, coldRequest, HttpStatusCode.OK), coldRequest, ["SOURCE", "POLICY", "HOLD"]);
                Assert.Equal("manual:current-3", ReadWirePayload(current.GetProperty("evidence")[1]).GetProperty("version").GetString());
                Assert.Equal(lostScope.GetProperty("purgeAtUtc").GetString(), current.GetProperty("purgeAtUtc").GetString());
            }
            await SetGrant("privacy:maintain", true, dbNow.AddSeconds(-1));
            await Expect(origin, coldRequest, HttpStatusCode.Forbidden, "insufficient_scope");
            await SetGrant("privacy:maintain", false, dbNow.AddDays(1));
            await Expect(origin, coldRequest, HttpStatusCode.Forbidden, "insufficient_scope");
            await Expect(origin, policyRequest, HttpStatusCode.OK);
            await SetGrant("privacy:maintain", true, dbNow.AddDays(1));
            await h.Db.Sys_RoleActions.Where(row => row.RoleId == 71 && row.ActionCode == "retention-run").ExecuteDeleteAsync(ct);
            await Expect(origin, coldRequest, HttpStatusCode.Forbidden, "insufficient_scope");

            registration.Oidc.ServiceClients.Single(item => item.ClientId == ServiceClient).Enabled = false;
            registration.Source.IdentityPublicationRevision++;
            registration.Source.Version = "fixture-reg-disabled-2";
            entry.AuthenticationEpoch = Guid.NewGuid();
            var disabledRegistry = new InquiryServicePrincipalRegistryV1(registration.Source, registration.Oidc);
            await PublishIdentity(disabledRegistry);
            // 原进程登记仍留在内存，持久服务身份已变，不能再签发。
            await Expect(origin, policyRequest, HttpStatusCode.ServiceUnavailable, "lead_maintenance_authority_unavailable");
            await using var disabled = await StartHost(disabledRegistry);
            await Expect(Origin(disabled), policyRequest, HttpStatusCode.Unauthorized, "invalid_client");
        }

        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_lead_maintenance_v1.evidence WHERE tenant_id={h.Tenant}
            """).SingleAsync(ct));
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_lead_maintenance_v1.scope_grants WHERE tenant_id={h.Tenant}
            """).SingleAsync(ct));

        if (Environment.GetEnvironmentVariable("CP6_LEAD_MAINTENANCE_PROOF_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            var proof = new
            {
                scenario = kind, mainPublicKeyBase64 = options.PublicKeyBase64, trusted,
                policy = new { request = policyRequest, response = originalPolicy },
                hold = new { request = holdRequest, response = originalHold },
                maintain = new { request = maintainRequest, response = originalMaintain },
                rejected = new[]
                {
                    new { request = maintainRequest with { Purpose = "privacy:export" }, status = 400, response = exportDenied },
                    new { request = policyRequest with { Authority = "sim.wrong.authority" }, status = 403, response = wrongAuthorityDenied },
                    new { request = holdReplayRequest, status = 409, response = activeHoldDenied }
                }
            };
            await File.WriteAllTextAsync(Path.Combine(directory, kind + ".json"),
                JsonSerializer.Serialize(proof, new JsonSerializerOptions(MaintenanceJson) { WriteIndented = true }) + "\n", ct);
        }

        async Task PublishIdentity(InquiryServicePrincipalRegistryV1 from)
        {
            var tenant = new TenantContext();
            await using var db = server.Context(tenant, runtime);
            await new InquiryServiceIdentityPublisherV1(db, tenant, from, runtime).PublishAsync(h.Tenant);
        }

        LeadMaintenanceProviderKeyV1 Provider(string component, string authority, Key key) => new()
        {
            TenantId = h.Tenant, TargetKind = kind, Component = component, Authority = authority,
            KeyId = "fixture-" + component, PublicKeyBase64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))
        };

        MaintenanceEvidence MakePolicy(long sequence, string version, DateTimeOffset effectiveTo) =>
            Encode("POLICY", policyKey, new
        {
            target, providerAuthority = policyOwner, version, sequence = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sourceSha256 = EvidenceHash(source.Payload), contentDigest = EvidenceHash(Encoding.UTF8.GetBytes("模拟保留政策-v7")),
            payloadClass = inbound ? "H01_QUARANTINED_RAW" : "MANUAL_LEAD_PII",
            purpose = inbound ? "CRM_LEAD_INBOUND_RETENTION" : "CRM_LEAD_RETENTION",
            effectiveFromUtc = dbNow.AddDays(-4), effectiveToUtc = effectiveTo,
            retainSeconds = inbound ? (int?)3600 : null, retainCalendarMonths = inbound ? null : (int?)24,
            holdAuthority = holdOwner, keyPolicyVersion
        });

        MaintenanceEvidence MakeHold(long sequence, DateTimeOffset observed, MaintenanceWindow[] windows, string policySha256) =>
            Encode("HOLD", holdKey, new
            {
                target, providerAuthority = holdOwner, version = $"hold:{sequence}", sequence = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                sourceSha256 = EvidenceHash(source.Payload), policySha256, observedAtUtc = observed, validUntilUtc = observed.AddMinutes(4), windows
            });

        Task<int> PublishEvidence(string component, long sequence, MaintenanceEvidence value)
        {
            var sourceAuthority = component == "SOURCE" ? target.Authority : null;
            var sourceMessageId = component == "SOURCE" ? message : null;
            return h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_lead_maintenance_v1.evidence
                    (tenant_id,target_kind,object_id,component,object_authority,source_message_id,sequence,payload,signature)
                VALUES ({h.Tenant},{kind},{target.ObjectId},{component},{sourceAuthority},{sourceMessageId},{sequence},{value.Payload},{value.Signature})
                ON CONFLICT (tenant_id,target_kind,object_id,component)
                DO UPDATE SET sequence=excluded.sequence,payload=excluded.payload,signature=excluded.signature
                """, ct);
        }

        Task<int> SetGrant(string purpose, bool enabled, DateTimeOffset end) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_lead_maintenance_v1.scope_grants SET enabled={enabled},valid_until_utc={end},version='grant:two'
            WHERE tenant_id={h.Tenant} AND object_id={target.ObjectId} AND purpose={purpose}
            """, ct);

        async Task<WebApplication> StartHost(InquiryServicePrincipalRegistryV1 localRegistry)
        {
            var host = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            host.Logging.ClearProviders();
            host.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
            host.Services.AddScoped<TenantContext>();
            host.Services.AddScoped<ITenantContext>(s => s.GetRequiredService<TenantContext>());
            host.Services.AddScoped<CP6Context>(s => server.Context(s.GetRequiredService<ITenantContext>(), runtime));
            host.Services.AddSingleton(localRegistry);
            host.Services.AddSingleton(signing);
            host.Services.AddSingleton(trust);
            host.Services.AddSingleton(runtime);
            host.Services.AddScoped<LeadMaintenanceContextIssuerV1>();
            host.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly)
                .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new ServiceContextControllers())).AddControllersAsServices();
            host.Services.RemoveAll<CrmOidcController>();
            host.Services.AddTransient(s => new CrmOidcController(registration.Oidc, null!, null!, null!, host.Configuration,
                null!, null!, leadMaintenance: s.GetRequiredService<LeadMaintenanceContextIssuerV1>()));
            var app = host.Build();
            app.UseMiddleware<CsrfMiddleware>();
            app.Use(async (context, next) =>
            {
                if (!context.Request.Headers.ContainsKey("X-Owned-Drop"))
                {
                    await next(context);
                    return;
                }
                // 故障注入只在隔离宿主中缓存并丢弃已签发回包。
                await using var sink = new MemoryStream();
                var responseBody = context.Response.Body;
                context.Response.Body = sink;
                try
                {
                    await next(context);
                    dropped.TrySetResult(context.Response.StatusCode);
                    context.Abort();
                }
                finally { context.Response.Body = responseBody; }
            });
            app.MapControllers();
            await app.StartAsync(ct);
            return app;
        }

        async Task<JsonElement> Expect(string origin, object body, HttpStatusCode status, string? error = null, string? auth = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, origin + MaintenanceRoute)
            {
                Content = body is string json ? new StringContent(json, Encoding.UTF8, "application/json") : JsonContent.Create(body)
            };
            request.Headers.Add("Authorization", auth ?? ServiceBasic().ToString());
            using var result = await http.SendAsync(request, ct);
            var raw = await result.Content.ReadAsStringAsync(ct);
            Assert.True(status == result.StatusCode, $"预期 {(int)status}，实际 {(int)result.StatusCode}：{raw}");
            Assert.True(result.Headers.CacheControl?.NoStore);
            var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
            if (error is not null)
            {
                Assert.Equal(error, parsed.GetProperty("error").GetString());
                Assert.Single(parsed.EnumerateObject());
            }
            return parsed;
        }

        JsonElement CheckContext(JsonElement outer, MaintenanceRequest request, string[] expected)
        {
            var version = outer.GetProperty("schemaVersion").GetString();
            var issuer = outer.GetProperty("issuer").GetString();
            var keyId = outer.GetProperty("keyId").GetString();
            Assert.Equal("cp6-lead-maintenance-context-v1", version);
            Assert.Equal(options.Issuer, issuer);
            Assert.Equal(options.KeyId, keyId);
            Assert.Equal("Ed25519", outer.GetProperty("algorithm").GetString());
            var unsigned = WebEncoders.Base64UrlDecode(outer.GetProperty("payload").GetString()!);
            Assert.True(ValidSignature(options.PublicKeyBase64, $"{version}\n{issuer}\n{keyId}\n", unsigned, outer));
            Assert.False(ValidSignature(options.PublicKeyBase64, $"cp6-inquiry-service-context-v1\n{issuer}\n{keyId}\n", unsigned, outer));
            var result = JsonSerializer.Deserialize<JsonElement>(unsigned);
            Assert.Equal(request.RequestNonce, result.GetProperty("requestNonce").GetString());
            Assert.Equal(request.Purpose, result.GetProperty("purpose").GetString());
            Assert.Equal(request.SourceMessageId, result.GetProperty("sourceMessageId").GetString());
            Assert.Equal(target, result.GetProperty("target").Deserialize<MaintenanceTarget>(MaintenanceJson));
            Assert.Equal("ALLOW", result.GetProperty("decision").GetString());
            var service = result.GetProperty("service");
            Assert.Equal(entry.SubjectId, service.GetProperty("principalId").GetGuid());
            Assert.Equal(entry.AuthenticationEpoch, service.GetProperty("authenticationEpoch").GetGuid());
            Assert.Equal("9007199254740993", service.GetProperty("registrationRevision").GetString());
            Assert.Equal(runtime.ContractBundleSha256, service.GetProperty("identityBundleSha256").GetString());
            var issued = result.GetProperty("issuedAtUtc").GetDateTimeOffset();
            var expires = result.GetProperty("expiresAtUtc").GetDateTimeOffset();
            Assert.True(expires > issued && expires <= issued.AddMinutes(1));
            Assert.Equal(expected, result.GetProperty("evidence").EnumerateArray().Select(fact => fact.GetProperty("component").GetString()));
            foreach (var fact in result.GetProperty("evidence").EnumerateArray())
            {
                var component = fact.GetProperty("component").GetString()!;
                var key = Assert.Single(trusted, item => item.Component == component);
                var bytes = WebEncoders.Base64UrlDecode(fact.GetProperty("payload").GetString()!);
                Assert.Equal(EvidenceHash(bytes), fact.GetProperty("sha256").GetString());
                Assert.Equal(key.KeyId, fact.GetProperty("keyId").GetString());
                Assert.True(ValidSignature(key.PublicKeyBase64, $"cp6-lead-maintenance-evidence-v1\n{component}\n", bytes, fact));
            }
            return result;
        }

        async Task WaitForMaintenanceAdvisoryLock(CancellationToken token)
        {
            var waitingTenant = new TenantContext { CurrentTenantId = h.Tenant };
            await using var observer = server.Context(waitingTenant, runtime);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (await observer.Database.SqlQueryRaw<bool>("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
                        AND wait_event='advisory' AND query LIKE '%cp6_lead_maintenance_v1.lock_target%') AS "Value"
                    """).SingleAsync(token)) return;
                await Task.Delay(30, token);
            }
            throw new InvalidOperationException("未观察到 Main 等待对象发布锁。");
        }
    }

    private static string Origin(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    private static MaintenanceEvidence Encode<T>(string component, Key key, T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, MaintenanceJson);
        var signature = SignatureAlgorithm.Ed25519.Sign(key,
            [.. Encoding.UTF8.GetBytes($"cp6-lead-maintenance-evidence-v1\n{component}\n"), .. payload]);
        return new(payload, signature);
    }

    private static bool ValidSignature(string publicKey, string prefix, byte[] payload, JsonElement envelope) =>
        SignatureAlgorithm.Ed25519.Verify(NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, Convert.FromBase64String(publicKey),
            KeyBlobFormat.RawPublicKey), [.. Encoding.UTF8.GetBytes(prefix), .. payload],
            WebEncoders.Base64UrlDecode(envelope.GetProperty("signature").GetString()!));

    private static string EvidenceHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JsonElement ReadWirePayload(JsonElement envelope) =>
        JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(envelope.GetProperty("payload").GetString()!));

    private sealed record MaintenanceEvidence(byte[] Payload, byte[] Signature);
    private sealed record MaintenanceTarget(Guid TenantId, string TargetKind, string Authority, Guid ObjectId);
    private sealed record MaintenanceRequest(Guid TenantId, string TargetKind, string Authority, Guid ObjectId,
        string? SourceMessageId, string Purpose, string RequestNonce);
    private sealed record MaintenanceWindow(DateTimeOffset FromUtc, DateTimeOffset? ToUtc);
}
