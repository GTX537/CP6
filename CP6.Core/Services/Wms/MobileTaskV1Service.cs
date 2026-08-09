using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

public sealed class MobileTaskV1Service : IMobileTaskV1Service
{
    private const string Prefix = "MTK";
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _sequence;
    private readonly IStockMovementService _stock;
    private readonly IMobileService _legacyMobile;
    private readonly IMobileTaskNotifier _taskNotifier;
    private readonly IWmsNotifier _stockNotifier;

    public MobileTaskV1Service(
        CP6Context db,
        IWmsSequenceService sequence,
        IStockMovementService stock,
        IMobileService legacyMobile,
        IMobileTaskNotifier? taskNotifier = null,
        IWmsNotifier? stockNotifier = null)
    {
        _db = db;
        _sequence = sequence;
        _stock = stock;
        _legacyMobile = legacyMobile;
        _taskNotifier = taskNotifier ?? new NoOpMobileTaskNotifier();
        _stockNotifier = stockNotifier ?? new NoOpWmsNotifier();
    }

    public async Task<PagedResult<MobileTaskV1Dto>> GetTasksAsync(
        MobileTaskV1Query query,
        CancellationToken ct = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 1, 200);

        var tasks = _db.MobileTasks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TaskType == MobileTaskType.Move);

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignedTo = query.AssignedTo.Trim();
            tasks = query.IncludeUnassigned
                ? tasks.Where(x => x.AssignedTo == assignedTo || x.AssignedTo == null)
                : tasks.Where(x => x.AssignedTo == assignedTo);
        }

        if (query.Status.HasValue)
            tasks = tasks.Where(x => x.Status == query.Status.Value);
        if (query.OpenOnly)
            tasks = tasks.Where(x => x.Status == MobileTaskStatus.Pending || x.Status == MobileTaskStatus.InProgress);

        var total = await tasks.CountAsync(ct);
        var items = await tasks
            .OrderBy(x => x.Status)
            .ThenBy(x => x.Priority)
            .ThenByDescending(x => x.CreateDate)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<MobileTaskV1Dto>
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<MobileTaskV1Dto?> GetAsync(string taskNo, CancellationToken ct = default)
    {
        var task = await _db.MobileTasks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MobileTaskNo == taskNo && !x.IsDeleted && x.TaskType == MobileTaskType.Move, ct);
        return task == null ? null : Map(task);
    }

    public async Task<MobileTaskV1Dto> CreateAsync(
        CreateMoveTaskRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateMove(request);
        var task = new MobileTask
        {
            MobileTaskNo = await _sequence.NextAsync(Prefix),
            TaskType = MobileTaskType.Move,
            AssignedTo = NullIfWhiteSpace(request.AssignedTo),
            Priority = request.Priority is >= 1 and <= 9 ? request.Priority : 2,
            Status = MobileTaskStatus.Pending,
            WarehouseCd = request.WarehouseCd.Trim(),
            FromLocationCd = request.FromLocationCd.Trim(),
            ToLocationCd = request.ToLocationCd.Trim(),
            ProductCd = request.ProductCd.Trim(),
            ProductName = NullIfWhiteSpace(request.ProductName),
            LotNo = request.LotNo?.Trim() ?? string.Empty,
            Qty = request.Qty,
            UnitCd = NullIfWhiteSpace(request.UnitCd),
            Instruction = NullIfWhiteSpace(request.Instruction),
            Remarks = NullIfWhiteSpace(request.Remarks),
            Creator = userName,
        };
        _db.MobileTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        await NotifyAsync(task, "MobileTaskCreated", ct);
        return Map(task);
    }

    public async Task<MobileTaskV1Dto> AssignAsync(
        string taskNo,
        AssignTaskRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AssignedTo))
            throw new ArgumentException("WM-MSG-305");

        var task = await LoadAsync(taskNo, ct);
        EnsurePending(task, "WM-CONFLICT-TASK-STARTED");
        ApplyRowVersion(task, request.RowVersion);
        task.AssignedTo = request.AssignedTo.Trim();
        Stamp(task, userName);
        await SaveWithConflictAsync(ct);
        await NotifyAsync(task, "MobileTaskAssigned", ct);
        return Map(task);
    }

    public async Task<MobileTaskV1Dto> ClaimAsync(
        string taskNo,
        ClaimTaskRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request.DeviceId))
            throw new ArgumentException("WM-MSG-305");

        var task = await LoadAsync(taskNo, ct);
        if (task.Status != MobileTaskStatus.Pending || task.AssignedTo != null)
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-CLAIMED");

        ApplyRowVersion(task, request.RowVersion);
        task.AssignedTo = userName;
        task.Status = MobileTaskStatus.InProgress;
        task.StartedAt = DateTime.Now;
        Stamp(task, userName);
        await SaveWithConflictAsync(ct, "WM-CONFLICT-TASK-CLAIMED");
        await NotifyAsync(task, "MobileTaskStarted", ct);
        return Map(task);
    }

    public async Task<MobileTaskV1Dto> StartAsync(
        string taskNo,
        StartTaskRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        var task = await LoadAsync(taskNo, ct);
        EnsurePending(task, "WM-CONFLICT-TASK-STARTED");
        if (task.AssignedTo == null || !string.Equals(task.AssignedTo, userName, StringComparison.OrdinalIgnoreCase))
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-NOT-ASSIGNED");

        ApplyRowVersion(task, request.RowVersion);
        task.Status = MobileTaskStatus.InProgress;
        task.StartedAt = DateTime.Now;
        Stamp(task, userName);
        await SaveWithConflictAsync(ct);
        await NotifyAsync(task, "MobileTaskStarted", ct);
        return Map(task);
    }

    public async Task<MobileScanResult> ScanAsync(
        string taskNo,
        MobileScanRequest request,
        CancellationToken ct = default)
    {
        _ = ct;
        var task = await LoadAsync(taskNo, ct);
        if (task.Status != MobileTaskStatus.InProgress)
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-NOT-STARTED");
        request.TaskNo = taskNo;
        request.WarehouseCd ??= task.WarehouseCd;
        return await _legacyMobile.ScanAsync(request);
    }

    public async Task<MobileTaskV1Dto> CompleteAsync(
        string taskNo,
        CompleteMoveRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty)
            throw new ArgumentException("WM-MSG-306");

        var task = await LoadAsync(taskNo, ct);
        if (task.CompletionOperationId == request.OperationId)
            return Map(task);
        if (task.Status == MobileTaskStatus.Completed)
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-ALREADY-COMPLETED");
        if (task.Status != MobileTaskStatus.InProgress)
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-NOT-STARTED");
        if (!string.Equals(task.AssignedTo, userName, StringComparison.OrdinalIgnoreCase))
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-NOT-ASSIGNED");
        if (request.ScannedQty <= 0)
            throw new ArgumentException("WM-MSG-031");
        if (request.ScannedQty != task.Qty)
            throw new ArgumentException("WM-SCAN-QTY-MISMATCH");
        if (!string.Equals(task.ToLocationCd, request.ToLocationCd?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("WM-MSG-302");
        if (string.IsNullOrWhiteSpace(task.WarehouseCd)
            || string.IsNullOrWhiteSpace(task.FromLocationCd)
            || string.IsNullOrWhiteSpace(task.ToLocationCd)
            || string.IsNullOrWhiteSpace(task.ProductCd))
            throw new ArgumentException("WM-MSG-303");

        var operationUsed = await _db.MobileTasks.AsNoTracking()
            .AnyAsync(x => x.CompletionOperationId == request.OperationId
                           && x.MobileTaskNo != taskNo, ct);
        if (operationUsed)
            throw new MobileTaskConflictException("WM-CONFLICT-OPERATION-ID-USED");

        ApplyRowVersion(task, request.RowVersion);
        IDbContextTransaction? tx = null;
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
            && _db.Database.CurrentTransaction == null)
            tx = await _db.Database.BeginTransactionAsync(ct);

        string? outTxn = null;
        string? inTxn = null;
        try
        {
            (outTxn, inTxn) = await _stock.MoveAsync(new StockMoveRequest
            {
                WarehouseCd = task.WarehouseCd,
                FromLocationCd = task.FromLocationCd,
                ToLocationCd = task.ToLocationCd,
                ProductCd = task.ProductCd,
                LotNo = task.LotNo ?? string.Empty,
                Qty = request.ScannedQty,
                OperatorCd = userName,
                Remark = $"Mobile task {taskNo} MOVE",
            }, ct);

            task.OutTxnNo = outTxn;
            task.InTxnNo = inTxn;
            task.ScannedQty = request.ScannedQty;
            task.Status = MobileTaskStatus.Completed;
            task.DoneAt = DateTime.Now;
            task.CompletionOperationId = request.OperationId;
            if (!string.IsNullOrWhiteSpace(request.Remarks))
                task.Remarks = request.Remarks.Trim();
            Stamp(task, userName);

            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var completed = await _db.MobileTasks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MobileTaskNo == taskNo
                                          && x.CompletionOperationId == request.OperationId, ct);
            if (completed != null) return Map(completed);
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        // StockMovementService defers ambient-transaction events.  Publish the
        // two stock legs only after the task and inventory transaction commits.
        if (tx != null)
        {
            await PublishMoveStockEventsAsync(task, outTxn!, inTxn!, userName);
        }
        await NotifyAsync(task, "MobileTaskCompleted", ct);
        return Map(task);
    }

    public async Task<MobileTaskV1Dto> CancelAsync(
        string taskNo,
        CancelTaskRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        var task = await LoadAsync(taskNo, ct);
        if (task.Status is MobileTaskStatus.Completed or MobileTaskStatus.Cancelled)
            throw new MobileTaskConflictException("WM-CONFLICT-TASK-FINAL");
        ApplyRowVersion(task, request.RowVersion);
        task.Status = MobileTaskStatus.Cancelled;
        Stamp(task, userName);
        await SaveWithConflictAsync(ct);
        await NotifyAsync(task, "MobileTaskCancelled", ct);
        return Map(task);
    }

    private async Task<MobileTask> LoadAsync(string taskNo, CancellationToken ct)
        => await _db.MobileTasks.FirstOrDefaultAsync(
               x => x.MobileTaskNo == taskNo && !x.IsDeleted && x.TaskType == MobileTaskType.Move, ct)
           ?? throw new MobileTaskNotFoundException();

    private static void ValidateMove(CreateMoveTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WarehouseCd)
            || string.IsNullOrWhiteSpace(request.FromLocationCd)
            || string.IsNullOrWhiteSpace(request.ToLocationCd)
            || string.IsNullOrWhiteSpace(request.ProductCd))
            throw new ArgumentException("WM-MSG-303");
        if (request.Qty <= 0)
            throw new ArgumentException("WM-MSG-031");
        if (string.Equals(request.FromLocationCd.Trim(), request.ToLocationCd.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("WM-MSG-010");
    }

    private void ApplyRowVersion(MobileTask task, string encoded)
    {
        byte[] supplied;
        try
        {
            supplied = string.IsNullOrWhiteSpace(encoded)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }

        var current = task.RowVersion ?? Array.Empty<byte>();
        if (current.Length > 0
            && (supplied.Length == 0 || !CryptographicOperations.FixedTimeEquals(current, supplied)))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        if (current.Length > 0)
            _db.Entry(task).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private async Task SaveWithConflictAsync(
        CancellationToken ct,
        string conflictCode = "WM-CONFLICT-ROW-VERSION")
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException(conflictCode);
        }
    }

    private Task NotifyAsync(MobileTask task, string eventName, CancellationToken ct)
        => _taskNotifier.NotifyAsync(task.TenantId, eventName, new MobileTaskEvent
        {
            TaskNo = task.MobileTaskNo,
            TaskType = task.TaskType,
            Status = task.Status,
            AssignedTo = task.AssignedTo,
            WarehouseCd = task.WarehouseCd,
            ProductCd = task.ProductCd,
            RowVersion = Encode(task.RowVersion),
        }, ct);

    private async Task PublishMoveStockEventsAsync(
        MobileTask task,
        string outTxn,
        string inTxn,
        string? userName)
    {
        var common = new
        {
            task.WarehouseCd,
            task.ProductCd,
            LotNo = task.LotNo ?? string.Empty,
            task.ScannedQty,
        };
        await _stockNotifier.NotifyStockChangedAsync(new StockChangedEvent
        {
            TxnNo = outTxn,
            TxnType = WmsTxnType.MOVE,
            TxnAt = DateTime.Now,
            WarehouseCd = common.WarehouseCd!,
            LocationCd = task.FromLocationCd!,
            ProductCd = common.ProductCd!,
            LotNo = common.LotNo,
            Qty = -common.ScannedQty,
            RelatedNo = task.MobileTaskNo,
            OperatorCd = userName,
        });
        await _stockNotifier.NotifyStockChangedAsync(new StockChangedEvent
        {
            TxnNo = inTxn,
            TxnType = WmsTxnType.MOVE,
            TxnAt = DateTime.Now,
            WarehouseCd = common.WarehouseCd!,
            LocationCd = task.ToLocationCd!,
            ProductCd = common.ProductCd!,
            LotNo = common.LotNo,
            Qty = common.ScannedQty,
            RelatedNo = outTxn,
            OperatorCd = userName,
        });
    }

    private static void EnsurePending(MobileTask task, string code)
    {
        if (task.Status != MobileTaskStatus.Pending)
            throw new MobileTaskConflictException(code);
    }

    private static void Stamp(MobileTask task, string? userName)
    {
        task.Modifier = userName;
        task.ModifyDate = DateTime.Now;
    }

    private static MobileTaskV1Dto Map(MobileTask task) => new()
    {
        TaskNo = task.MobileTaskNo,
        TaskType = task.TaskType,
        Status = task.Status,
        AssignedTo = task.AssignedTo,
        Priority = task.Priority,
        WarehouseCd = task.WarehouseCd,
        FromLocationCd = task.FromLocationCd,
        ToLocationCd = task.ToLocationCd,
        ProductCd = task.ProductCd,
        ProductName = task.ProductName,
        LotNo = task.LotNo,
        Qty = task.Qty,
        ScannedQty = task.ScannedQty,
        UnitCd = task.UnitCd,
        Instruction = task.Instruction,
        Remarks = task.Remarks,
        StartedAt = task.StartedAt,
        CompletedAt = task.DoneAt,
        CompletionOperationId = task.CompletionOperationId,
        RowVersion = Encode(task.RowVersion),
    };

    private static string Encode(byte[]? rowVersion)
        => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
