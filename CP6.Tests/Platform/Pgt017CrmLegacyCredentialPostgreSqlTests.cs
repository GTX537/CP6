using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OtpNet;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_legacy_credential_mutations_preserve_the_current_user_crm_seal()
    {
        var mode = Environment.GetEnvironmentVariable("CP6_W666_CREDENTIAL_MODE");
        var ordered = Environment.GetEnvironmentVariable("CP6_W666_SCENARIO") == "orders";
        var passwordChange = mode is "password" or "restricted";
        var restricted = mode == "restricted";
        var impersonated = mode == "impersonated-reset";
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await GrantHttpPermissions(h);
        await CopyRole71ForIndependentInquiry(h);

        var oldPassword = TestPassword();
        var newPassword = TestPassword();
        var historyPassword = TestPassword();
        var managerPassword = TestPassword();
        var hasher = new BCryptPasswordHasher(4);
        var originalHash = hasher.Hash(oldPassword);
        var factorSecret = passwordChange ? null : Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));
        var subject = await h.Db.Sys_Users.SingleAsync(user => user.Id == h.ActorId);
        subject.Password = originalHash;
        subject.AuthenticationEpoch = Guid.NewGuid();
        subject.MustChangePassword = restricted;
        subject.TwoFactorEnabled = !passwordChange;
        subject.TwoFactorSecret = factorSecret;
        subject.TwoFactorEnrolledAt = factorSecret is null ? null : DateTime.Now;
        if (passwordChange)
        {
            h.Db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = subject.Id, PasswordHash = hasher.Hash(historyPassword), ChangedAt = DateTime.Now.AddDays(-2) });
            var edit = await (from action in h.Db.Sys_RoleActions
                              join menu in h.Db.Sys_Menus on action.MenuId equals menu.MenuId
                              where action.RoleId == 71 && menu.MenuKey == "user" && action.ActionCode == "edit"
                              select action).SingleAsync();
            h.Db.Sys_RoleActions.Remove(edit);
        }

        const string managerName = "w666-independent-manager";
        var manager = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = managerName, Enable = true, DeptId = h.Parent, RoleId = 72,
            Password = hasher.Hash(managerPassword), AuthenticationEpoch = Guid.NewGuid()
        };
        h.Db.Sys_Users.Add(manager);
        var platformTenant = new Sys_Tenant { Id = Guid.NewGuid(), TenantName = "W666 local platform", TenantCode = "w666_" + Guid.NewGuid().ToString("N") };
        var platformUser = new Sys_User
        {
            Id = Guid.NewGuid(), TenantId = platformTenant.Id, UserName = "w666-platform-actor", Enable = true, IsPlatformAdmin = true,
            Password = hasher.Hash(managerPassword), AuthenticationEpoch = Guid.NewGuid()
        };
        if (impersonated)
        {
            h.Db.Sys_Tenants.Add(platformTenant);
            h.Db.Sys_Users.Add(platformUser);
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var options = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        if (!ordered) await InstallUserSealFixture(crm, seal, h.Tenant, store);
        var crmAck = Environment.GetEnvironmentVariable("CP6_W666_CRM_ACK_PORT");
        var mainAck = Environment.GetEnvironmentVariable("CP6_W666_MAIN_ACK_PORT");
        await using var crmRelay = string.IsNullOrEmpty(crmAck) ? null :
            new StockPostgresCommitRelay(seal.Port, seal.Username!, int.Parse(crmAck));
        var sealConnection = new NpgsqlConnectionStringBuilder(seal.ConnectionString);
        if (crmRelay is not null) sealConnection.Port = crmRelay.Port;
        var mainConnection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()!);
        await using var mainRelay = string.IsNullOrEmpty(mainAck) ? null :
            new StockPostgresCommitRelay(mainConnection.Port, mainConnection.Username!, int.Parse(mainAck));
        if (mainRelay is not null)
        {
            mainConnection.Port = mainRelay.Port;
            mainConnection.SslMode = SslMode.Disable;
        }

        var environment = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        const string redirect = "https://crm.cp6.test/callback";
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
        environment["Security__Password__HistoryCount"] = "3";
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "ConnectionString"] = sealConnection.ConnectionString;
        environment[prefix + "StoreId"] = store.ToString("D");
        Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(mainConnection.ConnectionString, environment, allowAutoRedirect: false);

        await using var host = await Start();
        var subjectCookies = await SignIn(host, "iam-admin", oldPassword, $"iam_{h.Tenant:N}", factorSecret);
        var managerCookies = await SignIn(host, managerName, managerPassword, $"iam_{h.Tenant:N}");
        var actorCookies = passwordChange ? subjectCookies : managerCookies;
        if (impersonated)
        {
            var platformCookies = await SignIn(host, platformUser.UserName, managerPassword, platformTenant.TenantCode);
            using var switched = await host.Send(Request(HttpMethod.Post, "api/platform/impersonation/start", platformCookies,
                new { TenantId = h.Tenant, UserId = manager.Id, Reason = "W666 simulated authorized impersonation" }));
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            actorCookies = ReadCookies(switched);
            actorCookies["cp6_csrf"] = platformCookies["cp6_csrf"];
            using var current = await host.Profile(actorCookies["cp6_at"]);
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            var claims = new JwtSecurityTokenHandler().ReadJwtToken(actorCookies["cp6_at"]).Claims;
            Assert.Equal(platformUser.Id, Guid.Parse(claims.Single(claim => claim.Type == "impersonator_id").Value));
        }
        var path = passwordChange ? "api/auth/change-password" : "api/user/reset-2fa";
        var eventType = passwordChange ? SecurityEventType.PasswordChanged : SecurityEventType.TwoFactorReset;
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        Task<HttpResponseMessage> Change(AuthenticationApiProcess api)
        {
            object body = passwordChange ? new { CurrentPassword = oldPassword, NewPassword = newPassword } : subject.Id;
            return api.Send(Request(HttpMethod.Post, path, actorCookies, body));
        }

        if (passwordChange)
        {
            using var invalid = await host.Send(Request(HttpMethod.Post, path, actorCookies,
                new { CurrentPassword = TestPassword(), NewPassword = newPassword }));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var reused = await host.Send(Request(HttpMethod.Post, path, actorCookies,
                new { CurrentPassword = oldPassword, NewPassword = historyPassword }));
            Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
            Assert.Equal(0, await Pending());
        }

        var affected = restricted ? default : await Exchange(host, subjectCookies);
        var unaffected = restricted ? default : await Exchange(host, managerCookies);
        var targetGrants = restricted ? [] : new[] { GrantId(affected.Access) };
        var userAggregate = $"user:{subject.Id:D}";
        var aggregates = new[] { userAggregate }.Concat(targetGrants.Select(id => IdentityEventContracts.TokenAggregate(options.Issuer, id.ToString("D")))).ToArray();
        var before = await MainFacts();
        var expectedFence = $"{store:D}/1";

        if (restricted)
        {
            using (var profile = await host.Profile(subjectCookies["cp6_at"]))
                Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
            using var changed = await Change(host);
            await CheckCommitted(host, changed);
            await Save("restricted-password.json", new { Subject = subject.Id, Tenant = h.Tenant, OriginalRestricted = true, Current = await MainFacts() });
            return;
        }

        Assert.Equal(expectedFence, await Sign(host, affected.Access, subject.Id, "intake:reject"));
        Assert.Equal(expectedFence, await Sign(host, unaffected.Access, manager.Id, "intake:reject"));
        if (ordered)
        {
            await CheckBothStores();
            return;
        }

        await SetSealLogin(false);
        try
        {
            await using (var denied = new NpgsqlConnection(seal.ConnectionString))
            {
                var failure = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, failure.SqlState);
            }
            var denials = await SealDeniedConnections();
            Assert.True(denials >= 1);
            var floors = await Floors();
            using var refused = await Change(host);
            Assert.False(refused.IsSuccessStatusCode);
            var invoked = await SealDeniedConnections() - denials;
            Assert.True(invoked > 0, "The actual credential route must reach the denied CRM login.");
            Assert.Equal(before, await MainFacts());
            Assert.Equal(floors, await Floors());
            Assert.False(refused.Headers.Contains("Set-Cookie"));
            using (var original = await host.Profile(subjectCookies["cp6_at"]))
                Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            var affectedFence = await Sign(host, affected.Access, subject.Id, "intake:reject");
            var independentFence = await Sign(host, unaffected.Access, manager.Id, "intake:reject");
            var originalRead = await Sign(host, affected.Access, subject.Id, "intake:list");
            Assert.Null(originalRead);
            Assert.Equal(expectedFence, independentFence);
            var pending = await Pending();
            var envelope = await FailureEnvelope(refused);
            await Save("credential-cold.json", new
            {
                Mode = mode, Route = path, Subject = subject.Id, Tenant = h.Tenant, Store = store,
                EffectiveActor = passwordChange ? subject.Id : manager.Id, Impersonator = impersonated ? platformUser.Id : (Guid?)null,
                TargetGrants = targetGrants, NativeSqlState = "28000", RouteSealDeniedConnections = invoked, HttpStatus = (int)refused.StatusCode, Envelope = envelope,
                AffectedCurrentFence = affectedFence is not null, IndependentCurrentFence = independentFence is not null, OriginalRead = 200,
                Pending = pending, Before = before, After = await MainFacts(), Floors = await Floors(), MainPid = host.ProcessId
            });
            Assert.Null(affectedFence);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Equal(503, envelope.Code);
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", envelope.Authority);
            Assert.Equal(1, pending);
            var originalIntent = await Intent();
            Assert.False(originalIntent.Started);
            Assert.Equal(passwordChange ? subject.Id : manager.Id, originalIntent.Actor);
            Assert.Equal(passwordChange ? "self-change-password" : "admin-reset-2fa", originalIntent.Kind);
            Assert.Equal(store, originalIntent.Store);
            Assert.Equal(aggregates.Order(StringComparer.Ordinal), originalIntent.Sources.Select(source => source.Aggregate).Order(StringComparer.Ordinal));
            Assert.False(await Committed(originalIntent));

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var restarted = await Start();
            Assert.NotEqual(pid, restarted.ProcessId);
            Assert.Null(await Sign(restarted, affected.Access, subject.Id, "intake:reject"));
            Assert.Equal(expectedFence, await Sign(restarted, unaffected.Access, manager.Id, "intake:reject"));
            Assert.Equal(before, await MainFacts());
            if (passwordChange)
            {
                using var different = await restarted.Send(Request(HttpMethod.Post, path, actorCookies,
                    new { CurrentPassword = oldPassword, NewPassword = TestPassword() }));
                Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            }
            if (impersonated)
            {
                using var different = await restarted.Send(Request(HttpMethod.Post, path, managerCookies, subject.Id));
                Assert.Equal(HttpStatusCode.Forbidden, different.StatusCode);
            }
            AssertSameIntent(originalIntent, await Intent());
            await SetSealLogin(true);
            var sourceFloors = aggregates.Select(aggregate => originalIntent.Sources.Single(source => source.Aggregate == aggregate).Version).ToArray();
            if (crmRelay is not null)
            {
                crmRelay.Arm();
                using var unconfirmed = await Change(restarted);
                await crmRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
                Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await FailureEnvelope(unconfirmed)).Authority);
                Assert.Equal(before, await MainFacts());
                Assert.False(await Committed(originalIntent));
                Assert.Equal(sourceFloors, await Floors());
                var stillPending = await Intent();
                AssertSameIntent(originalIntent, stillPending);
                Assert.True(stillPending.Started);
                Assert.Null(await Sign(restarted, affected.Access, subject.Id, "intake:reject"));
                Assert.Equal(expectedFence, await Sign(restarted, unaffected.Access, manager.Id, "intake:reject"));
                await Save("credential-crm-ack.json", new { Intent = stillPending, Floors = await Floors(), LocalWritesUnchanged = true, ReceiptExists = false });
            }

            using var resumed = mainRelay is null ? await Change(restarted) : await LoseFinalMainAck(restarted, mainRelay);
            await CheckCommitted(restarted, resumed);
            Assert.Equal(0, await Pending());
            Assert.True(await Committed(originalIntent));
            Assert.Equal(sourceFloors, await Floors());
            foreach (var planned in originalIntent.Sources)
            {
                var actual = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(row => row.TenantId == h.Tenant && row.AggregateId == planned.Aggregate);
                Assert.Equal((planned.Version, planned.SuccessorType, planned.IntentHash), (actual.Version, actual.EventType, actual.PayloadSha256));
            }
            Assert.Equal(expectedFence, await Sign(restarted, unaffected.Access, manager.Id, "intake:reject"));
            await Save("credential-recovery.json", new { Before = before, After = await MainFacts(), Floors = await Floors(),
                RestartPid = restarted.ProcessId, OriginalIntent = originalIntent, ReceiptExists = true,
                CrmCommitAckLost = crmRelay is not null, MainCommitAckLost = mainRelay is not null });
        }
        finally { await SetSealLogin(true); }

        async Task<HttpResponseMessage> LoseFinalMainAck(AuthenticationApiProcess api, StockPostgresCommitRelay relay)
        {
            var original = h.Db.Database.GetConnectionString()!;
            await using var holder = new NpgsqlConnection(original);
            await holder.OpenAsync();
            await using var tx = await holder.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("""
                SELECT 1 FROM public."Sys_Users" WHERE "TenantId"=@tenant AND "Id"=@user FOR UPDATE
                """, holder, tx);
            hold.Parameters.AddWithValue("tenant", h.Tenant);
            hold.Parameters.AddWithValue("user", subject.Id);
            Assert.Equal(1, await hold.ExecuteScalarAsync());
            await using var observer = new NpgsqlConnection(original);
            await observer.OpenAsync();
            var pendingChange = Change(api);
            try
            {
                var writer = await WaitForMainBlock(observer, holder.ProcessID, "%UPDATE%Sys_Users%", pendingChange);
                Assert.Equal(before, await MainFacts());
                await Save("credential-main-ack-gate.json", new { Writer = writer, Holder = holder.ProcessID, OriginalWritesUnchanged = true });
                relay.Arm();
            }
            finally { await tx.RollbackAsync(); }
            var result = await pendingChange;
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
            return result;
        }

        async Task CheckBothStores()
        {
            var sources = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant).OrderBy(row => row.AggregateId).ToArrayAsync();
            Directory.CreateDirectory(bridge);
            var sessionFile = Path.Combine(bridge, "session.json");
            var temporary = sessionFile + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
                {
                    marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = options.Issuer, origin = host.Origin.ToString(),
                    accessToken = affected.Access, idToken = affected.Identity, tenantId = h.Tenant, subjectId = subject.Id,
                    departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId, keyId = options.KeyId,
                    publicKeyBase64 = options.PublicKeyBase64, activeFromUtc = options.ActiveFromUtc, authenticationEpoch = subject.AuthenticationEpoch,
                    sourceRows = sources.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson })
                }));
                File.Move(temporary, sessionFile);
                await WaitConfigurationBridge(bridge, "ready");
                var ownerFirst = Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") == "owner-first";
                await WaitConfigurationBridge(bridge, ownerFirst ? "owner-held" : "permission-held");
                var change = Change(host);
                int? waitingPid = null;
                if (ownerFirst)
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
                            Assert.False(change.IsCompleted, "Main returned before the admitted CRM Owner finished.");
                            await Task.Delay(25, timeout.Token);
                        }
                        Assert.Equal(before, await MainFacts());
                    }
                    finally { await Signal("release-owner"); }
                }
                using var committed = await change;
                await CheckCommitted(host, committed);
                await Signal("paused");
                await WaitConfigurationBridge(bridge, "finished");
                await Save("credential-order.json", new { Mode = mode, Route = path, OwnerFirst = ownerFirst, WaitingMainSealPid = waitingPid,
                    Tenant = h.Tenant, Subject = subject.Id, TargetGrants = targetGrants, Before = before, After = await MainFacts(), Floors = await Floors() });
            }
            finally
            {
                File.Delete(sessionFile);
                File.Delete(temporary);
            }
        }

        async Task Signal(string marker)
        {
            var path = Path.Combine(bridge, marker);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, "PASS");
            File.Move(temporary, path);
        }

        async Task CheckCommitted(AuthenticationApiProcess api, HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, result.RootElement.GetProperty("code").GetInt32());
            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject.Id);
            Assert.NotEqual(subject.CredentialRevision, user.CredentialRevision);
            Assert.True(user.Enable);
            if (passwordChange)
            {
                Assert.True(hasher.Verify(newPassword, user.Password));
                Assert.False(hasher.Verify(oldPassword, user.Password));
                Assert.False(user.MustChangePassword);
                Assert.Equal("OK", result.RootElement.GetProperty("message").GetString());
                Assert.True(response.Headers.Contains("Set-Cookie"));
                Assert.True(await h.Db.Sys_PasswordHistories.AnyAsync(row => row.UserId == user.Id && row.PasswordHash == originalHash));
                Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(row => row.UserId == user.Id && row.RevokedAt == null));
            }
            else
            {
                Assert.False(user.TwoFactorEnabled);
                Assert.True(user.TwoFactorSecret is null);
                Assert.Null(user.TwoFactorEnrolledAt);
                Assert.NotEqual(subject.AuthenticationEpoch, user.AuthenticationEpoch);
            }
            var audit = await h.Db.Sys_SecurityLogs.AsNoTracking()
                .SingleAsync(row => row.UserId == subject.Id && row.EventType == (int)eventType);
            if (!passwordChange)
            {
                Assert.Equal("admin-reset", audit.Reason);
                using var actors = JsonDocument.Parse(audit.ContextJson!);
                Assert.Equal(manager.Id, actors.RootElement.GetProperty("actorId").GetGuid());
                if (impersonated) Assert.Equal(platformUser.Id, actors.RootElement.GetProperty("impersonatorId").GetGuid());
                else Assert.Equal(JsonValueKind.Null, actors.RootElement.GetProperty("impersonatorId").ValueKind);
            }
            Assert.Equal(targetGrants.Length, await h.Db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant}
                    AND "SubjectId"={subject.Id} AND "Id"=ANY({targetGrants}) AND "Revoked"
                """).SingleAsync());
            using (var stale = await api.Profile(subjectCookies["cp6_at"]))
                Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            if (passwordChange)
            {
                var reentered = await SignIn(api, "iam-admin", newPassword, $"iam_{h.Tenant:N}");
                using var profile = await api.Profile(reentered["cp6_at"]);
                Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            }
        }

        async Task<CredentialProbeState> MainFacts()
        {
            var current = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject.Id);
            var mutations = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object(
                    'History',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","ChangedAt") ORDER BY "Id"),'[]'::jsonb) FROM public."Sys_PasswordHistories" WHERE "UserId"={subject.Id}),
                    'Refresh',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","RevokedAt") ORDER BY "Id"),'[]'::jsonb) FROM public."Sys_RefreshTokens" WHERE "TenantId"={h.Tenant} AND "UserId"={subject.Id}),
                    'Grants',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","Revoked") ORDER BY "Id"),'[]'::jsonb) FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant} AND "SubjectId"={subject.Id}),
                    'Sources',(SELECT coalesce(jsonb_agg(jsonb_build_array("AggregateId","Version","PayloadSha256") ORDER BY "AggregateId"),'[]'::jsonb)
                        FROM crm_identity."Snapshot" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})),
                    'Generation',(SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}),
                    'Outbox',(SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates}))
                      +(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})),
                    'Audit',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant} AND "UserId"={subject.Id} AND "EventType"={(int)eventType})
                )::text AS "Value"
                """).SingleAsync();
            return new(current.CredentialRevision, current.AuthenticationEpoch, current.Password == originalHash,
                current.PasswordChangedAt, current.MustChangePassword, current.TwoFactorEnabled, current.TwoFactorSecret == factorSecret,
                current.TwoFactorEnrolledAt, mutations);
        }

        Task<int> Pending() => h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_authentication_v1.pending_crm_user_seals
            WHERE tenant_id={h.Tenant} AND user_id={subject.Id}
            """).SingleAsync();

        async Task<CredentialIntentProbe> Intent()
        {
            var row = await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, CancellationToken.None, subject.Id);
            Assert.NotNull(row);
            return new(row.OperationId, row.ActorId, row.Kind, row.StoreId, row.SealStarted, row.ReadAdvances());
        }

        Task<bool> Committed(CredentialIntentProbe intent) => h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.iam_operations
                WHERE tenant_id={h.Tenant} AND operation_id={intent.Operation} AND actor_id={intent.Actor}
                    AND kind={intent.Kind} AND state='COMMITTED' AND result_json::jsonb->>'EntityId'={subject.Id.ToString("D")}) AS "Value"
            """).SingleAsync();

        static void AssertSameIntent(CredentialIntentProbe original, CredentialIntentProbe current)
        {
            Assert.Equal((original.Operation, original.Actor, original.Kind, original.Store), (current.Operation, current.Actor, current.Kind, current.Store));
            Assert.Equal(original.Sources, current.Sources);
        }

        async Task<int[]> Floors()
        {
            await using var sql = new NpgsqlCommand("""
                SELECT ARRAY(SELECT coalesce(f.minimum_version,0) FROM unnest(@aggregates::text[]) WITH ORDINALITY a(aggregate,idx)
                    LEFT JOIN crm_inquiry_tenant_pause_v1.identity_fences f ON f.tenant_id=@tenant AND f.aggregate_id=a.aggregate ORDER BY a.idx)
                """, crm);
            sql.Parameters.AddWithValue("tenant", h.Tenant);
            sql.Parameters.AddWithValue("aggregates", aggregates);
            return (int[])(await sql.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool active)
        {
            using var quote = new NpgsqlCommandBuilder();
            var role = quote.QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(active ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        async Task<int> SealDeniedConnections()
        {
            var count = 0;
            var file = Environment.GetEnvironmentVariable("CP6_W666_CRM_SERVER_LOG")!;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync() is { } line)
                if (line.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal)) count++;
            return count;
        }

        async Task<string?> Sign(AuthenticationApiProcess api, string bearer, Guid expectedSubject, string purpose)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var value = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(value, options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            Assert.Equal(purpose, value["purpose"]!.GetValue<string>());
            Assert.Equal(expectedSubject, value["subjectId"]!.GetValue<Guid>());
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        async Task<(string Access, string Identity)> Exchange(AuthenticationApiProcess api, Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["code_challenge_method"] = "S256",
                ["code_challenge"] = CrmOidcCrypto.Challenge(verifier), ["state"] = CrmOidcCrypto.RandomToken(),
                ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm"
            });
            using var entry = await api.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var approved = await api.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
            var code = QueryHelpers.ParseQuery(approved.Headers.Location!.Query)["code"].ToString();
            Assert.True(code.Length > 0);
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var response = await api.Send(exchange);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (result.RootElement.GetProperty("access_token").GetString()!, result.RootElement.GetProperty("id_token").GetString()!);
        }

        Task Save(string file, object value) => File.WriteAllTextAsync(Path.Combine(bridge, file), JsonSerializer.Serialize(value));
    }

    private static async Task<Dictionary<string, string>> SignIn(AuthenticationApiProcess api, string name, string password, string tenantCode, string? factor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { UserName = name, Password = password, TenantCode = tenantCode })
        };
        using var logged = await api.Send(request);
        Assert.Equal(HttpStatusCode.OK, logged.StatusCode);
        var cookies = ReadCookies(logged);
        if (factor is not null)
        {
            Assert.True(cookies.ContainsKey("cp6_2fa"));
            var code = new Totp(Base32Encoding.ToBytes(factor)).ComputeTotp();
            using var verified = await api.Send(Request(HttpMethod.Post, "api/auth/2fa/verify", cookies, new { Method = "totp", Code = code }));
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
            cookies = ReadCookies(verified);
        }
        Assert.True(cookies.ContainsKey("cp6_at"));
        return cookies;
    }

    private static Dictionary<string, string> ReadCookies(HttpResponseMessage response)
        => response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0].Split('=', 2))
            .Where(value => value[1].Length > 0).ToDictionary(value => value[0], value => value[1]);

    private static string TestPassword() => "W666a!9-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    private static async Task<CredentialFailureEnvelope> FailureEnvelope(HttpResponseMessage response)
    {
        var media = response.Content.Headers.ContentType?.MediaType;
        if (media is not ("application/json" or "application/problem+json")) return new(media, null, null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Object) return new(media, null, null);
        var code = json.RootElement.TryGetProperty("code", out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetInt32() : (int?)null;
        var message = json.RootElement.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        return new(media, code, message is "AUTHORITY_FENCE_UNAVAILABLE" or "IAM_OPERATION_UNKNOWN" ? message : null);
    }

    private sealed record CredentialFailureEnvelope(string? Media, int? Code, string? Authority);
    private sealed record CredentialIntentProbe(Guid Operation, Guid Actor, string Kind, Guid Store, bool Started, CrmUserSealAdvanceV1[] Sources);

    private sealed record CredentialProbeState(Guid CredentialRevision, Guid Epoch, bool PasswordUnchanged, DateTime? PasswordDate,
        bool MustChangePassword, bool FactorEnabled, bool FactorSecretUnchanged, DateTime? FactorEnrollment, string OriginalWrites);
}
