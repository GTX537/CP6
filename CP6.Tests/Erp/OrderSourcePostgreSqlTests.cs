using System.Text.Json;
using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SalesProducer = "Main.Sales";

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_legacy_writer_preserves_native_lines_versions_and_old_facts()
    {
        await using var h = await Seed();
        await GrantOrderWrites(h);
        var reader = SalesReader(h);
        var oldId = await h.Db.Orders.Select(o => o.Id).SingleAsync();
        var old = await reader.ReadLegacyAsync(oldId);
        Assert.Equal("UNPROVEN", old.OriginalCoverage);
        Assert.Equal(2, old.Current.Lines.Count);

        var draft = NewOrder();
        draft.Details =
        [
            new() { ProductCd = "SAME", Quantity = 2m, QtyUnit = "EA", CustomerDeliveryDate = new(2026, 12, 1), SalesAvailable = "A" },
            new() { ProductCd = "SAME", Quantity = 3m, QtyUnit = "EA", CustomerDeliveryDate = new(2026, 12, 2), SalesAvailable = "B" }
        ];
        var number = await h.Service.CreateAsync(draft, null);
        var id = await h.Db.Orders.Where(o => o.WebOrderNo == number).Select(o => o.Id).SingleAsync();
        var first = await reader.ReadLegacyAsync(id);
        Assert.Equal("Order.Create", first.Writer);
        Assert.Equal(1L, first.OriginalRevision);
        var lines = first.Current.Lines;
        Assert.Equal(2, lines.Select(l => l.Id).Distinct().Count());
        Assert.All(lines, l => Assert.NotNull(l.NativeVersion));
        Assert.Equal(new DateTime(2026, 12, 2), lines[1].CustomerDeliveryDate);
        Assert.Equal("B", lines[1].SalesAvailable);
        Assert.Equal("UNPROVEN", first.ScheduleCoverage);

        var edit = (await h.Service.GetByWebOrderNoAsync(number))!;
        edit.Details[0].Quantity = 5m;
        await h.Service.UpdateAsync(number, edit, null);
        var second = await reader.ReadLegacyAsync(id);
        Assert.Equal(2L, second.OriginalRevision);
        Assert.Equal("Order.Edit", second.Writer);
        Assert.Equal(5m, second.Current.Lines[0].Quantity);
        Assert.Equal(lines[0].Id, second.Current.Lines[0].Id);
        Assert.NotEqual(lines[0].NativeVersion, second.Current.Lines[0].NativeVersion);
        Assert.Equal("UNPROVEN", second.ScheduleCoverage);

        await h.Service.DeleteAsync(number, null, null);
        var afterDelete = await reader.ReadLegacyAsync(id, first.OriginalRevision);
        Assert.True(afterDelete.Current.Deleted);
        Assert.Equal(first.OriginalDigest, afterDelete.OriginalDigest);
        Assert.Equal(2m, afterDelete.Original!.Lines[0].Quantity);

        await using var other = await Seed();
        await GrantOrderReads(other);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => SalesReader(other).ReadLegacyAsync(id))).Status);
        Assert.Equal(first.OriginalDigest, (await SalesReader(h).ReadLegacyAsync(id, 1)).OriginalDigest);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            id, lines = lines.Select(l => new { l.Id, l.NativeVersion, l.CustomerDeliveryDate, l.SalesAvailable }),
            first.OriginalDigest, afterEdit = second.OriginalDigest, afterDelete.Current.Deleted,
            old.OriginalCoverage, first.ScheduleCoverage
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_enlist_retains_exact_request_across_parallel_replay_rollback_and_control()
    {
        await using var h = await Seed();
        await GrantOrderReads(h);
        var orderId = await h.Db.Orders.Select(o => o.Id).SingleAsync();
        var native = (await OrderSourceFacts.ReadNative(h.Db, orderId, default))!;
        var line = native.Lines[0];
        var scope = new OrderSourceScope(orderId, line.Id, "confirmed-synthetic-1", "schedule-synthetic-1");
        var lineage = "sales-test-" + h.Tenant.ToString("N");
        var firstBody = SalesBody(scope, lineage, "release-original", 1);
        var firstEnvelope = SalesEnvelope(h.Tenant, lineage, "original-envelope", firstBody);
        var alias = SalesEnvelope(h.Tenant, lineage, "transport-alias", firstBody);
        var store = new OrderSourceParticipant(h.Db);
        var reader = SalesReader(h);

        var schedule = JsonSerializer.SerializeToUtf8Bytes(new
        {
            scheduleId = scope.ScheduleId,
            orderLineId = scope.LineId,
            quantity = TestQuantity(),
            requiredDate = TestDate(),
            promisedDate = (object?)null,
            supplyTarget = TestTarget()
        });
        var confirmation = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = "synthetic-confirmation", order = SalesOrderRef(scope), scheduleSha256 = SalesDigest(schedule)
        });

        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var missingProof = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                store.EnlistRelease(tx.GetDbTransaction(), h.User, SalesProducer, scope, firstBody, firstEnvelope));
            Assert.Equal("SALES_SOURCE_UNPROVEN", missingProof.Message);
            Assert.Null(await store.ReadFrontier(lineage, default));
            await store.EnlistConfirm(tx.GetDbTransaction(), h.User, "confirm-original", scope,
                native.NativeVersion!, line.NativeVersion!, schedule, confirmation);
            await store.EnlistConfirm(tx.GetDbTransaction(), h.User, "confirm-original", scope,
                native.NativeVersion!, line.NativeVersion!, schedule, confirmation);
            await tx.CommitAsync();
        }
        var exact = await reader.ReadScheduleAsync(scope);
        Assert.Equal("ORIGINAL_FOUND", exact.Knowledge);
        Assert.Equal(SalesDigest(schedule), exact.ScheduleSha256);
        Assert.Equal(SalesDigest(confirmation), exact.ConfirmationSha256);
        Assert.Equal(scope.ScheduleId, exact.OriginalSchedule!.Value.GetProperty("scheduleId").GetString());

        // 首先固定原始封装，同时在第二个本机连接重投同一个业务请求。
        OrderSourceReceipt receipt;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            receipt = await store.EnlistRelease(tx.GetDbTransaction(), h.User, SalesProducer, scope, firstBody, firstEnvelope);
            await using var peer = SalesContext(h.Tenant);
            Assert.Null(await new OrderSourceParticipant(peer).ReadOperation(SalesProducer, receipt.OperationKey, default));
            var peerPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var replay = ReplayRelease(peerPid);
            var pid = await peerPid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waited = false;
            for (var probe = 0; probe < 100 && !waited; probe++)
            {
                waited = await peer.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS "Value"
                    """).SingleAsync();
                if (!waited) await Task.Delay(25);
            }
            Assert.True(waited, "第二个 PostgreSQL 连接应等待同一来源事务。");
            await tx.CommitAsync();
            Assert.Equal(receipt, await replay.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        async Task<OrderSourceReceipt> ReplayRelease(TaskCompletionSource<int> peerPid)
        {
            await using var peer = SalesContext(h.Tenant);
            await using var tx = await peer.Database.BeginTransactionAsync();
            peerPid.SetResult(((NpgsqlConnection)peer.Database.GetDbConnection()).ProcessID);
            var replay = await new OrderSourceParticipant(peer).EnlistRelease(tx.GetDbTransaction(),
                h.User, SalesProducer, scope, firstBody, alias);
            await tx.CommitAsync();
            return replay;
        }

        Assert.Equal("CURRENT", (await reader.ReadResultAsync(orderId, line.Id, SalesProducer, receipt.OperationKey)).Current!.State);
        var saved = (await store.ReadOperation(SalesProducer, receipt.OperationKey, default))!;
        Assert.Equal(firstBody, saved.Body);
        Assert.Equal(firstEnvelope, saved.Envelope);
        Assert.Equal(SalesDigest(firstEnvelope), receipt.EnvelopeSha256);

        await h.Service.BatchUpdatePriceAsync(h.Request(), null);
        Assert.Equal("Order.PriceCorrection", (await reader.ReadLegacyAsync(orderId)).Writer);
        Assert.Equal("CURRENT", (await reader.ReadResultAsync(orderId, line.Id, SalesProducer, receipt.OperationKey)).Current!.State);

        var controlBody = SalesBody(scope, lineage, "control-original", 2);
        var controlEnvelope = SalesEnvelope(h.Tenant, lineage, "control-envelope", controlBody);
        async Task<OrderSourceReceipt> Control(bool commit)
        {
            await using var tx = await h.Db.Database.BeginTransactionAsync();
            var result = await store.EnlistControl(tx.GetDbTransaction(), h.User, SalesProducer, scope, controlBody, controlEnvelope);
            Assert.Equal(2L, (await store.ReadFrontier(lineage, default))!.SourceVersion);
            Assert.Equal("FENCED", (await store.CheckCurrentSource(tx.GetDbTransaction(), scope, lineage, 1, receipt.SourceDigest)).State);
            Assert.Equal("FENCED", (await store.CheckCurrentPublication(tx.GetDbTransaction(), receipt)).State);
            Assert.Equal("CURRENT", (await store.CheckCurrentPublication(tx.GetDbTransaction(), result)).State);
            if (commit) await tx.CommitAsync();
            else await tx.RollbackAsync();
            return result;
        }

        var rolledBack = await Control(false);
        var missing = await reader.ReadResultAsync(orderId, line.Id, SalesProducer, rolledBack.OperationKey);
        Assert.Equal("UNKNOWN", missing.Knowledge);
        Assert.Null(missing.Original);
        Assert.Equal(1L, (await store.ReadFrontier(lineage, default))!.SourceVersion);
        var applied = await Control(true);
        Assert.Equal(rolledBack, applied);
        var history = await reader.ReadResultAsync(orderId, line.Id, SalesProducer, receipt.OperationKey);
        Assert.Equal(receipt, history.Original);
        Assert.Equal("FENCED", history.Current!.State);
        Assert.Equal(2L, history.Current.CurrentSourceVersion);

        var count = await h.Db.Database.SqlQuery<long>($"""
            SELECT count(*) AS "Value" FROM cp6_sales_order_source_v1.outbox WHERE tenant_id={h.Tenant}
            """).SingleAsync();
        Assert.Equal(2L, count);
        var writes = await h.Db.Database.SqlQuery<PublicationWriteEvidence>($"""
            SELECT op.operation_key AS "OperationKey",op.xmin::text AS "SourceXid",ob.xmin::text AS "OutboxXid",
                   head.xmin::text AS "FrontierXid"
            FROM cp6_sales_order_source_v1.operations op
            JOIN cp6_sales_order_source_v1.outbox ob USING (tenant_id,producer,operation_key)
            LEFT JOIN cp6_sales_order_source_v1.frontiers head USING(tenant_id,lineage_id,source_version)
            WHERE op.tenant_id={h.Tenant}
            """).ToArrayAsync();
        Assert.Equal(2, writes.Length);
        Assert.All(writes, write => Assert.Equal(write.SourceXid, write.OutboxXid));
        var frontier = Assert.Single(writes, write => write.OperationKey == applied.OperationKey);
        Assert.Equal(frontier.SourceXid, frontier.FrontierXid);
        output.WriteLine(JsonSerializer.Serialize(new { exact, release = receipt, control = applied,
            afterRollback = missing.Knowledge, history.Current, outbox = count, writes,
            originalReleaseBody = saved.Body, originalReleaseEnvelope = saved.Envelope,
            originalControlBody = controlBody, originalControlEnvelope = controlEnvelope }));
    }

    private sealed class PublicationWriteEvidence
    {
        public string OperationKey { get; init; } = "";
        public string SourceXid { get; init; } = "";
        public string OutboxXid { get; init; } = "";
        public string? FrontierXid { get; init; }
    }

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_backorder_keeps_original_and_successor_native_identities()
    {
        await using var h = await Seed();
        await GrantOrderReads(h);
        var old = await h.Db.OrderDetails.SingleAsync(d => d.WebOrderDetailNo == 1);
        var oldLineId = old.Id;
        h.Db.ChangeTracker.Clear();
        var actions = new BackorderService(h.Db);
        var split = await actions.SplitToNewOrderAsync(h.Number, 1, new BackorderActionRequest { Reason = "synthetic split" }, "price-owner");
        var initial = await h.Db.Orders.Where(o => o.WebOrderNo == h.Number).Select(o => o.Id).SingleAsync();
        var successor = await h.Db.Orders.Where(o => o.WebOrderNo == split.NewWebOrderNo).Select(o => o.Id).SingleAsync();
        var reader = SalesReader(h);
        var original = await reader.ReadLegacyAsync(initial, 1);
        var parent = await reader.ReadLegacyAsync(initial);
        var child = await reader.ReadLegacyAsync(successor);
        Assert.Equal("Backorder.SplitParent", parent.Writer);
        Assert.Equal("Backorder.SplitSuccessor", child.Writer);
        Assert.Equal(oldLineId, original.Original!.Lines[0].Id);
        Assert.Equal(2m, parent.Current.Lines[0].BackorderQuantity);
        Assert.Equal(2m, child.Current.Lines[0].Quantity);
        Assert.NotEqual(oldLineId, child.Current.Lines[0].Id);
        Assert.Equal("UNPROVEN", child.ScheduleCoverage);
        var origin = (await OrderSourceFacts.ReadOriginal(h.Db, successor, 1, default))!.Origin;
        using var json = JsonDocument.Parse(origin);
        Assert.Equal(oldLineId, json.RootElement.GetProperty("SourceLineId").GetGuid());
        Assert.Equal(child.Current.Lines[0].Id, json.RootElement.GetProperty("SuccessorLineId").GetGuid());
        output.WriteLine(JsonSerializer.Serialize(new { initial, successor, oldLineId, original.OriginalDigest,
            successorLineId = child.Current.Lines[0].Id, child.ScheduleCoverage }));
    }

    private static OrderSourceReader SalesReader(Harness h) => new(h.Db,
        new(h.Db, new HttpContextAccessor { HttpContext = h.Http }, _ => Task.FromResult(h.User), h.Catalog.Capture));

    private CP6Context SalesContext(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });

    private static object SalesOrderRef(OrderSourceScope scope) => new { orderId = scope.OrderId,
        orderVersion = scope.OrderVersion, orderLineId = scope.LineId, scheduleId = scope.ScheduleId };

    private static string SalesDigest(byte[] bytes) => MainOwnerOperationJournalV1.Digest(bytes);

    private static object TestEvidence(string owner) => new { owner, contractId = "fixture-only", contractVersion = "1",
        evidenceId = "synthetic", evidenceVersion = "1", contentDigest = new string('0', 64) };

    private static object TestQuantity() => new { value = "2", uom = TestEvidence("UOM") };

    private static object TestDate() => new { kind = "DATE", date = "2026-12-03", instantUtc = (string?)null,
        node = "test-site", timeZone = "UTC", calendar = TestEvidence("Calendar") };

    private static object TestTarget() => new { transactionQuantity = TestQuantity(), standardQuantity = TestQuantity(),
        conversion = (object?)null, site = "test-site", requiredDate = TestDate(),
        technical = new { item = TestEvidence("Product"), revision = TestEvidence("Technical"),
            baseline = (object?)null, configuration = (object?)null, applicability = Array.Empty<object>() },
        supplyMode = "STOCK", purposeEvidence = Array.Empty<object>() };

    private static byte[] SalesBody(OrderSourceScope scope, string lineage, string key, int version)
    {
        object[] controls = version == 1 ? [] :
        [
            new { controlId = "synthetic-control", controlVersion = "1", owner = SalesProducer, reasonId = "fixture-hold",
                scope = new { order = SalesOrderRef(scope), releaseLineageId = lineage, actionSet = new[] { "SHIP" } },
                effectiveAt = "2026-11-24T09:30:00.000Z", disposition = "BLOCK", authority = TestEvidence(SalesProducer),
                predecessor = (object?)null }
        ];
        var node = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = OrderSourceParticipant.SourceContract, operationKey = key,
            kind = version == 1 ? "INITIAL_RELEASE" : "CONTROL_SOURCE", order = SalesOrderRef(scope),
            salesReleaseId = "synthetic-release", salesReleaseLineId = "synthetic-release-line", releaseLineageId = lineage,
            sourceVersion = version.ToString(CultureInfo.InvariantCulture),
            expectedAcceptedSourceVersion = (string?)null, previousIssuedSourceVersion = version == 1 ? null : "1",
            interveningFinalResults = Array.Empty<object>(), target = version == 1 ? TestTarget() : null,
            controls, approvalBindings = Array.Empty<object>(), cancelAllocation = (object?)null, activationManifest = (object?)null
        })!.AsObject();
        node["sourceDigest"] = CommercialCanonicalV1.Digest("ORD-DEM-SOURCE-1", JsonSerializer.SerializeToElement(node));
        return JsonSerializer.SerializeToUtf8Bytes(node);
    }

    private static byte[] SalesEnvelope(Guid tenant, string lineage, string id, byte[] body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("specversion", "1.0");
            json.WriteString("id", id);
            json.WriteString("source", "urn:cp6:fixture:sales");
            json.WriteString("type", "com.gtx537.fixture.sales-source.v1");
            json.WriteString("datacontenttype", "application/json");
            json.WriteString("tenantid", tenant);
            json.WriteString("aggregateid", lineage);
            json.WritePropertyName("data");
            json.WriteRawValue(body);
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
