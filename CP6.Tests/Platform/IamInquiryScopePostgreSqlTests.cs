using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static (IamInquiryScopeAdministrationV1 Service, IamAdministrationV1 Authority) InquiryScopes(Harness h)
    { var p = PolicySession(h); return (new(h.Db, p.Authority), p.Authority); }
    private static async Task<IamInquiryScopeCommandV1> InquiryCommand(Harness h, IamInquiryScopeAdministrationV1 service,
        int role = 72, params InquirySiteFormScopeV1[] pairs)
    {
        var e = await service.ReadAsync(role, h.Actor, default);
        return new() { OperationId = Guid.NewGuid(), ExpectedVersion = e.Version, ExpectedReferencesVersion = e.ReferencesVersion,
            Reason = "Owned explicit resource scope", SiteForms = pairs };
    }
    [TenantManagementPostgresTheory]
    [InlineData(72)][InlineData(0)][InlineData(int.MinValue)][InlineData(int.MaxValue)]
    public async Task Inquiry_scope_original_command_commits_exact_pairs_with_v3_snapshot_outbox_and_audit(int role)
    {
        await using var h = await Seed(); var s = InquiryScopes(h);
        if (role != 72) { h.Db.Sys_Roles.Add(new() { RoleId = role, RoleName = "full integer" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "crm-site");
        if (menu is null) { menu = new() { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "crm-site", MenuName = "Owned site fixture" }; h.Db.Sys_Menus.Add(menu); }
        h.Db.Sys_RoleActions.AddRange(new Sys_RoleAction { RoleId = role, MenuId = menu.MenuId, ActionCode = "query" },
            new Sys_RoleAction { RoleId = role, MenuId = menu.MenuId, ActionCode = "configure" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var pair = new InquirySiteFormScopeV1(Guid.NewGuid(), Guid.NewGuid());
        var command = await InquiryCommand(h, s.Service, role, pair);
        var result = await s.Service.ChangeAsync(role, command, h.Actor, default);
        Assert.Equal("COMMITTED", result.State); Assert.Equal(role, result.RoleId);
        var snapshot = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == "permission:role:" + role);
        Assert.Equal(IdentityEventContracts.PermissionChangedV3, snapshot.EventType);
        var data = JsonSerializer.Deserialize<PermissionIdentityDataV3>(snapshot.PayloadJson, IdentityEventContracts.Json)!;
        Assert.Equal(new[] { pair }, data.SiteFormScopes);
        var message = await h.Db.Set<Cp6OutboxMessage>().SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == snapshot.AggregateId && x.AggregateVersion == snapshot.Version);
        Assert.Contains("siteFormScopes", System.Text.Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(result, await s.Service.ChangeAsync(role, command, h.Actor, default));
        Assert.Equal(result, await s.Authority.Resolve(command.OperationId, IamInquiryScopeAdministrationV1.Kind, false, h.Actor, default));
        Assert.Equal(409, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => s.Service.ChangeAsync(71, command, h.Actor, default))).Status);
        Assert.Equal(1, await h.Db.Sys_SecurityLogs.CountAsync(x => x.EventType == (int)SecurityEventType.IamAdministrationChanged));
        Assert.Equal(0L, await h.Db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM cp6_inquiry_scope_v1.pending_roles").SingleAsync());
        // Retain a real producer envelope for the separate CRM native ingress run.
        if (role == 72 && Environment.GetEnvironmentVariable("CP6_INQUIRY_SCOPE_EVENT_OUTPUT") is { Length: > 0 } path)
            await File.WriteAllBytesAsync(path, message.Payload);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_clear_disable_delete_and_sticky_version_preserve_revocation()
    {
        await using var h = await Seed(); var s = InquiryScopes(h); var pair = new InquirySiteFormScopeV1(Guid.NewGuid(), Guid.NewGuid());
        await s.Service.ChangeAsync(72, await InquiryCommand(h, s.Service, 72, pair), h.Actor, default);
        await s.Service.ChangeAsync(72, await InquiryCommand(h, s.Service), h.Actor, default);
        async Task AssertEmpty(bool deleted = false)
        {
            h.Db.ChangeTracker.Clear(); var row = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == "permission:role:72");
            Assert.Equal(IdentityEventContracts.PermissionChangedV3, row.EventType); Assert.Equal(deleted, row.IsDeleted);
            Assert.Empty(JsonSerializer.Deserialize<PermissionIdentityDataV3>(row.PayloadJson, IdentityEventContracts.Json)!.SiteFormScopes);
        }
        await AssertEmpty();
        await s.Service.ChangeAsync(72, await InquiryCommand(h, s.Service, 72, pair), h.Actor, default);
        (await h.Db.Sys_Roles.SingleAsync(x => x.RoleId == 72)).Enable = false; await h.Db.SaveChangesAsync(); await AssertEmpty();
        (await h.Db.Sys_Roles.SingleAsync(x => x.RoleId == 72)).Enable = true; await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Single((await s.Service.ReadAsync(72, h.Actor, default)).SiteForms);
        h.Db.Sys_Roles.Remove(await h.Db.Sys_Roles.SingleAsync(x => x.RoleId == 72)); await h.Db.SaveChangesAsync(); await AssertEmpty(true);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_raw_change_without_snapshot_cannot_commit_or_leave_partial_revision()
    {
        await using var h = await Seed(); var s = InquiryScopes(h); var command = await InquiryCommand(h, s.Service);
        var site = Guid.NewGuid(); var form = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO cp6_inquiry_scope_v1.role_scopes VALUES({h.Tenant},{72},{site},{form})"));
        Assert.Equal("23514", error.SqlState); Assert.Contains("SNAPSHOT_REQUIRED", error.MessageText);
        var after = await s.Service.ReadAsync(72, h.Actor, default);
        Assert.Empty(after.SiteForms); Assert.Equal(command.ExpectedVersion, after.Version); Assert.Equal(command.ExpectedReferencesVersion, after.ReferencesVersion);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_inquiry_scope_v1.role_scopes"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_invalid_pairs_CAS_ABA_and_closed_original_have_zero_effect()
    {
        await using var h = await Seed(); var s = InquiryScopes(h); var pair = new InquirySiteFormScopeV1(Guid.NewGuid(), Guid.NewGuid());
        var command = await InquiryCommand(h, s.Service, 72, pair);
        foreach (var input in new InquirySiteFormScopeV1[]?[] { null, [pair, pair], [pair with { FormId = Guid.Empty }] })
            Assert.Equal(400, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => s.Service.ChangeAsync(72, command with { SiteForms = input }, h.Actor, default))).Status);
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, true));
        Assert.Equal(412, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => s.Service.ChangeAsync(72, command, h.Actor, default))).Status);
        command = await InquiryCommand(h, s.Service, 72, pair);
        await s.Authority.Resolve(command.OperationId, IamInquiryScopeAdministrationV1.Kind, true, h.Actor, default);
        Assert.Equal("IAM_OPERATION_CLOSED", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => s.Service.ChangeAsync(72, command, h.Actor, default))).Message);
        Assert.Empty((await s.Service.ReadAsync(72, h.Actor, default)).SiteForms);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_receipt_failure_rolls_back_and_lost_commit_ack_recovers_original()
    {
        var fault = new ReceiptFault(); await using var h = await Seed(fault); var s = InquiryScopes(h);
        var pair = new InquirySiteFormScopeV1(Guid.NewGuid(), Guid.NewGuid()); var command = await InquiryCommand(h, s.Service, 72, pair);
        var before = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant && x.AggregateId == "permission:role:72");
        fault.Armed = true;
        await Assert.ThrowsAsync<IOException>(() => s.Service.ChangeAsync(72, command, h.Actor, default));
        fault.Armed = false; Assert.True(fault.Fired); Assert.Empty((await s.Service.ReadAsync(72, h.Actor, default)).SiteForms);
        Assert.Equal(before.Version, await h.Db.CrmIdentitySnapshots.Where(x => x.TenantId == h.Tenant && x.AggregateId == "permission:role:72").Select(x => x.Version).SingleAsync());
        Assert.Empty(await h.Db.Sys_SecurityLogs.ToListAsync());
        var lost = new CommitFault(); await using var next = await Seed(lost); var target = InquiryScopes(next);
        var pending = await InquiryCommand(next, target.Service, 72, pair); lost.Armed = true;
        Assert.Equal("IAM_OPERATION_UNKNOWN", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => target.Service.ChangeAsync(72, pending, next.Actor, default))).Message);
        var recovered = await target.Authority.Resolve(pending.OperationId, IamInquiryScopeAdministrationV1.Kind, false, next.Actor, default);
        Assert.Equal("COMMITTED", recovered.State); Assert.Equal(recovered, await target.Service.ChangeAsync(72, pending, next.Actor, default));
        Assert.Single((await target.Service.ReadAsync(72, next.Actor, default)).SiteForms);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_cross_tenant_role_and_pair_storage_are_independent()
    {
        await using var one = await Seed(); await using var other = await Seed();
        var a = InquiryScopes(one); var b = InquiryScopes(other); var pair = new InquirySiteFormScopeV1(Guid.NewGuid(), Guid.NewGuid());
        await b.Service.ChangeAsync(72, await InquiryCommand(other, b.Service, 72, pair), other.Actor, default);
        Assert.Empty((await a.Service.ReadAsync(72, one.Actor, default)).SiteForms);
        other.Db.Sys_Roles.Add(new() { RoleId = 1234567, RoleName = "Other tenant only" }); await other.Db.SaveChangesAsync();
        var command = await InquiryCommand(one, a.Service, 72, pair);
        Assert.Equal(404, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => a.Service.ChangeAsync(1234567, command, one.Actor, default))).Status);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => one.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO cp6_inquiry_scope_v1.role_scopes VALUES({one.Tenant},{1234567},{pair.SiteId},{pair.FormId})"));
        Assert.Equal("23503", foreign.SqlState);
        Assert.Empty((await a.Service.ReadAsync(72, one.Actor, default)).SiteForms);
        Assert.Single((await b.Service.ReadAsync(72, other.Actor, default)).SiteForms);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_scope_actual_HTTP_uses_current_permission_CSRF_tenant_and_original_receipt()
    {
        await using var h = await Seed(); await GrantPolicyHttpPermissions(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IdentityEnvironment(h.Tenant));
        var cookies = await Login(host, h);
        using var read = await host.Send(Request(HttpMethod.Get, "api/iam/inquiry-scopes/72", cookies));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var editor = (await read.Content.ReadFromJsonAsync<IamInquiryScopeEditorV1>())!;
        var command = new IamInquiryScopeCommandV1 { OperationId = Guid.NewGuid(), ExpectedVersion = editor.Version,
            ExpectedReferencesVersion = editor.ReferencesVersion, Reason = "Actual HTTP", SiteForms = [new(Guid.NewGuid(), Guid.NewGuid())] };
        using (var denied = await host.Send(Request(HttpMethod.Put, "api/iam/inquiry-scopes/72", cookies, command, false))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var saved = await host.Send(Request(HttpMethod.Put, "api/iam/inquiry-scopes/72", cookies, command))) Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        using (var result = await host.Send(Request(HttpMethod.Get, $"api/iam/inquiry-scopes/operations/{command.OperationId}", cookies)))
        { Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal("COMMITTED", (await result.Content.ReadFromJsonAsync<IamOperationV1>())!.State); }
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "pub-data-scope").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.MenuId == menu).ExecuteDeleteAsync();
        using var revoked = await host.Send(Request(HttpMethod.Get, "api/iam/inquiry-scopes/72", cookies)); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
}
