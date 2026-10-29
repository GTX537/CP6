using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private sealed record ApplicationPageItem(Guid ActorId, Guid[] Outbounds, RmaSummaryV1 Summary);
    private sealed record ApplicationPageBody(string Filter, DateTimeOffset At, ApplicationPageItem[] Items);

    public Task<RmaApplicationPageV1> ListAsync(RmaApplicationQueryV1 q, CancellationToken ct = default)
    {
        Reference(q.ScopeOptionRef); if (q.CustomerRef != null) Reference(q.CustomerRef);
        if (q.States == null || q.States.Length > 4 || q.States.Distinct().Count() != q.States.Length
            || q.States.Any(x => x is not ("DRAFT" or "SUBMITTED" or "WITHDRAWN" or "SUPERSEDED"))
            || q.RecordedFrom > q.RecordedTo || q.PageSize is < 1 or > 100 || q.Cursor is { Length: < 1 or > 2000 })
            throw Error("VALIDATION_FAILED", 400);
        // Current captured sources have no independently issued shipment-line/invoice-line Owner reference.
        // Do not guess a relationship by matching a business code or treating an unobserved invoice as absent.
        if (q.ShipmentLineRef != null || q.InvoiceLineRef != null) throw Error("APPLICATION_FILTER_SOURCE_UNAVAILABLE", 503);
        return Run<RmaApplicationPageV1>("query", true, async (store, a, t) =>
        {
            ReadFields(a); var scope = (await ResolveScope(store, a, q.ScopeOptionRef, t)).Scope;
            var filter = Digest(q with { Cursor = null }); var offset = 0; FulfilmentRefV1 reference; ApplicationPageBody page;
            var visible = (await a.Outbounds().Select(x => x.Id).ToArrayAsync(t)).ToHashSet();
            if (q.Cursor != null)
            {
                Cursor cursor;
                try { cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(q.Cursor), RmaFormalStoreV1.Json)!; }
                catch (Exception e) when (e is CryptographicException or JsonException) { throw Error("RMA_CURSOR_INVALID", 400); }
                reference = cursor.Page; offset = cursor.Offset;
                page = await store.Selection<ApplicationPageBody>(reference, "Main.Wms.RmaApplicationPage.v1", a.ActorId, t);
                if (page.Filter != filter || offset < 0 || offset >= page.Items.Length) throw Error("RMA_CURSOR_INVALID", 400);
            }
            else
            {
                if (q.CustomerRef != null) await Customer(a, q.CustomerRef, t);
                var heads = store.Heads(); if (a.RmaApplicationSelfOnly) heads = heads.Where(x => x.Actor == a.ActorId);
                var items = new List<ApplicationPageItem>();
                foreach (var head in await heads.ToArrayAsync(t))
                {
                    var original = await store.Read(head.Id, head.Version, t);
                    if (original.Scope.EnvironmentId != scope.EnvironmentId || original.Scope.Mode != scope.Mode
                        || original.Scope.SiteId != scope.SiteId || original.Scope.WarehouseId != scope.WarehouseId
                        || original.Lines.Any(x => !visible.Contains(x.OutboundId))
                        || q.CustomerRef != null && original.Customer != q.CustomerRef
                        || q.States.Length > 0 && !q.States.Contains(original.State)
                        || original.RequestedAt < q.RecordedFrom || original.RequestedAt > q.RecordedTo) continue;
                    var view = View(original);
                    items.Add(new(original.ActorId, original.Lines.Select(x => x.OutboundId).Distinct().ToArray(),
                        new(original.Id, "RA-" + original.Id.ToString("N"), original.Customer, original.State,
                            "PENDING", "UNKNOWN", "PENDING", "ADOPTION_UNPROVEN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
                            original.RequestedAt, view.ApplicationRef)));
                }
                page = new(filter, DateTimeOffset.UtcNow, items.OrderByDescending(x => x.Summary.RecordedAt).ThenBy(x => x.Summary.RmaId).ToArray());
                reference = await store.Fact("Main.Wms.RmaApplicationPage.v1", a.ActorId, page, true, t);
            }
            var selected = page.Items.Skip(offset).Take(q.PageSize).ToArray();
            if (selected.Any(x => a.RmaApplicationSelfOnly && x.ActorId != a.ActorId || x.Outbounds.Any(id => !visible.Contains(id))))
                throw Error("OBJECT_FORBIDDEN", 403);
            var end = offset + selected.Length;
            var next = end < page.Items.Length ? protector.Protect(JsonSerializer.Serialize(new Cursor(reference, end), RmaFormalStoreV1.Json)) : null;
            return new(selected.Select(x => x.Summary).ToArray(), next, reference, page.At, "PARTIAL");
        }, ct);
    }
    public Task<RmaApplicationEditContextV1> EditContextAsync(Guid id, CancellationToken ct = default) =>
        Run<RmaApplicationEditContextV1>("query", false, async (store, a, t) =>
        {
            var original = await Current(store, a, id, t);
            if (!a.Authorities[0].Fields.Fields["Remarks"].CanRead) throw Error("FIELD_READ_FORBIDDEN", 403);
            return new(View(original), original.Remarks);
        }, ct);
}
