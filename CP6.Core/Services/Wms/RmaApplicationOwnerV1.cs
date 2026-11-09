using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>Return intent based on actual native shipment lines. Never grants authorization or moves stock.</summary>
public sealed class RmaApplicationOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string ApplicationRoute = "api/wms/rma/native-applications-v1";
    private const int ApplicationPageSize = 50;
    private const int ApplicationProbeSize = 128;
    private readonly TenantRmaReadStore? _tenantReads;

    internal RmaApplicationOwnerV1(CP6Context db, OperationsOwnerV1 owner, TenantRmaReadStore? tenantReads)
        : this(db, owner) => _tenantReads = tenantReads;

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
        var lineage = await OutboundOrderLineageV1.Read(db, header.OutboundNo, ct);
        var lines = rows.Select(x => new RmaApplicationSourceLineV1(x.Id, x.LineNo, x.ProductCd, Quantity(x.ShippedQty),
            x.UnitCd!, x.WarehouseCd ?? header.WarehouseCd, x.LocationCd, x.LotNo, x.ShipTxnNo!)
            { OrderLine = lineage.GetValueOrDefault(x.Id) }).ToArray();
        var version = MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new { header.Id, header.OutboundNo, header.CustomerCd, header.WebOrderNo, Lines = lines }));
        return new(header.Id, header.OutboundNo, header.CustomerCd, header.WebOrderNo, version, lines);
    }
    public Task<RmaApplicationSourceV1> SourceAsync(Guid id, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, (a, t) => Source(a, id, t), ct);
    public Task<OutboundFulfilmentSnapshotV1> FulfilmentAsync(Guid id, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (a, t) =>
        {
            if (!await a.Outbounds().AnyAsync(x => x.Id == id, t)) throw Error("OBJECT_FORBIDDEN", 403);
            return await OutboundFulfilmentV1.Read(db, id, t);
        }, ct);
    public Task<MasterOperationScopeV1> ScopeAsync(CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false,
            (a, t) => Task.FromResult(new MasterOperationScopeV1(db.CurrentTenantId, a.ActorId)), ct);
    public Task<RmaApplicationSourceChoiceV1[]> SourcesAsync(string? keyword, int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > int.MaxValue / 50 || keyword?.Length > 100) throw Error("RMA_PAGE_INVALID", 400);
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, (a, t) =>
        {
            var rows = a.Outbounds().Where(x => x.OutboundType == OutboundType.Shipping && x.Status == OutboundOrderStatus.Completed);
            if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(x => x.OutboundNo.Contains(keyword) || x.CustomerCd != null && x.CustomerCd.Contains(keyword));
            return rows.OrderBy(x => x.OutboundNo).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50)
                .Select(x => new RmaApplicationSourceChoiceV1(x.Id, x.OutboundNo, x.CustomerCd)).ToArrayAsync(t);
        }, ct);
    }
    public Task<RmaApplicationViewV1> ReadAsync(Guid id, int? version = null, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (a, t) =>
        {
            if (_tenantReads is not null)
                return await _tenantReads.ReadAsync(db, a.ActorId, ApplicationRoute + "/{id}",
                    (storage, token) => ReadTenantAsync(storage, a, id, version, token), t);

            var h = await RequireHead(a, id, t);
            return a.ProjectRmaApplication(await ReadVersion(id, SelectVersion(version, h.Version), t));
        }, ct);

    private static async Task<RmaApplicationViewV1> ReadTenantAsync(RmaTenantReadContext storage, OperationsAccessV1 access,
        Guid id, int? version, CancellationToken ct)
    {
        var head = await RequireTenantHead(storage, access, id, ct);
        var selected = SelectVersion(version, head.Version);
        return access.ProjectRmaApplication(await ReadTenantVersion(storage, id, selected, ct));
    }

    private static async Task<RmaApplicationViewV1> ReadTenantVersion(RmaTenantReadContext storage,
        Guid id, int version, CancellationToken ct)
    {
        var tenant = storage.Binding.TenantId;
        var payload = await storage.Versions.AsNoTracking()
            .Where(row => row.TenantId == tenant && row.Id == id && row.Version == version)
            .Select(row => row.Payload).SingleAsync(ct);
        return JsonSerializer.Deserialize<RmaApplicationViewV1>(payload)!;
    }

    private static async Task<RmaTenantHead> RequireTenantHead(RmaTenantReadContext storage, OperationsAccessV1 access,
        Guid id, CancellationToken ct)
    {
        var tenant = storage.Binding.TenantId;
        var head = await storage.Heads.AsNoTracking().SingleOrDefaultAsync(row => row.TenantId == tenant && row.Id == id, ct)
            ?? throw Error("RMA_APPLICATION_NOT_FOUND", 404);
        await access.RequireRmaApplicationSource(head.OutboundId, head.ActorId, ct);
        return head;
    }

    private static int SelectVersion(int? requested, int latest)
    {
        if (requested is < 1 || requested > latest) throw Error("RMA_APPLICATION_VERSION_NOT_FOUND", 404);
        return requested ?? latest;
    }

    private void RequireSharedEntry()
    {
        if (_tenantReads is not null) throw Error("RMA_TENANT_ENTRY_UNAVAILABLE", 503);
    }
    public Task<IReadOnlyList<RmaApplicationViewV1>> ListAsync(int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > int.MaxValue / ApplicationPageSize) throw Error("RMA_PAGE_INVALID", 400);
        var skip = (page - 1) * ApplicationPageSize;
        return owner.ExecuteAsync<IReadOnlyList<RmaApplicationViewV1>>(OperationsOwnerV1.Rma, "query", false, async (a, t) =>
        {
            if (_tenantReads is not null)
                return await _tenantReads.ReadAsync(db, a.ActorId, ApplicationRoute,
                    (storage, token) => ListTenantAsync(storage, a, skip, token), t);

            var sources = a.Outbounds().Select(x => x.Id);
            var heads = Heads().Where(x => sources.Contains(x.Outbound));
            if (a.RmaApplicationSelfOnly) heads = heads.Where(x => x.Actor == a.ActorId);
            var selected = await heads.OrderBy(x => x.Id).Skip(skip).Take(ApplicationPageSize).ToArrayAsync(t);
            var result = new List<RmaApplicationViewV1>();
            foreach (var h in selected) result.Add(a.ProjectRmaApplication(await ReadVersion(h.Id, h.Version, t)));
            return result;
        }, ct);
    }

    private static async Task<IReadOnlyList<RmaApplicationViewV1>> ListTenantAsync(RmaTenantReadContext storage,
        OperationsAccessV1 access, int skip, CancellationToken ct)
    {
        var tenant = storage.Binding.TenantId;
        var heads = storage.Heads.AsNoTracking().Where(row => row.TenantId == tenant);
        if (access.RmaApplicationSelfOnly) heads = heads.Where(row => row.ActorId == access.ActorId);
        var outbounds = access.Outbounds();
        var selected = new List<(Guid Id, int Version)>(ApplicationPageSize);
        Guid? after = null;
        while (selected.Count < ApplicationPageSize)
        {
            var next = after is Guid cursor ? heads.Where(row => row.Id > cursor) : heads;
            var candidates = await next.OrderBy(row => row.Id).Take(ApplicationProbeSize)
                .Select(row => new { row.Id, row.OutboundId, row.Version }).ToArrayAsync(ct);
            if (candidates.Length == 0) break;

            var outboundIds = candidates.Select(row => row.OutboundId).Distinct().ToArray();
            var visible = (await outbounds.Where(row => outboundIds.Contains(row.Id))
                .Select(row => row.Id).ToArrayAsync(ct)).ToHashSet();

            // Only visible candidates advance the page offset, in the database's UUID order.
            foreach (var row in candidates)
            {
                if (!visible.Contains(row.OutboundId)) continue;
                if (skip > 0)
                {
                    skip--;
                    continue;
                }
                selected.Add((row.Id, row.Version));
                if (selected.Count == ApplicationPageSize) break;
            }

            if (candidates.Length < ApplicationProbeSize) break;
            after = candidates[^1].Id;
        }

        if (selected.Count == 0) return [];
        // Fetch just the selected immutable versions. Grouping keeps the number of SQL
        // branches bounded by the page size and never cross-matches an ID to another version.
        var versions = storage.Versions.AsNoTracking().Where(row => row.TenantId == tenant);
        IQueryable<RmaTenantVersion>? matches = null;
        foreach (var group in selected.GroupBy(row => row.Version))
        {
            var version = group.Key;
            var ids = group.Select(row => row.Id).ToArray();
            var slice = versions.Where(row => row.Version == version && ids.Contains(row.Id));
            matches = matches is null ? slice : matches.Concat(slice);
        }
        var payloads = await matches!.OrderBy(row => row.Id).Select(row => row.Payload).ToArrayAsync(ct);
        return payloads.Select(payload => access.ProjectRmaApplication(JsonSerializer.Deserialize<RmaApplicationViewV1>(payload)!)).ToArray();
    }
    public Task<RmaApplicationResultV1> CreateAsync(RmaApplicationCreateV1 c, CancellationToken ct = default)
    {
        if (_tenantReads is { DraftEnabled: false }) RequireSharedEntry();
        if (c.OperationId == Guid.Empty || c.ApplicationId == Guid.Empty || c.OutboundId == Guid.Empty
            || string.IsNullOrWhiteSpace(c.SourceVersion) || string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length > 1000
            || c.Remarks?.Length > 1000 || c.RequestedOutcome is not ("CREDIT" or "REFUND" or "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT" or "RETURN_ONLY" or "TO_BE_DECIDED")
            || c.Lines is null || c.Lines.Count is < 1 or > 100 || c.Lines.Any(x => x is null || x.OutboundLineId == Guid.Empty)
            || c.Lines.Select(x => x.OutboundLineId).Distinct().Count() != c.Lines.Count)
            throw Error("RMA_APPLICATION_INPUT_INVALID", 400);
        var quantities = c.Lines.Select(x => (x.OutboundLineId, Value: ParseQuantity(x.Quantity))).ToArray();
        var input = MainOwnerOperationJournalV1.Encode(c);
        if (_tenantReads is { DraftEnabled: true } storage)
            return CreateTenantDraft(storage, c, quantities, input, ct);

        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "add", true, async (a, t) =>
        {
            if (original != null) return await Disclose(a, original, input, t);
            var v = await PrepareDraft(a, c, quantities, null, t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_rma_application_v1.heads(tenant_id,id,actor_id,outbound_id,version)
                VALUES({db.CurrentTenantId},{v.Id},{a.ActorId},{v.OutboundId},1)
                """, t);
            await Append(v, t);
            var result = new RmaApplicationResultV1(c.OperationId, v.Id, v.Version, v.State, v.Etag);
            await journal.Store(JournalOwner, Contract, c.OperationId, a.ActorId, input, result, [v.Id], new { action = "create-application", result }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(JournalOwner, c.OperationId, t); return original == null ? "add" : "query"; });
    }
    private Task<RmaApplicationResultV1> CreateTenantDraft(TenantRmaReadStore storage, RmaApplicationCreateV1 command,
        (Guid OutboundLineId, decimal Value)[] quantities, byte[] input, CancellationToken ct) =>
        owner.ExecuteRmaWriteAsync(storage, RmaWriteEntry.Draft, command.OperationId, async (access, tenantDb, original, token) =>
        {
            if (original is not null)
                return await Disclose(access, original, input, token, tenantDb);

            var view = await PrepareDraft(access, command, quantities, tenantDb, token);
            var result = new RmaApplicationResultV1(command.OperationId, view.Id, view.Version, view.State, view.Etag);
            var audit = JsonSerializer.Serialize(new { action = "create-application", result });
            await tenantDb.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT cp6_rma_draft_writer_v1.create_draft({tenantDb.Binding.TenantId},{command.OperationId},{view.Id},
                    {access.ActorId},{view.OutboundId},CAST({JsonSerializer.Serialize(view)} AS jsonb),{input},
                    {MainOwnerOperationJournalV1.Digest(input)},{MainOwnerOperationJournalV1.Encode(result)},CAST({audit} AS jsonb))
                """, token);
            return result;
        }, ct);

    private async Task<RmaApplicationViewV1> PrepareDraft(OperationsAccessV1 access, RmaApplicationCreateV1 command,
        (Guid OutboundLineId, decimal Value)[] quantities, RmaTenantReadContext? storage, CancellationToken ct)
    {
        access.RequireRmaApplicationFields(command.Remarks != null);
        var source = await Source(access, command.OutboundId, ct);
        if (source.Version != command.SourceVersion) throw Error("RMA_SOURCE_CHANGED", 412);
        var exists = storage is null
            ? await Heads().AnyAsync(x => x.Id == command.ApplicationId, ct)
            : await storage.Heads.AsNoTracking().AnyAsync(row => row.TenantId == storage.Binding.TenantId
                && row.Id == command.ApplicationId, ct);
        if (exists) throw Error("RMA_APPLICATION_EXISTS", 409);

        var lines = new List<RmaApplicationRequestedLineV1>();
        foreach (var quantity in quantities)
        {
            var line = source.Lines.SingleOrDefault(x => x.Id == quantity.OutboundLineId) ?? throw Error("RMA_SOURCE_LINE_INVALID");
            if (quantity.Value > decimal.Parse(line.ShippedQuantity, CultureInfo.InvariantCulture))
                throw Error("RMA_QUANTITY_EXCEEDS_SHIPMENT");
            lines.Add(new(line, Quantity(quantity.Value)));
        }

        return new(command.ApplicationId, 1, "DRAFT", access.ActorId, DateTimeOffset.UtcNow,
            source.Id, source.Number, source.CustomerCode, source.Version, command.Reason, command.RequestedOutcome, lines,
            command.Remarks, null);
    }

    public Task<RmaApplicationResultV1> SubmitAsync(Guid id, RmaApplicationSubmitV1 c, string? ifMatch, CancellationToken ct = default)
    {
        if (_tenantReads is { SubmitEnabled: false }) RequireSharedEntry();
        if (id == Guid.Empty || c.OperationId == Guid.Empty || c.ExpectedVersion < 1) throw Error("RMA_APPLICATION_INPUT_INVALID", 400);
        if (string.IsNullOrWhiteSpace(ifMatch)) throw Error("RMA_IF_MATCH_REQUIRED", 428);
        var input = MainOwnerOperationJournalV1.Encode(new { Id = id, Command = c, IfMatch = ifMatch });
        if (_tenantReads is { SubmitEnabled: true } storage)
            return SubmitTenantDraft(storage, id, c, ifMatch, input, ct);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return owner.ExecuteAsync(OperationsOwnerV1.Rma, "add", true, async (a, t) =>
        {
            if (original != null) return await Disclose(a, original, input, t);
            var v = await PrepareSubmission(a, id, c.ExpectedVersion, ifMatch, null, t);
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
    private Task<RmaApplicationResultV1> SubmitTenantDraft(TenantRmaReadStore storage, Guid id, RmaApplicationSubmitV1 command,
        string ifMatch, byte[] input, CancellationToken ct) =>
        owner.ExecuteRmaWriteAsync(storage, RmaWriteEntry.Submit, command.OperationId, async (access, tenantDb, original, token) =>
        {
            if (original is not null)
                return await Disclose(access, original, input, token, tenantDb);

            var view = await PrepareSubmission(access, id, command.ExpectedVersion, ifMatch, tenantDb, token);
            var result = new RmaApplicationResultV1(command.OperationId, id, view.Version, view.State, view.Etag);
            var audit = JsonSerializer.Serialize(new { action = "submit-application", result });
            var changed = await tenantDb.Database.SqlQuery<bool>($"""
                SELECT cp6_rma_submit_writer_v1.submit_draft({tenantDb.Binding.TenantId},{command.OperationId},{id},
                    {access.ActorId},{command.ExpectedVersion},CAST({JsonSerializer.Serialize(view)} AS jsonb),{input},
                    {MainOwnerOperationJournalV1.Digest(input)},{MainOwnerOperationJournalV1.Encode(result)},CAST({audit} AS jsonb)) AS "Value"
                """).SingleAsync(token);
            if (!changed) throw Error("RMA_APPLICATION_CHANGED", 412);
            return result;
        }, ct);

    private async Task<RmaApplicationViewV1> PrepareSubmission(OperationsAccessV1 access, Guid id, int expectedVersion,
        string ifMatch, RmaTenantReadContext? storage, CancellationToken ct)
    {
        RmaApplicationViewV1 before;
        Guid outbound;
        if (storage is null)
        {
            var head = await RequireHead(access, id, ct);
            before = await ReadVersion(id, head.Version, ct);
            outbound = head.Outbound;
        }
        else
        {
            var head = await RequireTenantHead(storage, access, id, ct);
            before = await ReadTenantVersion(storage, id, head.Version, ct);
            outbound = head.OutboundId;
        }

        if (before.Version != expectedVersion || before.Etag != ifMatch) throw Error("RMA_APPLICATION_CHANGED", 412);
        if (before.State != "DRAFT") throw Error("RMA_APPLICATION_NOT_DRAFT", 409);
        access.RequireRmaApplicationFields(before.Remarks != null);
        var source = await Source(access, outbound, ct);
        if (source.Version != before.SourceVersion) throw Error("RMA_SOURCE_CHANGED", 412);
        return before with { Version = before.Version + 1, State = "SUBMITTED", SubmittedAt = DateTimeOffset.UtcNow };
    }

    private async Task<RmaApplicationResultV1> Disclose(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original,
        byte[]? input, CancellationToken ct, RmaTenantReadContext? storage = null)
    {
        if (storage is not null && original.Contract != Contract)
            throw Error("RMA_TENANT_BINDING_UNAVAILABLE", 503);
        if (original.Actor != a.ActorId) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<RmaApplicationResultV1>(original.Result)!;
        if (storage is null) await RequireHead(a, result.ApplicationId, ct);
        else await RequireTenantHead(storage, a, result.ApplicationId, ct);
        return result;
    }
    public Task<RmaApplicationOperationV1> OperationAsync(Guid id, bool close = false, CancellationToken ct = default)
    {
        RequireSharedEntry();
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
