using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Sales_source_synthetic_history_reads_exact_originals_and_preserves_unproven_coverage()
    {
        await using var h = await SeedWithProduct("R4-SAME");
        await GrantOrderReads(h);
        var native = await h.Db.Orders.AsNoTracking().SingleAsync(o => o.WebOrderNo == h.Number);
        var lines = await h.Db.OrderDetails.OrderBy(d => d.WebOrderDetailNo).ToArrayAsync();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal("R4-SAME", line.ProductCd.TrimEnd()));
        lines[0].CustomerDeliveryDate = new(2026, 12, 3);
        lines[1].CustomerDeliveryDate = new(2026, 12, 4);
        lines[0].SalesAvailable = "A";
        lines[1].SalesAvailable = "B";
        await h.Db.SaveChangesAsync();
        var included = new HistoricalLine(lines[0].Id, Convert.ToBase64String(lines[0].RowVersion!));
        var excluded = new HistoricalLine(lines[1].Id, Convert.ToBase64String(lines[1].RowVersion!));
        var nativeOrderVersion = Convert.ToBase64String(native.RowVersion!);
        var scope = new OrderSourceScope(native.Id, included.Id, "r4-fixture-commercial-1", "history-schedule-1");
        var lineage = "history-" + included.Id.ToString("N");
        var schedule = JsonSerializer.SerializeToUtf8Bytes(new
        {
            scheduleId = scope.ScheduleId, orderLineId = scope.LineId, quantity = TestQuantity(),
            requiredDate = TestDate(), promisedDate = (object?)null, supplyTarget = TestTarget()
        });
        var confirmation = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fixture = "synthetic-history", tenantId = h.Tenant, id = "history-confirmation",
            order = SalesOrderRef(scope), scheduleSha256 = SalesDigest(schedule)
        });
        var releaseBody = SalesBody(scope, lineage, "history-release", 1);
        var releaseEnvelope = SalesEnvelope(h.Tenant, lineage, "history-release-original", releaseBody);
        var store = new OrderSourceParticipant(h.Db);
        await using var peer = SalesContext(h.Tenant);
        var empty = await ReadSalesPersistence(peer);
        AssertSalesCounts(empty, legacy: 0, confirmations: 0, operations: 0, frontier: null);

        OrderSourceReceipt rolledBackReceipt;
        SalesPersistence inTransaction;
        await using (var staged = await h.Db.Database.BeginTransactionAsync())
        {
            rolledBackReceipt = await EnlistReleaseAfterSyntheticConfirm(staged.GetDbTransaction());
            inTransaction = await ReadSalesPersistence(h.Db);
            AssertSalesCounts(inTransaction, legacy: 1, confirmations: 1, operations: 1, frontier: 1);
            Assert.Equal(empty, await ReadSalesPersistence(peer));
            await staged.RollbackAsync();
        }

        var afterRollback = await ReadSalesPersistence(peer);
        Assert.Equal(empty, afterRollback);
        var reader = SalesReader(h);
        Assert.Equal("UNPROVEN", (await reader.ReadScheduleAsync(scope)).Knowledge);
        var beforeRelease = await reader.ReadResultAsync(native.Id, included.Id, SalesProducer, "history-release");
        Assert.Equal("UNKNOWN", beforeRelease.Knowledge);

        OrderSourceReceipt receipt;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            receipt = await EnlistReleaseAfterSyntheticConfirm(tx.GetDbTransaction());
            Assert.Equal(rolledBackReceipt, receipt);
            await tx.CommitAsync();
        }

        var beforeReads = await ReadSalesPersistence(peer);
        AssertSalesCounts(beforeReads, legacy: 1, confirmations: 1, operations: 1, frontier: 1);
        var original = Assert.IsType<OrderSourceFacts.Original>(
            await OrderSourceFacts.ReadOriginal(peer, native.Id, 1, default));
        using var releaseInput = JsonDocument.Parse(releaseBody);
        var originalOperation = releaseInput.RootElement;
        var mapping = new SyntheticHistoryMap(h.Tenant, native.Id, nativeOrderVersion, original.Revision,
            SalesDigest(original.Body), included, excluded, scope, SalesDigest(schedule), SalesDigest(confirmation),
            lineage, 1, originalOperation.GetProperty("salesReleaseId").GetString()!,
            originalOperation.GetProperty("salesReleaseLineId").GetString()!,
            originalOperation.GetProperty("sourceDigest").GetString()!, SalesProducer, "history-release",
            new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero), new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
        var mappingBody = JsonSerializer.SerializeToUtf8Bytes(mapping);
        var resolved = JsonSerializer.Deserialize<SyntheticHistoryMap>(mappingBody)!;
        Assert.Equal(mapping, resolved);
        var validAt = resolved.ValidFromUtc;
        string Coverage(HistoricalLine line, DateTimeOffset at) =>
            FixtureHistoryCoverage(resolved, h.Tenant, native.Id, nativeOrderVersion, original.Revision, line, at);
        var coverageBeforeStart = Coverage(included, validAt.AddTicks(-1));
        var mappedCoverage = Coverage(included, validAt);
        var excludedCoverage = Coverage(excluded, validAt);
        var atExpiry = Coverage(included, resolved.ValidUntilUtc);
        Assert.Equal("UNPROVEN", coverageBeforeStart);
        Assert.Equal("MAPPED", mappedCoverage);
        Assert.Equal("UNPROVEN", excludedCoverage);
        Assert.Equal("UNPROVEN", atExpiry);

        var legacy = await reader.ReadLegacyAsync(resolved.OrderId, resolved.LegacyRevision);
        Assert.Equal("ORIGINAL_FOUND", legacy.OriginalCoverage);
        Assert.Equal("UNPROVEN", legacy.ScheduleCoverage);
        Assert.Equal(resolved.LegacySha256, legacy.OriginalDigest);
        Assert.Equal(resolved.LegacyRevision, legacy.OriginalRevision);
        var old = Assert.IsType<LegacyOrderSourceSnapshot>(legacy.Original);
        Assert.Equal(resolved.OrderId, old.Id);
        Assert.Equal(resolved.NativeOrderVersion, old.NativeVersion);
        Assert.Equal(nativeOrderVersion, legacy.Current.NativeVersion);
        Assert.Equal(new[] { included, excluded }, old.Lines.Select(l => new HistoricalLine(l.Id, l.NativeVersion!)));
        Assert.Equal(new[] { included, excluded }, legacy.Current.Lines.Select(l => new HistoricalLine(l.Id, l.NativeVersion!)));
        Assert.Equal(old.Lines[0].ProductCode, old.Lines[1].ProductCode);
        Assert.NotEqual(old.Lines[0].Id, old.Lines[1].Id);
        Assert.Equal(new DateTime(2026, 12, 4), old.Lines[1].CustomerDeliveryDate);
        Assert.Equal("B", old.Lines[1].SalesAvailable);

        var exactSchedule = await reader.ReadScheduleAsync(resolved.Scope);
        Assert.Equal("ORIGINAL_FOUND", exactSchedule.Knowledge);
        Assert.Equal(resolved.ScheduleSha256, exactSchedule.ScheduleSha256);
        Assert.Equal(resolved.ConfirmationSha256, exactSchedule.ConfirmationSha256);
        Assert.Equal(resolved.LegacyRevision, exactSchedule.OriginalOrderRevision);
        Assert.Equal(schedule, JsonSerializer.SerializeToUtf8Bytes(exactSchedule.OriginalSchedule!.Value));
        var storedConfirmation = Assert.IsType<OrderSourceParticipant.Confirmed>(
            await new OrderSourceParticipant(peer).ConfirmedScope(resolved.Scope, default));
        Assert.Equal(resolved.NativeOrderVersion, storedConfirmation.NativeOrderVersion);
        Assert.Equal(resolved.Included.NativeVersion, storedConfirmation.NativeLineVersion);
        Assert.Equal(confirmation, storedConfirmation.Proof);
        Assert.Equal(schedule, storedConfirmation.Schedule);

        var originalResult = await reader.ReadResultAsync(resolved.OrderId, included.Id, resolved.Producer, resolved.OperationKey);
        Assert.Equal("FOUND", originalResult.Knowledge);
        Assert.Equal(receipt, originalResult.Original);
        Assert.Equal("LOCAL_REQUEST", originalResult.Original!.State);
        Assert.Equal(resolved.Lineage, receipt.LineageId);
        Assert.Equal(resolved.SourceVersion, receipt.SourceVersion);
        Assert.Equal(resolved.ReleaseId, receipt.ReleaseId);
        Assert.Equal(resolved.ReleaseLineId, receipt.ReleaseLineId);
        Assert.Equal(resolved.SourceDigest, receipt.SourceDigest);
        Assert.Equal("CURRENT", originalResult.Current!.State);
        var kept = Assert.IsType<OrderSourceParticipant.Saved>(
            await new OrderSourceParticipant(peer).ReadOperation(resolved.Producer, resolved.OperationKey, default));
        Assert.Equal(receipt, kept.Receipt);
        Assert.Equal(releaseBody, kept.Body);
        Assert.Equal(releaseEnvelope, kept.Envelope);

        var excludedScope = resolved.Scope with { LineId = excluded.Id };
        var unprovenSchedule = await reader.ReadScheduleAsync(excludedScope);
        Assert.Equal("UNPROVEN", unprovenSchedule.Knowledge);
        Assert.Null(unprovenSchedule.OriginalSchedule);
        var unprovenSource = await ReadExcludedSource();
        Assert.Equal("UNPROVEN", unprovenSource.State);
        var unmatched = await reader.ReadResultAsync(resolved.OrderId, excluded.Id, resolved.Producer, resolved.OperationKey);
        var unseen = await reader.ReadResultAsync(resolved.OrderId, included.Id, resolved.Producer, "unobserved-history");
        Assert.Equal(new OrderSourceResultRead("UNKNOWN", null, null), unmatched);
        Assert.Equal(new OrderSourceResultRead("UNKNOWN", null, null), unseen);

        var historicalAfterExpiry = await reader.ReadResultAsync(resolved.OrderId, included.Id, resolved.Producer, resolved.OperationKey);
        Assert.Equal(receipt, historicalAfterExpiry.Original);
        var scheduleAfterExpiry = await reader.ReadScheduleAsync(resolved.Scope);
        Assert.Equal(exactSchedule.ScheduleSha256, scheduleAfterExpiry.ScheduleSha256);
        Assert.Equal("ORIGINAL_FOUND", scheduleAfterExpiry.Knowledge);
        var afterReads = await ReadSalesPersistence(peer);
        Assert.Equal(beforeReads, afterReads);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "r4-synthetic-history", boundary = "FIXTURE_ONLY", mappingBody,
            mappingSha256 = SalesDigest(mappingBody), coverageBeforeStart, mappedCoverage, excludedCoverage, atExpiry,
            original = original.Body, original.Digest,
            schedule = storedConfirmation.Schedule, confirmation = storedConfirmation.Proof, originalResult,
            release = new { savedBody = kept.Body, savedEnvelope = kept.Envelope, savedReceipt = kept.Result },
            legacy.OriginalCoverage, legacy.ScheduleCoverage, exactSchedule, unprovenSchedule, unprovenSource,
            unmatched, unseen, historicalAfterExpiry = new { historicalAfterExpiry.Knowledge, historicalAfterExpiry.Original },
            scheduleAfterExpiry,
            persistence = new { empty, inTransaction, afterRollback, beforeReads, afterReads }
        }));

        async Task<OrderSourceReceipt> EnlistReleaseAfterSyntheticConfirm(DbTransaction tx)
        {
            await store.EnlistConfirm(tx, h.User, "history-confirmation", scope, nativeOrderVersion,
                included.NativeVersion, schedule, confirmation);
            return await store.EnlistRelease(tx, h.User, SalesProducer, scope, releaseBody, releaseEnvelope);
        }

        async Task<OrderSourceGate> ReadExcludedSource()
        {
            await using var tx = await h.Db.Database.BeginTransactionAsync();
            var result = await store.CheckCurrentSource(tx.GetDbTransaction(), excludedScope,
                resolved.Lineage, resolved.SourceVersion, resolved.SourceDigest);
            await tx.CommitAsync();
            return result;
        }
    }

    private sealed record HistoricalLine(Guid Id, string NativeVersion);

    private sealed record SyntheticHistoryMap(Guid Tenant, Guid OrderId, string NativeOrderVersion,
        long LegacyRevision, string LegacySha256, HistoricalLine Included, HistoricalLine Excluded, OrderSourceScope Scope,
        string ScheduleSha256, string ConfirmationSha256, string Lineage, long SourceVersion, string ReleaseId,
        string ReleaseLineId, string SourceDigest, string Producer, string OperationKey,
        DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);

    private static string FixtureHistoryCoverage(SyntheticHistoryMap mapping, Guid tenant, Guid orderId,
        string nativeOrderVersion, long legacyRevision, HistoricalLine line, DateTimeOffset at)
        => mapping.Tenant == tenant && mapping.OrderId == orderId && mapping.NativeOrderVersion == nativeOrderVersion
           && mapping.LegacyRevision == legacyRevision && mapping.Included == line && mapping.Scope.LineId == line.Id
           && at >= mapping.ValidFromUtc && at < mapping.ValidUntilUtc ? "MAPPED" : "UNPROVEN";

    private sealed record SalesPersistence
    {
        public long Legacy { get; init; }
        public long Confirmations { get; init; }
        public long Operations { get; init; }
        public long Outbox { get; init; }
        public long Frontiers { get; init; }
        public long? FrontierVersion { get; init; }
        public long LocalTransferDemands { get; init; }
        public string RowIdentities { get; init; } = "";
    }

    private static Task<SalesPersistence> ReadSalesPersistence(CP6Context db) =>
        db.Database.SqlQuery<SalesPersistence>($"""
            WITH rows(kind, identity, version) AS (
              SELECT 'legacy', jsonb_build_array(order_id,revision,digest,xmin::text), NULL::bigint
                FROM cp6_sales_order_source_v1.legacy_originals WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'confirmation', jsonb_build_array(operation_key,schedule_sha256,proof_sha256,xmin::text), NULL
                FROM cp6_sales_order_source_v1.confirmed_scopes WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'operation', jsonb_build_array(operation_key,source_digest,body_sha256,xmin::text), NULL
                FROM cp6_sales_order_source_v1.operations WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'outbox', jsonb_build_array(operation_key,envelope_sha256,xmin::text), NULL
                FROM cp6_sales_order_source_v1.outbox WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'frontier', jsonb_build_array(lineage_id,source_version,xmin::text), source_version
                FROM cp6_sales_order_source_v1.frontiers WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'local-transfer-demand', jsonb_build_array(budget_key,version,digest,xmin::text), NULL
                FROM cp6_transfer_local_v1.demand_versions WHERE tenant_id={db.CurrentTenantId}
              UNION ALL SELECT 'native-order', jsonb_build_array("Id",encode("RowVersion",'base64'),xmin::text), NULL
                FROM public."T_Order" WHERE "TenantId"={db.CurrentTenantId}
              UNION ALL SELECT 'native-line', jsonb_build_array("Id",encode("RowVersion",'base64'),xmin::text), NULL
                FROM public."T_OrderDetail" WHERE "TenantId"={db.CurrentTenantId}
            )
            SELECT count(*) FILTER (WHERE kind='legacy') AS "Legacy",
                   count(*) FILTER (WHERE kind='confirmation') AS "Confirmations",
                   count(*) FILTER (WHERE kind='operation') AS "Operations",
                   count(*) FILTER (WHERE kind='outbox') AS "Outbox",
                   count(*) FILTER (WHERE kind='frontier') AS "Frontiers",
                   max(version) AS "FrontierVersion",
                   count(*) FILTER (WHERE kind='local-transfer-demand') AS "LocalTransferDemands",
                   jsonb_agg(jsonb_build_array(kind,identity) ORDER BY kind,identity)::text AS "RowIdentities"
              FROM rows
            """).SingleAsync();

    private static void AssertSalesCounts(SalesPersistence value, long legacy, long confirmations, long operations,
        long? frontier)
    {
        Assert.Equal(legacy, value.Legacy);
        Assert.Equal(confirmations, value.Confirmations);
        Assert.Equal(operations, value.Operations);
        Assert.Equal(operations, value.Outbox);
        Assert.Equal(frontier.HasValue ? 1L : 0L, value.Frontiers);
        Assert.Equal(frontier, value.FrontierVersion);
        Assert.Equal(0L, value.LocalTransferDemands);
    }
}
