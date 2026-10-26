using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantReplenish(Harness h, int scope = 5)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == 423);
        if (menu is null) h.Db.Sys_Menus.Add(new() { MenuId = 423, MenuKey = "wms-replenish", MenuName = "Replenishment" });
        else menu.MenuKey = "wms-replenish";
        foreach (var action in new[] { "query", "add", "generate", "execute", "cancel" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 423 && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = 423, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 423, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-replenish", ScopeType = scope });
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-replenish", "Replenishment", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields("wms-replenish", [new("OperatorCd", "OperatorCd"), new("Remarks", "Remarks")]); catalog.Seal();
        return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static ReplenishService Replenishments(Harness h, OperationsOwnerV1 owner) =>
        new(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IMobileTaskV2Service>(), ReplenishLegacyScopes(), owner);
    private static IWmsAccessScopeProvider ReplenishLegacyScopes(WmsAccessScope? scope = null)
    {
        var provider = new Mock<IWmsAccessScopeProvider>();
        provider.Setup(p => p.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope ?? WmsAccessScope.All);
        return provider.Object;
    }
    private static async Task SeedReplenish(Harness h, string number = "RPL-OWNER", string creator = "price-owner")
    {
        if (!await h.Db.Locations.AnyAsync(l => l.WarehouseCd == "W1"))
            h.Db.Locations.AddRange(new Location { WarehouseCd = "W1", LocationCd = "FROM", AreaCd = "SOURCE" },
                new Location { WarehouseCd = "W1", LocationCd = "TO", AreaCd = "TARGET" });
        h.Db.ReplenishOrders.Add(new() { ReplenishNo = number, ProductCd = "P1", WarehouseCd = "W1",
            FromLocationCd = "FROM", ToLocationCd = "TO", Qty = 12.12345678m, Creator = creator,
            OperatorCd = "private operator", Remarks = "private note", Status = ReplenishStatus.Pending });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_missing_authority_cannot_read_source_with_target_only_scope()
    {
        await using var h = await Seed(); await SeedReplenish(h);
        var legacy = ReplenishLegacyScopes(new(false, [new("W1", "TARGET")]));
        var service = new ReplenishService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IMobileTaskV2Service>(), legacy);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_missing_authority_cannot_cancel_with_forged_actor()
    {
        await using var h = await Seed(); await SeedReplenish(h);
        var service = new ReplenishService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IMobileTaskV2Service>(), ReplenishLegacyScopes());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CancelAsync("RPL-OWNER", "forged-admin"));
        Assert.Equal(ReplenishStatus.Pending, await h.Db.ReplenishOrders.Select(r => r.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_requires_both_actual_locations_and_unions_current_area_grants()
    {
        await using var h = await Seed(); await SeedReplenish(h); var service = Replenishments(h, await GrantReplenish(h));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1", AreaCd = "TARGET" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await service.SearchAsync(new())); Assert.Equal(0, (await service.SearchPageAsync(new())).Total); Assert.Null(await service.GetAsync("RPL-OWNER"));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ExecuteAsync("RPL-OWNER", "forged"))).Status);
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1", AreaCd = "SOURCE" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.ReplenishOrders.ExecuteUpdateAsync(s => s.SetProperty(r => r.FromLocationCd, "from ").SetProperty(r => r.ToLocationCd, "to "));
        Assert.Equal(12.12345678m, Assert.Single(await service.SearchAsync(new() { Status = 0 })).Qty);
        await h.Db.Locations.Where(l => l.LocationCd == "FROM").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDeleted, true));
        Assert.Empty(await service.SearchAsync(new())); Assert.Equal(0, (await service.SearchPageAsync(new())).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_self_and_tenant_predicates_precede_paging_and_do_not_mutate_input()
    {
        await using var h = await Seed(); await SeedReplenish(h); await SeedReplenish(h, "RPL-OTHER", "other");
        await using var foreign = await Seed(); await SeedReplenish(foreign, "RPL-FOREIGN");
        var service = Replenishments(h, await GrantReplenish(h, 1));
        await h.Db.ReplenishOrders.Where(r => r.ReplenishNo == "RPL-OTHER").ExecuteUpdateAsync(s => s.SetProperty(r => r.Priority, 1));
        var input = new ReplenishSearchQuery { Page = 0, PageSize = 1, Status = 0 };
        Assert.Equal("RPL-OWNER", Assert.Single(await service.SearchAsync(input)).ReplenishNo); Assert.Equal(0, input.Page);
        Assert.Null(await service.GetAsync("RPL-FOREIGN")); Assert.Null(await service.GetAsync("RPL-OTHER"));
        await h.Db.ReplenishOrders.Where(r => r.ReplenishNo == "RPL-OWNER").ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, (string?)null));
        Assert.Empty(await service.SearchAsync(new()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Replenish_owner_department_requires_explicit_binding_and_current_healthy_ancestry(int scope)
    {
        await using var h = await Seed(); await SeedReplenish(h); var service = Replenishments(h, await GrantReplenish(h));
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        Assert.Empty(await service.SearchAsync(new())); Assert.Equal(0, (await service.SearchPageAsync(new())).Total);
        var id = await h.Db.ReplenishOrders.Select(r => r.Id).SingleAsync(); await BindStandalone(h, admin, "replenish-order", id);
        Assert.Single(await service.SearchAsync(new())); Assert.Equal(1, (await service.SearchPageAsync(new())).Total);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE object_id={id}");
        Assert.Empty(await service.SearchAsync(new())); Assert.Equal(0, (await service.SearchPageAsync(new())).Total);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE object_id={id}");
        await h.Db.Sys_Depts.Where(d => d.Id == admin.Root).ExecuteUpdateAsync(s => s.SetProperty(d => d.Enable, false));
        if (scope == 4)
        {
            Assert.Empty(await service.SearchAsync(new())); Assert.Equal(0, (await service.SearchPageAsync(new())).Total);
        }
        else
        {
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()));
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchPageAsync(new()));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_native_binding_rejects_foreign_objects_and_departments_and_preserves_original_operation()
    {
        await using var h = await Seed(); await SeedReplenish(h); var admin = await MappingAdmin(h);
        var id = await h.Db.ReplenishOrders.Select(r => r.Id).SingleAsync();
        Assert.Equal(id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("replenish-order", "RPL-", _ => Task.FromResult(h.User), default)).Id);
        var command = await StandaloneCommand(h, admin, "replenish-order", id); var saved = await SaveStandalone(h, admin, command);
        Assert.Equal(saved, await SaveStandalone(h, admin, command));
        Assert.Single((await h.Db.Sys_SecurityLogs.Select(x => x.ContextJson).ToArrayAsync()).Where(x => x?.Contains(command.OperationId.ToString()) == true));
        var stale = await StandaloneCommand(h, admin, "replenish-order", id, saved.EntityId);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        Assert.Equal(412, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => SaveStandalone(h, admin, stale))).Status);
        await using var foreign = await Seed(); await SeedReplenish(foreign); var otherId = await foreign.Db.ReplenishOrders.Select(r => r.Id).SingleAsync();
        var otherAdmin = await MappingAdmin(foreign);
        Assert.Equal(409, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => BindStandalone(h, admin, "replenish-order", otherId))).Status);
        var wrongObject = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'replenish-order',{otherId},{admin.Child},true)
            """)); Assert.Equal("23514", wrongObject.SqlState);
        var wrongDept = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET department_id={otherAdmin.Child} WHERE id={saved.EntityId}
            """)); Assert.Equal("23514", wrongDept.SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_projects_current_fields_without_modifying_native_source()
    {
        await using var h = await Seed(); await SeedReplenish(h); var service = Replenishments(h, await GrantReplenish(h));
        var listed = Assert.Single(await service.SearchAsync(new()));
        Assert.Null(listed.OperatorCd); Assert.Null(listed.Remarks); Assert.Null(listed.Creator); Assert.Null(listed.Modifier);
        Assert.Equal("private note", (await service.GetAsync("RPL-OWNER"))!.Remarks);
        foreach (var field in new[] { "OperatorCd", "Remarks" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-replenish", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var detail = (await service.GetAsync("RPL-OWNER"))!; Assert.Null(detail.OperatorCd); Assert.Null(detail.Remarks);
        Assert.Equal(12.12345678m, detail.Qty); Assert.Equal("private note", await h.Db.ReplenishOrders.Select(r => r.Remarks).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_five_legacy_commands_reject_before_sequence_task_stock_capacity_or_audit()
    {
        await using var h = await Seed(); await SeedReplenish(h); var owner = await GrantReplenish(h);
        var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var tasks = new Mock<IMobileTaskV2Service>(MockBehavior.Strict);
        var legacy = new Mock<IWmsAccessScopeProvider>(MockBehavior.Strict); var service = new ReplenishService(h.Db, seq.Object, tasks.Object, legacy.Object, owner);
        var audit = await h.Db.Sys_FieldAuditLogs.CountAsync(); var security = await h.Db.Sys_SecurityLogs.CountAsync();
        var capacity = await h.Db.Locations.Select(l => l.ReservedCapacityQty).ToArrayAsync();
        Func<Task>[] commands = [() => service.CreateAsync(new(), "forged"), () => service.UpdateAsync("RPL-OWNER", new(), "forged"),
            () => service.GenerateBatchAsync("W1", 10m, "forged"), () => service.ExecuteAsync("RPL-OWNER", "forged"),
            () => service.CancelAsync("RPL-OWNER", "forged")];
        foreach (var command in commands)
        {
            var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(command);
            Assert.Equal(503, failure.Status); Assert.Equal("REPLENISH_OWNER_ADOPTION_UNPROVEN", failure.Message);
            Assert.False(h.Db.ChangeTracker.HasChanges());
        }
        seq.VerifyNoOtherCalls(); tasks.VerifyNoOtherCalls(); legacy.VerifyNoOtherCalls();
        Assert.Equal(ReplenishStatus.Pending, await h.Db.ReplenishOrders.Select(r => r.Status).SingleAsync());
        Assert.Equal(0, await h.Db.MobileTasks.CountAsync()); Assert.Equal(0, await h.Db.WmsSequences.CountAsync());
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(capacity, await h.Db.Locations.Select(l => l.ReservedCapacityQty).ToArrayAsync());
        Assert.Equal(audit, await h.Db.Sys_FieldAuditLogs.CountAsync()); Assert.Equal(security, await h.Db.Sys_SecurityLogs.CountAsync());
        Assert.False((await service.GetCapabilitiesAsync()).WritesAvailable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_rechecks_current_grants_and_final_field_source_without_false_empty_success()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedReplenish(h);
        var service = Replenishments(h, await GrantReplenish(h)); var reads = 0;
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final source unavailable"); return Task.CompletedTask; };
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()))).Status); reads = 0; Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchPageAsync(new()))).Status); hook.Before = null;
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 423 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetCapabilitiesAsync())).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetAsync("RPL-OWNER"))).Status);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 423 && a.ActionCode == "cancel").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CancelAsync("RPL-OWNER", "forged"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_rejects_wrong_context_tracked_objects_and_outer_transaction()
    {
        await using var h = await Seed(); await SeedReplenish(h); var owner = await GrantReplenish(h);
        await using var other = await Seed(); await SeedReplenish(other);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Replenishments(other, owner).SearchAsync(new()))).Status);
        var row = await h.Db.ReplenishOrders.SingleAsync();
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Replenishments(h, owner).SearchAsync(new()))).Status);
        row.Remarks = "pending caller work";
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Replenishments(h, owner).CancelAsync(row.ReplenishNo, "forged"))).Status);
        Assert.True(h.Db.ChangeTracker.HasChanges()); h.Db.ChangeTracker.Clear();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Replenishments(h, owner).SearchAsync(new()))).Status);
        await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_native_fence_holds_object_ownership_until_final_authority_read()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedReplenish(h);
        var service = Replenishments(h, await GrantReplenish(h, 1)); var attempted = false;
        hook.Before = async command =>
        {
            if (attempted || !command.CommandText.Contains("FROM \"T_ReplenishOrder\"")) return; attempted = true;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ReplenishOrders.ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "other")));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(failure.InnerException).SqlState); await tx.RollbackAsync();
        };
        Assert.Single(await service.SearchAsync(new())); Assert.True(attempted); hook.Before = null;
        await h.Db.ReplenishOrders.ExecuteUpdateAsync(s => s.SetProperty(r => r.Creator, "other")); Assert.Empty(await service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_catalog_migration_preserves_existing_action_and_never_grants_roles()
    {
        await using var h = await Seed(); await GrantReplenish(h); await using var other = await Seed();
        var id = await h.Db.Sys_MenuActions.Where(a => a.MenuId == 423 && a.ActionCode == "query").Select(a => a.Id).SingleAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.ReplenishDepartmentOwnerV1();
        foreach (var operation in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        Assert.Equal(id, await h.Db.Sys_MenuActions.Where(a => a.MenuId == 423 && a.ActionCode == "query").Select(a => a.Id).SingleAsync());
        Assert.Single(await other.Db.Sys_MenuActions.Where(a => a.MenuId == 423 && a.ActionCode == "query").ToArrayAsync());
        Assert.False(await other.Db.Sys_RoleActions.AnyAsync(a => a.MenuId == 423 && a.ActionCode == "query"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Replenish_owner_actual_Program_login_current_query_and_safe_command_denial()
    {
        await using var h = await Seed(); await SeedReplenish(h); await GrantReplenish(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using (var caps = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/replenish/capabilities")))
        {
            Assert.True(caps.IsSuccessStatusCode, await OperationsFailure(caps));
            using var body = JsonDocument.Parse(await caps.Content.ReadAsStringAsync()); Assert.False(body.RootElement.GetProperty("data").GetProperty("writesAvailable").GetBoolean());
        }
        using (var list = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/replenish")))
        { Assert.True(list.IsSuccessStatusCode, await OperationsFailure(list)); Assert.DoesNotContain("private note", await list.Content.ReadAsStringAsync()); Assert.True(list.Headers.CacheControl?.NoStore); }
        using (var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/replenish/RPL-OWNER/cancel")))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); Assert.Contains("REPLENISH_OWNER_ADOPTION_UNPROVEN", await denied.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 423 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/replenish/RPL-OWNER")); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(ReplenishStatus.Pending, await h.Db.ReplenishOrders.Select(r => r.Status).SingleAsync());
    }
}
