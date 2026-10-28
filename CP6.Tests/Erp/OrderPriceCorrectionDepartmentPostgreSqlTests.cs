using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantQuery(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "erp-order-price-correction");
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "Query prices" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Hidden_prices_never_escape_native_price_correction_list_or_derived_amount()
    {
        await using var h = await Seed(); await GrantQuery(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var (rows, total) = await h.Service.SearchPriceCorrectionsAsync(new OrderPriceCorrectionQueryDto { BaseCd = "BASE" });
        Assert.Equal(2, total); Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Null(row.IndividualUnitPriceBefore); Assert.Null(row.IndividualUnitPriceAfter);
            Assert.Null(row.SetUnitPriceBefore); Assert.Null(row.SetUnitPriceAfter);
            Assert.Null(row.Amount);
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Hidden_amount_cannot_be_inferred_from_price_filter_or_calculation_endpoint()
    {
        await using var h = await Seed(); await GrantQuery(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "Amount", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var filter = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.SearchPriceCorrectionsAsync(
            new OrderPriceCorrectionQueryDto { BaseCd = "BASE", AmountFrom = 6m }));
        Assert.Equal("FIELD_READ_FORBIDDEN", filter.Message);
        var calc = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CalcAmountAsync(h.Number, 1, 3m, null));
        Assert.Equal("FIELD_READ_FORBIDDEN", calc.Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Readonly_price_also_protects_its_change_reason_with_no_price_difference()
    {
        await using var h = await Seed();
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(h.Request(3m), null));
        Assert.Equal("FIELD_WRITE_FORBIDDEN", denied.Message);
        h.Db.ChangeTracker.Clear();
        Assert.All(await h.Db.OrderDetails.ToArrayAsync(), row => Assert.Null(row.PriceChangeReason));
        Assert.Equal(0, await Intents(h));
    }

    private sealed record MappingHarness(IamOrderDepartmentMappingV1 Service, IamAdministrationV1 Authority,
        Guid Root, Guid Child, Guid Other);
    private sealed class MappingHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
    private static async Task<MappingHarness> MappingAdmin(Harness h)
    {
        var root = Guid.NewGuid(); var child = Guid.NewGuid(); var other = Guid.NewGuid();
        h.Db.Sys_Depts.AddRange(
            new Sys_Dept { Id = root, DeptCode = "IAM-root", DeptName = "Root", Path = $"/{root}/" },
            new Sys_Dept { Id = child, DeptCode = "NOT-SALES", DeptName = "Child", ParentId = root, Path = $"/{root}/{child}/" },
            new Sys_Dept { Id = other, DeptCode = "Other", DeptName = "Other", Path = $"/{other}/" });
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "pub-dept");
        if (menu == null)
        {
            // Native Owner migrations can legitimately occupy a former fixture-only id.
            // Do not collide with an actual menu or replace its existing action bindings.
            var used = (await h.Db.Sys_Menus.Select(x => x.MenuId).ToArrayAsync()).ToHashSet();
            var id = used.Contains(32721) ? Enumerable.Range(1, int.MaxValue).First(x => !used.Contains(x)) : 32721;
            menu = new() { MenuId = id, MenuKey = "pub-dept", MenuName = "Departments" }; h.Db.Sys_Menus.Add(menu);
        }
        foreach (var action in new[] { "query", "edit" })
        {
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
            if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var permissions = new CurrentPermissionContext(new MappingHttpContext { HttpContext = h.Http },
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), h.Db, new PermissionAggregator(h.Db));
        var authority = new IamAdministrationV1(h.Db, permissions, new EphemeralDataProtectionProvider());
        return new(new(h.Db, authority), authority, root, child, other);
    }

    private static async Task<IamOrderDepartmentCommandV1> MappingCommand(Harness h, MappingHarness admin,
        string code = "SALES", Guid? id = null)
    {
        var editor = await admin.Service.ReadAsync(_ => Task.FromResult(h.User), default);
        var row = editor.Mappings.SingleOrDefault(x => x.Id == id);
        return new() { Id = id, Code = code, DepartmentId = admin.Child, Enabled = true,
            OperationId = Guid.NewGuid(), ExpectedVersion = row?.Version ?? "absent",
            ExpectedReferencesVersion = editor.ReferencesVersion, Reason = "Explicit owned fixture mapping" };
    }
    private static Task<IamOperationV1> SaveMapping(Harness h, MappingHarness admin, IamOrderDepartmentCommandV1 command)
        => admin.Service.ChangeAsync(command, _ => Task.FromResult(h.User), default);

    [CurrentPermissionPostgresFact]
    public async Task Explicit_mapping_preserves_native_codes_and_current_department_subtree_custom_scopes()
    {
        await using var h = await Seed(); await GrantQuery(h); var admin = await MappingAdmin(h);
        Assert.Empty((await admin.Service.ReadAsync(_ => Task.FromResult(h.User), default)).Mappings);
        await SaveMapping(h, admin, await MappingCommand(h, admin));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "sales "));
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Root));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 3));
        var (rows, total) = await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" });
        Assert.Equal(2, total); Assert.All(rows, r => Assert.True(r.PriceWritable));
        Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(h.Request(), "forged")).UpdatedCount);
        h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 2));
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(5m), null))).Message);
        Assert.Equal(0, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Child));
        Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(h.Request(5m), null)).UpdatedCount);
        h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, (Guid?)null));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 4).SetProperty(x => x.CustomDeptIds, admin.Child.ToString()));
        Assert.Equal(6m, await h.Service.CalcAmountAsync(h.Number.ToLowerInvariant()+" ", 1, 3m, null));
        await h.Db.Sys_Depts.Where(d => d.Id == admin.Root).ExecuteUpdateAsync(s => s.SetProperty(d => d.Enable, false));
        Assert.Equal("OWNER_SCOPE_MAPPING_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(6m), null))).Message);
        Assert.Equal(0, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mapping_administration_is_explicit_tenant_bound_cas_audited_and_recoverable()
    {
        await using var h = await Seed(); var admin = await MappingAdmin(h);
        var command = await MappingCommand(h, admin);
        var saved = await SaveMapping(h, admin, command);
        Assert.Equal(saved, await SaveMapping(h, admin, command)); // Exact original receipt, no new write.
        Assert.Equal(saved, await admin.Authority.Resolve(command.OperationId, "dept-order-mapping", false,
            _ => Task.FromResult(h.User), default));
        var editor = await admin.Service.ReadAsync(_ => Task.FromResult(h.User), default);
        Assert.Single(editor.Mappings); Assert.Equal(admin.Child, editor.Mappings[0].DepartmentId);
        Assert.Single((await h.Db.Sys_SecurityLogs.AsNoTracking().Select(x => x.ContextJson).ToArrayAsync())
            .Where(json => json?.Contains(command.OperationId.ToString(), StringComparison.Ordinal) == true));
        var duplicate = await MappingCommand(h, admin, "sales ");
        Assert.Equal("ORDER_DEPARTMENT_CODE_CONFLICT", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(
            () => SaveMapping(h, admin, duplicate))).Message);
        Assert.Equal("ORDER_DEPARTMENT_MAPPING_INPUT_INVALID", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(
            () => SaveMapping(h, admin, command with { Code = "12345678901", OperationId = Guid.NewGuid() }))).Message);
        var old = await MappingCommand(h, admin, id: saved.EntityId);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_order_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_order_owner_v1.department_mappings SET enabled=true WHERE id={saved.EntityId}");
        Assert.Equal(412, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => SaveMapping(h, admin, old))).Status);
        var current = await MappingCommand(h, admin, id: saved.EntityId);
        await SaveMapping(h, admin, current with { Enabled = false });
        Assert.False((await admin.Service.ReadAsync(_ => Task.FromResult(h.User), default)).Mappings.Single().Available);
        await using var foreign = await Seed();
        var foreignId = Guid.NewGuid();
        foreign.Db.Sys_Depts.Add(new() { Id = foreignId, DeptCode = "foreign", DeptName = "Foreign", Path = $"/{foreignId}/" });
        await foreign.Db.SaveChangesAsync(); foreign.Db.ChangeTracker.Clear();
        Assert.Equal("IAM_DEPARTMENT_UNAVAILABLE", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(
            async () => await SaveMapping(h, admin, (await MappingCommand(h, admin, id: saved.EntityId)) with { DepartmentId = foreignId }))).Message);
        var raw = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_order_owner_v1.department_mappings(id,tenant_id,code,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},CAST('BAD' AS bpchar),{foreignId},true)
            """));
        Assert.Equal("23514", raw.SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mapping_revoke_waits_for_business_commit_and_next_read_write_deny()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantQuery(h);
        var admin = await MappingAdmin(h); await SaveMapping(h, admin, await MappingCommand(h, admin));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "SALES"));
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Child));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 2));
        Task? revoker = null; var waited = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            hook.After = null;
            revoker = Task.Run(async () =>
            {
                await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                await writer.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE cp6_order_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant}");
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE cp6_order_owner_v1.department_mappings%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            waited = true; Assert.False(revoker.IsCompleted);
        };
        try { Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(h.Request(), null)).UpdatedCount); }
        finally { if (revoker != null) await revoker; }
        Assert.True(waited); h.Db.ChangeTracker.Clear(); Assert.Equal(1, await Intents(h));
        Assert.Equal(0, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
        Assert.Equal("OWNER_SCOPE_MAPPING_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(8m), null))).Message);
        Assert.Equal(1, await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Department_move_rebind_and_empty_custom_scope_never_keep_an_old_allow()
    {
        await using var h = await Seed(); await GrantQuery(h); var admin = await MappingAdmin(h);
        var binding = await SaveMapping(h, admin, await MappingCommand(h, admin));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "SALES"));
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Root));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 3));
        Assert.Equal(2, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
        await h.Db.Sys_Depts.Where(d => d.Id == admin.Child).ExecuteUpdateAsync(s =>
            s.SetProperty(d => d.ParentId, admin.Other).SetProperty(d => d.Path, $"/{admin.Other}/{admin.Child}/"));
        Assert.Equal(0, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        await SaveMapping(h, admin, (await MappingCommand(h, admin, id: binding.EntityId)) with { DepartmentId = admin.Root });
        Assert.Equal(2, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" })).Item2);
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 4).SetProperty(x => x.CustomDeptIds, ""));
        Assert.Equal(0, (await h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE", MaxRows = 1 })).Item2);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            h.Service.CalcAmountAsync(h.Number, 1, 3m, null))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Read_owner_refuses_old_transactions_missing_adapters_and_final_source_failure()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantQuery(h);
        await using (var old = await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
        {
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" }))).Message);
            Assert.Equal("IAM_AUTHORITY_UNAVAILABLE", (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() =>
                new IamAdministrationV1(h.Db, null!, new EphemeralDataProtectionProvider())
                    .ExecuteAsync("pub-dept:query", _ => Task.FromResult(h.User), (_, _) => Task.FromResult(1), default))).Message);
            await old.RollbackAsync();
        }
        var unbound = new OrderService(h.Db, h.Workflow, new CP6.Core.Services.Integration.NoOpWmsBridgeHook());
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            unbound.CalcAmountAsync(h.Number, 1, 3m, null))).Message);
        var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") && ++reads == 2)
                throw new IOException("owned read final source failure");
            return Task.CompletedTask;
        };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            h.Service.SearchPriceCorrectionsAsync(new() { BaseCd = "BASE" }))).Message);
        Assert.Equal(2, reads); Assert.Null(h.Db.Database.CurrentTransaction);
    }
}
