using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string ScopeSelection = "Main.Wms.RmaScopeSelection.v1", SourceSelection = "Main.Wms.RmaSourceSelection.v1";
    private sealed record ScopeSelectionBody(RmaTrustedScopeV1 Scope, string WarehouseCode);
    private sealed record SourceSelectionBody(RmaTrustedScopeV1 Scope, Guid OutboundId, FulfilmentRefV1 Customer, OriginalFulfilmentV1 Original);
    private sealed record OptionPageBody(RmaTrustedScopeV1 Scope, string Filter, DateTimeOffset At, RmaFulfilmentOptionV1[] Options, Guid[] Outbounds);
    private sealed record Cursor(FulfilmentRefV1 Page, int Offset);
    private sealed class WarehouseSite
    {
        public Guid Id { get; init; }
        public Guid Site { get; init; }
    }
    private async Task<Dictionary<Guid, Guid>> WarehouseSites(CancellationToken ct)
    {
        var sites = await db.Database.SqlQuery<WarehouseSite>($"""
        SELECT id AS "Id",site_id AS "Site" FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id={db.CurrentTenantId}
        """).ToDictionaryAsync(x => x.Id, x => x.Site, ct);
        foreach (var item in options.Value.WarehouseSites.Where(x => x.TenantId == db.CurrentTenantId))
        {
            if (sites.TryGetValue(item.WarehouseId, out var original) && original != item.SiteId) throw Error("RMA_SCOPE_CONFIGURATION_CONFLICT", 503);
            sites[item.WarehouseId] = item.SiteId;
        }
        return sites;
    }
    private IQueryable<string> VisibleWarehouseCodes(OperationsAccessV1 a) =>
        from line in db.OutboundOrderDetails.AsNoTracking() join header in a.Outbounds() on line.OutboundNo equals header.OutboundNo
        where !line.IsDeleted select line.WarehouseCd ?? header.WarehouseCd;
    private async Task<string> RequireScope(OperationsAccessV1 a, RmaTrustedScopeV1 requested, CancellationToken ct)
    {
        if (requested.EnvironmentId != environment.EnvironmentId.ToString() || requested.Mode != runtime.EvidenceMode)
            throw Error("SCOPE_MISMATCH", 403);
        var sites = await WarehouseSites(ct);
        if (!sites.TryGetValue(requested.WarehouseId, out var site) || site != requested.SiteId) throw Error("SCOPE_NOT_VISIBLE", 403);
        var codes = VisibleWarehouseCodes(a);
        var row = await db.Warehouses.AsNoTracking().Where(w => !w.IsDeleted && w.Id == requested.WarehouseId && codes.Contains(w.WarehouseCd)
            && db.Space_Sites.Any(s => s.Id == site && s.Enable && !s.IsDeleted)).Select(w => w.WarehouseCd).SingleOrDefaultAsync(ct);
        return row ?? throw Error("SCOPE_NOT_VISIBLE", 403);
    }
    private async Task<ScopeSelectionBody> ResolveScope(RmaFormalStoreV1 store, OperationsAccessV1 a, FulfilmentRefV1 reference, CancellationToken ct)
    {
        var selected = await store.Selection<ScopeSelectionBody>(reference, ScopeSelection, a.ActorId, ct);
        await RequireScope(a, selected.Scope, ct); return selected;
    }
    private Task<FulfilmentRefV1> Actor(RmaFormalStoreV1 store, OperationsAccessV1 a, CancellationToken ct) =>
        store.Fact("Main.Wms.RmaActorObservation.v1", a.ActorId, new { TenantId = db.CurrentTenantId,
            EnvironmentId = environment.EnvironmentId, ActorId = a.ActorId, a.Authorities[0].Source.SourcePermissionVersion }, false, ct);
    private static string[] Capabilities(OperationsAccessV1 a) => new[] { ("query", "wms.rma.read"),
        ("application-write", "wms.rma.application.write"), ("application-submit", "wms.rma.application.submit"),
        ("application-withdraw", "wms.rma.application.withdraw") }.Where(x => a.Authorities[0].Actor.ActionKeys.Contains("wms-rma:" + x.Item1)).Select(x => x.Item2).ToArray();
    public Task<RmaFormalContextV1> ContextAsync(CancellationToken ct = default) => Run<RmaFormalContextV1>("query", true, async (store, a, t) =>
    {
        var sites = await WarehouseSites(t); var codes = VisibleWarehouseCodes(a); var warehouseIds = sites.Keys.ToArray();
        var activeSites = await db.Space_Sites.Where(s => s.Enable && !s.IsDeleted).Select(s => s.Id).ToArrayAsync(t);
        var rows = (await db.Warehouses.AsNoTracking().Where(w => !w.IsDeleted && warehouseIds.Contains(w.Id) && codes.Contains(w.WarehouseCd))
            .OrderBy(w => w.WarehouseCd).ThenBy(w => w.Id).Select(w => new { w.Id, w.WarehouseCd }).ToArrayAsync(t))
            .Where(w => activeSites.Contains(sites[w.Id])).Select(w => new { w.Id, Site = sites[w.Id], w.WarehouseCd }).ToArray();
        if (rows.Length > 200) throw Error("RMA_SCOPE_LIMIT_EXCEEDED");
        var capabilities = Capabilities(a); var result = new List<RmaScopeOptionV1>();
        foreach (var row in rows)
        {
            var head = await store.ScopeHead(row.Id, a.ActorId, new { TenantId = db.CurrentTenantId, EnvironmentId = environment.EnvironmentId,
                Mode = runtime.EvidenceMode, SiteId = row.Site, WarehouseId = row.Id }, t);
            var scope = new RmaTrustedScopeV1(environment.EnvironmentId.ToString(), row.Site, row.Id, runtime.EvidenceMode, head);
            var option = await store.Fact(ScopeSelection, a.ActorId, new ScopeSelectionBody(scope, row.WarehouseCd), true, t);
            result.Add(new(option, row.Site, row.Id, row.WarehouseCd, head, capabilities, "PARTIAL"));
        }
        return new(await Actor(store, a, t), result.ToArray(), capabilities, new(), DateTimeOffset.UtcNow);
    }, ct);
    private static void Reference(FulfilmentRefV1? reference)
    {
        if (reference == null || string.IsNullOrWhiteSpace(reference.Owner) || reference.Owner.Length > 64
            || string.IsNullOrWhiteSpace(reference.Id) || reference.Id.Length > 200 || string.IsNullOrWhiteSpace(reference.Version)
            || reference.Version.Length > 64 || reference.Digest?.Length != 64 || reference.Digest.Any(x => !char.IsAsciiHexDigitLower(x)))
            throw Error("VALIDATION_FAILED", 400);
    }
    private async Task Customer(OperationsAccessV1 a, FulfilmentRefV1 reference, CancellationToken ct)
    {
        if (reference.Owner != BusinessPartnerIdentityOwnerV1.Owner || !Guid.TryParse(reference.Id, out var id)
            || !long.TryParse(reference.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw Error("RMA_CUSTOMER_REFERENCE_INVALID", 400);
        var partner = await BusinessPartnerIdentityOwnerV1.ReadOwnedAsync(db, await a.SourceAsync(OperationsOwnerV1.BusinessPartner, ct), id, version, ct);
        if (partner.Reference.Version != reference.Version || partner.Reference.Digest != reference.Digest || !partner.SourcePresent) throw Error("SOURCE_VERSION_STALE", 412);
    }
    public Task<RmaFulfilmentOptionPageV1> SourcesAsync(RmaFulfilmentQueryV1 q, CancellationToken ct = default)
    {
        Reference(q.ScopeOptionRef); if (q.CustomerRef != null) Reference(q.CustomerRef); if (q.ShipmentRef != null) Reference(q.ShipmentRef);
        if (q.PageSize is < 1 or > 100 || q.Keyword is { Length: < 1 or > 200 } || q.Cursor is { Length: < 1 or > 2000 }) throw Error("VALIDATION_FAILED", 400);
        return Run<RmaFulfilmentOptionPageV1>("query", true, async (store, a, t) =>
        {
            var selected = await ResolveScope(store, a, q.ScopeOptionRef, t); var filter = Digest(q with { Cursor = null });
            OptionPageBody page; FulfilmentRefV1 pageRef; var offset = 0;
            if (q.Cursor != null)
            {
                Cursor cursor;
                try { cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(q.Cursor), RmaFormalStoreV1.Json)!; }
                catch (Exception error) when (error is CryptographicException or JsonException) { throw Error("RMA_CURSOR_INVALID", 400); }
                pageRef = cursor.Page; offset = cursor.Offset; page = await store.Selection<OptionPageBody>(pageRef, "Main.Wms.RmaSourcePage.v1", a.ActorId, t);
                if (page.Filter != filter || offset < 0 || offset >= page.Options.Length) throw Error("RMA_CURSOR_INVALID", 400);
                foreach (var id in page.Outbounds) await RequireOutbound(a, id, t);
            }
            else
            {
                if (q.CustomerRef != null) await Customer(a, q.CustomerRef, t);
                var rows = a.Outbounds().Where(x => x.Status == CP6.Entity.DomainModels.Wms.OutboundOrderStatus.Completed);
                if (q.Keyword != null) rows = rows.Where(x => x.OutboundNo.Contains(q.Keyword) || x.CustomerCd != null && x.CustomerCd.Contains(q.Keyword));
                var ids = await rows.OrderBy(x => x.OutboundNo).ThenBy(x => x.Id).Select(x => x.Id).ToArrayAsync(t);
                var choices = new List<RmaFulfilmentOptionV1>(); var outbounds = new HashSet<Guid>(); var at = DateTimeOffset.UtcNow;
                foreach (var id in ids)
                {
                    var snapshot = await OutboundFulfilmentV1.TryRead(db, id, t);
                    if (snapshot?.CustomerRef == null || q.CustomerRef != null && q.CustomerRef != snapshot.CustomerRef) continue;
                    var lineIds = await db.OutboundOrderDetails.AsNoTracking().Where(x => x.OutboundNo == snapshot.OutboundNumber && !x.IsDeleted
                        && (x.WarehouseCd ?? db.OutboundOrders.Where(h => h.Id == id).Select(h => h.WarehouseCd).Single()) == selected.WarehouseCode)
                        .Select(x => x.Id.ToString()).ToArrayAsync(t);
                    foreach (var original in snapshot.Lines.Where(x => lineIds.Contains(x.ShipmentLineId)))
                    {
                        if (q.ShipmentRef != null && q.ShipmentRef != original.ShipmentRef || q.DeliveredOnly && original.DeliveryProofRef == null) continue;
                        var preview = await store.Fact("Main.Wms.RmaEntitlementObservation.v1", a.ActorId, new { Original = original,
                            State = "UNKNOWN", RemainingAuthorizableQuantity = (FulfilmentQtyV1?)null, PolicyRef = (FulfilmentRefV1?)null,
                            Reasons = new[] { "ENTITLEMENT_POLICY_UNAVAILABLE", "ENTITLEMENT_OWNER_CUT_UNAVAILABLE" }, ObservedAt = at }, false, t);
                        var option = await store.Fact(SourceSelection, a.ActorId, new SourceSelectionBody(selected.Scope, id, snapshot.CustomerRef, original), true, t);
                        choices.Add(new(option, original, preview, null, "PARTIAL", ["SOURCE_COVERAGE_PARTIAL", "ENTITLEMENT_UNKNOWN"], at)); outbounds.Add(id);
                    }
                }
                page = new(selected.Scope, filter, at, choices.ToArray(), outbounds.ToArray());
                pageRef = await store.Fact("Main.Wms.RmaSourcePage.v1", a.ActorId, page, true, t);
            }
            var end = Math.Min(page.Options.Length, offset + q.PageSize);
            var next = end < page.Options.Length ? protector.Protect(JsonSerializer.Serialize(new Cursor(pageRef, end), RmaFormalStoreV1.Json)) : null;
            return new(page.Options.Skip(offset).Take(q.PageSize).ToArray(), next, pageRef, page.At, "PARTIAL");
        }, ct);
    }
    private static async Task RequireOutbound(OperationsAccessV1 a, Guid id, CancellationToken ct)
    { if (!await a.Outbounds().AnyAsync(x => x.Id == id, ct)) throw Error("OBJECT_FORBIDDEN", 403); }
    private async Task Evidence(OperationsAccessV1 a, FulfilmentRefV1 reference, CancellationToken ct)
    {
        if (reference.Owner != FileContentBytesV1.Owner || !Guid.TryParse(reference.Id, out var id)) throw Error("EVIDENCE_OWNER_UNAVAILABLE", 503);
        var content = await files.ReadOwnedAsync(a, id, ct);
        if (content.ContentRef.Version != reference.Version || content.ContentRef.Digest != reference.Digest) throw Error("EVIDENCE_REFERENCE_CHANGED", 412);
    }
}
