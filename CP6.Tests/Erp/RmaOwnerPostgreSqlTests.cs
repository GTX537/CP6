using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Net;
using System.Text.Json;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantRma(Harness h, int scope = 5)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == 426);
        if (menu is null) h.Db.Sys_Menus.Add(new() { MenuId = 426, MenuKey = "wms-rma", MenuName = "RMA" });
        else menu.MenuKey = "wms-rma";
        foreach (var action in new[] { "query", "add", "receive", "inspect", "judge", "close", "cancel" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 426 && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = 426, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 426, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-rma", ScopeType = scope });
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-rma", "RMA", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields("wms-rma", [new("CustomerName", "CustomerName"), new("OperatorCd", "OperatorCd"),
            new("Remarks", "Remarks"), new("ReturnReason", "ReturnReason")]);
        catalog.Seal();
        return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static RmaService Rmas(Harness h, OperationsOwnerV1 owner) =>
        new(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), Mock.Of<IErpBridgeHook>(), owner: owner);

    private static async Task SeedRma(Harness h, string number = "RMA-OWNER", string creator = "price-owner")
    {
        h.Db.RmaHeaders.Add(new RmaHeader
        {
            RmaNo = number, CustomerCd = "CUSTOMER-1", CustomerName = "Private customer",
            WarehouseCd = "W1", Creator = creator, Status = RmaStatus.Authorized,
            OperatorCd = "private operator", ReturnReason = "private reason", Remarks = "private header note"
        });
        h.Db.RmaDetails.Add(new RmaDetail
        {
            RmaNo = number, LineNo = 1, ProductCd = "P1", Qty = 1, Creator = creator, Remarks = "private line note"
        });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_missing_authority_cannot_read_native_rows()
    {
        await using var h = await Seed(); await SeedRma(h);
        var service = new RmaService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_missing_authority_cannot_cancel_with_a_forged_actor()
    {
        await using var h = await Seed(); await SeedRma(h);
        var service = new RmaService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CancelAsync("RMA-OWNER", "forged-admin"));
        Assert.Equal(RmaStatus.Authorized, await h.Db.RmaHeaders.Select(r => r.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_current_read_projects_private_fields_and_preserves_source_rows()
    {
        await using var h = await Seed(); await SeedRma(h); var service = Rmas(h, await GrantRma(h));
        var listed = Assert.Single(await service.SearchAsync(new()));
        Assert.Equal("CUSTOMER-1", listed.CustomerCd); Assert.Null(listed.CustomerName); Assert.Null(listed.ReturnReason);
        Assert.Null(listed.OperatorCd); Assert.Null(listed.Remarks); Assert.Null(listed.Creator);
        Assert.Equal("Private customer", (await service.GetAsync("RMA-OWNER"))!.CustomerName);
        foreach (var field in new[] { "CustomerName", "OperatorCd", "Remarks", "ReturnReason" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var detail = (await service.GetAsync("RMA-OWNER"))!;
        Assert.Null(detail.CustomerName); Assert.Null(detail.OperatorCd); Assert.Null(detail.ReturnReason); Assert.Null(detail.Remarks);
        Assert.Null(Assert.Single(detail.Details).Remarks); Assert.Equal(1m, detail.Details[0].Qty);
        Assert.Equal("Private customer", await h.Db.RmaHeaders.Select(r => r.CustomerName).SingleAsync());
        Assert.Equal("private line note", await h.Db.RmaDetails.Select(r => r.Remarks).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_self_and_warehouse_filters_precede_paging_and_hide_other_tenant_rows()
    {
        await using var h = await Seed(); await SeedRma(h); await SeedRma(h, "RMA-OTHER", "other");
        await using var foreign = await Seed(); await SeedRma(foreign, "RMA-FOREIGN");
        var service = Rmas(h, await GrantRma(h, 1));
        await h.Db.RmaHeaders.Where(r => r.RmaNo == "RMA-OTHER").ExecuteUpdateAsync(s => s.SetProperty(r => r.AppliedDate, DateTime.Today.AddDays(1)));
        Assert.Equal("RMA-OWNER", Assert.Single(await service.SearchAsync(new() { PageSize = 1 })).RmaNo);
        Assert.Null(await service.GetAsync("RMA-OTHER")); Assert.Null(await service.GetAsync("RMA-FOREIGN"));
        await h.Db.RmaDetails.Where(r => r.RmaNo == "RMA-OWNER").ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "other"));
        Assert.Empty(await service.SearchAsync(new()));
        await h.Db.RmaDetails.Where(r => r.RmaNo == "RMA-OWNER").ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "price-owner"));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); Assert.Empty(await service.SearchAsync(new()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Rma_owner_department_requires_real_stored_shipment_and_explicit_order_department(int scope)
    {
        await using var h = await Seed(); await SeedRma(h); var service = Rmas(h, await GrantRma(h));
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        Assert.Empty(await service.SearchAsync(new())); // No inference from the actor or warehouse.
        await SaveMapping(h, admin, await MappingCommand(h, admin));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "sales "));
        h.Db.OutboundOrders.Add(new() { OutboundNo = "SHIP-1", WarehouseCd = "W1", OutboundType = OutboundType.Shipping,
            CustomerCd = "CUSTOMER-1", WebOrderNo = h.Number, Creator = "origin" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.RmaHeaders.ExecuteUpdateAsync(s => s.SetProperty(r => r.OriginalShippingNo, "ship-1 "));
        Assert.Single(await service.SearchAsync(new()));
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerCd, "OTHER"));
        Assert.Empty(await service.SearchAsync(new()));
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerCd, "CUSTOMER-1").SetProperty(o => o.IsDeleted, true));
        Assert.Empty(await service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_area_grants_union_actual_destinations_and_reject_unknown_location()
    {
        await using var h = await Seed(); await SeedRma(h); var service = Rmas(h, await GrantRma(h));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        h.Db.WmsRoleScopes.AddRange(new WmsRoleScope { RoleId = 72, WarehouseCd = "W1", AreaCd = "A" },
            new WmsRoleScope { RoleId = 72, WarehouseCd = "W1", AreaCd = "B" });
        h.Db.Locations.AddRange(new Location { WarehouseCd = "W1", LocationCd = "L1", AreaCd = "A" },
            new Location { WarehouseCd = "W1", LocationCd = "L2", AreaCd = "B" });
        h.Db.RmaDetails.Add(new() { RmaNo = "RMA-OWNER", LineNo = 2, ProductCd = "P2", Qty = 1, DestLocationCd = "L2", Creator = "price-owner" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await service.SearchAsync(new()));
        await h.Db.RmaDetails.Where(d => d.LineNo == 1).ExecuteUpdateAsync(s => s.SetProperty(d => d.DestLocationCd, "l1 "));
        Assert.Single(await service.SearchAsync(new()));
        await h.Db.Locations.Where(l => l.LocationCd == "L2").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDeleted, true));
        Assert.Empty(await service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_all_legacy_commands_reject_before_sequence_stock_audit_or_credit_effects()
    {
        await using var h = await Seed(); await SeedRma(h); var owner = await GrantRma(h);
        var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var stock = new Mock<IStockMovementService>(MockBehavior.Strict);
        var credit = new Mock<IErpBridgeHook>(MockBehavior.Strict); var service = new RmaService(h.Db, seq.Object, stock.Object, credit.Object, owner: owner);
        var audit = await h.Db.Sys_FieldAuditLogs.CountAsync(); var security = await h.Db.Sys_SecurityLogs.CountAsync();
        Func<Task>[] commands = [() => service.CreateAsync(new(), "forged"), () => service.ReceiveAsync("RMA-OWNER", "forged"),
            () => service.StartInspectionAsync("RMA-OWNER", "forged"), () => service.JudgeAndDisposeAsync("RMA-OWNER", [new() { LineNo = 1, Judgement = "RESELL" }], "forged"),
            () => service.CloseAsync("RMA-OWNER", "forged"), () => service.CancelAsync("RMA-OWNER", "forged")];
        foreach (var command in commands)
        {
            var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(command);
            Assert.Equal(503, failure.Status); Assert.Equal("RMA_OWNER_ADOPTION_UNPROVEN", failure.Message);
            Assert.False(h.Db.ChangeTracker.HasChanges());
        }
        seq.VerifyNoOtherCalls(); stock.VerifyNoOtherCalls(); credit.VerifyNoOtherCalls();
        Assert.Equal(RmaStatus.Authorized, await h.Db.RmaHeaders.Select(r => r.Status).SingleAsync());
        Assert.Null(await h.Db.RmaDetails.Select(r => r.Judgement).SingleAsync());
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await h.Db.WmsSequences.CountAsync());
        Assert.Equal(audit, await h.Db.Sys_FieldAuditLogs.CountAsync()); Assert.Equal(security, await h.Db.Sys_SecurityLogs.CountAsync());
        Assert.False((await service.GetCapabilitiesAsync()).WritesAvailable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_rechecks_current_grant_and_does_not_turn_unavailable_into_empty()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedRma(h);
        var service = Rmas(h, await GrantRma(h)); var reads = 0;
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final source unavailable"); return Task.CompletedTask; };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()))).Message);
        hook.Before = null;
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 426 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetCapabilitiesAsync())).Status);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 426 && a.ActionCode == "cancel").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CancelAsync("RMA-OWNER", "forged"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_rejects_ambient_work_and_cross_context_authority()
    {
        await using var h = await Seed(); await SeedRma(h); var owner = await GrantRma(h);
        await using var other = await Seed(); await SeedRma(other);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Rmas(other, owner).SearchAsync(new()))).Status);
        var row = await h.Db.RmaHeaders.SingleAsync();
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Rmas(h, owner).SearchAsync(new()))).Status);
        row.Remarks = "pending caller work";
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Rmas(h, owner).CancelAsync(row.RmaNo, "forged"))).Status);
        Assert.True(h.Db.ChangeTracker.HasChanges()); h.Db.ChangeTracker.Clear();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Rmas(h, owner).SearchAsync(new()))).Status);
        await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_read_holds_native_object_fence_until_current_authority_is_rechecked()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedRma(h);
        var service = Rmas(h, await GrantRma(h, 1)); var attempted = false;
        var table = h.Db.Model.FindEntityType(typeof(RmaHeader))!.GetTableName();
        hook.Before = async command =>
        {
            if (attempted || !command.CommandText.Contains($"FROM \"{table}\"")) return;
            attempted = true;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.RmaHeaders
                .Where(r => r.RmaNo == "RMA-OWNER").ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "other")));
            var blocked = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, blocked.SqlState);
            await tx.RollbackAsync();
        };
        Assert.Single(await service.SearchAsync(new())); Assert.True(attempted); hook.Before = null;
        await using var after = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(1, await after.RmaHeaders.Where(r => r.RmaNo == "RMA-OWNER")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "other")));
        Assert.Empty(await service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_catalog_migration_adds_query_without_role_grants()
    {
        await using var h = await Seed(); await GrantRma(h); await using var other = await Seed();
        var id = await h.Db.Sys_MenuActions.Where(a => a.MenuId == 426 && a.ActionCode == "query").Select(a => a.Id).SingleAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.RmaOwnerCatalogV1();
        foreach (var operation in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        Assert.Equal(id, await h.Db.Sys_MenuActions.Where(a => a.MenuId == 426 && a.ActionCode == "query").Select(a => a.Id).SingleAsync());
        Assert.Single(await other.Db.Sys_MenuActions.Where(a => a.MenuId == 426 && a.ActionCode == "query").ToArrayAsync());
        Assert.False(await other.Db.Sys_RoleActions.AnyAsync(a => a.MenuId == 426 && a.ActionCode == "query"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Rma_owner_actual_Program_login_scoped_reads_and_native_write_denial()
    {
        await using var h = await Seed(); await SeedRma(h); await GrantRma(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using (var caps = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/rma/capabilities")))
        {
            Assert.True(caps.IsSuccessStatusCode, await OperationsFailure(caps));
            using var body = JsonDocument.Parse(await caps.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("data").GetProperty("writesAvailable").GetBoolean());
        }
        using (var list = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/rma")))
        {
            Assert.True(list.IsSuccessStatusCode, await OperationsFailure(list)); Assert.DoesNotContain("Private customer", await list.Content.ReadAsStringAsync());
        }
        using (var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/rma/RMA-OWNER/cancel")))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); Assert.Contains("RMA_OWNER_ADOPTION_UNPROVEN", await denied.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 426 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/rma/RMA-OWNER")); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(RmaStatus.Authorized, await h.Db.RmaHeaders.Select(r => r.Status).SingleAsync());
    }
}
