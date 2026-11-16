using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private const string PrivacyDispositionRoute = "api/platform/privacy-dispositions-v1";
    private const string PrivacyCrmRedirect = "https://crm.cp6.test/callback";

    [Pgt017PrivacyDispositionFact]
    public async Task PGT017_approved_identity_disposition_preserves_the_original_crm_boundary()
    {
        var scenario = Environment.GetEnvironmentVariable("CP6_W670_SCENARIO");
        Assert.Contains(scenario, new[] { "cold", "owner-first", "pause-first" });
        var cold = scenario == "cold";
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await CopyRole71ForIndependentInquiry(h);

        var environmentId = Guid.NewGuid();
        var password = TestPassword();
        var hasher = new BCryptPasswordHasher(4);
        var subject = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        subject.Password = hasher.Hash(password);
        subject.AuthenticationEpoch = Guid.NewGuid();
        subject.Email = "disposition-subject@example.invalid";
        subject.NickName = "Simulated disposition subject";
        var independent = NewUser("w670-independent", 72, platform: false);
        var requester = NewUser("w670-requester", 74, platform: true);
        var approver = NewUser("w670-approver", 75, platform: true);
        await SetUpActors();
        var originalEpoch = subject.AuthenticationEpoch;

        var signing = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, signing.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        if (cold)
            await InstallUserSealFixture(crm, seal, h.Tenant, store);

        var environment = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        environment["ScopeAuthorization__EnvironmentId"] = environmentId.ToString("D");
        environment["CrmOidc__Issuer"] = signing.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = PrivacyCrmRedirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = signing.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = signing.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = signing.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = signing.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = signing.ActiveFromUtc.ToString("O");
        environment["Security__Password__BcryptWorkFactor"] = "4";
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var binding = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[binding + "StoreId"] = store.ToString("D");
        environment[binding + "ConnectionString"] = seal.ConnectionString;
        var mainConnection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString());
        var relayPort = Environment.GetEnvironmentVariable("CP6_W670_MAIN_ACK_PORT");
        await using var relay = string.IsNullOrEmpty(relayPort) ? null :
            new StockPostgresCommitRelay(mainConnection.Port, mainConnection.Username!, int.Parse(relayPort));
        var routed = new NpgsqlConnectionStringBuilder(mainConnection.ConnectionString);
        if (relay is not null)
        {
            routed.Port = relay.Port;
            routed.SslMode = SslMode.Disable;
        }
        Task<AuthenticationApiProcess> Start(bool direct = false) => AuthenticationApiProcess.Start(
            direct ? mainConnection.ConnectionString : routed.ConnectionString, environment, allowAutoRedirect: false);

        await using var host = await Start();
        var requesterSession = await Login(host, h, password, requester.UserName);
        var approverSession = await Login(host, h, password, approver.UserName);
        var subjectSession = await Login(host, h, password, subject.UserName);
        var independentSession = await Login(host, h, password, independent.UserName);
        var approved = await ApprovePrivacyDisposition(host, h.Tenant, subject.Id, requester.Id, approver.Id, requesterSession, approverSession);
        var input = new PrivacyDispositionExecuteV1(Guid.NewGuid(), approved.Version, approved.ScopeDigest, Guid.NewGuid());
        var path = $"{PrivacyDispositionRoute}/plans/{approved.PlanId:D}/execute";
        var affected = await ExchangePrivacyGrant(host, subjectSession, clientSecret);
        var alsoAffected = await ExchangePrivacyGrant(host, subjectSession, clientSecret);
        var unaffected = await ExchangePrivacyGrant(host, independentSession, clientSecret);
        var targetGrants = new[] { GrantId(affected.Access), GrantId(alsoAffected.Access) };
        Assert.NotEqual(targetGrants[0], targetGrants[1]);
        var userAggregate = $"user:{subject.Id:D}";
        var aggregates = new[] { userAggregate }.Concat(targetGrants.Select(id =>
            IdentityEventContracts.TokenAggregate(signing.Issuer, id.ToString("D")))).ToArray();
        var writesBefore = await Writes();
        var versionsBefore = await Versions();
        var expectedFence = $"{store:D}/1";

        using (var staleVersion = await Execute(host, input with { ExpectedVersion = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleVersion.StatusCode);
        Assert.Equal(writesBefore, await Writes());
        Assert.Equal(expectedFence, await Sign(host, affected.Access, subject.Id, "intake:reject"));
        Assert.Equal(expectedFence, await Sign(host, alsoAffected.Access, subject.Id, "intake:reject"));
        Assert.Equal(expectedFence, await Sign(host, unaffected.Access, independent.Id, "intake:reject"));

        if (cold)
            await CheckColdRecovery();
        else
            await CheckCommitOrder();

        async Task CheckColdRecovery()
        {
            var floorsBefore = await Floors();
            Assert.All(floorsBefore, floor => Assert.Equal(0, floor));
            await SetSealLogin(false);
            try
            {
                await using (var deniedConnection = new NpgsqlConnection(seal.ConnectionString))
                {
                    var refusal = await Assert.ThrowsAsync<PostgresException>(() => deniedConnection.OpenAsync());
                    Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);
                }
                var closeInput = input with { OperationId = Guid.NewGuid(), ConfirmationRef = Guid.NewGuid() };
                using (var refusedBeforeSend = await Execute(host, closeInput))
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, refusedBeforeSend.StatusCode);
                var closed = await ReadPrivacy<PrivacyDispositionOperationV1>(host, HttpMethod.Post,
                    $"{PrivacyDispositionRoute}/operations/{closeInput.OperationId:D}/close", requesterSession);
                Assert.Equal(("CLOSED", closeInput.OperationId), (closed.State, closed.OperationId));
                var closedReplay = await ReadPrivacy<PrivacyDispositionOperationV1>(host, HttpMethod.Post, path, requesterSession, closeInput);
                Assert.Equal(closed, closedReplay);
                Assert.Equal(expectedFence, await Sign(host, affected.Access, subject.Id, "intake:reject"));
                Assert.Equal(writesBefore, await Writes());
                Assert.Equal(floorsBefore, await Floors());

                var priorRefusals = await SealDenials();
                using (var refused = await Execute(host, input))
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
                Assert.True(await SealDenials() > priorRefusals, "The authorized disposition reached its dedicated CRM seal connection.");
                Assert.Equal(writesBefore, await Writes());
                Assert.Equal(floorsBefore, await Floors());
                await CheckPending(host);

                await host.StopAsync();
                await using var restarted = await Start();
                Assert.NotEqual(host.ProcessId, restarted.ProcessId);
                await CheckPending(restarted);
                using (var different = await Execute(restarted, input with { ConfirmationRef = Guid.NewGuid() }))
                    Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
                Assert.Equal(writesBefore, await Writes());

                await SetSealLogin(true);
                if (relay is null)
                {
                    await CheckCommitted(restarted);
                }
                else
                {
                    await LoseFinalMainAcknowledgement(restarted, relay);
                    await restarted.StopAsync();
                    await using var readback = await Start(direct: true);
                    Assert.NotEqual(restarted.ProcessId, readback.ProcessId);
                    await CheckCommitted(readback);
                    await SaveEvidence("privacy-main-ack-readback.json", new
                    {
                        FaultedPid = restarted.ProcessId, DirectPid = readback.ProcessId, input.OperationId, approved.PlanId,
                        Subject = subject.Id, Completed = await Writes()
                    });
                }

                await SaveEvidence("privacy-cold.json", new
                {
                    approved.PlanId, input.OperationId, OriginalSubject = subject.Id, requester.Id,
                    MainFirstProcess = host.ProcessId, MainRecoveredProcess = restarted.ProcessId,
                    NewSealConnectionRefused = true, Before = writesBefore, After = await Writes(), Floors = await Floors()
                });
            }
            finally { await SetSealLogin(true); }
        }

        async Task LoseFinalMainAcknowledgement(AuthenticationApiProcess api, StockPostgresCommitRelay fault)
        {
            await using var holder = new NpgsqlConnection(mainConnection.ConnectionString);
            await holder.OpenAsync();
            await using var transaction = await holder.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("""
                SELECT 1 FROM public."Sys_Users" WHERE "TenantId"=@tenant AND "Id"=@id FOR UPDATE
                """, holder, transaction);
            hold.Parameters.AddWithValue("tenant", h.Tenant);
            hold.Parameters.AddWithValue("id", subject.Id);
            Assert.Equal(1, await hold.ExecuteScalarAsync());
            await using var observer = new NpgsqlConnection(mainConnection.ConnectionString);
            await observer.OpenAsync();
            var submit = Execute(api, input);
            int writer;
            try
            {
                writer = await WaitForMainBlock(observer, holder.ProcessID, "%UPDATE%Sys_Users%", submit);
                Assert.Equal(writesBefore, await Writes());
                fault.Arm();
            }
            finally { await transaction.RollbackAsync(); }

            using var unconfirmed = await submit;
            await fault.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
            Assert.Contains("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN", await unconfirmed.Content.ReadAsStringAsync());
            var committed = await Writes();
            Assert.Equal((1, 1, 1), (committed.Executions, committed.Tombstones, committed.Originals));
            await SaveEvidence("privacy-main-ack.json", new
            {
                Writer = writer, Holder = holder.ProcessID, input.OperationId, approved.PlanId,
                Before = writesBefore, Committed = committed, Floors = await Floors()
            });
        }

        async Task CheckCommitOrder()
        {
            var rows = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant)
                .OrderBy(row => row.AggregateId).ToArrayAsync();
            Directory.CreateDirectory(bridge);
            var session = Path.Combine(bridge, "session.json");
            var temporary = session + ".tmp";
            var ownerFirst = scenario == "owner-first";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
                {
                    marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = signing.Issuer, origin = host.Origin.ToString(),
                    accessToken = affected.Access, idToken = affected.Identity, tenantId = h.Tenant, subjectId = subject.Id,
                    departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId, keyId = signing.KeyId,
                    publicKeyBase64 = signing.PublicKeyBase64, activeFromUtc = signing.ActiveFromUtc, authenticationEpoch = originalEpoch,
                    sourceRows = rows.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson })
                }));
                File.Move(temporary, session);
                await WaitConfigurationBridge(bridge, "ready");
                await WaitConfigurationBridge(bridge, ownerFirst ? "owner-held" : "permission-held");
                Assert.All(await Floors(), floor => Assert.Equal(0, floor));
                var execute = Execute(host, input);
                int? waitingSeal = null;
                if (ownerFirst)
                {
                    try
                    {
                        await using var waiting = new NpgsqlCommand("""
                            SELECT pid FROM pg_stat_activity WHERE datname=current_database() AND usename=@login
                                AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                                AND cardinality(pg_blocking_pids(pid))>0
                            """, crm);
                        waiting.Parameters.AddWithValue("login", seal.Username!);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        while (waitingSeal is null)
                        {
                            waitingSeal = (int?)await waiting.ExecuteScalarAsync(timeout.Token);
                            if (waitingSeal is not null)
                                break;
                            Assert.False(execute.IsCompleted, "The active CRM Owner must hold the physical seal boundary.");
                            await Task.Delay(25, timeout.Token);
                        }
                        Assert.Equal(writesBefore, await Writes());
                    }
                    finally { await Signal("release-owner"); }
                }

                await CheckCommitted(host, await execute);
                await Signal("paused");
                await WaitConfigurationBridge(bridge, "finished");
                await SaveEvidence("privacy-order.json", new
                {
                    Scenario = scenario, WaitingSealPid = waitingSeal, approved.PlanId, input.OperationId, OriginalSubject = subject.Id,
                    Before = writesBefore, After = await Writes(), Floors = await Floors()
                });
            }
            finally
            {
                File.Delete(session);
                File.Delete(temporary);
            }
        }

        async Task CheckPending(AuthenticationApiProcess api)
        {
            using var profile = await api.Profile(subjectSession["cp6_at"]);
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            Assert.Null(await Sign(api, affected.Access, subject.Id, "intake:reject"));
            Assert.Null(await Sign(api, alsoAffected.Access, subject.Id, "intake:reject"));
            Assert.Null(await Sign(api, affected.Access, subject.Id, "intake:list"));
            Assert.Equal(expectedFence, await Sign(api, unaffected.Access, independent.Id, "intake:reject"));
            var original = await ReadPrivacy<PrivacyDispositionPlanV1>(api, HttpMethod.Get,
                $"{PrivacyDispositionRoute}/plans/{approved.PlanId:D}", requesterSession);
            Assert.Equal(("APPROVED", approved.Version, approved.ScopeDigest), (original.State, original.Version, original.ScopeDigest));
            Assert.Equal((requester.Id, approver.Id), (original.RequesterId, original.ApproverId!.Value));
            Assert.Null(original.Execution);
            using var operation = await api.Send(Request(HttpMethod.Get,
                $"{PrivacyDispositionRoute}/operations/{input.OperationId:D}", requesterSession));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, operation.StatusCode);
            Assert.Contains("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN", await operation.Content.ReadAsStringAsync());
        }

        async Task CheckCommitted(AuthenticationApiProcess api, HttpResponseMessage? completed = null)
        {
            using var original = completed ?? await Execute(api, input);
            var text = await original.Content.ReadAsStringAsync();
            Assert.True(original.StatusCode == HttpStatusCode.OK, text);
            var result = JsonSerializer.Deserialize<PrivacyDispositionOperationV1>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(("COMMITTED", "EXECUTE", input.OperationId), (result.State, result.Kind, result.OperationId));
            var execution = result.Execution;
            Assert.NotNull(execution);
            Assert.Equal((approved.PlanId, h.Tenant, subject.Id, "Main.Identity.Profile.v1"),
                (execution.PlanId, execution.TargetTenantId, execution.SubjectId, execution.Owner));
            Assert.Equal((requester.Id, requester.Id, approver.Id, input.ConfirmationRef),
                (execution.OperatorId, execution.RequesterId, execution.ApproverId, execution.ConfirmationRef));
            Assert.Equal((approved.Scope.PolicyVersion, approved.Scope.TargetVersion, "APPLIED", "PARTIAL"),
                (execution.PolicyVersion, execution.BeforeVersion, execution.OwnerOutcome, execution.State));
            Assert.NotEqual(execution.BeforeVersion, execution.AfterVersion);
            var stored = await h.Db.Database.SqlQuery<string>($"""
                SELECT receipt_json AS "Value" FROM cp6_privacy_disposition_v1.executions
                WHERE plan_id={approved.PlanId} AND target_tenant={h.Tenant} AND subject_id={subject.Id}
                """).SingleAsync();
            Assert.Equal(stored, JsonSerializer.Serialize(execution));

            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject.Id);
            Assert.Equal((h.Tenant, $"anon_{subject.Id:N}", false), (user.TenantId, user.UserName, user.Enable));
            Assert.Null(user.NickName);
            Assert.Null(user.Email);
            Assert.NotEqual(originalEpoch, user.AuthenticationEpoch);
            Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(row => row.TenantId == h.Tenant && row.UserId == subject.Id && row.RevokedAt == null));
            Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM public."Sys_BrowserSessions" WHERE "TenantId"={h.Tenant} AND "UserId"={subject.Id}
                    AND "LoggedOutAtUtc" IS NULL) AS "Value"
                """).SingleAsync());
            var committed = await Writes();
            Assert.Equal((targetGrants.Length, 1, 1, 1), (committed.RevokedGrants, committed.Executions, committed.Tombstones, committed.Originals));
            Assert.Equal(writesBefore.Outbox + aggregates.Length, committed.Outbox);
            Assert.Equal(writesBefore.Generation + aggregates.Length, committed.Generation);
            var versions = await Versions();
            Assert.Equal(versionsBefore.Select(version => version + 1), versions);
            Assert.Equal(versions, await Floors());
            using (var revoked = await api.Profile(subjectSession["cp6_at"]))
                Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            using (var current = await api.Profile(independentSession["cp6_at"]))
                Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Equal(expectedFence, await Sign(api, unaffected.Access, independent.Id, "intake:reject"));

            using var retry = await Execute(api, input);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(text, await retry.Content.ReadAsStringAsync());
            using var lookup = await api.Send(Request(HttpMethod.Get, $"{PrivacyDispositionRoute}/operations/{input.OperationId:D}", requesterSession));
            Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
            Assert.Equal(text, await lookup.Content.ReadAsStringAsync());
            Assert.Equal(committed, await Writes());
        }

        async Task<HttpResponseMessage> Execute(AuthenticationApiProcess api, PrivacyDispositionExecuteV1 command)
        {
            using var request = Request(HttpMethod.Post, path, requesterSession, command);
            return await api.Send(request);
        }

        Task<PrivacyDispositionWriteSet> Writes() => h.Db.Database.SqlQuery<PrivacyDispositionWriteSet>($"""
            SELECT u."CredentialRevision" AS "Credential",u."AuthenticationEpoch" AS "Epoch",u."Enable" AS "Enabled",
                u."Email" IS NOT NULL AS "ProfilePresent",
                (SELECT count(*)::integer FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant}
                    AND "SubjectId"={subject.Id} AND "Id"=ANY({targetGrants}) AND "Revoked") AS "RevokedGrants",
                (SELECT count(*)::integer FROM public."Sys_RefreshTokens" WHERE "TenantId"={h.Tenant}
                    AND "UserId"={subject.Id} AND "RevokedAt" IS NULL) AS "ActiveRefresh",
                (SELECT count(*)::integer FROM public."Sys_BrowserSessions" WHERE "TenantId"={h.Tenant}
                    AND "UserId"={subject.Id} AND "LoggedOutAtUtc" IS NULL) AS "ActiveBrowser",
                (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}) AS "Generation",
                (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates}))+
                (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})) AS "Outbox",
                (SELECT count(*)::integer FROM cp6_privacy_disposition_v1.executions WHERE plan_id={approved.PlanId} AND subject_id={subject.Id}) AS "Executions",
                (SELECT count(*)::integer FROM cp6_privacy_disposition_v1.tombstones WHERE plan_id={approved.PlanId} AND subject_id={subject.Id}) AS "Tombstones",
                (SELECT count(*)::integer FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}
                    AND actor_id={requester.Id} AND operation_id={input.OperationId} AND state='COMMITTED') AS "Originals"
            FROM public."Sys_Users" u WHERE u."TenantId"={h.Tenant} AND u."Id"={subject.Id}
            """).SingleAsync();

        Task<int[]> Versions() => h.Db.Database.SqlQuery<int[]>($"""
            SELECT ARRAY(SELECT COALESCE(s."Version",0) FROM unnest({aggregates}::text[]) WITH ORDINALITY a(id,i)
              LEFT JOIN crm_identity."Snapshot" s ON s."TenantId"={h.Tenant} AND s."AggregateId"=a.id ORDER BY a.i) AS "Value"
            """).SingleAsync();

        async Task<int[]> Floors()
        {
            await using var command = new NpgsqlCommand("""
                SELECT ARRAY(SELECT coalesce(f.minimum_version,0) FROM unnest(@aggregates::text[]) WITH ORDINALITY a(id,i)
                    LEFT JOIN crm_inquiry_tenant_pause_v1.identity_fences f ON f.tenant_id=@tenant AND f.aggregate_id=a.id ORDER BY a.i)
                """, crm);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            command.Parameters.AddWithValue("aggregates", aggregates);
            return (int[])(await command.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool enable)
        {
            using var quote = new NpgsqlCommandBuilder();
            await using var command = new NpgsqlCommand($"ALTER ROLE {quote.QuoteIdentifier(seal.Username!)} {(enable ? "LOGIN" : "NOLOGIN")}", crm);
            await command.ExecuteNonQueryAsync();
        }

        async Task<int> SealDenials()
        {
            var file = Environment.GetEnvironmentVariable("CP6_W670_CRM_SERVER_LOG")!;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var count = 0;
            while (await reader.ReadLineAsync() is { } row)
                if (row.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal))
                    count++;
            return count;
        }

        async Task<string?> Sign(AuthenticationApiProcess api, string bearer, Guid user, string purpose)
        {
            var nonce = ServiceNonce();
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, nonce))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var value = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(value, signing.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            Assert.Equal((h.Tenant, user, purpose, nonce), (value["organizationId"]!.GetValue<Guid>(), value["subjectId"]!.GetValue<Guid>(),
                value["purpose"]!.GetValue<string>(), value["requestNonce"]!.GetValue<string>()));
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        Sys_User NewUser(string name, int role, bool platform) => new()
        {
            Id = Guid.NewGuid(), UserName = name, Enable = true, DeptId = h.Parent, RoleId = role, IsPlatformAdmin = platform,
            Password = hasher.Hash(password), AuthenticationEpoch = Guid.NewGuid()
        };

        async Task SetUpActors()
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_scope_authorization_v1.environment(singleton,environment_id) VALUES(true,{environmentId})
                """);
            h.Db.Sys_Roles.AddRange(new Sys_Role { RoleId = 74, RoleName = "W670 disposition operator" },
                new Sys_Role { RoleId = 75, RoleName = "W670 independent approver" });
            h.Db.Sys_Users.AddRange(independent, requester, approver);
            var menu = await h.Db.Sys_Menus.SingleAsync(row => row.MenuKey == PrivacyDispositionOwnerV1.Menu);
            foreach (var action in new[] { "read", "policy", "prepare", "approve", "hold", "execute" })
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == action))
                    h.Db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 74, MenuId = menu.MenuId, ActionCode = action });
            }
            foreach (var action in new[] { "read", "approve" })
                h.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 75, MenuId = menu.MenuId, ActionCode = action });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }

        Task Signal(string stage) => File.WriteAllTextAsync(Path.Combine(bridge, stage), "PASS");
        Task SaveEvidence(string name, object data) => File.WriteAllTextAsync(Path.Combine(bridge, name), JsonSerializer.Serialize(data));
    }

    private static async Task<PrivacyDispositionPlanV1> ApprovePrivacyDisposition(AuthenticationApiProcess api, Guid tenant,
        Guid subject, Guid requester, Guid approver, Dictionary<string, string> requesterSession, Dictionary<string, string> approverSession)
    {
        var target = new PrivacyDispositionTargetV1(tenant, subject, Guid.NewGuid());
        await ReadPrivacy<PrivacyDispositionOperationV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/policies", requesterSession,
            new PrivacyDispositionPolicyCommandV1(Guid.NewGuid(), target.PolicyId, tenant, null, Guid.NewGuid(), true));
        var review = DateTimeOffset.UtcNow.AddMinutes(15);
        var held = await ReadPrivacy<PrivacyDispositionOperationV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/holds", requesterSession,
            new PrivacyDispositionHoldCommandV1(Guid.NewGuid(), tenant, subject, null, true, Guid.NewGuid(), review.AddTicks(-(review.Ticks % 10))));
        var blocked = await ReadPrivacy<PrivacyDispositionPreviewV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/preview", requesterSession, target);
        Assert.Contains("ACTIVE_HOLD", blocked.Blockers);
        Assert.Equal(held.Version, blocked.Hold!.Version);
        var released = await ReadPrivacy<PrivacyDispositionOperationV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/holds", requesterSession,
            new PrivacyDispositionHoldCommandV1(Guid.NewGuid(), tenant, subject, held.Version, false, Guid.NewGuid(), null));
        var preview = await ReadPrivacy<PrivacyDispositionPreviewV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/preview", requesterSession, target);
        Assert.Empty(preview.Blockers);
        Assert.Equal(released.Version, preview.Scope.HoldVersion);
        var requested = await ReadPrivacy<PrivacyDispositionOperationV1>(api, HttpMethod.Post, $"{PrivacyDispositionRoute}/plans", requesterSession,
            new PrivacyDispositionPlanCommandV1(Guid.NewGuid(), Guid.NewGuid(), target, preview.ScopeDigest));
        Assert.Equal("COMMITTED", requested.State);
        var path = $"{PrivacyDispositionRoute}/plans/{requested.PlanId!.Value:D}";
        var decision = new PrivacyDispositionDecisionV1(Guid.NewGuid(), requested.Version!.Value, "APPROVE", Guid.NewGuid());
        using (var selfApproval = await api.Send(Request(HttpMethod.Post, $"{path}/decisions", requesterSession, decision)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
            Assert.Contains("PRIVACY_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED", await selfApproval.Content.ReadAsStringAsync());
        }
        await ReadPrivacy<PrivacyDispositionOperationV1>(api, HttpMethod.Post, $"{path}/decisions", approverSession, decision);
        var approved = await ReadPrivacy<PrivacyDispositionPlanV1>(api, HttpMethod.Get, path, requesterSession);
        Assert.True(approved.ApprovalCurrent);
        Assert.Equal(("APPROVED", requester, approver, preview.ScopeDigest),
            (approved.State, approved.RequesterId, approved.ApproverId!.Value, approved.ScopeDigest));
        return approved;
    }

    private static async Task<T> ReadPrivacy<T>(AuthenticationApiProcess api, HttpMethod method, string path,
        Dictionary<string, string> session, object? body = null)
    {
        using var request = Request(method, path, session, body);
        using var response = await api.Send(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(string Access, string Identity)> ExchangePrivacyGrant(
        AuthenticationApiProcess api, Dictionary<string, string> cookies, string secret)
    {
        var verifier = CrmOidcCrypto.RandomToken();
        var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "CP6.Web", ["redirect_uri"] = PrivacyCrmRedirect, ["response_type"] = "code",
            ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
            ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm"
        });
        using var entry = await api.Send(Request(HttpMethod.Get, query, cookies));
        Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
        using var approved = await api.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var code = QueryHelpers.ParseQuery(approved.Headers.Location!.Query)["code"].ToString();
        Assert.NotEmpty(code);
        using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code,
                ["redirect_uri"] = PrivacyCrmRedirect, ["code_verifier"] = verifier
            })
        };
        exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + secret)));
        using var response = await api.Send(exchange);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (result.RootElement.GetProperty("access_token").GetString()!, result.RootElement.GetProperty("id_token").GetString()!);
    }

    private sealed record PrivacyDispositionWriteSet(Guid Credential, Guid Epoch, bool Enabled, bool ProfilePresent,
        int RevokedGrants, int ActiveRefresh, int ActiveBrowser, long Generation, long Outbox, int Executions, int Tombstones, int Originals);
}

internal sealed class Pgt017PrivacyDispositionFactAttribute : FactAttribute
{
    public Pgt017PrivacyDispositionFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CP6_W670_SCENARIO")))
            Skip = "Requires the isolated W670 authorized Main and CRM PostgreSQL fixture.";
    }
}
