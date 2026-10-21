using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

[Collection("TenantManagementPostgres")]
public sealed partial class OrderPriceCorrectionPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private sealed class Workflow : IPowerEggWorkflowService
    {
        internal int Calls;
        public Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? applicantStaffCd,
            CancellationToken ct = default) { Calls++; return Task.FromResult(true); }
    }

    private sealed record Harness(CP6Context Db, Guid Tenant, Guid User, string Number,
        DefaultHttpContext Http, AuthorizationCatalogStoreV1 Catalog, Workflow Workflow) : IAsyncDisposable
    {
        // Controlled service fixture. The companion HTTP cases use actual Program/login/session evidence.
        internal OrderPriceCorrectionOwnerV1 Owner => new(Db, new HttpContextAccessor { HttpContext = Http },
            _ => Task.FromResult(User), Catalog.Capture);
        internal OrderService Service => new(Db, Workflow, new NoOpWmsBridgeHook(), priceCorrectionOwner: Owner,
            readOwner: new OrderReadOwnerV1(Db, new HttpContextAccessor { HttpContext = Http }, _ => Task.FromResult(User), Catalog.Capture));
        internal OrderPriceCorrectionBatchUpdateDto Request(decimal price = 4m) => new()
        {
            Items = [new() { WebOrderNo = Number, WebOrderDetailNo = 1,
                IndividualUnitPriceAfter = price, SetUnitPriceAfter = null, PriceChangeReason = "negotiated" }]
        };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<Harness> Seed(params IInterceptor[] interceptors)
    {
        var tenant = Guid.NewGuid();
        var db = server.Context(new TenantContext { CurrentTenantId = tenant }, null, interceptors);
        var user = new Sys_User { UserName = "price-owner", Password = new BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 };
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "owned" });
        db.Sys_Roles.Add(new() { RoleId = 72, RoleName = "price-correction" });
        db.Sys_Users.Add(user);
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "erp-order-price-correction");
        if (menu == null)
        {
            menu = new() { MenuId = 32720, MenuKey = "erp-order-price-correction", MenuName = "price-correction" };
            db.Sys_Menus.Add(menu);
        }
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "correct" });
        db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "correct", ActionName = "Correct price" });
        db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "order", ScopeType = 5 });
        var number = "W32" + tenant.ToString("N")[..16];
        var order = new Order { WebOrderNo = number, CustomerCd = "C", OrderType = "1", Creator = user.UserName };
        for (var i = 1; i <= 2; i++)
            order.Details.Add(new() { WebOrderNo = number, WebOrderDetailNo = i, ProductCd = "P" + i,
                Quantity = 2m, SalesPriceDiv = "1", IndividualUnitPrice = 3m, SetUnitPrice = null,
                Amount = 6m, Creator = user.UserName, ApprovalStatus = 0 });
        db.Orders.Add(order);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("order", [new("UnitPrice", "単価"), new("Amount", "金額")]);
        catalog.RegisterScope("order", "受注", [1, 2, 3, 4, 5], 2); catalog.Seal();
        return new(db, tenant, user.Id, number,
            new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(user.Id, tenant) }, catalog, new());
    }

    [CurrentPermissionPostgresFact]
    public async Task Revoked_field_write_is_rejected_at_service_boundary_without_workflow_or_business_effect()
    {
        await using var h = await Seed();
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(h.Request(), "forged-name"));
        Assert.Equal("FIELD_WRITE_FORBIDDEN", denied.Message);
        Assert.Equal(new decimal?[] { 3m, 3m }, await h.Db.OrderDetails.AsNoTracking()
            .OrderBy(d => d.WebOrderDetailNo).Select(d => d.IndividualUnitPrice).ToArrayAsync());
        Assert.Equal(0, h.Workflow.Calls);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stale_selected_row_rolls_back_all_sibling_price_propagation_and_workflow()
    {
        await using var h = await Seed();
        var request = h.Request();
        request.Items[0].SetUnitPriceAfter = 9m;
        request.Items[0].RowVersion = [0, 0, 0, 0, 0, 0, 0, 1];
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Service.BatchUpdatePriceAsync(request, "forged-name"));
        h.Db.ChangeTracker.Clear();
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Null(d.SetUnitPrice));
        Assert.Equal(0, h.Workflow.Calls);
    }

    private static Task<int> Intents(Harness h) => h.Db.Database.SqlQuery<int>(
        $"SELECT COUNT(*)::integer AS \"Value\" FROM cp6_order_price_v1.workflow_intents WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task Allowed_native_batch_keeps_precision_bound_actor_missing_keys_and_durable_pending_intent()
    {
        await using var h = await Seed();
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).ExecuteUpdateAsync(s => s.SetProperty(d => d.Quantity, 1m));
        var request = h.Request(1234567890123.12345678m);
        request.Items[0].RowVersion = await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).Select(d => d.RowVersion).SingleAsync();
        request.Items[0].WebOrderNo = h.Number.ToLowerInvariant() + " "; // Native accepted bpchar/collation contract.
        request.Items.Add(new() { WebOrderNo = "MISSING", WebOrderDetailNo = 9 });
        var result = await h.Service.BatchUpdatePriceAsync(request, "forged-name");
        Assert.Equal(1, result.UpdatedCount); Assert.Equal(1, result.WfRequestedCount);
        Assert.Equal("AwaitingAdapter", result.WorkflowDeliveryState);
        Assert.Equal(new[] { "MISSING-9" }, result.ConflictedKeys); Assert.Equal(0, h.Workflow.Calls);
        h.Db.ChangeTracker.Clear();
        var row = await h.Db.OrderDetails.SingleAsync(d => d.WebOrderDetailNo == 1);
        Assert.Equal(1234567890123.12345678m, row.Amount); Assert.Equal(row.Amount, row.IndividualUnitPrice);
        Assert.Equal("price-owner", row.Modifier); Assert.Equal(1, row.ApprovalStatus); Assert.False(row.WfApprovalFlg);
        Assert.Equal(1, await Intents(h));
        var payload = await h.Db.Database.SqlQuery<string>(
            $"SELECT payload::text AS \"Value\" FROM cp6_order_price_v1.workflow_intents WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Contains("1234567890123.12345678", payload);
    }

    [CurrentPermissionPostgresFact]
    public async Task Set_price_propagation_checks_and_updates_every_actual_effect_row_atomically()
    {
        await using var h = await Seed();
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SalesPriceDiv, "2").SetProperty(d => d.Quantity, 3m).SetProperty(d => d.Amount, 0m));
        var request = h.Request(); request.Items[0].SetUnitPriceAfter = 5.25m;
        request.Items[0].RowVersion = await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).Select(d => d.RowVersion).SingleAsync();
        var result = await h.Service.BatchUpdatePriceAsync(request, "forged");
        h.Db.ChangeTracker.Clear(); var rows = await h.Db.OrderDetails.OrderBy(d => d.WebOrderDetailNo).ToArrayAsync();
        Assert.Equal(1, result.UpdatedCount); Assert.Equal(2, result.WfRequestedCount);
        Assert.All(rows, d => { Assert.Equal(5.25m, d.SetUnitPrice); Assert.Equal(1, d.ApprovalStatus); Assert.False(d.WfApprovalFlg); });
        Assert.Equal(8m, rows[0].Amount); Assert.Equal(15.75m, rows[1].Amount);
        Assert.Equal(2, await Intents(h)); Assert.Equal(0, h.Workflow.Calls);
    }

    [CurrentPermissionPostgresFact]
    public async Task Readonly_derived_amount_and_hidden_price_reject_whole_intent_including_propagation()
    {
        await using var h = await Seed();
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "Amount", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var request = h.Request(); request.Items[0].SetUnitPriceAfter = 7m;
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(request, null));
        Assert.Equal("FIELD_WRITE_FORBIDDEN", denied.Message); Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => { Assert.Equal(3m, d.IndividualUnitPrice); Assert.Null(d.SetUnitPrice); });
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s => s.SetProperty(d => d.FieldName, "UnitPrice").SetProperty(d => d.Access, 3));
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(request, null))).Message);
        Assert.Equal(0, await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Self_scope_checks_native_creator_for_selected_and_propagated_siblings_without_claim_name_trust()
    {
        await using var h = await Seed();
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(d => d.ScopeType, 1));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 1).ExecuteUpdateAsync(s => s.SetProperty(d => d.Creator, "PRICE-OWNER "));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(d => d.Creator, "other-owner"));
        Assert.Equal(1, (await h.Service.BatchUpdatePriceAsync(h.Request(), "other-owner")).UpdatedCount);
        h.Db.ChangeTracker.Clear();
        var request = h.Request(6m); request.Items[0].SetUnitPriceAfter = 9m;
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(request, "other-owner"))).Message);
        Assert.Equal(1, await Intents(h)); // Only the first selected-row operation committed.
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Null(d.SetUnitPrice));
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Unmapped_department_scopes_refuse_instead_of_manufacturing_object_access(int scope)
    {
        await using var h = await Seed();
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(d => d.ScopeType, scope));
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(h.Request(), null));
        Assert.Equal(503, denied.Status); Assert.Equal("OWNER_SCOPE_MAPPING_UNAVAILABLE", denied.Message);
        Assert.Equal(0, await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Foreign_subject_or_tenant_cannot_use_owner_and_foreign_business_key_has_no_effect()
    {
        await using var h = await Seed(); await using var foreign = await Seed();
        var request = foreign.Request();
        var missing = await h.Service.BatchUpdatePriceAsync(request, null);
        Assert.Equal(0, missing.UpdatedCount); Assert.Single(missing.ConflictedKeys);
        Assert.Equal(0, await Intents(h)); Assert.Equal(0, await Intents(foreign));
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, foreign.Tenant);
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Old_transaction_dirty_context_or_missing_owner_never_falls_back_and_preserves_callers_work()
    {
        await using var h = await Seed();
        await using (var tx = await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
        {
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(h.Request(), null));
            Assert.Same(tx, h.Db.Database.CurrentTransaction); await tx.RollbackAsync();
        }
        var user = await h.Db.Sys_Users.SingleAsync(); user.Email = "unsaved@example.test";
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.BatchUpdatePriceAsync(h.Request(), null));
        Assert.Equal("unsaved@example.test", user.Email); Assert.True(h.Db.ChangeTracker.HasChanges());
        h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new OrderService(h.Db, h.Workflow, new NoOpWmsBridgeHook())
            .BatchUpdatePriceAsync(h.Request(), null));
        Assert.Equal(0, await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Removed_action_definition_does_not_leave_an_orphan_role_grant_usable()
    {
        await using var h = await Seed();
        await h.Db.Sys_MenuActions.ExecuteDeleteAsync();
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync());
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => h.Service.BatchUpdatePriceAsync(h.Request(), null))).Message);
        Assert.Equal(0, await Intents(h));
    }
}
