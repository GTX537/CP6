using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Transactions;

namespace CP6.Core.Services.Wms;

public sealed class LpnService : ILpnService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly CP6Context _db;
    private readonly IStockMovementService _stock;

    public LpnService(CP6Context db, IStockMovementService stock)
    {
        _db = db;
        _stock = stock;
    }

    public async Task<PagedResult<LogisticsUnitDto>> GetAsync(
        string? warehouseCd,
        string? locationCd,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.LogisticsUnits.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query = query.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(locationCd))
            query = query.Where(x => x.LocationCd == locationCd);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.LpnNo.Contains(search));
        var total = await query.CountAsync(ct);
        var units = await query.OrderBy(x => x.LpnNo)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<LogisticsUnitDto>();
        foreach (var unit in units) items.Add(await MapAsync(unit, ct));
        return new PagedResult<LogisticsUnitDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LogisticsUnitDto?> GetOneAsync(
        string lpnNo,
        CancellationToken ct = default)
    {
        var unit = await _db.LogisticsUnits.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.LpnNo == lpnNo, ct);
        return unit is null ? null : await MapAsync(unit, ct);
    }

    public async Task<LogisticsUnitDto> CreateAsync(
        CreateLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureOperation(request.OperationId);
        ValidateLpn(request.LpnNo, request.ContainerType,
            request.WarehouseCd, request.LocationCd);
        var key = Key(request.LpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "create", ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        await EnsureFeatureAsync(request.WarehouseCd, ct);
        if (!await _db.Locations.AnyAsync(x => !x.IsDeleted
                                              && x.WarehouseCd == request.WarehouseCd
                                              && x.LocationCd == request.LocationCd, ct))
            throw new ArgumentException("WM-V2-LOCATION-NOT-FOUND");

        var unit = new LogisticsUnit
        {
            LpnNo = request.LpnNo.Trim(),
            ContainerType = request.ContainerType.Trim(),
            WarehouseCd = request.WarehouseCd.Trim(),
            LocationCd = request.LocationCd.Trim(),
            Creator = userName
        };
        _db.LogisticsUnits.Add(unit);
        _db.LpnClosures.Add(new LpnClosure
        {
            AncestorLpnNo = unit.LpnNo,
            DescendantLpnNo = unit.LpnNo,
            Depth = 0
        });
        AddEvent(unit, request.OperationId, "Created", userName, request.DeviceId);
        await _db.SaveChangesAsync(ct);
        var result = await MapAsync(unit, ct);
        AddReceipt(key, request.OperationId, "create", result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    public async Task<LogisticsUnitDto> PackAsync(
        string lpnNo,
        PackLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        var key = Key(lpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "pack", ct);
        if (replay is not null) return replay;
        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        try
        {
            var parent = await LoadAsync(lpnNo, true, ct);
            ApplyRowVersion(parent, request.RowVersion);
            await EnsureFeatureAsync(parent.WarehouseCd, ct);
            var children = await _db.LogisticsUnits
                .Where(x => !x.IsDeleted && request.ChildLpns.Contains(x.LpnNo))
                .ToListAsync(ct);
            if (children.Count != request.ChildLpns.Distinct().Count())
                throw new ArgumentException("WM-LPN-CHILD-NOT-FOUND");
            foreach (var child in children)
            {
                if (child.LpnNo == parent.LpnNo
                    || child.ParentLpnNo is not null
                    || child.WarehouseCd != parent.WarehouseCd
                    || child.LocationCd != parent.LocationCd
                    || await _db.LpnClosures.AnyAsync(x =>
                        x.AncestorLpnNo == child.LpnNo
                        && x.DescendantLpnNo == parent.LpnNo, ct))
                    throw new MobileTaskConflictException("WM-LPN-CYCLE-OR-SCOPE");
                child.ParentLpnNo = parent.LpnNo;
            }

            ValidateContentInputs(request.Contents);
            await EnsureMixAllowedAsync(parent, request.Contents, ct);
            await EnsureContentWithinStockAsync(parent, request.Contents, ct);
            foreach (var input in request.Contents)
            {
                if (!string.IsNullOrWhiteSpace(input.SerialNo))
                {
                    var serial = await _db.StockSerials.FirstOrDefaultAsync(x =>
                        !x.IsDeleted
                        && x.ProductCd == input.ProductCd
                        && x.SerialNo == input.SerialNo, ct)
                        ?? throw new ArgumentException("WM-LPN-SERIAL-NOT-FOUND");
                    if (serial.LpnNo is not null
                        || serial.WarehouseCd != parent.WarehouseCd
                        || serial.LocationCd != parent.LocationCd
                        || serial.LotNo != (input.LotNo ?? string.Empty)
                        || serial.Status == StockSerialStatus.Shipped)
                        throw new MobileTaskConflictException("WM-LPN-SERIAL-NOT-PACKABLE");
                    serial.LpnNo = parent.LpnNo;
                }
                _db.LpnContents.Add(new LpnContent
                {
                    LpnNo = parent.LpnNo,
                    ProductCd = input.ProductCd.Trim(),
                    LotNo = input.LotNo?.Trim() ?? string.Empty,
                    SerialNo = NullIfWhiteSpace(input.SerialNo),
                    Qty = input.SerialNo is null ? input.Qty : 1m,
                    Creator = userName
                });
            }
            await RebuildClosureAsync(ct);
            Stamp(parent, userName);
            AddEvent(parent, request.OperationId, "Packed", userName,
                request.DeviceId, new
                {
                    childLpns = request.ChildLpns,
                    contentCount = request.Contents.Count
                });
            await _db.SaveChangesAsync(ct);
            var result = await MapAsync(parent, ct);
            AddReceipt(key, request.OperationId, "pack", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task<LogisticsUnitDto> UnpackAsync(
        string lpnNo,
        UnpackLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        var key = Key(lpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "unpack", ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        var parent = await LoadAsync(lpnNo, true, ct);
        ApplyRowVersion(parent, request.RowVersion);
        var children = await _db.LogisticsUnits
            .Where(x => !x.IsDeleted
                        && request.ChildLpns.Contains(x.LpnNo)
                        && x.ParentLpnNo == parent.LpnNo)
            .ToListAsync(ct);
        if (children.Count != request.ChildLpns.Distinct().Count())
            throw new MobileTaskConflictException("WM-LPN-CHILD-NOT-PACKED");
        foreach (var child in children) child.ParentLpnNo = null;

        var contents = await _db.LpnContents
            .Where(x => !x.IsDeleted
                        && x.LpnNo == parent.LpnNo
                        && x.SerialNo != null
                        && request.SerialNos.Contains(x.SerialNo))
            .ToListAsync(ct);
        if (contents.Count != request.SerialNos.Distinct().Count())
            throw new MobileTaskConflictException("WM-LPN-CONTENT-NOT-FOUND");
        var serials = await LoadContentSerialsAsync(contents, ct);
        foreach (var serial in serials) serial.LpnNo = null;
        _db.LpnContents.RemoveRange(contents);
        await RebuildClosureAsync(ct);
        Stamp(parent, userName);
        AddEvent(parent, request.OperationId, "Unpacked", userName,
            request.DeviceId, new { request.ChildLpns, request.SerialNos });
        await _db.SaveChangesAsync(ct);
        var result = await MapAsync(parent, ct);
        AddReceipt(key, request.OperationId, "unpack", result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    public async Task<LogisticsUnitDto> MoveAsync(
        string lpnNo,
        MoveLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        if (string.IsNullOrWhiteSpace(request.ToLocationCd))
            throw new ArgumentException("WM-LPN-TARGET-REQUIRED");
        var key = Key(lpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "move", ct);
        if (replay is not null) return replay;
        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        try
        {
            var root = await LoadAsync(lpnNo, true, ct);
            ApplyRowVersion(root, request.RowVersion);
            var targetCd = request.ToLocationCd.Trim();
            var target = await _db.Locations.FirstOrDefaultAsync(x =>
                !x.IsDeleted
                && x.WarehouseCd == root.WarehouseCd
                && x.LocationCd == targetCd, ct)
                ?? throw new ArgumentException("WM-V2-LOCATION-NOT-FOUND");
            if (target.IsBlocked)
                throw new MobileTaskConflictException("WM-V2-LOCATION-BLOCKED");
            if (root.LocationCd == targetCd) return await MapAsync(root, ct);
            var sourceLocationCd = root.LocationCd;

            var descendantNos = await _db.LpnClosures.AsNoTracking()
                .Where(x => x.AncestorLpnNo == root.LpnNo)
                .Select(x => x.DescendantLpnNo).ToListAsync(ct);
            var units = await _db.LogisticsUnits
                .Where(x => !x.IsDeleted && descendantNos.Contains(x.LpnNo))
                .ToListAsync(ct);
            if (units.Any(x => x.WarehouseCd != root.WarehouseCd
                               || x.LocationCd != root.LocationCd))
                throw new MobileTaskConflictException("WM-LPN-TREE-LOCATION-MISMATCH");
            var contents = await _db.LpnContents
                .Where(x => !x.IsDeleted && descendantNos.Contains(x.LpnNo))
                .ToListAsync(ct);
            var totalQty = contents.Sum(x => x.Qty);
            await EnsureTargetCapacityAsync(target, totalQty, ct);
            foreach (var group in contents.GroupBy(x => new { x.ProductCd, x.LotNo }))
            {
                var moved = await _stock.MoveAsync(new StockMoveRequest
                {
                    WarehouseCd = root.WarehouseCd,
                    FromLocationCd = sourceLocationCd,
                    ToLocationCd = targetCd,
                    ProductCd = group.Key.ProductCd,
                    LotNo = group.Key.LotNo,
                    Qty = group.Sum(x => x.Qty),
                    OperatorCd = userName,
                    Remark = $"Atomic LPN tree move {root.LpnNo}"
                }, ct);
                _ = moved;
            }
            var serials = await LoadContentSerialsAsync(contents, ct);
            foreach (var serial in serials)
            {
                var from = serial.LocationCd;
                serial.LocationCd = targetCd;
                serial.LastTxnNo = AddSerialLedger(
                    request.OperationId, serial, from, targetCd,
                    userName, request.DeviceId);
            }
            foreach (var unit in units)
            {
                unit.LocationCd = targetCd;
                Stamp(unit, userName);
            }
            AddEvent(root, request.OperationId, "TreeMoved", userName,
                request.DeviceId, new { from = sourceLocationCd, to = targetCd, totalQty });
            await _db.SaveChangesAsync(ct);
            var result = await MapAsync(root, ct);
            AddReceipt(key, request.OperationId, "move", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task<LogisticsUnitDto> SplitAsync(
        string lpnNo,
        SplitLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        if (string.IsNullOrWhiteSpace(request.TargetLpnNo)
            || string.IsNullOrWhiteSpace(request.TargetContainerType))
            throw new ArgumentException("WM-LPN-SPLIT-DATA");
        var key = Key(lpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "split", ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        var source = await LoadAsync(lpnNo, true, ct);
        ApplyRowVersion(source, request.RowVersion);
        if (await _db.LogisticsUnits.AnyAsync(
            x => !x.IsDeleted && x.LpnNo == request.TargetLpnNo, ct))
            throw new MobileTaskConflictException("WM-LPN-ALREADY-EXISTS");
        var target = new LogisticsUnit
        {
            LpnNo = request.TargetLpnNo.Trim(),
            ContainerType = request.TargetContainerType.Trim(),
            WarehouseCd = source.WarehouseCd,
            LocationCd = source.LocationCd,
            Creator = userName
        };
        _db.LogisticsUnits.Add(target);
        var contents = await _db.LpnContents
            .Where(x => !x.IsDeleted
                        && x.LpnNo == source.LpnNo
                        && x.SerialNo != null
                        && request.SerialNos.Contains(x.SerialNo))
            .ToListAsync(ct);
        if (contents.Count != request.SerialNos.Distinct().Count())
            throw new MobileTaskConflictException("WM-LPN-CONTENT-NOT-FOUND");
        var serials = await LoadContentSerialsAsync(contents, ct);
        foreach (var content in contents) content.LpnNo = target.LpnNo;
        foreach (var serial in serials) serial.LpnNo = target.LpnNo;
        var children = await _db.LogisticsUnits
            .Where(x => !x.IsDeleted
                        && request.ChildLpns.Contains(x.LpnNo)
                        && x.ParentLpnNo == source.LpnNo)
            .ToListAsync(ct);
        if (children.Count != request.ChildLpns.Distinct().Count())
            throw new MobileTaskConflictException("WM-LPN-CHILD-NOT-PACKED");
        foreach (var child in children) child.ParentLpnNo = target.LpnNo;
        await EnsureMixAllowedAsync(target,
            contents.Select(x => new LpnContentInput
            {
                ProductCd = x.ProductCd,
                LotNo = x.LotNo,
                SerialNo = x.SerialNo,
                Qty = x.Qty
            }).ToList(), ct);
        await RebuildClosureAsync(ct);
        Stamp(source, userName);
        AddEvent(source, request.OperationId, "Split", userName,
            request.DeviceId, new { target = target.LpnNo });
        AddEvent(target, request.OperationId, "CreatedBySplit", userName,
            request.DeviceId, new { source = source.LpnNo });
        await _db.SaveChangesAsync(ct);
        var result = await MapAsync(source, ct);
        AddReceipt(key, request.OperationId, "split", result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    public async Task<LogisticsUnitDto> MergeAsync(
        string lpnNo,
        MergeLpnRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        var key = Key(lpnNo);
        var replay = await ReplayAsync(key, request.OperationId, "merge", ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        var target = await LoadAsync(lpnNo, true, ct);
        ApplyRowVersion(target, request.RowVersion);
        var source = await LoadAsync(request.SourceLpnNo, true, ct);
        if (source.LpnNo == target.LpnNo
            || source.ParentLpnNo is not null
            || source.WarehouseCd != target.WarehouseCd
            || source.LocationCd != target.LocationCd
            || await _db.LpnClosures.AnyAsync(x =>
                (x.AncestorLpnNo == source.LpnNo && x.DescendantLpnNo == target.LpnNo)
                || (x.AncestorLpnNo == target.LpnNo && x.DescendantLpnNo == source.LpnNo), ct))
            throw new MobileTaskConflictException("WM-LPN-MERGE-SCOPE");
        var sourceContents = await _db.LpnContents
            .Where(x => !x.IsDeleted && x.LpnNo == source.LpnNo)
            .ToListAsync(ct);
        var serials = await LoadContentSerialsAsync(sourceContents, ct);
        await EnsureMixAllowedAsync(target,
            sourceContents.Select(x => new LpnContentInput
            {
                ProductCd = x.ProductCd,
                LotNo = x.LotNo,
                SerialNo = x.SerialNo,
                Qty = x.Qty
        }).ToList(), ct);
        foreach (var content in sourceContents) content.LpnNo = target.LpnNo;
        foreach (var serial in serials) serial.LpnNo = target.LpnNo;
        var children = await _db.LogisticsUnits
            .Where(x => !x.IsDeleted && x.ParentLpnNo == source.LpnNo)
            .ToListAsync(ct);
        foreach (var child in children) child.ParentLpnNo = target.LpnNo;
        source.IsDeleted = true;
        source.Status = "MERGED";
        await RebuildClosureAsync(ct);
        Stamp(target, userName);
        AddEvent(target, request.OperationId, "Merged", userName,
            request.DeviceId, new { source = source.LpnNo });
        await _db.SaveChangesAsync(ct);
        var result = await MapAsync(target, ct);
        AddReceipt(key, request.OperationId, "merge", result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    public async Task<LpnPolicyRequest> UpsertPolicyAsync(
        LpnPolicyRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.WarehouseCd)
            || string.IsNullOrWhiteSpace(request.ContainerType))
            throw new ArgumentException("WM-LPN-POLICY-DATA");
        var row = await _db.LpnPolicies.FirstOrDefaultAsync(x =>
            !x.IsDeleted
            && x.WarehouseCd == request.WarehouseCd
            && x.ContainerType == request.ContainerType, ct);
        if (row is null)
        {
            row = new LpnPolicy
            {
                WarehouseCd = request.WarehouseCd.Trim(),
                ContainerType = request.ContainerType.Trim(),
                Creator = userName
            };
            _db.LpnPolicies.Add(row);
        }
        row.AllowMixedProducts = request.AllowMixedProducts;
        row.AllowMixedLots = request.AllowMixedLots;
        row.Modifier = userName;
        row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return request;
    }

    private async Task EnsureMixAllowedAsync(
        LogisticsUnit target,
        IReadOnlyCollection<LpnContentInput> incoming,
        CancellationToken ct)
    {
        if (incoming.Count == 0) return;
        var policy = await _db.LpnPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.WarehouseCd == target.WarehouseCd
                                      && x.ContainerType == target.ContainerType, ct);
        var descendants = await _db.LpnClosures.AsNoTracking()
            .Where(x => x.AncestorLpnNo == target.LpnNo)
            .Select(x => x.DescendantLpnNo).ToListAsync(ct);
        if (descendants.Count == 0) descendants.Add(target.LpnNo);
        var current = await _db.LpnContents.AsNoTracking()
            .Where(x => !x.IsDeleted && descendants.Contains(x.LpnNo))
            .Select(x => new { x.ProductCd, x.LotNo })
            .ToListAsync(ct);
        var products = current.Select(x => x.ProductCd)
            .Concat(incoming.Select(x => x.ProductCd))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var lots = current.Select(x => x.LotNo)
            .Concat(incoming.Select(x => x.LotNo ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (products > 1 && policy?.AllowMixedProducts != true)
            throw new MobileTaskConflictException("WM-LPN-MIXED-PRODUCTS");
        if (lots > 1 && policy?.AllowMixedLots != true)
            throw new MobileTaskConflictException("WM-LPN-MIXED-LOTS");
    }

    private async Task EnsureContentWithinStockAsync(
        LogisticsUnit unit,
        IReadOnlyCollection<LpnContentInput> incoming,
        CancellationToken ct)
    {
        foreach (var bucket in incoming.GroupBy(x =>
                     new { ProductCd = x.ProductCd.Trim(), LotNo = x.LotNo?.Trim() ?? string.Empty }))
        {
            var physical = await _db.Stocks.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.WarehouseCd == unit.WarehouseCd
                            && x.LocationCd == unit.LocationCd
                            && x.ProductCd == bucket.Key.ProductCd
                            && x.LotNo == bucket.Key.LotNo)
                .SumAsync(x => (decimal?)x.PhysicalQty, ct) ?? 0m;
            var packed = await (from content in _db.LpnContents.AsNoTracking()
                                join lpn in _db.LogisticsUnits.AsNoTracking()
                                    on content.LpnNo equals lpn.LpnNo
                                where !content.IsDeleted && !lpn.IsDeleted
                                      && lpn.WarehouseCd == unit.WarehouseCd
                                      && lpn.LocationCd == unit.LocationCd
                                      && content.ProductCd == bucket.Key.ProductCd
                                      && content.LotNo == bucket.Key.LotNo
                                select content.Qty)
                .SumAsync(x => (decimal?)x, ct) ?? 0m;
            if (packed + bucket.Sum(x => x.SerialNo is null ? x.Qty : 1m) > physical)
                throw new MobileTaskConflictException("WM-LPN-CONTENT-EXCEEDS-STOCK");
        }
    }

    private async Task<List<StockSerial>> LoadContentSerialsAsync(
        IReadOnlyCollection<LpnContent> contents,
        CancellationToken ct)
    {
        var serializedContents = contents
            .Where(x => !string.IsNullOrWhiteSpace(x.SerialNo))
            .ToList();
        if (serializedContents.Count == 0) return [];

        var expectedLpnBySerial = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var content in serializedContents)
        {
            var key = SerialKey(content.ProductCd, content.SerialNo!);
            if (!expectedLpnBySerial.TryAdd(key, content.LpnNo))
                throw new MobileTaskConflictException(
                    "WM-LPN-SERIAL-CONTENT-MISMATCH");
        }

        var serialNos = serializedContents
            .Select(x => x.SerialNo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidates = await _db.StockSerials
            .Where(x => !x.IsDeleted && serialNos.Contains(x.SerialNo))
            .ToListAsync(ct);
        var serials = candidates
            .Where(x => expectedLpnBySerial.ContainsKey(
                SerialKey(x.ProductCd, x.SerialNo)))
            .ToList();
        if (serials.Count != expectedLpnBySerial.Count
            || serials.Any(x =>
                !expectedLpnBySerial.TryGetValue(
                    SerialKey(x.ProductCd, x.SerialNo),
                    out var expectedLpn)
                || !string.Equals(
                    x.LpnNo,
                    expectedLpn,
                    StringComparison.OrdinalIgnoreCase)))
            throw new MobileTaskConflictException(
                "WM-LPN-SERIAL-CONTENT-MISMATCH");
        return serials;
    }

    private static string SerialKey(string productCd, string serialNo)
        => $"{productCd.Trim()}\u001f{serialNo.Trim()}";

    private async Task EnsureTargetCapacityAsync(
        Location target,
        decimal qty,
        CancellationToken ct)
    {
        if (target.CapacityQty <= 0m) return;
        var physical = await _db.Stocks.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.WarehouseCd == target.WarehouseCd
                        && x.LocationCd == target.LocationCd)
            .SumAsync(x => (decimal?)x.PhysicalQty, ct) ?? 0m;
        if (physical + target.ReservedCapacityQty + qty > target.CapacityQty)
            throw new MobileTaskConflictException("WM-V2-TARGET-CAPACITY");
    }

    private async Task RebuildClosureAsync(CancellationToken ct)
    {
        var existing = await _db.LpnClosures.ToListAsync(ct);
        _db.LpnClosures.RemoveRange(existing);
        var units = (await _db.LogisticsUnits.ToListAsync(ct))
            .Where(x => !x.IsDeleted)
            .ToList();
        foreach (var local in _db.LogisticsUnits.Local
                     .Where(x => !x.IsDeleted
                                 && _db.Entry(x).State != EntityState.Deleted))
        {
            var index = units.FindIndex(x =>
                string.Equals(x.LpnNo, local.LpnNo,
                    StringComparison.OrdinalIgnoreCase));
            if (index >= 0) units[index] = local;
            else units.Add(local);
        }
        var byNo = units.ToDictionary(x => x.LpnNo, StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units)
        {
            _db.LpnClosures.Add(new LpnClosure
            {
                AncestorLpnNo = unit.LpnNo,
                DescendantLpnNo = unit.LpnNo,
                Depth = 0
            });
            var current = unit;
            var depth = 1;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { unit.LpnNo };
            while (!string.IsNullOrWhiteSpace(current.ParentLpnNo))
            {
                if (!visited.Add(current.ParentLpnNo)
                    || !byNo.TryGetValue(current.ParentLpnNo, out var parent))
                    throw new MobileTaskConflictException("WM-LPN-CYCLE");
                _db.LpnClosures.Add(new LpnClosure
                {
                    AncestorLpnNo = parent.LpnNo,
                    DescendantLpnNo = unit.LpnNo,
                    Depth = depth++
                });
                current = parent;
            }
        }
    }

    private string AddSerialLedger(
        Guid operationId,
        StockSerial serial,
        string from,
        string to,
        string? userName,
        string? deviceId)
    {
        var txnNo = $"S{DateTime.UtcNow:yyMMdd}{Guid.NewGuid():N}"[..25];
        _db.StockSerialTransactions.Add(new StockSerialTransaction
        {
            TxnNo = txnNo,
            OperationId = operationId,
            TxnType = "LPN_MOVE",
            ProductCd = serial.ProductCd,
            SerialNo = serial.SerialNo,
            WarehouseCd = serial.WarehouseCd,
            LotNo = serial.LotNo,
            FromLocationCd = from,
            ToLocationCd = to,
            LpnNo = serial.LpnNo,
            OperatorCd = userName,
            DeviceId = NullIfWhiteSpace(deviceId)
        });
        return txnNo;
    }

    private void AddEvent(
        LogisticsUnit unit,
        Guid operationId,
        string eventType,
        string? userName,
        string? deviceId,
        object? data = null)
        => _db.LpnEvents.Add(new LpnEvent
        {
            TenantId = unit.TenantId == Guid.Empty
                ? _db.CurrentTenantId
                : unit.TenantId,
            LpnNo = unit.LpnNo,
            OperationId = operationId,
            EventType = eventType,
            UserName = userName,
            DeviceId = NullIfWhiteSpace(deviceId),
            DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions)
        });

    private async Task<LogisticsUnitDto> MapAsync(
        LogisticsUnit unit,
        CancellationToken ct)
    {
        var contents = await _db.LpnContents.AsNoTracking()
            .Where(x => !x.IsDeleted && x.LpnNo == unit.LpnNo)
            .OrderBy(x => x.ProductCd).ThenBy(x => x.SerialNo)
            .Select(x => new LpnContentDto
            {
                ProductCd = x.ProductCd,
                LotNo = x.LotNo,
                SerialNo = x.SerialNo,
                Qty = x.Qty
            }).ToListAsync(ct);
        var children = await _db.LogisticsUnits.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ParentLpnNo == unit.LpnNo)
            .OrderBy(x => x.LpnNo).Select(x => x.LpnNo).ToListAsync(ct);
        return new LogisticsUnitDto
        {
            LpnNo = unit.LpnNo,
            ContainerType = unit.ContainerType,
            WarehouseCd = unit.WarehouseCd,
            LocationCd = unit.LocationCd,
            ParentLpnNo = unit.ParentLpnNo,
            Status = unit.Status,
            Contents = contents,
            ChildLpns = children,
            RowVersion = unit.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(unit.RowVersion)
                : string.Empty
        };
    }

    private async Task<LogisticsUnitDto?> ReplayAsync(
        string key,
        Guid operationId,
        string command,
        CancellationToken ct)
    {
        var receipt = await _db.TaskCommandReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.TaskNo != key || receipt.CommandName != command)
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
        return JsonSerializer.Deserialize<LogisticsUnitDto>(
            receipt.ResultJson, JsonOptions);
    }

    private void AddReceipt(
        string key,
        Guid operationId,
        string command,
        LogisticsUnitDto result)
        => _db.TaskCommandReceipts.Add(new TaskCommandReceipt
        {
            OperationId = operationId,
            TaskNo = key,
            CommandName = command,
            ResultJson = JsonSerializer.Serialize(result, JsonOptions)
        });

    private async Task<LogisticsUnit> LoadAsync(
        string lpnNo,
        bool tracking,
        CancellationToken ct)
    {
        var query = tracking
            ? _db.LogisticsUnits.AsQueryable()
            : _db.LogisticsUnits.AsNoTracking();
        return await query.FirstOrDefaultAsync(
                   x => !x.IsDeleted && x.LpnNo == lpnNo, ct)
               ?? throw new MobileTaskNotFoundException();
    }

    private async Task EnsureFeatureAsync(string warehouseCd, CancellationToken ct)
    {
        if (!await _db.WmsFeatureFlags.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted
                           && x.WarehouseCd == warehouseCd
                           && x.SerialLpnEnabled, ct))
            throw new MobileTaskConflictException("WM-R2B-FEATURE-DISABLED");
    }

    private void ApplyRowVersion(LogisticsUnit unit, string encoded)
    {
        byte[] supplied;
        try { supplied = Convert.FromBase64String(encoded); }
        catch (FormatException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        var current = unit.RowVersion ?? Array.Empty<byte>();
        if (current.Length > 0
            && (supplied.Length == 0
                || !CryptographicOperations.FixedTimeEquals(current, supplied)))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        if (current.Length > 0)
            _db.Entry(unit).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private static void ValidateContentInputs(IEnumerable<LpnContentInput> inputs)
    {
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.ProductCd)
                || (string.IsNullOrWhiteSpace(input.SerialNo) && input.Qty <= 0m)
                || (!string.IsNullOrWhiteSpace(input.SerialNo) && input.Qty is not (0m or 1m)))
                throw new ArgumentException("WM-LPN-CONTENT-DATA");
        }
    }

    private static void ValidateLpn(
        string lpnNo,
        string type,
        string warehouse,
        string location)
    {
        if (string.IsNullOrWhiteSpace(lpnNo)
            || lpnNo.Length > 64
            || string.IsNullOrWhiteSpace(type)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(location))
            throw new ArgumentException("WM-LPN-DATA");
    }

    private static void EnsureCommand(LpnCommand command)
    {
        EnsureOperation(command.OperationId);
        if (string.IsNullOrWhiteSpace(command.RowVersion))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
    }

    private static void EnsureOperation(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("WM-V2-OPERATION-ID-REQUIRED");
    }

    private static void Stamp(LogisticsUnit unit, string? userName)
    {
        unit.Modifier = userName;
        unit.ModifyDate = DateTime.Now;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken ct)
        => _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

    private TransactionScope? BeginAmbientTransaction()
        => _db.Database.IsRelational()
            ? new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions
                {
                    IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted
                },
                TransactionScopeAsyncFlowOption.Enabled)
            : null;

    private static string Key(string lpnNo) => $"LPN:{lpnNo.Trim()}";
    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
