using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceWmsAdapter : ISpaceWmsAdapter
{
    public const string AdapterId = "cp6-wms-v1";
    public const string DataSourceId = "CP6_WMS";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private static readonly SpaceWmsCapabilities Capabilities =
        new(
            AtomicStaging: false,
            IdempotentUpsert: true,
            IdempotentDisable: true,
            RenameLocation: false,
            QueryByLogicalId: true,
            QueryBlockingReferences: true,
            QueryInventory: true,
            QueryTasks: true,
            ReliableOperationStatus: true,
            ReadBackHash: true,
            BatchMaxSize: 500,
            AllowedCodePattern: "^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$",
            CodeMaxLength: 30);

    private static readonly int[] ActiveOutboundStatuses =
    [
        OutboundOrderStatus.Confirmed,
        OutboundOrderStatus.Allocated,
        OutboundOrderStatus.Picking,
        OutboundOrderStatus.PartialAllocated,
    ];

    private readonly CP6Context _db;

    public Cp6SpaceWmsAdapter(CP6Context db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    public string RuntimeAdapterId => AdapterId;
    public string RuntimeDataSourceId => DataSourceId;
    public SpaceWmsDataSourceKind RuntimeDataSourceKind =>
        SpaceWmsDataSourceKind.Real;

    public Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(
        SpaceWmsContext context,
        CancellationToken ct = default)
    {
        EnsureScope(context);
        return Task.FromResult(CapabilitySnapshot());
    }

    public async Task<SpaceWmsHealth> CheckHealthAsync(
        SpaceWmsContext context,
        CancellationToken ct = default)
    {
        EnsureScope(context);
        var started = DateTimeOffset.UtcNow;
        try
        {
            var available = await _db.Database.CanConnectAsync(ct);
            var finished = DateTimeOffset.UtcNow;
            return new SpaceWmsHealth(
                AdapterId,
                available
                    ? SpaceWmsHealthState.Healthy
                    : SpaceWmsHealthState.Unavailable,
                finished,
                finished - started,
                available ? null : "SPACE_WMS_UNAVAILABLE");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var finished = DateTimeOffset.UtcNow;
            return new SpaceWmsHealth(
                AdapterId,
                SpaceWmsHealthState.Unavailable,
                finished,
                finished - started,
                "SPACE_WMS_UNAVAILABLE");
        }
    }

    public async Task<SpaceWmsPreflightResult> PreflightAsync(
        SpaceWmsPreflightRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        var snapshot = CapabilitySnapshot();
        var issues = new List<SpaceWmsPreflightIssue>();
        if (!string.Equals(
                request.CapabilityHash,
                snapshot.CapabilityHash,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new SpaceWmsPreflightIssue(
                null,
                "SPACE_WMS_CAPABILITY_MISSING",
                true));
        }
        if (request.Items.Count > Capabilities.BatchMaxSize)
        {
            issues.Add(new SpaceWmsPreflightIssue(
                null,
                "SPACE_WMS_BATCH_LIMIT_EXCEEDED",
                true));
        }

        var codePattern = new System.Text.RegularExpressions.Regex(
            Capabilities.AllowedCodePattern,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        foreach (var item in request.Items)
        {
            if (item.LocationCode.Length > Capabilities.CodeMaxLength ||
                !codePattern.IsMatch(item.LocationCode))
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_LOCATION_CODE_UNSUPPORTED",
                    true));
            }
        }

        var ids = request.Items
            .Select(item => item.LogicalId)
            .ToHashSet();
        var codes = request.Items
            .Select(item => item.LocationCode)
            .ToHashSet(StringComparer.Ordinal);
        var existing = await _db.WmsBins
            .Where(bin =>
                ids.Contains(bin.Id) ||
                (bin.WarehouseCd == request.Context.WarehouseCode &&
                 codes.Contains(bin.LocationCode)))
            .ToListAsync(ct);
        var byId = existing.ToDictionary(bin => bin.Id);
        var byCode = existing
            .GroupBy(bin => bin.LocationCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            if (byCode.TryGetValue(item.LocationCode, out var holder) &&
                holder.Id != item.LogicalId)
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_LOCATION_CODE_CONFLICT",
                    true,
                    holder.Id.ToString("D")));
            }
            if (!byId.TryGetValue(item.LogicalId, out var current))
                continue;
            if (!string.Equals(
                    current.WarehouseCd,
                    request.Context.WarehouseCode,
                    StringComparison.Ordinal))
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_LOGICAL_ID_SCOPE_CONFLICT",
                    true));
            }
            if (!string.Equals(
                    current.LocationCode,
                    item.LocationCode,
                    StringComparison.Ordinal) &&
                !Capabilities.RenameLocation)
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_CAPABILITY_MISSING",
                    true));
            }
            if (item.Version < current.Version)
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_STALE_VERSION",
                    true));
            }
        }

        var disableIds = request.Items
            .Where(item => item.Action == SpaceWmsLocationAction.Disable)
            .Select(item => item.LogicalId)
            .ToArray();
        if (disableIds.Length > 0)
        {
            var blocking = await LoadBlockingReferencesAsync(
                request.Context,
                disableIds,
                ct);
            issues.AddRange(blocking.Items.Select(reference =>
                new SpaceWmsPreflightIssue(
                    reference.LogicalId,
                    "SPACE_LOCATION_IN_USE",
                    true,
                    reference.ReferenceId)));
        }

        return new SpaceWmsPreflightResult(
            snapshot.CapabilityHash,
            issues
                .DistinctBy(issue => new
                {
                    issue.LogicalId,
                    issue.Code,
                    issue.ReferenceId,
                })
                .ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<SpaceWmsBatchResult> ApplyBatchAsync(
        SpaceWmsBatch batch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        EnsureScope(batch.Context);
        EnsureCleanWriteContext();

        IDbContextTransaction? transaction = null;
        var ownsTransaction =
            _db.Database.IsRelational() &&
            _db.Database.CurrentTransaction is null;
        if (ownsTransaction)
        {
            transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                ct);
        }

        try
        {
            var previous = await _db.SpaceWmsOperations
                .SingleOrDefaultAsync(
                    operation =>
                        operation.OperationKey == batch.OperationKey,
                    ct);
            if (previous is not null)
            {
                if (!string.Equals(
                        previous.PayloadHash,
                        batch.PayloadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (transaction is not null)
                        await transaction.CommitAsync(ct);
                    return ConflictResult(batch);
                }

                var replay = JsonSerializer.Deserialize<SpaceWmsBatchResult>(
                    previous.ResultJson,
                    Json);
                if (replay is null)
                    throw new InvalidOperationException(
                        "SPACE_WMS_RESULT_UNCERTAIN");
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return replay;
            }

            var compatibility = SpaceWmsContract.CheckCompatibility(
                batch,
                CapabilitySnapshot());
            SpaceWmsBatchResult result;
            if (compatibility.Count > 0)
            {
                result = FailureResult(
                    batch,
                    compatibility[0].Code);
            }
            else
            {
                result = await ApplyNewOperationAsync(batch, ct);
            }

            var assessment =
                SpaceWmsContract.AssessBatchResult(batch, result);
            var operationId = Guid.NewGuid();
            result = result with
            {
                ExternalOperationId = operationId.ToString("D"),
            };
            _db.SpaceWmsOperations.Add(new SpaceWmsOperation
            {
                Id = operationId,
                OperationKey = batch.OperationKey,
                PayloadHash = batch.PayloadHash,
                State = (int)ToOperationState(assessment.Kind),
                ExternalOperationId = result.ExternalOperationId,
                ResultJson = JsonSerializer.Serialize(result, Json),
                ObservedAtUtc = result.ObservedAtUtc.UtcDateTime,
            });
            await _db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            DetachOwnWrites();
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<SpaceWmsOperationStatus> GetOperationStatusAsync(
        SpaceWmsOperationQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        SpaceWmsContract.ValidateOperationKeyScope(
            request.Context,
            request.OperationKey);
        var operation = await _db.SpaceWmsOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.OperationKey == request.OperationKey,
                ct);
        if (operation is null)
        {
            return new SpaceWmsOperationStatus(
                request.OperationKey,
                request.PayloadHash,
                SpaceWmsOperationState.FailedNoEffect,
                true,
                DateTimeOffset.UtcNow);
        }
        if (!string.Equals(
                operation.PayloadHash,
                request.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SpaceWmsOperationStatus(
                request.OperationKey,
                request.PayloadHash,
                SpaceWmsOperationState.FailedNoEffect,
                true,
                new DateTimeOffset(
                    DateTime.SpecifyKind(
                        operation.ObservedAtUtc,
                        DateTimeKind.Utc)));
        }
        return new SpaceWmsOperationStatus(
            operation.OperationKey,
            operation.PayloadHash,
            (SpaceWmsOperationState)operation.State,
            true,
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    operation.ObservedAtUtc,
                    DateTimeKind.Utc)),
            operation.ExternalOperationId);
    }

    public async Task<SpaceWmsReadBackResult> ReadBackAsync(
        SpaceWmsReadBackRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        SpaceWmsContract.ValidateOperationKeyScope(
            request.Context,
            request.OperationKey);
        var states = await LoadLocationStatesAsync(
            request.Context,
            request.LogicalIds,
            ct);
        var material = new StringBuilder()
            .Append(request.OperationKey).Append('\n')
            .Append(request.PayloadHash.ToLowerInvariant()).Append('\n')
            .Append(request.PlanHash.ToLowerInvariant()).Append('\n');
        foreach (var state in states.OrderBy(value => value.LogicalId))
        {
            material
                .Append(state.LogicalId.ToString("D"))
                .Append('|')
                .Append(state.StateHash)
                .Append('\n');
        }
        return new SpaceWmsReadBackResult(
            Source(),
            states,
            Hash(material.ToString()));
    }

    public Task<SpaceWmsBlockingReferences> GetBlockingReferencesAsync(
        SpaceWmsBlockingReferencesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        return LoadBlockingReferencesAsync(
            request.Context,
            request.LogicalIds,
            ct);
    }

    public async Task<SpaceWmsLocationResult> QueryLocationsAsync(
        SpaceWmsLocationQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        return new SpaceWmsLocationResult(
            Source(),
            await LoadLocationStatesAsync(
                request.Context,
                request.LogicalIds,
                ct));
    }

    public async Task<SpaceWmsInventoryResult> QueryInventoryAsync(
        SpaceWmsInventoryQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        var bins = await LoadBinsAsync(
            request.Context,
            request.LogicalIds,
            ct);
        var codeToId = bins.ToDictionary(
            bin => bin.LocationCode,
            bin => bin.Id,
            StringComparer.Ordinal);
        if (codeToId.Count == 0)
            return new SpaceWmsInventoryResult(Source(), []);
        var codes = codeToId.Keys.ToArray();
        var ownerIds = request.OwnerIds?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var locate = request.LocateCriteria;
        if (!string.IsNullOrWhiteSpace(locate?.OwnerId))
        {
            var locatedOwnerId = locate.OwnerId.Trim().ToUpperInvariant();
            ownerIds = ownerIds is null
                ? [locatedOwnerId]
                : ownerIds
                    .Where(value => string.Equals(
                        value,
                        locatedOwnerId,
                        StringComparison.Ordinal))
                    .ToArray();
        }
        if ((request.OwnerIds is not null ||
             !string.IsNullOrWhiteSpace(locate?.OwnerId)) &&
            ownerIds!.Length == 0)
        {
            return new SpaceWmsInventoryResult(Source(), []);
        }
        if (!string.IsNullOrWhiteSpace(locate?.ContainerNumber))
        {
            var containerNumber = locate.ContainerNumber.Trim();
            var palletQuery = _db.Pallets
                .AsNoTracking()
                .Where(pallet =>
                    pallet.WarehouseCd == request.Context.WarehouseCode &&
                    codes.Contains(pallet.LocationCd) &&
                    pallet.PalletNo == containerNumber &&
                    pallet.Status != PalletStatus.Shipped &&
                    pallet.CartonQty > 0);
            if (!string.IsNullOrWhiteSpace(locate.MaterialNumber))
            {
                var materialNumber = locate.MaterialNumber.Trim();
                palletQuery = palletQuery.Where(pallet =>
                    pallet.ProductCd == materialNumber);
            }
            if (!string.IsNullOrWhiteSpace(locate.LotNumber))
            {
                var lotNumber = locate.LotNumber.Trim();
                palletQuery = palletQuery.Where(pallet =>
                    pallet.LotNo == lotNumber);
            }
            var pallets = await palletQuery
                .OrderBy(pallet => pallet.LocationCd)
                .ThenBy(pallet => pallet.PalletNo)
                .ToListAsync(ct);
            var ownerQuery = _db.Stocks
                .AsNoTracking()
                .Where(stock =>
                    stock.WarehouseCd == request.Context.WarehouseCode &&
                    codes.Contains(stock.LocationCd) &&
                    stock.PhysicalQty > 0);
            if (ownerIds is not null)
            {
                ownerQuery = ownerQuery.Where(stock =>
                    stock.OwnerCd != null &&
                    ownerIds.Contains(stock.OwnerCd));
            }
            var stockOwners = await ownerQuery
                .Select(stock => new
                {
                    stock.LocationCd,
                    stock.ProductCd,
                    stock.LotNo,
                    stock.OwnerCd,
                })
                .ToListAsync(ct);
            var ownerByStockIdentity = stockOwners.ToDictionary(
                value => (value.LocationCd, value.ProductCd, value.LotNo),
                value => value.OwnerCd?.Trim().ToUpperInvariant());
            return new SpaceWmsInventoryResult(
                Source(),
                pallets
                    .Where(pallet => ownerIds is null ||
                        ownerByStockIdentity.ContainsKey((
                            pallet.LocationCd,
                            pallet.ProductCd,
                            pallet.LotNo)))
                    .Select(pallet => new SpaceWmsInventoryItem(
                        codeToId[pallet.LocationCd],
                        pallet.LocationCd,
                        pallet.CartonQty,
                        0,
                        pallet.ProductCd,
                        pallet.LotNo,
                        pallet.PalletNo,
                        ownerByStockIdentity.GetValueOrDefault((
                            pallet.LocationCd,
                            pallet.ProductCd,
                            pallet.LotNo))))
                    .ToArray());
        }
        var stockQuery = _db.Stocks
            .AsNoTracking()
            .Where(stock =>
                stock.WarehouseCd == request.Context.WarehouseCode &&
                codes.Contains(stock.LocationCd));
        if (ownerIds is not null)
        {
            stockQuery = stockQuery.Where(stock =>
                stock.OwnerCd != null &&
                ownerIds.Contains(stock.OwnerCd));
        }
        if (locate is not null)
        {
            stockQuery = stockQuery.Where(stock => stock.PhysicalQty > 0);
            if (!string.IsNullOrWhiteSpace(locate.MaterialNumber))
            {
                var materialNumber = locate.MaterialNumber.Trim();
                stockQuery = stockQuery.Where(stock =>
                    stock.ProductCd == materialNumber);
            }
            if (!string.IsNullOrWhiteSpace(locate.LotNumber))
            {
                var lotNumber = locate.LotNumber.Trim();
                stockQuery = stockQuery.Where(stock => stock.LotNo == lotNumber);
            }
        }
        var stocks = await stockQuery
            .OrderBy(stock => stock.LocationCd)
            .ThenBy(stock => stock.ProductCd)
            .ThenBy(stock => stock.LotNo)
            .ToListAsync(ct);
        return new SpaceWmsInventoryResult(
            Source(),
            stocks.Select(stock => new SpaceWmsInventoryItem(
                codeToId[stock.LocationCd],
                stock.LocationCd,
                stock.PhysicalQty,
                stock.AllocatedQty,
                stock.ProductCd,
                stock.LotNo,
                null,
                stock.OwnerCd?.Trim().ToUpperInvariant())).ToArray());
    }

    public async Task<SpaceWmsTaskResult> QueryTasksAsync(
        SpaceWmsTaskQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        var bins = await LoadBinsAsync(
            request.Context,
            request.LogicalIds,
            ct);
        var codeToId = bins.ToDictionary(
            bin => bin.LocationCode,
            bin => bin.Id,
            StringComparer.Ordinal);
        if (codeToId.Count == 0)
            return new SpaceWmsTaskResult(Source(), []);
        var codes = codeToId.Keys.ToArray();
        var taskIds = request.TaskIds?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.TaskIds is not null && taskIds!.Length == 0)
            return new SpaceWmsTaskResult(Source(), []);
        var taskQuery =
            from detail in _db.OutboundOrderDetails.AsNoTracking()
            join order in _db.OutboundOrders.AsNoTracking()
                on detail.OutboundNo equals order.OutboundNo
            where detail.LocationCd != null &&
                  codes.Contains(detail.LocationCd) &&
                  ActiveOutboundStatuses.Contains(order.Status) &&
                  (detail.WarehouseCd ?? order.WarehouseCd) ==
                  request.Context.WarehouseCode
            orderby detail.OutboundNo, detail.LineNo
            select new
            {
                detail.OutboundNo,
                order.OutboundType,
                order.Status,
                detail.LineNo,
                LocationCode = detail.LocationCd!,
                detail.RequiredQty,
                detail.ProductCd,
            };
        if (taskIds is not null)
        {
            taskQuery = taskQuery.Where(row =>
                taskIds.Contains(row.OutboundNo));
        }
        var rows = await taskQuery.ToListAsync(ct);
        return new SpaceWmsTaskResult(
            Source(),
            rows.Select(row => new SpaceWmsTaskItem(
                row.OutboundNo,
                row.OutboundType.ToString(CultureInfo.InvariantCulture),
                row.Status.ToString(CultureInfo.InvariantCulture),
                row.LineNo,
                codeToId[row.LocationCode],
                row.LocationCode,
                row.RequiredQty,
                row.ProductCd)).ToArray());
    }

    public async Task<SpaceWmsDispatchTaskResult> QueryDispatchTasksAsync(
        SpaceWmsDispatchTaskQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        var rows = await _db.MobileTasks
            .AsNoTracking()
            .Where(value =>
                !value.IsDeleted &&
                value.WarehouseCd == request.Context.WarehouseCode &&
                value.Status != MobileTaskStatus.Completed &&
                value.Status != MobileTaskStatus.Cancelled)
            .OrderBy(value => value.MobileTaskNo)
            .ThenBy(value => value.Id)
            .Take(10_001)
            .Select(value => new
            {
                value.MobileTaskNo,
                value.TaskType,
                value.Status,
                value.AssignedTo,
                value.Priority,
                value.ContractVersion,
                value.ExecutionVersion,
                value.RowVersion,
                value.FromLocationCd,
                value.ToLocationCd,
                value.Qty,
                value.ProductCd,
            })
            .ToArrayAsync(ct);
        var bins = await _db.WmsBins
            .AsNoTracking()
            .Where(value =>
                value.WarehouseCd == request.Context.WarehouseCode)
            .Select(value => new
            {
                value.Id,
                value.LocationCode,
            })
            .ToArrayAsync(ct);
        var binByCode = bins
            .GroupBy(value => value.LocationCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.Id).ToArray(),
                StringComparer.Ordinal);

        var items = rows.Select(value =>
        {
            var from = value.FromLocationCd?.Trim();
            var to = value.ToLocationCd?.Trim();
            var locationCode = string.IsNullOrWhiteSpace(from) ? to : from;
            var role = string.IsNullOrWhiteSpace(from)
                ? "Destination"
                : "Source";
            Guid? logicalId = null;
            if (!string.IsNullOrWhiteSpace(locationCode) &&
                binByCode.TryGetValue(locationCode, out var matches) &&
                matches.Length == 1)
            {
                logicalId = matches[0];
            }
            return new SpaceWmsDispatchTaskItem(
                value.MobileTaskNo,
                value.TaskType,
                DispatchStatus(value.Status),
                value.AssignedTo,
                value.Priority,
                value.ContractVersion,
                value.ExecutionVersion,
                Convert.ToBase64String(value.RowVersion ?? []),
                logicalId,
                locationCode,
                role,
                value.Qty,
                value.ProductCd);
        }).ToArray();
        return new SpaceWmsDispatchTaskResult(Source(), items);
    }

    public async Task<SpaceWmsAbcResult> QueryAbcAsync(
        SpaceWmsAbcQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureScope(request.Context);
        if (request.FromDateInclusive == default ||
            request.ToDateExclusive == default ||
            request.FromDateInclusive >= request.ToDateExclusive)
        {
            throw new ArgumentException(
                "A valid half-open ABC analysis date window is required.",
                nameof(request));
        }

        var from = request.FromDateInclusive.ToDateTime(TimeOnly.MinValue);
        var to = request.ToDateExclusive.ToDateTime(TimeOnly.MinValue);
        var rows = await _db.StockTransactions
            .AsNoTracking()
            .Where(value =>
                !value.IsDeleted &&
                value.WarehouseCd == request.Context.WarehouseCode &&
                value.TxnType == WmsTxnType.OUT &&
                value.Qty > 0 &&
                value.TxnDateTime >= from &&
                value.TxnDateTime < to)
            .GroupBy(value => value.ProductCd)
            .Select(group => new
            {
                MaterialNumber = group.Key,
                OutboundMovementCount = group.Count(),
                OutboundQuantity = group.Sum(value => value.Qty),
            })
            .OrderByDescending(value => value.OutboundQuantity)
            .ThenBy(value => value.MaterialNumber)
            .ToArrayAsync(ct);
        var items = rows
            .Select(value => new SpaceWmsAbcAggregate(
                value.MaterialNumber,
                value.OutboundMovementCount,
                value.OutboundQuantity))
            .ToArray();
        return new SpaceWmsAbcResult(Source(), items);
    }

    private async Task<SpaceWmsBatchResult> ApplyNewOperationAsync(
        SpaceWmsBatch batch,
        CancellationToken ct)
    {
        var ids = batch.Items.Select(item => item.LogicalId).ToHashSet();
        var codes = batch.Items
            .Select(item => item.LocationCode)
            .ToHashSet(StringComparer.Ordinal);
        var bins = await _db.WmsBins
            .Where(bin =>
                ids.Contains(bin.Id) ||
                (bin.WarehouseCd == batch.Context.WarehouseCode &&
                 codes.Contains(bin.LocationCode)))
            .ToListAsync(ct);
        var byId = bins.ToDictionary(bin => bin.Id);
        var byCode = bins
            .GroupBy(bin => bin.LocationCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var disableIds = batch.Items
            .Where(item => item.Action == SpaceWmsLocationAction.Disable)
            .Select(item => item.LogicalId)
            .ToArray();
        var blocking = disableIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<SpaceWmsBlockingReference>>()
            : (await LoadBlockingReferencesAsync(
                    batch.Context,
                    disableIds,
                    ct))
                .Items
                .GroupBy(reference => reference.LogicalId)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<SpaceWmsBlockingReference>)
                        group.ToArray());
        var receipts = new List<SpaceWmsItemReceipt>(batch.Items.Count);
        foreach (var item in batch.Items)
        {
            byId.TryGetValue(item.LogicalId, out var bin);
            if (bin is not null &&
                !string.Equals(
                    bin.WarehouseCd,
                    batch.Context.WarehouseCode,
                    StringComparison.Ordinal))
            {
                receipts.Add(FailureReceipt(
                    item,
                    "SPACE_WMS_LOGICAL_ID_SCOPE_CONFLICT"));
                continue;
            }
            if (bin is not null && item.Version < bin.Version)
            {
                receipts.Add(FailureReceipt(
                    item,
                    "SPACE_WMS_STALE_VERSION"));
                continue;
            }

            var pathJson = JsonSerializer.Serialize(item.Path, Json);
            var attributesJson =
                JsonSerializer.Serialize(item.Attributes, Json);
            var desiredActive =
                item.Action != SpaceWmsLocationAction.Disable;
            if (bin is not null && item.Version == bin.Version)
            {
                if (Matches(
                        bin,
                        batch.Context.WarehouseCode,
                        item.LocationCode,
                        pathJson,
                        attributesJson,
                        desiredActive))
                {
                    receipts.Add(SuccessReceipt(
                        item,
                        bin,
                        SpaceWmsItemOutcome.AlreadyApplied));
                }
                else
                {
                    receipts.Add(FailureReceipt(
                        item,
                        "WMS_VERSION_CONFLICT"));
                }
                continue;
            }

            if (item.Action == SpaceWmsLocationAction.Disable)
            {
                if (blocking.ContainsKey(item.LogicalId))
                {
                    receipts.Add(FailureReceipt(
                        item,
                        "SPACE_LOCATION_IN_USE"));
                    continue;
                }
                if (bin is null)
                {
                    bin = new WmsBin
                    {
                        Id = item.LogicalId,
                    };
                    _db.WmsBins.Add(bin);
                    byId.Add(bin.Id, bin);
                }
                Apply(
                    bin,
                    batch.Context.WarehouseCode,
                    item,
                    pathJson,
                    attributesJson,
                    false);
                byCode[item.LocationCode] = bin;
                receipts.Add(SuccessReceipt(
                    item,
                    bin,
                    SpaceWmsItemOutcome.Applied));
                continue;
            }

            if (byCode.TryGetValue(item.LocationCode, out var holder) &&
                holder.Id != item.LogicalId)
            {
                receipts.Add(FailureReceipt(
                    item,
                    "SPACE_WMS_LOCATION_CODE_CONFLICT"));
                continue;
            }
            if (bin is not null &&
                !string.Equals(
                    bin.LocationCode,
                    item.LocationCode,
                    StringComparison.Ordinal))
            {
                receipts.Add(FailureReceipt(
                    item,
                    "SPACE_WMS_CAPABILITY_MISSING"));
                continue;
            }
            if (bin is null)
            {
                bin = new WmsBin
                {
                    Id = item.LogicalId,
                };
                _db.WmsBins.Add(bin);
                byId.Add(bin.Id, bin);
            }
            Apply(
                bin,
                batch.Context.WarehouseCode,
                item,
                pathJson,
                attributesJson,
                true);
            byCode[item.LocationCode] = bin;
            receipts.Add(SuccessReceipt(
                item,
                bin,
                SpaceWmsItemOutcome.Applied));
        }

        return new SpaceWmsBatchResult(
            batch.OperationKey,
            batch.PayloadHash,
            null,
            receipts,
            DateTimeOffset.UtcNow);
    }

    private async Task<SpaceWmsBlockingReferences>
        LoadBlockingReferencesAsync(
            SpaceWmsContext context,
            IReadOnlyCollection<Guid> logicalIds,
            CancellationToken ct)
    {
        var bins = await LoadBinsAsync(context, logicalIds, ct);
        if (bins.Count == 0)
            return new SpaceWmsBlockingReferences(Source(), []);
        var codeToId = bins.ToDictionary(
            bin => bin.LocationCode,
            bin => bin.Id,
            StringComparer.Ordinal);
        var codes = codeToId.Keys.ToArray();
        var stocks = await _db.Stocks
            .AsNoTracking()
            .Where(stock =>
                stock.WarehouseCd == context.WarehouseCode &&
                codes.Contains(stock.LocationCd) &&
                stock.PhysicalQty > 0)
            .GroupBy(stock => stock.LocationCd)
            .Select(group => new
            {
                LocationCode = group.Key,
                Quantity = group.Sum(stock => stock.PhysicalQty),
            })
            .ToListAsync(ct);
        var tasks = await (
            from detail in _db.OutboundOrderDetails.AsNoTracking()
            join order in _db.OutboundOrders.AsNoTracking()
                on detail.OutboundNo equals order.OutboundNo
            where detail.LocationCd != null &&
                  codes.Contains(detail.LocationCd) &&
                  ActiveOutboundStatuses.Contains(order.Status) &&
                  detail.AllocatedQty > detail.ShippedQty &&
                  (detail.WarehouseCd ?? order.WarehouseCd) ==
                  context.WarehouseCode
            select new
            {
                LocationCode = detail.LocationCd!,
                detail.OutboundNo,
            }).Distinct().ToListAsync(ct);
        var pallets = await _db.Pallets
            .AsNoTracking()
            .Where(pallet =>
                pallet.WarehouseCd == context.WarehouseCode &&
                codes.Contains(pallet.LocationCd) &&
                pallet.Status != PalletStatus.Shipped)
            .Select(pallet => new
            {
                pallet.LocationCd,
                pallet.PalletNo,
                pallet.CartonQty,
            })
            .ToListAsync(ct);
        var result = new List<SpaceWmsBlockingReference>();
        result.AddRange(stocks.Select(stock =>
            new SpaceWmsBlockingReference(
                codeToId[stock.LocationCode],
                SpaceWmsBlockingReferenceKind.Inventory,
                $"stock:{context.WarehouseCode}:{stock.LocationCode}",
                stock.Quantity)));
        result.AddRange(tasks.Select(task =>
            new SpaceWmsBlockingReference(
                codeToId[task.LocationCode],
                SpaceWmsBlockingReferenceKind.ActiveTask,
                task.OutboundNo,
                null)));
        result.AddRange(pallets.Select(pallet =>
            new SpaceWmsBlockingReference(
                codeToId[pallet.LocationCd],
                SpaceWmsBlockingReferenceKind.Container,
                pallet.PalletNo,
                pallet.CartonQty)));
        return new SpaceWmsBlockingReferences(
            Source(),
            result
                .OrderBy(reference => reference.LogicalId)
                .ThenBy(reference => reference.Kind)
                .ThenBy(
                    reference => reference.ReferenceId,
                    StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<IReadOnlyList<WmsBin>> LoadBinsAsync(
        SpaceWmsContext context,
        IReadOnlyCollection<Guid> logicalIds,
        CancellationToken ct)
    {
        if (logicalIds.Count == 0)
        {
            return await _db.WmsBins
                .AsNoTracking()
                .Where(bin => bin.WarehouseCd == context.WarehouseCode)
                .OrderBy(bin => bin.Id)
                .ToListAsync(ct);
        }
        var ids = logicalIds.ToHashSet();
        return await _db.WmsBins
            .AsNoTracking()
            .Where(bin =>
                ids.Contains(bin.Id) &&
                bin.WarehouseCd == context.WarehouseCode)
            .OrderBy(bin => bin.Id)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<SpaceWmsLocationState>>
        LoadLocationStatesAsync(
            SpaceWmsContext context,
            IReadOnlyCollection<Guid> logicalIds,
            CancellationToken ct)
    {
        var bins = await LoadBinsAsync(context, logicalIds, ct);
        return bins.Select(ToLocationState).ToArray();
    }

    private static SpaceWmsLocationState ToLocationState(WmsBin bin) =>
        new(
            bin.Id,
            bin.LocationCode,
            bin.Id.ToString("D"),
            bin.IsActive,
            bin.Version.ToString(CultureInfo.InvariantCulture),
            StateHash(bin));

    private static void Apply(
        WmsBin bin,
        string warehouseCode,
        SpaceWmsLocationMutation item,
        string pathJson,
        string attributesJson,
        bool active)
    {
        bin.LocationCode = item.LocationCode;
        bin.WarehouseCd = warehouseCode;
        bin.PathJson = pathJson;
        bin.AttrsJson = attributesJson;
        bin.IsActive = active;
        bin.Version = item.Version;
        bin.LastPublishedAt = DateTime.UtcNow;
        bin.LastPublishedBy = "space-design-v1";
    }

    private static bool Matches(
        WmsBin bin,
        string warehouseCode,
        string locationCode,
        string pathJson,
        string attributesJson,
        bool active) =>
        string.Equals(
            bin.WarehouseCd,
            warehouseCode,
            StringComparison.Ordinal) &&
        string.Equals(
            bin.LocationCode,
            locationCode,
            StringComparison.Ordinal) &&
        string.Equals(bin.PathJson, pathJson, StringComparison.Ordinal) &&
        string.Equals(
            bin.AttrsJson,
            attributesJson,
            StringComparison.Ordinal) &&
        bin.IsActive == active;

    private static SpaceWmsItemReceipt SuccessReceipt(
        SpaceWmsLocationMutation item,
        WmsBin bin,
        SpaceWmsItemOutcome outcome) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            outcome,
            bin.Id.ToString("D"),
            bin.Version.ToString(CultureInfo.InvariantCulture),
            StateHash(bin),
            null);

    private static SpaceWmsItemReceipt FailureReceipt(
        SpaceWmsLocationMutation item,
        string errorCode) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            SpaceWmsItemOutcome.NotApplied,
            null,
            null,
            null,
            errorCode);

    private static SpaceWmsBatchResult ConflictResult(
        SpaceWmsBatch batch) =>
        FailureResult(batch, "WMS_IDEMPOTENCY_CONFLICT");

    private static SpaceWmsBatchResult FailureResult(
        SpaceWmsBatch batch,
        string errorCode) =>
        new(
            batch.OperationKey,
            batch.PayloadHash,
            null,
            batch.Items
                .Select(item => FailureReceipt(item, errorCode))
                .ToArray(),
            DateTimeOffset.UtcNow);

    private static SpaceWmsOperationState ToOperationState(
        SpaceWmsBatchAssessmentKind assessment) =>
        assessment switch
        {
            SpaceWmsBatchAssessmentKind.Succeeded =>
                SpaceWmsOperationState.Applied,
            SpaceWmsBatchAssessmentKind.FailedNoEffect =>
                SpaceWmsOperationState.FailedNoEffect,
            SpaceWmsBatchAssessmentKind.Partial =>
                SpaceWmsOperationState.Partial,
            _ => SpaceWmsOperationState.Unknown,
        };

    private static string StateHash(WmsBin bin) =>
        Hash(string.Join(
            "\n",
            bin.Id.ToString("D"),
            bin.WarehouseCd,
            bin.LocationCode,
            bin.Version.ToString(CultureInfo.InvariantCulture),
            bin.IsActive ? "1" : "0",
            bin.PathJson,
            bin.AttrsJson));

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceWmsSourceMetadata Source() =>
        new(
            SpaceWmsDataSourceKind.Real,
            DataSourceId,
            DateTimeOffset.UtcNow);

    private static string DispatchStatus(int value) =>
        value switch
        {
            MobileTaskStatus.Pending => "Pending",
            MobileTaskStatus.InProgress => "InProgress",
            MobileTaskStatus.Completed => "Completed",
            MobileTaskStatus.PartiallyCompleted => "PartiallyCompleted",
            MobileTaskStatus.Paused => "Paused",
            MobileTaskStatus.Exception => "Exception",
            MobileTaskStatus.Cancelled => "Cancelled",
            _ => $"Unknown:{value.ToString(CultureInfo.InvariantCulture)}",
        };

    private static SpaceWmsCapabilitySnapshot CapabilitySnapshot() =>
        SpaceWmsCapabilitySnapshot.Create(
            AdapterId,
            SpaceWmsDataSourceKind.Real,
            SpaceWmsCertificationLevel.CertifiedIdempotent,
            Capabilities,
            DateTimeOffset.UtcNow);

    private void EnsureScope(SpaceWmsContext context)
    {
        SpaceWmsContract.ValidateContext(context);
        if (context.TenantId != _db.CurrentTenantId)
            throw new InvalidOperationException(
                "SPACE_TENANT_SCOPE_DENIED");
        if (context.WarehouseCode.Length > 10)
            throw new InvalidOperationException(
                "SPACE_WMS_LOCATION_CODE_UNSUPPORTED");
    }

    private void EnsureCleanWriteContext()
    {
        _db.ChangeTracker.DetectChanges();
        if (_db.ChangeTracker.Entries().Any(entry =>
                entry.State is
                    EntityState.Added or
                    EntityState.Modified or
                    EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "SPACE_WMS_CONTEXT_DIRTY");
        }
    }

    private void DetachOwnWrites()
    {
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(entry =>
                         entry.Entity is WmsBin or SpaceWmsOperation &&
                         entry.State is
                             EntityState.Added or
                             EntityState.Modified)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
