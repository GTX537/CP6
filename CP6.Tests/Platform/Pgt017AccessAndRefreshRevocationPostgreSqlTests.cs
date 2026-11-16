using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.Tests.Erp;
using CP6.WebApi.Seed;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017AccessAndRefreshFact]
    public async Task PGT017_access_logout_and_delivered_refresh_reuse_retire_only_their_original_CRM_sources()
    {
        var scenario = Environment.GetEnvironmentVariable("CP6_W667_SCENARIO")!;
        var baseline = Environment.GetEnvironmentVariable("CP6_W667_BASELINE") == "1";
        var auditFault = scenario == "unknown-cookie-audit-fault";
        var exactAccess = scenario is "web-access" or "client-web-alias" or "web-access-duplicate" ||
            !baseline && (scenario == "unknown-cookie" || auditFault);
        var reuse = scenario is "web-reuse" or "native-reuse";
        var affected = reuse ? 4 : exactAccess ? 2 : 0;
        var completionControlled = scenario == "rejected-completion";
        var nativeClosed = scenario == "ordinary-close";
        var recovery = Environment.GetEnvironmentVariable("CP6_W667_RECOVERY");
        var order = Environment.GetEnvironmentVariable("CP6_PGT017_ORDER");
        var proofDirectory = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        var user = await h.Db.Sys_Users.SingleAsync(u => u.Id == h.ActorId);
        user.AuthenticationEpoch = Guid.NewGuid();
        Sys_User? unaffectedUser = null;
        if (recovery is not null)
        {
            unaffectedUser = new Sys_User { UserName = "w667-unrelated", DeptId = h.Parent, RoleId = 71, Enable = true,
                Password = new BCryptPasswordHasher(4).Hash("Iam1!Password"), AuthenticationEpoch = Guid.NewGuid() };
            h.Db.Sys_Users.Add(unaffectedUser);
        }
        var authenticationEpoch = user.AuthenticationEpoch;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var signing = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, signing.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        const string redirect = "https://crm.cp6.test/callback";
        var clientSecret = CrmOidcCrypto.RandomToken();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        var reader = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_W667_CRM_READER"));
        var crmObserver = Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER");
        await using var crm = new NpgsqlConnection(crmObserver);
        await crm.OpenAsync();
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database, "w667_main_seal"), (seal.Host, seal.Port, seal.Database, seal.Username));
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database), (reader.Host, reader.Port, reader.Database));
        Assert.Matches("^w51_runtime_[a-f0-9]{8}$", reader.Username!);
        if (order is null)
        {
            await InstallUserSealFixture(crm, seal, h.Tenant, store);
            await GrantReadWitness();
        }

        var main = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString());
        await using var mainRelay = recovery == "ack" ? new StockPostgresCommitRelay(main.Port, main.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W667_MAIN_ACK_PORT")!)) : null;
        await using var crmRelay = recovery == "ack" ? new StockPostgresCommitRelay(seal.Port, seal.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W667_CRM_ACK_PORT")!)) : null;
        if (mainRelay is not null)
        {
            main.Port = mainRelay.Port;
            main.SslMode = SslMode.Disable;
        }
        var environment = IdentityEnvironment(h.Tenant);
        var binding = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[binding + "StoreId"] = store.ToString("D");
        environment[binding + "ConnectionString"] = crmRelay is null ? seal.ConnectionString
            : new NpgsqlConnectionStringBuilder(seal.ConnectionString) { Port = crmRelay.Port }.ConnectionString;
        environment["CrmOidc__Issuer"] = signing.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = signing.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = signing.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = signing.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = signing.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = signing.ActiveFromUtc.ToString("O");
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        environment["Kafka__BootstrapServers"] = "";
        environment["RabbitMQ__HostName"] = "";
        environment["CrmIdentity__DaprHttpEndpoint"] = "http://127.0.0.1:57257";
        environment["CrmIdentity__DaprGrpcEndpoint"] = "http://127.0.0.1:57258";
        await using var initial = await StartMain();
        var host = initial;

        var original = await WebLogin();
        var first = await Authorize(original);
        var sameJti = await Authorize(original);
        using var webRotation = await host.Send(Request(HttpMethod.Post, "api/auth/refresh", original, new { }));
        Assert.Equal(HttpStatusCode.OK, webRotation.StatusCode);
        var current = Cookies(webRotation);
        await RequireDelivered(original["cp6_rt"], current["cp6_rt"], "Web");
        var laterJti = await Authorize(current);
        var other = await WebLogin();
        var otherFamily = await Authorize(other);
        var outsideCookies = unaffectedUser is null ? null : await WebLogin(unaffectedUser.UserName);
        var outsidePermit = outsideCookies is null ? null : await Authorize(outsideCookies, unaffectedUser!.Id);
        var noGrant = scenario == "unknown-cookie-local" ? await WebLogin() : null;
        var authorities = new[] { first, sameJti, laterJti, otherFamily };
        Assert.NotEqual(first.GrantId, sameJti.GrantId);
        Assert.Equal(first.Jti, sameJti.Jti);
        Assert.NotEqual(first.Jti, laterJti.Jti);
        Assert.Equal(first.Family, laterJti.Family);
        Assert.NotEqual(first.Family, otherFamily.Family);
        if (order is not null)
        {
            await SendOriginalBridge();
            await WaitConfigurationBridge(proofDirectory, "ready");
            await GrantReadWitness();
        }
        foreach (var permit in authorities) Assert.True(await GateCurrent(permit));

        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "w667-owned-windows", AppVersion = "1.0.0" };
        using var nativeLogin = await host.Send(Native("login", new NativeLoginRequest
        {
            Client = client, UserName = "iam-admin", Password = "Iam1!Password", TenantCode = "iam_" + h.Tenant.ToString("N")
        }));
        Assert.Equal(HttpStatusCode.OK, nativeLogin.StatusCode);
        var native = (await nativeLogin.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        var currentNative = native;
        if (scenario == "native-reuse")
        {
            using var rotated = await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken }));
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            currentNative = (await rotated.Content.ReadFromJsonAsync<TokenSessionDto>())!;
            await RequireDelivered(native.RefreshToken, currentNative.RefreshToken, "Windows");
        }

        var nativeJti = AccessJti(currentNative.AccessToken);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM public."CrmOidcGrant" WHERE "SourceJti"=CAST({nativeJti} AS bpchar)) AS "Value"
            """).SingleAsync());

        var forbidSeal = baseline || !exactAccess && !reuse || recovery is not null;
        if (forbidSeal)
        {
            await SetSealLogin(false);
            await using var witness = new NpgsqlConnection(seal.ConnectionString);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => witness.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refused.SqlState);
        }
        var before = await ReadState();
        Assert.Equal(new SourceRetirementState(scenario == "native-reuse" || noGrant is not null ? 5 : 4,
            noGrant is null ? 3 : 4, 0, 0, 4, 0, 0, 0), before);
        using var beforeAlias = await host.Profile(original["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, beforeAlias.StatusCode);
        var auditsBefore = await LogoutAudits();
        if (auditFault)
        {
            await h.Db.Database.ExecuteSqlRawAsync($"""
                CREATE FUNCTION public.w667_owned_logout_audit_rejection() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW."TenantId"='{h.Tenant:D}'::uuid AND NEW."UserId"='{h.ActorId:D}'::uuid AND NEW."EventType"={(int)SecurityEventType.Logout}
                  THEN RAISE EXCEPTION 'Owned best-effort logout audit rejection'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER w667_owned_logout_audit_rejection BEFORE INSERT ON public."Sys_SecurityLogs"
                  FOR EACH ROW EXECUTE FUNCTION public.w667_owned_logout_audit_rejection();
                """);
        }

        string? acceptedPlan = null;
        var originalRevision = recovery is null ? 0 : await CrmRevision();
        await using var restarted = recovery is null ? null : await RestartAfterRefusal();
        if (restarted is not null) host = restarted;
        using var result = order is not null ? await InPhysicalOrder()
            : recovery is not null ? await FinishRecovery() : await Act();

        Task<HttpResponseMessage> Act() => scenario switch
        {
            "web-access" => host.Send(Request(HttpMethod.Post, "api/auth/logout", NoWebRefresh(), new { })),
            "web-access-duplicate" => DuplicateAccessLogout(),
            "client-web-alias" => host.Send(Native("logout", new NativeLogoutRequest { Client = client }, original["cp6_at"])),
            "native-access" => host.Send(Native("logout", new NativeLogoutRequest { Client = client }, native.AccessToken)),
            "unknown-cookie" or "unknown-cookie-local" or "unknown-cookie-audit-fault" => host.Send(Request(HttpMethod.Post, "api/auth/logout",
                new Dictionary<string, string>(noGrant ?? original) { ["cp6_rt"] = CrmOidcCrypto.RandomToken() }, new { })),
            "web-reuse" => host.Send(Request(HttpMethod.Post, "api/auth/refresh", original, new { })),
            "native-reuse" => host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken })),
            "wrong-kind" => host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = original["cp6_rt"] })),
            "simultaneous-loser" => SimultaneousLoser(),
            "rejected-completion" => RejectedCompletion(),
            "ordinary-close" => OrdinaryClose(),
            _ => throw new InvalidOperationException("Expected an owned access/refresh scenario.")
        };
        var expectedError = scenario switch
        {
            "web-reuse" or "native-reuse" => "E-SEC-008",
            "wrong-kind" => "E-SEC-024",
            "simultaneous-loser" or "rejected-completion" => "E-SEC-007",
            "ordinary-close" => "REAUTH_REQUIRED",
            _ => null
        };
        if (recovery == "worker") expectedError = reuse ? "REAUTH_REQUIRED" : null;
        var expectedStatus = expectedError switch
        {
            "REAUTH_REQUIRED" => HttpStatusCode.Unauthorized,
            null => HttpStatusCode.OK,
            _ => HttpStatusCode.BadRequest
        };
        if (recovery == "worker") expectedStatus = HttpStatusCode.Unauthorized;
        Assert.Equal(expectedStatus, result.StatusCode);
        var errorMessage = expectedError is null ? null : await RequireNativeError(result, expectedError);
        var cleared = result.Headers.TryGetValues("Set-Cookie", out var headers) ? headers.Count() : 0;
        Assert.Equal(recovery != "worker" && (scenario is "web-access" or "web-access-duplicate" or "unknown-cookie" or
            "unknown-cookie-local" or "unknown-cookie-audit-fault") ? 4 : 0, cleared);

        var oldAccessDenied = reuse || exactAccess || scenario == "unknown-cookie";
        var currentNativeDenied = reuse || nativeClosed || scenario == "native-access";
        await RequireProfile(original["cp6_at"], !oldAccessDenied);
        await RequireProfile(current["cp6_at"], !reuse);
        await RequireProfile(other["cp6_at"], !reuse);
        await RequireProfile(currentNative.AccessToken, !currentNativeDenied);
        if (noGrant is not null) await RequireProfile(noGrant["cp6_at"], false);
        if (outsidePermit is not null)
        {
            await RequireProfile(outsideCookies!["cp6_at"], true);
            await RequireProjection(outsidePermit.Bearer, true);
            Assert.True(await GateCurrent(outsidePermit));
        }
        var after = await ReadState();
        var audits = await LogoutAudits() - auditsBefore;
        Assert.Equal((exactAccess && !auditFault || scenario is "native-access" or "unknown-cookie" or "unknown-cookie-local" or "ordinary-close") ? 1 : 0, audits);
        Assert.Equal(reuse ? 0 : nativeClosed || completionControlled ? before.ActiveRefresh - 1 : before.ActiveRefresh, after.ActiveRefresh);
        Assert.Equal(scenario == "simultaneous-loser" || completionControlled ? before.RefreshRows + 1 : before.RefreshRows, after.RefreshRows);
        Assert.Equal(exactAccess || scenario == "native-access" ? 1 : 0, after.RejectedAliases);
        Assert.Equal(reuse ? before.RefreshRows : nativeClosed || completionControlled ? 1 : 0, after.RejectedGenerations);
        Assert.Equal(before.CrmGrants, after.CrmGrants);
        var nativeGates = new bool[authorities.Length];
        for (var i = 0; i < authorities.Length; i++) nativeGates[i] = await GateCurrent(authorities[i]);

        if (baseline) Assert.Equal(before with { RefreshRows = after.RefreshRows, ActiveRefresh = after.ActiveRefresh,
            RejectedAliases = after.RejectedAliases, RejectedGenerations = after.RejectedGenerations }, after);
        Directory.CreateDirectory(proofDirectory);
        await File.WriteAllTextAsync(Path.Combine(proofDirectory, "source-retirement.json"), JsonSerializer.Serialize(new
        {
            Scenario = scenario, Baseline = baseline, Recovery = recovery, Order = order, Status = (int)result.StatusCode,
            Error = expectedError, ErrorMessage = errorMessage, Cleared = cleared, LogoutAudits = audits,
            Before = before, After = after, NativeGates = nativeGates,
            RefusedSealWitness = forbidSeal, Scope = new { SameJti = 2, SameFamilyLaterJti = 1, OtherFamily = 1, NativeGrant = false }
        }));

        for (var i = 0; i < nativeGates.Length; i++)
            Assert.True(nativeGates[i] == (i >= affected), $"Original CRM intake permission at source slot {i} has the wrong native disposition.");
        Assert.Equal((affected, affected, affected), (after.CrmRevoked, after.Snapshots, after.Outbox));
        if (affected > 0)
        {
            Assert.Null(await ReadOriginalPlan(completed: false));
            Assert.NotNull(await ReadOriginalPlan(completed: true));
        }
        if (recovery is not null)
        {
            Assert.Equal(originalRevision + affected * (recovery == "ack" ? 2 : 1), await CrmRevision());
            Assert.Equal(acceptedPlan, await ReadOriginalPlan(completed: true));
        }
        if (order is not null)
        {
            await Mark("paused");
            await WaitConfigurationBridge(proofDirectory, "finished");
        }

        Task<AuthenticationApiProcess> StartMain()
            => AuthenticationApiProcess.Start(main.ConnectionString, environment, allowAutoRedirect: false);

        async Task GrantReadWitness()
        {
            using var quoter = new NpgsqlCommandBuilder();
            var runtimeRole = quoter.QuoteIdentifier(reader.Username!);
            await using var grant = new NpgsqlCommand($"""
                GRANT USAGE ON SCHEMA crm_identity_lock_v1,crm_inquiry_tenant_pause_v1 TO {runtimeRole};
                GRANT EXECUTE ON FUNCTION crm_identity_lock_v1.hold_v1(uuid),
                  crm_inquiry_tenant_pause_v1.identity_current_v1(uuid,text,integer,text) TO {runtimeRole};
                """, crm);
            await grant.ExecuteNonQueryAsync();
        }

        async Task SetSealLogin(bool enabled)
        {
            await using var command = new NpgsqlCommand(enabled ? "ALTER ROLE w667_main_seal LOGIN" : "ALTER ROLE w667_main_seal NOLOGIN", crm);
            await command.ExecuteNonQueryAsync();
        }

        async Task<long> CrmRevision()
        {
            await using var command = new NpgsqlCommand("""SELECT "Revision" FROM crm_auth."IdentityFenceV1" WHERE "TenantId"=@tenant""", crm);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        Task<string?> ReadOriginalPlan(bool completed) => h.Db.Database.SqlQuery<string?>($"""
            SELECT jsonb_build_object('operation',operation_id,'store',store_id,'jti',source_jti,'source',refresh_hash,
                'replacement',replacement_hash,'audit',access_audit,'grants',grant_plan)::text AS "Value"
            FROM cp6_authentication_v1.crm_auth_source_retirements_v1
            WHERE tenant_id={h.Tenant} AND subject_id={h.ActorId} AND (completed_at_utc IS NOT NULL)={completed}
            """).SingleOrDefaultAsync();

        async Task<AuthenticationApiProcess> RestartAfterRefusal()
        {
            using (var refused = await Act()) await RequireUnknown(refused);
            acceptedPlan = await ReadOriginalPlan(completed: false);
            Assert.NotNull(acceptedPlan);
            if (scenario == "native-reuse")
            {
                using var competing = await host.Send(Request(HttpMethod.Post, "api/auth/refresh", original, new { }));
                await RequireUnknown(competing);
                Assert.Equal(acceptedPlan, await ReadOriginalPlan(completed: false));
            }
            foreach (var privateValue in new[] { original["cp6_at"], original["cp6_rt"], current["cp6_rt"],
                native.RefreshToken, currentNative.RefreshToken, "Iam1!Password" })
                Assert.False(acceptedPlan.Contains(privateValue, StringComparison.Ordinal), "The original credential must stay outside the accepted plan.");
            using (var proof = JsonDocument.Parse(acceptedPlan))
            {
                var plan = proof.RootElement;
                var source = scenario == "native-reuse" ? native.RefreshToken : original["cp6_rt"];
                Assert.Equal(AuthenticationInteractionStoreV1.Hash(source), plan.GetProperty("source").GetString());
                Assert.Equal(exactAccess ? first.Jti : null, plan.GetProperty("jti").GetString());
                Assert.Equal(reuse ? AuthenticationInteractionStoreV1.Hash(scenario == "native-reuse" ? currentNative.RefreshToken : current["cp6_rt"])
                    : null, plan.GetProperty("replacement").GetString());
                Assert.Equal(store, plan.GetProperty("store").GetGuid());
                Assert.Equal(authorities.Take(affected).Select(permit => permit.GrantId).Order(),
                    plan.GetProperty("grants").EnumerateArray().Select(item => item.GetProperty("GrantId").GetGuid()).Order());
            }
            Assert.Equal(before, await ReadState());
            Assert.Equal(auditsBefore, await LogoutAudits());
            Assert.Equal(originalRevision, await CrmRevision());
            await RequirePendingAdmission();
            var pid = host.ProcessId;
            await host.StopAsync();
            if (recovery == "worker") environment["Startup__SkipHostedServices"] = "false";
            var next = await StartMain();
            Assert.NotEqual(pid, next.ProcessId);
            return next;
        }

        async Task<HttpResponseMessage> FinishRecovery()
        {
            Assert.Equal(acceptedPlan, await ReadOriginalPlan(completed: false));
            Assert.Equal(before, await ReadState());
            using (var retry = await Act()) await RequireUnknown(retry);
            Assert.Equal(acceptedPlan, await ReadOriginalPlan(completed: false));
            await RequirePendingAdmission();
            await SetSealLogin(true);
            if (recovery == "worker")
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(32));
                while (await ReadOriginalPlan(completed: false) is not null) await Task.Delay(80, deadline.Token);
                return await Act();
            }

            crmRelay!.Arm();
            using (var unknown = await Act())
            {
                await crmRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
                await RequireUnknown(unknown);
            }
            Assert.Equal(before, await ReadState());
            Assert.Equal(acceptedPlan, await ReadOriginalPlan(completed: false));
            Assert.Equal(originalRevision + affected, await CrmRevision());
            for (var i = 0; i < authorities.Length; i++)
                Assert.Equal(i >= affected, await GateCurrent(authorities[i]));

            await using var barrier = new NpgsqlConnection(crmObserver);
            await barrier.OpenAsync();
            await using var held = await barrier.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SELECT crm_identity_lock_v1.hold_v1(@tenant)", barrier, held))
            {
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            var finishing = Act();
            try
            {
                await WaitForMainBlock(crm, barrier.ProcessID, "%advance_identity_v1%", finishing);
                mainRelay!.Arm();
            }
            finally { await held.RollbackAsync(); }
            var result = await finishing;
            await mainRelay!.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
            return result;
        }

        async Task RequirePendingAdmission()
        {
            await RequireProfile(original["cp6_at"], true);
            for (var i = 0; i < authorities.Length; i++)
                await RequireProjection(authorities[i].Bearer, i >= affected);
            await RequireProjection(outsidePermit!.Bearer, true);
            await RequireProfile(outsideCookies!["cp6_at"], true);
            // Pair the original signed JTI with the active R1 cookie from the same original family.
            var cookies = new Dictionary<string, string>(current) { ["cp6_at"] = original["cp6_at"] };
            var response = await ReadAuthorizeRedirect(cookies, CrmOidcCrypto.RandomToken());
            var query = QueryHelpers.ParseQuery(response.Query);
            Assert.False(query.ContainsKey("code"));
            Assert.Equal("login_required", query["error"].ToString());
        }

        async Task RequireUnknown(HttpResponseMessage unknown)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
            Assert.False(unknown.Headers.Contains("Set-Cookie"));
            if (!reuse)
            {
                await RequireNativeError(unknown, "ACCESS_LOGOUT_OUTCOME_UNKNOWN");
                return;
            }
            var envelope = await unknown.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN", envelope.GetProperty("code").GetString());
        }

        async Task SendOriginalBridge()
        {
            var sources = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant)
                .OrderBy(row => row.AggregateId).ToArrayAsync();
            Directory.CreateDirectory(proofDirectory);
            await File.WriteAllTextAsync(Path.Combine(proofDirectory, "session.json"), JsonSerializer.Serialize(new
            {
                marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = signing.Issuer, origin = host.Origin.ToString(),
                accessToken = first.Bearer, idToken = first.Identity, tenantId = h.Tenant, subjectId = h.ActorId,
                departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId,
                keyId = signing.KeyId, publicKeyBase64 = signing.PublicKeyBase64, activeFromUtc = signing.ActiveFromUtc,
                sourceRows = sources.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson }),
                authenticationEpoch
            }));
        }

        async Task<HttpResponseMessage> InPhysicalOrder()
        {
            if (order == "pause-first")
            {
                await WaitConfigurationBridge(proofDirectory, "permission-held");
                return await Act();
            }
            await WaitConfigurationBridge(proofDirectory, "owner-held");
            var retiring = Act();
            try
            {
                await using var waiting = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
                      AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                      AND cardinality(pg_blocking_pids(pid)) > 0)
                    """, crm);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (!(bool)(await waiting.ExecuteScalarAsync(timeout.Token))!)
                {
                    Assert.False(retiring.IsCompleted, "The source retirement must wait for the actual CRM writer.");
                    await Task.Delay(25, timeout.Token);
                }
                Assert.Equal(before, await ReadState());
            }
            finally { await Mark("release-owner"); }
            return await retiring;
        }

        async Task Mark(string stage)
        {
            var location = Path.Combine(proofDirectory, stage);
            await File.WriteAllTextAsync(location + ".tmp", "PASS");
            File.Move(location + ".tmp", location);
        }

        static string AccessJti(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).Id;

        static async Task<string> RequireNativeError(HttpResponseMessage response, string key)
        {
            var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((int)response.StatusCode, envelope.GetProperty("code").GetInt32());
            var message = envelope.GetProperty("message").GetString()!;
            var translation = I18nSecScreenSeed.Items.Concat(I18nSecSsoScreenSeed.Items).SingleOrDefault(item => item.LangKey == key);
            var contract = translation is null ? new[] { key } : [key, translation.ZhCN, translation.ZhTW, translation.En, translation.Ja, translation.Ko];
            Assert.Contains(message, contract);
            return message;
        }

        static Dictionary<string, string> Cookies(HttpResponseMessage reply) => reply.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0].Split('=', 2)).Where(pair => pair[1].Length > 0)
            .ToDictionary(pair => pair[0], pair => pair[1]);

        Dictionary<string, string> NoWebRefresh() => new() { ["cp6_at"] = original["cp6_at"], ["cp6_csrf"] = original["cp6_csrf"] };

        static HttpRequestMessage Native(string action, object body, string? bearer = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/client-auth/" + action) { Content = JsonContent.Create(body) };
            if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
            return request;
        }

        async Task<Dictionary<string, string>> WebLogin(string userName = "iam-admin")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new { UserName = userName, Password = "Iam1!Password", TenantCode = "iam_" + h.Tenant.ToString("N") })
            };
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Cookies(response);
        }

        async Task<Uri> ReadAuthorizeRedirect(Dictionary<string, string> cookies, string verifier)
        {
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "openid profile crm",
                ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
                ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken()
            });
            using var entry = await host.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var authorization = await host.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            return authorization.Headers.Location!;
        }

        async Task<SourceRetirementPermit> Authorize(Dictionary<string, string> cookies, Guid? actor = null)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var location = await ReadAuthorizeRedirect(cookies, verifier);
            var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
            Assert.NotEmpty(code);
            using var swap = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            swap.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var issued = await host.Send(swap);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            var token = body.RootElement.GetProperty("access_token").GetString()!;
            var idToken = body.RootElement.GetProperty("id_token").GetString()!;
            var grantId = GrantId(token);
            var subject = actor ?? h.ActorId;
            var sourceJti = AccessJti(cookies["cp6_at"]);
            Assert.False(grantId.ToString("D") == sourceJti, "CRM grant identity and the source JWT's JTI are distinct.");
            var sourceHash = AuthenticationInteractionStoreV1.Hash(cookies["cp6_rt"]);
            var family = await h.Db.Database.SqlQuery<Guid>($"""
                SELECT r."BrowserSessionId" AS "Value" FROM public."CrmOidcGrant" g
                JOIN public."Sys_RefreshTokens" r ON g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                JOIN cp6_authentication_v1.access_sessions a ON a.refresh_hash=r."TokenHash"
                WHERE g."Id"={grantId} AND g."OrganizationId"={h.Tenant} AND g."SubjectId"={subject}
                  AND g."SourceJti"=CAST({sourceJti} AS bpchar) AND a.access_jti={sourceJti} AND r."TokenHash"={sourceHash}
                  AND r."TenantId"={h.Tenant} AND r."UserId"={subject}
                """).SingleAsync();
            var json = await RequireProjection(token, true);
            var userSource = json["userSource"]!;
            Assert.Equal(h.Tenant, json["organizationId"]!.GetValue<Guid>());
            Assert.Equal(subject, json["subjectId"]!.GetValue<Guid>());
            return new(grantId, sourceJti, family, userSource["aggregateId"]!.GetValue<string>(), userSource["version"]!.GetValue<int>(),
                IdentityEventContracts.TokenAggregate(signing.Issuer, grantId.ToString("D")), json["expiresAtUtc"]!.GetValue<DateTimeOffset>(), token, idToken);
        }

        async Task<JsonObject> RequireProjection(string token, bool expectedFence)
        {
            using var context = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1("intake:reject", pair.SiteId, pair.FormId, ServiceNonce()))
            };
            context.Headers.Authorization = new("Bearer", token);
            context.Headers.Add("X-Forwarded-Proto", "https");
            using var signed = await host.Send(context);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.Equal(expectedFence, signed.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var fence));
            if (expectedFence) Assert.StartsWith(store.ToString("D") + "/", fence!.Single());
            var json = JsonNode.Parse(await signed.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(json, signing.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            return json;
        }

        async Task RequireDelivered(string parent, string replacement, string clientKind)
        {
            var parentHash = AuthenticationInteractionStoreV1.Hash(parent);
            var nextHash = AuthenticationInteractionStoreV1.Hash(replacement);
            Assert.True(await h.Db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM public."Sys_RefreshTokens" p
                    JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=p."ReplacedByTokenHash"
                    JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=r."TokenHash"
                    WHERE p."TokenHash"={parentHash} AND r."TokenHash"={nextHash} AND s.parent_refresh_hash=p."TokenHash"
                      AND p."TenantId"={h.Tenant} AND p."UserId"={h.ActorId} AND r."ClientKind"={clientKind}
                      AND p."RevokedAt" IS NOT NULL AND r."RevokedAt" IS NULL AND NOT r."CompletionPending"
                      AND r."CompletionRejectedAtUtc" IS NULL AND s.rejected_at IS NULL) AS "Value"
                """).SingleAsync());
        }

        async Task RequireProfile(string token, bool allowed)
        {
            using var currentProfile = await host.Profile(token);
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, currentProfile.StatusCode);
        }

        async Task<bool> GateCurrent(SourceRetirementPermit permit)
        {
            Assert.True(permit.ExpiresAt > DateTimeOffset.UtcNow);
            await using var witness = new NpgsqlConnection(reader.ConnectionString);
            await witness.OpenAsync();
            await using var transaction = await witness.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SET TRANSACTION READ ONLY; SELECT crm_identity_lock_v1.hold_v1(@tenant)", witness, transaction))
            {
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            await using var read = new NpgsqlCommand("SELECT crm_inquiry_tenant_pause_v1.identity_current_v1(@tenant,@source,@version,@session)", witness, transaction);
            read.Parameters.AddWithValue("tenant", h.Tenant);
            read.Parameters.AddWithValue("source", permit.UserAggregate);
            read.Parameters.AddWithValue("version", permit.UserVersion);
            read.Parameters.AddWithValue("session", permit.SessionAggregate);
            var active = (bool)(await read.ExecuteScalarAsync())!;
            await transaction.CommitAsync();
            return active;
        }

        async Task<HttpResponseMessage> RejectedCompletion()
        {
            var service = new RefreshTokenService(h.Db, Options.Create(new SecurityOptions()), new TenantContext { CurrentTenantId = h.Tenant });
            var (replacement, _) = await service.RotateForCompletionAsync(native.RefreshToken, null, null, new(client.ClientKind, client.DeviceId, client.AppVersion));
            var replacementHash = AuthenticationInteractionStoreV1.Hash(replacement);
            Assert.True(await h.Db.Sys_RefreshTokens.AsNoTracking().Where(r => r.TokenHash == replacementHash)
                .Select(r => r.CompletionPending).SingleAsync());
            var refresh = new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken };
            using (var pending = await host.Send(Native("refresh", refresh)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, pending.StatusCode);
                await RequireNativeError(pending, "E-SEC-007");
            }
            await service.RejectCompletionAsync(replacement);
            return await host.Send(Native("refresh", refresh));
        }

        async Task<HttpResponseMessage> OrdinaryClose()
        {
            using var closed = await host.Send(Native("logout", new NativeLogoutRequest { Client = client, RefreshToken = native.RefreshToken }, native.AccessToken));
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            return await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken }));
        }

        async Task<HttpResponseMessage> SimultaneousLoser()
        {
            var replies = await WaitTwoRequests(() => Request(HttpMethod.Post, "api/auth/refresh", current, new { }));
            using var winner = Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
            var failed = Assert.Single(replies, r => r.StatusCode != HttpStatusCode.OK);
            await RequireDelivered(current["cp6_rt"], Cookies(winner)["cp6_rt"], "Web");
            return failed;
        }

        async Task<HttpResponseMessage> DuplicateAccessLogout()
        {
            var replies = await WaitTwoRequests(() => Request(HttpMethod.Post, "api/auth/logout", NoWebRefresh(), new { }));
            using var firstReply = replies[0];
            Assert.Equal(HttpStatusCode.OK, firstReply.StatusCode);
            return replies[1];
        }

        async Task<HttpResponseMessage[]> WaitTwoRequests(Func<HttpRequestMessage> request)
        {
            await using var main = new NpgsqlConnection(h.Db.Database.GetConnectionString());
            await main.OpenAsync();
            await using var lockRoot = await main.BeginTransactionAsync();
            await using (var held = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire()", main, lockRoot))
                await held.ExecuteNonQueryAsync();
            using var left = request();
            using var right = request();
            var two = new[] { host.Send(left), host.Send(right) };
            try
            {
                await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
                await observer.OpenAsync();
                await using var waiting = new NpgsqlCommand("""
                    SELECT count(*)::int FROM pg_stat_activity
                    WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%'
                      AND cardinality(pg_blocking_pids(pid)) > 0
                    """, observer);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while ((int)(await waiting.ExecuteScalarAsync(deadline.Token))! < 2) await Task.Delay(25, deadline.Token);
            }
            finally { await lockRoot.RollbackAsync(); }
            return await Task.WhenAll(two);
        }

        async Task<SourceRetirementState> ReadState()
        {
            var value = await h.Db.Database.SqlQuery<string>($"""
                WITH refresh AS (SELECT "TokenHash","RevokedAt" FROM public."Sys_RefreshTokens"
                    WHERE "TenantId"={h.Tenant} AND "UserId"={h.ActorId}),
                grants AS (SELECT "Id","Revoked" FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant} AND "SubjectId"={h.ActorId})
                SELECT json_build_object('RefreshRows',(SELECT count(*) FROM refresh),
                    'ActiveRefresh',(SELECT count(*) FROM refresh WHERE "RevokedAt" IS NULL),
                    'RejectedAliases',(SELECT count(*) FROM cp6_authentication_v1.access_sessions a JOIN refresh r ON a.refresh_hash=r."TokenHash" WHERE a.revoked_at IS NOT NULL),
                    'RejectedGenerations',(SELECT count(*) FROM cp6_authentication_v1.session_evidence s JOIN refresh r ON s.refresh_hash=r."TokenHash" WHERE s.rejected_at IS NOT NULL),
                    'CrmGrants',(SELECT count(*) FROM grants),'CrmRevoked',(SELECT count(*) FROM grants WHERE "Revoked"),
                    'Snapshots',(SELECT count(*) FROM crm_identity."Snapshot" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'),
                    'Outbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<SourceRetirementState>(value)!;
        }

        Task<int> LogoutAudits() => h.Db.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(row =>
            row.TenantId == h.Tenant && row.UserId == h.ActorId && row.EventType == (int)SecurityEventType.Logout);
    }

    private sealed record SourceRetirementPermit(Guid GrantId, string Jti, Guid Family, string UserAggregate, int UserVersion,
        string SessionAggregate, DateTimeOffset ExpiresAt, string Bearer, string Identity)
    {
        public override string ToString() => nameof(SourceRetirementPermit);
    }
    private sealed record SourceRetirementState(int RefreshRows, int ActiveRefresh, int RejectedAliases, int RejectedGenerations,
        int CrmGrants, int CrmRevoked, int Snapshots, int Outbox);
}

internal sealed class Pgt017AccessAndRefreshFactAttribute : FactAttribute
{
    public Pgt017AccessAndRefreshFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE") is null || Environment.GetEnvironmentVariable("CP6_W667_SCENARIO") is null)
            Skip = "Requires the owned Main and CRM PostgreSQL source-retirement verification.";
    }
}
