using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<int> GrantFieldAudit(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "sys-field-audit");
        if (menu == null)
        {
            menu = new() { MenuId = 990433, MenuKey = "sys-field-audit", MenuName = "Field audit" };
            h.Db.Sys_Menus.Add(menu);
        }
        if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "Query" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return menu.MenuId;
    }

    private static string AuditUrl(Guid key, string entity = "Machine", Guid? after = null, int size = 100)
        => $"api/sys/field-audit/record?entityName={entity}&entityKey={key:D}&pageSize={size}"
            + (after is null ? "" : $"&afterId={after:D}");

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_actual_HTTP_log_query_cannot_bypass_missing_business_query()
    {
        await using var h = await Seed(); await GrantFieldAudit(h); await SeedMachine(h);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Machine MC1", await response.Content.ReadAsStringAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_actual_HTTP_only_projects_current_Machine_status_and_preserves_native_history()
    {
        await using var h = await Seed(); await GrantFieldAudit(h); await GrantMachine(h); await SeedMachine(h);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        const string native = """[{"Field":"Status","Old":"0","New":"2"},{"Field":"ActiveFlg","Old":"True","New":"False"},{"Field":"Remarks","Old":"PRIVATE-OLD","New":"PRIVATE-NEW"}]""";
        var row = new Sys_FieldAuditLog { EntityName = "Machine", EntityKey = key.ToString("D"), Changes = native,
            UserName = "PRIVATE-ACTOR", UserId = h.User, Operation = 2, ChangedAt = DateTime.Now };
        h.Db.Sys_FieldAuditLogs.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE", body); Assert.DoesNotContain("Remarks", body);
        using var json = JsonDocument.Parse(body); var root = json.RootElement;
        Assert.Equal(FieldAuditReadOwnerV1.MappingVersion, root.GetProperty("projectionVersion").GetString());
        var actual = root.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == row.Id);
        Assert.Equal("RESTRICTED", actual.GetProperty("evidenceStatus").GetString());
        var changes = actual.GetProperty("changes").EnumerateArray().ToArray(); Assert.Equal(2, changes.Length);
        Assert.Equal("Status", changes[0].GetProperty("field").GetString()); Assert.Equal("2", changes[0].GetProperty("new").GetString());
        Assert.Equal("False", changes[1].GetProperty("new").GetString());
        Assert.False(actual.TryGetProperty("userName", out _)); Assert.False(actual.TryGetProperty("userId", out _));
        Assert.Equal(native, await h.Db.Sys_FieldAuditLogs.Where(a => a.Id == row.Id).Select(a => a.Changes).SingleAsync());
        using var unknown = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key, "Sys_User")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
        Assert.Contains("DETAIL_UNAVAILABLE", await unknown.Content.ReadAsStringAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_actual_HTTP_rechecks_both_current_actions_without_cached_history()
    {
        await using var h = await Seed(); var auditMenu = await GrantFieldAudit(h); await GrantMachine(h); await SeedMachine(h);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var first = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 310 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var machineDenied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key)));
        Assert.Equal(HttpStatusCode.Forbidden, machineDenied.StatusCode);
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 310, ActionCode = "query" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == auditMenu && a.ActionCode == "query").ExecuteDeleteAsync();
        using var auditDenied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, AuditUrl(key)));
        Assert.Equal(HttpStatusCode.Forbidden, auditDenied.StatusCode);
        // The service also checks the audit capability inside the Owner fence; MVC is not its only gate.
        var owner = new CP6.Core.Services.Integration.OperationsOwnerV1(h.Db, new OperationsHttp { HttpContext = h.Http },
            _ => Task.FromResult(h.User), AuditMachineCatalog().Capture);
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => new FieldAuditReadOwnerV1(h.Db, owner).ReadAsync("Machine", key.ToString("D")))).Message);
    }

    private static AuthorizationCatalogStoreV1 AuditMachineCatalog()
    {
        var catalog = new AuthorizationCatalogStoreV1(); catalog.RegisterScope("mes-machine", "Machine", [1,2,3,4,5], 1);
        catalog.RegisterFields("mes-machine", []); catalog.Seal(); return catalog;
    }

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_current_object_scope_foreign_deleted_and_missing_are_indistinguishable()
    {
        await using var h = await Seed(); await GrantFieldAudit(h); var owner = await GrantMachine(h); await SeedMachine(h);
        await using var foreign = await Seed(); await SeedMachine(foreign);
        var reader = new FieldAuditReadOwnerV1(h.Db, owner);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        var foreignKey = await foreign.Db.Machines.Select(m => m.Id).SingleAsync();
        async Task Hidden(Guid id)
        {
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync("Machine", id.ToString("D")));
            Assert.Equal(404, error.Status); Assert.Equal("OBJECT_NOT_VISIBLE", error.Message);
        }
        await Hidden(foreignKey); await Hidden(Guid.NewGuid());
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "mes-machine").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.Machines.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "another-actor")); await Hidden(key);
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "mes-machine").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.Machines.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); await Hidden(key);
        // A valid but differently bound Owner cannot read this context.
        var mismatch = new FieldAuditReadOwnerV1(foreign.Db, owner);
        Assert.Equal("DETAIL_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => mismatch.ReadAsync("Machine", foreignKey.ToString("D")))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_native_cursor_is_bounded_stable_and_never_crosses_object_or_tenant()
    {
        await using var h = await Seed(); await GrantFieldAudit(h); var owner = await GrantMachine(h); await SeedMachine(h);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        var time = new DateTime(2026, 10, 1, 12, 0, 0);
        var ids = Enumerable.Range(1, 103).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}")).ToArray();
        foreach (var id in ids) h.Db.Sys_FieldAuditLogs.Add(new() { Id = id, EntityName = "Machine", EntityKey = key.ToString("D"),
            Changes = """[{"field":"Status","old":"0","new":"1"}]""", ChangedAt = time, Operation = 2 });
        var wrong = new Sys_FieldAuditLog { EntityName = "Machine", EntityKey = Guid.NewGuid().ToString("D"), ChangedAt = time, Changes = "[]" };
        h.Db.Sys_FieldAuditLogs.Add(wrong); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var reader = new FieldAuditReadOwnerV1(h.Db, owner);
        var first = await reader.ReadAsync("Machine", key.ToString("D"), pageSize: int.MaxValue);
        Assert.Equal(100, first.Rows.Count); Assert.Equal(ids.Take(100), first.Rows.Select(x => x.Id)); Assert.Equal(ids[99], first.NextCursor);
        var second = await reader.ReadAsync("Machine", key.ToString("D"), first.NextCursor);
        Assert.Equal(ids.Skip(100), second.Rows.Take(3).Select(x => x.Id)); Assert.Null(second.NextCursor);
        Assert.DoesNotContain(second.Rows, x => first.Rows.Any(y => y.Id == x.Id));
        Assert.Equal("AUDIT_CURSOR_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => reader.ReadAsync("Machine", key.ToString("D"), wrong.Id))).Message);
        await using var foreign = await Seed(); await SeedMachine(foreign);
        var foreignCursor = await foreign.Db.Sys_FieldAuditLogs.Where(a => a.EntityName == "Machine").Select(a => a.Id).SingleAsync();
        Assert.Equal("AUDIT_CURSOR_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => reader.ReadAsync("Machine", key.ToString("D"), foreignCursor))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Field_audit_malformed_and_unmapped_history_is_explicit_and_not_a_zero_change_claim()
    {
        await using var h = await Seed(); await GrantFieldAudit(h); var owner = await GrantMachine(h); await SeedMachine(h);
        var key = await h.Db.Machines.Select(m => m.Id).SingleAsync();
        var bad = new Sys_FieldAuditLog { EntityName = "Machine", EntityKey = key.ToString("D"), Changes = "broken PRIVATE", ChangedAt = DateTime.Now };
        var restricted = new Sys_FieldAuditLog { EntityName = "Machine", EntityKey = key.ToString("D"), Changes = """[{"Field":"Remarks","Old":null,"New":"PRIVATE"}]""", ChangedAt = DateTime.Now };
        h.Db.Sys_FieldAuditLogs.AddRange(bad, restricted); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var page = await new FieldAuditReadOwnerV1(h.Db, owner).ReadAsync("Machine", key.ToString("D"));
        var unreadable = page.Rows.Single(x => x.Id == bad.Id); Assert.Empty(unreadable.Changes); Assert.Equal("UNREADABLE", unreadable.EvidenceStatus);
        var hidden = page.Rows.Single(x => x.Id == restricted.Id); Assert.Empty(hidden.Changes); Assert.Equal("RESTRICTED", hidden.EvidenceStatus);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/sys/field-audit?entityName=Machine&pageSize=100"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("PRIVATE", body);
        using var json = JsonDocument.Parse(body);
        var row = json.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == bad.Id);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("changeCount").ValueKind); Assert.Equal("UNREADABLE", row.GetProperty("evidenceStatus").GetString());
    }
}
