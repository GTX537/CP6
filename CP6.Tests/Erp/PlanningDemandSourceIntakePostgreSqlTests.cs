using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plan;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Planning_source_accepts_exact_sales_initial_release_and_replays_the_original()
    {
        await using var h = await Seed();
        var sales = new OrderSourceParticipant(h.Db);
        var release = await SeedSalesRelease(h, sales);
        var receiver = new DemandSourceIntake(h.Db);
        AssertWrites(await ReadPlanningWrites(h.Db), 0, 0, 0);

        var result = await receiver.ReceiveAsync(release.Body);
        using var receipt = JsonDocument.Parse(result);
        var historical = receipt.RootElement;
        Assert.Equal("BUSINESS_ACCEPTED", historical.GetProperty("outcome").GetString());
        Assert.Equal(release.Receipt.OperationKey, historical.GetProperty("operationKey").GetString());
        Assert.Equal(release.Receipt.SourceDigest, historical.GetProperty("sourceDigest").GetString());
        Assert.Equal("1", historical.GetProperty("sourceVersion").GetString());
        Assert.Equal("1", historical.GetProperty("demandVersion").GetString());
        Assert.Equal("2", historical.GetProperty("acceptedTarget").GetProperty("standardQuantity").GetProperty("value").GetString());

        var alias = SalesEnvelope(h.Tenant, release.Receipt.LineageId, "normal-transport-redelivery", release.Body);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var replay = await sales.EnlistRelease(tx.GetDbTransaction(), h.User, SalesProducer, release.Receipt.Scope, release.Body, alias);
            Assert.Equal(release.Receipt, replay);
            await tx.CommitAsync();
        }
        Assert.Equal(result, await receiver.ReceiveAsync(release.Body));
        var read = await receiver.ReadResultAsync(PlanningQuery(release));
        Assert.Equal("FOUND", read.Knowledge);
        Assert.Equal(historical.GetRawText(), read.Historical!.Value.GetRawText());
        Assert.Equal("EFFECTIVE", read.Current.Effectivity);
        Assert.Equal("1", read.Current.AcceptedSourceVersion);
        Assert.Equal(historical.GetProperty("demandId").GetGuid(), read.Current.DemandId);
        Assert.Equal(historical.GetProperty("acceptedTarget").GetRawText(), read.Current.Target!.Value.GetRawText());
        Assert.True(read.Current.Watermark.Complete);

        var writes = await ReadPlanningWrites(h.Db);
        AssertWrites(writes, 1, 1, 0);
        var saved = await ReadPlanningOriginal(h.Db, release.Receipt.OperationKey);
        await AssertPlanningOriginal(sales, h, release, saved, result);
        Assert.Equal(release.Receipt.Scope.OrderId, saved.OrderId);
        Assert.Equal(release.Receipt.Scope.LineId, saved.LineId);
        Assert.Equal(release.Receipt.Scope.ScheduleId, saved.ScheduleId);
        Assert.Equal(release.Receipt.Scope.OrderVersion, saved.OrderVersion);
        Assert.Equal(release.Receipt.ReleaseId, saved.ReleaseId);
        Assert.Equal(release.Receipt.ReleaseLineId, saved.ReleaseLineId);
        Assert.Equal(read.Current.DemandId, saved.DemandId);
        using var sourceBody = JsonDocument.Parse(release.Body);
        using var storedTarget = JsonDocument.Parse(saved.Target!);
        Assert.Equal(sourceBody.RootElement.GetProperty("target").GetRawText(), storedTarget.RootElement.GetRawText());
        var native = (await OrderSourceFacts.ReadNative(h.Db, saved.OrderId, default))!;
        Assert.Equal(OrderLifecycleStatus.Confirmed, native.Lifecycle);
        Assert.Equal(2m, native.Lines.Single(line => line.Id == saved.LineId).Quantity);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "H10 original and transport redelivery",
            tenant = h.Tenant, release, result = historical, read, writes, saved, native.Lifecycle }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_source_overlapping_delivery_creates_one_demand_and_one_receipt()
    {
        await using var h = await Seed();
        var release = await SeedSalesRelease(h, new OrderSourceParticipant(h.Db));
        var overlap = new PlanningConcurrentDelivery();
        await using var first = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, overlap);
        await using var next = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, overlap);

        var receipts = await Task.WhenAll(new DemandSourceIntake(first).ReceiveAsync(release.Body),
            new DemandSourceIntake(next).ReceiveAsync(release.Body));
        Assert.Equal(2, overlap.Started);
        Assert.Equal(receipts[0], receipts[1]);
        var writes = await ReadPlanningWrites(h.Db);
        AssertWrites(writes, 1, 1, 0);
        var saved = await ReadPlanningOriginal(h.Db, release.Receipt.OperationKey);
        Assert.Equal(saved.Result, receipts[0]);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "two overlapping PostgreSQL transactions",
            tenant = h.Tenant, release.Receipt, overlap.Started, writes, saved }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_source_h32_wins_before_h10_at_exact_planning_effective_time()
    {
        await using var h = await Seed();
        var boundary = new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero).AddTicks(9);
        var clock = new SalesControlClock(boundary.AddTicks(-1));
        var sales = new OrderSourceParticipant(h.Db, clock);
        var receiver = new DemandSourceIntake(h.Db, clock);
        var release = await SeedSalesRelease(h, sales);
        var stop = await StageSalesControl(h, sales, release, 2, "stop-before-planning",
            ControlFact(release.Receipt.Scope, release.Receipt.LineageId, SalesProducer, "shipping-window", 1, "BLOCK",
                ["SHIP"], ControlUtc(boundary.AddMinutes(-1))),
            ControlFact(release.Receipt.Scope, release.Receipt.LineageId, SalesProducer, "planning-window", 1, "BLOCK",
                ["PLAN"], ControlUtc(boundary)));

        var firstResult = await receiver.ReceiveAsync(stop.Body);
        var control = await receiver.ReadResultAsync(PlanningQuery(stop));
        Assert.Equal("SOURCE_CONTROL_APPLIED", control.Historical!.Value.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, control.Historical.Value.GetProperty("demandId").ValueKind);
        Assert.Equal("NONE", control.Current.Effectivity);
        Assert.Equal("2", control.Current.Watermark.Version);
        Assert.False(control.Current.Watermark.Complete);
        Assert.Equal("SOURCE_HISTORY_UNPROVEN", control.Current.Watermark.UnknownReason);
        Assert.Equal(2, control.Current.Controls.Length);
        Assert.Equal(ControlUtc(boundary), control.Current.Controls[1].GetProperty("effectiveAt").GetString());
        Assert.Equal(firstResult, await receiver.ReceiveAsync(stop.Body));
        AssertWrites(await ReadPlanningWrites(h.Db), 1, 0, 2);

        var before = await Assert.ThrowsAsync<DemandSourceIntakeException>(() => receiver.ReceiveAsync(release.Body));
        Assert.Equal("PLANNING_SOURCE_UNPROVEN", before.Message);
        var beforeRead = await receiver.ReadResultAsync(PlanningQuery(release));
        AssertUnknown(beforeRead);
        clock.Now = boundary;
        var late = await receiver.ReceiveAsync(release.Body);
        var rejected = await receiver.ReadResultAsync(PlanningQuery(release));
        Assert.Equal("BUSINESS_REJECTED", rejected.Historical!.Value.GetProperty("outcome").GetString());
        Assert.Equal("SOURCE_CONTROL_BLOCKED", rejected.Historical.Value.GetProperty("reasonCodes")[0].GetString());
        Assert.Equal(JsonValueKind.Null, rejected.Historical.Value.GetProperty("demandId").ValueKind);
        Assert.Null(rejected.Current.AcceptedSourceVersion);
        Assert.Equal("NONE", rejected.Current.Effectivity);
        Assert.Equal("2", rejected.Current.Watermark.Version);
        Assert.Equal(late, await receiver.ReceiveAsync(release.Body));
        var writes = await ReadPlanningWrites(h.Db);
        AssertWrites(writes, 2, 0, 2);

        var savedH32 = await ReadPlanningOriginal(h.Db, stop.Receipt.OperationKey);
        var savedH10 = await ReadPlanningOriginal(h.Db, release.Receipt.OperationKey);
        await AssertPlanningOriginal(sales, h, stop, savedH32, firstResult);
        await AssertPlanningOriginal(sales, h, release, savedH10, late);
        Assert.Null(savedH32.DemandId);
        Assert.Null(savedH10.DemandId);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "H32 first and late H10",
            tenant = h.Tenant, boundary, release, stop, before = beforeRead, control, rejected, writes, savedH32, savedH10 }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_source_control_retains_the_original_acceptance_with_separate_current_effectivity()
    {
        await using var h = await Seed();
        var now = new DateTimeOffset(2026, 11, 24, 14, 0, 0, TimeSpan.Zero);
        var clock = new SalesControlClock(now);
        var sales = new OrderSourceParticipant(h.Db, clock);
        var receiver = new DemandSourceIntake(h.Db, clock);
        var release = await SeedSalesRelease(h, sales);
        var original = await receiver.ReceiveAsync(release.Body);
        var before = await receiver.ReadResultAsync(PlanningQuery(release));

        var stop = await StageSalesControl(h, sales, release, 2, "stop-after-acceptance",
            ControlFact(release.Receipt.Scope, release.Receipt.LineageId, SalesProducer, "planning-hold", 1, "BLOCK",
                ["PLAN"], ControlUtc(now)));
        _ = await receiver.ReceiveAsync(stop.Body);

        var after = await receiver.ReadResultAsync(PlanningQuery(release));
        Assert.Equal("EFFECTIVE", before.Current.Effectivity);
        Assert.Equal(before.Historical!.Value.GetRawText(), after.Historical!.Value.GetRawText());
        Assert.Equal(original, await receiver.ReceiveAsync(release.Body));
        Assert.Equal(before.Current.DemandId, after.Current.DemandId);
        Assert.Equal("1", after.Current.DemandVersion);
        Assert.Equal("1", after.Current.AcceptedSourceVersion);
        Assert.Equal("2", after.Current.Watermark.Version);
        Assert.Null(after.Current.Target);
        Assert.Equal("UNKNOWN", after.Current.Effectivity);
        Assert.Equal("SOURCE_CONTROL_BLOCKED", after.Current.Watermark.UnknownReason);
        Assert.Single(after.Current.Controls);
        var writes = await ReadPlanningWrites(h.Db);
        AssertWrites(writes, 2, 1, 1);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "historic acceptance and current hold",
            tenant = h.Tenant, release.Receipt, before, after, writes }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_source_rolls_back_pending_writes_and_recovers_a_committed_result_after_confirmation_loss()
    {
        await using var h = await Seed();
        var release = await SeedSalesRelease(h, new OrderSourceParticipant(h.Db));
        var query = PlanningQuery(release);
        var before = await new DemandSourceIntake(h.Db).ReadResultAsync(query);
        AssertUnknown(before);
        var rollback = new PlanningCommitProbe(h.Tenant, async () =>
        {
            await using var separate = SalesContext(h.Tenant);
            AssertWrites(await ReadPlanningWrites(separate), 0, 0, 0);
        });
        await using (var failed = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, rollback))
            await Assert.ThrowsAsync<IOException>(() => new DemandSourceIntake(failed).ReceiveAsync(release.Body));
        Assert.False(rollback.Committed);
        Assert.NotNull(rollback.Staged);
        Assert.Equal(new long[] { 1, 1, 1, 1, 1, 1 }, rollback.Staged!.Writes);
        var afterRollback = await new DemandSourceIntake(h.Db).ReadResultAsync(query);
        AssertUnknown(afterRollback);
        AssertWrites(await ReadPlanningWrites(h.Db), 0, 0, 0);

        var lost = new PlanningCommitProbe(h.Tenant);
        await using (var sender = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, lost))
            await Assert.ThrowsAsync<IOException>(() => new DemandSourceIntake(sender).ReceiveAsync(release.Body));
        Assert.True(lost.Committed);
        Assert.NotNull(lost.Staged);
        Assert.Equal(new long[] { 1, 1, 1, 1, 1, 1 }, lost.Staged!.Writes);

        await using var reconnect = SalesContext(h.Tenant);
        var receiver = new DemandSourceIntake(reconnect);
        var recovered = await receiver.ReadResultAsync(query);
        var saved = await ReadPlanningOriginal(reconnect, query.OperationKey);
        Assert.Equal("FOUND", recovered.Knowledge);
        Assert.Equal("BUSINESS_ACCEPTED", recovered.Historical!.Value.GetProperty("outcome").GetString());
        using var storedResult = JsonDocument.Parse(saved.Result);
        Assert.Equal(storedResult.RootElement.GetRawText(), recovered.Historical.Value.GetRawText());
        Assert.Equal(saved.Result, await receiver.ReceiveAsync(release.Body));
        Assert.NotNull(recovered.Current.DemandId);
        Assert.Equal("1", recovered.Current.DemandVersion);
        Assert.Equal("EFFECTIVE", recovered.Current.Effectivity);
        Assert.Equal(lost.Staged.Transaction, saved.JournalTransaction);
        Assert.Equal(lost.Staged.Transaction, saved.InboxTransaction);
        Assert.NotEqual(rollback.Staged.Transaction, lost.Staged.Transaction);
        var writes = await ReadPlanningWrites(reconnect);
        AssertWrites(writes, 1, 1, 0);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "rollback and committed response loss",
            tenant = h.Tenant, release.Receipt, before, rolledBack = rollback.Staged, afterRollback,
            committed = lost.Staged, recovered, writes, saved }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_source_keeps_draft_and_confirm_only_orders_out_of_demand()
    {
        await using var h = await Seed();
        var order = await h.Db.Orders.SingleAsync(x => x.WebOrderNo == h.Number);
        order.OrderStatus = "DRAFT"; // Synthetic draft state before formal Sales confirmation.
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var native = (await OrderSourceFacts.ReadNative(h.Db, order.Id, default))!;
        var lineage = "planning-candidate-" + order.Id.ToString("N");
        var scope = new OrderSourceScope(order.Id, native.Lines[0].Id, "synthetic-order-version", "synthetic-schedule");
        var key = "planning-expected-release";
        var body = SalesBody(scope, lineage, key, 1);
        using var proposed = JsonDocument.Parse(body);
        var query = new DemandSourceIntake.SourceResultQuery(key, lineage, "1", proposed.RootElement.GetProperty("sourceDigest").GetString()!);
        var receiver = new DemandSourceIntake(h.Db);
        var draft = await receiver.ReadResultAsync(query);
        AssertUnknown(draft);
        AssertWrites(await ReadPlanningWrites(h.Db), 0, 0, 0);

        order = await h.Db.Orders.SingleAsync(x => x.Id == order.Id);
        order.OrderStatus = OrderLifecycleStatus.Confirmed;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        native = (await OrderSourceFacts.ReadNative(h.Db, order.Id, default))!;
        var confirmation = JsonSerializer.SerializeToUtf8Bytes(new { id = "planning-synthetic-confirm", order = SalesOrderRef(scope) });
        var schedule = JsonSerializer.SerializeToUtf8Bytes(new { scheduleId = scope.ScheduleId, orderLineId = scope.LineId,
            quantity = TestQuantity(), requiredDate = TestDate(), promisedDate = (object?)null, supplyTarget = TestTarget() });
        var sales = new OrderSourceParticipant(h.Db);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await sales.EnlistConfirm(tx.GetDbTransaction(), h.User, "planning-confirmation", scope,
                native.NativeVersion!, native.Lines[0].NativeVersion!, schedule, confirmation);
            await tx.CommitAsync();
        }
        var confirmed = await sales.ConfirmedScope(scope, default);
        Assert.NotNull(confirmed);
        Assert.Equal(confirmation, confirmed.Proof);
        Assert.Equal(schedule, confirmed.Schedule);
        Assert.Null(await sales.ReadOperation(SalesProducer, key, default));
        var pending = await Assert.ThrowsAsync<DemandSourceIntakeException>(() => receiver.ReceiveAsync(body));
        Assert.Equal("PLANNING_SOURCE_UNPROVEN", pending.Message);
        var missing = await receiver.ReadResultAsync(query);
        AssertUnknown(missing);
        var writes = await ReadPlanningWrites(h.Db);
        AssertWrites(writes, 0, 0, 0);
        output.WriteLine(JsonSerializer.Serialize(new { caseName = "draft and confirmed without Sales release",
            tenant = h.Tenant, scope, query, draft, native.Lifecycle, confirmed, pending = pending.Message, missing, writes }));
    }

    private static DemandSourceIntake.SourceResultQuery PlanningQuery(SalesEvent source) =>
        new(source.Receipt.OperationKey, source.Receipt.LineageId,
            source.Receipt.SourceVersion.ToString(CultureInfo.InvariantCulture), source.Receipt.SourceDigest);

    private static void AssertUnknown(DemandSourceIntake.SourceResultRead read)
    {
        Assert.Equal("NOT_OBSERVED", read.Knowledge);
        Assert.Equal("UNKNOWN", read.Current.Effectivity);
        Assert.False(read.Current.Watermark.Complete);
        Assert.False(read.NoEffectProven);
        Assert.Null(read.Historical);
        Assert.Null(read.Finality);
    }

    private sealed record PlanningWrites(long Lineages, long Inbox, long Demands, long Versions, long Bindings, long Controls, long Journal);

    private static void AssertWrites(PlanningWrites writes, long inbox, long demand, long controls) =>
        Assert.Equal(new PlanningWrites(inbox == 0 ? 0 : 1, inbox, demand, demand, demand, controls, inbox), writes);

    private static Task<PlanningWrites> ReadPlanningWrites(CP6Context db) =>
        db.Database.SqlQuery<PlanningWrites>($"""
            SELECT (SELECT count(*) FROM cp6_planning_demand_source_v1.lineages WHERE tenant_id={db.CurrentTenantId}) AS "Lineages",
                (SELECT count(*) FROM cp6_planning_demand_source_v1.inbox WHERE tenant_id={db.CurrentTenantId}) AS "Inbox",
                (SELECT count(*) FROM cp6_planning_demand_source_v1.demands WHERE tenant_id={db.CurrentTenantId}) AS "Demands",
                (SELECT count(*) FROM cp6_planning_demand_source_v1.demand_versions WHERE tenant_id={db.CurrentTenantId}) AS "Versions",
                (SELECT count(*) FROM cp6_planning_demand_source_v1.source_bindings WHERE tenant_id={db.CurrentTenantId}) AS "Bindings",
                (SELECT count(*) FROM cp6_planning_demand_source_v1.controls WHERE tenant_id={db.CurrentTenantId}) AS "Controls",
                (SELECT count(*) FROM cp6_main_owner_v1.operations WHERE tenant_id={db.CurrentTenantId} AND owner={DemandSourceIntake.Owner}) AS "Journal"
            """).SingleAsync();

    private sealed class PlanningOriginal
    {
        public Guid Actor { get; init; }
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string ScheduleId { get; init; } = "";
        public string? OrderVersion { get; init; }
        public string? ReleaseId { get; init; }
        public string? ReleaseLineId { get; init; }
        public Guid? DemandId { get; init; }
        public byte[]? Target { get; init; }
        public long SourceVersion { get; init; }
        public string SourceDigest { get; init; } = "";
        public string BodySha256 { get; init; } = "";
        public byte[] Body { get; init; } = [];
        public byte[] Envelope { get; init; } = [];
        public byte[] SalesReceipt { get; init; } = [];
        public byte[] Result { get; init; } = [];
        public string Audit { get; init; } = "";
        public string JournalTransaction { get; init; } = "";
        public string InboxTransaction { get; init; } = "";
    }

    private static Task<PlanningOriginal> ReadPlanningOriginal(CP6Context db, string key) =>
        db.Database.SqlQuery<PlanningOriginal>($"""
            SELECT op.actor_id AS "Actor",lineage.order_id AS "OrderId",lineage.line_id AS "LineId",lineage.schedule_id AS "ScheduleId",
                binding.order_version AS "OrderVersion",binding.release_id AS "ReleaseId",binding.release_line_id AS "ReleaseLineId",
                binding.demand_id AS "DemandId",version.target AS "Target",inbox.source_version AS "SourceVersion",
                inbox.source_digest AS "SourceDigest",inbox.body_sha256 AS "BodySha256",op.input_bytes AS "Body",
                inbox.sales_envelope AS "Envelope",inbox.sales_receipt AS "SalesReceipt",op.result_bytes AS "Result",
                op.audit_json::text AS "Audit",op.xmin::text AS "JournalTransaction",inbox.xmin::text AS "InboxTransaction"
            FROM cp6_planning_demand_source_v1.inbox inbox
            JOIN cp6_main_owner_v1.operations op ON
                (op.tenant_id,op.owner,op.operation_id)=(inbox.tenant_id,inbox.result_owner,inbox.result_id)
            JOIN cp6_planning_demand_source_v1.lineages lineage ON
                (lineage.tenant_id,lineage.producer,lineage.lineage_id)=(inbox.tenant_id,inbox.producer,inbox.lineage_id)
            LEFT JOIN cp6_planning_demand_source_v1.source_bindings binding ON
                (binding.tenant_id,binding.producer,binding.lineage_id,binding.source_version)=
                (inbox.tenant_id,inbox.producer,inbox.lineage_id,inbox.source_version)
            LEFT JOIN cp6_planning_demand_source_v1.demand_versions version ON
                (version.tenant_id,version.demand_id,version.demand_version)=(binding.tenant_id,binding.demand_id,binding.demand_version)
            WHERE inbox.tenant_id={db.CurrentTenantId} AND inbox.producer={SalesProducer} AND inbox.operation_key={key}
            """).SingleAsync();

    private static async Task AssertPlanningOriginal(OrderSourceParticipant sales, Harness h, SalesEvent expected,
        PlanningOriginal saved, byte[] result)
    {
        var salesOriginal = (await sales.ReadOperation(SalesProducer, expected.Receipt.OperationKey, default))!;
        Assert.Equal(h.User, saved.Actor);
        Assert.Equal(expected.Receipt.SourceVersion, saved.SourceVersion);
        Assert.Equal(expected.Receipt.SourceDigest, saved.SourceDigest);
        Assert.Equal(SalesDigest(expected.Body), saved.BodySha256);
        Assert.Equal(expected.Body, saved.Body);
        Assert.Equal(expected.Envelope, saved.Envelope);
        Assert.Equal(salesOriginal.Result, saved.SalesReceipt);
        Assert.Equal(result, saved.Result);
        Assert.Equal(saved.JournalTransaction, saved.InboxTransaction);
        using var audit = JsonDocument.Parse(saved.Audit);
        Assert.Equal(expected.Receipt.OperationKey, audit.RootElement.GetProperty("OperationKey").GetString());
        Assert.Equal(saved.BodySha256, audit.RootElement.GetProperty("BodySha256").GetString());
    }

    private sealed class PlanningConcurrentDelivery : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int started;
        internal int Started => started;

        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref started) == 2) both.SetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed record PlanningPendingWrites(string Transaction, long[] Writes);

    private sealed class PlanningCommitProbe(Guid tenant, Func<Task>? beforeRollback = null) : DbTransactionInterceptor
    {
        internal PlanningPendingWrites? Staged { get; private set; }
        internal bool Committed { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT pg_current_xact_id_if_assigned()::text,
                    (SELECT count(*) FROM cp6_planning_demand_source_v1.lineages WHERE tenant_id=@tenant),
                    (SELECT count(*) FROM cp6_planning_demand_source_v1.inbox WHERE tenant_id=@tenant),
                    (SELECT count(*) FROM cp6_planning_demand_source_v1.demands WHERE tenant_id=@tenant),
                    (SELECT count(*) FROM cp6_planning_demand_source_v1.demand_versions WHERE tenant_id=@tenant),
                    (SELECT count(*) FROM cp6_planning_demand_source_v1.source_bindings WHERE tenant_id=@tenant),
                    (SELECT count(*) FROM cp6_main_owner_v1.operations WHERE tenant_id=@tenant AND owner='Main.Planning')
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "tenant";
            parameter.Value = tenant;
            command.Parameters.Add(parameter);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                var writes = Enumerable.Range(1, 6).Select(index => reader.GetInt64(index)).ToArray();
                Staged = new(reader.GetString(0), writes);
            }
            if (beforeRollback is null) return result;
            await beforeRollback();
            throw new IOException("Synthetic Planning write interrupted before commit.");
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Committed = true;
            throw new IOException("Synthetic Planning commit confirmation not received.");
        }
    }
}
