using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Sales_source_h32_applies_to_its_actions_and_only_from_the_exact_utc_boundary()
    {
        await using var h = await Seed();
        var effectiveAt = new DateTimeOffset(2026, 11, 24, 11, 30, 0, TimeSpan.Zero);
        var clock = new SalesControlClock(effectiveAt.AddTicks(-1));
        var store = new OrderSourceParticipant(h.Db, clock);
        var release = await SeedSalesRelease(h, store);
        var scope = release.Receipt.Scope;
        var lineage = release.Receipt.LineageId;
        var future = await StageSalesControl(h, store, release, 2, "ship-at-boundary",
            ControlFact(scope, lineage, SalesProducer, "ship-hold", 1, "BLOCK", ["SHIP"], ControlUtc(effectiveAt)));

        var before = await ReadSalesControlGates(h, store, release, future);
        Assert.Equal("STALE", before.Ship.State);
        Assert.Equal("STALE", before.Plan.State);
        Assert.Equal("FENCED", before.OriginalPublication.State);
        Assert.Equal("CURRENT", before.ControlPublication.State);

        clock.Now = effectiveAt;
        var after = await ReadSalesControlGates(h, store, release, future);
        Assert.Equal("FENCED", after.Ship.State);
        Assert.Equal("STALE", after.Plan.State);
        Assert.Equal("FENCED", after.OriginalPublication.State);

        var plan = await StageSalesControl(h, store, release, 3, "plan-hold",
            ControlFact(scope, lineage, "Planning", "planning-hold", 1, "BLOCK", ["PLAN", "CONVERT"], ControlUtc(effectiveAt)));
        var multiple = await ReadSalesControlGates(h, store, release, plan);
        Assert.Equal("FENCED", multiple.Ship.State);
        Assert.Equal("FENCED", multiple.Plan.State);
        Assert.Equal("STALE", multiple.Release.State);

        var uncertain = await StageSalesControl(h, store, release, 4, "manufacture-subtick",
            ControlFact(scope, lineage, "Engineering", "machine-window", 1, "BLOCK", ["MANUFACTURE"],
                "2026-11-24T11:30:00.000000001Z"));
        var precise = await ReadSalesControlGates(h, store, release, uncertain);
        Assert.Equal("FENCED", precise.Ship.State);
        Assert.Equal("UNPROVEN", precise.Manufacture.State);
        Assert.Equal(OrderControlEvidenceGap.EffectiveTime, precise.Manufacture.ControlGap);
        Assert.Equal("UNPROVEN", precise.ControlPublication.State);
        await AssertOriginalSalesEvents(store, release, future, plan, uncertain);
        output.WriteLine(JsonSerializer.Serialize(new { effectiveAt, before, after, multiple, precise,
            originals = new[] { release, future, plan, uncertain } }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_h32_preserves_blocking_reasons_when_release_correspondence_or_predecessor_is_unproven()
    {
        await using var h = await Seed();
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var store = new OrderSourceParticipant(h.Db, new SalesControlClock(now));
        var release = await SeedSalesRelease(h, store);
        var scope = release.Receipt.Scope;
        var lineage = release.Receipt.LineageId;
        var former = ControlUtc(now.AddMinutes(-2));
        var later = ControlUtc(now.AddMinutes(-1));
        var blocks = await StageSalesControl(h, store, release, 2, "separate-reasons",
            ControlFact(scope, lineage, SalesProducer, "shared-label", 1, "BLOCK", ["SHIP", "RELEASE"], former),
            ControlFact(scope, lineage, "QA", "quality-hold", 1, "BLOCK", ["PLAN"], former),
            ControlFact(scope, lineage, "Engineering", "ended-job", 1, "TERMINATE", ["MANUFACTURE"], former));
        var before = await ReadSalesControlGates(h, store, release, blocks);
        Assert.Equal("FENCED", before.Plan.State);
        Assert.Equal("FENCED", before.Manufacture.State);

        var differentOwner = await StageSalesControl(h, store, release, 3, "wrong-owner",
            ControlFact(scope, lineage, "QA", "shared-label", 2, "RELEASE_OWN_REASON", ["SHIP", "RELEASE"], later,
                TestEvidence(SalesProducer)));
        var ownerGate = await ReadSalesControlGates(h, store, release, differentOwner);
        Assert.Equal(OrderControlEvidenceGap.OriginalReason, ownerGate.Ship.ControlGap);
        Assert.Equal("FENCED", ownerGate.Ship.State);

        var differentReason = await StageSalesControl(h, store, release, 4, "wrong-reason",
            ControlFact(scope, lineage, SalesProducer, "another-reason", 2, "RELEASE_OWN_REASON", ["SHIP", "RELEASE"],
                later, TestEvidence(SalesProducer)));
        var reasonGate = await ReadSalesControlGates(h, store, release, differentReason);
        Assert.Equal(OrderControlEvidenceGap.OriginalReason, reasonGate.Ship.ControlGap);
        Assert.Equal("UNPROVEN", reasonGate.ControlPublication.State);

        var differentScope = await StageSalesControl(h, store, release, 5, "different-actions",
            ControlFact(scope, lineage, SalesProducer, "shared-label", 2, "RELEASE_OWN_REASON", ["SHIP"], later,
                TestEvidence(SalesProducer)));
        var scopeGate = await ReadSalesControlGates(h, store, release, differentScope);
        Assert.Equal(OrderControlEvidenceGap.OriginalReason, scopeGate.Ship.ControlGap);
        Assert.Equal("FENCED", scopeGate.Release.State);

        var exactReason = await StageSalesControl(h, store, release, 6, "unverified-predecessor",
            ControlFact(scope, lineage, SalesProducer, "shared-label", 2, "RELEASE_OWN_REASON", ["RELEASE", "SHIP"], later,
                TestEvidence(SalesProducer)));
        var exactGate = await ReadSalesControlGates(h, store, release, exactReason);
        Assert.Equal(OrderControlEvidenceGap.Predecessor, exactGate.Ship.ControlGap);
        Assert.Equal("UNPROVEN", exactGate.Ship.ControlEvidence);
        Assert.Equal("FENCED", exactGate.Ship.State);
        Assert.Equal("FENCED", exactGate.Plan.State);
        Assert.Equal("FENCED", exactGate.OriginalPublication.State);
        Assert.Equal("UNPROVEN", exactGate.ControlPublication.State);

        var terminateRelease = await StageSalesControl(h, store, release, 7, "terminate-is-final",
            ControlFact(scope, lineage, "Engineering", "ended-job", 2, "RELEASE_OWN_REASON", ["MANUFACTURE"], later,
                TestEvidence("Engineering")));
        var terminalGate = await ReadSalesControlGates(h, store, release, terminateRelease);
        Assert.Equal("FENCED", terminalGate.Manufacture.State);
        Assert.Equal(OrderControlEvidenceGap.OriginalReason, terminalGate.Manufacture.ControlGap);
        await AssertOriginalSalesEvents(store, release, blocks, differentOwner, differentReason, differentScope,
            exactReason, terminateRelease);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ownerGap = ownerGate.Ship.ControlGap.ToString(), reasonGap = reasonGate.Ship.ControlGap.ToString(),
            scopeGap = scopeGate.Ship.ControlGap.ToString(), predecessorGap = exactGate.Ship.ControlGap.ToString(),
            terminalGap = terminalGate.Manufacture.ControlGap.ToString(), exactGate, terminalGate,
            originals = new[] { release, blocks, differentOwner, differentReason, differentScope, exactReason, terminateRelease }
        }));
    }

    private sealed class SalesControlClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record SalesEvent(OrderSourceReceipt Receipt, byte[] Body, byte[] Envelope);

    private sealed record SalesControlGates(OrderSourceGate Ship, OrderSourceGate Plan, OrderSourceGate Release,
        OrderSourceGate Manufacture, OrderSourceGate OriginalPublication, OrderSourceGate ControlPublication);

    private static async Task<SalesEvent> SeedSalesRelease(Harness h, OrderSourceParticipant store, int lineOffset = 0)
    {
        var id = await h.Db.Orders.Where(o => o.WebOrderNo == h.Number).Select(o => o.Id).SingleAsync();
        var original = (await OrderSourceFacts.ReadNative(h.Db, id, default))!;
        var line = original.Lines[lineOffset];
        var scope = new OrderSourceScope(id, line.Id, "r2-synthetic-confirmed", "r2-synthetic-schedule");
        var lineage = "sales-r2-" + line.Id.ToString("N");
        var schedule = JsonSerializer.SerializeToUtf8Bytes(new
        {
            scheduleId = scope.ScheduleId, orderLineId = scope.LineId, quantity = TestQuantity(),
            requiredDate = TestDate(), promisedDate = (object?)null, supplyTarget = TestTarget()
        });
        var confirmation = JsonSerializer.SerializeToUtf8Bytes(new { id = "r2-local-test", order = SalesOrderRef(scope) });
        var body = SalesBody(scope, lineage, "r2-release", 1);
        var envelope = SalesEnvelope(h.Tenant, lineage, "r2-release", body);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await store.EnlistConfirm(tx.GetDbTransaction(), h.User, "r2-confirmation", scope, original.NativeVersion!,
            line.NativeVersion!, schedule, confirmation);
        var receipt = await store.EnlistRelease(tx.GetDbTransaction(), h.User, SalesProducer, scope, body, envelope);
        await tx.CommitAsync();
        return new(receipt, body, envelope);
    }

    private static object ControlFact(OrderSourceScope scope, string lineage, string owner, string reason, long version,
        string disposition, string[] actions, string effectiveAt, object? predecessor = null) =>
        new { controlId = owner + "-" + reason, controlVersion = version.ToString(CultureInfo.InvariantCulture),
            owner, reasonId = reason, scope = new { order = SalesOrderRef(scope), releaseLineageId = lineage, actionSet = actions },
            effectiveAt, disposition, authority = TestEvidence(owner), predecessor };

    private static string ControlUtc(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static async Task<SalesEvent> StageSalesControl(Harness h, OrderSourceParticipant store, SalesEvent release,
        int version, string key, params object[] controls)
    {
        var body = SalesBody(release.Receipt.Scope, release.Receipt.LineageId, key, version, controls);
        var envelope = SalesEnvelope(h.Tenant, release.Receipt.LineageId, key, body);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var receipt = await store.EnlistControl(tx.GetDbTransaction(), h.User, SalesProducer, release.Receipt.Scope, body, envelope);
        await tx.CommitAsync();
        return new(receipt, body, envelope);
    }

    private static async Task<SalesControlGates> ReadSalesControlGates(Harness h, OrderSourceParticipant store,
        SalesEvent release, SalesEvent current)
    {
        var original = release.Receipt;
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var transaction = tx.GetDbTransaction();
        Task<OrderSourceGate> Check(OrderSourceAction action) => store.CheckCurrentSource(transaction, original.Scope,
            original.LineageId, original.SourceVersion, original.SourceDigest, action);
        var gates = new SalesControlGates(await Check(OrderSourceAction.Ship), await Check(OrderSourceAction.Plan),
            await Check(OrderSourceAction.Release), await Check(OrderSourceAction.Manufacture),
            await store.CheckCurrentPublication(transaction, original),
            await store.CheckCurrentPublication(transaction, current.Receipt));
        await tx.CommitAsync();
        return gates;
    }

    private static async Task AssertOriginalSalesEvents(OrderSourceParticipant store, params SalesEvent[] expected)
    {
        foreach (var item in expected)
        {
            var record = (await store.ReadOperation(item.Receipt.Producer, item.Receipt.OperationKey, default))!;
            Assert.Equal(item.Receipt, record.Receipt);
            Assert.Equal(item.Body, record.Body);
            Assert.Equal(item.Envelope, record.Envelope);
        }
    }
}
