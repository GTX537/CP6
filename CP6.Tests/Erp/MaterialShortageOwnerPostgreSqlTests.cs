using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pub;
using CP6.Entity.DTOs.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Localization;
using Moq;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantShortage(Harness h)
    {
        await GrantOperations(h);
        const string key = "wms-material-shortage";
        if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == 417)) h.Db.Sys_Menus.Add(new() { MenuId = 417, MenuKey = key, MenuName = key });
        foreach (var action in new[] { "query", "resolve", "dismiss" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 417 && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = 417, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { MenuId = 417, RoleId = 72, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return ShortageOwner(h);
    }
    private static OperationsOwnerV1 ShortageOwner(Harness h)
    {
        const string key = "wms-material-shortage";
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (resource, fields) in new[] {
            (key, new[] { "Remark" }), ("mes-work-order", Array.Empty<string>()),
            ("wms-outbound-order", new[] { "UnitPrice" }), ("order", new[] { "UnitPrice", "Amount" }) })
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 1);
            catalog.RegisterFields(resource, fields.Select(f => new FieldRegistry.Field(f, f)));
        }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static async Task<MaterialShortage> SeedShortage(Harness h, bool linked = true, string creator = "price-owner", string suffix = "1")
    {
        // Existing native document keys are global alternate keys, even though rows are tenant-filtered.
        var prefix = "S" + h.Tenant.ToString("N")[..10]; var wo = prefix + "W" + suffix; var outbound = prefix + "O" + suffix;
        h.Db.WorkOrders.Add(new() { WorkOrderNo = wo, ProductCd = "FG", Status = WorkOrderStatus.Issued, Creator = creator });
        if (linked)
        {
            h.Db.OutboundOrders.Add(new() { OutboundNo = outbound, WorkOrderNo = wo, WarehouseCd = "W01", OutboundType = OutboundType.Material, Status = OutboundOrderStatus.Confirmed, Creator = creator });
            h.Db.OutboundOrderDetails.Add(new() { OutboundNo = outbound, LineNo = 1, ProductCd = "MAT", RequiredQty = 10m, Creator = creator });
        }
        var row = new MaterialShortage { WorkOrderNo = wo, RelatedOutboundNo = linked ? outbound : null, ProductCd = "MAT",
            RequiredQty = 9999999999999.99999999m, AvailableQty = 0.00000001m, DetectedAt = DateTime.UtcNow,
            Creator = creator, Remark = "private note" };
        h.Db.MaterialShortages.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return row;
    }
    private static MaterialShortageService Shortages(Harness h, OperationsOwnerV1 owner) => new(h.Db, owner);
    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_missing_authority_rejects_even_empty_search()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new MaterialShortageService(h.Db).SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_public_create_cannot_replace_actual_parent_capability()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new MaterialShortageService(h.Db).CreateAsync(new()
        {
            WorkOrderNo = "WO-FORGED", ProductCd = "MAT", RequiredQty = 10, Creator = "forged"
        }));
        Assert.Empty(await h.Db.MaterialShortages.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_missing_authority_cannot_mark_followup_resolved()
    {
        await using var h = await Seed();
        var row = new MaterialShortage { WorkOrderNo = "WO", ProductCd = "MAT", RequiredQty = 10, Creator = "other", DetectedAt = DateTime.UtcNow };
        h.Db.MaterialShortages.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new MaterialShortageService(h.Db).ResolveAsync(row.Id, "forged", "forged"));
        Assert.Equal(MaterialShortageStatus.Open, await h.Db.MaterialShortages.Select(x => x.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_exact_read_real_version_and_followup_do_not_mutate_stock()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var row = await SeedShortage(h); var service = Shortages(h, owner);
        var page = await service.SearchOwnerAsync(new()); var item = Assert.Single(page.Rows);
        Assert.Equal(1, page.Total); Assert.Equal(1, page.OpenCount); Assert.Equal("9999999999999.99999999", item.RequiredQty);
        Assert.Equal("0.00000001", item.AvailableQty); Assert.Equal("9999999999999.99999998", item.ShortQty); Assert.NotEmpty(item.RowVersion!);
        var command = new MaterialShortageActionV1 { RowVersion = item.RowVersion, SetRemark = true, Remark = "followed up" };
        var result = await service.ResolveOwnerAsync(row.Id, command);
        Assert.Equal(MaterialShortageStatus.Resolved, result.Status); Assert.NotEqual(Convert.ToBase64String(item.RowVersion!), Convert.ToBase64String(result.RowVersion!));
        Assert.Equal("price-owner", await h.Db.MaterialShortages.Select(x => x.Modifier).SingleAsync());
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.DismissOwnerAsync(row.Id, command))).Message);
        Assert.Equal(0, (await service.SearchOwnerAsync(new())).OpenCount); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Empty(await h.Db.PurchaseRequests.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_sources_self_tenant_and_warehouse_filter_before_paging()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var first = await SeedShortage(h); var second = await SeedShortage(h, creator: "other", suffix: "2");
        await using var foreign = await Seed(); await SeedShortage(foreign, suffix: "foreign");
        var service = Shortages(h, owner); Assert.Equal(2, (await service.SearchOwnerAsync(new() { PageSize = 1 })).Total);
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "wms-material-shortage").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        Assert.Equal(first.WorkOrderNo, Assert.Single((await service.SearchOwnerAsync(new() { PageSize = 1 })).Rows).WorkOrderNo);
        await h.Db.WorkOrders.Where(x => x.WorkOrderNo == first.WorkOrderNo).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        Assert.Empty((await service.SearchOwnerAsync(new())).Rows);
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey != "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "OTHER")); Assert.Equal(0, (await service.SearchOwnerAsync(new())).Total);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "*"));
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 408 && a.ActionCode == "query").ExecuteDeleteAsync(); Assert.Equal(0, (await service.SearchOwnerAsync(new())).OpenCount);
        h.Db.Sys_RoleActions.Add(new() { MenuId = 408, RoleId = 72, ActionCode = "query" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.OutboundOrders.Where(x => x.OutboundNo == first.RelatedOutboundNo).ExecuteUpdateAsync(s => s.SetProperty(x => x.WorkOrderNo, second.WorkOrderNo));
        Assert.Equal(second.WorkOrderNo, Assert.Single((await service.SearchOwnerAsync(new())).Rows).WorkOrderNo);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 302 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Material_shortage_owner_department_binding_and_source_scope_are_both_required(int scope)
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var row = await SeedShortage(h, linked: false);
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope); var service = Shortages(h, owner);
        Assert.Empty((await service.SearchOwnerAsync(new())).Rows);
        var id = await h.Db.WorkOrders.Select(x => x.Id).SingleAsync(); await BindStandalone(h, admin, "work-order", id);
        Assert.Equal(row.Id, Assert.Single((await service.SearchOwnerAsync(new())).Rows).Id);
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "mes-work-order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.WorkOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other")); Assert.Empty((await service.SearchOwnerAsync(new())).Rows);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_remark_hidden_readonly_and_explicit_preservation_follow_current_min()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var row = await SeedShortage(h); var service = Shortages(h, owner);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-material-shortage", FieldName = "Remark", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var page = await service.SearchOwnerAsync(new()); var item = Assert.Single(page.Rows);
        Assert.False(page.RemarkReadable); Assert.False(page.RemarkWritable); Assert.Null(item.Remark);
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ResolveOwnerAsync(row.Id,
            new() { RowVersion = item.RowVersion, SetRemark = true, Remark = "overwrite" }))).Message);
        var result = await service.ResolveOwnerAsync(row.Id, new() { RowVersion = item.RowVersion }); Assert.Null(result.Remark);
        Assert.Equal("private note", await h.Db.MaterialShortages.Select(x => x.Remark).SingleAsync());
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "second" }); h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = 73 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 73, ResourceKey = "wms-material-shortage", FieldName = "Remark", Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); page = await service.SearchOwnerAsync(new()); Assert.True(page.RemarkWritable);
        Assert.Equal("private note", Assert.Single(page.Rows).Remark);
        await h.Db.Sys_RoleFieldPerms.Where(f => f.RoleId == 73).ExecuteDeleteAsync();
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s => s.SetProperty(x => x.Access, 2)); page = await service.SearchOwnerAsync(new());
        Assert.True(page.RemarkReadable); Assert.False(page.RemarkWritable);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Material_shortage_owner_final_failure_rolls_back_and_lost_commit_stays_unknown(bool lostAck)
    {
        var hook = new CommandHook(); var commit = new CommitFault(); await using var h = await Seed(hook, commit);
        var owner = await GrantShortage(h); var row = await SeedShortage(h); var service = Shortages(h, owner);
        var item = Assert.Single((await service.SearchOwnerAsync(new())).Rows); var reads = 0;
        if (lostAck) commit.Armed = true;
        else hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 4) throw new IOException("final shortage authority unavailable"); return Task.CompletedTask; };
        var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.DismissOwnerAsync(row.Id, new() { RowVersion = item.RowVersion })); hook.Before = null;
        Assert.Equal(lostAck ? "OPERATIONS_OUTCOME_UNKNOWN" : "AUTHORITY_FENCE_UNAVAILABLE", failure.Message);
        Assert.Equal(lostAck ? MaterialShortageStatus.Dismissed : MaterialShortageStatus.Open, await h.Db.MaterialShortages.Select(x => x.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_dirty_context_outer_transaction_and_old_routes_cannot_bypass_versions()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var row = await SeedShortage(h); var service = Shortages(h, owner);
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()));
        await h.Db.MaterialShortages.SingleAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new())); h.Db.ChangeTracker.Clear();
        Assert.Equal("ROW_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ResolveAsync(row.Id, null, "forged"))).Message);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 417 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_freezes_query_and_command_before_first_authority_await()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantShortage(h); var row = await SeedShortage(h); var service = Shortages(h, owner);
        var query = new MaterialShortageQuery { Page = 1, PageSize = 1 };
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; query.Page = 999; } return Task.CompletedTask; };
        var item = Assert.Single((await service.SearchOwnerAsync(query)).Rows);
        var input = new MaterialShortageActionV1 { RowVersion = item.RowVersion, SetRemark = true, Remark = "original" };
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; input.Remark = "mutated"; Array.Clear(input.RowVersion!); } return Task.CompletedTask; };
        Assert.Equal("original", (await service.ResolveOwnerAsync(row.Id, input)).Remark);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Material_shortage_owner_real_parent_creation_remains_atomic_without_independent_manage_grant(bool failFinal)
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantOperations(h); var row = await SeedShortage(h);
        await h.Db.MaterialShortages.ExecuteDeleteAsync(); var calls = 0;
        var notifier = new Mock<IMaterialShortageNotifier>(); notifier.Setup(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns(() => { Assert.Null(h.Db.Database.CurrentTransaction); calls++; return Task.CompletedTask; });
        var seq = new WmsSequenceService(h.Db); var service = new OutboundService(h.Db, seq, new StockMovementService(h.Db, seq),
            shortage: new MaterialShortageService(h.Db), shortageNotifier: notifier.Object, owner: owner);
        var reads = 0;
        if (failFinal) hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final parent authority unavailable"); return Task.CompletedTask; };
        if (failFinal) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.AllocateAsync(row.RelatedOutboundNo!, "forged"));
        else await service.AllocateAsync(row.RelatedOutboundNo!, "forged");
        hook.Before = null; Assert.Equal(failFinal ? 0 : 1, await h.Db.MaterialShortages.CountAsync()); Assert.Equal(failFinal ? 0 : 1, calls);
        if (!failFinal) Assert.Equal("price-owner", await h.Db.MaterialShortages.Select(s => s.Creator).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_actual_notifier_uses_empty_tenant_group_and_never_uncommitted_payload()
    {
        await using var h = await Seed(); var deliveries = new List<(string Group, string Method, object?[] Args)>();
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns((string group) =>
        {
            var proxy = new Mock<IClientProxy>(); proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((method, args, _) => deliveries.Add((group, method, args))).Returns(Task.CompletedTask); return proxy.Object;
        });
        var hub = new Mock<IHubContext<WmsHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        using var provider = new ServiceCollection().AddSingleton<IWmsNotifier>(new SignalRWmsNotifier(hub.Object,
            Mock.Of<CP6.Core.Utilities.INotificationPublisher>(), NullLogger<SignalRWmsNotifier>.Instance, Mock.Of<IStringLocalizer>(), h.Db)).BuildServiceProvider();
        var notifier = new MaterialShortageNotifier(provider, NullLogger<MaterialShortageNotifier>.Instance, h.Db);
        await notifier.NotifyAsync("PRIVATE-WO", "PRIVATE-MAT", 9999999999999.99999999m);
        var sent = Assert.Single(deliveries); Assert.Equal(WmsHub.InventoryGroup(h.Tenant), sent.Group); Assert.Equal("InventoryInvalidatedV1", sent.Method); Assert.Empty(sent.Args);
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await notifier.NotifyAsync("PRIVATE-WO", "PRIVATE-MAT", 1m);
        Assert.Single(deliveries); clients.VerifyGet(x => x.All, Times.Never);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_procurement_without_actual_owner_is_rejected_before_any_effect()
    {
        await using var h = await Seed(); var price = new Mock<ISupplierPriceService>(MockBehavior.Strict); var sequence = new Mock<ISeqService>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new PrGenerationService(h.Db, price.Object, sequence.Object)
            .GenerateFromShortageAsync(Guid.NewGuid(), "forged")); Assert.Equal("PURCHASE_REQUEST_OWNER_REQUIRED", error.Message);
        price.VerifyNoOtherCalls(); sequence.VerifyNoOtherCalls(); Assert.Empty(await h.Db.PurchaseRequests.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_actual_program_http_preserves_version_no_store_and_current_revoke()
    {
        await using var h = await Seed(); await GrantShortage(h); var row = await SeedShortage(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/material-shortage/owner-v1"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var item = body.RootElement.GetProperty("data").GetProperty("rows")[0];
        Assert.Equal("9999999999999.99999999", item.GetProperty("requiredQty").GetString()); var version = item.GetProperty("rowVersion").GetString();
        using var saved = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/wms/material-shortage/owner-v1/{row.Id}/resolve", new { rowVersion = version }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var stale = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/wms/material-shortage/owner-v1/{row.Id}/dismiss", new { rowVersion = version }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 417 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/material-shortage/owner-v1")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_concurrent_real_connections_only_one_version_can_commit()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantShortage(h); var row = await SeedShortage(h);
        var service = Shortages(h, owner); var item = Assert.Single((await service.SearchOwnerAsync(new())).Rows);
        Task<string>? second = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            second = Task.Run(async () =>
            {
                await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var other = h with { Db = db };
                try { await Shortages(other, ShortageOwner(other)).DismissOwnerAsync(row.Id, new() { RowVersion = item.RowVersion }); return "COMMITTED"; }
                catch (OrderPriceCorrectionException error) { return error.Message; }
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(second.IsCompleted);
        };
        string? outcome = null;
        try { await service.ResolveOwnerAsync(row.Id, new() { RowVersion = item.RowVersion, SetRemark = true, Remark = "winner" }); }
        finally { if (second != null) outcome = await second; }
        Assert.True(blocked); Assert.Equal("ROW_VERSION_CONFLICT", outcome);
        var saved = await h.Db.MaterialShortages.AsNoTracking().SingleAsync();
        Assert.Equal(MaterialShortageStatus.Resolved, saved.Status); Assert.Equal("winner", saved.Remark);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_paging_counts_optional_source_and_invalid_input_stay_bounded()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h); var first = await SeedShortage(h, linked: false);
        var second = await SeedShortage(h, linked: false, suffix: "2"); await SeedShortage(h, suffix: "3"); var service = Shortages(h, owner);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 408 && a.ActionCode == "query").ExecuteDeleteAsync();
        await h.Db.MaterialShortages.ExecuteUpdateAsync(s => s.SetProperty(x => x.DetectedAt, new DateTime(2026, 10, 23, 0, 0, 0)));
        var page1 = await service.SearchOwnerAsync(new() { PageSize = 1 }); var page2 = await service.SearchOwnerAsync(new() { Page = 2, PageSize = 1 });
        Assert.Equal(2, page1.Total); Assert.Equal(2, page1.OpenCount); Assert.Equal(2, page2.Total);
        Assert.Equal(2, page1.Rows.Concat(page2.Rows).Select(x => x.Id).Distinct().Count());
        var item = Assert.Single(page1.Rows); await service.ResolveOwnerAsync(item.Id, new() { RowVersion = item.RowVersion });
        var resolved = await service.SearchOwnerAsync(new() { Status = MaterialShortageStatus.Resolved });
        Assert.Equal(1, resolved.Total); Assert.Equal(1, resolved.OpenCount); Assert.Equal(item.Id, Assert.Single(resolved.Rows).Id);
        var filtered = await service.SearchOwnerAsync(new() { WorkOrderNo = item.WorkOrderNo }); Assert.Equal(1, filtered.Total); Assert.Equal(0, filtered.OpenCount);
        foreach (var query in new MaterialShortageQuery[] { new() { Page = 0 }, new() { PageSize = 501 }, new() { Page = int.MaxValue }, new() { Status = "unknown" } })
            Assert.Equal("SHORTAGE_QUERY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(query))).Message);
        var open = Assert.Single((await service.SearchOwnerAsync(new() { Status = MaterialShortageStatus.Open })).Rows);
        Assert.Equal("SHORTAGE_COMMAND_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.DismissOwnerAsync(open.Id,
            new() { RowVersion = open.RowVersion, Remark = "implicit mutation" }))).Message);
        Assert.Equal("SHORTAGE_COMMAND_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.DismissOwnerAsync(open.Id,
            new() { RowVersion = open.RowVersion, SetRemark = true, Remark = new string('x', 501) }))).Message);
        Assert.Equal(1, (await service.SearchOwnerAsync(new())).OpenCount);
    }

    [CurrentPermissionPostgresFact]
    public async Task Material_shortage_owner_catalog_migration_registers_without_granting_query()
    {
        await using var h = await Seed(); var owner = await GrantShortage(h);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 417 && a.ActionCode == "query").ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(a => a.MenuId == 417 && a.ActionCode == "query").ExecuteDeleteAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.MaterialShortageOwnerV1();
        var sql = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        await h.Db.Database.ExecuteSqlRawAsync(sql); await h.Db.Database.ExecuteSqlRawAsync(sql);
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(a => a.MenuId == 417 && a.ActionCode == "query"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(a => a.MenuId == 417 && a.ActionCode == "query"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Shortages(h, owner).SearchOwnerAsync(new()))).Message);
    }
}
