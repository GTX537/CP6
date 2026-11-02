using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private Task<MenuTreeReceiptV1> DisplayWrite(Harness h, MenuDisplayCommandV1 command, params IInterceptor[] interceptors)
        => TreeCall(h, (s, a) => s.ExecuteDisplayAsync(command, a), null, interceptors);
    private static MenuDisplayCommandV1 DisplayCommand(MenuTreeSnapshotV1 tree, int id)
        => new(Guid.NewGuid().ToString("N"), tree.Revision, id, "名称 保留尾空格 ", "Folder ", false, "展示配置验收");

    [CurrentPermissionPostgresFact]
    public async Task Menu_display_v1_exact_fields_partial_preservation_and_original_cross_kind_conflict()
    {
        await using var h = await TreeSeed(); await using var other = await TreeSeed(); var ids = await TreeNodes(h); var before = await TreeRead(h);
        var command = DisplayCommand(before, ids[1]) with { OperationRef = "display\0" + Guid.NewGuid() };
        var impact = await TreeCall(h, (s, a) => s.PreviewDisplayAsync(command, a)); Assert.True(impact.AffectedTenantsCount >= 2);
        var first = await DisplayWrite(h, command); var current = await TreeRead(h); var node = current.Menus.Single(x => x.MenuId == ids[1]);
        Assert.Equal(command.MenuName, node.MenuName); Assert.Equal(command.Icon, node.Icon); Assert.False(node.Enable);
        Assert.Equal(before.Menus.Single(x => x.MenuId == ids[1]), node with { MenuName = "Child", Icon = null, Enable = true });
        Assert.Equal(before.Menus.Single(x => x.MenuId == ids[2]), current.Menus.Single(x => x.MenuId == ids[2]));
        SameTreeReceipt(first, await DisplayWrite(h, command)); SameTreeReceipt(first, await TreeOriginal(h, command.OperationRef));
        Assert.Equal(409, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, TreeMove(current, ids[1], null) with { OperationRef = command.OperationRef }))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeOriginal(other, command.OperationRef))).Status);
        await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteDeleteAsync(); SameTreeReceipt(first, await DisplayWrite(h, command));
        Assert.False(await h.Db.Sys_Menus.AnyAsync(x => x.MenuId == ids[1]));
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_display_v1_tree_and_native_writes_share_CAS_and_noop_keeps_revision()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var initial = await TreeRead(h); var command = DisplayCommand(initial, ids[1]);
        var results = await Task.WhenAll(new Func<Task>[] { async () => { await TreeWrite(h, TreeMove(initial, ids[1], null)); }, async () => { await DisplayWrite(h, command); } }
            .Select(async action => { try { await action(); return 200; } catch (MenuTreeExceptionV1 e) { return e.Status; } }));
        Assert.Equal(new[] { 200, 412 }, results.Order());
        var current = await TreeRead(h); var node = current.Menus.Single(x => x.MenuId == ids[1]);
        var noop = DisplayCommand(current, ids[1]) with { MenuName = node.MenuName, Icon = node.Icon, Enable = node.Enable };
        var receipt = await DisplayWrite(h, noop); Assert.Equal(current.Revision, receipt.AppliedRevision); Assert.Empty(receipt.Impact.ChangedMenuIds);
        await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteUpdateAsync(x => x.SetProperty(n => n.MenuName, "ABA"));
        await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteUpdateAsync(x => x.SetProperty(n => n.MenuName, node.MenuName));
        Assert.Equal(412, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => DisplayWrite(h, noop with { OperationRef = Guid.NewGuid().ToString() }))).Status);
        SameTreeReceipt(receipt, await DisplayWrite(h, noop));
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_display_v1_invalid_inputs_do_not_write_and_maximum_text_is_lossless()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var before = await TreeRead(h); var command = DisplayCommand(before, ids[1]);
        foreach (var invalid in new[] { command with { MenuName = " " }, command with { MenuName = new string('界', 101) }, command with { Icon = new string('I', 101) },
            command with { Icon = "bad\0icon" }, command with { MenuName = "\ud800" }, command with { Reason = "" }, command with { MenuId = 0 } })
            Assert.Equal(400, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => DisplayWrite(h, invalid))).Status);
        Assert.Equal(before.Revision, (await TreeRead(h)).Revision); Assert.Null(await TreeOriginal(h, command.OperationRef));
        var valid = command with { MenuName = new string('界', 99) + " ", Icon = new string('I', 100), Reason = new string('R', 256) };
        await DisplayWrite(h, valid); var current = (await TreeRead(h)).Menus.Single(x => x.MenuId == ids[1]);
        Assert.Equal(valid.MenuName, current.MenuName); Assert.Equal(valid.Icon, current.Icon);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_display_v1_audit_session_rollback_unknown_recovery_and_current_revocation()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var before = await TreeRead(h); var command = DisplayCommand(before, ids[1]);
        var constraint = "display_" + Guid.NewGuid().ToString("N");
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT {constraint} CHECK (\"UserId\" <> '{h.User}' OR \"Reason\" <> '展示配置验收')");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => DisplayWrite(h, command)); Assert.Equal(before.Revision, (await TreeRead(h)).Revision); }
        finally { await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT {constraint}"); }
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => TreeCall(h, (s, a) => s.ExecuteDisplayAsync(command, a), _ => ++calls == 2 ? throw new IOException("final session") : Task.FromResult(h.User)));
        Assert.Equal(before.Revision, (await TreeRead(h)).Revision);
        Assert.Equal("MENU_TREE_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => DisplayWrite(h, command, new NumberLostCommit { Armed = true }))).Message);
        var original = await TreeOriginal(h, command.OperationRef); Assert.NotNull(original); SameTreeReceipt(original, await DisplayWrite(h, command));
        Assert.Single(await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.UserId == h.User && x.Reason == command.Reason).ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => DisplayWrite(h, command))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_display_v1_actual_http_visibility_changes_preserve_role_grants_and_recover_original()
    {
        await using var h = await TreeSeed(); await using var ordinary = await Seed(); var ids = await TreeNodes(h);
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = ids[1] }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var command = DisplayCommand(await TreeRead(h), ids[1]);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        const string path = "api/menu/tree-v1/display";
        Task<HttpResponseMessage> Send(string suffix, object body, bool csrf = true) => host.Send(OperationsRequest(cookies, HttpMethod.Post, path + suffix, body, csrf));
        using (var r = await Send("/preview", command)) Assert.Contains(ids[1], (await NumberHttpData<MenuTreeImpactV1>(r)).ChangedMenuIds);
        using (var r = await Send("/commands", command, false)) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var r = await Send("/commands", new { command.OperationRef, command.ExpectedRevision, command.MenuId, command.MenuName, command.Icon, command.Enable, command.Reason, MenuKey = "forged" })) Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var otherCookies = await Login(host, ordinary);
        using (var r = await host.Send(OperationsRequest(otherCookies, HttpMethod.Post, path + "/commands", command))) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        MenuTreeReceiptV1 first;
        using (var r = await Send("/commands", command)) first = await NumberHttpData<MenuTreeReceiptV1>(r);
        using (var r = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/auth/profile")))
        { Assert.Equal(HttpStatusCode.OK, r.StatusCode); var profile = await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.DoesNotContain(profile.GetProperty("menus").EnumerateArray(), x => x.GetProperty("id").GetInt32() == ids[1]); }
        Assert.True(await h.Db.Sys_RoleMenus.AnyAsync(x => x.MenuId == ids[1]));
        var enable = command with { OperationRef = Guid.NewGuid().ToString(), ExpectedRevision = first.AppliedRevision, Enable = true };
        using (var r = await Send("/commands", enable)) await NumberHttpData<MenuTreeReceiptV1>(r);
        using (var r = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/auth/profile")))
        { var profile = await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.Contains(profile.GetProperty("menus").EnumerateArray(), x => x.GetProperty("id").GetInt32() == ids[1] && x.GetProperty("menuName").GetString() == command.MenuName); }
        using (var r = await Send("/commands", command)) SameTreeReceipt(first, await NumberHttpData<MenuTreeReceiptV1>(r));
    }
}
