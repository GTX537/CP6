using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task<InquirySiteFormScopeV1[]> GrantInquiry(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "crm-site");
        if (menu is null) { menu = new() { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "crm-site", MenuName = "Inquiry" }; h.Db.Sys_Menus.Add(menu); }
        h.Db.Sys_RoleActions.AddRange(new Sys_RoleAction { RoleId = 71, MenuId = menu.MenuId, ActionCode = "query" },
            new Sys_RoleAction { RoleId = 71, MenuId = menu.MenuId, ActionCode = "configure" });
        (await h.Db.Sys_Users.SingleAsync(x => x.Id == h.ActorId)).RoleId = 71;
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var pairs = new InquirySiteFormScopeV1[] { new(Guid.NewGuid(), Guid.NewGuid()), new(Guid.NewGuid(), Guid.NewGuid()) };
        var s = InquiryScopes(h); await s.Service.ChangeAsync(71, await InquiryCommand(h, s.Service, 71, pairs), h.Actor, default);
        return pairs;
    }
    private static InquiryPermissionSubjectV1 InquirySubject(Harness h)
        => new(h.Tenant, h.ActorId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(4));
    private static Task<string> Issue(InquiryPermissionIssuerV1 service, InquiryPermissionSubjectV1 subject, InquirySiteFormScopeV1 pair, string purpose = "configuration:write")
        => service.IssueAsync(new(purpose, pair.SiteId, pair.FormId), (_, _) => Task.FromResult<InquiryPermissionSubjectV1?>(subject), default);

    [TenantManagementPostgresTheory]
    [InlineData("configuration:read")][InlineData("configuration:write")]
    [InlineData("configuration:preview")][InlineData("configuration:activate")]
    public async Task Inquiry_producer_reads_actual_actions_and_emits_one_exact_pair(string purpose)
    {
        await using var h = await Seed(); var pairs = await GrantInquiry(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var subject = InquirySubject(h); var service = new InquiryPermissionIssuerV1(h.Db, key);
        using var body = JsonDocument.Parse(await Issue(service, subject, pairs[0], purpose)); var p = body.RootElement;
        Assert.Equal(h.Tenant, p.GetProperty("organizationId").GetGuid()); Assert.Equal(h.ActorId, p.GetProperty("subjectId").GetGuid());
        Assert.Equal(purpose, p.GetProperty("purpose").GetString());
        Assert.Equal(pairs[0].SiteId, Assert.Single(p.GetProperty("scopes").GetProperty("siteIds").EnumerateArray()).GetGuid());
        Assert.Equal(pairs[0].FormId, Assert.Single(p.GetProperty("scopes").GetProperty("formIds").EnumerateArray()).GetGuid());
        Assert.Empty(p.GetProperty("fieldPermissions").EnumerateArray()); Assert.Empty(p.GetProperty("managedDepartmentIds").EnumerateArray());
        Assert.False(p.GetProperty("isSupervisor").GetBoolean());
        Assert.Equal(purpose == "configuration:read" ? 1 : 2, p.GetProperty("permissions").GetArrayLength());
        Assert.InRange(p.GetProperty("expiresAtUtc").GetDateTimeOffset(), p.GetProperty("issuedAtUtc").GetDateTimeOffset().AddTicks(1), subject.ExpiresAtUtc);
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, subject, new(pairs[0].SiteId, pairs[1].FormId), purpose))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData("action")][InlineData("scope")][InlineData("role")][InlineData("tenant")]
    public async Task Inquiry_producer_rejects_current_revocation_and_foreign_identity(string kind)
    {
        await using var h = await Seed(); var pair = (await GrantInquiry(h))[0]; var subject = InquirySubject(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options()); var service = new InquiryPermissionIssuerV1(h.Db, key);
        await Issue(service, subject, pair);
        switch (kind)
        {
            case "action": await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.ActionCode == "configure").ExecuteDeleteAsync(); break;
            case "scope": var s = InquiryScopes(h); await s.Service.ChangeAsync(71, await InquiryCommand(h, s.Service, 71), h.Actor, default); break;
            case "role": await h.Db.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false)); break;
            case "tenant": subject = subject with { TenantId = Guid.NewGuid() }; break;
        }
        Assert.Equal(kind == "tenant" ? 401 : 403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, subject, pair))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_producer_rereads_session_after_await_and_never_returns_on_commit_failure()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var pair = (await GrantInquiry(h))[0]; var subject = InquirySubject(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options()); var service = new InquiryPermissionIssuerV1(h.Db, key);
        var reads = 0;
        var error = await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => service.IssueAsync(new("configuration:write", pair.SiteId, pair.FormId),
            (_, _) => Task.FromResult<InquiryPermissionSubjectV1?>(++reads == 1 ? subject : null), default));
        Assert.Equal(401, error.Status); Assert.Equal(2, reads);
        fault.Armed = true; await Assert.ThrowsAsync<IOException>(() => Issue(service, subject, pair));
        Assert.NotNull(await Issue(service, subject, pair));
    }

    [TenantManagementPostgresTheory]
    [InlineData("future")][InlineData("revoked")][InlineData("retired")][InlineData("expired")][InlineData("wire-id")]
    public async Task Inquiry_producer_fails_closed_for_key_window_expiry_and_frozen_identity_representation(string kind)
    {
        await using var h = await Seed(); var pair = (await GrantInquiry(h))[0]; var subject = InquirySubject(h); var options = InquiryPermissionSigningTests.Options();
        if (kind == "future") options.ActiveFromUtc = DateTimeOffset.UtcNow.AddHours(1);
        if (kind == "revoked") options.Revoked = true;
        if (kind == "retired") options.ActiveToUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        if (kind == "expired") subject = subject with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
        if (kind == "wire-id")
        {
            pair = pair with { FormId = Guid.Parse("11111111-1111-1111-8111-111111111111") };
            var s = InquiryScopes(h); await s.Service.ChangeAsync(71, await InquiryCommand(h, s.Service, 71, pair), h.Actor, default);
        }
        using var key = InquiryPermissionSigningTests.Signer(options);
        Assert.Equal(kind == "expired" ? 401 : kind == "wire-id" ? 422 : 503,
            (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(new(h.Db, key), subject, pair))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_producer_waiting_for_native_authority_observes_committed_role_revocation()
    {
        await using var h = await Seed(); var pair = (await GrantInquiry(h))[0]; var subject = InquirySubject(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var writer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await writer.OpenAsync();
        await using var tx = await writer.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"RoleId\"=71", writer, tx);
        command.Parameters.AddWithValue("tenant", h.Tenant); await command.ExecuteNonQueryAsync();
        await h.Db.Database.OpenConnectionAsync(); var reader = (NpgsqlConnection)h.Db.Database.GetDbConnection();
        var pending = Issue(new(h.Db, key), subject, pair);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await observer.OpenAsync();
        await using var blocked = new NpgsqlCommand("SELECT @writer=ANY(pg_blocking_pids(@reader))", observer);
        blocked.Parameters.AddWithValue("writer", writer.ProcessID); blocked.Parameters.AddWithValue("reader", reader.ProcessID);
        var waiting = false;
        for (var n = 0; n < 100 && !waiting; n++) { waiting = (bool)(await blocked.ExecuteScalarAsync())!; if (!waiting) await Task.Delay(10); }
        Assert.True(waiting, "Observed PostgreSQL blocker is required"); await tx.CommitAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(async () => await pending)).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_producer_actual_HTTP_login_OIDC_signing_rejects_spoofing_and_current_revocation()
        => await InquiryProducerHttp("configuration:write");

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_intake_actual_HTTP_login_OIDC_signing_rejects_spoofing_and_current_revocation()
        => await InquiryProducerHttp("intake:release");

    private async Task InquiryProducerHttp(string purpose, bool humanContext=false)
    {
        await using var h = await Seed(); var intake = purpose.StartsWith("intake:", StringComparison.Ordinal);
        var consent = purpose == "privacy:consent-withdraw";
        var pair = consent ? await GrantConsent(h) : intake ? await GrantIntake(h) : (await GrantInquiry(h))[0]; var options = InquiryPermissionSigningTests.Options();
        if(humanContext)
        {
            (await h.Db.Sys_Users.SingleAsync(x=>x.Id==h.ActorId)).AuthenticationEpoch=Guid.NewGuid();await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            await new IdentityBootstrapService(h.Db,ContextRuntime(h.Tenant,options.Issuer)).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        }
        var bridgeDirectory = Environment.GetEnvironmentVariable("CP6_INQUIRY_CONFIGURATION_BRIDGE");
        if (intake || consent) bridgeDirectory = null;
        if (bridgeDirectory is not null)
            await h.Db.Sys_Users.Where(x => x.Id == h.ActorId).ExecuteUpdateAsync(x => x.SetProperty(u => u.DeptId, h.Parent));
        var environment = IdentityEnvironment(h.Tenant); const string clientSecret = "owned-local-client-secret";
        const string redirect = "https://crm.cp6.test/callback";
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer; environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);
        var cookies = await Login(host, h); var verifier = CrmOidcCrypto.RandomToken();
        var authorize = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?> {
            ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["code_challenge_method"] = "S256",
            ["code_challenge"] = CrmOidcCrypto.Challenge(verifier), ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm" });
        using var authorized = await host.Send(Request(HttpMethod.Get, authorize, cookies));
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode); var bridge = authorized.Headers.Location!;
        Assert.Equal(options.Issuer, bridge.GetLeftPart(UriPartial.Authority)); Assert.Equal("/api/auth/crm-authorize", bridge.AbsolutePath);
        using var continued = await host.Send(Request(HttpMethod.Get, bridge.PathAndQuery, cookies));
        Assert.Equal(HttpStatusCode.Redirect, continued.StatusCode); var location = continued.Headers.Location!;
        Assert.StartsWith(redirect, location.ToString()); var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString(); Assert.NotEmpty(code);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "connect/token") { Content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier }) };
        tokenRequest.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
        using var tokenResponse = await host.Send(tokenRequest); Assert.True(tokenResponse.StatusCode == HttpStatusCode.OK, await tokenResponse.Content.ReadAsStringAsync());
        using var tokenBody = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()); var token = tokenBody.RootElement.GetProperty("access_token").GetString()!;
        var command = new InquiryPermissionRequestV1(purpose, pair.SiteId, pair.FormId);
        var nonce=ServiceNonce(); var endpoint=humanContext?"connect/inquiry-human-context":"connect/inquiry-permission";
        HttpRequestMessage Projection(object body, string? bearer, bool secure = true)
        { if(humanContext && body is InquiryPermissionRequestV1 b) body=new InquiryHumanContextRequestV1(b.Purpose,b.SiteId,b.FormId,nonce);
          var r = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
            if (bearer is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (secure) r.Headers.Add("X-Forwarded-Proto", "https"); return r; }
        // Unsafe cookie/no-auth requests remain subject to the existing CSRF middleware.
        using (var noToken = await host.Send(Projection(command, null))) Assert.Equal(HttpStatusCode.Forbidden, noToken.StatusCode);
        using (var invalidToken = await host.Send(Projection(command, "invalid"))) Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
        using (var cookieOnly = await host.Send(Request(HttpMethod.Post, endpoint, cookies, command))) Assert.NotEqual(HttpStatusCode.OK, cookieOnly.StatusCode);
        using (var insecure = await host.Send(Projection(command, token, false))) Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        using (var forged = await host.Send(Projection(new { command.Purpose, command.SiteId, command.FormId, SubjectId = Guid.NewGuid() }, token))) Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var signed = await host.Send(Projection(command, token)); var json = await signed.Content.ReadAsStringAsync();
        Assert.True(signed.StatusCode == HttpStatusCode.OK, json); Assert.True(signed.Headers.CacheControl?.NoStore);
        using var outer=JsonDocument.Parse(json);
        if(humanContext && Environment.GetEnvironmentVariable("CP6_HUMAN_CONTEXT_OUTPUT") is {Length:>0} humanOutput)
        {
            var sources=await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x=>x.TenantId==h.Tenant).OrderBy(x=>x.AggregateId).ToArrayAsync();
            await File.WriteAllTextAsync(humanOutput,JsonSerializer.Serialize(new{contextJson=json,publicKeyBase64=options.PublicKeyBase64,activeFromUtc=options.ActiveFromUtc,
                departmentId=h.Parent,rows=sources.Select(x=>new{x.AggregateId,x.EventType,x.Version,x.PayloadSha256,x.IsDeleted,payloadJson=x.PayloadJson}),
                producer="actual Main login/OIDC/HTTP; unchanged signed bytes and persisted source payloads; isolated PostgreSQL"},IdentityEventContracts.Json));
        }
        if(humanContext){Assert.Equal(nonce,outer.RootElement.GetProperty("requestNonce").GetString());Assert.True(ContextSignature(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject(),options.PublicKeyBase64,InquiryPermissionSigningKeyV1.HumanContextVersion));}
        using var document = JsonDocument.Parse(humanContext?outer.RootElement.GetProperty("permissionJson").GetString()!:json); Assert.Equal(h.ActorId, document.RootElement.GetProperty("subjectId").GetGuid());
        if (intake)
        {
            Assert.True(document.RootElement.GetProperty("isSupervisor").GetBoolean());
            Assert.Equal(6, document.RootElement.GetProperty("fieldPermissions").GetArrayLength());
            Assert.Equal(2, document.RootElement.GetProperty("managedDepartmentIds").GetArrayLength());
            if(purpose is "intake:list" or "intake:detail")
            {
                var actions=document.RootElement.GetProperty("permissions").EnumerateArray().Select(x=>x.GetString());
                Assert.Contains("crm-lead:add",actions);Assert.Contains("crm-lead:edit",actions);
            }
        }
        if (Environment.GetEnvironmentVariable(intake ? "CP6_INQUIRY_INTAKE_PROJECTION_OUTPUT" : "CP6_INQUIRY_PROJECTION_OUTPUT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { projection = document.RootElement,
                publicKeyBase64 = options.PublicKeyBase64, activeFromUtc = options.ActiveFromUtc, producer = "actual Main login/OIDC/HTTP endpoint; local isolated PostgreSQL" }));
        // Optional two-process integration. The isolated-run directory is supplied by the local
        // harness, never by a request. Only its private session file contains the short-lived token.
        if (bridgeDirectory is not null)
        {
            var root = Path.GetFullPath(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!);
            bridgeDirectory = Path.GetFullPath(bridgeDirectory);
            Assert.StartsWith(root + Path.DirectorySeparatorChar, bridgeDirectory, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(bridgeDirectory);
            await File.WriteAllTextAsync(Path.Combine(bridgeDirectory, "session.json"), JsonSerializer.Serialize(new
            {
                marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = options.Issuer, origin = host.Origin.ToString(),
                accessToken = token, idToken = tokenBody.RootElement.GetProperty("id_token").GetString(),
                tenantId = h.Tenant, subjectId = h.ActorId, departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId,
                keyId = options.KeyId, publicKeyBase64 = options.PublicKeyBase64, activeFromUtc = options.ActiveFromUtc
            }));
            await WaitConfigurationBridge(bridgeDirectory, "saved");
        }
        var revokeAction = consent ? "consent-withdraw" : intake ? purpose is "intake:list" or "intake:detail" ? "query" : "add" : "configure";
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.ActionCode == revokeAction).ExecuteDeleteAsync();
        using (var revoked = await host.Send(Projection(command, token))) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        if (bridgeDirectory is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(bridgeDirectory, "revoked"), "actual Main configure action revoked");
            await WaitConfigurationBridge(bridgeDirectory, "done");
            File.Delete(Path.Combine(bridgeDirectory, "session.json"));
        }
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"CrmOidcGrant\" SET \"Revoked\"=true WHERE \"SubjectId\"={h.ActorId}");
        using var revokedSession = await host.Send(Projection(command with { Purpose = humanContext ? "intake:list" : "configuration:read" }, token)); Assert.Equal(HttpStatusCode.Unauthorized, revokedSession.StatusCode);
    }

    private static async Task WaitConfigurationBridge(string directory, string stage)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (!File.Exists(Path.Combine(directory, stage))) await Task.Delay(100, timeout.Token);
        Assert.Equal("PASS", await File.ReadAllTextAsync(Path.Combine(directory, stage), timeout.Token));
    }
}
