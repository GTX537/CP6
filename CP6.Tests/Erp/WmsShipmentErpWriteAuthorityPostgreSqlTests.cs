using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_erp_shipment_projection_and_pause_share_the_native_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var shipment = await AllocateFulfilment(h, quantity: 2.5m);
        await SeedAr(h, "invoice-create");
        var database = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString())
        {
            SslMode = SslMode.Disable,
        };
        var route = $"/api/wms/outbound-order/{shipment.Number}/ship";

        FenceWait staged;
        // The source rows must stay invisible while their intent insertion is blocked.
        // The relay then drops the first real COMMIT reply from the same source request.
        var relayPort = ShipmentProjectionPort("CP6_ERP_SHIPMENT_RELAY_PORT", 57176);
        await using (var relay = new StockPostgresCommitRelay(database.Port, database.Username!, relayPort))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Port = relay.Port };
            await using var originalHost = await StartW643Api(h, forwarded.ConnectionString);
            var actor = await Login(originalHost, h);
            await using var barrier = new NpgsqlConnection(database.ConnectionString);
            await using var sourceObserver = new NpgsqlConnection(database.ConnectionString);
            await barrier.OpenAsync();
            await sourceObserver.OpenAsync();
            var before = await ReadW643(sourceObserver, h.Tenant, h.Number, shipment.Number);
            Assert.Equal(OutboundOrderStatus.Allocated, AssetInt(Assert.Single(before.Rows("physical")), "Status"));
            Assert.Empty(before.Rows("events"));
            await using var hold = await barrier.BeginTransactionAsync();
            await using var relation = new NpgsqlCommand(
                """LOCK TABLE public."T_IntegrationEvent" IN SHARE MODE""", barrier, hold);
            await relation.ExecuteNonQueryAsync();
            relay.Arm();
            var producing = originalHost.Send(OperationsRequest(actor, HttpMethod.Post, route,
                new { captureReturnSource = true }));
            staged = await WaitForFence(sourceObserver, barrier.ProcessID, "%INSERT%T_IntegrationEvent%", null, producing);
            Assert.Equal(before, await ReadW643(sourceObserver, h.Tenant, h.Number, shipment.Number));
            await hold.RollbackAsync();
            using var lost = await producing;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await OperationsFailure(lost), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }

        // The same accepted shipment also created a separate FIN intent. Keep it outside this retry race.
        await h.Db.IntegrationEvents.Where(row => row.SourceModule == "WMS" && row.TargetModule == "FIN")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.NextRetryAt, DateTime.UtcNow.AddHours(1)));

        await using var host = await StartW643Api(h, database.ConnectionString);
        var manager = await Login(host, admin);
        using var tenantResponse = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var tenant = await NumberHttpData<TenantManagementSnapshotV1>(tenantResponse);
        var suspend = new TenantManagementCommandV1("erp-shipment-pause", tenant.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await using var parents = new NpgsqlConnection(database.ConnectionString);
        await using var directory = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        await parents.OpenAsync();
        await directory.OpenAsync();

        var original = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
        var intent = Assert.Single(original.Rows("events"));
        var source = Assert.Single(original.Rows("physical"));
        Assert.Equal(OutboundOrderStatus.Completed, AssetInt(source, "Status"));
        using var payload = JsonDocument.Parse(intent.GetProperty("PayloadJson").GetString()!);
        Assert.Equal(AssetId(source), payload.RootElement.GetProperty("OutboundId").GetGuid());
        Assert.Equal(h.Number, payload.RootElement.GetProperty("OrderNo").GetString());
        Assert.Equal((IntegrationEventStatus.Pending, 0),
            (intent.GetProperty("Status").GetString(), AssetInt(intent, "Attempts")));
        Assert.All(original.Rows("lines"), line =>
        {
            var qty = line.GetProperty("ShippedQty");
            Assert.True(qty.ValueKind == JsonValueKind.Null || qty.GetDecimal() == 0m);
        });

        await using var workers = ShipmentProjectionWorkers(h.Tenant);
        var worker = W620Worker(workers);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var pauseHold = await directory.BeginTransactionAsync(deadline.Token);
        await using var holdTenant = new NpgsqlCommand("""LOCK TABLE public."Sys_Tenants" IN SHARE MODE""", directory, pauseHold);
        await holdTenant.ExecuteNonQueryAsync(deadline.Token);

        FenceWait first;
        FenceWait sameRoot;
        Task<HttpResponseMessage> pausing;
        AssetRows firstCommitted;
        if (pauseFirst)
        {
            pausing = Pause();
            first = await WaitForFence(observer, directory.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            var retry = worker.ProcessOnceAsync(deadline.Token);
            sameRoot = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire%", null, retry);
            Assert.Equal(original, await ReadW643(observer, h.Tenant, h.Number, shipment.Number));
            await pauseHold.RollbackAsync();
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            firstCommitted = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
            Assert.Equal(original, firstCommitted);
        }
        else
        {
            await using var orderHold = await parents.BeginTransactionAsync(deadline.Token);
            await using var holdOrder = new NpgsqlCommand("""LOCK TABLE public."T_Order" IN SHARE MODE""", parents, orderHold);
            await holdOrder.ExecuteNonQueryAsync(deadline.Token);
            var retry = worker.ProcessOnceAsync(deadline.Token);
            first = await WaitForFence(observer, parents.ProcessID, "%UPDATE%T_Order%", null, retry);
            pausing = Pause();
            sameRoot = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire%", null, pausing);
            Assert.Equal(original, await ReadW643(observer, h.Tenant, h.Number, shipment.Number));
            Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            await orderHold.RollbackAsync();
            await retry.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitForFence(observer, directory.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            firstCommitted = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
            AssertW643Projection(original, firstCommitted);
            authority = authority with { RootRevision = authority.RootRevision + 1 };
            await pauseHold.RollbackAsync();
        }

        using var paused = await pausing;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        Assert.Equal(firstCommitted, await ReadW643(observer, h.Tenant, h.Number, shipment.Number));
        if (pauseFirst)
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
        await worker.ProcessOnceAsync();
        var final = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
        AssertW643Projection(original, final);
        await worker.ProcessOnceAsync();
        Assert.Equal(final, await ReadW643(observer, h.Tenant, h.Number, shipment.Number));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            pauseFirst, staged, first, sameRoot, Source = PurchaseState(original),
            DuringPause = PurchaseState(firstCommitted), Final = PurchaseState(final),
        }));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), suspend));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_erp_shipment_projection_keeps_the_proven_legacy_effect()
    {
        await using var h = await Seed();
        var shipment = await AllocateFulfilment(h, quantity: 2.5m);
        await SeedAr(h, "invoice-create");
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartW643Api(h, connection);
        var actor = await Login(host, h);
        using var shipped = await host.Send(OperationsRequest(actor, HttpMethod.Post,
            $"/api/wms/outbound-order/{shipment.Number}/ship", new { captureReturnSource = true }));
        Assert.True(shipped.StatusCode == HttpStatusCode.OK, await OperationsFailure(shipped));

        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var applied = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
        var original = Assert.Single(applied.Rows("events"));
        Assert.Equal(IntegrationEventStatus.Success, original.GetProperty("Status").GetString());
        Assert.All(applied.Rows("lines"), row => Assert.Equal(2.5m, AssetDecimal(row, "ShippedQty")));
        var id = AssetId(original);
        var legacy = JsonSerializer.Serialize(new { outboundNo = shipment.Number, userName = original.GetProperty("Creator").GetString() });
        // Preserve the actual earlier order effect and simulate the old pending result format.
        Assert.Equal(1, await h.Db.IntegrationEvents.Where(row => row.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.PayloadJson, legacy)
            .SetProperty(row => row.Status, IntegrationEventStatus.Failed)
            .SetProperty(row => row.TargetNo, (string?)null)
            .SetProperty(row => row.LastError, "original result unavailable")
            .SetProperty(row => row.NextRetryAt, DateTime.UtcNow.AddSeconds(-1))));
        var pending = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);

        await using var services = ShipmentProjectionWorkers(h.Tenant);
        var worker = W620Worker(services);
        await worker.ProcessOnceAsync();
        var completed = await ReadW643(observer, h.Tenant, h.Number, shipment.Number);
        CheckPurchaseTables(pending, completed, "physical", "physical-lines", "orders", "lines");
        var result = Assert.Single(completed.Rows("events"));
        Assert.Equal(id, AssetId(result));
        Assert.Equal(original.GetProperty("CorrelationId").GetString(), result.GetProperty("CorrelationId").GetString());
        Assert.Equal(legacy, result.GetProperty("PayloadJson").GetString());
        Assert.Equal((IntegrationEventStatus.Success, 2),
            (result.GetProperty("Status").GetString(), AssetInt(result, "Attempts")));
        Assert.Equal(h.Number, result.GetProperty("TargetNo").GetString());
        await worker.ProcessOnceAsync();
        Assert.Equal(completed, await ReadW643(observer, h.Tenant, h.Number, shipment.Number));
        output.WriteLine(JsonSerializer.Serialize(new { h.Tenant, shipment.Number, Event = id,
            Before = PurchaseState(pending), After = PurchaseState(completed) }));
    }

    private ServiceProvider ShipmentProjectionWorkers(Guid tenant)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => server.Context((TenantContext)sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<TenantEnumerator>();
        services.AddScoped<ITenantEnumerator>(sp => new OaEventTenantEnumerator(sp.GetRequiredService<TenantEnumerator>(), tenant));
        services.AddScoped<IErpBridgeHook, ErpBridgeHook>();
        services.AddScoped<IDeadLetterNotifier, DeadLetterNotifier>();
        return services.BuildServiceProvider();
    }

    private static int ShipmentProjectionPort(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var port) ? port : fallback;

    private static Task<AuthenticationApiProcess> StartW643Api(Harness h, string connection, int? requestedPort = null)
    {
        var env = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        var port = requestedPort ?? ShipmentProjectionPort("CP6_ERP_SHIPMENT_HTTP_PORT", 57177);
        env["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        env["IntegrationEvent__Enabled"] = "false";
        return AuthenticationApiProcess.Start(connection, env);
    }

    private static void AssertW643Projection(AssetRows before, AssetRows after)
    {
        CheckPurchaseTables(before, after, "physical", "physical-lines");
        var original = Assert.Single(before.Rows("events"));
        var result = Assert.Single(after.Rows("events"));
        foreach (var field in new[] { "Id", "TenantId", "SourceNo", "CorrelationId", "PayloadJson", "Creator" })
            Assert.Equal(original.GetProperty(field).GetRawText(), result.GetProperty(field).GetRawText());
        Assert.Equal((IntegrationEventStatus.Success, 1),
            (result.GetProperty("Status").GetString(), AssetInt(result, "Attempts")));
        var header = Assert.Single(after.Rows("orders"));
        Assert.Equal(AssetId(Assert.Single(before.Rows("orders"))), AssetId(header));
        Assert.Equal(result.GetProperty("writer_xmin").GetString(), header.GetProperty("writer_xmin").GetString());
        Assert.Equal(9, AssetInt(header, "ShipStatus"));
        Assert.Equal(result.GetProperty("TargetNo").GetString(), header.GetProperty("WebOrderNo").GetString());
        var rows = after.Rows("lines");
        Assert.Equal(2, rows.Length);
        Assert.Equal(before.Rows("lines").Select(row => AssetId(row)), rows.Select(row => AssetId(row)));
        Assert.All(rows, line =>
        {
            Assert.Equal(2.5m, AssetDecimal(line, "ShippedQty"));
            Assert.Equal(9, AssetInt(line, "ShipStatus"));
            Assert.Equal(result.GetProperty("SourceNo").GetString(), line.GetProperty("LastOutboundNo").GetString());
            Assert.Equal(result.GetProperty("writer_xmin").GetString(), line.GetProperty("writer_xmin").GetString());
        });
    }

    private static async Task<AssetRows> ReadW643(NpgsqlConnection db, Guid tenant, string order, string outbound)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
                'orders', ARRAY(SELECT to_jsonb(r)||jsonb_build_object('writer_xmin',r.xmin::text)
                  FROM public."T_Order" AS r WHERE r."TenantId"=@tenant AND r."WebOrderNo"=@order ORDER BY r."Id"),
                'lines', ARRAY(SELECT to_jsonb(r)||jsonb_build_object('writer_xmin',r.xmin::text)
                  FROM public."T_OrderDetail" AS r WHERE r."TenantId"=@tenant AND r."WebOrderNo"=@order ORDER BY r."WebOrderDetailNo"),
                'physical', ARRAY(SELECT to_jsonb(r)||jsonb_build_object('writer_xmin',r.xmin::text)
                  FROM public."T_OutboundOrder" AS r WHERE r."TenantId"=@tenant AND r."OutboundNo"=@outbound ORDER BY r."Id"),
                'physical-lines', ARRAY(SELECT to_jsonb(r) FROM public."T_OutboundOrderDetail" AS r
                  WHERE r."TenantId"=@tenant AND r."OutboundNo"=@outbound ORDER BY r."LineNo"),
                'events', ARRAY(SELECT to_jsonb(r)||jsonb_build_object('writer_xmin',r.xmin::text)
                  FROM public."T_IntegrationEvent" AS r WHERE r."TenantId"=@tenant AND r."SourceNo"=@outbound
                    AND r."SourceModule"='WMS' AND r."TargetModule"='ERP' ORDER BY r."Id")
            )::text
            """, db);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("order", order);
        command.Parameters.AddWithValue("outbound", outbound);
        return new(Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }
}
