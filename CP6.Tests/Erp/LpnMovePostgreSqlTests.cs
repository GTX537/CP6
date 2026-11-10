using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lpn_move_composes_stock_and_current_tenant_fence(bool withContents)
    {
        await using var h = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        var tenant = h.Tenant;
        var suffix = withContents ? "stock" : "empty";
        var output = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!;
        var product = "LM" + tenant.ToString("N")[..10];
        var serialProduct = product + "S";
        await GrantMobile(h);
        foreach (var (id, key, action) in new[] { (461, "wms-mobile", "lpn-manage"), (403, "wms-stock", "move") })
        {
            if (!await db.Sys_Menus.AnyAsync(m => m.MenuId == id)) db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action))
                db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
            db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
        }
        db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-stock", ScopeType = 5 });
        db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 3 });
        db.Warehouses.Add(new() { WarehouseCd = "W1", WarehouseName = "LPN simulated movement" });
        db.WmsFeatureFlags.Add(new() { WarehouseCd = "W1", SerialLpnEnabled = true });
        db.Locations.AddRange(new() { WarehouseCd = "W1", LocationCd = "SRC", AreaCd = "SOURCE" },
            new() { WarehouseCd = "W1", LocationCd = "DST", AreaCd = "DESTINATION" });
        if (withContents)
        {
            db.ProductMasters.AddRange(new() { ProductCd = product, ItemCd = product },
                new() { ProductCd = serialProduct, ItemCd = serialProduct, TrackingMode = ProductTrackingMode.Serial });
            foreach (var code in new[] { product, serialProduct })
                foreach (var location in new[] { "SRC", "DST" })
                {
                    var stock = CustodyStock(location);
                    stock.WarehouseCd = "W1";
                    stock.ProductCd = code;
                    stock.PhysicalQty = stock.AvailableQty = location == "DST" ? 0m : code == product ? 10.12345678m : 4m;
                    db.Stocks.Add(stock);
                }
            foreach (var serial in new[] { "SER1", "SER2" })
                db.StockSerials.Add(new() { ProductCd = serialProduct, SerialNo = serial, WarehouseCd = "W1", LocationCd = "SRC", LotNo = "LOT" });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        if (withContents)
        {
            var mapping = await MappingAdmin(h);
            foreach (var stock in await db.Stocks.AsNoTracking().ToArrayAsync())
                await BindStandalone(h, mapping, "stock", stock.Id, stock.LocationCd == "SRC" ? mapping.Child : mapping.Other);
        }
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, h);
        var manager = await Login(host, administrator);
        using (var policy = await Post("api/v2/wms/lpns/policies", new LpnPolicyRequest
            { WarehouseCd = "W1", ContainerType = "BOX", AllowMixedProducts = true, AllowMixedLots = true }))
            Assert.True(policy.StatusCode == HttpStatusCode.OK, await OperationsFailure(policy));
        foreach (var number in new[] { "A1", "B1", "A2", "B2" })
        {
            using var created = await Post("api/v2/wms/lpns", new CreateLpnRequest
                { OperationId = Guid.NewGuid(), LpnNo = number, ContainerType = "BOX", WarehouseCd = "W1", LocationCd = "SRC" });
            Assert.True(created.StatusCode == HttpStatusCode.OK, await OperationsFailure(created));
        }
        foreach (var index in new[] { 1, 2 })
        {
            using var root = await Post("api/v2/wms/lpns/A" + index + "/pack", new PackLpnRequest
            {
                OperationId = Guid.NewGuid(), RowVersion = await Version("A" + index), ChildLpns = ["B" + index],
                Contents = withContents ? [new() { ProductCd = product, LotNo = "LOT", Qty = 1.12345678m }] : []
            });
            Assert.True(root.StatusCode == HttpStatusCode.OK, await OperationsFailure(root));
            if (withContents)
            {
                using var child = await Post("api/v2/wms/lpns/B" + index + "/pack", new PackLpnRequest
                {
                    OperationId = Guid.NewGuid(), RowVersion = await Version("B" + index),
                    Contents = [new() { ProductCd = serialProduct, LotNo = "LOT", SerialNo = "SER" + index, Qty = 1m }]
                });
                Assert.True(child.StatusCode == HttpStatusCode.OK, await OperationsFailure(child));
            }
        }
        var initialBefore = await WriteSet();
        var original = await Command("A1");
        using var completed = await Move("A1", original);
        var originalReply = await completed.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(Path.Combine(output, "lpn-move-" + suffix + "-initial.json"), JsonSerializer.Serialize(new
            { Status = (int)completed.StatusCode, Body = originalReply, Before = initialBefore, After = await WriteSet() }));
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await OperationsFailure(completed));
        Assert.All(await db.LogisticsUnits.AsNoTracking().Where(u => u.LpnNo == "A1" || u.LpnNo == "B1").ToArrayAsync(), u => Assert.Equal("DST", u.LocationCd));
        Assert.Equal("A1", await db.LogisticsUnits.Where(u => u.LpnNo == "B1").Select(u => u.ParentLpnNo).SingleAsync());
        Assert.True(await db.LpnClosures.AnyAsync(c => c.AncestorLpnNo == "A1" && c.DescendantLpnNo == "B1" && c.Depth == 1));
        if (withContents)
        {
            var stocks = await db.Stocks.AsNoTracking().ToArrayAsync();
            Assert.All(stocks, AssertMoveCustody);
            Assert.Equal(9m, stocks.Single(s => s.ProductCd == product && s.LocationCd == "SRC").PhysicalQty);
            Assert.Equal(1.12345678m, stocks.Single(s => s.ProductCd == product && s.LocationCd == "DST").PhysicalQty);
            Assert.Equal(10.12345678m, stocks.Where(s => s.ProductCd == product).Sum(s => s.PhysicalQty));
            Assert.Equal(4m, stocks.Where(s => s.ProductCd == serialProduct).Sum(s => s.PhysicalQty));
            Assert.Equal("DST", await db.StockSerials.Where(s => s.SerialNo == "SER1").Select(s => s.LocationCd).SingleAsync());
            Assert.Equal("SRC", await db.StockSerials.Where(s => s.SerialNo == "SER2").Select(s => s.LocationCd).SingleAsync());
            var legs = await db.StockTransactions.AsNoTracking().ToArrayAsync();
            Assert.Equal(4, legs.Length);
            Assert.All(legs, t => { Assert.Equal("price-owner", t.OperatorCd); Assert.Equal("price-owner", t.Creator);
                Assert.Equal("CUSTOMER", t.OwnerType); Assert.Equal("CUST1", t.OwnerCd); Assert.Equal("BOX", t.UnitCd); Assert.Equal(123.4567m, t.UnitPrice); });
            foreach (var pair in legs.GroupBy(t => t.ProductCd))
            {
                Assert.Equal(0m, pair.Sum(t => t.Qty));
                Assert.Equal(pair.Single(t => t.Qty < 0).TxnNo, pair.Single(t => t.Qty > 0).RelatedNo);
            }
            Assert.Equal(4, await InheritedMappings(h));
            Assert.Equal(4, await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings m
                JOIN public."T_StockTransaction" t ON t."Id"=m.object_id AND t."TenantId"=m.tenant_id
                JOIN public."T_Stock" s ON s."TenantId"=t."TenantId" AND s."WarehouseCd"=t."WarehouseCd"
                  AND s."LocationCd"=t."LocationCd" AND s."ProductCd"=t."ProductCd" AND s."LotNo"=t."LotNo"
                JOIN cp6_operations_owner_v1.department_mappings source ON source.tenant_id=s."TenantId" AND source.object_id=s."Id"
                  AND source.object_kind='stock' AND source.department_id=m.department_id AND source.enabled
                WHERE m.tenant_id={tenant} AND m.object_kind='stock-transaction' AND m.enabled
                """).SingleAsync());
            Assert.Single(await db.StockSerialTransactions.ToArrayAsync());
        }
        var before = await WriteSet();
        using (var replay = await Move("A1", original))
        { Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay)); Assert.Equal(originalReply, await replay.Content.ReadAsStringAsync()); }
        Assert.Equal(before, await WriteSet());
        var stale = await Command("A2"); stale.RowVersion = Convert.ToBase64String(new byte[8]);
        await Rejected(stale, HttpStatusCode.Conflict, "WM-CONFLICT-ROW-VERSION");
        var missing = await Command("A2"); missing.ToLocationCd = "MISSING";
        await Rejected(missing, HttpStatusCode.BadRequest, "WM-V2-LOCATION-NOT-FOUND");
        await db.Locations.Where(l => l.LocationCd == "DST").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsBlocked, true));
        await Rejected(await Command("A2"), HttpStatusCode.Conflict, "WM-V2-LOCATION-BLOCKED");
        await db.Locations.Where(l => l.LocationCd == "DST").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsBlocked, false));
        Assert.Equal(before, await WriteSet());
        var command = await Command("A2");
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var businessBlocker = new NpgsqlConnection(connection);
        await using var tenantBlocker = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await businessBlocker.OpenAsync(); await tenantBlocker.OpenAsync(); await observer.OpenAsync();
        FenceWait pauseWait, commandWait;
        Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended;
        await using (var businessLock = await businessBlocker.BeginTransactionAsync())
        {
            await using var units = new NpgsqlCommand("""LOCK TABLE public."T_LogisticsUnit" IN ACCESS EXCLUSIVE MODE""", businessBlocker, businessLock);
            await units.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock = await tenantBlocker.BeginTransactionAsync();
                await using var tenantRow = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", tenantBlocker, tenantLock);
                tenantRow.Parameters.AddWithValue("tenant", tenant); Assert.Equal(1, await tenantRow.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("lpn-move-" + suffix, active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = Move("A2", command);
                    commandWait = await ObserveLpnCreateWait(observer, pauseWait.Backend, businessBlocker.ProcessID, pending);
                }
                finally { await tenantLock.RollbackAsync(); }
                using var paused = await pause; suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await businessLock.RollbackAsync(); }
        }
        using var response = await pending;
        var after = await WriteSet();
        await File.WriteAllTextAsync(Path.Combine(output, "lpn-move-" + suffix + "-pause.json"), JsonSerializer.Serialize(new
        { Tenant = tenant, pauseWait, commandWait, suspended.Snapshot.Revision, suspended.CommittedAt,
            AuthorityWait = commandWait.Blocker == pauseWait.Backend, Status = (int)response.StatusCode, Before = before, After = after }));
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.Contains("REAUTH_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(pauseWait.Backend, commandWait.Blocker); Assert.Equal(before, after);
        using var deniedReplay = await Move("A1", original);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedReplay.StatusCode); Assert.Equal(before, await WriteSet());

        using var reactivated = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
            new TenantManagementCommandV1("lpn-move-reactivate-" + suffix, suspended.Snapshot.Revision, "reactivate", null)));
        var resumed = await NumberHttpData<TenantManagementReceiptV1>(reactivated);
        Assert.True(resumed.Snapshot.Enable);
        writer = await Login(host, h);
        foreach (var area in new[] { "SOURCE", "DESTINATION" })
        {
            await db.WmsRoleScopes.Where(s => s.RoleId == 72).ExecuteUpdateAsync(s => s.SetProperty(g => g.AreaCd, area));
            writer = await Login(host, h);
            await Rejected(await Command("A2"), HttpStatusCode.Forbidden, "OBJECT_FORBIDDEN");
            using var scopedReplay = await Move("A1", original);
            using var sameLocation = await Move("A1", await Command("A1"));
            Assert.Equal(area == "SOURCE" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, scopedReplay.StatusCode);
            Assert.Equal(area == "SOURCE" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, sameLocation.StatusCode);
            if (area == "DESTINATION") Assert.Equal(originalReply, await scopedReplay.Content.ReadAsStringAsync());
        }
        await db.WmsRoleScopes.Where(s => s.RoleId == 72).ExecuteUpdateAsync(s => s.SetProperty(g => g.AreaCd, ""));
        writer = await Login(host, h);
        if (withContents)
        {
            await db.Sys_RoleActions.Where(a => a.MenuId == 403 && a.ActionCode == "move").ExecuteDeleteAsync();
            writer = await Login(host, h);
            await Rejected(await Command("A2"), HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
            db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 403, ActionCode = "move" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            writer = await Login(host, h);
            var late = await Command("A2");
            await using var control = new NpgsqlConnection(connection); await control.OpenAsync();
            await using var install = new NpgsqlCommand($$"""
                CREATE FUNCTION public.cp6_test_lpn_move_abort() RETURNS trigger LANGUAGE plpgsql AS $failure$
                BEGIN RAISE EXCEPTION 'simulated late LPN receipt failure'; END $failure$;
                CREATE TRIGGER cp6_test_lpn_move_abort BEFORE INSERT ON public."T_TaskCommandReceipt"
                FOR EACH ROW WHEN (NEW."OperationId"='{{late.OperationId}}'::uuid) EXECUTE FUNCTION public.cp6_test_lpn_move_abort();
                """, control);
            await install.ExecuteNonQueryAsync();
            try { await Rejected(late, HttpStatusCode.ServiceUnavailable, "AUTHORITY_FENCE_UNAVAILABLE"); }
            finally
            {
                await using var remove = new NpgsqlCommand("DROP TRIGGER cp6_test_lpn_move_abort ON public.\"T_TaskCommandReceipt\"; DROP FUNCTION public.cp6_test_lpn_move_abort()", control);
                await remove.ExecuteNonQueryAsync();
            }
        }
        Assert.Equal(before, await WriteSet());

        Task<HttpResponseMessage> Post(string path, object body) => host.Send(OperationsRequest(writer, HttpMethod.Post, path, body));
        Task<HttpResponseMessage> Move(string number, MoveLpnRequest body) => Post("api/v2/wms/lpns/" + number + "/move", body);
        async Task<MoveLpnRequest> Command(string number) => new()
            { OperationId = Guid.NewGuid(), RowVersion = await Version(number), ToLocationCd = "DST" };
        async Task<string> Version(string number)
        {
            using var response = await host.Send(OperationsRequest(writer, HttpMethod.Get, "api/v2/wms/lpns/" + number));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("rowVersion").GetString()!;
        }
        async Task Rejected(MoveLpnRequest body, HttpStatusCode status, string code)
        {
            using var response = await Move("A2", body);
            Assert.True(response.StatusCode == status, await OperationsFailure(response));
            Assert.Contains(code, await response.Content.ReadAsStringAsync());
        }
        Task<string> WriteSet() => LpnMoveWriteSet(db, tenant);
    }

    private static Task<string> LpnMoveWriteSet(CP6.Core.EFDbContext.CP6Context db, Guid tenant) => db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object(
          'Units',(SELECT coalesce(jsonb_agg(to_jsonb(u) ORDER BY u."Id"),'[]') FROM public."T_LogisticsUnit" u WHERE u."TenantId"={tenant}),
          'Closures',(SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c."Id"),'[]') FROM public."T_LpnClosure" c WHERE c."TenantId"={tenant}),
          'Contents',(SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c."Id"),'[]') FROM public."T_LpnContent" c WHERE c."TenantId"={tenant}),
          'Serials',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockSerial" s WHERE s."TenantId"={tenant}),
          'Events',(SELECT coalesce(jsonb_agg(to_jsonb(e) ORDER BY e."Id"),'[]') FROM public."T_LpnEvent" e WHERE e."TenantId"={tenant}),
          'Receipts',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."T_TaskCommandReceipt" r WHERE r."TenantId"={tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={tenant}),
          'SerialTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockSerialTransaction" t WHERE t."TenantId"={tenant}),
          'Sequences',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsSequence" s WHERE s."TenantId"={tenant}),
          'MovementSequences',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s.movement_id),'[]') FROM cp6_stock_return_v1.movement_sequences s WHERE s.tenant_id={tenant}),
          'InheritedMappings',(SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY m.id),'[]') FROM cp6_operations_owner_v1.department_mappings m WHERE m.tenant_id={tenant} AND m.object_kind='stock-transaction'),
          'InheritanceAudits',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Sys_SecurityLogs" s WHERE s."TenantId"={tenant} AND s."Reason"='New stock transaction inherited its actual Stock department')
        )::text AS "Value"
        """).SingleAsync();
}
