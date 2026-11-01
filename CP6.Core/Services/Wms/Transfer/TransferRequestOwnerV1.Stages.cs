using System.Globalization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;
using Store = CP6.Core.Services.Wms.Transfer.TransferRequestStoreV1;

namespace CP6.Core.Services.Wms.Transfer;

public sealed partial class TransferRequestOwnerV1
{
    private static decimal N(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static string Q(decimal value) => value.ToString("0.00000000", CultureInfo.InvariantCulture);
    private async Task<Dictionary<Guid, string>> CurrentApproved(OperationsAccessV1 a, TransferEnvironmentRegistrationV1 registration, TransferRequestViewV1 v, CancellationToken ct)
    {
        if (v.State != "APPROVED") throw Error("TRANSFER_APPROVAL_REQUIRED", 409);
        async Task Approver(CancellationToken t)
        {
            var authority = await owner.TransferApprovalAuthorityAsync(v.Approval!.ActorId, t);
            var access = new OperationsAccessV1(db, owner, authority);
            await Visible(access, v, t);
        }
        await Approver(ct); a.AtCommit(Approver);
        var current = await Resolve(a, registration, v.Lines.Select(x => x.Input).ToArray(), v.Id, v.Version, v.Lines, ct);
        for (var i = 0; i < current.Length; i++)
            if (current[i].Product != v.Lines[i].Product || current[i].BaseQuantity != v.Lines[i].BaseQuantity
                || current[i].SourceSiteRef != v.Lines[i].SourceSiteRef || current[i].DestinationSiteRef != v.Lines[i].DestinationSiteRef)
                throw Error("TRANSFER_SOURCE_CHANGED", 412);
        var budget = await Budget(v.Deployment, v.Lines.Select(x => x.BudgetKey), ct);
        foreach (var l in v.Lines)
            if (await budget.ReadState(l.BudgetKey, l.BindingId, ct) != "ACTIVE") throw Error("TRANSFER_SOURCE_BINDING_CLOSED", 409);
        return current.ToDictionary(x => x.Id, x => x.BaseUnitCode);
    }
    private async Task<(string Product, string Unit, string? Customer)> NativeIdentity(TransferRequestLineV1 l, string baseUnit, CancellationToken ct)
    {
        var product = await db.ProductMasters.AsNoTracking().Where(x => x.Id == l.Product.ProductRef.Id && !x.IsDeleted).Select(x => x.ProductCd).SingleAsync(ct);
        var customer = l.Product.Owner.Kind == "CUSTOMER" ? await db.BusinessPartners.AsNoTracking()
            .Where(x => x.Id == l.Product.Owner.Reference.Id && !x.IsDeleted).Select(x => x.BpCd).SingleAsync(ct) : null;
        return (product, baseUnit, customer);
    }
    public Task<TransferStockChoiceV1[]> StockChoicesAsync(Guid id, CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.TransferRequest, "query", false, async (a, t) =>
        {
            await Fence(a, false, t); var v = await new Store(db).Read(id, t); await Visible(a, v, t);
            var registration = await EnvironmentStore().Acquire(a, t); var units = await CurrentApproved(a, registration, v, t);
            var result = new List<TransferStockChoiceV1>();
            foreach (var line in v.Lines)
            {
                var identity = await NativeIdentity(line, units[line.Id], t);
                var location = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == line.Input.SourceLocationId, t);
                var pools = await a.InventoryStocks().Where(x => x.WarehouseCd == location.WarehouseCd && x.LocationCd == location.LocationCd
                    && x.ProductCd == identity.Product && x.UnitCd == identity.Unit && x.OwnerType == line.Product.Owner.Kind
                    && x.OwnerCd == identity.Customer && x.AvailableQty > 0 && !x.RecallFlag
                    && (x.QcStatus == StockQcStatus.Pending || x.QcStatus == StockQcStatus.Passed)).OrderBy(x => x.Id).Take(501).ToArrayAsync(t);
                if (pools.Length > 500) throw Error("TRANSFER_STOCK_SELECTION_TOO_LARGE", 422);
                result.AddRange(pools.Select(x => new TransferStockChoiceV1(line.Id, x.Id, x.LotNo, Q(x.AvailableQty), identity.Unit)));
            }
            return result.ToArray();
        }, ct);
    public Task<TransferRequestResultV1> PrepareStageAsync(Guid id, TransferStagePrepareV1 input, CancellationToken ct = default)
    {
        Command(input.OperationId, input.Reason); var c = OperationsOwnerV1.Freeze(input);
        if (!EngineeringProductValuesV1.ValidText(c.Device, 100, true) || !EngineeringProductValuesV1.ValidText(c.ScanOccurrenceKey, 200, true)
            || c.Lines == null || c.Lines.Length is < 1 or > 500 || c.Lines.Any(x => x == null || x.StockId == Guid.Empty || x.RequestLineId == Guid.Empty))
            throw Error("TRANSFER_STAGE_INPUT_INVALID", 400);
        foreach (var l in c.Lines)
        {
            Amount(l.Quantity);
            if (l.RequestFrom == null || !Quantity.IsMatch(l.RequestFrom)) throw Error("TRANSFER_RANGE_INVALID", 422);
        }
        c = c with { Lines = c.Lines.OrderBy(x => x.RequestLineId).ThenBy(x => N(x.RequestFrom)).ThenBy(x => x.StockId).ToArray() };
        return Execute(c.OperationId, "prepare", new { Action = "PREPARE_STAGE", Id = id, Command = c }, async (a, registration, t) =>
        {
            var v = await new Store(db).Read(id, t); await Visible(a, v, t); Expected(v, c.ExpectedRevision, c.Etag);
            if (v.ContentDigest != c.ContentDigest) throw Error("TRANSFER_CONTENT_CHANGED", 412);
            var units = await CurrentApproved(a, registration, v, t);
            if (await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM cp6_transfer_request_v1.stage_occurrences
                WHERE tenant_id={db.CurrentTenantId} AND device={c.Device} AND scan_key={c.ScanOccurrenceKey}
                """).SingleAsync(t) != 0) throw Error("TRANSFER_SCAN_ALREADY_BOUND", 409);
            var old = v.Stages ?? [];
            var occupied = old.Where(x => x.State != "CLOSED_NO_EFFECT").SelectMany(x => x.Lines).Select(x => x.Selection).ToArray();
            foreach (var group in c.Lines.Concat(occupied).GroupBy(x => x.RequestLineId))
            {
                var request = v.Lines.SingleOrDefault(x => x.Id == group.Key) ?? throw Error("TRANSFER_REQUEST_LINE_INVALID", 422);
                var ordered = group.OrderBy(x => N(x.RequestFrom)).ToArray();
                for (var i = 0; i < ordered.Length; i++)
                    if (N(ordered[i].RequestFrom) + N(ordered[i].Quantity) > N(request.BaseQuantity)
                        || i > 0 && N(ordered[i - 1].RequestFrom) + N(ordered[i - 1].Quantity) > N(ordered[i].RequestFrom))
                        throw Error("TRANSFER_REQUEST_RANGE_UNAVAILABLE", 409);
            }
            if (c.Lines.Select(x => v.Lines.Single(l => l.Id == x.RequestLineId).Product.BaseUomRef).Distinct().Count() != 1)
                throw Error("TRANSFER_STAGE_MIXED_UNITS", 422);
            var lines = await ResolveStage(a, v, c.Lines, units, t); await NativeCapacity(lines, t);
            var stage = new TransferStageV1(Guid.NewGuid(), "PREPARED", c.Device, c.ScanOccurrenceKey, a.ActorId,
                v.Version, v.ContentDigest, "", lines, []);
            stage = stage with { Digest = Store.Digest(stage) };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_transfer_request_v1.stage_occurrences(tenant_id,stage_id,request_id,device,scan_key,input_digest)
                VALUES({db.CurrentTenantId},{stage.Id},{v.Id},{c.Device},{c.ScanOccurrenceKey},{stage.Digest})
                """, t);
            return (StageUpdate(v, [.. old, stage]), true);
        }, ct);
    }
    public Task<TransferRequestResultV1> DecideStageAsync(Guid request, Guid stage, bool execute, TransferStageDecisionV1 input, CancellationToken ct = default)
    {
        Command(input.OperationId, input.Reason); var c = OperationsOwnerV1.Freeze(input);
        return Execute(c.OperationId, execute ? "execute" : "close-stage", new { Action = execute ? "EXECUTE_STAGE" : "CLOSE_STAGE", Request = request, Stage = stage, Command = c }, async (a, registration, t) =>
        {
            var v = await new Store(db).Read(request, t); await Visible(a, v, t); Expected(v, c.ExpectedRevision, c.Etag);
            var stages = v.Stages ?? []; var original = stages.SingleOrDefault(x => x.Id == stage) ?? throw Error("TRANSFER_STAGE_NOT_FOUND", 404);
            if (original.Digest != c.StageDigest) throw Error("TRANSFER_STAGE_CHANGED", 412);
            if (original.State != "PREPARED") throw Error("TRANSFER_STAGE_TERMINAL", 409);
            if (execute)
            {
                if (c.PhysicalNotOccurred) throw Error("TRANSFER_EXECUTION_CONFIRMATION_INVALID", 422);
                if (original.PerformerId != a.ActorId) throw Error("TRANSFER_PERFORMER_REQUIRED", 403);
                var units = await CurrentApproved(a, registration, v, t);
                var current = await ResolveStage(a, v, original.Lines.Select(x => x.Selection).ToArray(), units, t);
                if (!current.SequenceEqual(original.Lines)) throw Error("TRANSFER_STOCK_CHANGED", 412);
                await NativeCapacity(current, t);
                using var inheritance = stock.InheritTransactions(a); var effects = new List<TransferStageMovementV1>();
                foreach (var line in current) effects.Add(await stock.ApplyTransferWithinOwnerAsync(stage, line, a.Actor, t));
                original = original with { State = "POSTED", Movements = effects.ToArray() };
            }
            else
            {
                if (!c.PhysicalNotOccurred) throw Error("TRANSFER_PHYSICAL_NO_EFFECT_REQUIRED", 409);
                original = original with { State = "CLOSED_NO_EFFECT" };
            }
            return (StageUpdate(v, stages.Select(x => x.Id == stage ? original : x).ToArray()), true);
        }, ct);
    }
    private static TransferRequestViewV1 StageUpdate(TransferRequestViewV1 v, TransferStageV1[] stages)
    {
        var posted = stages.Where(x => x.State == "POSTED").SelectMany(x => x.Lines).ToArray();
        var complete = v.Lines.All(l => posted.Where(x => x.Selection.RequestLineId == l.Id).Sum(x => N(x.Selection.Quantity)) == N(l.BaseQuantity));
        return Seal(v with { Revision = Next(v.Revision), Stages = stages,
            ExecutionState = complete ? "COMPLETED" : posted.Length > 0 ? "PARTIALLY_MOVED" : "NOT_DISPATCHED" }, false);
    }
    private async Task<TransferStageLineV1[]> ResolveStage(OperationsAccessV1 a, TransferRequestViewV1 v, TransferStageSelectionV1[] selections, Dictionary<Guid, string> units, CancellationToken ct)
    {
        var result = new List<TransferStageLineV1>();
        foreach (var group in selections.GroupBy(x => x.RequestLineId))
        {
            var l = v.Lines.Single(x => x.Id == group.Key); var identity = await NativeIdentity(l, units[l.Id], ct);
            var from = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == l.Input.SourceLocationId, ct);
            var to = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == l.Input.DestinationLocationId, ct);
            await a.GuardNewLocationUse(from.WarehouseCd, from.LocationCd, ct); await a.GuardNewLocationUse(to.WarehouseCd, to.LocationCd, ct);
            foreach (var selection in group)
            {
                var pool = await a.InventoryStocks().SingleOrDefaultAsync(x => x.Id == selection.StockId, ct) ?? throw Error("TRANSFER_STOCK_FORBIDDEN", 403);
                if (pool.WarehouseCd != from.WarehouseCd || pool.LocationCd != from.LocationCd || pool.ProductCd != identity.Product
                    || pool.UnitCd != identity.Unit || pool.OwnerType != l.Product.Owner.Kind || pool.OwnerCd != identity.Customer)
                    throw Error("TRANSFER_STOCK_IDENTITY_MISMATCH", 422);
                if (pool.RecallFlag || !StockQcStatus.IsAllocatable(pool.QcStatus)) throw Error("TRANSFER_STOCK_QUALITY_BLOCKED", 409);
                await a.RequireStockKey(to.WarehouseCd, to.LocationCd, pool.ProductCd, pool.LotNo, true, ct);
                a.AtCommit(async t =>
                {
                    await a.RequireStockKey(from.WarehouseCd, from.LocationCd, pool.ProductCd, pool.LotNo, false, t);
                    await a.RequireStockKey(to.WarehouseCd, to.LocationCd, pool.ProductCd, pool.LotNo, true, t);
                });
                result.Add(new(selection, from.WarehouseCd, from.LocationCd, to.WarehouseCd, to.LocationCd, pool.ProductCd, pool.LotNo, StockMovementService.TransferMetadata(pool)));
            }
        }
        foreach (var group in selections.GroupBy(x => x.StockId))
        {
            var pool = await db.Stocks.AsNoTracking().SingleAsync(x => x.Id == group.Key, ct); var amount = group.Sum(x => N(x.Quantity));
            if (pool.PhysicalQty < amount || pool.AvailableQty < amount || pool.AvailableQty != pool.PhysicalQty - pool.AllocatedQty || pool.AllocatedQty < 0)
                throw Error("TRANSFER_STOCK_QUANTITY_UNAVAILABLE", 409);
        }
        return result.OrderBy(x => x.Selection.RequestLineId).ThenBy(x => N(x.Selection.RequestFrom)).ThenBy(x => x.Selection.StockId).ToArray();
    }
    // Existing native quantity-capacity contract: zero is explicitly legacy-unlimited.
    // Full typed MetricSet/Root occupancy is a separate adapter; this does not fabricate that evidence.
    private async Task NativeCapacity(TransferStageLineV1[] lines, CancellationToken ct)
    {
        var deltas = new Dictionary<(string Warehouse, string Location), decimal>();
        async Task Add(string warehouse, string code, decimal delta)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                if (!seen.Add(code)) throw Error("TRANSFER_LOCATION_HIERARCHY_INVALID", 409);
                var l = await db.Locations.AsNoTracking().SingleAsync(x => x.WarehouseCd == warehouse && x.LocationCd == code && !x.IsDeleted, ct);
                if (l.IsBlocked || l.IsInitializationQuarantined || l.CapacityQty < 0 || l.ReservedCapacityQty < 0) throw Error("TRANSFER_LOCATION_UNAVAILABLE", 409);
                var key = (warehouse, code); deltas[key] = deltas.GetValueOrDefault(key) + delta;
                if (string.IsNullOrEmpty(l.ParentLocationCd)) break; code = l.ParentLocationCd;
            }
        }
        foreach (var l in lines) { await Add(l.SourceWarehouse, l.SourceLocation, -N(l.Selection.Quantity)); await Add(l.DestinationWarehouse, l.DestinationLocation, N(l.Selection.Quantity)); }
        foreach (var (key, delta) in deltas)
        {
            var all = await db.Locations.AsNoTracking().Where(x => x.WarehouseCd == key.Warehouse && !x.IsDeleted).ToArrayAsync(ct);
            var root = all.Single(x => x.LocationCd == key.Location); var codes = new HashSet<string> { root.LocationCd }; bool changed;
            do { changed = false; foreach (var l in all) if (l.ParentLocationCd != null && codes.Contains(l.ParentLocationCd)) changed |= codes.Add(l.LocationCd); } while (changed);
            var physical = await db.Stocks.Where(x => x.WarehouseCd == key.Warehouse && codes.Contains(x.LocationCd) && !x.IsDeleted).SumAsync(x => x.PhysicalQty, ct);
            var pending = all.Where(x => codes.Contains(x.LocationCd)).Sum(x => x.ReservedCapacityQty);
            if (physical + delta < 0 || root.CapacityQty > 0 && physical + delta + pending > root.CapacityQty) throw Error("TRANSFER_LOCATION_CAPACITY_EXCEEDED", 409);
        }
    }
}
