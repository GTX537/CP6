using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private const string ServiceClient = "inquiry-h01";
    private const string ServiceSecret = "owned-local-inquiry-service-secret";
    private static StringValues ServiceBasic(string secret = ServiceSecret)
        => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(ServiceClient + ":" + secret));
    private static (InquiryServicePrincipalOptionsV1 Source, CrmOidcOptions Oidc) ServiceRegistration(Harness h, Guid subject, int[]? roles = null)
        => (new() { Enabled = true, Version = "owned-test-v1", Registrations = [new() {
                ClientId = ServiceClient, SubjectId = subject, DepartmentId = h.Parent, RoleIds = roles ?? [71] }] },
            new() { Enabled = true, Organizations = [new() { TenantId = h.Tenant, CrmEnabled = true }],
                ServiceClients = [new() { ClientId = ServiceClient, TenantId = h.Tenant, Enabled = true,
                    SecretSha256 = CrmOidcCrypto.Hash(ServiceSecret), AllowedScopes = ["cp6.services"] }] });
    private ServiceFixture ServiceIssuer(Harness h, InquiryPermissionSigningKeyV1 key, Guid subject, int[]? roles = null,
        AuthorizationCatalogSnapshotV1? catalog = null)
    {
        var config = ServiceRegistration(h, subject, roles);
        var registry = new InquiryServicePrincipalRegistryV1(config.Source, config.Oidc);
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        var db = server.Context(tenant);
        return new(db, tenant, new(db, tenant, registry, key, () => catalog ?? IntakeCatalog()));
    }
    private sealed record ServiceFixture(CP6Context Db, TenantContext Tenant, InquiryServicePermissionIssuerV1 Issuer) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Db.DisposeAsync(); }
    private static Task<string> ServiceIssue(ServiceFixture f, InquirySiteFormScopeV1 pair, string purpose = "intake:release")
        => f.Issuer.IssueAsync(new(purpose, pair.SiteId, pair.FormId), ServiceBasic(), default);

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_uses_registered_principal_actual_IAM_and_restores_tenant_without_human_session()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h); var subject = Guid.NewGuid();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, subject); var prior = service.Tenant.CurrentTenantId;
        using var document = JsonDocument.Parse(await ServiceIssue(service, pair)); var p = document.RootElement;
        Assert.Equal(subject, p.GetProperty("subjectId").GetGuid()); Assert.NotEqual(h.ActorId, subject);
        Assert.Equal(h.Tenant, p.GetProperty("organizationId").GetGuid());
        Assert.Equal("intake:release", p.GetProperty("purpose").GetString()); Assert.True(p.GetProperty("isSupervisor").GetBoolean());
        Assert.Equal(new[] { h.Parent, h.Child }.Order(), p.GetProperty("managedDepartmentIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        Assert.Equal(6, p.GetProperty("fieldPermissions").GetArrayLength()); Assert.Equal(3, p.GetProperty("permissions").GetArrayLength());
        Assert.Equal(prior, service.Tenant.CurrentTenantId);
        Assert.False(await h.Db.Sys_Users.AnyAsync(x => x.Id == subject));
        Assert.Empty(service.Db.ChangeTracker.Entries());
        var second = await ServiceIssue(service, pair); Assert.Contains(subject.ToString("D"), second);
    }

    [TenantManagementPostgresTheory]
    [InlineData("role")][InlineData("action")][InlineData("field")][InlineData("department")][InlineData("scope")][InlineData("tenant")]
    public async Task Inquiry_service_current_revocation_prevents_new_projection(string kind)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, Guid.NewGuid()); await ServiceIssue(service, pair);
        switch (kind)
        {
            case "role": await h.Db.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false)); break;
            case "action": await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.ActionCode == "add").ExecuteDeleteAsync(); break;
            case "field": h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake", FieldName = "email", Access = 3 }); await h.Db.SaveChangesAsync(); break;
            case "department": await h.Db.Sys_Depts.Where(x => x.Id == h.Parent).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, false)); break;
            case "scope": var s = InquiryScopes(h); await s.Service.ChangeAsync(71, await InquiryCommand(h, s.Service, 71), h.Actor, default); break;
            case "tenant": await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, false)); break;
        }
        var prior = service.Tenant.CurrentTenantId;
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Status);
        Assert.Equal(prior, service.Tenant.CurrentTenantId);
    }

    [TenantManagementPostgresTheory]
    [InlineData("human")][InlineData("unknown-field")][InlineData("bad-access")][InlineData("unsealed")][InlineData("partial")]
    public async Task Inquiry_service_does_not_alias_a_human_or_fabricate_missing_field_facts(string failure)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        if (failure is "unknown-field" or "bad-access")
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake",
                FieldName = failure == "unknown-field" ? "Email" : "email", Access = failure == "bad-access" ? 0 : 1 });
            await h.Db.SaveChangesAsync();
        }
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, failure == "human" ? h.ActorId : Guid.NewGuid(),
            catalog: IntakeCatalog(failure != "unsealed", failure != "partial"));
        if (failure == "human")
            Assert.Equal("inquiry_service_identity_collision", (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Message);
        else await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => ServiceIssue(service, pair));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_field_MIN_uses_actual_roles_without_filling_missing_role_rows_with_defaults()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake", FieldName = "email", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, Guid.NewGuid(), [71, 72]);
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "crm-intake", FieldName = "email", Access = 2 });
        await h.Db.SaveChangesAsync(); Assert.NotEmpty(await ServiceIssue(service, pair));
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task Inquiry_service_projects_only_current_healthy_department_scope(int scope)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h, scope);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, Guid.NewGuid());
        if (scope == 1) { Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Status); return; }
        using var doc = JsonDocument.Parse(await ServiceIssue(service, pair));
        Assert.Equal((scope == 2 ? new[] { h.Parent } : scope == 4 ? [h.Child] : [h.Parent, h.Child]).Order(),
            doc.RootElement.GetProperty("managedDepartmentIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        var other = pair with { FormId = Guid.NewGuid() };
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, other))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData("role")][InlineData("field")][InlineData("department")]
    public async Task Inquiry_service_waits_on_actual_authority_and_observes_committed_revocation(string kind)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake", FieldName = "email", Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var service = ServiceIssuer(h, key, Guid.NewGuid());
        await using var writer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await writer.OpenAsync();
        await using var tx = await writer.BeginTransactionAsync();
        var update = kind switch
        {
            "role" => "UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"RoleId\"=71",
            "field" => "UPDATE public.\"Sys_RoleFieldPerm\" SET \"Access\"=3 WHERE \"TenantId\"=@tenant AND \"RoleId\"=71",
            _ => "UPDATE public.\"Sys_Dept\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"Id\"=@department"
        };
        await using var sql = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); " + update, writer, tx);
        sql.Parameters.AddWithValue("tenant", h.Tenant); sql.Parameters.AddWithValue("department", h.Parent); await sql.ExecuteNonQueryAsync();
        await service.Db.Database.OpenConnectionAsync(); var reader = (NpgsqlConnection)service.Db.Database.GetDbConnection();
        var pending = ServiceIssue(service, pair);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await observer.OpenAsync();
        await using var blocked = new NpgsqlCommand("SELECT @writer=ANY(pg_blocking_pids(@reader))", observer);
        blocked.Parameters.AddWithValue("writer", writer.ProcessID); blocked.Parameters.AddWithValue("reader", reader.ProcessID);
        var waiting = false;
        for (var n = 0; n < 200 && !waiting; n++) { waiting = (bool)(await blocked.ExecuteScalarAsync())!; if (!waiting) await Task.Delay(10); }
        Assert.True(waiting, "Actual PostgreSQL blocker required"); await tx.CommitAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(async () => await pending)).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_endpoint_uses_only_registered_Basic_and_current_IAM_over_actual_HTTP()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h); var subject = Guid.NewGuid();
        var signing = InquiryPermissionSigningTests.Options(); var env = IdentityEnvironment(h.Tenant);
        env["CrmOidc__Issuer"] = signing.Issuer; env["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        env["CrmOidc__ServiceClients__1__ClientId"] = ServiceClient;
        env["CrmOidc__ServiceClients__1__SecretSha256"] = CrmOidcCrypto.Hash(ServiceSecret);
        env["CrmOidc__ServiceClients__1__TenantId"] = h.Tenant.ToString("D");
        env["CrmOidc__ServiceClients__1__Enabled"] = "true"; env["CrmOidc__ServiceClients__1__AllowedScopes__0"] = "cp6.services";
        env["InquiryServicePrincipals__Enabled"] = "true"; env["InquiryServicePrincipals__Version"] = "owned-http-v1";
        env["InquiryServicePrincipals__Registrations__0__ClientId"] = ServiceClient;
        env["InquiryServicePrincipals__Registrations__0__SubjectId"] = subject.ToString("D");
        env["InquiryServicePrincipals__Registrations__0__DepartmentId"] = h.Parent.ToString("D");
        env["InquiryServicePrincipals__Registrations__0__RoleIds__0"] = "71";
        env["InquiryPermissionSigning__Enabled"] = "true"; env["InquiryPermissionSigning__Issuer"] = signing.Issuer;
        env["InquiryPermissionSigning__KeyId"] = signing.KeyId; env["InquiryPermissionSigning__PrivateKeyBase64"] = signing.PrivateKeyBase64;
        env["InquiryPermissionSigning__PublicKeyBase64"] = signing.PublicKeyBase64;
        env["InquiryPermissionSigning__ActiveFromUtc"] = signing.ActiveFromUtc.ToString("O");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, env, allowAutoRedirect: false);
        var command = new InquiryPermissionRequestV1("intake:release", pair.SiteId, pair.FormId);
        HttpRequestMessage RequestService(object body, string? auth = null, bool secure = true)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-service-permission") { Content = JsonContent.Create(body) };
            if (auth is not null) r.Headers.TryAddWithoutValidation("Authorization", auth);
            if (secure) r.Headers.Add("X-Forwarded-Proto", "https"); return r;
        }
        using (var missing = await host.Send(RequestService(command))) Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using (var wrong = await host.Send(RequestService(command, ServiceBasic("wrong").ToString()))) Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using (var bearer = await host.Send(RequestService(command, "Bearer invalid"))) Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);
        using (var insecure = await host.Send(RequestService(command, ServiceBasic().ToString(), false))) Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        using (var forged = await host.Send(RequestService(new { command.Purpose, command.SiteId, command.FormId, SubjectId = h.ActorId }, ServiceBasic().ToString())))
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using (var purpose = await host.Send(RequestService(command with { Purpose = "configuration:write" }, ServiceBasic().ToString())))
            Assert.Equal(HttpStatusCode.BadRequest, purpose.StatusCode);
        var cookies = await Login(host, h);
        using (var cookieRequest = RequestService(command))
        {
            cookieRequest.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
            using var cookieOnly = await host.Send(cookieRequest); Assert.Equal(HttpStatusCode.Unauthorized, cookieOnly.StatusCode);
        }
        using var signedRequest = RequestService(command, ServiceBasic().ToString());
        signedRequest.Headers.Add("tenantid", Guid.NewGuid().ToString("D"));
        signedRequest.Headers.Add("X-Subject-Id", h.ActorId.ToString("D"));
        signedRequest.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        using var signed = await host.Send(signedRequest); var json = await signed.Content.ReadAsStringAsync();
        Assert.True(signed.StatusCode == HttpStatusCode.OK, json); Assert.True(signed.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(json); Assert.Equal(subject, document.RootElement.GetProperty("subjectId").GetGuid());
        Assert.Equal(h.Tenant, document.RootElement.GetProperty("organizationId").GetGuid());
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.ActionCode == "add").ExecuteDeleteAsync();
        using var revoked = await host.Send(RequestService(command, ServiceBasic().ToString())); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_commit_failure_and_cancel_never_return_a_projection_or_leak_tenant_context()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var config = ServiceRegistration(h, Guid.NewGuid()); var registry = new InquiryServicePrincipalRegistryV1(config.Source, config.Oidc);
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; var prior = tenant.CurrentTenantId; var fault = new CommitFault();
        await using var db = server.Context(tenant, null, fault);
        var issuer = new InquiryServicePermissionIssuerV1(db, tenant, registry, key, () => IntakeCatalog());
        var request = new InquiryPermissionRequestV1("intake:release", pair.SiteId, pair.FormId);
        fault.Armed = true; await Assert.ThrowsAsync<IOException>(() => issuer.IssueAsync(request, ServiceBasic(), default));
        Assert.Equal(prior, tenant.CurrentTenantId); Assert.Null(db.Database.CurrentTransaction);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => issuer.IssueAsync(request, ServiceBasic(), cancellation.Token));
        Assert.Equal(prior, tenant.CurrentTenantId); Assert.NotEmpty(await issuer.IssueAsync(request, ServiceBasic(), default));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_expiry_is_bounded_by_actual_tenant_deadline_and_other_roles_are_not_borrowed()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var absent = ServiceIssuer(h, key, Guid.NewGuid(), [int.MinValue]);
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(absent, pair))).Status);
        await using var service = ServiceIssuer(h, key, Guid.NewGuid());
        var deadline = DateTime.Now.AddMinutes(2);
        await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpireDate, deadline));
        using var doc = JsonDocument.Parse(await ServiceIssue(service, pair));
        Assert.InRange(doc.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset(),
            new DateTimeOffset(deadline).AddMilliseconds(-1), new DateTimeOffset(deadline));
        await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpireDate, DateTime.Now.AddMinutes(-1)));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => ServiceIssue(service, pair))).Status);
    }
}
