using System.Text;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantOrderReads(Harness h)
    {
        foreach (var (id, key, actions) in new[] {
            (32621, "order-list", new[] { "query", "export", "download" }),
            (32622, "erp-order", new[] { "query" }) })
        {
            if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == id))
                h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            foreach (var action in actions)
            {
                h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { MenuId = id, RoleId = 72, ActionCode = action });
            }
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_hides_prices_and_derived_amount_before_list_or_export()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var (rows, total) = await h.Service.SearchOrdersAsync(new());
        Assert.Equal(2, total);
        Assert.All(rows, r => { Assert.Null(r.IndividualUnitPrice); Assert.Null(r.SetUnitPrice); Assert.Null(r.Amount); });
        var csv = Encoding.UTF8.GetString(await h.Service.ExportListCsvAsync(new()));
        Assert.DoesNotContain(",3,", csv); Assert.DoesNotContain(",6,", csv);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_rejects_direct_detail_report_and_hidden_sort_bypass()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "Amount", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.GetByWebOrderNoAsync(h.Number));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportOrderReportPdfAsync([h.Number + "-1"]));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.SearchOrdersAsync(new() { SortField = "amount" }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_uses_trusted_department_binding_before_count_page_and_download()
    {
        await using var h = await Seed(); await GrantOrderReads(h); var admin = await MappingAdmin(h);
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Child));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 2));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "SALES"));
        Assert.Equal(0, (await h.Service.SearchOrdersAsync(new())).Item2);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.GetByWebOrderNoAsync(h.Number));
        var saved = await SaveMapping(h, admin, await MappingCommand(h, admin, "sales "));
        var (rows, total) = await h.Service.SearchOrdersAsync(new() { PageSize = 1 });
        Assert.Single(rows); Assert.Equal(2, total);
        Assert.Equal(2, (await h.Service.GetByWebOrderNoAsync(h.Number.ToLowerInvariant() + " "))!.Details.Count);
        Assert.Contains("3", Encoding.UTF8.GetString(await h.Service.ExportOrderReportPdfAsync([h.Number.ToLowerInvariant() + " -1"])));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_order_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        Assert.Equal(0, (await h.Service.SearchOrdersAsync(new())).Item2);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportOrderReportPdfAsync([h.Number + "-1"]));
        Assert.Single(Encoding.UTF8.GetString(await h.Service.ExportListCsvAsync(new())).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_self_scope_never_returns_partial_edit_graph_or_foreign_report_rows()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(d => d.Creator, "another"));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).ExecuteUpdateAsync(s => s.SetProperty(d => d.HaibaiNo1, "OWN"));
        var (rows, total) = await h.Service.SearchOrdersAsync(new()); Assert.Single(rows); Assert.Equal(1, total);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.GetByWebOrderNoAsync(h.Number));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.LookupByHaibaiNoAsync("own ", null, null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportOrderReportPdfAsync([h.Number + "-1", h.Number + "-2"]));
        Assert.NotEmpty(await h.Service.ExportOrderReportPdfAsync([h.Number + "-1"]));
        await using var other = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.GetByWebOrderNoAsync(other.Number));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportOrderReportPdfAsync([other.Number + "-1"]));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_export_download_require_separate_real_actions_and_no_missing_owner_fallback()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 32621 && a.ActionCode != "query").ExecuteDeleteAsync();
        Assert.Equal(2, (await h.Service.SearchOrdersAsync(new())).Item2);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportListCsvAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportOrderReportPdfAsync([h.Number + "-1"]));
        await h.Db.Sys_MenuActions.Where(a => a.MenuId == 32621 && a.ActionCode == "query").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.SearchOrdersAsync(new()));
        var missing = new OrderService(h.Db, h.Workflow, new NoOpWmsBridgeHook());
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => missing.SearchOrdersAsync(new()))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_refuses_old_transactions_and_final_source_failure()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantOrderReads(h);
        await using (var tx = await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
            Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.SearchOrdersAsync(new()))).Status);
        var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("owned final read failure");
            return Task.CompletedTask;
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.ExportListCsvAsync(new()))).Status);
        Assert.Equal(2, reads);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_read_keeps_raw_revocation_waiting_until_return_and_denies_next_read()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantOrderReads(h);
        Task? revoker = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            hook.After = null;
            revoker = Task.Run(async () =>
            {
                await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(revoker.IsCompleted);
        };
        try { Assert.NotEmpty(await h.Service.ExportListCsvAsync(new())); }
        finally { if (revoker != null) await revoker; }
        Assert.True(blocked);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.SearchOrdersAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_actual_program_enforces_owner_on_reads_files_csrf_and_revoked_identity()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using (var list = await host.Send(BoundRequest(cookies, "api/orders/list")))
        { Assert.True(list.IsSuccessStatusCode, await list.Content.ReadAsStringAsync()); Assert.True(list.Headers.CacheControl?.NoStore); }
        using (var report = await host.Send(BoundRequest(cookies, "api/orders/report", new { keys = new[] { h.Number + "-1" } }, false)))
            Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var list = await host.Send(BoundRequest(cookies, "api/orders/list")))
        {
            Assert.True(list.IsSuccessStatusCode, await list.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var row = json.RootElement.GetProperty("data").GetProperty("rows")[0];
            Assert.Equal(JsonValueKind.Null, row.GetProperty("individualUnitPrice").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("amount").ValueKind);
        }
        foreach (var path in new[] { "api/orders/" + h.Number, "api/orders/list?SortField=amount" })
        { using var response = await host.Send(BoundRequest(cookies, path)); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        using (var report = await host.Send(BoundRequest(cookies, "api/orders/report", new { keys = new[] { h.Number + "-1" } })))
            Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        using (var csv = await host.Send(BoundRequest(cookies, "api/orders/list/export.csv")))
        { Assert.True(csv.IsSuccessStatusCode); Assert.True(csv.Headers.CacheControl?.NoStore); Assert.DoesNotContain(",3,", await csv.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 32621 && a.ActionCode == "export").ExecuteDeleteAsync();
        using (var csv = await host.Send(BoundRequest(cookies, "api/orders/list/export.csv")))
            Assert.Equal(HttpStatusCode.Forbidden, csv.StatusCode);
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.Enable, false));
        using var denied = await host.Send(BoundRequest(cookies, "api/orders/list"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_native_detail_and_csv_preserve_child_keys_deleted_scope_and_full_decimal_precision()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        const decimal exact = 1234567890123.12345678m;
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(d => d.IndividualUnitPrice, exact).SetProperty(d => d.Amount, exact));
        h.Db.OrderMaterials.Add(new() { WebOrderNo = h.Number.ToLowerInvariant() + " ", WebOrderDetailNo = 1,
            ProductCd = "p1 ", ProcessCd = "PROC", MaterialCd = "MAT", SupplyUnitPrice = exact });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var dto = await h.Service.GetByWebOrderNoAsync(h.Number.ToLowerInvariant() + " ");
        Assert.Equal(exact, Assert.Single(dto!.Details[0].Materials).SupplyUnitPrice);
        var csv = Encoding.UTF8.GetString(await h.Service.ExportListCsvAsync(new()));
        Assert.Contains("1234567890123.12345678", csv);
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.IsDeleted, true));
        Assert.Equal(0, (await h.Service.SearchOrdersAsync(new())).Item2);
        Assert.Equal(2, (await h.Service.SearchOrdersAsync(new() { IncludeDeleted = true })).Item2);
        Assert.Equal(2, (await h.Service.GetByWebOrderNoAsync(h.Number, true))!.Details.Count);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_list_holds_native_business_department_until_read_completes()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantOrderReads(h);
        var admin = await MappingAdmin(h);
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Child));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 2));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "SALES"));
        await SaveMapping(h, admin, await MappingCommand(h, admin));
        Task? mover = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("FOR SHARE OF d,o")) return;
            hook.After = null;
            mover = Task.Run(async () =>
            {
                await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_Order\" SET \"OrderDepartment\"='OTHER' WHERE \"TenantId\"={h.Tenant} AND \"WebOrderNo\"={h.Number}");
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE public."T_Order"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(mover.IsCompleted);
        };
        try { Assert.Equal(2, (await h.Service.SearchOrdersAsync(new())).Item2); }
        finally { if (mover != null) await mover; }
        Assert.True(blocked); Assert.Equal(0, (await h.Service.SearchOrdersAsync(new())).Item2);
    }
}
