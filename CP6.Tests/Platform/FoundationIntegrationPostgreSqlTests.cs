using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class FoundationIntegrationPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Http_role_changes_update_current_permissions_and_identity_atomically_across_tenants()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var runtime = Runtime(a, b);
        await using (var db = server.Context(new TenantContext()))
        {
            db.Sys_Menus.AddRange(new Sys_Menu { MenuId = 72004, MenuKey = "role", MenuName = "Roles" },
                new Sys_Menu { MenuId = 72005, MenuKey = "crm-foundation", MenuName = "CRM fixture" });
            await db.SaveChangesAsync();
        }
        async Task<(Guid Reader, Guid Editor)> Seed(Guid tenant)
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }, runtime);
            db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "tenant" });
            db.Sys_Roles.AddRange(new Sys_Role { RoleId = 10, RoleName = "reader" }, new Sys_Role { RoleId = 20, RoleName = "editor" });
            var reader = new Sys_User { UserName = "reader", Password = "fixture-only", RoleId = 10 };
            var editor = new Sys_User { UserName = "editor", Password = "fixture-only", RoleId = 20 };
            db.Sys_Users.AddRange(reader, editor);
            db.Sys_RoleActions.AddRange(new Sys_RoleAction { RoleId = 10, MenuId = 72004, ActionCode = "query" },
                new Sys_RoleAction { RoleId = 10, MenuId = 72005, ActionCode = "read" },
                new Sys_RoleAction { RoleId = 20, MenuId = 72004, ActionCode = "edit" });
            await db.SaveChangesAsync(); return (reader.Id, editor.Id);
        }
        var usersA = await Seed(a); var usersB = await Seed(b);
        await using var host = await TenantManagementHttpHost.Start(server, runtime, includeRoles: true);
        var readerA = host.Token(usersA.Reader, a, platform: false); var editorA = host.Token(usersA.Editor, a, platform: false);
        var readerB = host.Token(usersB.Reader, b, platform: false);
        // This old unversioned allow must never authorize a later request after role revocation.
        await host.Services.GetRequiredService<IDistributedCache>().SetStringAsync($"perm-ctx:{usersA.Reader}",
            JsonSerializer.Serialize(new UserPermissionContext { UserId = usersA.Reader, ActionKeys = ["role:query"] }));
        async Task Get(string token, Guid tenant, HttpStatusCode expected)
        {
            using var response = await Send(host, token, HttpMethod.Get, "api/role?pageSize=100");
            Assert.Equal(expected, response.StatusCode);
            if (expected != HttpStatusCode.OK) return;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = body.RootElement.GetProperty("rows").EnumerateArray().ToArray();
            Assert.Equal(2, rows.Length); Assert.All(rows, row => Assert.Equal(tenant, row.GetProperty("tenantId").GetGuid()));
        }
        object Role(bool enabled) => new { RoleId = 10, RoleName = "reader", Enable = enabled, TenantId = b };
        await Get(readerA, a, HttpStatusCode.OK); await Get(readerB, b, HttpStatusCode.OK);
        using (var response = await Send(host, editorA, HttpMethod.Put, "api/role", Role(false)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Get(readerA, a, HttpStatusCode.Forbidden); await Get(readerB, b, HttpStatusCode.OK);
        await Get(host.Token(usersA.Reader, b, platform: false), b, HttpStatusCode.Forbidden);
        await AssertAggregate(a, "permission:role:10", 2, false, runtime);
        await AssertAggregate(b, "permission:role:10", 1, true, runtime);

        var before = await State(a); var constraint = "reject_role_" + a.ToString("N");
        await Ddl($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" ADD CONSTRAINT {constraint} CHECK (\"TenantId\" <> '{a:D}'::uuid) NOT VALID");
        try
        {
            using var response = await Send(host, editorA, HttpMethod.Put, "api/role", Role(true));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, await State(a)); await Get(readerA, a, HttpStatusCode.Forbidden);
        }
        finally { await Ddl($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" DROP CONSTRAINT {constraint}"); }
        using (var response = await Send(host, editorA, HttpMethod.Put, "api/role", Role(true)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Get(readerA, a, HttpStatusCode.OK);
        await AssertAggregate(a, "permission:role:10", 3, true, runtime);
        await AssertAggregate(b, "permission:role:10", 1, true, runtime);
    }

    [CurrentPermissionPostgresFact]
    public async Task Http_tenant_management_audit_snapshot_and_messages_share_success_and_failure()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id); var code = id.ToString("N");
        var actor = new Sys_User { UserName = "platform-" + code, Password = "fixture-only", IsPlatformAdmin = true };
        await using (var seed = server.Context(new TenantContext(), runtime))
        {
            seed.Sys_Users.Add(actor);
            seed.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = code, TenantName = "HTTP target" });
            await seed.SaveChangesAsync();
        }
        await using var host = await TenantManagementHttpHost.Start(server, runtime);
        var token = host.Token(actor.Id, TenantContext.DefaultTenant);
        using (var response = await Send(host, token, HttpMethod.Post, $"api/platform/tenant/{id:D}/suspend"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertAggregate(id, $"tenant:{id:D}", 2, false, runtime);
        await using (var db = server.Context(new TenantContext()))
        {
            var audit = await db.Sys_SecurityLogs.IgnoreQueryFilters().SingleAsync(x => x.RequestTenantCode == code);
            Assert.Equal(actor.Id, audit.UserId); Assert.Equal(TenantContext.DefaultTenant, audit.TenantId);
            Assert.Equal((int)SecurityEventType.TenantSuspended, audit.EventType); Assert.Contains(id.ToString("D"), audit.Reason);
            Assert.False((await db.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable);
        }
        var before = await State(id); var constraint = "reject_reactivation_" + code;
        await using var schema = server.Context(new TenantContext());
        var logTable = schema.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            schema.Model.FindEntityType(typeof(Sys_SecurityLog))!.GetTableName()!, "public");
        await Ddl($"ALTER TABLE {logTable} ADD CONSTRAINT {constraint} CHECK (\"RequestTenantCode\" <> '{code}' OR \"EventType\" <> {(int)SecurityEventType.TenantReactivated}) NOT VALID");
        try
        {
            using var response = await Send(host, token, HttpMethod.Post, $"api/platform/tenant/{id:D}/reactivate");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, await State(id));
        }
        finally { await Ddl($"ALTER TABLE {logTable} DROP CONSTRAINT {constraint}"); }
        using (var response = await Send(host, token, HttpMethod.Post, $"api/platform/tenant/{id:D}/reactivate"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertAggregate(id, $"tenant:{id:D}", 3, true, runtime);
        await using var verify = server.Context(new TenantContext());
        Assert.True((await verify.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable);
        Assert.Equal(2, await verify.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(x => x.RequestTenantCode == code));
    }

    private static async Task<HttpResponseMessage> Send(TenantManagementHttpHost host, string token,
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await host.Client.SendAsync(request);
    }

    private async Task Ddl(string sql)
    {
        await using var db = server.Context(new TenantContext());
        // Only fixed test identifiers and generated GUIDs are interpolated by the callers.
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<string[]> State(Guid tenant)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant });
        await using var priority = server.PriorityContext();
        var state = new List<string> { "tenant:" + (await db.Sys_Tenants.SingleAsync(x => x.Id == tenant)).Enable,
            "security:" + await db.Sys_SecurityLogs.IgnoreQueryFilters().CountAsync(),
            "field:" + await db.Sys_FieldAuditLogs.IgnoreQueryFilters().CountAsync() };
        state.AddRange((await db.Sys_Roles.ToArrayAsync()).Select(x => $"role:{x.RoleId}:{x.Enable}:{x.RoleName}"));
        state.AddRange((await db.CrmIdentitySnapshots.Where(x => x.TenantId == tenant).ToArrayAsync())
            .Select(x => $"snapshot:{x.AggregateId}:{x.Version}:{x.PayloadSha256}:{Convert.ToHexString(x.RowVersion)}"));
        state.AddRange((await db.CrmIdentityTenantGenerations.Where(x => x.TenantId == tenant).ToArrayAsync()).Select(x => $"generation:{x.Generation}"));
        state.AddRange((await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant).ToArrayAsync()).Select(x => $"normal:{x.MessageId}:{x.PayloadSha256}"));
        state.AddRange((await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant).ToArrayAsync()).Select(x => $"priority:{x.MessageId}:{x.PayloadSha256}"));
        return state.Order(StringComparer.Ordinal).ToArray();
    }

    private async Task AssertAggregate(Guid tenant, string aggregate, int version, bool enabled, CrmIdentityRuntime runtime)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant });
        await using var priority = server.PriorityContext();
        var snapshot = await db.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate);
        Assert.Equal(version, snapshot.Version);
        using var data = JsonDocument.Parse(snapshot.PayloadJson);
        Assert.Equal(enabled, data.RootElement.GetProperty("enabled").GetBoolean());
        var normalMessages = await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync();
        var priorityMessages = await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync();
        Assert.Equal(Enumerable.Range(1, version), normalMessages.Concat(priorityMessages).Select(x => x.AggregateVersion).Order());
        var current = Assert.Single((enabled ? normalMessages : priorityMessages).Where(x => x.AggregateVersion == version));
        Assert.True(runtime.Validator.IsValid(current.Payload));
        using var envelope = JsonDocument.Parse(current.Payload);
        Assert.Equal(snapshot.PayloadJson, envelope.RootElement.GetProperty("data").GetRawText());
    }

    private static CrmIdentityRuntime Runtime(params Guid[] tenants)
    {
        const string issuer = "https://foundation-integration.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        foreach (var tenant in tenants) options.Tenants.Add(tenant, "us");
        return new CrmIdentityRuntime(options, new IdentityEventValidator(
            Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer));
    }
}
