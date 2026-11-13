using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Tenant_pause_and_ordinary_stock_commit_share_the_real_fence_in_both_orders()
    {
        await using var stockTenant = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantStocktakeDashboard(stockTenant);
        await SeedInventory(stockTenant);
        var db = stockTenant.Db;
        var tenant = stockTenant.Tenant;
        var stock = await db.Stocks.AsNoTracking().SingleAsync(row => row.ProductCd == "P1");
        var departments = await MappingAdmin(stockTenant);
        await BindStandalone(stockTenant, departments, "stock", stock.Id);
        await StandaloneScope(stockTenant, departments, 3);
        await db.WmsRoleScopes.ExecuteUpdateAsync(rows => rows.SetProperty(row => row.AreaCd, "A"));

        var connection = db.Database.GetConnectionString()!;
        await using var blocker = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await blocker.OpenAsync();
        await observer.OpenAsync();
        const string login = "stock_pause_a";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(blocker, $"CREATE ROLE {login} LOGIN PASSWORD '{password}'");
        TenantLoginV1[] logins = [new(tenant, login)];
        await using (var install = await blocker.BeginTransactionAsync())
        {
            var read = await StockReadStorageV1.InstallAsync(blocker, install, logins);
            await StockWriteAuthorityFenceV1.InstallAsync(blocker, install, read, logins);
            await StockOriginalJournalStorageV1.InstallAsync(blocker, install, read, logins);
            await StockOutboxWriterStorageV1.InstallAsync(blocker, install, read, logins);
            await StockMovementWriterStorageV1.InstallAsync(blocker, install, read, logins);
            await StockAdjustOwnerStorageV1.InstallAsync(blocker, install, read, logins);
            await install.CommitAsync();
        }

        var settings = StockReadSettings();
        foreach (var (key, value) in IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant)) settings[key] = value;
        settings["StockReadTenancy__EnableV1Adjust"] = "true";
        ConfigureStockReader(settings, 0, stockTenant, login, password);
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var manager = await Login(host, administrator);
        var writer = await Login(host, stockTenant);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var initial = await NumberHttpData<TenantManagementSnapshotV1>(current);

        var authorityBefore = await ReadPauseAuthority(stockTenant, administrator.User);
        var stockBefore = await ReadStockWriteSet();
        var admitted = StockPauseCommand(1.12345678m);
        var stockFirst = new TenantManagementCommandV1("stock-first-pause", initial.Revision, "suspend", null);
        FenceWait stockGraph, pauseRoot;
        Task<HttpResponseMessage> firstStock;
        Task<HttpResponseMessage> firstPause;
        await using (var table = await blocker.BeginTransactionAsync())
        {
            await using var lockTable = new NpgsqlCommand("""LOCK TABLE public."T_Stock" IN ROW EXCLUSIVE MODE""", blocker, table);
            await lockTable.ExecuteNonQueryAsync();
            firstStock = Adjust(admitted);
            try
            {
                stockGraph = await WaitForFence(observer, blocker.ProcessID, "%cp6_stock_adjust_owner_v1.lock_graph%", login, firstStock);
                firstPause = Manage(stockFirst);
                pauseRoot = await WaitForFence(observer, stockGraph.Backend, "%cp6_authentication_v1.acquire()%", null, firstPause);
                Assert.Equal(stockBefore, await ReadStockWriteSet());
                Assert.Equal(authorityBefore, await ReadPauseAuthority(stockTenant, administrator.User));
            }
            finally { await table.RollbackAsync(); }
        }
        using var stockResponse = await firstStock;
        var result = await NumberHttpData<StockOperationResultV1>(stockResponse);
        using var firstPauseResponse = await firstPause;
        var paused = await NumberHttpData<TenantManagementReceiptV1>(firstPauseResponse);
        Assert.Equal(admitted.OperationId, result.OperationId);
        var committed = await ReadStockWriteSet();
        using (var json = JsonDocument.Parse(committed))
        {
            var rows = json.RootElement;
            var original = Assert.Single(rows.GetProperty("originals").EnumerateArray());
            Assert.Equal(admitted.OperationId, original.GetProperty("operation_id").GetGuid());
            Assert.Equal(stockTenant.User, original.GetProperty("actor_id").GetGuid());
            Assert.Equal("COMMITTED", original.GetProperty("state").GetString());
            var audit = original.GetProperty("audit_json");
            Assert.Equal("adjust", audit.GetProperty("action").GetString());
            Assert.Equal(stockTenant.User, audit.GetProperty("actor").GetGuid());
            var transaction = Assert.Single(rows.GetProperty("transactions").EnumerateArray());
            Assert.Equal(result.TxnNo, transaction.GetProperty("TxnNo").GetString());
            Assert.Equal(admitted.Qty, transaction.GetProperty("Qty").GetDecimal());
            Assert.Equal("price-owner", transaction.GetProperty("OperatorCd").GetString());
            var source = Assert.Single(rows.GetProperty("sources").EnumerateArray());
            Assert.Equal(admitted.OperationId, source.GetProperty("OperationId").GetGuid());
            var outbox = Assert.Single(rows.GetProperty("outbox").EnumerateArray());
            Assert.Equal(source.GetProperty("NativeId").GetGuid(), outbox.GetProperty("Id").GetGuid());
            Assert.Equal(stock.PhysicalQty + admitted.Qty, rows.GetProperty("stock").GetProperty("PhysicalQty").GetDecimal());
        }
        var firstAuthority = await ReadPauseAuthority(stockTenant, administrator.User);
        CheckPause(authorityBefore, firstAuthority, paused, stockFirst);
        using (var stale = await Adjust(StockPauseCommand(2m))) Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(committed, await ReadStockWriteSet());

        using var reactivated = await Manage(new("resume-between-orders", paused.Snapshot.Revision, "reactivate", null));
        var active = await NumberHttpData<TenantManagementReceiptV1>(reactivated);
        Assert.True(active.Snapshot.Enable);
        writer = await Login(host, stockTenant);
        var activeAuthority = await ReadPauseAuthority(stockTenant, administrator.User);
        var pauseFirst = new TenantManagementCommandV1("pause-first", active.Snapshot.Revision, "suspend", null);
        var denied = StockPauseCommand(4m);
        FenceWait pauseRow, stockRoot;
        Task<HttpResponseMessage> secondPause;
        Task<HttpResponseMessage> secondStock;
        await using (var row = await blocker.BeginTransactionAsync())
        {
            await using var lockRow = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", blocker, row);
            lockRow.Parameters.AddWithValue("tenant", tenant);
            Assert.Equal(1, await lockRow.ExecuteScalarAsync());
            secondPause = Manage(pauseFirst);
            try
            {
                pauseRow = await WaitForFence(observer, blocker.ProcessID, "%UPDATE%Sys_Tenants%", null, secondPause);
                secondStock = Adjust(denied);
                stockRoot = await WaitForFence(observer, pauseRow.Backend, "%cp6_stock_adjust_owner_v1.lock_graph%", login, secondStock);
                Assert.Equal(committed, await ReadStockWriteSet());
                Assert.Equal(activeAuthority, await ReadPauseAuthority(stockTenant, administrator.User));
            }
            finally { await row.RollbackAsync(); }
        }
        using var secondPauseResponse = await secondPause;
        var second = await NumberHttpData<TenantManagementReceiptV1>(secondPauseResponse);
        using var secondStockResponse = await secondStock;
        Assert.True(secondStockResponse.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(secondStockResponse));
        Assert.Contains("REAUTH_REQUIRED", await secondStockResponse.Content.ReadAsStringAsync());
        using (var stale = await Adjust(StockPauseCommand(8m))) Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(committed, await ReadStockWriteSet());
        var finalAuthority = await ReadPauseAuthority(stockTenant, administrator.User);
        CheckPause(activeAuthority, finalAuthority, second, pauseFirst);

        using var report = File.Open(Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!,
            "stock-pause-race.json"), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(report, new
        {
            Tenant = tenant, Actor = stockTenant.User, Administrator = administrator.User,
            StockFirst = new { stockGraph, pauseRoot, Before = authorityBefore, After = firstAuthority, admitted.OperationId, result.TxnNo, paused },
            PauseFirst = new { pauseRow, stockRoot, Before = activeAuthority, After = finalAuthority, denied.OperationId, Status = (int)secondStockResponse.StatusCode, second },
            StockWriteSetSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(committed))).ToLowerInvariant()
        });

        Task<HttpResponseMessage> Adjust(StockApplyCommandV1 command) => host.Send(
            OperationsRequest(writer, HttpMethod.Post, "api/wms/stock/commands/v1/apply", command));
        Task<HttpResponseMessage> Manage(TenantManagementCommandV1 command) => host.Send(
            OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), command));

        Task<string> ReadStockWriteSet() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('stock',(SELECT to_jsonb(s) FROM public."T_Stock" s WHERE s."Id"={stock.Id}),
                'transactions',coalesce((SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id")
                    FROM public."T_StockTransaction" t WHERE t."TenantId"={tenant}),'[]'::jsonb),
                'originals',coalesce((SELECT jsonb_agg(to_jsonb(o) ORDER BY o.operation_id)
                    FROM cp6_main_owner_v1.operations o WHERE o.tenant_id={tenant} AND o.owner='Main.Wms.StockMovement'),'[]'::jsonb),
                'sources',coalesce((SELECT jsonb_agg(to_jsonb(s) ORDER BY s."NativeId")
                    FROM cp6_stock_outbox_v1."Source" s WHERE s."TenantId"={tenant}),'[]'::jsonb),
                'outbox',coalesce((SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Id")
                    FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" m WHERE m."TenantId"={tenant}),'[]'::jsonb))::text AS "Value"
            """).SingleAsync();
    }

    private static StockApplyCommandV1 StockPauseCommand(decimal quantity) => new()
    {
        OperationId = Guid.NewGuid(), TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1",
        ProductCd = "P1", LotNo = "LOT", Qty = quantity, UnitPrice = 3m, OperatorCd = "forged"
    };

    private sealed record PauseAuthority(bool Enabled, Guid PolicyRevision, long Revision, long RootRevision,
        long Generation, long SnapshotVersion, int PriorityCount, int AuditCount, int ReceiptCount);

    private static async Task<PauseAuthority> ReadPauseAuthority(Harness h, Guid administrator)
    {
        var tenant = h.Tenant;
        var json = await h.Db.Database.SqlQuery<string>($"""
            SELECT json_build_object('Enabled',t."Enable",'PolicyRevision',t."AuthenticationPolicyRevision",
                'Revision',v.revision,'RootRevision',(SELECT revision FROM cp6_platform_admin_guard_v1.root WHERE singleton),
                'Generation',coalesce((SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),0),
                'SnapshotVersion',coalesce((SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={tenant}
                    AND "AggregateId"={$"tenant:{tenant:D}"}),0),
                'PriorityCount',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant}),
                'AuditCount',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "UserId"={administrator}
                    AND "RequestTenantCode"={tenant.ToString("N")} AND "EventType"={(int)SecurityEventType.TenantSuspended}),
                'ReceiptCount',(SELECT count(*) FROM cp6_tenant_management_v1.commands WHERE tenant_id={tenant}))::text AS "Value"
            FROM public."Sys_Tenants" t JOIN cp6_tenant_management_v1.versions v ON v.tenant_id=t."Id"
            WHERE t."Id"={tenant}
            """).SingleAsync();
        return JsonSerializer.Deserialize<PauseAuthority>(json)!;
    }

    private static void CheckPause(PauseAuthority before, PauseAuthority after, TenantManagementReceiptV1 receipt, TenantManagementCommandV1 command)
    {
        Assert.True(before.Enabled);
        Assert.False(after.Enabled);
        Assert.NotEqual(before.PolicyRevision, after.PolicyRevision);
        Assert.True(after.RootRevision > before.RootRevision);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.True(after.SnapshotVersion > before.SnapshotVersion);
        Assert.Equal(before.PriorityCount + 1, after.PriorityCount);
        Assert.Equal(before.AuditCount + 1, after.AuditCount);
        Assert.Equal(before.ReceiptCount + 1, after.ReceiptCount);
        Assert.Equal(command.OperationRef, receipt.OperationRef);
        Assert.Equal(after.Revision, receipt.Snapshot.Revision);
        Assert.False(receipt.Snapshot.Enable);
    }

    private sealed record FenceWait(int Backend, int Blocker, string Login, string WaitEvent, DateTimeOffset ObservedAt);

    private static async Task<FenceWait> WaitForFence(NpgsqlConnection observer, int blocker, string statement, string? login,
        Task<HttpResponseMessage> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT pid,usename,wait_event,clock_timestamp() FROM pg_stat_activity
            WHERE datname=current_database() AND wait_event_type='Lock' AND @blocker=ANY(pg_blocking_pids(pid))
              AND query LIKE @statement AND (@login='' OR usename=@login) ORDER BY pid LIMIT 1
            """, observer);
        command.Parameters.AddWithValue("blocker", blocker);
        command.Parameters.AddWithValue("statement", statement);
        command.Parameters.AddWithValue("login", login ?? "");
        while (true)
        {
            await using (var rows = await command.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                    return new(rows.GetInt32(0), blocker, rows.GetString(1), rows.GetString(2), rows.GetFieldValue<DateTimeOffset>(3));
            if (request.IsCompleted) Assert.Fail("Request completed before the expected native fence: " + await OperationsFailure(await request));
            await Task.Delay(35, deadline.Token);
        }
    }
}
