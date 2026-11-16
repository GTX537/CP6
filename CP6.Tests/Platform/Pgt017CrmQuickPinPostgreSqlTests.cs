using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_quick_pin_keeps_original_Wms_authority_and_retires_Crm_grants()
    {
        const string route = "api/v2/admin/client-devices/quick-pin";
        const string actorName = "w671-device-manager";
        const string badge = "w671-issued-badge";
        const string originalBadge = "w671-original-badge";
        const string redirect = "https://crm.cp6.test/callback";
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        var order = Environment.GetEnvironmentVariable("CP6_PGT017_ORDER");
        var ordered = order is "owner-first" or "pause-first";
        var storeId = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);

        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await CopyRole71ForIndependentInquiry(h);
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(row => row.MenuKey == "wms-mobile");
        if (menu is null)
        {
            menu = new Sys_Menu { MenuId = 461, MenuKey = "wms-mobile", MenuName = "Mobile" };
            h.Db.Sys_Menus.Add(menu);
        }
        if (!await h.Db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == "device-manage"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "device-manage", ActionName = "Device management" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "device-manage" });

        var hasher = new BCryptPasswordHasher(4);
        var password = TestPassword();
        var managerPassword = TestPassword();
        var pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var alternatePin = (int.Parse(pin, CultureInfo.InvariantCulture) == 999999 ? 999998 : 999999).ToString(CultureInfo.InvariantCulture);
        var originalPinHash = hasher.Hash(alternatePin);
        var target = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        target.Password = hasher.Hash(password);
        target.AuthenticationEpoch = Guid.NewGuid();
        target.BadgeNo = originalBadge;
        target.QuickPinHash = originalPinHash;
        var actor = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = actorName, Enable = true, DeptId = h.Parent, RoleId = 72,
            Password = hasher.Hash(managerPassword), AuthenticationEpoch = Guid.NewGuid()
        };
        h.Db.Sys_Users.Add(actor);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var options = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        if (!ordered) await InstallUserSealFixture(crm, seal, h.Tenant, storeId);
        var directMain = h.Db.Database.GetConnectionString()!;
        var environment = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        environment["Security__Password__BcryptWorkFactor"] = "4";
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var registration = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[registration + "ConnectionString"] = seal.ConnectionString;
        environment[registration + "StoreId"] = storeId.ToString("D");
        Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(directMain, environment, allowAutoRedirect: false);

        await using var host = await Start();
        var targetCookies = await SignIn(host, target.UserName, password, $"iam_{h.Tenant:N}");
        var actorCookies = await SignIn(host, actorName, managerPassword, $"iam_{h.Tenant:N}");
        var oldGrant = await Exchange(targetCookies);
        var independentGrant = await Exchange(actorCookies);
        var grantId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(oldGrant.Access).Id);
        var tokenAggregate = IdentityEventContracts.TokenAggregate(options.Issuer, grantId.ToString("D"));
        var userAggregate = $"user:{target.Id:D}";
        var expectedFence = $"{storeId:D}/1";
        Task<HttpResponseMessage> SetPin(AuthenticationApiProcess api, string secret)
            => api.Send(Request(HttpMethod.Post, route, actorCookies,
                new SetWarehouseQuickPinRequest { UserName = target.UserName, BadgeNo = $" {badge} ", Pin = secret }));

        var before = await ReadFacts();
        Assert.False(before.GrantRevoked);
        Assert.True(before.OriginalPin);
        Assert.Equal(originalBadge, before.Badge);
        Assert.Equal(expectedFence, await Sign(host, oldGrant.Access, target.Id, "intake:reject"));
        Assert.Equal(expectedFence, await Sign(host, independentGrant.Access, actor.Id, "intake:reject"));
        if (ordered)
        {
            await CheckCrmOrder();
            return;
        }

        await SetSealLogin(false);
        try
        {
            await using (var rejectedLogin = new NpgsqlConnection(seal.ConnectionString))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => rejectedLogin.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, error.SqlState);
            }
            var denials = await DeniedConnections();
            using var refused = await SetPin(host, pin);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            var envelope = await FailureEnvelope(refused);
            Assert.Equal(503, envelope.Code);
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", envelope.Authority);
            var routeDenials = await DeniedConnections() - denials;
            Assert.Equal(1, routeDenials);
            Assert.Equal(before, await ReadFacts());
            Assert.Equal(0, await Floor(tokenAggregate));
            var original = await ReadIntent();
            Assert.False(original.Started);
            Assert.False(await HasReceipt(original.Operation));

            Assert.Null(await Sign(host, oldGrant.Access, target.Id, "intake:reject"));
            Assert.Null(await Sign(host, oldGrant.Access, target.Id, "intake:list"));
            Assert.Equal(expectedFence, await Sign(host, independentGrant.Access, actor.Id, "intake:reject"));
            using (var stillCurrent = await host.Profile(targetCookies["cp6_at"]))
                Assert.Equal(HttpStatusCode.OK, stillCurrent.StatusCode);
            await Save("quick-pin-cold.json", new { h.Tenant, Store = storeId, Actor = actor.Id, Target = target.Id, Grant = grantId,
                Code = (int)refused.StatusCode, FenceLoginSqlState = "28000", RouteDenials = routeDenials, Original = original,
                Before = before, OriginalRead = true, IndependentFence = true });

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var restarted = await Start();
            Assert.NotEqual(pid, restarted.ProcessId);
            Assert.Equal(original, await ReadIntent());
            Assert.Null(await Sign(restarted, oldGrant.Access, target.Id, "intake:reject"));
            Assert.Equal(expectedFence, await Sign(restarted, independentGrant.Access, actor.Id, "intake:reject"));
            using var different = await SetPin(restarted, alternatePin);
            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(original, await ReadIntent());
            Assert.Equal(before, await ReadFacts());
            Assert.Equal(denials + routeDenials, await DeniedConnections());

            await SetSealLogin(true);
            await using var holder = new NpgsqlConnection(directMain);
            await using var observer = new NpgsqlConnection(directMain);
            await holder.OpenAsync();
            await observer.OpenAsync();
            await using var barrier = await holder.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Users\" WHERE \"Id\"=@target AND \"TenantId\"=@tenant FOR UPDATE", holder, barrier);
            hold.Parameters.AddWithValue("target", target.Id);
            hold.Parameters.AddWithValue("tenant", h.Tenant);
            Assert.Equal(1, await hold.ExecuteScalarAsync());
            var finishing = SetPin(restarted, pin);
            Task<string?>? independent = null;
            try
            {
                var writer = await WaitForMainBlock(observer, holder.ProcessID, "%UPDATE%Sys_Users%", finishing);
                independent = Sign(restarted, independentGrant.Access, actor.Id, "intake:reject");
                var signer = await WaitForMainBlock(observer, writer, "%cp6_authentication_v1.acquire()%", finishing);
                Assert.Equal(before, await ReadFacts());
                await Save("quick-pin-main-root.json", new { Holder = holder.ProcessID, Writer = writer, Signer = signer });
            }
            finally { await barrier.RollbackAsync(); }
            using var committed = await finishing;
            if (independent is not null) Assert.Equal(expectedFence, await independent);
            var after = await CheckCommitted(restarted, committed);
            Assert.True(await HasReceipt(original.Operation));
            Assert.False(await IamCrmUserSealV1.ExistsAsync(h.Db, h.Tenant, target.Id, default));
            Assert.Equal(original.Advance.Version, await Floor(tokenAggregate));
            var source = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(row => row.TenantId == h.Tenant && row.AggregateId == tokenAggregate);
            Assert.Equal((original.Advance.Version, original.Advance.SuccessorType, original.Advance.IntentHash),
                (source.Version, source.EventType, source.PayloadSha256));
            await Save("quick-pin-recovered.json", new { RestartPid = restarted.ProcessId, Original = original, Receipt = true, After = after });
        }
        finally { await SetSealLogin(true); }

        async Task CheckCrmOrder()
        {
            var sources = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant).OrderBy(row => row.AggregateId).ToArrayAsync();
            Directory.CreateDirectory(bridge);
            var handoff = Path.Combine(bridge, "session.json");
            var temporary = handoff + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
                {
                    marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = options.Issuer, origin = host.Origin.ToString(),
                    accessToken = oldGrant.Access, idToken = oldGrant.Identity, tenantId = h.Tenant, subjectId = target.Id,
                    departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId, keyId = options.KeyId,
                    publicKeyBase64 = options.PublicKeyBase64, activeFromUtc = options.ActiveFromUtc, authenticationEpoch = target.AuthenticationEpoch,
                    sourceRows = sources.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson })
                }));
                File.Move(temporary, handoff);
                await WaitConfigurationBridge(bridge, "ready");
                var crmFirst = order == "owner-first";
                await WaitConfigurationBridge(bridge, crmFirst ? "owner-held" : "permission-held");
                var writing = SetPin(host, pin);
                int? waitingPid = null;
                if (crmFirst)
                {
                    try
                    {
                        await using var waiting = new NpgsqlCommand("""
                            SELECT pid FROM pg_stat_activity WHERE datname=current_database()
                              AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                              AND cardinality(pg_blocking_pids(pid))>0
                            """, crm);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        while (waitingPid is null)
                        {
                            waitingPid = (int?)await waiting.ExecuteScalarAsync(timeout.Token);
                            if (waitingPid is not null) break;
                            Assert.False(writing.IsCompleted, "The native CRM lock must serialize the actual QuickPIN write.");
                            await Task.Delay(25, timeout.Token);
                        }
                        Assert.Equal(before, await ReadFacts());
                    }
                    finally { await Mark("release-owner"); }
                }
                using var response = await writing;
                var after = await CheckCommitted(host, response);
                await Mark("paused");
                await WaitConfigurationBridge(bridge, "finished");
                Assert.Equal(1, await Floor(tokenAggregate));
                await Save("quick-pin-order.json", new { Route = route, Order = order, WaitingMainSealPid = waitingPid,
                    h.Tenant, Target = target.Id, Actor = actor.Id, Grant = grantId, Before = before, After = after });
            }
            finally { File.Delete(handoff); File.Delete(temporary); }
        }

        async Task<QuickPinFacts> CheckCommitted(AuthenticationApiProcess api, HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, body.RootElement.GetProperty("code").GetInt32());
            Assert.Equal("OK", body.RootElement.GetProperty("message").GetString());
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var after = await ReadFacts();
            Assert.True(after.NewPin);
            Assert.False(after.OriginalPin);
            Assert.Equal(badge, after.Badge);
            Assert.Equal(actorName, after.Modifier);
            Assert.NotEqual(before.Revision, after.Revision);
            Assert.Equal(before.Epoch, after.Epoch);
            Assert.True(after.OriginalPassword);
            Assert.Equal(before.RefreshState, after.RefreshState);
            Assert.Equal(before.UserSource, after.UserSource);
            Assert.True(after.GrantRevoked);
            Assert.Equal(1, after.TokenVersion);
            Assert.Equal(before.Outbox + 1, after.Outbox);
            using (var stale = await api.Profile(targetCookies["cp6_at"]))
                Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.Equal(expectedFence, await Sign(api, independentGrant.Access, actor.Id, "intake:reject"));
            return after;
        }

        async Task<QuickPinFacts> ReadFacts()
        {
            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.TenantId == h.Tenant && row.Id == target.Id);
            var grantRevoked = await h.Db.Database.SqlQuery<bool>($"SELECT \"Revoked\" AS \"Value\" FROM public.\"CrmOidcGrant\" WHERE \"OrganizationId\"={h.Tenant} AND \"SubjectId\"={target.Id} AND \"Id\"={grantId}").SingleAsync();
            var snapshots = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant &&
                (row.AggregateId == userAggregate || row.AggregateId == tokenAggregate)).ToArrayAsync();
            var source = snapshots.Single(row => row.AggregateId == userAggregate);
            var outbox = await h.Db.Database.SqlQuery<int>($"""
                SELECT ((SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId" IN ({userAggregate},{tokenAggregate}))
                    +(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId" IN ({userAggregate},{tokenAggregate})))::integer AS "Value"
                """).SingleAsync();
            var refresh = await h.Db.Database.SqlQuery<string>($"""
                SELECT coalesce(jsonb_agg(jsonb_build_array("Id","RevokedAt") ORDER BY "Id"),'[]'::jsonb)::text AS "Value"
                  FROM public."Sys_RefreshTokens" WHERE "TenantId"={h.Tenant} AND "UserId"={target.Id}
                """).SingleAsync();
            return new(user.CredentialRevision, user.AuthenticationEpoch, user.BadgeNo, user.Modifier, user.ModifyDate,
                user.QuickPinHash == originalPinHash, user.QuickPinHash is { } encoded && hasher.Verify(pin, encoded),
                user.Password == target.Password, grantRevoked, $"{source.Version}/{source.PayloadSha256}",
                snapshots.SingleOrDefault(row => row.AggregateId == tokenAggregate)?.Version ?? 0, outbox, refresh);
        }

        async Task<QuickPinIntent> ReadIntent()
        {
            var intent = await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, default, target.Id);
            Assert.NotNull(intent);
            Assert.Equal("wms-set-quick-pin", intent.Kind);
            Assert.Equal(actor.Id, intent.ActorId);
            Assert.Equal(storeId, intent.StoreId);
            Assert.False(intent.RequestProtected.Contains(pin, StringComparison.Ordinal), "The stored plan must not record a plaintext PIN.");
            var advance = Assert.Single(intent.ReadAdvances());
            Assert.Equal(tokenAggregate, advance.Aggregate);
            Assert.Equal(1, advance.Version);
            return new(intent.OperationId, intent.SealStarted, advance);
        }

        Task<bool> HasReceipt(Guid operation) => h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.iam_operations
                WHERE tenant_id={h.Tenant} AND operation_id={operation} AND actor_id={actor.Id}
                  AND kind='wms-set-quick-pin' AND state='COMMITTED' AND result_json::jsonb->>'EntityId'={target.Id.ToString("D")}) AS "Value"
            """).SingleAsync();

        async Task<int> Floor(string aggregate)
        {
            await using var read = new NpgsqlCommand("""
                SELECT coalesce((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.identity_fences
                    WHERE tenant_id=@tenant AND aggregate_id=@aggregate),0)
                """, crm);
            read.Parameters.AddWithValue("tenant", h.Tenant);
            read.Parameters.AddWithValue("aggregate", aggregate);
            return (int)(await read.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool active)
        {
            using var quote = new NpgsqlCommandBuilder();
            await using var sql = new NpgsqlCommand($"ALTER ROLE {quote.QuoteIdentifier(seal.Username!)} {(active ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        async Task<int> DeniedConnections()
        {
            var log = Environment.GetEnvironmentVariable("CP6_W671_CRM_SERVER_LOG")!;
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var count = 0;
            while (await reader.ReadLineAsync() is { } line)
                if (line.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal)) count++;
            return count;
        }

        async Task<string?> Sign(AuthenticationApiProcess api, string bearer, Guid subject, string purpose)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var claims = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(claims, options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            Assert.Equal(subject, claims["subjectId"]!.GetValue<Guid>());
            Assert.Equal(purpose, claims["purpose"]!.GetValue<string>());
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        async Task<(string Access, string Identity)> Exchange(Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["code_challenge_method"] = "S256",
                ["code_challenge"] = CrmOidcCrypto.Challenge(verifier), ["state"] = CrmOidcCrypto.RandomToken(),
                ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm"
            });
            using var entry = await host.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var accepted = await host.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
            var code = QueryHelpers.ParseQuery(accepted.Headers.Location!.Query)["code"].ToString();
            Assert.False(string.IsNullOrWhiteSpace(code));
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var response = await host.Send(exchange);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (result.RootElement.GetProperty("access_token").GetString()!, result.RootElement.GetProperty("id_token").GetString()!);
        }

        Task Save(string file, object value) => File.WriteAllTextAsync(Path.Combine(bridge, file), JsonSerializer.Serialize(value));

        async Task Mark(string file)
        {
            var path = Path.Combine(bridge, file);
            await File.WriteAllTextAsync(path + ".tmp", "PASS");
            File.Move(path + ".tmp", path);
        }
    }

    private sealed record QuickPinIntent(Guid Operation, bool Started, CrmUserSealAdvanceV1 Advance);

    private sealed record QuickPinFacts(Guid Revision, Guid Epoch, string? Badge, string? Modifier, DateTime? Modified,
        bool OriginalPin, bool NewPin, bool OriginalPassword, bool GrantRevoked, string UserSource, int TokenVersion, int Outbox, string RefreshState);
}
