using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>Return intent based on actual native shipment lines. Never grants authorization or moves stock.</summary>
public sealed class RmaApplicationOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    internal const string JournalOwner = "Main.Wms.RmaApplication.v1", Contract = "RMA-NATIVE-APPLICATION/1";
    private static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    private static string Quantity(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static decimal ParseQuantity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => c != '.' && (c < '0' || c > '9'))
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)
            || n <= 0 || n >= 10000000000000m || value.Contains('.') && value.Length - value.IndexOf('.') - 1 > 8)
            throw Error("RMA_QUANTITY_INVALID", 400);
        return n;
    }
    private sealed class Head
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public Guid Outbound { get; init; }
        public int Version { get; init; }
    }
    private IQueryable<Head> Heads() => db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id", actor_id AS "Actor", outbound_id AS "Outbound", version AS "Version"
        FROM cp6_rma_application_v1.heads WHERE tenant_id={db.CurrentTenantId}
        """);
    private async Task<Head> RequireHead(OperationsAccessV1 a, Guid id, CancellationToken ct)
    {
        var head = await Heads().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("RMA_APPLICATION_NOT_FOUND", 404);
        await a.RequireRmaApplicationSource(head.Outbound, head.Actor, ct);
        return head;
    }
    private async Task<RmaApplicationViewV1> ReadVersion(Guid id, int version, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT payload::text AS "Value" FROM cp6_rma_application_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND id={id} AND version={version}
            """).SingleAsync(ct);
        return JsonSerializer.Deserialize<RmaApplicationViewV1>(json)!;
    }
    private async Task Append(RmaApplicationViewV1 view, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_application_v1.versions(tenant_id,id,version,state,payload)
            VALUES({db.CurrentTenantId},{view.Id},{view.Version},{view.State},CAST({JsonSerializer.Serialize(view)} AS jsonb))
            """, ct);
    private async Task<RmaApplicationSourceV1> Source(OperationsAccessV1 a, Guid id, CancellationToken ct)
    {
        var header = await a.Outbounds().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        if (header.OutboundType != OutboundType.Shipping || header.Status != OutboundOrderStatus.Completed)
            throw Error("RMA_SOURCE_NOT_SHIPPED");
        if (string.IsNullOrWhiteSpace(header.CustomerCd)) throw Error("RMA_SOURCE_CUSTOMER_UNAVAILABLE", 503);
        var rows = await db.OutboundOrderDetails.AsNoTracking().Where(x => x.OutboundNo == header.OutboundNo && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToArrayAsync(ct);
        if (rows.Length == 0 || rows.Any(x => x.ShippedQty <= 0 || string.IsNullOrWhiteSpace(x.UnitCd) || string.IsNullOrWhiteSpace(x.ShipTxnNo)))
            throw Error("RMA_SOURCE_LINE_UNAVAILABLE", 503);
        // Native outbound owns these facts; do not revalidate its stock journal or manufacture formal range/UOM references.
        var lines = rows.Select(x => new RmaApplicationSourceLineV1(x.Id, x.LineNo, x.ProductCd, Quantity(x.ShippedQty),
            x.UnitCd!, x.WarehouseCd ?? header.WarehouseCd, x.LocationCd, x.LotNo, x.ShipTxnNo!)).ToArray();
        var version = MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new { header.Id, header.OutboundNo, header.CustomerCd, header.WebOrderNo, Lines = lines }));
        return new(header.Id, header.OutboundNo, header.CustomerCd, header.WebOrderNo, version, lines);
    }
    public Task<RmaApplicationSourceV1> SourceAsync(Guid id, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, (a, t) => Source(a, id, t), ct);
    public Task<RmaApplicationViewV1> ReadAsync(Guid id, int? version = null, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (a, t) =>
        {
            var h = await RequireHead(a, id, t);
            if (version is < 1 || version > h.Version) throw Error("RMA_APPLICATION_VERSION_NOT_FOUND", 404);
            return a.ProjectRmaApplication(await ReadVersion(id, version ?? h.Version, t));
        }, ct);
    public Task<IReadOnlyList<RmaApplicationViewV1>> ListAsync(int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > int.MaxValue / 50) throw Error("RMA_PAGE_INVALID", 400);
        return owner.ExecuteAsync<IReadOnlyList<RmaApplicationViewV1>>(OperationsOwnerV1.Rma, "query", false, async (a, t) =>
        {
            var sources = a.Outbounds().Select(x => x.Id);
            var heads = Heads().Where(x => sources.Contains(x.Outbound));
            if (a.RmaApplicationSelfOnly) heads = heads.Where(x => x.Actor == a.ActorId);
            var selected = await heads.OrderBy(x => x.Id).Skip((page - 1) * 50).Take(50).ToArrayAsync(t);
            var result = new List<RmaApplicationViewV1>();
            foreach (var h in selected) result.Add(a.ProjectRmaApplication(await ReadVersion(h.Id, h.Version, t)));
            return result;
        }, ct);
    }
    public Task<RmaApplicationResultV1> CreateAsync(RmaApplicationCreateV1 c, CancellationToken ct = default)
    {
        if (c.OperationId == Guid.Empty || c.ApplicationId == Guid.Empty || c.OutboundId == Guid.Empty
            || string.IsNullOrWhiteSpace(c.SourceVersion) || string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length > 1000
            || c.Remarks?.Length > 1000 || c.RequestedOutcome is not ("CREDIT" or "REFUND" or "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT" or "RETURN_ONLY" or "TO_BE_DECIDED")
            || c.Lines is null || c.Lines.Count is < 1 or > 100 || c.Lines.Any(x => x is null || x.OutboundLineId == Guid.Empty)
            || c.Lines.Select(x => x.OutboundLineId).Distinct().Count() != c.Lines.Count)
            throw Error("RMA_APPLICATION_INPUT_INVALID", 400);
        var quantities = c.Lines.Select(x => (x.OutboundLineId, Value: ParseQuantity(x.Quantity))).ToArray();
        var input = MainOwnerOperationJournalV1.Encode(c);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "add", true, async (a, t) =>
        {
            if (original != null) return await Disclose(a, original, input, t);
            a.RequireRmaApplicationFields(c.Remarks != null);
            var source = await Source(a, c.OutboundId, t);
            if (source.Version != c.SourceVersion) throw Error("RMA_SOURCE_CHANGED", 412);
            if (await Heads().AnyAsync(x => x.Id == c.ApplicationId, t)) throw Error("RMA_APPLICATION_EXISTS", 409);
            var lines = new List<RmaApplicationRequestedLineV1>();
            foreach (var q in quantities)
            {
                var line = source.Lines.SingleOrDefault(x => x.Id == q.OutboundLineId) ?? throw Error("RMA_SOURCE_LINE_INVALID");
                if (q.Value > decimal.Parse(line.ShippedQuantity, CultureInfo.InvariantCulture)) throw Error("RMA_QUANTITY_EXCEEDS_SHIPMENT");
                lines.Add(new(line, Quantity(q.Value)));
            }
            var v = new RmaApplicationViewV1(c.ApplicationId, 1, "DRAFT", a.ActorId, DateTimeOffset.UtcNow,
                source.Id, source.Number, source.CustomerCode, source.Version, c.Reason, c.RequestedOutcome, lines, c.Remarks, null);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_rma_application_v1.heads(tenant_id,id,actor_id,outbound_id,version)
                VALUES({db.CurrentTenantId},{v.Id},{a.ActorId},{source.Id},1)
                """, t);
            await Append(v, t);
            var result = new RmaApplicationResultV1(c.OperationId, v.Id, v.Version, v.State, v.Etag);
            await journal.Store(JournalOwner, Contract, c.OperationId, a.ActorId, input, result, [v.Id], new { action = "create-application", result }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(JournalOwner, c.OperationId, t); return original == null ? "add" : "query"; });
    }
    public Task<RmaApplicationResultV1> SubmitAsync(Guid id, RmaApplicationSubmitV1 c, string? ifMatch, CancellationToken ct = default)
    {
        if (id == Guid.Empty || c.OperationId == Guid.Empty || c.ExpectedVersion < 1) throw Error("RMA_APPLICATION_INPUT_INVALID", 400);
        if (string.IsNullOrWhiteSpace(ifMatch)) throw Error("RMA_IF_MATCH_REQUIRED", 428);
        var input = MainOwnerOperationJournalV1.Encode(new { Id = id, Command = c, IfMatch = ifMatch });
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "add", true, async (a, t) =>
        {
            if (original != null) return await Disclose(a, original, input, t);
            var h = await RequireHead(a, id, t); var before = await ReadVersion(id, h.Version, t);
            if (before.Version != c.ExpectedVersion || before.Etag != ifMatch) throw Error("RMA_APPLICATION_CHANGED", 412);
            if (before.State != "DRAFT") throw Error("RMA_APPLICATION_NOT_DRAFT", 409);
            a.RequireRmaApplicationFields(before.Remarks != null);
            var source = await Source(a, h.Outbound, t);
            if (source.Version != before.SourceVersion) throw Error("RMA_SOURCE_CHANGED", 412);
            var v = before with { Version = before.Version + 1, State = "SUBMITTED", SubmittedAt = DateTimeOffset.UtcNow };
            await Append(v, t);
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_rma_application_v1.heads SET version={v.Version}
                WHERE tenant_id={db.CurrentTenantId} AND id={id} AND version={c.ExpectedVersion}
                """, t);
            if (changed != 1) throw Error("RMA_APPLICATION_CHANGED", 412);
            var result = new RmaApplicationResultV1(c.OperationId, id, v.Version, v.State, v.Etag);
            await journal.Store(JournalOwner, Contract, c.OperationId, a.ActorId, input, result, [id], new { action = "submit-application", result }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(JournalOwner, c.OperationId, t); return original == null ? "add" : "query"; });
    }
    private async Task<RmaApplicationResultV1> Disclose(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<RmaApplicationResultV1>(original.Result)!;
        await RequireHead(a, result.ApplicationId, ct);
        return result;
    }
    public Task<RmaApplicationOperationV1> OperationAsync(Guid id, bool close = false, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", close, async (a, t) =>
        {
            if (original != null)
            {
                if (original.Actor != a.ActorId) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new RmaApplicationOperationV1(id, "COMMITTED", await Disclose(a, original, null, t));
            }
            if (!close) return new RmaApplicationOperationV1(id, "UNKNOWN");
            var result = new RmaApplicationOperationV1(id, "CLOSED");
            await journal.Store(JournalOwner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-application-operation" }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(JournalOwner, id, t); return close && original == null ? "cancel" : "query"; });
    }
}
