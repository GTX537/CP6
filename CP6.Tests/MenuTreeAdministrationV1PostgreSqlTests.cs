using System.Net;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> TreeSeed()
    {
        var h = await TenantManagementSeed();
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "menu");
        if (menu == null) { menu = new() { MenuId = 92000, MenuKey = "menu", MenuName = "Menus", Enable = true }; h.Db.Sys_Menus.Add(menu); }
        h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "edit", ActionName = "Edit" });
        h.Db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = 72, ActionCode = "edit" });
        h.Db.Sys_RoleMenus.Add(new() { MenuId = menu.MenuId, RoleId = 72 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return h;
    }
    private async Task<int[]> TreeNodes(Harness h)
    {
        var id = Random.Shared.Next(300000000, 500000000);
        h.Db.Sys_Menus.AddRange(new Sys_Menu { MenuId = id, MenuName = "Root", Enable = true },
            new Sys_Menu { MenuId = id + 1, MenuName = "Child", ParentId = id, OrderNo = 1, Enable = true },
            new Sys_Menu { MenuId = id + 2, MenuName = "Sibling", ParentId = id, OrderNo = 2, Enable = true });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return [id, id + 1, id + 2];
    }
    private async Task<T> TreeCall<T>(Harness h, Func<MenuTreeAdministrationV1, TenantManagementSessionV1, Task<T>> action,
        Func<CancellationToken, Task<Guid>>? session = null, params IInterceptor[] interceptors)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, interceptors);
        var http = new NumberHttp(h.Http); var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        session ??= _ => Task.FromResult(h.User);
        return await action(new(db, new CurrentPermissionContext(http, cache, db, new PermissionAggregator(db)), http), new(session, async ct => { _ = await session(ct); }));
    }
    private Task<MenuTreeSnapshotV1> TreeRead(Harness h) => TreeCall(h, (s, a) => s.ReadAsync(a));
    private Task<MenuTreeReceiptV1> TreeWrite(Harness h, MenuTreeCommandV1 command, params IInterceptor[] interceptors)
        => TreeCall(h, (s, a) => s.ExecuteAsync(command, a), null, interceptors);
    private Task<MenuTreeReceiptV1?> TreeOriginal(Harness h, string operation) => TreeCall(h, (s, a) => s.ReadOriginalAsync(operation, a));
    private static MenuTreeCommandV1 TreeMove(MenuTreeSnapshotV1 tree, int id, int? parent, int order = 3)
        => new(Guid.NewGuid().ToString("N"), tree.Revision, [new(id, parent, order)], "调整导航层级");
    private static void SameTreeReceipt(MenuTreeReceiptV1 expected, MenuTreeReceiptV1? actual)
        => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_partial_move_global_impact_original_and_concurrent_CAS()
    {
        await using var h = await TreeSeed(); await using var other = await TreeSeed(); var ids = await TreeNodes(h);
        other.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = ids[1] }); await other.Db.SaveChangesAsync(); other.Db.ChangeTracker.Clear();
        var before = await TreeRead(h); var command = TreeMove(before, ids[1], null) with { OperationRef = new string('界', 3000) + "\0/original" };
        var impact = await TreeCall(h, (s, a) => s.PreviewAsync(command, a));
        Assert.Equal(new[] { ids[1] }, impact.ChangedMenuIds); Assert.True(impact.AffectedTenantsCount >= 2); Assert.True(impact.AffectedRolesCount >= 2);
        var first = await TreeWrite(h, command); Assert.True(first.AppliedRevision > before.Revision);
        var current = await TreeRead(h); Assert.Null(current.Menus.Single(x => x.MenuId == ids[1]).ParentId);
        Assert.Equal(before.Menus.Single(x => x.MenuId == ids[2]), current.Menus.Single(x => x.MenuId == ids[2]));
        var results = await Task.WhenAll(new[] { 4, 5 }.Select(async order => { try { await TreeWrite(h, TreeMove(current, ids[1], null, order)); return 200; } catch (MenuTreeExceptionV1 e) { return e.Status; } }));
        Assert.Equal(new[] { 200, 412 }, results.Order());
        SameTreeReceipt(first, await TreeWrite(h, command)); SameTreeReceipt(first, await TreeOriginal(h, command.OperationRef));
        Assert.Equal(409, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, command with { Reason = "异义" }))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeOriginal(other, command.OperationRef))).Status);
        Assert.True(await other.Db.Sys_RoleMenus.AnyAsync(x => x.MenuId == ids[1]));
        await h.Db.Sys_Menus.Where(x => ids.Contains(x.MenuId)).ExecuteDeleteAsync();
        SameTreeReceipt(first, await TreeWrite(h, command)); Assert.False(await h.Db.Sys_Menus.AnyAsync(x => ids.Contains(x.MenuId)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_rejects_invalid_structure_and_native_ABA_changes_revision()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var before = await TreeRead(h); var valid = TreeMove(before, ids[1], null);
        foreach (var positions in new MenuTreePositionV1[][] { [], [new(ids[1], null, -1)], [new(ids[1], null, 1), new(ids[1], null, 2)],
            [new(ids[1], ids[1], 0)], [new(ids[0], ids[1], 0)], [new(ids[1], int.MaxValue, 0)], [null!] })
            Assert.Equal(400, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, valid with { Positions = positions }))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, valid with { Positions = [new(int.MaxValue, null, 0)] }))).Status);
        Assert.Equal(before.Revision, (await TreeRead(h)).Revision); Assert.Null(await TreeOriginal(h, valid.OperationRef));
        await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteUpdateAsync(x => x.SetProperty(n => n.OrderNo, 8));
        await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).ExecuteUpdateAsync(x => x.SetProperty(n => n.OrderNo, 1));
        Assert.Equal(before.Revision + 2, (await TreeRead(h)).Revision);
        Assert.Equal(412, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, valid))).Status);
        foreach (var sql in new[] { "UPDATE cp6_menu_tree_v1.root SET revision=1", "DELETE FROM cp6_menu_tree_v1.root", "TRUNCATE cp6_menu_tree_v1.commands" })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_audit_final_session_rollback_and_lost_commit_original()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var before = await TreeRead(h); var command = TreeMove(before, ids[1], null);
        var name = "tree_" + Guid.NewGuid().ToString("N");
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT {name} CHECK (\"UserId\" <> '{h.User}' OR \"Reason\" <> '调整导航层级')");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => TreeWrite(h, command)); Assert.Equal(before.Revision, (await TreeRead(h)).Revision); }
        finally { await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT {name}"); }
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => TreeCall(h, (s, a) => s.ExecuteAsync(command, a), _ => ++calls == 2 ? throw new IOException("session lost") : Task.FromResult(h.User)));
        Assert.Equal(before.Revision, (await TreeRead(h)).Revision); Assert.Null(await TreeOriginal(h, command.OperationRef));
        Assert.Equal("MENU_TREE_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeWrite(h, command, new NumberLostCommit { Armed = true }))).Message);
        var receipt = await TreeOriginal(h, command.OperationRef); Assert.NotNull(receipt); SameTreeReceipt(receipt, await TreeWrite(h, command));
        Assert.Single(await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.UserId == h.User && x.Reason == command.Reason).ToArrayAsync());
        Assert.Equal(receipt.AppliedRevision, (await TreeRead(h)).Revision);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_waiting_command_rechecks_current_action_permission()
    {
        await using var h = await TreeSeed(); var ids = await TreeNodes(h); var command = TreeMove(await TreeRead(h), ids[1], null);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = TreeWrite(h, command);
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(timeout.Token))
            { Assert.False(attempt.IsCompleted); await Task.Delay(20, timeout.Token); }
            await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal(403, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => attempt)).Status);
        Assert.Equal(ids[0], await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).Select(x => x.ParentId).SingleAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<MenuTreeExceptionV1>(() => TreeOriginal(h, command.OperationRef))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_http_current_platform_action_CSRF_strict_input_and_original()
    {
        await using var h = await TreeSeed(); await using var ordinary = await Seed(); await using var noAction = await TenantManagementSeed();
        var ids = await TreeNodes(h); var command = TreeMove(await TreeRead(h), ids[1], null);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        const string path = "api/menu/tree-v1";
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix, object? body = null, bool csrf = true) => host.Send(OperationsRequest(cookies, method, path + suffix, body, csrf));
        using (var response = await Send(HttpMethod.Get, "")) Assert.Equal(command.ExpectedRevision, (await NumberHttpData<MenuTreeSnapshotV1>(response)).Revision);
        using (var response = await Send(HttpMethod.Post, "/preview", command)) Assert.Contains(ids[1], (await NumberHttpData<MenuTreeImpactV1>(response)).ChangedMenuIds);
        using (var response = await Send(HttpMethod.Post, "/commands", command, false)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/commands", new { command.OperationRef, command.ExpectedRevision, command.Positions, command.Reason, ActorId = h.User })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        MenuTreeReceiptV1 first;
        using (var response = await Send(HttpMethod.Post, "/commands", command)) first = await NumberHttpData<MenuTreeReceiptV1>(response);
        using (var response = await Send(HttpMethod.Post, "/commands/read", new { command.OperationRef })) SameTreeReceipt(first, await NumberHttpData<MenuTreeReceiptV1>(response));
        using (var response = await Send(HttpMethod.Post, "/commands", command)) SameTreeReceipt(first, await NumberHttpData<MenuTreeReceiptV1>(response));
        foreach (var deniedActor in new[] { ordinary, noAction })
        {
            var deniedCookies = await Login(host, deniedActor);
            using var denied = await host.Send(OperationsRequest(deniedCookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var response = await Send(HttpMethod.Post, "/commands", command with { OperationRef = Guid.NewGuid().ToString() })) Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        using (var logout = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/auth/logout", new { }))) Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using (var response = await Send(HttpMethod.Post, "/commands/read", new { command.OperationRef })) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_upgrade_waits_for_native_writer_and_preserves_shared_tree()
    {
        await using var h = await TreeSeed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" }; var name = "tree_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        { await root.OpenAsync(); await using var create = new NpgsqlCommand("CREATE DATABASE " + name, root); await create.ExecuteNonQueryAsync(); }
        settings.Database = name; var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var policy = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString, policy.MigrationsAssembly, policy.HistoryTable, policy.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext()); await db.GetService<IMigrator>().MigrateAsync("20261102110000_TenantCreationV1");
        db.Sys_Menus.Add(new() { MenuId = 101, MenuName = "Existing ", MenuKey = "existing", OrderNo = 9 }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Task<string[]> Rows() => db.Database.SqlQueryRaw<string>("SELECT row_to_json(t)::text AS \"Value\" FROM public.\"Sys_Menus\" t ORDER BY \"MenuId\"").ToArrayAsync();
        var before = await Rows();
        await using (var writer = new CP6Context(options.Options, new TenantContext()))
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            writer.Sys_Menus.Add(new() { MenuId = 102, MenuName = "Concurrent", ParentId = 101 }); await writer.SaveChangesAsync();
            var migration = db.Database.MigrateAsync();
            try
            {
                await using var observer = new NpgsqlConnection(settings.ConnectionString); await observer.OpenAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var waiting = new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%LOCK TABLE public.%Sys_Menus%')", observer);
                while (!(bool)(await waiting.ExecuteScalarAsync(timeout.Token))!) { Assert.False(migration.IsCompleted); await Task.Delay(20, timeout.Token); }
            }
            finally { await transaction.CommitAsync(); }
            await migration;
        }
        var after = await Rows(); Assert.Equal(before.Length + 1, after.Length); Assert.All(before, row => Assert.Contains(row, after));
        Assert.Equal(1, await db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_menu_tree_v1.root").SingleAsync());
        await db.Database.MigrateAsync(); Assert.Equal(after, await Rows());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"Sys_Menus\" CASCADE");
        Assert.Equal(2, await db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_menu_tree_v1.root").SingleAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Menu_tree_v1_http_rechecks_platform_identity_after_native_revocation_wait()
    {
        await using var h = await TreeSeed(); await using var spare = await TreeSeed(); var ids = await TreeNodes(h); var command = TreeMove(await TreeRead(h), ids[1], null);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/menu/tree-v1/commands", command));
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(timeout.Token))
            { if (attempt.IsCompleted) Assert.Fail(await OperationsFailure(await attempt)); await Task.Delay(20, timeout.Token); }
            await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        }
        finally { await tx.CommitAsync(); }
        using var response = await attempt; Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.Null(await TreeOriginal(spare, command.OperationRef));
        Assert.Equal(ids[0], await h.Db.Sys_Menus.Where(x => x.MenuId == ids[1]).Select(x => x.ParentId).SingleAsync());
    }
}
