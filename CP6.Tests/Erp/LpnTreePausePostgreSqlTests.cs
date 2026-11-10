using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("pack")]
    [InlineData("unpack")]
    [InlineData("split")]
    [InlineData("merge")]
    public async Task Lpn_tree_commands_keep_original_state_after_tenant_pause(string operation)
    {
        await using var tenantUser = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = tenantUser.Db;
        var tenant = tenantUser.Tenant;
        var product = "LP" + tenant.ToString("N")[..10];
        var serialized = product + "S";
        await GrantMobile(tenantUser);
        if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == 461 && a.ActionCode == "lpn-manage"))
            db.Sys_MenuActions.Add(new() { MenuId = 461, ActionCode = "lpn-manage", ActionName = "Manage LPN" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 461, ActionCode = "lpn-manage" });
        db.WmsFeatureFlags.Add(new() { WarehouseCd = "W1", SerialLpnEnabled = true });
        db.Locations.Add(new() { WarehouseCd = "W1", LocationCd = "LPN-LOC", AreaCd = "LPN" });
        db.ProductMasters.AddRange(new() { ProductCd = product, ItemCd = product },
            new() { ProductCd = serialized, ItemCd = serialized, TrackingMode = ProductTrackingMode.Serial });
        db.Stocks.AddRange(new() { ProductCd = product, WarehouseCd = "W1", LocationCd = "LPN-LOC", LotNo = "", PhysicalQty = 3m, AvailableQty = 3m },
            new() { ProductCd = serialized, WarehouseCd = "W1", LocationCd = "LPN-LOC", LotNo = "", PhysicalQty = 2m, AvailableQty = 2m });
        foreach (var serial in new[] { "SER1", "SER2" })
            db.StockSerials.Add(new() { ProductCd = serialized, SerialNo = serial, WarehouseCd = "W1", LocationCd = "LPN-LOC", LotNo = "" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, tenantUser);
        var manager = await Login(host, administrator);
        using (var policy = await Post("api/v2/wms/lpns/policies", new LpnPolicyRequest
            { WarehouseCd = "W1", ContainerType = "BOX", AllowMixedProducts = true, AllowMixedLots = true }))
            Assert.True(policy.StatusCode == HttpStatusCode.OK, await OperationsFailure(policy));
        foreach (var number in new[] { "A1", "B1", "M1", "A2", "B2", "M2" })
        {
            using var created = await Post("api/v2/wms/lpns", new CreateLpnRequest
            { OperationId = Guid.NewGuid(), LpnNo = number, ContainerType = "BOX", WarehouseCd = "W1", LocationCd = "LPN-LOC" });
            Assert.True(created.StatusCode == HttpStatusCode.OK, await OperationsFailure(created));
        }
        var stockBefore = await StockRows();
        if (operation != "pack")
            foreach (var index in new[] { 1, 2 })
            {
                var pack = Pack(index);
                pack.RowVersion = await Version("A" + index);
                using var packed = await Post("api/v2/wms/lpns/A" + index + "/pack", pack);
                Assert.True(packed.StatusCode == HttpStatusCode.OK, await OperationsFailure(packed));
            }
        var firstTarget = Target(1);
        var first = Command(1);
        first.RowVersion = await Version(firstTarget);
        using var completed = await Execute(firstTarget, first);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await OperationsFailure(completed));
        var originalReply = await completed.Content.ReadAsStringAsync();
        var parent = operation switch { "pack" => "A1", "unpack" => null, "split" => "NEW1", _ => "M1" };
        Assert.Equal(parent, await db.LogisticsUnits.Where(u => u.LpnNo == "B1").Select(u => u.ParentLpnNo).SingleAsync());
        Assert.Equal(parent, await db.StockSerials.Where(s => s.ProductCd == serialized && s.SerialNo == "SER1").Select(s => s.LpnNo).SingleAsync());
        Assert.Equal(operation == "pack" ? 1.12345678m : 2.24691356m,
            await db.LpnContents.Where(c => c.ProductCd == product).SumAsync(c => c.Qty));
        Assert.Equal(stockBefore, await StockRows());
        if (parent != null)
            Assert.True(await db.LpnClosures.AnyAsync(c => c.AncestorLpnNo == parent && c.DescendantLpnNo == "B1" && c.Depth == 1));
        else
            Assert.False(await db.LpnClosures.AnyAsync(c => c.AncestorLpnNo != "B1" && c.DescendantLpnNo == "B1"));
        if (operation == "merge")
            Assert.True(await db.LogisticsUnits.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenant && u.LpnNo == "A1" && u.IsDeleted && u.Status == "MERGED"));
        var before = await WriteSet();
        using var replay = await Execute(firstTarget, first);
        Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
        Assert.Equal(originalReply, await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await WriteSet());

        var target = Target(2);
        var stale = Command(2);
        stale.RowVersion = Convert.ToBase64String(new byte[8]);
        await Rejected(stale, HttpStatusCode.Conflict, "WM-CONFLICT-ROW-VERSION");
        var invalid = Command(2);
        invalid.RowVersion = await Version(target);
        switch (invalid)
        {
            case PackLpnRequest pack: pack.ChildLpns = ["MISSING"]; break;
            case UnpackLpnRequest unpack: unpack.ChildLpns = ["MISSING"]; break;
            case SplitLpnRequest split: split.TargetLpnNo = "A2"; break;
            case MergeLpnRequest merge: merge.SourceLpnNo = target; break;
        }
        var error = operation switch { "pack" => "WM-LPN-CHILD-NOT-FOUND", "unpack" => "WM-LPN-CHILD-NOT-PACKED",
            "split" => "WM-LPN-ALREADY-EXISTS", _ => "WM-LPN-MERGE-SCOPE" };
        await Rejected(invalid, operation == "pack" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, error);
        Assert.Equal(before, await WriteSet());
        var command = Command(2);
        command.RowVersion = await Version(target);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var businessBlocker = new NpgsqlConnection(connection);
        await using var tenantBlocker = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await businessBlocker.OpenAsync();
        await tenantBlocker.OpenAsync();
        await observer.OpenAsync();
        FenceWait pauseWait, commandWait;
        Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended;
        await using (var businessLock = await businessBlocker.BeginTransactionAsync())
        {
            await using var lockUnits = new NpgsqlCommand("""LOCK TABLE public."T_LogisticsUnit" IN ACCESS EXCLUSIVE MODE""", businessBlocker, businessLock);
            await lockUnits.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock = await tenantBlocker.BeginTransactionAsync();
                await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", tenantBlocker, tenantLock);
                lockTenant.Parameters.AddWithValue("tenant", tenant);
                Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("lpn-tree-" + operation, active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = Execute(target, command);
                    commandWait = await ObserveLpnCreateWait(observer, pauseWait.Backend, businessBlocker.ProcessID, pending);
                }
                finally { await tenantLock.RollbackAsync(); }
                using var paused = await pause;
                suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await businessLock.RollbackAsync(); }
        }
        using var response = await pending;
        var after = await WriteSet();
        using var report = File.Open(Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!,
            "lpn-tree-" + operation + ".json"), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(report, new
        {
            Operation = operation, Tenant = tenant, pauseWait, commandWait, suspended.Snapshot.Revision, suspended.CommittedAt,
            AuthorityWait = commandWait.Blocker == pauseWait.Backend, Status = (int)response.StatusCode, Before = before, After = after
        });
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.Contains("REAUTH_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(pauseWait.Backend, commandWait.Blocker);
        Assert.Equal(before, after);
        using var deniedReplay = await Execute(firstTarget, first);
        Assert.True(deniedReplay.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(deniedReplay));
        Assert.Equal(before, await WriteSet());

        string Target(int index) => (operation == "merge" ? "M" : "A") + index;
        PackLpnRequest Pack(int index) => new()
        {
            OperationId = Guid.NewGuid(), ChildLpns = ["B" + index], Contents =
            [new() { ProductCd = product, Qty = 1.12345678m }, new() { ProductCd = serialized, SerialNo = "SER" + index, Qty = 1m }]
        };
        LpnCommand Command(int index) => operation switch
        {
            "pack" => Pack(index),
            "unpack" => new UnpackLpnRequest { OperationId = Guid.NewGuid(), ChildLpns = ["B" + index], SerialNos = ["SER" + index] },
            "split" => new SplitLpnRequest { OperationId = Guid.NewGuid(), TargetLpnNo = "NEW" + index, TargetContainerType = "BOX", ChildLpns = ["B" + index], SerialNos = ["SER" + index] },
            _ => new MergeLpnRequest { OperationId = Guid.NewGuid(), SourceLpnNo = "A" + index }
        };
        Task<HttpResponseMessage> Post(string path, object body) => host.Send(OperationsRequest(writer, HttpMethod.Post, path, body));
        Task<HttpResponseMessage> Execute(string number, LpnCommand body) => Post("api/v2/wms/lpns/" + number + "/" + operation, body);
        async Task<string> Version(string number)
        {
            using var found = await host.Send(OperationsRequest(writer, HttpMethod.Get, "api/v2/wms/lpns/" + number));
            Assert.True(found.StatusCode == HttpStatusCode.OK, await OperationsFailure(found));
            using var json = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("rowVersion").GetString()!;
        }
        async Task Rejected(LpnCommand body, HttpStatusCode status, string code)
        {
            using var rejected = await Execute(target, body);
            Assert.True(rejected.StatusCode == status, await OperationsFailure(rejected));
            using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        }
        Task<string> StockRows() => db.Database.SqlQuery<string>($"""
            SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]')::text AS "Value" FROM public."T_Stock" s WHERE s."TenantId"={tenant}
            """).SingleAsync();
        Task<string> WriteSet() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'Units', (SELECT coalesce(jsonb_agg(to_jsonb(u) ORDER BY u."Id"),'[]') FROM public."T_LogisticsUnit" u WHERE u."TenantId"={tenant}),
              'Closures', (SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c."Id"),'[]') FROM public."T_LpnClosure" c WHERE c."TenantId"={tenant}),
              'Contents', (SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c."Id"),'[]') FROM public."T_LpnContent" c WHERE c."TenantId"={tenant}),
              'Serials', (SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockSerial" s WHERE s."TenantId"={tenant}),
              'Events', (SELECT coalesce(jsonb_agg(to_jsonb(e) ORDER BY e."Id"),'[]') FROM public."T_LpnEvent" e WHERE e."TenantId"={tenant}),
              'Receipts', (SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."T_TaskCommandReceipt" r WHERE r."TenantId"={tenant}),
              'Stock', (SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={tenant}),
              'StockTransactions', (SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={tenant}),
              'SerialTransactions', (SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockSerialTransaction" t WHERE t."TenantId"={tenant})
            )::text AS "Value"
            """).SingleAsync();
    }
}
