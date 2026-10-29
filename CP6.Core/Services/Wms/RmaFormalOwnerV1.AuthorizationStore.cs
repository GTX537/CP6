using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using RangeSet = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string AuthorizationOwner = "Main.Wms.RmaAuthorization.v1", AuthorizationJournal = "Main.Wms.RmaAuthorizationCommand.v1",
        AuthorizationAuthority = "Main.Wms.RmaAuthorizationAuthority.v1", EntitlementOwner = "Main.Wms.ReturnEntitlement.v1",
        EntitlementHeadOwner = "Main.Wms.ReturnEntitlementHead.v1", ClaimOwner = "Main.Wms.ReturnRangeClaim.v1", ShareOwner = "Main.Wms.ReturnShare.v1";
    private sealed record AuthorizationAuthorityBody(Guid ApplicationId, string PermissionVersion, FulfilmentRefV1 PolicyRef, FulfilmentRefV1? DecisionHeadRef);
    private sealed record EntitlementSelection(Guid ApplicationId, Guid LineId, FulfilmentRefV1 ApplicationRef,
        RmaReturnSourceCutV1 Source, FulfilmentRefV1 HeadRef, FulfilmentRefV1 PolicyRef);
    private sealed class EntitlementHeadRow
    {
        public int Revision { get; init; }
        public string Signature { get; init; } = null!;
        public byte[] Reference { get; init; } = null!;
    }
    private sealed class AuthorizationHeadRow
    {
        public Guid Id { get; init; }
        public int Version { get; init; }
    }
    private sealed class ClaimSegmentRow
    {
        public Guid ClaimId { get; init; }
        public string Key { get; init; } = null!;
        public decimal From { get; init; }
        public decimal To { get; init; }
        public string State { get; init; } = null!;
    }
    private static string CoordinateKey(RmaSourceCoordinateV1 c) => Digest(new { c.SourceOwner,
        ShipmentOwner = c.ShipmentLineRef.Owner, ShipmentId = c.ShipmentLineRef.Id,
        InvoiceOwner = c.InvoiceLineRef?.Owner, InvoiceId = c.InvoiceLineRef?.Id,
        ScheduleOwner = c.ScheduleRef?.Owner, ScheduleId = c.ScheduleRef?.Id, c.BaseUom });
    private IQueryable<ClaimSegmentRow> ClaimSegments() => db.Database.SqlQuery<ClaimSegmentRow>($"""
        SELECT claim_id AS "ClaimId",coordinate_key AS "Key",range_from AS "From",range_to AS "To",state AS "State"
        FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={db.CurrentTenantId}
        """);
    private async Task<FulfilmentRefV1> EntitlementHead(string key, string signature, bool advance, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<EntitlementHeadRow>($"""
            SELECT revision AS "Revision",source_signature AS "Signature",reference AS "Reference"
            FROM cp6_rma_formal_v1.entitlement_heads WHERE tenant_id={db.CurrentTenantId} AND coordinate_key={key}
            """).SingleOrDefaultAsync(ct);
        if (row != null && row.Signature == signature && !advance) return JsonSerializer.Deserialize<FulfilmentRefV1>(row.Reference,RmaFormalStoreV1.Json)!;
        var version = checked((row?.Revision ?? 0) + 1);
        var reference = new FulfilmentRefV1(EntitlementHeadOwner,key,version.ToString(System.Globalization.CultureInfo.InvariantCulture),Digest(new { key, version, signature }));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.entitlement_heads(tenant_id,coordinate_key,revision,source_signature,reference)
            VALUES({db.CurrentTenantId},{key},{version},{signature},{Encode(reference)})
            ON CONFLICT(tenant_id,coordinate_key) DO UPDATE SET revision=EXCLUDED.revision,source_signature=EXCLUDED.source_signature,reference=EXCLUDED.reference
            """,ct);
        return reference;
    }
    private async Task<RmaAuthorizationDecisionV1?> AuthorizationCurrent(Guid application, CancellationToken ct, int? version = null)
    {
        var head = await db.Database.SqlQuery<AuthorizationHeadRow>($"""
            SELECT decision_id AS "Id",version AS "Version" FROM cp6_rma_formal_v1.authorization_heads
            WHERE tenant_id={db.CurrentTenantId} AND application_id={application}
            """).SingleOrDefaultAsync(ct);
        if (head == null) { if (version != null) throw Error("AUTHORIZATION_VERSION_NOT_FOUND",404); return null; }
        var body = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_rma_formal_v1.authorization_versions
            WHERE tenant_id={db.CurrentTenantId} AND decision_id={head.Id} AND version={version ?? head.Version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("AUTHORIZATION_VERSION_NOT_FOUND",404);
        return JsonSerializer.Deserialize<RmaAuthorizationDecisionV1>(body,RmaFormalStoreV1.Json)!;
    }
    private async Task<RmaPolicyVersionV1> AuthorizationPolicy(RmaTrustedScopeV1 scope, CancellationToken ct)
    {
        var head = await PolicyCurrent(scope,ct) ?? throw Error("RMA_POLICY_NOT_CONFIGURED",409);
        return await PolicyVersion(head.Id,head.Version,ct);
    }
    private async Task<(RmaReturnSourceCutV1 Source, FulfilmentRefV1 Head, RangeSet.Segment[] Occupied, RangeSet.Segment[] Received)> EntitlementCurrent(
        RmaFormalLineOriginalV1 line, RmaPolicyVersionV1 policy, CancellationToken ct)
    {
        var source = await (returnSources ?? throw Error("RMA_RETURN_SOURCE_NOT_CONFIGURED",503)).ReadAsync(db.CurrentTenantId,line.Original,ct);
        var key = CoordinateKey(source.Coordinate); var segments = await ClaimSegments().Where(x => x.Key == key).ToArrayAsync(ct);
        var occupied = RangeSet.Union(RangeSet.Read(source.HistoricalOccupiedRanges).Concat(segments.Where(x => x.State is "ACTIVE_RESERVED" or "CONSUMED_BY_RECEIPT" or "UNKNOWN_HOLD").Select(x => new RangeSet.Segment(x.From,x.To))));
        var received = RangeSet.Union(RangeSet.Read(source.HistoricalReceivedRanges).Concat(segments.Where(x => x.State == "CONSUMED_BY_RECEIPT").Select(x => new RangeSet.Segment(x.From,x.To))));
        if (!RangeSet.Subset(occupied,RangeSet.Read(source.Coordinate.EligibleRanges))) throw Error("RETURN_SOURCE_CONFLICTS_WITH_EXISTING_CLAIMS",409);
        var head = await EntitlementHead(key,Digest(new { source.Coordinate.SourceCutRef, source.Coordinate.SourceVersionRef, policy.Reference }),false,ct);
        return (source,head,occupied,received);
    }
    public Task<RmaAuthorizationContextV1> AuthorizationContextAsync(Guid application, CancellationToken ct = default, bool includeEntitlements = true) => Run("query",true,async (store,a,t) =>
    {
        var current = await Current(store,a,application,t); var view = View(current); var policy = await AuthorizationPolicy(current.Scope,t);
        var decision = await AuthorizationCurrent(application,t);
        var authority = await store.Fact(AuthorizationAuthority,a.ActorId,new AuthorizationAuthorityBody(application,a.Authorities[0].Source.SourcePermissionVersion,policy.Reference,decision?.DecisionRef),true,t);
        var lines = new List<RmaAuthorizationLineV1>();
        foreach (var line in includeEntitlements ? current.Lines : [])
        {
            var e = await EntitlementCurrent(line,policy,t); var eligible = RangeSet.Read(e.Source.Coordinate.EligibleRanges); var unit = line.Original.BaseQty.Uom;
            var reference = await store.Fact(EntitlementOwner,a.ActorId,new EntitlementSelection(application,line.LineId,view.ApplicationRef,e.Source,e.Head,policy.Reference),true,t);
            var active = new FulfilmentQtyV1(Text(RangeSet.Measure(e.Occupied)),unit);
            var snapshot = new RmaEntitlementV1(reference,e.Source.Original,policy.Reference,policy.Reference.Version,DateTimeOffset.UtcNow,active,
                new(Text(RangeSet.Measure(e.Received)),unit),active,new(Text(RangeSet.Measure(RangeSet.Read(e.Source.CreditedRanges))),unit),
                new(Text(RangeSet.Measure(eligible)-RangeSet.Measure(e.Occupied)),unit),"COMPLETE",null,e.Head);
            var applicationLine = view.RequestedLines.Single(x => x.LineId == line.LineId);
            var lineRef = new FulfilmentRefV1("Main.Wms.RmaRequestedLine.v1",line.LineId.ToString(),line.LineVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),applicationLine.LineDigest);
            var available = RangeSet.Except(RangeSet.Read(line.Ranges),RangeSet.Except(RangeSet.Read(line.Ranges),eligible).Concat(e.Occupied));
            lines.Add(new(line.LineId,lineRef,snapshot,e.Source.Coordinate,RangeSet.Wire(available),e.Source.EvidenceMode));
        }
        return new RmaAuthorizationContextV1(view.ApplicationRef,authority,policy,lines.ToArray(),decision,decision?.AuthorizedShares.Select(ShareReference).ToArray() ?? []);
    },ct);
    public Task<RmaAuthorizationDecisionV1?> ReadAuthorizationAsync(Guid application, int? version = null, CancellationToken ct = default) => Run("query",false,async (store,a,t) =>
    { await Current(store,a,application,t); return await AuthorizationCurrent(application,t,version); },ct);
}
