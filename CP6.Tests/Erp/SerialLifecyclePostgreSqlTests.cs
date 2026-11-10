using System.Net;
using System.Net.Http.Json;
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
    private async Task<Harness> SerialLifecycleSeed(string operation)
    {
        var h = await Seed(); var db = h.Db;
        await GrantMobile(h);
        foreach (var (menu, key, action) in new[] { (461, "wms-mobile", "serial-manage"), (403, "wms-stock", "adjust"), (403, "wms-stock", "move") })
        {
            if (!await db.Sys_Menus.AnyAsync(m => m.MenuId == menu)) db.Sys_Menus.Add(new() { MenuId = menu, MenuKey = key, MenuName = key });
            if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu && a.ActionCode == action))
                db.Sys_MenuActions.Add(new() { MenuId = menu, ActionCode = action, ActionName = action });
            db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu, ActionCode = action });
            // The two Stock actions share one new menu in this fixture.
            if (menu == 403) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        }
        db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-stock", ScopeType = 5 });
        db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 1 });
        db.Warehouses.Add(new() { WarehouseCd = "W1", WarehouseName = "Simulated serial lifecycle" });
        db.WmsFeatureFlags.Add(new() { WarehouseCd = "W1", SerialLpnEnabled = true });
        db.Locations.AddRange(new() { WarehouseCd = "W1", LocationCd = "SRC", AreaCd = "SOURCE" },
            new() { WarehouseCd = "W1", LocationCd = "DST", AreaCd = "DESTINATION" });
        foreach (var product in new[] { SerialProduct(h, "P"), SerialProduct(h, "Q") })
        {
            db.ProductMasters.Add(new() { ProductCd = product, ItemCd = product,
                TrackingMode = operation == "enable" ? ProductTrackingMode.None : ProductTrackingMode.Serial });
            foreach (var location in new[] { "SRC", "DST" })
                db.Stocks.Add(new() { ProductCd = product, WarehouseCd = "W1", LocationCd = location, LotNo = "LOT", UnitCd = "EA",
                    UnitPrice = 12.3456m, PhysicalQty = location == "SRC" && operation is not ("RECEIVE" or "RETURN") ? 2m : 0m,
                    AvailableQty = location == "SRC" && operation is not ("RECEIVE" or "RETURN") ? 2m : 0m, Creator = "price-owner" });
            if (operation is not ("enable" or "RECEIVE"))
                foreach (var serial in new[] { "S1", "S2" })
                    db.StockSerials.Add(new() { ProductCd = product, SerialNo = serial, WarehouseCd = "W1", LocationCd = "SRC", LotNo = "LOT",
                        Status = operation == "RETURN" ? StockSerialStatus.Shipped : operation == "SHIP" ? StockSerialStatus.Picked : StockSerialStatus.InStock,
                        Creator = "price-owner" });
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var mapping = await MappingAdmin(h);
        foreach (var stock in await db.Stocks.AsNoTracking().ToArrayAsync())
            await BindStandalone(h, mapping, "stock", stock.Id, stock.LocationCd == "SRC" ? mapping.Child : mapping.Other);
        return h;
    }

    [TenantManagementPostgresTheory]
    [InlineData("enable")]
    [InlineData("RECEIVE")]
    [InlineData("PUTAWAY")]
    [InlineData("MOVE")]
    [InlineData("PICK")]
    [InlineData("SHIP")]
    [InlineData("COUNT")]
    [InlineData("RETURN")]
    public async Task Serial_writes_compose_stock_and_current_tenant_authority(string operation)
    {
        await using var h = await SerialLifecycleSeed(operation);
        await using var administrator = await TenantManagementSeed();
        var db = h.Db; var tenant = h.Tenant; var connection = db.Database.GetConnectionString()!;
        var normalProduct = SerialProduct(h, "P"); var pendingProduct = SerialProduct(h, "Q");
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, h); var manager = await Login(host, administrator);
        var normalKey = Guid.NewGuid(); var pendingKey = Guid.NewGuid();
        var prior = await SerialWriteSet(h);
        using var normal = await Post(normalProduct, normalKey);
        await DeviceEvidence("serial-" + operation + "-normal.json", new { Operation = operation, Status = (int)normal.StatusCode,
            Body = await normal.Content.ReadAsStringAsync(), Before = prior, After = await SerialWriteSet(h) });
        Assert.True(normal.StatusCode == HttpStatusCode.OK, await OperationsFailure(normal));
        var original = await normal.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SerialOperationResult>(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(normalKey, result.OperationId); Assert.Equal(2, result.SerialCount);
        Assert.All(result.Serials, s => Assert.Equal(operation is "MOVE" or "PUTAWAY" or "RECEIVE" or "RETURN" ? "DST" : "SRC", s.LocationCd));
        var state = operation == "SHIP" ? StockSerialStatus.Shipped : operation == "PICK" ? StockSerialStatus.Picked
            : operation == "RETURN" ? StockSerialStatus.Returned : StockSerialStatus.InStock;
        Assert.All(result.Serials, s => Assert.Equal(state, s.Status));
        Assert.Equal(2, await db.StockSerialTransactions.CountAsync(s => s.OperationId == normalKey && s.OperatorCd == "price-owner"));
        var legs = operation is "MOVE" or "PUTAWAY" ? 2 : operation is "RECEIVE" or "RETURN" or "SHIP" ? 1 : 0;
        Assert.Equal(legs, result.StockTxnNos.Count);
        var stockRows = await db.StockTransactions.AsNoTracking().Where(t => t.ProductCd == normalProduct).ToArrayAsync();
        Assert.Equal(legs, stockRows.Length); Assert.Equal(result.StockTxnNos.Order(), stockRows.Select(t => t.TxnNo).Order());
        Assert.All(stockRows, s => Assert.Equal("price-owner", s.OperatorCd));
        if (legs != 0)
            Assert.Equal(legs, await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM public."T_StockTransaction" t
                JOIN public."T_Stock" s ON s."TenantId"=t."TenantId" AND s."WarehouseCd"=t."WarehouseCd"
                  AND s."LocationCd"=t."LocationCd" AND s."ProductCd"=t."ProductCd" AND s."LotNo"=t."LotNo"
                JOIN cp6_operations_owner_v1.department_mappings a ON a.tenant_id=t."TenantId" AND a.object_kind='stock' AND a.object_id=s."Id"
                JOIN cp6_operations_owner_v1.department_mappings b ON b.tenant_id=t."TenantId" AND b.object_kind='stock-transaction' AND b.object_id=t."Id" AND b.department_id=a.department_id
                WHERE t."TenantId"={tenant} AND t."ProductCd"={normalProduct}
                """).SingleAsync());
        Assert.Equal(operation is "RECEIVE" or "RETURN" or "MOVE" or "PUTAWAY" ? 2m : operation == "SHIP" ? 0m : 2m,
            await db.Stocks.Where(s => s.ProductCd == normalProduct).SumAsync(s => s.PhysicalQty));
        var beforeReplay = await SerialWriteSet(h);
        using var replay = await Post(normalProduct, normalKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Equal(beforeReplay, await SerialWriteSet(h));

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business = new NpgsqlConnection(connection); await business.OpenAsync();
        await using var target = new NpgsqlConnection(connection); await target.OpenAsync();
        await using var observer = new NpgsqlConnection(connection); await observer.OpenAsync();
        FenceWait pauseWait, serialWait; Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended; var before = await SerialWriteSet(h);
        await using (var businessLock = await business.BeginTransactionAsync())
        {
            await using var lockBusiness = new NpgsqlCommand("LOCK TABLE public.\"T_ProductMaster\" IN ACCESS EXCLUSIVE MODE", business, businessLock);
            await lockBusiness.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock = await target.BeginTransactionAsync();
                await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", target, tenantLock);
                lockTenant.Parameters.AddWithValue("tenant", tenant); Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("serial-" + operation, active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, target.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = Post(pendingProduct, pendingKey);
                    serialWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID, operation, pending);
                }
                finally { await tenantLock.RollbackAsync(); }
                using var paused = await pause; suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await businessLock.RollbackAsync(); }
        }
        using var denied = await pending;
        await DeviceEvidence("serial-" + operation + "-pause.json", new { Operation = operation, Tenant = tenant, pauseWait, serialWait,
            suspended.CommittedAt, Status = (int)denied.StatusCode, AuthorityWait = serialWait.Blocker == pauseWait.Backend,
            Before = before, After = await SerialWriteSet(h) });
        Assert.True(denied.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(denied));
        Assert.Equal(pauseWait.Backend, serialWait.Blocker); Assert.Equal(before, await SerialWriteSet(h));
        using var deniedReplay = await Post(normalProduct, normalKey); Assert.Equal(HttpStatusCode.Unauthorized, deniedReplay.StatusCode);
        Assert.Equal(before, await SerialWriteSet(h));
        using var reactivated = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
            new TenantManagementCommandV1("serial-reactivate-" + operation, suspended.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);
        writer = await Login(host, h);

        await db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "NONE")); writer = await Login(host, h);
        await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Reject(() => Post(normalProduct, normalKey), HttpStatusCode.Forbidden, "FORBIDDEN");
        await db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "")); writer = await Login(host, h);
        using var restored = await Post(normalProduct, normalKey);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode); Assert.Equal(original, await restored.Content.ReadAsStringAsync());
        if (operation is "MOVE" or "PUTAWAY" or "RETURN")
        {
            foreach (var area in new[] { "SOURCE", "DESTINATION" })
            {
                await db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, area)); writer = await Login(host, h);
                await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Forbidden, "FORBIDDEN");
            }
            await db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "")); writer = await Login(host, h);
        }
        if (operation == "MOVE")
        {
            await db.Locations.Where(l => l.LocationCd == "DST").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsBlocked, true));
            await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Conflict, "LOCATION_OPERATION_BLOCKED");
            await db.Locations.Where(l => l.LocationCd == "DST").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsBlocked, false));
        }
        if (operation is "enable" or "MOVE")
        {
            await using var fault = new NpgsqlConnection(connection); await fault.OpenAsync();
            await using var install = new NpgsqlCommand($$"""
                CREATE FUNCTION public.cp6_test_serial_receipt() RETURNS trigger LANGUAGE plpgsql AS $fault$
                BEGIN RAISE EXCEPTION 'simulated serial receipt failure'; END $fault$;
                CREATE TRIGGER cp6_test_serial_receipt BEFORE INSERT ON public."T_TaskCommandReceipt"
                FOR EACH ROW WHEN (NEW."OperationId"='{{pendingKey}}'::uuid) EXECUTE FUNCTION public.cp6_test_serial_receipt();
                """, fault);
            await install.ExecuteNonQueryAsync();
            try { await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.ServiceUnavailable, "AUTHORITY_FENCE_UNAVAILABLE"); }
            finally
            {
                await using var remove = new NpgsqlCommand("DROP TRIGGER cp6_test_serial_receipt ON public.\"T_TaskCommandReceipt\"; DROP FUNCTION public.cp6_test_serial_receipt()", fault);
                await remove.ExecuteNonQueryAsync();
            }
        }
        if (operation is "RECEIVE" or "MOVE")
        {
            var stockAction = operation == "MOVE" ? "move" : "adjust";
            await db.Sys_RoleActions.Where(a => a.MenuId == 403 && a.ActionCode == stockAction).ExecuteDeleteAsync(); writer = await Login(host, h);
            await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Forbidden, "ACTION_FORBIDDEN");
            db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 403, ActionCode = stockAction }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            writer = await Login(host, h);
        }
        if (operation == "RECEIVE")
        {
            await db.Sys_RoleFieldPerms.Where(p => p.ResourceKey == "wms-stock" && p.FieldName == "UnitPrice").ExecuteUpdateAsync(s => s.SetProperty(p => p.Access, 2));
            writer = await Login(host, h); await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Forbidden, "FIELD_WRITE_FORBIDDEN");
            await db.Sys_RoleFieldPerms.Where(p => p.ResourceKey == "wms-stock" && p.FieldName == "UnitPrice").ExecuteUpdateAsync(s => s.SetProperty(p => p.Access, 1)); writer = await Login(host, h);
        }
        if (operation is "enable" or "PICK" or "COUNT" or "MOVE")
        {
            await db.Sys_RoleFieldPerms.Where(p => p.ResourceKey == "wms-stock" && p.FieldName == "UnitPrice").ExecuteUpdateAsync(s => s.SetProperty(p => p.Access, 2));
            writer = await Login(host, h);
        }
        if (operation == "enable")
        {
            await db.Stocks.Where(s => s.ProductCd == pendingProduct && s.LocationCd == "SRC")
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.PhysicalQty, 1.5m).SetProperty(s => s.AvailableQty, 1.5m));
            await Reject(() => Post(pendingProduct, pendingKey), HttpStatusCode.Conflict, "WM-SERIAL-STOCK-NOT-WHOLE");
            await db.Stocks.Where(s => s.ProductCd == pendingProduct && s.LocationCd == "SRC")
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.PhysicalQty, 2m).SetProperty(s => s.AvailableQty, 2m));
        }
        using var recovered = await Post(pendingProduct, pendingKey);
        Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
        if (operation == "enable")
        {
            await Reject(() => Post(normalProduct, Guid.NewGuid()), HttpStatusCode.Conflict, "WM-SERIAL-TRACKING-LOCKED");
            db.ProductMasters.Add(new() { ProductCd = SerialProduct(h, "E"), ItemCd = SerialProduct(h, "E") }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            using var empty = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/v2/wms/serials/enable-tracking", new EnableSerialTrackingRequest
                { OperationId = Guid.NewGuid(), ProductCd = SerialProduct(h, "E") }));
            Assert.True(empty.StatusCode == HttpStatusCode.OK, await OperationsFailure(empty));
            Assert.Empty((await empty.Content.ReadFromJsonAsync<SerialOperationResult>())!.StockTxnNos);
            Assert.False(await db.Stocks.AnyAsync(s => s.ProductCd == SerialProduct(h, "E")));
        }
        else if (operation == "RECEIVE")
            await Reject(() => Post(normalProduct, Guid.NewGuid()), HttpStatusCode.Conflict, "WM-SERIAL-ALREADY-EXISTS");

        Task<HttpResponseMessage> Post(string product, Guid operationId) => host.Send(OperationsRequest(writer, HttpMethod.Post,
            "api/v2/wms/serials" + (operation == "enable" ? "/enable-tracking" : ""),
            operation == "enable" ? new EnableSerialTrackingRequest { OperationId = operationId, ProductCd = product,
                ExistingSerials = [new() { SerialNo = "S1", WarehouseCd = "W1", LocationCd = "SRC", LotNo = "LOT" }, new() { SerialNo = "S2", WarehouseCd = "W1", LocationCd = "SRC", LotNo = "LOT" }] }
            : new SerialLifecycleRequest { OperationId = operationId, ProductCd = product, TxnType = operation, WarehouseCd = "W1", LotNo = "LOT",
                SerialNos = ["S1", "S2"], FromLocationCd = operation is "RECEIVE" or "RETURN" ? null : "SRC",
                ToLocationCd = operation is "RECEIVE" or "RETURN" or "MOVE" or "PUTAWAY" ? "DST" : null, DeviceId = "simulated-scanner" }));
        async Task Reject(Func<Task<HttpResponseMessage>> action, HttpStatusCode status, string code)
        {
            var unchanged = await SerialWriteSet(h); using var response = await action();
            Assert.True(response.StatusCode == status, await OperationsFailure(response)); Assert.Contains(code, await response.Content.ReadAsStringAsync());
            Assert.Equal(unchanged, await SerialWriteSet(h));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Serial_complete_legacy_cycle_keeps_stock_and_serial_results_together()
    {
        await using var h = await SerialLifecycleSeed("RECEIVE");
        var product = SerialProduct(h, "P");
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitPrice, 0m));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var writer = await Login(host, h);
        var results = new List<SerialOperationResult>();
        SerialLifecycleRequest? last = null; string? original = null;
        foreach (var (type, from, to) in new (string, string?, string?)[] { ("RECEIVE", null, "SRC"), ("PUTAWAY", "SRC", "DST"),
                     ("PICK", "DST", null), ("MOVE", "DST", "SRC"), ("COUNT", "SRC", null), ("SHIP", "SRC", null), ("RETURN", null, "DST") })
        {
            last = new() { OperationId = Guid.NewGuid(), ProductCd = product, TxnType = type, WarehouseCd = "W1", LotNo = "LOT",
                SerialNos = ["S1", "S2"], FromLocationCd = from, ToLocationCd = to, DeviceId = "simulated-scanner" };
            using var posted = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/v2/wms/serials", last));
            Assert.True(posted.StatusCode == HttpStatusCode.OK, await OperationsFailure(posted));
            original = await posted.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SerialOperationResult>(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(type, result.TxnType); Assert.Equal(2, result.SerialCount); results.Add(result);
            Assert.Equal(type == "SHIP" ? 0m : 2m, await h.Db.Stocks.Where(s => s.ProductCd == product).SumAsync(s => s.PhysicalQty));
            foreach (var location in new[] { "SRC", "DST" })
                Assert.Equal(await h.Db.StockSerials.CountAsync(s => s.ProductCd == product && s.LocationCd == location && s.Status != StockSerialStatus.Shipped),
                    await h.Db.Stocks.Where(s => s.ProductCd == product && s.LocationCd == location).SumAsync(s => s.PhysicalQty));
        }
        Assert.Equal(7, results.Sum(r => r.StockTxnNos.Count));
        Assert.Equal(7, await h.Db.StockTransactions.CountAsync(t => t.ProductCd == product));
        Assert.Equal(14, await h.Db.StockSerialTransactions.CountAsync(t => t.ProductCd == product));
        Assert.Equal(7, await h.Db.TaskCommandReceipts.CountAsync(r => r.TaskNo == "SERIAL:" + product));
        var before = await SerialWriteSet(h);
        using var replay = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/v2/wms/serials", last));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await SerialWriteSet(h));
        await DeviceEvidence("serial-cycle.json", new { Results = results, StockTransactions = 7, SerialTransactions = 14, Receipts = 7 });
    }

    private static string SerialProduct(Harness h, string suffix) => "S" + h.Tenant.ToString("N")[..12] + suffix;

    private static async Task<FenceWait> SerialNativeWait(NpgsqlConnection observer, int pause, int business, string operation, Task<HttpResponseMessage> pending)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            await using var query = new NpgsqlCommand("""
                SELECT a.pid,b.blocker,a.usename,a.wait_event,clock_timestamp(),a.query FROM pg_stat_activity a
                CROSS JOIN LATERAL unnest(pg_blocking_pids(a.pid)) b(blocker)
                WHERE a.datname=current_database() AND a.wait_event_type='Lock'
                  AND b.blocker IN (@pause,@business) AND a.pid NOT IN (@pause,@business) ORDER BY a.pid LIMIT 1
                """, observer);
            query.Parameters.AddWithValue("pause", pause); query.Parameters.AddWithValue("business", business);
            await using (var rows = await query.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                {
                    var wait = new FenceWait(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
                    await DeviceEvidence("serial-" + operation + "-wait.json", new { Wait = wait, Query = rows.GetString(5) });
                    return wait;
                }
            if (pending.IsCompleted) Assert.Fail("Serial request completed before its controlled native wait: " + await OperationsFailure(await pending));
            await Task.Delay(35, deadline.Token);
        }
    }

    private static Task<string> SerialWriteSet(Harness h) => h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object(
          'Products',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p."Id"),'[]') FROM public."T_ProductMaster" p WHERE p."TenantId"={h.Tenant}),
          'Serials',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockSerial" s WHERE s."TenantId"={h.Tenant}),
          'SerialTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockSerialTransaction" s WHERE s."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockTransaction" s WHERE s."TenantId"={h.Tenant}),
          'Receipts',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_TaskCommandReceipt" s WHERE s."TenantId"={h.Tenant}),
          'Sequences',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsSequence" s WHERE s."TenantId"={h.Tenant}),
          'MovementSequences',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s.movement_id),'[]') FROM cp6_stock_return_v1.movement_sequences s WHERE s.tenant_id={h.Tenant}),
          'InheritedMappings',(SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY m.id),'[]') FROM cp6_operations_owner_v1.department_mappings m WHERE m.tenant_id={h.Tenant} AND m.object_kind='stock-transaction'),
          'InheritanceAudits',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Sys_SecurityLogs" s WHERE s."TenantId"={h.Tenant} AND s."Reason"='New stock transaction inherited its actual Stock department')
        )::text AS "Value"
        """).SingleAsync();
}
