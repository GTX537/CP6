using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string DeliveryEvidenceOwner = "Main.Wms.RmaDeliveryEvidence.v1", DeliveryJournal = "Main.Wms.RmaDeliveryEvidenceCommand.v1";
    private sealed class DeliveryEvidenceRow
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public Guid Outbound { get; init; }
        public Guid Line { get; init; }
        public Guid Warehouse { get; init; }
        public Guid Site { get; init; }
        public DateTimeOffset At { get; init; }
        public string InputDigest { get; init; } = null!;
        public byte[] Body { get; init; } = null!;
    }
    private IQueryable<DeliveryEvidenceRow> DeliveryRows() => db.Database.SqlQuery<DeliveryEvidenceRow>($"""
        SELECT id AS "Id",actor_id AS "Actor",outbound_id AS "Outbound",line_id AS "Line",warehouse_id AS "Warehouse",
          site_id AS "Site",recorded_at AS "At",input_digest AS "InputDigest",body AS "Body"
        FROM cp6_rma_formal_v1.delivery_evidence WHERE tenant_id={db.CurrentTenantId}
        """);
    private static RmaDeliveryEvidenceV1 DeliveryOriginal(DeliveryEvidenceRow row) =>
        JsonSerializer.Deserialize<RmaDeliveryEvidenceV1>(row.Body, RmaFormalStoreV1.Json)!;
    private async Task<RmaDeliveryEvidenceWithdrawalV1?> DeliveryWithdrawal(Guid id, CancellationToken ct)
    {
        var body = await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_rma_formal_v1.delivery_evidence_withdrawals WHERE tenant_id={db.CurrentTenantId} AND id={id}").SingleOrDefaultAsync(ct);
        return body == null ? null : JsonSerializer.Deserialize<RmaDeliveryEvidenceWithdrawalV1>(body, RmaFormalStoreV1.Json)!;
    }
    private static void DeliveryScope(RmaTrustedScopeV1 actual, RmaTrustedScopeV1 requested)
    {
        if (actual.EnvironmentId != requested.EnvironmentId || actual.Mode != requested.Mode || actual.SiteId != requested.SiteId || actual.WarehouseId != requested.WarehouseId)
            throw Error("SCOPE_MISMATCH", 403);
    }
    private static decimal DeliveryPosition(string value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
            throw Error("DELIVERY_EVIDENCE_RANGE_INVALID", 400);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
    private async Task DeliveryRead(OperationsAccessV1 a, RmaDeliveryEvidenceV1 value, CancellationToken ct, RmaTrustedScopeV1? scope = null)
    {
        ReadFields(a);
        if (a.RmaApplicationSelfOnly && value.ActorId != a.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
        if (scope == null) await RequireScope(a, value.Scope, ct); else DeliveryScope(value.Scope, scope);
        await RequireOutbound(a, value.OutboundId, ct);
        await DeliveryFilesAndNotes(a, value, ct);
    }
    private async Task DeliveryFilesAndNotes(OperationsAccessV1 a, RmaDeliveryEvidenceV1 value, CancellationToken ct)
    {
        if (value.Notes != null && !a.Authorities[0].Fields.Fields["Remarks"].CanRead) throw Error("FIELD_READ_FORBIDDEN", 403);
        foreach (var file in value.FileRefs) await Evidence(a, file, ct);
    }
    private async Task<OriginalFulfilmentV1> DeliverySource(OperationsAccessV1 a, ScopeSelectionBody scope, FulfilmentRefV1 shipment, Guid lineId, CancellationToken ct)
    {
        if (!Guid.TryParse(shipment.Id, out var id)) throw Error("DELIVERY_SOURCE_INVALID", 400);
        await RequireOutbound(a, id, ct);
        var snapshot = await OutboundFulfilmentV1.Read(db, id, ct);
        var original = snapshot.Lines.SingleOrDefault(x => x.ShipmentLineId == lineId.ToString()) ?? throw Error("DELIVERY_SOURCE_LINE_NOT_FOUND", 404);
        if (original.ShipmentRef != shipment) throw Error("SOURCE_VERSION_STALE", 412);
        if (!await (from line in db.OutboundOrderDetails join header in a.Outbounds() on line.OutboundNo equals header.OutboundNo
            where line.Id == lineId && !line.IsDeleted && header.Id == id && (line.WarehouseCd ?? header.WarehouseCd) == scope.WarehouseCode select line.Id).AnyAsync(ct))
            throw Error("SCOPE_MISMATCH", 403);
        return original;
    }
    public Task<RmaDeliveryEvidenceViewV1> RecordDeliveryEvidenceAsync(Guid command, RmaDeliveryEvidenceRecordV1 input, CancellationToken ct = default)
    {
        Reference(input.ScopeOptionRef); Reference(input.ShipmentRef);
        if (command == Guid.Empty || input.EvidenceId == Guid.Empty || input.ShipmentLineId == Guid.Empty
            || string.IsNullOrWhiteSpace(input.DocumentReference) || input.DocumentReference.Length > 200
            || input.Notes is { Length: > 1000 } || input.Ranges == null || input.Ranges.Length is < 1 or > 100
            || input.FileRefs == null || input.FileRefs.Length is < 1 or > 50 || input.FileRefs.Distinct().Count() != input.FileRefs.Length)
            throw Error("DELIVERY_EVIDENCE_INPUT_INVALID", 400);
        foreach (var file in input.FileRefs) Reference(file);
        var ranges = input.Ranges.Select(x => x == null ? throw Error("DELIVERY_EVIDENCE_RANGE_INVALID", 400) :
            (From: DeliveryPosition(x.From), To: DeliveryPosition(x.To))).OrderBy(x => x.From).ToArray();
        for (var i = 0; i < ranges.Length; i++) if (ranges[i].From >= ranges[i].To || i > 0 && ranges[i].From < ranges[i - 1].To)
            throw Error("DELIVERY_EVIDENCE_RANGE_INVALID", 400);
        var c = input with { Ranges = [..input.Ranges], FileRefs = [..input.FileRefs] };
        var bytes = Encode(new { action = "RECORD", value = c }); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? replay = null;
        return Run("evidence-record", true, async (store, a, t) =>
        {
            if (replay != null) return await DiscloseDelivery(a, replay, bytes, t);
            ReadFields(a); WriteFields(a, c.Notes != null);
            if (c.Notes != null && !a.Authorities[0].Fields.Fields["Remarks"].CanRead) throw Error("FIELD_READ_FORBIDDEN", 403);
            var scope = await ResolveScope(store, a, c.ScopeOptionRef, t);
            var source = await DeliverySource(a, scope, c.ShipmentRef, c.ShipmentLineId, t);
            var digest = Digest(new { scope.Scope.EnvironmentId, scope.Scope.Mode, scope.Scope.SiteId, scope.Scope.WarehouseId,
                c.ShipmentRef, c.ShipmentLineId, c.EvidenceId, c.DocumentReference, c.OccurredAt, c.Ranges, c.FileRefs, c.Notes });
            var existing = await DeliveryRows().SingleOrDefaultAsync(x => x.Id == c.EvidenceId, t);
            RmaDeliveryEvidenceViewV1 result;
            if (existing != null)
            {
                if (existing.Actor != a.ActorId || existing.InputDigest != digest) throw Error("DELIVERY_EVIDENCE_ID_CONFLICT", 409);
                var value = DeliveryOriginal(existing); await DeliveryFilesAndNotes(a, value, t);
                result = new(value, await DeliveryWithdrawal(existing.Id, t));
            }
            else
            {
                if (c.OccurredAt == default || c.OccurredAt > DateTimeOffset.UtcNow) throw Error("DELIVERY_EVIDENCE_TIME_INVALID", 400);
                var domain = source.SourceShareRanges.Select(x => (From: decimal.Parse(x.From, CultureInfo.InvariantCulture), To: decimal.Parse(x.To, CultureInfo.InvariantCulture))).OrderBy(x => x.From).ToArray();
                foreach (var range in ranges)
                {
                    var covered = range.From;
                    foreach (var part in domain) { if (part.From > covered) break; covered = Math.Max(covered, part.To); if (covered >= range.To) break; }
                    if (covered < range.To) throw Error("DELIVERY_EVIDENCE_OUTSIDE_SHIPMENT", 422);
                }
                foreach (var file in c.FileRefs)
                {
                    if (file.Owner != FileContentBytesV1.Owner || !Guid.TryParse(file.Id, out var fileId)) throw Error("EVIDENCE_OWNER_UNAVAILABLE", 503);
                    var content = await files.ReadOwnedAsync(a, fileId, t);
                    if (content.ContentRef.Version != file.Version || content.ContentRef.Digest != file.Digest) throw Error("EVIDENCE_REFERENCE_CHANGED", 412);
                    if (content.Availability != "AVAILABLE") throw Error("FILE_CONTENT_NOT_AVAILABLE", 409);
                }
                var at = DateTimeOffset.UtcNow; var actor = await Actor(store, a, t);
                var ordered = ranges.Select(x => new FulfilmentRangeV1(Text(x.From), Text(x.To))).ToArray();
                var quantity = new FulfilmentQtyV1(Text(ranges.Sum(x => x.To - x.From)), source.BaseQty.Uom);
                var reference = new FulfilmentRefV1(DeliveryEvidenceOwner, c.EvidenceId.ToString(), "1", Digest(new { digest, at, actor }));
                var value = new RmaDeliveryEvidenceV1(reference, scope.Scope, Guid.Parse(c.ShipmentRef.Id), a.ActorId, source,
                    c.DocumentReference, c.OccurredAt, ordered, quantity, c.FileRefs, c.Notes, at, actor);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_rma_formal_v1.delivery_evidence(tenant_id,id,actor_id,outbound_id,line_id,warehouse_id,site_id,recorded_at,input_digest,body)
                    VALUES({db.CurrentTenantId},{c.EvidenceId},{a.ActorId},{value.OutboundId},{c.ShipmentLineId},{scope.Scope.WarehouseId},{scope.Scope.SiteId},{at},{digest},{Encode(value)})
                    """, t);
                result = new(value, null);
            }
            await journal.Store(DeliveryJournal, DeliveryJournal, command, a.ActorId, bytes, result, [c.EvidenceId], new { action = "RECEIPT_EVIDENCE_COLLECTED", result.Evidence.Reference }, t);
            return result;
        }, ct, async t => { replay = await journal.Read(DeliveryJournal, command, t); return replay == null ? "evidence-record" : "query"; });
    }
    public Task<RmaDeliveryEvidenceViewV1> WithdrawDeliveryEvidenceAsync(Guid command, RmaDeliveryEvidenceWithdrawV1 input, CancellationToken ct = default)
    {
        Reference(input.ScopeOptionRef); Reference(input.EvidenceRef);
        if (command == Guid.Empty || input.EvidenceRef.Owner != DeliveryEvidenceOwner || !Guid.TryParse(input.EvidenceRef.Id, out var id)
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500) throw Error("DELIVERY_EVIDENCE_INPUT_INVALID", 400);
        var bytes = Encode(new { action = "WITHDRAW", value = input }); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? replay = null;
        return Run("evidence-withdraw", true, async (store, a, t) =>
        {
            if (replay != null) return await DiscloseDelivery(a, replay, bytes, t);
            var scope = (await ResolveScope(store, a, input.ScopeOptionRef, t)).Scope;
            var row = await DeliveryRows().SingleOrDefaultAsync(x => x.Id == id, t) ?? throw Error("DELIVERY_EVIDENCE_NOT_FOUND", 404);
            var value = DeliveryOriginal(row); await DeliveryRead(a, value, t, scope); WriteFields(a, false);
            if (input.EvidenceRef != value.Reference) throw Error("DELIVERY_EVIDENCE_REFERENCE_CHANGED", 412);
            var withdrawal = await DeliveryWithdrawal(id, t);
            if (withdrawal != null) throw Error("DELIVERY_EVIDENCE_ALREADY_WITHDRAWN", 409);
            withdrawal = new(value.Reference, input.Reason, DateTimeOffset.UtcNow, await Actor(store, a, t));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.delivery_evidence_withdrawals(tenant_id,id,body) VALUES({db.CurrentTenantId},{id},{Encode(withdrawal)})", t);
            var result = new RmaDeliveryEvidenceViewV1(value, withdrawal);
            await journal.Store(DeliveryJournal, DeliveryJournal, command, a.ActorId, bytes, result, [id], new { action = "RECEIPT_EVIDENCE_WITHDRAWN", value.Reference, input.Reason }, t);
            return result;
        }, ct, async t => { replay = await journal.Read(DeliveryJournal, command, t); return replay == null ? "evidence-withdraw" : "query"; });
    }
    private async Task<RmaDeliveryEvidenceViewV1> DiscloseDelivery(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct, RmaTrustedScopeV1? scope = null)
    {
        if (original.Actor != a.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BINDING_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<RmaDeliveryEvidenceViewV1>(original.Result)!;
        await DeliveryRead(a, result.Evidence, ct, scope); return result;
    }
    public Task<RmaDeliveryEvidenceCommandV1> QueryDeliveryEvidenceCommandAsync(RmaPolicyCommandQueryV1 input, CancellationToken ct = default)
    {
        Reference(input.ScopeOptionRef); if (input.CommandId == Guid.Empty) throw Error("VALIDATION_FAILED", 400);
        return Run("query", false, async (store, a, t) =>
        {
            var scope = (await ResolveScope(store, a, input.ScopeOptionRef, t)).Scope;
            var original = await new MainOwnerOperationJournalV1(db).Read(DeliveryJournal, input.CommandId, t);
            if (original == null) { ReadFields(a); return new RmaDeliveryEvidenceCommandV1("NOT_OBSERVED", null); }
            return new RmaDeliveryEvidenceCommandV1("FOUND", await DiscloseDelivery(a, original, null, t, scope));
        }, ct);
    }
    public Task<RmaDeliveryEvidencePageV1> DeliveryEvidenceAsync(RmaDeliveryEvidenceQueryV1 input, CancellationToken ct = default)
    {
        Reference(input.ScopeOptionRef); Reference(input.ShipmentRef);
        if (input.ShipmentLineId == Guid.Empty || input.Page is < 1 or > 100000 || input.PageSize is < 1 or > 100) throw Error("VALIDATION_FAILED", 400);
        return Run("query", false, async (store, a, t) =>
        {
            ReadFields(a); var scope = await ResolveScope(store, a, input.ScopeOptionRef, t);
            await DeliverySource(a, scope, input.ShipmentRef, input.ShipmentLineId, t); var outbound = Guid.Parse(input.ShipmentRef.Id);
            var query = DeliveryRows().Where(x => x.Outbound == outbound && x.Line == input.ShipmentLineId && x.Warehouse == scope.Scope.WarehouseId && x.Site == scope.Scope.SiteId);
            if (a.RmaApplicationSelfOnly) query = query.Where(x => x.Actor == a.ActorId);
            var rows = await query.OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip((input.Page - 1) * input.PageSize).Take(input.PageSize + 1).ToArrayAsync(t);
            var result = new List<RmaDeliveryEvidenceViewV1>();
            foreach (var row in rows.Take(input.PageSize))
            {
                // Scope, exact source and self filtering were decided once for the whole page.
                var value = DeliveryOriginal(row); await DeliveryFilesAndNotes(a, value, t);
                result.Add(new(value, await DeliveryWithdrawal(row.Id, t)));
            }
            return new RmaDeliveryEvidencePageV1(result.ToArray(), rows.Length > input.PageSize);
        }, ct);
    }
    public Task<RmaEvidenceFilePageV1> DeliveryEvidenceFilesAsync(RmaEvidenceFileQueryV1 input, CancellationToken ct = default)
    {
        Reference(input.ScopeOptionRef); if (input.Page is < 1 or > 100000 || input.Keyword?.Length > 200) throw Error("VALIDATION_FAILED", 400);
        return Run("query", false, async (store, a, t) =>
        {
            ReadFields(a); await ResolveScope(store, a, input.ScopeOptionRef, t);
            var fileAccess = await a.SourceAsync(OperationsOwnerV1.FileContent, t); var rows = fileAccess.FileContents();
            if (!string.IsNullOrWhiteSpace(input.Keyword)) rows = rows.Where(x => x.FileName.Contains(input.Keyword));
            var ids = await rows.OrderBy(x => x.FileName).ThenBy(x => x.Id).Skip((input.Page - 1) * 20).Take(21).Select(x => x.Id).ToArrayAsync(t);
            var result = new List<RmaEvidenceFileV1>();
            foreach (var id in ids.Take(20))
            {
                var f = await files.ReadOwnedAsync(a, id, t);
                result.Add(new(new(f.ContentRef.Owner, id.ToString(), f.ContentRef.Version, f.ContentRef.Digest), f.FileName, f.ContentRef.MediaType, f.Availability));
            }
            return new RmaEvidenceFilePageV1(result.ToArray(), ids.Length > 20);
        }, ct);
    }
}
