using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FixtureSourceControlContract = "SourceControl";
    private const string FixtureFreezeAuthorityContract = "CP6.Tests.SyntheticH32BlockAuthority";
    private const string FixtureReleaseAuthorityContract = "CP6.Tests.SyntheticH32ReleaseAuthority";
    private static readonly JsonSerializerOptions FixtureControlJson = new(JsonSerializerDefaults.Web);

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_synthetic_h32_rule_releases_only_the_resolved_reason_and_keeps_the_default_fence()
    {
        await RunSyntheticH32(independentReason: false);
        await RunSyntheticH32(independentReason: true);
    }

    private async Task RunSyntheticH32(bool independentReason)
    {
        await using var h = await Seed();
        await GrantOrderReads(h);
        var releaseAt = new DateTimeOffset(2026, 11, 24, 12, 30, 0, TimeSpan.Zero);
        var blockAt = releaseAt.AddMinutes(-30);
        var clock = new SalesControlClock(releaseAt.AddTicks(-1));
        var participant = new OrderSourceParticipant(h.Db, clock);
        var initial = await SeedSalesRelease(h, participant);
        var scope = initial.Receipt.Scope;
        var lineage = initial.Receipt.LineageId;
        const string reason = "customer-review";
        const string blockKey = "r4-business-freeze";
        const string releaseKey = "r4-business-resume";
        const string block = "BLOCK";
        const string release = "RELEASE_OWN_REASON";
        var controlId = SalesProducer + "-" + reason;
        var actions = new[] { "SHIP", "RELEASE" };

        var blockGrant = FixtureAuthority(h.Tenant, SalesProducer, FixtureFreezeAuthorityContract, block,
            controlId, 1, 2, reason, scope, lineage, actions, blockAt, null);
        var blockControl = FixtureControl(SalesProducer, scope, lineage, reason, 1, block, actions, blockAt, blockGrant.Reference, null);
        var originalBlock = FixtureOriginal(h.Tenant, SalesProducer, FixtureSourceControlContract, controlId, "1",
            Encoding.UTF8.GetBytes(blockControl.GetRawText()));
        var resumeGrant = FixtureAuthority(h.Tenant, SalesProducer, FixtureReleaseAuthorityContract, release,
            controlId, 2, 3, reason, scope, lineage, actions, releaseAt, originalBlock.Reference);
        var resumeControl = FixtureControl(SalesProducer, scope, lineage, reason, 2, release, actions, releaseAt,
            resumeGrant.Reference, originalBlock.Reference);
        var catalog = new List<FixtureControlOriginal> { blockGrant, originalBlock, resumeGrant };
        var controls = new List<object> { blockControl };
        if (independentReason)
        {
            const string owner = "QA";
            const string independent = "quality-review";
            var independentGrant = FixtureAuthority(h.Tenant, owner, FixtureFreezeAuthorityContract, block,
                owner + "-" + independent, 1, 2, independent, scope, lineage, ["SHIP"], blockAt, null);
            catalog.Add(independentGrant);
            controls.Add(FixtureControl(owner, scope, lineage, independent, 1, block, ["SHIP"], blockAt,
                independentGrant.Reference, null));
        }

        await using var peer = SalesContext(h.Tenant);
        var observer = new OrderSourceParticipant(peer);
        var beforeFreeze = await ReadSalesPersistence(peer);
        AssertSalesCounts(beforeFreeze, legacy: 1, confirmations: 1, operations: 1, frontier: 1);
        var frozen = await StageSalesControl(h, participant, initial, 2, blockKey, controls.ToArray());
        var beforeResume = await ReadSalesPersistence(peer);
        AssertSalesCounts(beforeResume, legacy: 1, confirmations: 1, operations: 2, frontier: 2);
        var beforeTime = await ReadSalesControlGates(h, participant, initial, frozen);
        Assert.Equal("FENCED", beforeTime.Ship.State);
        Assert.Equal("FENCED", beforeTime.Release.State);

        var body = SalesBody(scope, lineage, releaseKey, 3, [resumeControl]);
        var envelope = SalesEnvelope(h.Tenant, lineage, "r4-resume-original", body);
        OrderSourceReceipt uncommitted;
        SalesPersistence withinResume;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            uncommitted = await participant.EnlistControl(tx.GetDbTransaction(), h.User, SalesProducer, scope, body, envelope);
            withinResume = await ReadSalesPersistence(h.Db);
            AssertSalesCounts(withinResume, legacy: 1, confirmations: 1, operations: 3, frontier: 3);
            Assert.Equal(uncommitted, await participant.ReadFrontier(lineage, default));
            Assert.Equal(beforeResume, await ReadSalesPersistence(peer));
            await tx.RollbackAsync();
        }
        var afterRollback = await ReadSalesPersistence(peer);
        Assert.Equal(beforeResume, afterRollback);
        Assert.Null(await observer.ReadOperation(SalesProducer, releaseKey, default));
        var reader = SalesReader(h);
        var rolledBackResult = await reader.ReadResultAsync(scope.OrderId, scope.LineId, SalesProducer, releaseKey);
        Assert.Equal(new OrderSourceResultRead("UNKNOWN", null, null), rolledBackResult);

        OrderSourceReceipt resumed;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            resumed = await participant.EnlistControl(tx.GetDbTransaction(), h.User, SalesProducer, scope, body, envelope);
            Assert.Equal(uncommitted, resumed);
            await tx.CommitAsync();
        }
        var afterCommit = await ReadSalesPersistence(peer);
        AssertSalesCounts(afterCommit, legacy: 1, confirmations: 1, operations: 3, frontier: 3);
        Assert.Equal(resumed, await observer.ReadFrontier(lineage, default));
        var receiptWithPayload = new SalesEvent(resumed, body, envelope);
        await AssertOriginalSalesEvents(observer, initial, frozen, receiptWithPayload);
        var originals = new[]
        {
            await RequiredControlOriginal(observer, h.Tenant, initial.Receipt),
            await RequiredControlOriginal(observer, h.Tenant, frozen.Receipt),
            await RequiredControlOriginal(observer, h.Tenant, resumed)
        };

        if (independentReason)
        {
            using var frozenBody = JsonDocument.Parse(originals[1].Body);
            var other = Assert.Single(frozenBody.RootElement.GetProperty("controls").EnumerateArray(),
                control => FixtureText(control, "owner") == "QA");
            var reference = other.GetProperty("authority").Deserialize<FixtureControlRef>(FixtureControlJson);
            Assert.True(AuthorizesFixtureControl(FindFixtureOriginal(h.Tenant, catalog, reference,
                FixtureFreezeAuthorityContract), frozen.Receipt, other, null));
        }
        var resolved = ResolveFixtureRelease(h.Tenant, originals[1], originals[2], catalog);
        Assert.Equal("RESOLVED", resolved.Status);
        var proof = Assert.IsType<OrderSourceParticipant.ResolvedControlRelease>(resolved.Proof);
        var noPredecessor = ResolveFixtureRelease(h.Tenant, originals[1], originals[2], [blockGrant, resumeGrant]);
        Assert.Equal("PREDECESSOR_UNAVAILABLE", noPredecessor.Status);
        Assert.Null(noPredecessor.Proof);
        var noAuthority = ResolveFixtureRelease(h.Tenant, originals[1], originals[2], [blockGrant, originalBlock]);
        Assert.Equal("AUTHORITY_UNAVAILABLE", noAuthority.Status);
        Assert.Null(noAuthority.Proof);

        var beforeBlock = Judge(OrderSourceAction.Ship, blockAt.AddTicks(-1), [proof]);
        var atBlock = Judge(OrderSourceAction.Ship, blockAt, [proof]);
        var beforeRelease = Judge(OrderSourceAction.Ship, releaseAt.AddTicks(-1), [proof]);
        var atRelease = Judge(OrderSourceAction.Ship, releaseAt, [proof]);
        var afterRelease = Judge(OrderSourceAction.Ship, releaseAt.AddTicks(1), [proof]);
        Assert.False(beforeBlock.Blocked);
        Assert.True(atBlock.Blocked);
        Assert.True(beforeRelease.Blocked);
        Assert.Equal(independentReason, atRelease.Blocked);
        Assert.Equal(atRelease, afterRelease);
        Assert.Null(atRelease.Gap);
        var otherAction = Judge(OrderSourceAction.Release, releaseAt, [proof]);
        Assert.False(otherAction.Blocked);
        Assert.Null(otherAction.Gap);
        var publicationRule = Judge(null, releaseAt, [proof]);
        Assert.Null(publicationRule.Gap);
        Assert.NotNull(await observer.ConfirmedScope(scope, default));

        var unresolvedPredecessor = Judge(OrderSourceAction.Ship, releaseAt, ResolvedProofs(noPredecessor));
        var unresolvedAuthority = Judge(OrderSourceAction.Ship, releaseAt, ResolvedProofs(noAuthority));
        Assert.Equal(new OrderSourceParticipant.ControlAssessment(true, OrderControlEvidenceGap.Predecessor), unresolvedPredecessor);
        Assert.Equal(unresolvedPredecessor, unresolvedAuthority);

        clock.Now = releaseAt;
        var current = await ReadSalesControlGates(h, participant, initial, receiptWithPayload);
        Assert.Equal("FENCED", current.Ship.State);
        Assert.Equal("FENCED", current.Release.State);
        Assert.Equal(OrderControlEvidenceGap.Predecessor, current.Ship.ControlGap);
        Assert.Equal("FENCED", current.OriginalPublication.State);
        Assert.Equal("UNPROVEN", current.ControlPublication.State);
        Assert.Equal(OrderControlEvidenceGap.Predecessor, current.ControlPublication.ControlGap);
        var recovered = await reader.ReadResultAsync(scope.OrderId, scope.LineId, SalesProducer, releaseKey);
        Assert.Equal("FOUND", recovered.Knowledge);
        Assert.Equal(resumed, recovered.Original);
        Assert.Equal("LOCAL_REQUEST", recovered.Original!.State);
        var finalRead = await ReadSalesPersistence(peer);
        Assert.Equal(afterCommit, finalRead);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = independentReason ? "r4-h32-independent-reason" : "r4-h32-one-reason",
            proofBoundary = "INTERNAL_SALES_RULE_FROM_PG_ORIGINALS", trustBoundary = "FIXTURE_ONLY",
            h.Tenant, blockAt, releaseAt, independentReason, catalog, resolved, noPredecessor, noAuthority,
            rule = new { beforeBlock, atBlock, beforeRelease, atRelease, afterRelease, otherAction, publicationRule,
                unresolvedPredecessor, unresolvedAuthority },
            defaultGate = current, rolledBackResult, recovered,
            originalOperations = originals.Select(record => new
            {
                record.Receipt, savedBody = record.Body, savedEnvelope = record.Envelope, savedReceipt = record.Result
            }),
            persistence = new { beforeFreeze, beforeResume, withinResume, afterRollback, afterCommit, finalRead }
        }));

        OrderSourceParticipant.ControlAssessment Judge(OrderSourceAction? action, DateTimeOffset time,
            IReadOnlyList<OrderSourceParticipant.ResolvedControlRelease> proofs) =>
            OrderSourceParticipant.AssessOriginalControls(originals, resumed, scope, action, time, proofs);
    }

    private sealed record FixtureControlRef(string Owner, string ContractId, string ContractVersion,
        string EvidenceId, string EvidenceVersion, string ContentDigest);

    private sealed record FixtureControlOriginal(Guid Tenant, FixtureControlRef Reference, byte[] Body);

    private sealed record FixtureControlAuthority(string Type, Guid Tenant, string Owner, string EvidenceId,
        string EvidenceVersion, string Disposition, string ControlId, long ControlVersion, long SourceVersion,
        string ReasonId, OrderSourceScope Scope, string Lineage, string[] Actions, DateTimeOffset EffectiveAt,
        FixtureControlRef? Predecessor);

    private sealed record FixtureReleaseResolution(string Status, OrderSourceParticipant.ResolvedControlRelease? Proof);

    private static FixtureControlOriginal FixtureOriginal(Guid tenant, string owner, string contract, string id,
        string version, byte[] body) => new(tenant, new(owner, contract, "1", id, version, SalesDigest(body)), body);

    private static FixtureControlOriginal FixtureAuthority(Guid tenant, string owner, string contract, string disposition,
        string controlId, long controlVersion, long sourceVersion, string reason, OrderSourceScope scope,
        string lineage, string[] actions, DateTimeOffset effectiveAt, FixtureControlRef? predecessor)
    {
        var id = controlId + "-authority";
        var version = controlVersion.ToString(CultureInfo.InvariantCulture);
        var authority = new FixtureControlAuthority(contract, tenant, owner, id, version, disposition, controlId,
            controlVersion, sourceVersion, reason, scope, lineage, actions, effectiveAt, predecessor);
        return FixtureOriginal(tenant, owner, contract, id, version, JsonSerializer.SerializeToUtf8Bytes(authority, FixtureControlJson));
    }

    private static JsonElement FixtureControl(string owner, OrderSourceScope scope, string lineage, string reason, long version,
        string disposition, string[] actions, DateTimeOffset effectiveAt, FixtureControlRef authority, FixtureControlRef? predecessor)
        => JsonSerializer.SerializeToElement(new
        {
            controlId = owner + "-" + reason, controlVersion = version.ToString(CultureInfo.InvariantCulture),
            owner, reasonId = reason,
            scope = new { order = SalesOrderRef(scope), releaseLineageId = lineage, actionSet = actions },
            effectiveAt = ControlUtc(effectiveAt), disposition, authority, predecessor
        }, FixtureControlJson);

    private static FixtureReleaseResolution ResolveFixtureRelease(Guid tenant, OrderSourceParticipant.Saved original,
        OrderSourceParticipant.Saved release, IReadOnlyList<FixtureControlOriginal> catalog)
    {
        using var releaseBody = JsonDocument.Parse(release.Body);
        var releaseControl = Assert.Single(releaseBody.RootElement.GetProperty("controls").EnumerateArray());
        var priorRef = releaseControl.GetProperty("predecessor").Deserialize<FixtureControlRef>(FixtureControlJson);
        var authorityRef = releaseControl.GetProperty("authority").Deserialize<FixtureControlRef>(FixtureControlJson);
        var predecessor = FindFixtureOriginal(tenant, catalog, priorRef, FixtureSourceControlContract);
        if (predecessor is null) return new("PREDECESSOR_UNAVAILABLE", null);
        using var originalBody = JsonDocument.Parse(original.Body);
        var originalControl = originalBody.RootElement.GetProperty("controls").EnumerateArray().SingleOrDefault(control =>
            FixtureText(control, "controlId") == priorRef!.EvidenceId && FixtureText(control, "controlVersion") == priorRef.EvidenceVersion);
        if (originalControl.ValueKind == JsonValueKind.Undefined || FixtureText(originalControl, "disposition") != "BLOCK"
            || FixtureText(originalControl, "owner") != priorRef!.Owner
            || !predecessor.Body.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(originalControl.GetRawText())))
            return new("PREDECESSOR_UNAVAILABLE", null);

        var blockRef = originalControl.GetProperty("authority").Deserialize<FixtureControlRef>(FixtureControlJson);
        var blockAuthority = FindFixtureOriginal(tenant, catalog, blockRef, FixtureFreezeAuthorityContract);
        var releaseAuthority = FindFixtureOriginal(tenant, catalog, authorityRef, FixtureReleaseAuthorityContract);
        if (!AuthorizesFixtureControl(blockAuthority, original.Receipt, originalControl, null)
            || !AuthorizesFixtureControl(releaseAuthority, release.Receipt, releaseControl, priorRef))
            return new("AUTHORITY_UNAVAILABLE", null);
        return new("RESOLVED", new(original.Receipt.SourceVersion, originalControl.GetRawText(),
            release.Receipt.SourceVersion, releaseControl.GetRawText()));
    }

    private static FixtureControlOriginal? FindFixtureOriginal(Guid tenant, IReadOnlyList<FixtureControlOriginal> catalog,
        FixtureControlRef? reference, string contract)
    {
        if (reference is null || reference.ContractId != contract || reference.ContractVersion != "1") return null;
        var stored = catalog.SingleOrDefault(item => item.Tenant == tenant && item.Reference == reference);
        return stored is not null && SalesDigest(stored.Body) == reference.ContentDigest ? stored : null;
    }

    private static bool AuthorizesFixtureControl(FixtureControlOriginal? evidence, OrderSourceReceipt operation,
        JsonElement control, FixtureControlRef? predecessor)
    {
        if (evidence is null) return false;
        var authority = JsonSerializer.Deserialize<FixtureControlAuthority>(evidence.Body, FixtureControlJson);
        var scope = control.GetProperty("scope");
        return authority is not null && authority.Type == evidence.Reference.ContractId && authority.Tenant == evidence.Tenant
            && authority.Owner == evidence.Reference.Owner && authority.Owner == FixtureText(control, "owner")
            && authority.EvidenceId == evidence.Reference.EvidenceId && authority.EvidenceVersion == evidence.Reference.EvidenceVersion
            && authority.Predecessor == predecessor && authority.Disposition == FixtureText(control, "disposition")
            && authority.ControlId == FixtureText(control, "controlId")
            && authority.ControlVersion.ToString(CultureInfo.InvariantCulture) == FixtureText(control, "controlVersion")
            && authority.SourceVersion == operation.SourceVersion && authority.ReasonId == FixtureText(control, "reasonId")
            && authority.Scope == operation.Scope && authority.Lineage == operation.LineageId
            && authority.Lineage == FixtureText(scope, "releaseLineageId")
            && authority.Actions.ToHashSet(StringComparer.Ordinal).SetEquals(
                scope.GetProperty("actionSet").EnumerateArray().Select(action => action.GetString()!))
            && ControlUtc(authority.EffectiveAt) == FixtureText(control, "effectiveAt");
    }

    private static async Task<OrderSourceParticipant.Saved> RequiredControlOriginal(OrderSourceParticipant participant,
        Guid tenant, OrderSourceReceipt receipt)
    {
        var saved = Assert.IsType<OrderSourceParticipant.Saved>(
            await participant.ReadOperation(receipt.Producer, receipt.OperationKey, default));
        Assert.Equal(receipt, saved.Receipt);
        using var envelope = JsonDocument.Parse(saved.Envelope);
        Assert.Equal(tenant, envelope.RootElement.GetProperty("tenantid").GetGuid());
        Assert.Equal(saved.Body, Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("data").GetRawText()));
        return saved;
    }

    private static string FixtureText(JsonElement value, string property) => value.GetProperty(property).GetString()!;

    private static OrderSourceParticipant.ResolvedControlRelease[] ResolvedProofs(FixtureReleaseResolution result)
        => result.Proof is { } proof ? [proof] : [];
}
