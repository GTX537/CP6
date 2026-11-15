using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_registered_user_seal_cold_refusal_and_original_recovery()
    {
        var mode = Environment.GetEnvironmentVariable("CP6_W660_USER_MODE") ?? "versioned";
        var invalidates = mode is "versioned" or "legacy-password";
        var acknowledge = Environment.GetEnvironmentVariable("CP6_W660_CRM_ACK_PORT");
        var mainAcknowledge = Environment.GetEnvironmentVariable("CP6_W660_MAIN_ACK_PORT");
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await SeedInquiryUserRevoker(h);
        await CopyRole71ForIndependentInquiry(h);
        foreach (var user in await h.Db.Sys_Users.Where(user => user.Id == h.ActorId || user.UserName == InquiryRevoker).ToListAsync())
        {
            user.AuthenticationEpoch = Guid.NewGuid();
            user.DeptId = h.Parent;
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
        await InstallUserSealFixture(crm, seal, h.Tenant, store);
        await using var relay = acknowledge is null ? null : new StockPostgresCommitRelay(seal.Port, seal.Username!, int.Parse(acknowledge));
        var mainSeal = new NpgsqlConnectionStringBuilder(seal.ConnectionString);
        if (relay is not null) mainSeal.Port = relay.Port;
        var connection = h.Db.Database.GetConnectionString()!;
        var mainConnection = new NpgsqlConnectionStringBuilder(connection);
        await using var mainRelay = mainAcknowledge is null ? null : new StockPostgresCommitRelay(mainConnection.Port,
            mainConnection.Username!, int.Parse(mainAcknowledge));
        if (mainRelay is not null)
        {
            mainConnection.Port = mainRelay.Port;
            mainConnection.SslMode = SslMode.Disable;
        }

        const string clientSecret = "owned-w660-oidc-client";
        const string redirect = "https://crm.cp6.test/callback";
        var environment = IdentityEnvironment(h.Tenant);
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
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "ConnectionString"] = mainSeal.ConnectionString;
        environment[prefix + "StoreId"] = store.ToString("D");
        Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(mainConnection.ConnectionString, environment, allowAutoRedirect: false);

        await using var host = await Start();
        var subject = await Login(host, h);
        var actor = await Login(host, h, userName: InquiryRevoker);
        var firstToken = await Exchange(host, subject);
        var secondToken = await Exchange(host, subject);
        var independentToken = await Exchange(host, actor);
        var grantIds = new[] { GrantId(firstToken), GrantId(secondToken) }.Order().ToArray();
        var independentId = GrantId(independentToken);
        Assert.Equal(3, grantIds.Append(independentId).Distinct().Count());

        var editorPath = $"api/iam/users/editor?id={h.ActorId:D}";
        using var editorResponse = await host.Send(Request(HttpMethod.Get, editorPath, actor));
        Assert.Equal(HttpStatusCode.OK, editorResponse.StatusCode);
        var editor = (await editorResponse.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        var target = editor.User!;
        var command = new IamUserCommandV1
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = target.Version, ExpectedReferencesVersion = editor.ReferencesVersion,
            Reason = "W660 original simulated user administration", Id = target.Id, UserName = target.UserName, NickName = target.NickName,
            RoleId = target.RoleId, RoleIds = mode is "roles" or "legacy-roles" ? [71, 72] : target.RoleIds,
            Enable = mode != "versioned", DeptId = mode == "organization" ? h.Child : target.DeptId,
            ManagerId = target.ManagerId, Email = target.Email,
            Password = mode == "legacy-password" ? "SimulatedReplacement1!" : null,
        };
        var kind = mode switch { "roles" or "legacy-roles" => "user-roles", "organization" => "user-org", _ => "user-update" };
        var operation = $"api/iam/users/operations/{command.OperationId:D}";
        var aggregate = $"user:{target.Id:D}";
        var grantAggregates = grantIds.Select(id => IdentityEventContracts.TokenAggregate(options.Issuer, id.ToString("D"))).ToArray();
        var before = await MainEffects();
        Assert.Equal((2, 0, 0), (before.Grants, before.RevokedGrants, before.Receipts));
        Assert.Equal(new[] { 0, 0, 0 }, await Floors());
        await RequireFence(host, firstToken, true);
        await RequireFence(host, independentToken, true);
        await SetSealLogin(false);
        try
        {
            await using var denied = new NpgsqlConnection(seal.ConnectionString);
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, error.SqlState);
            if (mode == "versioned")
            {
                var abandoned = command with { OperationId = Guid.NewGuid() };
                using var rejected = await Write(host, actor, abandoned);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
                using var closed = await host.Send(Request(HttpMethod.Post,
                    $"api/iam/users/operations/{abandoned.OperationId:D}/close?kind={kind}", actor));
                Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
                Assert.Equal("CLOSED", (await closed.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
                using var cannotReplay = await Write(host, actor, abandoned);
                Assert.Equal(HttpStatusCode.Conflict, cannotReplay.StatusCode);
                await RequireFence(host, firstToken, true);
            }
            using var cold = await Write(host, actor, command);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, cold.StatusCode);
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await cold.Content.ReadAsStringAsync());
            Assert.Equal(before, await MainEffects());
            Assert.Equal(new[] { 0, 0, 0 }, await Floors());
            await RequireFence(host, firstToken, false);
            await RequireFence(host, secondToken, false);
            await RequireFence(host, independentToken, true);
            using (var oldWrite = await Projection(host, secondToken, "intake:reject", false))
                Assert.Equal(HttpStatusCode.ServiceUnavailable, oldWrite.StatusCode);
            using (var read = await Projection(host, firstToken, "intake:list", true))
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var staged = await Intent();
            using (var savedIntent = JsonDocument.Parse(staged))
            {
                Assert.Equal(target.Id, savedIntent.RootElement.GetProperty("user_id").GetGuid());
                Assert.Equal(store, savedIntent.RootElement.GetProperty("store_id").GetGuid());
                Assert.False(savedIntent.RootElement.GetProperty("seal_started").GetBoolean());
                Assert.Equal(invalidates ? 3 : 1, savedIntent.RootElement.GetProperty("source_seals").GetArrayLength());
            }
            if (mode == "versioned") await RejectCompetingUserSource(host, administrative: false);

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var next = await Start();
            Assert.NotEqual(pid, next.ProcessId);
            await RequireFence(next, secondToken, false);
            await RequireFence(next, independentToken, true);
            Assert.Equal(staged, await Intent());
            using var unknown = await next.Send(Request(HttpMethod.Get, operation + $"?kind={kind}", actor));
            Assert.Equal("UNKNOWN", (await unknown.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
            using var wrong = await Write(next, subject, command);
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            using var changed = await Write(next, actor, command with { Reason = "Changed pending request" });
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            using var other = await Write(next, actor, command with { OperationId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);

            await SetSealLogin(true);
            if (relay is not null)
            {
                relay.Arm();
                using var unconfirmed = await Write(next, actor, command);
                await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
                Assert.Equal(before, await MainEffects());
                Assert.Equal(new[] { before.UserVersion + 1, 0, 0 }, await Floors());
                using var close = await next.Send(Request(HttpMethod.Post, operation + $"/close?kind={kind}", actor));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, close.StatusCode);
                await RequireFence(next, firstToken, false);
                await RejectCompetingUserSource(next, administrative: true);
            }

            IamOperationV1 receipt;
            if (mainRelay is null)
            {
                using var committed = await Write(next, actor, command);
                Assert.True(committed.StatusCode == HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
                receipt = (await committed.Content.ReadFromJsonAsync<IamOperationV1>())!;
            }
            else
            {
                await using var holder = new NpgsqlConnection(connection);
                await holder.OpenAsync();
                await using var holdTransaction = await holder.BeginTransactionAsync();
                await using var hold = new NpgsqlCommand("""
                    SELECT 1 FROM public."Sys_Users" WHERE "TenantId"=@tenant AND "Id"=@user FOR UPDATE
                    """, holder, holdTransaction);
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                hold.Parameters.AddWithValue("user", target.Id);
                Assert.Equal(1, await hold.ExecuteScalarAsync());
                await using var observer = new NpgsqlConnection(connection);
                await observer.OpenAsync();
                var finishing = Write(next, actor, command);
                try
                {
                    await WaitForMainBlock(observer, holder.ProcessID, "%UPDATE%Sys_Users%", finishing);
                    Assert.Equal(before, await MainEffects());
                    mainRelay.Arm();
                }
                finally { await holdTransaction.RollbackAsync(); }

                using var unconfirmed = await finishing;
                await mainRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
                Assert.Contains("IAM_OPERATION_UNKNOWN", await unconfirmed.Content.ReadAsStringAsync());
                using var resolved = await next.Send(Request(HttpMethod.Get, operation + $"?kind={kind}", actor));
                Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
                receipt = (await resolved.Content.ReadFromJsonAsync<IamOperationV1>())!;
            }
            Assert.Equal((command.OperationId, target.Id, kind, "COMMITTED"), (receipt.OperationId, receipt.EntityId!.Value, receipt.Kind, receipt.State));
            Assert.Equal("", await Intent());
            var after = await MainEffects();
            var expectedChildren = invalidates ? 2 : 0;
            Assert.Equal((before.UserVersion + 1, expectedChildren, expectedChildren, 1, 1),
                (after.UserVersion, after.RevokedGrants, after.TokenSnapshots, after.Audits, after.Receipts));
            Assert.Equal(before.Generation + 1 + expectedChildren, after.Generation);
            Assert.Equal(before.Outbox + 1 + expectedChildren, after.Outbox);
            Assert.Equal(before.Refresh + (mode == "legacy-password" ? 1 : 0), after.Refresh);
            Assert.Equal(new[] { after.UserVersion, invalidates ? 1 : 0, invalidates ? 1 : 0 }, await Floors());
            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == target.Id);
            var saved = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(row => row.AggregateId == aggregate);
            var data = JsonSerializer.Deserialize<UserIdentityData>(saved.PayloadJson, IdentityEventContracts.Json)!;
            Assert.Equal(user.AuthenticationEpoch, data.AuthenticationEpoch);
            Assert.Equal(command.Enable, data.Enabled);
            if (mode == "legacy-password")
            {
                Assert.True(data.MustChangePassword);
                Assert.Equal(new DateTimeOffset(user.PasswordChangedAt!.Value).ToUniversalTime(), data.ValidAfterUtc);
            }
            if (mode is "roles" or "legacy-roles") Assert.Contains("role:72", data.Roles);
            if (mode == "organization") Assert.Equal(h.Child, data.DeptId);
            using var original = await next.Send(Request(HttpMethod.Get, operation + $"?kind={kind}", actor));
            Assert.Equal(receipt, await original.Content.ReadFromJsonAsync<IamOperationV1>());
            using var repeated = await Write(next, actor, command);
            Assert.Equal(receipt, await repeated.Content.ReadFromJsonAsync<IamOperationV1>());
            if (kind == "user-update")
            {
                var alias = mode == "versioned" ? "api/user" : $"api/iam/users/{target.Id:D}";
                using var aliased = await next.Send(Request(HttpMethod.Put, alias, actor, command));
                Assert.Equal(receipt, await aliased.Content.ReadFromJsonAsync<IamOperationV1>());
            }
            Assert.Equal(after, await MainEffects());
            Assert.Equal(new[] { after.UserVersion, invalidates ? 1 : 0, invalidates ? 1 : 0 }, await Floors());
            await RequireFence(next, independentToken, true);
            if (invalidates)
            {
                using var old = await Projection(next, firstToken, "intake:list", true);
                Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
            }
            await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!, "user-seal.json"),
                JsonSerializer.Serialize(new { Mode = mode, Target = target.Id, OriginalGrantIds = grantIds, IndependentGrantId = independentId,
                    Receipt = receipt, Before = before, After = after, Floors = await Floors(), StartPid = pid, RestartPid = next.ProcessId,
                    FirstCrmCommitAckLost = relay is not null, FinalMainCommitAckLost = mainRelay is not null }));
        }
        finally { await SetSealLogin(true); }

        Task<HttpResponseMessage> Write(AuthenticationApiProcess api, Dictionary<string, string> cookies, IamUserCommandV1 input)
        {
            var path = mode switch
            {
                "legacy-password" => "api/user", "legacy-roles" => $"api/pub/user-role/{target.Id:D}",
                "roles" => $"api/iam/users/{target.Id:D}/roles", "organization" => $"api/iam/users/{target.Id:D}/organization",
                _ => $"api/iam/users/{target.Id:D}",
            };
            object body = mode == "legacy-roles" ? new { input.OperationId, input.ExpectedVersion, input.ExpectedReferencesVersion,
                input.Reason, input.RoleIds, PrimaryRoleId = input.RoleId } : input;
            return api.Send(Request(HttpMethod.Put, path, cookies, body));
        }

        Task<HttpResponseMessage> Projection(AuthenticationApiProcess api, string bearer, string purpose, bool context)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, context ? "connect/inquiry-human-context" : "connect/inquiry-permission")
            {
                Content = context ? JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
                    : JsonContent.Create(new InquiryPermissionRequestV1(purpose, pair.SiteId, pair.FormId)),
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            return api.Send(request);
        }

        async Task RequireFence(AuthenticationApiProcess api, string bearer, bool expected)
        {
            using var response = await Projection(api, bearer, "intake:reject", true);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
            Assert.Equal(expected, response.Headers.Contains("X-CP6-Inquiry-Tenant-Fence"));
            Assert.True(ContextSignature(JsonNode.Parse(raw)!.AsObject(), options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
        }

        async Task<string> Exchange(AuthenticationApiProcess api, Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["code_challenge_method"] = "S256",
                ["code_challenge"] = CrmOidcCrypto.Challenge(verifier), ["state"] = CrmOidcCrypto.RandomToken(),
                ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm",
            });
            using var entry = await api.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var authorization = await api.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
            Assert.NotEmpty(code);
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var response = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("access_token").GetString()!;
        }

        async Task SetSealLogin(bool enable)
        {
            var role = new NpgsqlCommandBuilder().QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(enable ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        async Task RejectCompetingUserSource(AuthenticationApiProcess api, bool administrative)
        {
            var floors = await Floors();
            var intent = await Intent();
            using var request = administrative
                ? Request(HttpMethod.Post, "api/user/reset-2fa", actor, target.Id)
                : Request(HttpMethod.Post, "api/auth/change-password", subject,
                    new { CurrentPassword = "Iam1!Password", NewPassword = "SimulatedCompeting1!" });
            using var response = await api.Send(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Conflict, body);
            using var error = JsonDocument.Parse(body);
            Assert.Equal("IAM_CRM_USER_SEAL_PENDING", error.RootElement.GetProperty("message").GetString());
            Assert.Equal(before, await MainEffects());
            Assert.Equal(floors, await Floors());
            Assert.Equal(intent, await Intent());
        }

        async Task<int[]> Floors()
        {
            var result = new List<int>();
            foreach (var identity in new[] { aggregate }.Concat(grantAggregates))
            {
                await using var sql = new NpgsqlCommand("""
                    SELECT coalesce((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.identity_fences
                        WHERE tenant_id=@tenant AND aggregate_id=@identity),0)
                    """, crm);
                sql.Parameters.AddWithValue("tenant", h.Tenant);
                sql.Parameters.AddWithValue("identity", identity);
                result.Add((int)(await sql.ExecuteScalarAsync())!);
            }
            return result.ToArray();
        }

        Task<string> Intent() => h.Db.Database.SqlQuery<string>($"""
            SELECT coalesce((SELECT row_to_json(p)::text FROM cp6_authentication_v1.pending_crm_user_seals p
                             WHERE tenant_id={h.Tenant} AND operation_id={command.OperationId}), '') AS "Value"
            """).SingleAsync();

        async Task<UserSealEffects> MainEffects()
        {
            var json = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object('User', (SELECT jsonb_build_array(u."Enable",u."AuthenticationEpoch",u."PasswordChangedAt",
                    u."MustChangePassword",u."DeptId",u."ManagerId",u."RoleId",u."NickName",u."ModifyDate") FROM public."Sys_Users" u
                      WHERE u."Id"={target.Id} AND u."TenantId"={h.Tenant}),
                  'UserVersion',(SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}),
                  'Generation',(SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}),
                  'TokenSnapshots',(SELECT count(*) FROM crm_identity."Snapshot" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({grantAggregates})),
                  'Grants',(SELECT count(*) FROM public."CrmOidcGrant" WHERE "Id"=ANY({grantIds})),
                  'RevokedGrants',(SELECT count(*) FROM public."CrmOidcGrant" WHERE "Id"=ANY({grantIds}) AND "Revoked"),
                  'Refresh',(SELECT count(*) FROM public."Sys_RefreshTokens" WHERE "UserId"={target.Id} AND "RevokedAt" IS NOT NULL),
                  'Outbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant}) +
                          (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant}),
                  'Audits',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant}
                            AND "ContextJson"::jsonb->>'operationId'={command.OperationId.ToString("D")}),
                  'Receipts',(SELECT count(*) FROM cp6_authentication_v1.iam_operations WHERE tenant_id={h.Tenant} AND operation_id={command.OperationId}))::text AS "Value"
                """).SingleAsync();
            using var parsed = JsonDocument.Parse(json);
            var e = parsed.RootElement;
            return new(e.GetProperty("User").GetRawText(), e.GetProperty("UserVersion").GetInt32(), e.GetProperty("Generation").GetInt64(),
                e.GetProperty("TokenSnapshots").GetInt32(), e.GetProperty("Grants").GetInt32(), e.GetProperty("RevokedGrants").GetInt32(),
                e.GetProperty("Refresh").GetInt32(), e.GetProperty("Outbox").GetInt32(), e.GetProperty("Audits").GetInt32(), e.GetProperty("Receipts").GetInt32());
        }
    }

    private sealed record UserSealEffects(string User, int UserVersion, long Generation, int TokenSnapshots, int Grants,
        int RevokedGrants, int Refresh, int Outbox, int Audits, int Receipts);

    private static Guid GrantId(string access) => Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(access).Claims.Single(c => c.Type == "sid").Value);

    private static async Task InstallUserSealFixture(NpgsqlConnection crm, NpgsqlConnectionStringBuilder seal, Guid tenant, Guid store)
    {
        var directory = Environment.GetEnvironmentVariable("CP6_W660_CRM_SCHEMA_SQL")!;
        await using var tx = await crm.BeginTransactionAsync();
        foreach (var file in new[] { "SessionSchemaV1", "BaselineSchemaV1", "ProjectionSchemaV1", "IdentityLockSchemaV1",
            "InquiryTenantPauseSchemaV1", "InquiryIdentityFenceSchemaV1", "InquiryStoreTimelineSchemaV1" })
        {
            await using var install = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, file + ".sql")), crm, tx);
            await install.ExecuteNonQueryAsync();
        }
        using var quote = new NpgsqlCommandBuilder();
        var login = quote.QuoteIdentifier(seal.Username!);
        var database = quote.QuoteIdentifier(crm.Database);
        Assert.Matches("^[a-f0-9]{64}$", seal.Password!);
        await using var register = new NpgsqlCommand($"""
            CREATE ROLE {login} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD '{seal.Password}';
            GRANT CONNECT ON DATABASE {database} TO {login};
            GRANT USAGE ON SCHEMA crm_inquiry_tenant_pause_v1 TO {login};
            GRANT EXECUTE ON FUNCTION crm_inquiry_tenant_pause_v1.advance_identity_v1(uuid,uuid,text,integer) TO {login};
            INSERT INTO crm_inquiry_tenant_pause_v1.fences(tenant_id,store_id,seal_login,reopened_at)
            VALUES (@tenant,@store,@login,1);
            INSERT INTO crm_auth."IdentityFenceV1"("TenantId","Revision") VALUES (@tenant,1);
            """, crm, tx);
        register.Parameters.AddWithValue("tenant", tenant);
        register.Parameters.AddWithValue("store", store);
        register.Parameters.AddWithValue("login", seal.Username!);
        await register.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
