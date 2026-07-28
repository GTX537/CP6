using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

public class ReplenishService : IReplenishService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IMobileTaskV2Service _tasks;
    private readonly IWmsAccessScopeProvider _accessScopes;
    private const string Prefix = "RPL";
    private const string ForwardPrefix = "PIK-";   // ピッキング棚
    private const string ReservePrefix = "RES-";   // 保管棚

    public ReplenishService(
        CP6Context db,
        IWmsSequenceService seq,
        IMobileTaskV2Service tasks,
        IWmsAccessScopeProvider accessScopes)
    {
        _db = db;
        _seq = seq;
        _tasks = tasks;
        _accessScopes = accessScopes;
    }

    public async Task<List<ReplenishOrder>> SearchAsync(ReplenishSearchQuery q)
    {
        q.Page = Math.Max(1, q.Page);
        q.PageSize = Math.Clamp(q.PageSize, 1, 500);
        var query = await ScopedOrdersAsync(tracking: false);
        if (!string.IsNullOrWhiteSpace(q.ReplenishNo)) query = query.Where(x => x.ReplenishNo.Contains(q.ReplenishNo));
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.Priority.HasValue) query = query.Where(x => x.Priority == q.Priority.Value);
        return await query.OrderBy(x => x.Priority).ThenByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<ReplenishOrder?> GetAsync(string replenishNo)
        => await (await ScopedOrdersAsync(tracking: false))
            .FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo);

    public async Task<string> CreateAsync(ReplenishOrderDto dto, string? userName)
    {
        Validate(dto);
        await EnsureTargetAccessAsync(
            dto.WarehouseCd, dto.ToLocationCd);

        var no = await _seq.NextAsync(Prefix);
        var order = new ReplenishOrder
        {
            ReplenishNo = no,
            Status = ReplenishStatus.Pending,
            Creator = userName
        };
        Apply(order, dto);
        _db.ReplenishOrders.Add(order);
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateAsync(
        string replenishNo,
        ReplenishOrderDto dto,
        string? userName)
    {
        Validate(dto);
        await using var tx = await BeginTransactionAsync();
        try
        {
            var order = await (await ScopedOrdersAsync(tracking: true))
                    .FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo)
                ?? throw new InvalidOperationException("WM-MSG-070");
            await EnsureTargetAccessAsync(
                dto.WarehouseCd, dto.ToLocationCd);
            if (order.Status is ReplenishStatus.Executed
                or ReplenishStatus.Cancelled)
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-TASK-STARTED");

            var linked = await _tasks.GetSourceTasksAsync(
                "REPLENISH", replenishNo);
            if (linked.Any(x => x.Status is not (
                    MobileTaskStatus.Pending or MobileTaskStatus.Cancelled)))
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-TASK-STARTED");
            var pending = linked
                .Where(x => x.Status == MobileTaskStatus.Pending)
                .ToList();
            if (pending.Count > 1)
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-LINK-DUPLICATE");

            Apply(order, dto);
            order.Modifier = userName;
            order.ModifyDate = DateTime.Now;
            if (pending.Count == 1)
            {
                await _tasks.SynchronizePendingSourceTaskAsync(
                    pending[0].TaskNo,
                    BuildTaskRequest(order),
                    userName);
                order.Status = ReplenishStatus.TaskIssued;
            }
            else if (order.Status == ReplenishStatus.TaskIssued)
            {
                order.Status = ReplenishStatus.Pending;
            }

            await _db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<int> GenerateBatchAsync(string warehouseCd, decimal minQty, string? userName)
    {
        if (string.IsNullOrWhiteSpace(warehouseCd))
            throw new InvalidOperationException("warehouseCd required");
        warehouseCd = warehouseCd.Trim();
        if (minQty <= 0) minQty = 10m; // 既定 10
        var scope = await _accessScopes.GetCurrentAsync();
        if (!scope.AllowsAnyArea(warehouseCd))
            throw new WmsAccessDeniedException();
        var locations = scope.Apply(_db.Locations.AsNoTracking())
            .Where(x => !x.IsDeleted);

        // ピッキング棚（PIK-*）で minQty 未満の在庫を抽出
        var lowStocks = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseCd == warehouseCd
                        && s.LocationCd.StartsWith(ForwardPrefix)
                        && locations.Any(l =>
                            l.WarehouseCd == s.WarehouseCd
                            && l.LocationCd == s.LocationCd)
                        && !s.IsDeleted && !s.RecallFlag
                        && s.PhysicalQty < minQty
                        && s.OwnerType == StockOwnerType.Self)
            .ToListAsync();

        // 同 ProductCd で 保管棚（RES-*）に在庫あり の組合せ
        int created = 0;
        foreach (var low in lowStocks)
        {
            // 既存の未実行補充指示があればスキップ（重複防止）
            var dup = await _db.ReplenishOrders.AnyAsync(x =>
                !x.IsDeleted
                && (x.Status == ReplenishStatus.Pending
                    || x.Status == ReplenishStatus.TaskIssued)
                && x.WarehouseCd == warehouseCd
                && x.ProductCd == low.ProductCd
                && x.ToLocationCd == low.LocationCd);
            if (dup) continue;

            var source = await _db.Stocks.AsNoTracking()
                .Where(s => s.WarehouseCd == warehouseCd
                            && s.ProductCd == low.ProductCd
                            && s.LocationCd.StartsWith(ReservePrefix)
                            && !s.IsDeleted && !s.RecallFlag
                            && s.AvailableQty > 0
                            && s.OwnerType == StockOwnerType.Self)
                .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)
                .FirstOrDefaultAsync();
            if (source == null) continue;

            var needed = minQty - low.PhysicalQty;
            var actualQty = Math.Min(needed, source.AvailableQty);
            if (actualQty <= 0) continue;

            var no = await _seq.NextAsync(Prefix);
            _db.ReplenishOrders.Add(new ReplenishOrder
            {
                ReplenishNo = no, Priority = 2,
                ProductCd = low.ProductCd, ProductName = null,
                WarehouseCd = warehouseCd,
                FromLocationCd = source.LocationCd, ToLocationCd = low.LocationCd,
                LotNo = source.LotNo, Qty = actualQty,
                UnitCd = source.UnitCd,
                TriggerType = ReplenishTrigger.Batch,
                Status = ReplenishStatus.Pending,
                OperatorCd = userName,
                Remarks = $"バッチ自動：MinQty={minQty}, 現状={low.PhysicalQty}",
                Creator = userName,
            });
            created++;
        }
        await _db.SaveChangesAsync();
        return created;
    }

    public async Task<string> ExecuteAsync(
        string replenishNo,
        string? userName)
    {
        await using var tx = await BeginTransactionAsync();
        try
        {
            var order = await (await ScopedOrdersAsync(tracking: true))
                    .FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo)
                ?? throw new InvalidOperationException("WM-MSG-070");
            if (order.Status == ReplenishStatus.TaskIssued)
            {
                var existing = (await _tasks.GetSourceTasksAsync(
                        "REPLENISH", replenishNo))
                    .FirstOrDefault(x =>
                        x.Status != MobileTaskStatus.Cancelled);
                if (existing is null)
                    throw new MobileTaskConflictException(
                        "WM-V2-SOURCE-TASK-NOT-FOUND");
                if (tx is not null) await tx.CommitAsync();
                return existing.TaskNo;
            }
            if (order.Status != ReplenishStatus.Pending)
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-TASK-STARTED");

            var task = await _tasks.CreateAsync(
                BuildTaskRequest(order), userName);
            order.Status = ReplenishStatus.TaskIssued;
            order.OperatorCd = userName ?? order.OperatorCd;
            order.Modifier = userName;
            order.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
            return task.TaskNo;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    public async Task CancelAsync(string replenishNo, string? userName)
    {
        await using var tx = await BeginTransactionAsync();
        try
        {
            var order = await (await ScopedOrdersAsync(tracking: true))
                    .FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo)
                ?? throw new InvalidOperationException("WM-MSG-070");
            if (order.Status == ReplenishStatus.Executed)
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-TASK-STARTED");
            if (order.Status == ReplenishStatus.Cancelled)
            {
                if (tx is not null) await tx.CommitAsync();
                return;
            }

            await _tasks.CancelPendingSourceTasksAsync(
                "REPLENISH", replenishNo, userName);
            order.Status = ReplenishStatus.Cancelled;
            order.Modifier = userName;
            order.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    private static void Validate(ReplenishOrderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ProductCd)
            || string.IsNullOrWhiteSpace(dto.WarehouseCd)
            || string.IsNullOrWhiteSpace(dto.FromLocationCd)
            || string.IsNullOrWhiteSpace(dto.ToLocationCd))
            throw new InvalidOperationException("Required fields missing");
        if (dto.Qty <= 0)
            throw new InvalidOperationException("WM-MSG-021");
        if (string.Equals(dto.FromLocationCd.Trim(),
                dto.ToLocationCd.Trim(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WM-MSG-010");
    }

    private static void Apply(
        ReplenishOrder order,
        ReplenishOrderDto dto)
    {
        order.Priority = dto.Priority == 0 ? 2 : dto.Priority;
        order.ProductCd = dto.ProductCd.Trim();
        order.ProductName = dto.ProductName?.Trim();
        order.WarehouseCd = dto.WarehouseCd.Trim();
        order.FromLocationCd = dto.FromLocationCd.Trim();
        order.ToLocationCd = dto.ToLocationCd.Trim();
        order.LotNo = dto.LotNo?.Trim() ?? string.Empty;
        order.Qty = dto.Qty;
        order.UnitCd = dto.UnitCd?.Trim();
        order.TriggerType = string.IsNullOrWhiteSpace(dto.TriggerType)
            ? ReplenishTrigger.Manual
            : dto.TriggerType.Trim();
        order.OperatorCd = dto.OperatorCd?.Trim();
        order.Remarks = dto.Remarks?.Trim();
    }

    private static CreateMoveTaskV2Request BuildTaskRequest(
        ReplenishOrder order)
        => new()
        {
            OperationId = Guid.NewGuid(),
            Priority = order.Priority,
            WarehouseCd = order.WarehouseCd,
            FromLocationCd = order.FromLocationCd,
            ToLocationCd = order.ToLocationCd,
            ProductCd = order.ProductCd,
            ProductName = order.ProductName,
            LotNo = order.LotNo,
            Qty = order.Qty,
            UnitCd = order.UnitCd,
            Instruction = $"Replenish {order.ReplenishNo}",
            Remarks = order.Remarks,
            SourceType = "REPLENISH",
            SourceNo = order.ReplenishNo
        };

    private async Task<IQueryable<ReplenishOrder>> ScopedOrdersAsync(
        bool tracking)
    {
        var scope = await _accessScopes.GetCurrentAsync();
        // Keep the scope subquery tracking-neutral. Applying AsNoTracking here
        // also makes the outer replenishment query no-tracking in EF, which
        // would silently discard state changes made by update/execute/cancel.
        var locations = scope.Apply(_db.Locations)
            .Where(x => !x.IsDeleted);
        var orders = tracking
            ? _db.ReplenishOrders.AsQueryable()
            : _db.ReplenishOrders.AsNoTracking();
        return orders
            .Where(x => !x.IsDeleted)
            .Where(order => locations.Any(location =>
                location.WarehouseCd == order.WarehouseCd
                && location.LocationCd == order.ToLocationCd));
    }

    private async Task EnsureTargetAccessAsync(
        string warehouseCd,
        string toLocationCd)
    {
        var warehouse = warehouseCd.Trim();
        var locationCode = toLocationCd.Trim();
        var location = await _db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.WarehouseCd == warehouse
                                      && x.LocationCd == locationCode)
            ?? throw new InvalidOperationException(
                "WM-V2-LOCATION-NOT-FOUND");
        if (!(await _accessScopes.GetCurrentAsync())
            .Allows(warehouse, location.AreaCd))
            throw new WmsAccessDeniedException();
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync()
        => _db.Database.IsRelational()
           && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
}
