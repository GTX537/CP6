using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

/// <summary>
/// Production MOVE state machine. Inventory, capacity, task state, audit event,
/// idempotency receipt, and remainder creation share one database transaction.
/// </summary>
public sealed class MobileTaskV2Service : IMobileTaskV2Service
{
    private const string Prefix = "MTK";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CP6Context _db;
    private readonly IWmsSequenceService _sequence;
    private readonly IStockMovementService _stock;
    private readonly IMobileTaskNotifier _notifier;
    private readonly IWmsAccessScopeProvider _accessScopes;

    public MobileTaskV2Service(
        CP6Context db,
        IWmsSequenceService sequence,
        IStockMovementService stock,
        IWmsAccessScopeProvider accessScopes,
        IMobileTaskNotifier? notifier = null)
    {
        _db = db;
        _sequence = sequence;
        _stock = stock;
        _accessScopes = accessScopes;
        _notifier = notifier ?? new NoOpMobileTaskNotifier();
    }

    public async Task<PagedResult<MobileTaskV2Dto>> GetTasksAsync(
        MobileTaskV2Query query,
        CancellationToken ct = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 1, 200);
        var tasks = (await _accessScopes.GetCurrentAsync(ct)).Apply(
            _db.MobileTasks.AsNoTracking())
            .Where(x => !x.IsDeleted
                        && x.ContractVersion == 2
                        && x.TaskType == MobileTaskType.Move);

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var user = query.AssignedTo.Trim();
            tasks = query.IncludeUnassigned
                ? tasks.Where(x => x.AssignedTo == user || x.AssignedTo == null)
                : tasks.Where(x => x.AssignedTo == user);
        }
        if (!string.IsNullOrWhiteSpace(query.WarehouseCd))
        {
            var warehouse = query.WarehouseCd.Trim();
            tasks = tasks.Where(x => x.WarehouseCd == warehouse);
        }
        if (!string.IsNullOrWhiteSpace(query.AreaCd))
        {
            var area = query.AreaCd.Trim();
            tasks = tasks.Where(x => x.AreaCd == area);
        }
        if (query.Status.HasValue)
            tasks = tasks.Where(x => x.Status == query.Status.Value);
        if (query.OpenOnly)
            tasks = tasks.Where(x => x.Status == MobileTaskStatus.Pending
                                     || x.Status == MobileTaskStatus.InProgress
                                     || x.Status == MobileTaskStatus.Paused
                                     || x.Status == MobileTaskStatus.Exception);

        var total = await tasks.CountAsync(ct);
        var rows = await tasks
            .OrderBy(x => x.Status)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.DueAt)
            .ThenBy(x => x.CreateDate)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return new PagedResult<MobileTaskV2Dto>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<MobileTaskV2Dto?> GetAsync(
        string taskNo,
        CancellationToken ct = default)
    {
        var task = await (await _accessScopes.GetCurrentAsync(ct)).Apply(
                _db.MobileTasks.AsNoTracking())
            .FirstOrDefaultAsync(x => x.MobileTaskNo == taskNo
                                      && x.ContractVersion == 2
                                      && !x.IsDeleted, ct);
        return task is null ? null : Map(task);
    }

    public async Task<IReadOnlyList<MobileTaskEventDto>> GetEventsAsync(
        string taskNo,
        CancellationToken ct = default)
    {
        await EnsureExistsAsync(taskNo, ct);
        return await _db.MobileTaskEvents.AsNoTracking()
            .Where(x => x.TaskNo == taskNo)
            .OrderBy(x => x.OccurredAt)
            .Select(x => new MobileTaskEventDto
            {
                TaskNo = x.TaskNo,
                EventType = x.EventType,
                OperationId = x.OperationId,
                ExecutionVersion = x.ExecutionVersion,
                UserName = x.UserName,
                DeviceId = x.DeviceId,
                OccurredAt = x.OccurredAt,
                DataJson = x.DataJson
            })
            .ToListAsync(ct);
    }

    public async Task<TaskScanProfileDto> GetScanProfileAsync(
        string taskNo,
        CancellationToken ct = default)
    {
        var task = await LoadAsync(taskNo, tracking: false, ct);
        var steps = new List<string> { "SourceLocation", "Product" };
        if (await ProductUsesLotAsync(task.ProductCd, ct)) steps.Add("Lot");
        steps.Add("TargetLocation");
        steps.Add("Quantity");
        return new TaskScanProfileDto
        {
            TaskNo = taskNo,
            ExecutionVersion = task.ExecutionVersion,
            Steps = steps
        };
    }

    public async Task<MobileTaskV2Dto> CreateAsync(
        CreateMoveTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateCreate(request);
        EnsureOperation(request.OperationId);
        var replay = await ReplayAnyAsync(request.OperationId, "create", ct);
        if (replay is not null)
        {
            await EnsureAccessAsync(replay.WarehouseCd, replay.AreaCd, ct);
            return replay;
        }
        await EnsureProductionMoveEnabledAsync(request.WarehouseCd, ct);

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        MobileTask? task = null;
        try
        {
            var warehouse = request.WarehouseCd.Trim();
            var from = request.FromLocationCd.Trim();
            var to = request.ToLocationCd.Trim();
            var product = request.ProductCd.Trim();
            var lot = request.LotNo?.Trim() ?? string.Empty;

            var locations = await _db.Locations
                .Where(x => !x.IsDeleted
                            && x.WarehouseCd == warehouse
                            && (x.LocationCd == from || x.LocationCd == to))
                .ToListAsync(ct);
            var sourceLocation = locations.FirstOrDefault(x => x.LocationCd == from);
            var targetLocation = locations.FirstOrDefault(x => x.LocationCd == to);
            if (sourceLocation is null || targetLocation is null)
                throw new ArgumentException("WM-V2-LOCATION-NOT-FOUND");
            if (sourceLocation.IsBlocked || targetLocation.IsBlocked)
                throw new MobileTaskConflictException("WM-V2-LOCATION-BLOCKED");
            if (!string.IsNullOrWhiteSpace(request.AreaCd)
                && !string.Equals(request.AreaCd.Trim(), targetLocation.AreaCd,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("WM-V2-AREA-MISMATCH");
            await EnsureAccessAsync(warehouse, targetLocation.AreaCd, ct);

            var physicalAtTarget = await _db.Stocks
                .Where(x => !x.IsDeleted
                            && x.WarehouseCd == warehouse
                            && x.LocationCd == to)
                .SumAsync(x => (decimal?)x.PhysicalQty, ct) ?? 0m;
            if (targetLocation.CapacityQty > 0m
                && physicalAtTarget + targetLocation.ReservedCapacityQty + request.Qty
                > targetLocation.CapacityQty)
                throw new MobileTaskConflictException("WM-V2-TARGET-CAPACITY");

            var taskNo = await _sequence.NextAsync(Prefix);
            task = new MobileTask
            {
                TenantId = _db.CurrentTenantId,
                MobileTaskNo = taskNo,
                ContractVersion = 2,
                TaskType = MobileTaskType.Move,
                Status = MobileTaskStatus.Pending,
                AssignedTo = NullIfWhiteSpace(request.AssignedTo),
                Priority = Math.Clamp(request.Priority, 1, 4),
                WarehouseCd = warehouse,
                AreaCd = targetLocation.AreaCd,
                FromLocationCd = from,
                ToLocationCd = to,
                ProductCd = product,
                ProductName = NullIfWhiteSpace(request.ProductName),
                LotNo = lot,
                Qty = request.Qty,
                UnitCd = NullIfWhiteSpace(request.UnitCd),
                Instruction = NullIfWhiteSpace(request.Instruction),
                Remarks = NullIfWhiteSpace(request.Remarks),
                RelatedType = NullIfWhiteSpace(request.SourceType),
                RelatedNo = NullIfWhiteSpace(request.SourceNo),
                PlannedStartAt = request.PlannedStartAt,
                DueAt = request.DueAt,
                ReservedSourceQty = request.Qty,
                ReservedTargetCapacityQty = request.Qty,
                Creator = userName
            };
            _db.MobileTasks.Add(task);
            _db.MobileTaskReservations.Add(new MobileTaskReservation
            {
                TenantId = _db.CurrentTenantId,
                TaskNo = taskNo,
                WarehouseCd = warehouse,
                FromLocationCd = from,
                ToLocationCd = to,
                ProductCd = product,
                LotNo = lot,
                ReservedQty = request.Qty,
                Creator = userName
            });
            targetLocation.ReservedCapacityQty += request.Qty;
            targetLocation.Modifier = userName;
            targetLocation.ModifyDate = DateTime.Now;

            await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.RSV,
                WarehouseCd = warehouse,
                LocationCd = from,
                ProductCd = product,
                LotNo = lot,
                Qty = request.Qty,
                UnitCd = request.UnitCd,
                RelatedNo = taskNo,
                RelatedType = "MOBILE_TASK_V2",
                OperatorCd = userName,
                Remark = $"Reserve for production MOVE {taskNo}"
            }, ct);

            RecordEvent(task, "Created", request.OperationId, userName, null,
                new { request.SourceType, request.SourceNo, request.Qty });
            await SaveWithConflictAsync(ct);
            var result = Map(task);
            AddReceipt(task, request.OperationId, "create", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task, "MobileTaskCreated", ct);
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

    public async Task<IReadOnlyList<MobileTaskV2Dto>> GetSourceTasksAsync(
        string sourceType,
        string sourceNo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(sourceNo))
            throw new ArgumentException("WM-V2-SOURCE-REQUIRED");

        var type = sourceType.Trim();
        var no = sourceNo.Trim();
        var baseQuery = _db.MobileTasks.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.ContractVersion == 2
                        && x.TaskType == MobileTaskType.Move
                        && x.RelatedType == type
                        && x.RelatedNo == no);
        var total = await baseQuery.CountAsync(ct);
        var visible = await (await _accessScopes.GetCurrentAsync(ct)).Apply(
                baseQuery)
                .OrderBy(x => x.CreateDate)
                .ThenBy(x => x.MobileTaskNo)
                .ToListAsync(ct);
        if (visible.Count != total)
            throw new WmsAccessDeniedException();
        return visible
            .Select(Map)
            .ToList();
    }

    public async Task<MobileTaskV2Dto> SynchronizePendingSourceTaskAsync(
        string taskNo,
        CreateMoveTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateCreate(request);
        EnsureOperation(request.OperationId);
        if (string.IsNullOrWhiteSpace(request.SourceType)
            || string.IsNullOrWhiteSpace(request.SourceNo))
            throw new ArgumentException("WM-V2-SOURCE-REQUIRED");
        await EnsureExistsAsync(taskNo, ct);
        var replay = await ReplayAsync(
            taskNo, request.OperationId, "source-sync", ct);
        if (replay is not null) return replay;
        await EnsureProductionMoveEnabledAsync(request.WarehouseCd, ct);

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        MobileTask? task = null;
        try
        {
            task = await LoadAsync(taskNo, true, ct);
            if (task.Status != MobileTaskStatus.Pending)
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-TASK-STARTED");
            if (!string.Equals(task.RelatedType, request.SourceType.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(task.RelatedNo, request.SourceNo.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                throw new MobileTaskConflictException(
                    "WM-V2-SOURCE-LINK-MISMATCH");

            var reservation = await _db.MobileTaskReservations
                .FirstOrDefaultAsync(
                    x => x.TaskNo == taskNo && x.IsActive, ct)
                ?? throw new MobileTaskConflictException(
                    "WM-V2-RESERVATION-MISSING");
            await ReleaseReservationsAsync(task, userName, ct);

            var warehouse = request.WarehouseCd.Trim();
            var from = request.FromLocationCd.Trim();
            var to = request.ToLocationCd.Trim();
            var product = request.ProductCd.Trim();
            var lot = request.LotNo?.Trim() ?? string.Empty;
            var locations = await _db.Locations
                .Where(x => !x.IsDeleted
                            && x.WarehouseCd == warehouse
                            && (x.LocationCd == from || x.LocationCd == to))
                .ToListAsync(ct);
            var sourceLocation =
                locations.FirstOrDefault(x => x.LocationCd == from);
            var targetLocation =
                locations.FirstOrDefault(x => x.LocationCd == to);
            if (sourceLocation is null || targetLocation is null)
                throw new ArgumentException("WM-V2-LOCATION-NOT-FOUND");
            if (sourceLocation.IsBlocked || targetLocation.IsBlocked)
                throw new MobileTaskConflictException(
                    "WM-V2-LOCATION-BLOCKED");
            if (!string.IsNullOrWhiteSpace(request.AreaCd)
                && !string.Equals(request.AreaCd.Trim(),
                    targetLocation.AreaCd,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("WM-V2-AREA-MISMATCH");
            await EnsureAccessAsync(warehouse, targetLocation.AreaCd, ct);
            await EnsureCapacityAsync(targetLocation, request.Qty, ct);

            await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.RSV,
                WarehouseCd = warehouse,
                LocationCd = from,
                ProductCd = product,
                LotNo = lot,
                Qty = request.Qty,
                UnitCd = request.UnitCd,
                RelatedNo = taskNo,
                RelatedType = "MOBILE_TASK_V2",
                OperatorCd = userName,
                Remark = $"Synchronize source reservation for MOVE {taskNo}"
            }, ct);

            reservation.WarehouseCd = warehouse;
            reservation.FromLocationCd = from;
            reservation.ToLocationCd = to;
            reservation.ProductCd = product;
            reservation.LotNo = lot;
            reservation.ReservedQty = request.Qty;
            reservation.ConsumedQty = 0m;
            reservation.ReleasedQty = 0m;
            reservation.IsActive = true;
            reservation.Modifier = userName;
            reservation.ModifyDate = DateTime.Now;

            targetLocation.ReservedCapacityQty += request.Qty;
            targetLocation.Modifier = userName;
            targetLocation.ModifyDate = DateTime.Now;

            task.AssignedTo = NullIfWhiteSpace(request.AssignedTo);
            task.Priority = Math.Clamp(request.Priority, 1, 4);
            task.WarehouseCd = warehouse;
            task.AreaCd = targetLocation.AreaCd;
            task.FromLocationCd = from;
            task.ToLocationCd = to;
            task.ProductCd = product;
            task.ProductName = NullIfWhiteSpace(request.ProductName);
            task.LotNo = lot;
            task.Qty = request.Qty;
            task.UnitCd = NullIfWhiteSpace(request.UnitCd);
            task.Instruction = NullIfWhiteSpace(request.Instruction);
            task.Remarks = NullIfWhiteSpace(request.Remarks);
            task.PlannedStartAt = request.PlannedStartAt;
            task.DueAt = request.DueAt;
            task.ReservedSourceQty = request.Qty;
            task.ReservedTargetCapacityQty = request.Qty;
            Stamp(task, userName);
            RecordEvent(task, "SourceSynchronized", request.OperationId,
                userName, null,
                new
                {
                    request.SourceType,
                    request.SourceNo,
                    request.Qty,
                    from,
                    to
                });
            await SaveWithConflictAsync(ct);
            var result = Map(task);
            AddReceipt(task, request.OperationId, "source-sync", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task, "MobileTaskSourceSynchronized", ct);
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

    public async Task<IReadOnlyList<MobileTaskV2Dto>>
        CancelPendingSourceTasksAsync(
            string sourceType,
            string sourceNo,
            string? userName,
            CancellationToken ct = default)
    {
        var tasks = await GetSourceTasksAsync(sourceType, sourceNo, ct);
        if (tasks.Any(x => x.Status is not (
                MobileTaskStatus.Pending or MobileTaskStatus.Cancelled)))
            throw new MobileTaskConflictException(
                "WM-V2-SOURCE-TASK-STARTED");

        foreach (var task in tasks.Where(
                     x => x.Status == MobileTaskStatus.Pending))
        {
            await CancelAsync(task.TaskNo, new CancelTaskV2Request
            {
                OperationId = Guid.NewGuid(),
                // EF InMemory and a few lightweight providers do not generate
                // SQL Server rowversion bytes. A non-empty placeholder still
                // passes through ApplyRowVersion safely when the current value
                // is empty, while production always uses the real token.
                RowVersion = string.IsNullOrWhiteSpace(task.RowVersion)
                    ? Convert.ToBase64String(new byte[] { 0 })
                    : task.RowVersion,
                Reason = $"Source document {sourceType}/{sourceNo} cancelled"
            }, userName, ct);
        }

        return await GetSourceTasksAsync(sourceType, sourceNo, ct);
    }

    public Task<MobileTaskV2Dto> AssignAsync(
        string taskNo,
        AssignTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
        => MutateAsync(taskNo, "assign", request, userName, "MobileTaskAssigned",
            task =>
            {
                if (task.Status != MobileTaskStatus.Pending)
                    throw new MobileTaskConflictException("WM-V2-TASK-STARTED");
                if (string.IsNullOrWhiteSpace(request.AssignedTo))
                    throw new ArgumentException("WM-V2-ASSIGNEE-REQUIRED");
                task.AssignedTo = request.AssignedTo.Trim();
            }, ct);

    public async Task<MobileTaskV2Dto> ClaimAsync(
        string taskNo,
        ClaimTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("WM-V2-USER-REQUIRED");
        await EnsureDeviceAllowedAsync(taskNo, request.DeviceId, ct);
        return await MutateAsync(taskNo, "claim", request, userName, "MobileTaskStarted",
            task =>
            {
                if (task.Status != MobileTaskStatus.Pending || task.AssignedTo is not null)
                    throw new MobileTaskConflictException("WM-V2-TASK-CLAIMED");
                BeginExecution(task, userName, request.DeviceId);
            }, ct);
    }

    public async Task<MobileTaskV2Dto> StartAsync(
        string taskNo,
        StartTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        await EnsureDeviceAllowedAsync(taskNo, request.DeviceId, ct);
        return await MutateAsync(taskNo, "start", request, userName, "MobileTaskStarted",
            task =>
            {
                if (task.Status != MobileTaskStatus.Pending)
                    throw new MobileTaskConflictException("WM-V2-TASK-STARTED");
                if (!string.Equals(task.AssignedTo, userName, StringComparison.OrdinalIgnoreCase))
                    throw new MobileTaskConflictException("WM-V2-TASK-NOT-ASSIGNED");
                BeginExecution(task, userName, request.DeviceId);
            }, ct);
    }

    public Task<MobileTaskV2Dto> PauseAsync(
        string taskNo,
        PauseTaskRequest request,
        string? userName,
        CancellationToken ct = default)
        => MutateAsync(taskNo, "pause", request, userName, "MobileTaskPaused",
            task =>
            {
                if (task.Status != MobileTaskStatus.InProgress)
                    throw new MobileTaskConflictException("WM-V2-TASK-NOT-IN-PROGRESS");
                if (string.IsNullOrWhiteSpace(request.Reason))
                    throw new ArgumentException("WM-V2-REASON-REQUIRED");
                InvalidateExecution(task);
                task.Status = MobileTaskStatus.Paused;
                task.PauseReason = request.Reason.Trim();
                task.ScannedQty = 0m;
            }, ct);

    public Task<MobileTaskV2Dto> ReleaseAsync(
        string taskNo,
        ReleaseTaskRequest request,
        string? userName,
        CancellationToken ct = default)
        => MutateAsync(taskNo, "release", request, userName, "MobileTaskReleased",
            task =>
            {
                if (task.Status is not (MobileTaskStatus.InProgress or MobileTaskStatus.Paused))
                    throw new MobileTaskConflictException("WM-V2-TASK-NOT-RELEASABLE");
                if (string.IsNullOrWhiteSpace(request.Reason))
                    throw new ArgumentException("WM-V2-REASON-REQUIRED");
                InvalidateExecution(task);
                task.Status = MobileTaskStatus.Pending;
                task.AssignedTo = null;
                task.PauseReason = request.Reason.Trim();
                task.ScannedQty = 0m;
            }, ct);

    public Task<MobileTaskV2Dto> TakeoverAsync(
        string taskNo,
        TakeoverTaskRequest request,
        string? userName,
        CancellationToken ct = default)
        => MutateAsync(taskNo, "takeover", request, userName, "MobileTaskTakenOver",
            task =>
            {
                if (task.Status is not (MobileTaskStatus.InProgress
                    or MobileTaskStatus.Paused
                    or MobileTaskStatus.Exception))
                    throw new MobileTaskConflictException("WM-V2-TASK-NOT-TAKEOVERABLE");
                if (string.IsNullOrWhiteSpace(request.AssignedTo)
                    || string.IsNullOrWhiteSpace(request.Reason))
                    throw new ArgumentException("WM-V2-TAKEOVER-DATA-REQUIRED");
                InvalidateExecution(task);
                BeginExecution(task, request.AssignedTo.Trim(), request.DeviceId);
                task.PauseReason = request.Reason.Trim();
                task.ExceptionReasonCd = null;
                task.ExceptionDescription = null;
                task.ScannedQty = 0m;
            }, ct);

    public Task<MobileTaskV2Dto> RaiseExceptionAsync(
        string taskNo,
        RaiseTaskExceptionRequest request,
        string? userName,
        CancellationToken ct = default)
        => MutateAsync(taskNo, "exception", request, userName, "MobileTaskException",
            task =>
            {
                if (task.Status is not (MobileTaskStatus.InProgress or MobileTaskStatus.Paused))
                    throw new MobileTaskConflictException("WM-V2-TASK-NOT-ACTIVE");
                if (string.IsNullOrWhiteSpace(request.ReasonCode)
                    || string.IsNullOrWhiteSpace(request.Description))
                    throw new ArgumentException("WM-V2-EXCEPTION-DATA-REQUIRED");
                InvalidateExecution(task);
                task.Status = MobileTaskStatus.Exception;
                task.ExceptionReasonCd = request.ReasonCode.Trim();
                task.ExceptionDescription = request.Description.Trim();
                task.ScannedQty = 0m;
            }, ct);

    public async Task<MobileTaskV2Dto> ResolveExceptionAsync(
        string taskNo,
        ResolveTaskExceptionRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        await EnsureExistsAsync(taskNo, ct);
        var replay = await ReplayAsync(taskNo, request.OperationId, "resolve-exception", ct);
        if (replay is not null) return replay;

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        MobileTask? task = null;
        try
        {
            task = await LoadAsync(taskNo, true, ct);
            if (task.Status != MobileTaskStatus.Exception)
                throw new MobileTaskConflictException("WM-V2-TASK-NOT-EXCEPTION");
            ApplyRowVersion(task, request.RowVersion);
            var action = request.Action.Trim().ToUpperInvariant();
            if (action == "CANCEL")
            {
                await ReleaseReservationsAsync(task, userName, ct);
                task.Status = MobileTaskStatus.Cancelled;
                task.DoneAt = DateTime.Now;
            }
            else
            {
                if (request.Qty.HasValue || !string.IsNullOrWhiteSpace(request.ToLocationCd))
                    await AdjustReservationAsync(task, request.Qty, request.ToLocationCd, userName, ct);
                InvalidateExecution(task);
                task.ScannedQty = 0m;
                if (action == "REASSIGN")
                {
                    if (string.IsNullOrWhiteSpace(request.AssignedTo))
                        throw new ArgumentException("WM-V2-ASSIGNEE-REQUIRED");
                    task.AssignedTo = request.AssignedTo.Trim();
                    task.Status = MobileTaskStatus.Pending;
                }
                else if (action is "RESUME" or "ADJUST")
                {
                    task.Status = MobileTaskStatus.Pending;
                }
                else
                {
                    throw new ArgumentException("WM-V2-RESOLUTION-ACTION");
                }
            }
            task.ExceptionReasonCd = null;
            task.ExceptionDescription = null;
            if (!string.IsNullOrWhiteSpace(request.Remarks))
                task.Remarks = request.Remarks.Trim();
            Stamp(task, userName);
            RecordEvent(task, "ExceptionResolved", request.OperationId, userName,
                request.DeviceId, new { action, request.Qty, request.ToLocationCd });
            await SaveWithConflictAsync(ct);
            await SynchronizeLinkedSourceStateAsync(task, userName, ct);
            var result = Map(task);
            AddReceipt(task, request.OperationId, "resolve-exception", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task,
                task.Status == MobileTaskStatus.Cancelled
                    ? "MobileTaskCancelled"
                    : "MobileTaskExceptionResolved", ct);
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

    public async Task<ScanResult> ScanAsync(
        string taskNo,
        ScanCommand request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        if (string.IsNullOrWhiteSpace(request.ClientScanNo)
            || string.IsNullOrWhiteSpace(request.RawBarcode)
            || string.IsNullOrWhiteSpace(request.Step)
            || string.IsNullOrWhiteSpace(request.DeviceId))
            throw new ArgumentException("WM-V2-SCAN-DATA-REQUIRED");
        await EnsureDeviceAllowedAsync(taskNo, request.DeviceId, ct);

        var existing = await _db.MobileTaskScanLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientScanNo == request.ClientScanNo, ct);
        if (existing is not null)
        {
            if (existing.TaskNo != taskNo)
                throw new MobileTaskConflictException("WM-V2-SCAN-ID-USED");
            return ScanFromLog(existing, string.Empty);
        }

        var task = await LoadAsync(taskNo, true, ct);
        EnsureActiveExecution(task, request, userName);
        var parsed = await ResolveBarcodeAsync(task, request.RawBarcode.Trim(), ct);
        var matched = await MatchesStepAsync(task, request.Step, parsed, request.RawBarcode, ct);
        var code = matched ? null : "WM-V2-SCAN-MISMATCH";
        var retentionDays = await _db.WmsFeatureFlags.AsNoTracking()
            .Where(x => !x.IsDeleted && x.WarehouseCd == task.WarehouseCd)
            .Select(x => (int?)x.ScanRetentionDays)
            .FirstOrDefaultAsync(ct) ?? 180;
        var log = new MobileTaskScanLog
        {
            TenantId = task.TenantId,
            TaskNo = taskNo,
            ClientScanNo = request.ClientScanNo.Trim(),
            ExecutionVersion = task.ExecutionVersion,
            Step = request.Step.Trim(),
            RawBarcode = request.RawBarcode.Trim(),
            DeviceId = request.DeviceId!,
            UserName = userName,
            ScannedAt = request.ScannedAt == default
                ? DateTime.UtcNow
                : request.ScannedAt.UtcDateTime,
            ParsedKind = parsed?.Kind,
            ParsedValue = parsed?.Value,
            Matched = matched,
            FailureCode = code,
            RetainUntil = DateTime.UtcNow.AddDays(Math.Clamp(retentionDays, 30, 3650))
        };
        _db.MobileTaskScanLogs.Add(log);
        RecordEvent(task, matched ? "ScanAccepted" : "ScanRejected",
            request.OperationId, userName, request.DeviceId,
            new { request.Step, request.ClientScanNo, code });
        await _db.SaveChangesAsync(ct);
        return new ScanResult
        {
            TaskNo = taskNo,
            Step = request.Step,
            RawBarcode = request.RawBarcode,
            Parsed = parsed,
            Matched = matched,
            ErrorCode = code,
            RecoveryAction = matched ? "CONTINUE" : "RESCAN",
            ExecutionVersion = task.ExecutionVersion,
            RowVersion = Encode(task.RowVersion)
        };
    }

    public async Task<MobileTaskV2Dto> CompleteAsync(
        string taskNo,
        CompleteMoveV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        await EnsureExistsAsync(taskNo, ct);
        var replay = await ReplayAsync(taskNo, request.OperationId, "complete", ct);
        if (replay is not null) return replay;
        await EnsureDeviceAllowedAsync(taskNo, request.DeviceId, ct);

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        MobileTask? task = null;
        MobileTask? remainder = null;
        try
        {
            task = await LoadAsync(taskNo, true, ct);
            EnsureActiveExecution(task, request, userName);
            ApplyRowVersion(task, request.RowVersion);
            if (request.ScannedQty <= 0m || request.ScannedQty > task.Qty)
                throw new ArgumentException("WM-V2-QTY-INVALID");
            if (!string.Equals(task.ToLocationCd, request.ToLocationCd?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("WM-V2-TARGET-MISMATCH");
            var partial = request.ScannedQty < task.Qty;
            if (partial && string.IsNullOrWhiteSpace(request.PartialReason))
                throw new ArgumentException("WM-V2-PARTIAL-REASON-REQUIRED");

            await EnsureRequiredScansAsync(task, ct);
            var reservation = await _db.MobileTaskReservations
                .FirstOrDefaultAsync(x => x.TaskNo == taskNo && x.IsActive, ct)
                ?? throw new MobileTaskConflictException("WM-V2-RESERVATION-MISSING");
            var target = await _db.Locations.FirstAsync(
                x => !x.IsDeleted
                     && x.WarehouseCd == task.WarehouseCd
                     && x.LocationCd == task.ToLocationCd, ct);

            var (outTxn, inTxn) = await _stock.MoveAsync(new StockMoveRequest
            {
                WarehouseCd = task.WarehouseCd!,
                FromLocationCd = task.FromLocationCd!,
                ToLocationCd = task.ToLocationCd!,
                ProductCd = task.ProductCd!,
                LotNo = task.LotNo ?? string.Empty,
                Qty = request.ScannedQty,
                OperatorCd = userName,
                Remark = $"Production MOVE {taskNo}"
            }, ct);

            target.ReservedCapacityQty =
                Math.Max(0m, target.ReservedCapacityQty - request.ScannedQty);
            target.Modifier = userName;
            target.ModifyDate = DateTime.Now;
            reservation.ConsumedQty = request.ScannedQty;
            reservation.IsActive = false;
            task.OutTxnNo = outTxn;
            task.InTxnNo = inTxn;
            task.ScannedQty = request.ScannedQty;
            task.DoneAt = DateTime.Now;
            task.CompletionOperationId = request.OperationId;
            task.ReservedSourceQty = 0m;
            task.ReservedTargetCapacityQty = 0m;
            task.PartialReason = NullIfWhiteSpace(request.PartialReason);
            if (!string.IsNullOrWhiteSpace(request.Remarks))
                task.Remarks = request.Remarks.Trim();

            if (partial)
            {
                var remaining = task.Qty - request.ScannedQty;
                var remainderNo = await _sequence.NextAsync(Prefix);
                remainder = CloneRemainder(task, remainderNo, remaining, userName);
                task.Status = MobileTaskStatus.PartiallyCompleted;
                task.RemainderTaskNo = remainderNo;
                reservation.ReleasedQty = remaining;
                _db.MobileTasks.Add(remainder);
                _db.MobileTaskReservations.Add(new MobileTaskReservation
                {
                    TenantId = task.TenantId,
                    TaskNo = remainderNo,
                    WarehouseCd = reservation.WarehouseCd,
                    FromLocationCd = reservation.FromLocationCd,
                    ToLocationCd = reservation.ToLocationCd,
                    ProductCd = reservation.ProductCd,
                    LotNo = reservation.LotNo,
                    ReservedQty = remaining,
                    Creator = userName
                });
                RecordEvent(remainder, "CreatedFromPartial", request.OperationId,
                    userName, request.DeviceId, new { parentTaskNo = taskNo, remaining });
            }
            else
            {
                task.Status = MobileTaskStatus.Completed;
            }

            Stamp(task, userName);
            RecordEvent(task,
                partial ? "PartiallyCompleted" : "Completed",
                request.OperationId, userName, request.DeviceId,
                new { request.ScannedQty, request.PartialReason, outTxn, inTxn });
            await SaveWithConflictAsync(ct);
            await SynchronizeLinkedSourceStateAsync(task, userName, ct);
            var result = Map(task);
            AddReceipt(task, request.OperationId, "complete", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task,
                partial ? "MobileTaskPartiallyCompleted" : "MobileTaskCompleted", ct);
            if (remainder is not null)
                await NotifyAsync(remainder, "MobileTaskCreated", ct);
            return result;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("OperationId",
                      StringComparison.OrdinalIgnoreCase) == true)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var saved = await ReplayAsync(taskNo, request.OperationId, "complete", ct);
            if (saved is not null) return saved;
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
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

    public async Task<MobileTaskV2Dto> CancelAsync(
        string taskNo,
        CancelTaskV2Request request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureCommand(request);
        await EnsureExistsAsync(taskNo, ct);
        var replay = await ReplayAsync(taskNo, request.OperationId, "cancel", ct);
        if (replay is not null) return replay;
        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        MobileTask? task = null;
        try
        {
            task = await LoadAsync(taskNo, true, ct);
            if (task.Status is MobileTaskStatus.Completed
                or MobileTaskStatus.PartiallyCompleted
                or MobileTaskStatus.Cancelled)
                throw new MobileTaskConflictException("WM-V2-TASK-FINAL");
            ApplyRowVersion(task, request.RowVersion);
            await ReleaseReservationsAsync(task, userName, ct);
            InvalidateExecution(task);
            task.Status = MobileTaskStatus.Cancelled;
            task.DoneAt = DateTime.Now;
            task.Remarks = string.IsNullOrWhiteSpace(request.Reason)
                ? task.Remarks
                : request.Reason.Trim();
            Stamp(task, userName);
            RecordEvent(task, "Cancelled", request.OperationId, userName,
                request.DeviceId, new { request.Reason });
            await SaveWithConflictAsync(ct);
            await SynchronizeLinkedSourceStateAsync(task, userName, ct);
            var result = Map(task);
            AddReceipt(task, request.OperationId, "cancel", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task, "MobileTaskCancelled", ct);
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

    public async Task<TaskAnalyticsDto> GetAnalyticsAsync(
        TaskAnalyticsQuery query,
        CancellationToken ct = default)
    {
        var tasks = (await _accessScopes.GetCurrentAsync(ct)).Apply(
            _db.MobileTasks.AsNoTracking())
            .Where(x => !x.IsDeleted && x.ContractVersion == 2);
        if (!string.IsNullOrWhiteSpace(query.WarehouseCd))
            tasks = tasks.Where(x => x.WarehouseCd == query.WarehouseCd);
        if (!string.IsNullOrWhiteSpace(query.AreaCd))
            tasks = tasks.Where(x => x.AreaCd == query.AreaCd);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
            tasks = tasks.Where(x => x.AssignedTo == query.AssignedTo);
        if (query.From.HasValue) tasks = tasks.Where(x => x.CreateDate >= query.From);
        if (query.To.HasValue) tasks = tasks.Where(x => x.CreateDate < query.To);

        var rows = await tasks.Select(x => new
        {
            x.Status, x.DueAt, x.StartedAt, x.DoneAt
        }).ToListAsync(ct);
        var durations = rows
            .Where(x => x.StartedAt.HasValue && x.DoneAt.HasValue)
            .Select(x => (x.DoneAt!.Value - x.StartedAt!.Value).TotalMinutes)
            .ToList();
        return new TaskAnalyticsDto
        {
            Created = rows.Count,
            Completed = rows.Count(x => x.Status == MobileTaskStatus.Completed),
            PartiallyCompleted = rows.Count(x => x.Status == MobileTaskStatus.PartiallyCompleted),
            Exceptions = rows.Count(x => x.Status == MobileTaskStatus.Exception),
            Overdue = rows.Count(x => x.DueAt < DateTime.Now
                                      && x.Status is not (MobileTaskStatus.Completed
                                          or MobileTaskStatus.PartiallyCompleted
                                          or MobileTaskStatus.Cancelled)),
            AverageMinutes = durations.Count == 0 ? 0d : durations.Average()
        };
    }

    private async Task<MobileTaskV2Dto> MutateAsync(
        string taskNo,
        string commandName,
        TaskCommand command,
        string? userName,
        string notification,
        Action<MobileTask> mutate,
        CancellationToken ct)
    {
        EnsureCommand(command);
        await EnsureExistsAsync(taskNo, ct);
        var replay = await ReplayAsync(taskNo, command.OperationId, commandName, ct);
        if (replay is not null) return replay;
        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        try
        {
            var task = await LoadAsync(taskNo, true, ct);
            if (commandName is "claim" or "start")
                await EnsureProductionMoveEnabledAsync(task.WarehouseCd, ct);
            ApplyRowVersion(task, command.RowVersion);
            mutate(task);
            Stamp(task, userName);
            RecordEvent(task, ToEventName(commandName), command.OperationId,
                userName, command.DeviceId);
            await SaveWithConflictAsync(ct);
            var result = Map(task);
            AddReceipt(task, command.OperationId, commandName, result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            await NotifyAsync(task, notification, ct);
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

    private async Task SynchronizeLinkedSourceStateAsync(
        MobileTask changedTask,
        string? userName,
        CancellationToken ct)
    {
        if (!string.Equals(changedTask.RelatedType, "REPLENISH",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(changedTask.RelatedNo))
            return;

        var order = await _db.ReplenishOrders.FirstOrDefaultAsync(
            x => !x.IsDeleted
                 && x.ReplenishNo == changedTask.RelatedNo, ct);
        if (order is null || order.Status == ReplenishStatus.Cancelled)
            return;

        var linked = await _db.MobileTasks.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.ContractVersion == 2
                        && x.RelatedType == "REPLENISH"
                        && x.RelatedNo == changedTask.RelatedNo)
            .OrderByDescending(x => x.DoneAt)
            .ToListAsync(ct);
        var hasOpen = linked.Any(x =>
            x.Status is MobileTaskStatus.Pending
                or MobileTaskStatus.InProgress
                or MobileTaskStatus.Paused
                or MobileTaskStatus.Exception);
        var movedQty = linked
            .Where(x => x.Status is MobileTaskStatus.Completed
                or MobileTaskStatus.PartiallyCompleted)
            .Sum(x => x.ScannedQty);

        if (!hasOpen && movedQty >= order.Qty)
        {
            var last = linked.FirstOrDefault(x =>
                x.Status is MobileTaskStatus.Completed
                    or MobileTaskStatus.PartiallyCompleted);
            order.Status = ReplenishStatus.Executed;
            order.ExecutedAt = last?.DoneAt ?? DateTime.Now;
            order.OutTxnNo = last?.OutTxnNo;
            order.InTxnNo = last?.InTxnNo;
            order.OperatorCd = userName ?? order.OperatorCd;
        }
        else if (movedQty == 0m
                 && linked.Count > 0
                 && linked.All(x => x.Status == MobileTaskStatus.Cancelled))
        {
            order.Status = ReplenishStatus.Pending;
            order.ExecutedAt = null;
            order.OutTxnNo = null;
            order.InTxnNo = null;
        }
        else if (order.Status != ReplenishStatus.Executed)
        {
            order.Status = ReplenishStatus.TaskIssued;
        }

        order.Modifier = userName;
        order.ModifyDate = DateTime.Now;
    }

    private async Task AdjustReservationAsync(
        MobileTask task,
        decimal? requestedQty,
        string? requestedTarget,
        string? userName,
        CancellationToken ct)
    {
        var reservation = await _db.MobileTaskReservations
            .FirstOrDefaultAsync(x => x.TaskNo == task.MobileTaskNo && x.IsActive, ct)
            ?? throw new MobileTaskConflictException("WM-V2-RESERVATION-MISSING");
        var oldTarget = await _db.Locations.FirstAsync(
            x => x.WarehouseCd == reservation.WarehouseCd
                 && x.LocationCd == reservation.ToLocationCd
                 && !x.IsDeleted, ct);

        if (requestedQty is > 0m && requestedQty.Value != task.Qty)
        {
            var delta = requestedQty.Value - task.Qty;
            if (delta > 0m)
            {
                await _stock.ApplyAsync(ReservationRequest(
                    reservation, WmsTxnType.RSV, delta, userName), ct);
                await EnsureCapacityAsync(oldTarget, delta, ct);
                oldTarget.ReservedCapacityQty += delta;
            }
            else
            {
                await _stock.ApplyAsync(ReservationRequest(
                    reservation, WmsTxnType.UNRSV, -delta, userName), ct);
                oldTarget.ReservedCapacityQty =
                    Math.Max(0m, oldTarget.ReservedCapacityQty + delta);
            }
            task.Qty = requestedQty.Value;
            task.ReservedSourceQty = requestedQty.Value;
            task.ReservedTargetCapacityQty = requestedQty.Value;
            reservation.ReservedQty = requestedQty.Value;
        }

        if (!string.IsNullOrWhiteSpace(requestedTarget)
            && !string.Equals(requestedTarget, reservation.ToLocationCd,
                StringComparison.OrdinalIgnoreCase))
        {
            var newTargetCd = requestedTarget.Trim();
            var newTarget = await _db.Locations.FirstOrDefaultAsync(
                x => !x.IsDeleted
                     && x.WarehouseCd == reservation.WarehouseCd
                     && x.LocationCd == newTargetCd, ct)
                ?? throw new ArgumentException("WM-V2-LOCATION-NOT-FOUND");
            await EnsureAccessAsync(
                reservation.WarehouseCd,
                newTarget.AreaCd,
                ct);
            await EnsureCapacityAsync(newTarget, reservation.ReservedQty, ct);
            oldTarget.ReservedCapacityQty =
                Math.Max(0m, oldTarget.ReservedCapacityQty - reservation.ReservedQty);
            newTarget.ReservedCapacityQty += reservation.ReservedQty;
            reservation.ToLocationCd = newTargetCd;
            task.ToLocationCd = newTargetCd;
            task.AreaCd = newTarget.AreaCd;
        }
    }

    private async Task EnsureCapacityAsync(
        Location target,
        decimal addedQty,
        CancellationToken ct)
    {
        if (target.CapacityQty <= 0m) return;
        var physical = await _db.Stocks
            .Where(x => !x.IsDeleted
                        && x.WarehouseCd == target.WarehouseCd
                        && x.LocationCd == target.LocationCd)
            .SumAsync(x => (decimal?)x.PhysicalQty, ct) ?? 0m;
        if (physical + target.ReservedCapacityQty + addedQty > target.CapacityQty)
            throw new MobileTaskConflictException("WM-V2-TARGET-CAPACITY");
    }

    private async Task ReleaseReservationsAsync(
        MobileTask task,
        string? userName,
        CancellationToken ct)
    {
        var reservation = await _db.MobileTaskReservations
            .FirstOrDefaultAsync(x => x.TaskNo == task.MobileTaskNo && x.IsActive, ct);
        if (reservation is null) return;
        var remaining = reservation.ReservedQty
                        - reservation.ConsumedQty
                        - reservation.ReleasedQty;
        if (remaining > 0m)
        {
            await _stock.ApplyAsync(ReservationRequest(
                reservation, WmsTxnType.UNRSV, remaining, userName), ct);
            var target = await _db.Locations.FirstAsync(
                x => !x.IsDeleted
                     && x.WarehouseCd == reservation.WarehouseCd
                     && x.LocationCd == reservation.ToLocationCd, ct);
            target.ReservedCapacityQty =
                Math.Max(0m, target.ReservedCapacityQty - remaining);
            reservation.ReleasedQty += remaining;
        }
        reservation.IsActive = false;
        task.ReservedSourceQty = 0m;
        task.ReservedTargetCapacityQty = 0m;
    }

    private static StockMovementRequest ReservationRequest(
        MobileTaskReservation reservation,
        string txnType,
        decimal qty,
        string? userName) => new()
        {
            TxnType = txnType,
            WarehouseCd = reservation.WarehouseCd,
            LocationCd = reservation.FromLocationCd,
            ProductCd = reservation.ProductCd,
            LotNo = reservation.LotNo,
            Qty = qty,
            RelatedNo = reservation.TaskNo,
            RelatedType = "MOBILE_TASK_V2",
            OperatorCd = userName,
            Remark = $"{txnType} production MOVE {reservation.TaskNo}"
        };

    private async Task EnsureRequiredScansAsync(
        MobileTask task,
        CancellationToken ct)
    {
        var accepted = await _db.MobileTaskScanLogs.AsNoTracking()
            .Where(x => x.TaskNo == task.MobileTaskNo
                        && x.ExecutionVersion == task.ExecutionVersion
                        && x.Matched)
            .Select(x => x.Step)
            .ToListAsync(ct);
        var required = new List<string>
            { "SourceLocation", "Product", "TargetLocation", "Quantity" };
        if (await ProductUsesLotAsync(task.ProductCd, ct)) required.Add("Lot");
        if (required.Any(step =>
                !accepted.Contains(step, StringComparer.OrdinalIgnoreCase)))
            throw new MobileTaskConflictException("WM-V2-SCAN-INCOMPLETE");
    }

    private async Task<ParsedBarcode?> ResolveBarcodeAsync(
        MobileTask task,
        string raw,
        CancellationToken ct)
    {
        var now = DateTime.Now;
        var alias = await _db.BarcodeAliases.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.IsEnabled
                                      && x.Barcode == raw
                                      && (!x.ValidFrom.HasValue || x.ValidFrom <= now)
                                      && (!x.ValidUntil.HasValue || x.ValidUntil >= now), ct);
        if (alias is not null)
            return new ParsedBarcode
            {
                Kind = alias.BarcodeType,
                Value = alias.TargetKey,
                ProductCd = alias.ProductCd,
                LotNo = alias.LotNo,
                LocationCd = alias.LocationCd,
                PackageUnitCd = alias.PackageUnitCd,
                ConversionRate = alias.ConversionRate
            };

        var location = await _db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.WarehouseCd == task.WarehouseCd
                                      && (x.LocationCd == raw || x.Barcode == raw), ct);
        if (location is not null)
            return new ParsedBarcode
            {
                Kind = BarcodeTargetType.Location,
                Value = location.LocationCd,
                LocationCd = location.LocationCd
            };
        if (string.Equals(task.ProductCd, raw, StringComparison.OrdinalIgnoreCase))
            return new ParsedBarcode
            {
                Kind = BarcodeTargetType.Product,
                Value = raw,
                ProductCd = task.ProductCd
            };
        if (!string.IsNullOrWhiteSpace(task.LotNo)
            && string.Equals(task.LotNo, raw, StringComparison.OrdinalIgnoreCase))
            return new ParsedBarcode
            {
                Kind = BarcodeTargetType.Lot,
                Value = raw,
                LotNo = task.LotNo
            };
        if (decimal.TryParse(raw, out var qty) && qty > 0m)
            return new ParsedBarcode
            {
                Kind = "QUANTITY",
                Value = raw,
                ConversionRate = qty
            };
        return null;
    }

    private static Task<bool> MatchesStepAsync(
        MobileTask task,
        string step,
        ParsedBarcode? parsed,
        string raw,
        CancellationToken ct)
    {
        _ = ct;
        if (parsed is null) return Task.FromResult(false);
        var matched = step.Trim().ToUpperInvariant() switch
        {
            "SOURCELOCATION" =>
                parsed.Kind == BarcodeTargetType.Location
                && string.Equals(parsed.LocationCd ?? parsed.Value,
                    task.FromLocationCd, StringComparison.OrdinalIgnoreCase),
            "PRODUCT" =>
                parsed.Kind is BarcodeTargetType.Product or BarcodeTargetType.Package
                && string.Equals(parsed.ProductCd ?? parsed.Value,
                    task.ProductCd, StringComparison.OrdinalIgnoreCase),
            "LOT" =>
                parsed.Kind == BarcodeTargetType.Lot
                && string.Equals(parsed.LotNo ?? parsed.Value,
                    task.LotNo, StringComparison.OrdinalIgnoreCase),
            "TARGETLOCATION" =>
                parsed.Kind == BarcodeTargetType.Location
                && string.Equals(parsed.LocationCd ?? parsed.Value,
                    task.ToLocationCd, StringComparison.OrdinalIgnoreCase),
            "QUANTITY" =>
                decimal.TryParse(raw, out var qty) && qty > 0m && qty <= task.Qty,
            _ => false
        };
        return Task.FromResult(matched);
    }

    private async Task<bool> ProductUsesLotAsync(
        string? productCd,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productCd)) return false;
        var mode = await _db.ProductMasters.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ProductCd == productCd)
            .Select(x => (int?)x.TrackingMode)
            .FirstOrDefaultAsync(ct);
        return mode.HasValue && ProductTrackingMode.UsesLot(mode.Value);
    }

    private async Task EnsureDeviceAllowedAsync(
        string taskNo,
        string? deviceId,
        CancellationToken ct)
    {
        var task = await LoadAsync(taskNo, false, ct);
        if (string.IsNullOrWhiteSpace(deviceId)
            || !await _db.ClientDevices.AsNoTracking()
                .AnyAsync(x => !x.IsDeleted
                               && x.DeviceId == deviceId
                               && x.Status == ClientDeviceStatus.Active
                               && (x.WarehouseCd == null
                                   || x.WarehouseCd == task.WarehouseCd)
                               && (x.AreaCd == null || x.AreaCd == task.AreaCd), ct))
            throw new MobileTaskConflictException("WM-V2-DEVICE-NOT-ACTIVE");
    }

    private async Task EnsureProductionMoveEnabledAsync(
        string? warehouseCd,
        CancellationToken ct)
    {
        var enabled = await _db.WmsFeatureFlags.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted
                           && x.WarehouseCd == warehouseCd
                           && x.ProductionMoveEnabled, ct);
        if (!enabled)
            throw new MobileTaskConflictException("WM-R2A-DISABLED");
    }

    private async Task<MobileTaskV2Dto?> ReplayAsync(
        string taskNo,
        Guid operationId,
        string commandName,
        CancellationToken ct)
    {
        var receipt = await _db.TaskCommandReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.TaskNo != taskNo || receipt.CommandName != commandName)
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
        return JsonSerializer.Deserialize<MobileTaskV2Dto>(
            receipt.ResultJson, JsonOptions)
            ?? throw new MobileTaskConflictException("WM-V2-RECEIPT-INVALID");
    }

    private async Task<MobileTaskV2Dto?> ReplayAnyAsync(
        Guid operationId,
        string commandName,
        CancellationToken ct)
    {
        var receipt = await _db.TaskCommandReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.CommandName != commandName)
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
        return JsonSerializer.Deserialize<MobileTaskV2Dto>(
            receipt.ResultJson, JsonOptions)
            ?? throw new MobileTaskConflictException("WM-V2-RECEIPT-INVALID");
    }

    private void AddReceipt(
        MobileTask task,
        Guid operationId,
        string commandName,
        MobileTaskV2Dto result)
        => _db.TaskCommandReceipts.Add(new TaskCommandReceipt
        {
            TenantId = task.TenantId,
            OperationId = operationId,
            TaskNo = task.MobileTaskNo,
            CommandName = commandName,
            ResultJson = JsonSerializer.Serialize(result, JsonOptions),
            CompletedAt = DateTime.UtcNow
        });

    private void RecordEvent(
        MobileTask task,
        string eventType,
        Guid? operationId,
        string? userName,
        string? deviceId,
        object? data = null)
        => _db.MobileTaskEvents.Add(
            new CP6.Entity.DomainModels.Wms.MobileTaskEvent
            {
                TenantId = task.TenantId,
                TaskNo = task.MobileTaskNo,
                EventType = eventType,
                OperationId = operationId,
                ExecutionVersion = task.ExecutionVersion,
                UserName = userName,
                DeviceId = deviceId,
                OccurredAt = DateTime.UtcNow,
                DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions)
            });

    private Task NotifyAsync(
        MobileTask task,
        string eventName,
        CancellationToken ct)
        => _notifier.NotifyAsync(task.TenantId, eventName,
            new CP6.Core.Services.Wms.MobileTaskEvent
            {
                TaskNo = task.MobileTaskNo,
                TaskType = task.TaskType,
                Status = task.Status,
                AssignedTo = task.AssignedTo,
                WarehouseCd = task.WarehouseCd,
                ProductCd = task.ProductCd,
                RowVersion = Encode(task.RowVersion)
            }, ct);

    private static MobileTask CloneRemainder(
        MobileTask source,
        string taskNo,
        decimal qty,
        string? userName)
        => new()
        {
            TenantId = source.TenantId,
            MobileTaskNo = taskNo,
            ContractVersion = 2,
            TaskType = source.TaskType,
            Status = MobileTaskStatus.Pending,
            Priority = source.Priority,
            WarehouseCd = source.WarehouseCd,
            AreaCd = source.AreaCd,
            FromLocationCd = source.FromLocationCd,
            ToLocationCd = source.ToLocationCd,
            ProductCd = source.ProductCd,
            ProductName = source.ProductName,
            LotNo = source.LotNo,
            Qty = qty,
            UnitCd = source.UnitCd,
            Instruction = source.Instruction,
            RelatedType = source.RelatedType,
            RelatedNo = source.RelatedNo,
            PlannedStartAt = source.PlannedStartAt,
            DueAt = source.DueAt,
            ParentTaskNo = source.MobileTaskNo,
            ReservedSourceQty = qty,
            ReservedTargetCapacityQty = qty,
            Creator = userName
        };

    private static void BeginExecution(
        MobileTask task,
        string? userName,
        string? deviceId)
    {
        task.AssignedTo = userName;
        task.Status = MobileTaskStatus.InProgress;
        task.StartedAt ??= DateTime.Now;
        task.ExecutionVersion++;
        task.ExecutionId = Guid.NewGuid();
        task.LastDeviceId = NullIfWhiteSpace(deviceId);
        task.PauseReason = null;
    }

    private static void InvalidateExecution(MobileTask task)
    {
        task.ExecutionVersion++;
        task.ExecutionId = null;
        task.LastDeviceId = null;
    }

    private static void EnsureActiveExecution(
        MobileTask task,
        TaskCommand command,
        string? userName)
    {
        if (task.Status != MobileTaskStatus.InProgress)
            throw new MobileTaskConflictException("WM-V2-TASK-NOT-IN-PROGRESS");
        if (!string.Equals(task.AssignedTo, userName, StringComparison.OrdinalIgnoreCase))
            throw new MobileTaskConflictException("WM-V2-TASK-NOT-ASSIGNED");
        if (!command.ExecutionVersion.HasValue
            || command.ExecutionVersion.Value != task.ExecutionVersion)
            throw new MobileTaskConflictException("WM-V2-EXECUTION-VERSION");
        if (!string.Equals(command.DeviceId, task.LastDeviceId, StringComparison.Ordinal))
            throw new MobileTaskConflictException("WM-V2-EXECUTION-DEVICE");
    }

    private static void ValidateCreate(CreateMoveTaskV2Request request)
    {
        if (string.IsNullOrWhiteSpace(request.WarehouseCd)
            || string.IsNullOrWhiteSpace(request.FromLocationCd)
            || string.IsNullOrWhiteSpace(request.ToLocationCd)
            || string.IsNullOrWhiteSpace(request.ProductCd))
            throw new ArgumentException("WM-V2-MOVE-DATA-REQUIRED");
        if (request.Qty <= 0m) throw new ArgumentException("WM-V2-QTY-INVALID");
        if (string.Equals(request.FromLocationCd.Trim(),
                request.ToLocationCd.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("WM-MSG-010");
        if (request.DueAt.HasValue && request.PlannedStartAt.HasValue
            && request.DueAt < request.PlannedStartAt)
            throw new ArgumentException("WM-V2-SCHEDULE-INVALID");
    }

    private static void EnsureCommand(TaskCommand command)
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

    private void ApplyRowVersion(MobileTask task, string encoded)
    {
        byte[] supplied;
        try { supplied = Convert.FromBase64String(encoded); }
        catch (FormatException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        var current = task.RowVersion ?? Array.Empty<byte>();
        if (current.Length > 0
            && (supplied.Length == 0
                || !CryptographicOperations.FixedTimeEquals(current, supplied)))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        if (current.Length > 0)
            _db.Entry(task).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private async Task SaveWithConflictAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
    }

    private async Task<MobileTask> LoadAsync(
        string taskNo,
        bool tracking,
        CancellationToken ct)
    {
        var query = tracking
            ? _db.MobileTasks.AsQueryable()
            : _db.MobileTasks.AsNoTracking();
        query = (await _accessScopes.GetCurrentAsync(ct)).Apply(query);
        return await query.FirstOrDefaultAsync(
                   x => x.MobileTaskNo == taskNo
                        && x.ContractVersion == 2
                        && !x.IsDeleted, ct)
               ?? throw new MobileTaskNotFoundException();
    }

    private async Task EnsureAccessAsync(
        string? warehouseCd,
        string? areaCd,
        CancellationToken ct)
    {
        if (!(await _accessScopes.GetCurrentAsync(ct)).Allows(warehouseCd, areaCd))
            throw new WmsAccessDeniedException();
    }

    private async Task EnsureExistsAsync(
        string taskNo,
        CancellationToken ct)
        => _ = await LoadAsync(taskNo, false, ct);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken ct)
        => _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

    private static void Stamp(MobileTask task, string? userName)
    {
        task.Modifier = userName;
        task.ModifyDate = DateTime.Now;
    }

    private static string ToEventName(string commandName)
        => string.Concat(commandName.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static ScanResult ScanFromLog(
        MobileTaskScanLog log,
        string rowVersion)
        => new()
        {
            TaskNo = log.TaskNo,
            Step = log.Step,
            RawBarcode = log.RawBarcode,
            Parsed = log.ParsedKind is null ? null : new ParsedBarcode
            {
                Kind = log.ParsedKind,
                Value = log.ParsedValue ?? string.Empty
            },
            Matched = log.Matched,
            ErrorCode = log.FailureCode,
            RecoveryAction = log.Matched ? "CONTINUE" : "RESCAN",
            ExecutionVersion = log.ExecutionVersion,
            RowVersion = rowVersion
        };

    private static MobileTaskV2Dto Map(MobileTask task) => new()
    {
        TaskNo = task.MobileTaskNo,
        TaskType = task.TaskType,
        Status = task.Status,
        AssignedTo = task.AssignedTo,
        Priority = task.Priority,
        WarehouseCd = task.WarehouseCd,
        AreaCd = task.AreaCd,
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
        SourceType = task.RelatedType,
        SourceNo = task.RelatedNo,
        PlannedStartAt = task.PlannedStartAt,
        DueAt = task.DueAt,
        ParentTaskNo = task.ParentTaskNo,
        RemainderTaskNo = task.RemainderTaskNo,
        ExceptionReasonCd = task.ExceptionReasonCd,
        ExceptionDescription = task.ExceptionDescription,
        ExecutionVersion = task.ExecutionVersion,
        ExecutionId = task.ExecutionId,
        ReservedSourceQty = task.ReservedSourceQty,
        ReservedTargetCapacityQty = task.ReservedTargetCapacityQty,
        StartedAt = task.StartedAt,
        CompletedAt = task.DoneAt,
        CompletionOperationId = task.CompletionOperationId,
        RowVersion = Encode(task.RowVersion)
    };

    private static string Encode(byte[]? rowVersion)
        => rowVersion is { Length: > 0 }
            ? Convert.ToBase64String(rowVersion)
            : string.Empty;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
