using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string RestrictedStockPath = "api/wms/stock/";
    private const decimal RestrictedExactQuantity = 2.000000004m;
    private const decimal RestrictedExactPrice = 1.00249m;

    [CurrentPermissionPostgresFact]
    public async Task W642_restricted_stock_orders_share_original_finance_write()
    {
        await using var h = await Seed();
        await using var other = await Seed();
        await using var administrator = await TenantManagementSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var control = await OpenRestrictedStockFinanceConnection(connection);
        await using var observer = await OpenRestrictedStockFinanceConnection(connection);
        var setup = await InstallRestrictedStockFixture(h, control, other: other);
        await CheckRestrictedStockFinancePrivileges(control, setup.Bindings);
        await using var host = await StartRestrictedStockFinanceApi(h, setup);
        var manager = await Login(host, administrator);
        var actor = await Login(host, h);

        foreach (var entry in new[] { "v1", "v2", "legacy" })
        {
            foreach (var stockFirst in new[] { true, false })
            {
                var submission = await RestrictedStockFinanceInput(host, actor, setup.Department, entry, stockFirst);
                using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
                var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
                var original = await ReadRestrictedStockFinance(observer, h.Tenant);
                FenceWait stockWait;
                FenceWait pauseWait;
                Task<HttpResponseMessage> pendingStock;
                Task<HttpResponseMessage> pendingPause;
                if (stockFirst)
                {
                    await using var held = await HoldUnderlayTable(control, "T_IntegrationEvent");
                    pendingStock = SendRestrictedStockFinance(host, actor, submission);
                    stockWait = await WaitForFence(observer, control.ProcessID, "%cp6_stock_movement_writer_v1.stage_finance_event%",
                        setup.Logins[0].Login, pendingStock);
                    pendingPause = Pause(active.Revision);
                    pauseWait = await WaitForFence(observer, stockWait.Backend, "%cp6_authentication_v1.acquire()%", null, pendingPause);
                    Assert.Equal(original, await ReadRestrictedStockFinance(observer, h.Tenant));
                    await held.RollbackAsync();
                }
                else
                {
                    await using var held = await HoldUnderlayTenant(control, h.Tenant);
                    pendingPause = Pause(active.Revision);
                    pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pendingPause);
                    pendingStock = SendRestrictedStockFinance(host, actor, submission);
                    stockWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_stock_adjust_owner_v1.lock_graph%",
                        setup.Logins[0].Login, pendingStock);
                    Assert.Equal(original, await ReadRestrictedStockFinance(observer, h.Tenant));
                    await held.RollbackAsync();
                }

                using var paused = await pendingPause;
                Assert.False((await NumberHttpData<TenantManagementReceiptV1>(paused)).Snapshot.Enable);
                using var result = await pendingStock;
                var after = await ReadRestrictedStockFinance(observer, h.Tenant);
                var order = stockFirst ? "stock-first" : "pause-first";
                if (!stockFirst)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
                    Assert.Equal(original, after);
                    await DeviceEvidence($"w642-{entry}-{order}.json", new { stockWait, pauseWait, Stock = original, After = after });
                    await ResumeSourceUploadTenant(host, manager, h.Tenant, paused);
                    actor = await Login(host, h);
                    if (submission.OriginalId is { } denied)
                        Assert.Equal("UNKNOWN", (await ReadRestrictedStockOriginal(host, actor, denied)).GetProperty("state").GetString());
                    continue;
                }

                var originalResult = await ReadRestrictedStockResult(result, entry == "legacy" ? "WM-MSG-071" : "OK");
                var movement = Assert.Single(after.Effects.Rows("stock-moves")
                    .Where(row => !original.Effects.Rows("stock-moves").Any(previous => AssetId(previous) == AssetId(row))));
                var id = AssetId(movement);
                var evt = CheckW636Event(after.Effects, movement, IntegrationEventStatus.Pending, 0);
                Assert.Equal(id, AssetId(evt));
                CheckRestrictedRawPayload(evt, movement, submission.Command);
                var writes = await ReadRestrictedStockTransactionIds(observer, h.Tenant, id, submission.OriginalId);
                var expectedTables = submission.OriginalId.HasValue ? new[] { "event", "movement", "original", "outbox", "stock" }
                    : ["event", "movement", "stock"];
                Assert.Equal(expectedTables, writes.Select(row => row.Kind));
                Assert.Single(writes.Where(row => row.Kind != "outbox").Select(row => row.Xmin).Distinct());
                Assert.Equal(original.Effects.Rows("journals").Count(), after.Effects.Rows("journals").Count());
                await CheckRestrictedStockIsolation(connection, setup.Logins[1], id);

                await ResumeSourceUploadTenant(host, manager, h.Tenant, paused);
                actor = await Login(host, h);
                if (submission.OriginalId is { } accepted)
                {
                    var recovered = await ReadRestrictedStockOriginal(host, actor, accepted);
                    Assert.Equal("COMMITTED", recovered.GetProperty("state").GetString());
                    Assert.Equal(originalResult.GetRawText(), recovered.GetProperty("result").GetRawText());
                    using var replay = await SendRestrictedStockFinance(host, actor, submission);
                    Assert.Equal(originalResult.GetRawText(), (await ReadRestrictedStockResult(replay)).GetRawText());
                }
                Assert.Equal(after, await ReadRestrictedStockFinance(observer, h.Tenant));

                await using (var worker = W620Services(h.Tenant))
                    await W620Worker(worker).ProcessOnceAsync();
                var completed = await ReadRestrictedStockFinance(observer, h.Tenant);
                Assert.Equal(after.Originals, completed.Originals);
                Assert.Equal(after.Outbox, completed.Outbox);
                Assert.Equal(movement.GetRawText(), Assert.Single(completed.Effects.Rows("stock-moves"), row => AssetId(row) == id).GetRawText());
                if (submission.Command.UnitPrice is null)
                {
                    var skipped = CheckW636Event(completed.Effects, movement, IntegrationEventStatus.Skipped, 1,
                        error: "单价缺失，无法计价过账");
                    Assert.Equal(AssetId(evt), AssetId(skipped));
                    Assert.Equal(after.Effects.Rows("journals").Count(), completed.Effects.Rows("journals").Count());
                }
                else
                {
                    var scrap = entry == "legacy";
                    CheckW636Event(completed.Effects, movement, IntegrationEventStatus.Success, 1,
                        scrap ? "Inventory.Scrapped" : "Inventory.Received");
                    CheckW636Voucher(completed.Effects, movement, setup.Book.Accounts[scrap ? "NON_OP_EXPENSE" : "INVENTORY"],
                        setup.Book.Accounts[scrap ? "INVENTORY" : "GRNI"], scrap ? 7.50m : 2.00m);
                }
                await DeviceEvidence($"w642-{entry}-{order}.json", new { stockWait, pauseWait, Writes = writes,
                    Original = originalResult, Before = original, Committed = after, Completed = completed });
            }
        }

        Task<HttpResponseMessage> Pause(long revision) => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), revision, "suspend", null)));
    }

    [CurrentPermissionPostgresFact]
    public async Task W642_restricted_stock_native_commit_ack_and_original_recovery()
    {
        await using var h = await Seed();
        var direct = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()!) { SslMode = SslMode.Disable };
        await using var control = await OpenRestrictedStockFinanceConnection(direct.ConnectionString);
        var setup = await InstallRestrictedStockFixture(h, control);
        await CheckRestrictedStockFinancePrivileges(control, setup.Bindings);
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("CP6_W642_RELAY_PORT"), out var reserved) ? reserved : 0;
        RestrictedStockSubmission submission;
        await using (var relay = new StockPostgresCommitRelay(direct.Port, setup.Logins[0].Login, relayPort))
        {
            var forwarded = new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = relay.Port };
            await using var faulted = await StartRestrictedStockFinanceApi(h, setup, forwarded.ConnectionString);
            var actor = await Login(faulted, h);
            submission = await RestrictedStockFinanceInput(faulted, actor, setup.Department, "v1", stockFirst: true);
            relay.Arm();
            using var result = await SendRestrictedStockFinance(faulted, actor, submission);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await OperationsFailure(result), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        }

        var committed = await ReadRestrictedStockFinance(control, h.Tenant);
        var movement = Assert.Single(committed.Effects.Rows("stock-moves"));
        var evt = CheckW636Event(committed.Effects, movement, IntegrationEventStatus.Pending, 0);
        CheckRestrictedRawPayload(evt, movement, submission.Command);
        var writes = await ReadRestrictedStockTransactionIds(control, h.Tenant, AssetId(movement), submission.OriginalId);
        Assert.Equal(5, writes.Length);
        Assert.Single(writes.Where(row => row.Kind != "outbox").Select(row => row.Xmin).Distinct());
        Assert.Empty(committed.Effects.Rows("journals"));

        await using var healthy = await StartRestrictedStockFinanceApi(h, setup, direct.ConnectionString);
        var freshActor = await Login(healthy, h);
        var recovered = await ReadRestrictedStockOriginal(healthy, freshActor, submission.OriginalId!.Value);
        Assert.Equal("COMMITTED", recovered.GetProperty("state").GetString());
        Assert.Equal(movement.GetProperty("TxnNo").GetString(), recovered.GetProperty("result").GetProperty("txnNo").GetString());
        using var replay = await SendRestrictedStockFinance(healthy, freshActor, submission);
        Assert.Equal(recovered.GetProperty("result").GetRawText(), (await ReadRestrictedStockResult(replay)).GetRawText());
        Assert.Equal(committed, await ReadRestrictedStockFinance(control, h.Tenant));

        await using var services = W620Services(h.Tenant);
        await W620Worker(services).ProcessOnceAsync();
        var finished = await ReadRestrictedStockFinance(control, h.Tenant);
        CheckW636Identity(committed.Effects, finished.Effects);
        CheckW636Event(finished.Effects, movement, IntegrationEventStatus.Success, 1, "Inventory.Received");
        CheckW636Voucher(finished.Effects, movement, setup.Book.Accounts["INVENTORY"], setup.Book.Accounts["GRNI"], 2.00m);
        await W620Worker(services).ProcessOnceAsync();
        Assert.Equal(finished, await ReadRestrictedStockFinance(control, h.Tenant));
        await DeviceEvidence("w642-native-ack-recovered.json", new { NativeAcknowledgementLost = true,
            Writes = writes, Recovered = recovered, Committed = committed, Finished = finished });
    }

    [CurrentPermissionPostgresFact]
    public async Task W642_restricted_stock_upgrades_exact_old_layout_idempotently()
    {
        await using var h = await Seed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var control = await OpenRestrictedStockFinanceConnection(connection);
        var setup = await InstallRestrictedStockFixture(h, control, upgrade: true);
        var before = await ReadRestrictedStockFinance(control, h.Tenant);
        RestrictedStockSubmission submission;
        await using (var old = await StartRestrictedStockFinanceApi(h, setup))
        {
            var actor = await Login(old, h);
            submission = await RestrictedStockFinanceInput(old, actor, setup.Department, "v1", stockFirst: true);
            using var result = await SendRestrictedStockFinance(old, actor, submission);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await OperationsFailure(result), StringComparison.Ordinal);
        }
        Assert.Equal(before, await ReadRestrictedStockFinance(control, h.Tenant));

        uint? installedOid = null;
        for (var i = 0; i < 2; i++)
        {
            await using (var tx = await control.BeginTransactionAsync())
            {
                await StockMovementFinanceWriterStorageV1.InstallAsync(control, tx, setup.ReadInstallation, setup.Bindings);
                await tx.CommitAsync();
            }
            await CheckRestrictedStockFinancePrivileges(control, setup.Bindings);
            await using var identity = new NpgsqlCommand("SELECT pg_catalog.to_regprocedure(@function)::oid", control);
            identity.Parameters.AddWithValue("function", StockMovementFinanceWriterStorageV1.Function);
            var oid = Assert.IsType<uint>(await identity.ExecuteScalarAsync());
            Assert.Equal(installedOid ?? oid, oid);
            installedOid = oid;
        }
        Assert.Equal(before, await ReadRestrictedStockFinance(control, h.Tenant));

        await using var adopted = await StartRestrictedStockFinanceApi(h, setup);
        var freshActor = await Login(adopted, h);
        using var applied = await SendRestrictedStockFinance(adopted, freshActor, submission);
        var receipt = await ReadRestrictedStockResult(applied);
        var final = await ReadRestrictedStockFinance(control, h.Tenant);
        var movement = Assert.Single(final.Effects.Rows("stock-moves"));
        var evt = CheckW636Event(final.Effects, movement, IntegrationEventStatus.Success, 1, "Inventory.Received");
        CheckRestrictedRawPayload(evt, movement, submission.Command);
        CheckW636Voucher(final.Effects, movement, setup.Book.Accounts["INVENTORY"], setup.Book.Accounts["GRNI"], 2.00m);
        var original = await ReadRestrictedStockOriginal(adopted, freshActor, submission.OriginalId!.Value);
        Assert.Equal(receipt.GetRawText(), original.GetProperty("result").GetRawText());
        await DeviceEvidence("w642-existing-layout-upgrade.json", new { setup.ReadInstallation,
            InstalledFunctionOid = installedOid, Before = before, Original = original, After = final });
    }

    private static async Task<RestrictedStockFinanceFixture> InstallRestrictedStockFixture(Harness h, NpgsqlConnection control,
        Harness? other = null, bool upgrade = false)
    {
        await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        var book = await SeedW636Book(h);
        var existing = await h.Db.Stocks.AsNoTracking().SingleAsync(row => row.ProductCd == "P1");
        var mapping = await MappingAdmin(h);
        await BindStandalone(h, mapping, "stock", existing.Id);
        await StandaloneScope(h, mapping);
        if (other is not null)
            await SeedInventory(other);

        var credentials = new List<RestrictedStockLogin> { NewLogin(h.Tenant) };
        if (other is not null)
            credentials.Add(NewLogin(other.Tenant));
        foreach (var credential in credentials)
            await CurrentPermissionPostgresFixture.Sql(control, $"CREATE ROLE {credential.Login} LOGIN PASSWORD '{credential.Password}'");
        var logins = credentials.Select(value => new TenantLoginV1(value.Tenant, value.Login)).ToArray();

        await using var install = await control.BeginTransactionAsync();
        var read = await StockReadStorageV1.InstallAsync(control, install, logins);
        await StockWriteAuthorityFenceV1.InstallAsync(control, install, read, logins);
        await StockOriginalJournalStorageV1.InstallAsync(control, install, read, logins);
        await StockOutboxWriterStorageV1.InstallAsync(control, install, read, logins);
        if (upgrade)
            await StockMovementWriterStorageV1.InstallMovementLayoutAsync(control, install, read, logins);
        else
            await StockMovementWriterStorageV1.InstallAsync(control, install, read, logins);
        await StockAdjustOwnerStorageV1.InstallAsync(control, install, read, logins);
        await StockCreationWriterStorageV2.InstallAsync(control, install, read, logins);
        await install.CommitAsync();
        return new(read, credentials.ToArray(), logins, mapping.Child, book);

        static RestrictedStockLogin NewLogin(Guid tenant) => new(tenant, $"stock_fin_{Guid.NewGuid():N}", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    private static Task<AuthenticationApiProcess> StartRestrictedStockFinanceApi(Harness h, RestrictedStockFinanceFixture setup,
        string? forwardedConnection = null)
    {
        var settings = StockReadSettings();
        foreach (var (key, value) in IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
            settings[key] = value;
        settings["StockReadTenancy__EnableV1Adjust"] = "true";
        settings["StockReadTenancy__EnableV2Adjust"] = "true";
        settings["StockFinBridge__Enabled"] = "true";
        settings["IntegrationEvent__Enabled"] = "false";
        for (var i = 0; i < setup.Logins.Length; i++)
        {
            var login = setup.Logins[i];
            var prefix = $"StockReadTenancy__Tenants__{i}__";
            settings[prefix + "TenantId"] = login.Tenant.ToString("D");
            settings[prefix + "Login"] = login.Login;
            settings[prefix + "Password"] = login.Password;
            settings[prefix + "CredentialVersion"] = "test-v1";
        }
        if (Environment.GetEnvironmentVariable("CP6_STOCK_FINANCE_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(forwardedConnection ?? h.Db.Database.GetConnectionString()!, settings);
    }

    private static async Task<RestrictedStockSubmission> RestrictedStockFinanceInput(AuthenticationApiProcess host,
        Dictionary<string, string> actor, Guid department, string entry, bool stockFirst)
    {
        if (entry == "legacy")
        {
            var scrap = InventoryAdjustment();
            scrap.RelatedType = "SCRAP";
            scrap.Qty = -2.5m;
            scrap.UnitPrice = 3m;
            return new(RestrictedStockPath + "apply", scrap, null);
        }
        if (entry == "v1")
        {
            var stock = new StockApplyCommandV1
            {
                OperationId = Guid.NewGuid(), TxnType = WmsTxnType.IN, RelatedType = "INBOUND",
                WarehouseCd = "W01", LocationCd = "L1", ProductCd = "P1", LotNo = "LOT",
                Qty = RestrictedExactQuantity, UnitPrice = RestrictedExactPrice,
            };
            return new(RestrictedStockPath + "commands/v1/apply", stock, stock.OperationId);
        }

        var product = stockFirst ? "W642-V2-A" : "W642-V2-B";
        using var creation = await host.Send(OperationsRequest(actor, HttpMethod.Get,
            RestrictedStockPath + $"creation-context/adjust?warehouseCd=W01&locationCd=L3&productCd={product}&lotNo=LOT"));
        var scope = await NumberHttpData<StockCreationContextV2>(creation);
        Assert.True(scope.RequiresCreation);
        Assert.Contains(scope.Departments, value => value.Id == department);
        var created = new StockApplyCommandV2
        {
            OperationId = Guid.NewGuid(), TxnType = WmsTxnType.IN, RelatedType = " \tInBoUnD\t ",
            WarehouseCd = "W01", LocationCd = "L3", ProductCd = product, LotNo = "LOT",
            Qty = 3m, UnitPrice = null, NewStock = new StockCreationIntentV2(scope.AuthorityVersion, department),
        };
        return new(RestrictedStockPath + "commands/v2/apply", created, created.OperationId);
    }

    private static Task<HttpResponseMessage> SendRestrictedStockFinance(AuthenticationApiProcess host, Dictionary<string, string> actor,
        RestrictedStockSubmission input) => host.Send(OperationsRequest(actor, HttpMethod.Post, input.Path, input.Command));

    private static async Task<JsonElement> ReadRestrictedStockOriginal(AuthenticationApiProcess host, Dictionary<string, string> actor, Guid id)
    {
        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Get, RestrictedStockPath + $"operations/v1/{id:D}"));
        return await ReadRestrictedStockResult(response);
    }

    private static async Task<JsonElement> ReadRestrictedStockResult(HttpResponseMessage response, string message = "OK")
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task CheckRestrictedStockFinancePrivileges(NpgsqlConnection connection, TenantLoginV1[] logins)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(*)=cardinality(@logins) AND bool_and(
                pg_catalog.has_function_privilege(oid, pg_catalog.to_regprocedure(@function), 'EXECUTE')
                AND NOT pg_catalog.has_table_privilege(oid, 'public."T_IntegrationEvent"',
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                AND NOT pg_catalog.has_any_column_privilege(oid, 'public."T_IntegrationEvent"', 'SELECT,INSERT,UPDATE,REFERENCES'))
            FROM pg_catalog.pg_roles WHERE rolname::text=ANY(@logins)
            """, connection);
        command.Parameters.AddWithValue("logins", logins.Select(row => row.Login).ToArray());
        command.Parameters.AddWithValue("function", StockMovementFinanceWriterStorageV1.Function);
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    private static async Task CheckRestrictedStockIsolation(string connection, RestrictedStockLogin other, Guid movement)
    {
        var ordinary = new NpgsqlConnectionStringBuilder(connection)
        { Username = other.Login, Password = other.Password, SearchPath = "pg_catalog", Pooling = false, Enlist = false };
        await using var session = await OpenRestrictedStockFinanceConnection(ordinary.ConnectionString);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM public."T_StockTransaction" WHERE "Id"=@movement
            """, session);
        command.Parameters.AddWithValue("movement", movement);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    private static void CheckRestrictedRawPayload(JsonElement evt, JsonElement movement, StockMovementRequest source)
    {
        using var payload = JsonDocument.Parse(evt.GetProperty("PayloadJson").GetString()!);
        var value = payload.RootElement;
        Assert.Equal(1, value.GetProperty("version").GetInt32());
        Assert.Equal(AssetId(movement), value.GetProperty("stockTransactionId").GetGuid());
        Assert.Equal(source.RelatedType, value.GetProperty("relatedType").GetString());
        Assert.Equal(source.Qty, value.GetProperty("Qty").GetDecimal());
        Assert.Equal(Math.Round(source.Qty, 8, MidpointRounding.AwayFromZero), AssetDecimal(movement, "Qty"));
        if (source.UnitPrice is decimal price)
        {
            Assert.Equal(price, value.GetProperty("UnitPrice").GetDecimal());
            Assert.Equal(Math.Round(price, 4, MidpointRounding.AwayFromZero), AssetDecimal(movement, "UnitPrice"));
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, value.GetProperty("UnitPrice").ValueKind);
            Assert.Equal(JsonValueKind.Null, movement.GetProperty("UnitPrice").ValueKind);
        }
    }

    private static async Task<NpgsqlConnection> OpenRestrictedStockFinanceConnection(string connection)
    {
        var opened = new NpgsqlConnection(connection);
        await opened.OpenAsync();
        return opened;
    }

    private static async Task<RestrictedStockSnapshot> ReadRestrictedStockFinance(NpgsqlConnection observer, Guid tenant)
    {
        var effects = await ReadW636(observer, tenant);
        await using var command = new NpgsqlCommand("""
            SELECT
                ARRAY(SELECT to_jsonb(o) FROM cp6_main_owner_v1.operations o
                    WHERE o.tenant_id=@tenant AND o.owner='Main.Wms.StockMovement' ORDER BY o.operation_id)::text,
                ARRAY(SELECT to_jsonb(s) FROM cp6_stock_outbox_v1."Source" s
                    WHERE s."TenantId"=@tenant ORDER BY s."NativeId")::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var rows = await command.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        return new(effects, rows.GetString(0), rows.GetString(1));
    }

    private static async Task<RestrictedStockXmin[]> ReadRestrictedStockTransactionIds(NpgsqlConnection observer, Guid tenant,
        Guid movement, Guid? operation)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'movement', t.xmin::text FROM public."T_StockTransaction" t
                WHERE t."TenantId"=@tenant AND t."Id"=@movement
            UNION ALL SELECT 'event', e.xmin::text FROM public."T_IntegrationEvent" e
                WHERE e."TenantId"=@tenant AND e."Id"=@movement
            UNION ALL SELECT 'stock', s.xmin::text FROM public."T_Stock" s
                JOIN public."T_StockTransaction" t ON t."TenantId"=s."TenantId" AND t."WarehouseCd"=s."WarehouseCd"
                    AND t."LocationCd"=s."LocationCd" AND t."ProductCd"=s."ProductCd" AND t."LotNo"=s."LotNo"
                WHERE s."TenantId"=@tenant AND t."Id"=@movement
            UNION ALL SELECT 'original', o.xmin::text FROM cp6_main_owner_v1.operations o
                WHERE o.tenant_id=@tenant AND o.owner='Main.Wms.StockMovement' AND o.operation_id=@operation
            UNION ALL SELECT 'outbox', m.xmin::text FROM cp6_stock_outbox_v1."Source" s
                JOIN cp6_stock_outbox_v1."Cp6_OutboxMessage" m ON m."Id"=s."NativeId" AND m."TenantId"=s."TenantId"
                WHERE s."TenantId"=@tenant AND s."OperationId"=@operation
            ORDER BY 1
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("movement", movement);
        command.Parameters.Add(new NpgsqlParameter("operation", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)operation ?? DBNull.Value });
        await using var rows = await command.ExecuteReaderAsync();
        var writes = new List<RestrictedStockXmin>();
        while (await rows.ReadAsync())
            writes.Add(new(rows.GetString(0), rows.GetString(1)));
        return writes.ToArray();
    }

    private sealed record RestrictedStockSubmission(string Path, StockMovementRequest Command, Guid? OriginalId);
    private sealed record RestrictedStockLogin(Guid Tenant, string Login, string Password);
    private sealed record RestrictedStockFinanceFixture(Guid ReadInstallation, RestrictedStockLogin[] Logins,
        TenantLoginV1[] Bindings, Guid Department, W636Book Book);
    private sealed record RestrictedStockSnapshot(AssetRows Effects, string Originals, string Outbox);
    private sealed record RestrictedStockXmin(string Kind, string Xmin);
}
