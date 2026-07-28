using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class WmsFeatureFlagChangeService : IWmsFeatureFlagChangeService
{
    public const string ApprovalBizType = "WMS_FEATURE_FLAG_CHANGE";

    private readonly CP6Context _db;
    private readonly IApprovalService _approval;
    private readonly ITaskCenterService _taskCenter;

    public WmsFeatureFlagChangeService(
        CP6Context db,
        IApprovalService approval,
        ITaskCenterService taskCenter)
    {
        _db = db;
        _approval = approval;
        _taskCenter = taskCenter;
    }

    public async Task<WmsFeatureFlagChangeDto> SubmitAsync(
        CreateWmsFeatureFlagChangeRequest request,
        Guid requestedById,
        string? requestedBy,
        CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty)
            throw Error("WM-FEATURE-OPERATION-ID-REQUIRED");
        if (requestedById == Guid.Empty)
            throw Error("WM-FEATURE-REQUESTER-REQUIRED");

        var warehouseCd = NormalizeWarehouse(request.WarehouseCd);
        var existingOperation = await _db.WmsFeatureFlagChanges
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationId == request.OperationId, ct);
        if (existingOperation is not null)
            return Map(existingOperation);

        if (!await _db.Warehouses.AnyAsync(
                x => !x.IsDeleted && x.WarehouseCd == warehouseCd, ct))
            throw Error("WM-V2-WAREHOUSE-NOT-FOUND");
        if (await _db.WmsFeatureFlagChanges.AnyAsync(
                x => !x.IsDeleted
                     && x.WarehouseCd == warehouseCd
                     && x.Status == WmsFeatureFlagChangeStatus.Pending, ct))
            throw Error("WM-FEATURE-CHANGE-ACTIVE");

        var feature = await _db.WmsFeatureFlags
            .SingleOrDefaultAsync(x => !x.IsDeleted && x.WarehouseCd == warehouseCd, ct);
        var currentRowVersion = EncodeRowVersion(feature?.RowVersion);
        ValidateBaseRowVersion(request.RowVersion, currentRowVersion);
        ValidateTarget(
            feature?.ProductionMoveEnabled ?? false,
            feature?.SerialLpnEnabled ?? false,
            request.ProductionMoveEnabled,
            request.SerialLpnEnabled,
            request.ScanRetentionDays,
            request.Reason,
            request.ChangeTicket,
            request.EvidenceUri);

        var flowInstanceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var row = new WmsFeatureFlagChange
        {
            Id = Guid.NewGuid(),
            OperationId = request.OperationId,
            WarehouseCd = warehouseCd,
            BaseProductionMoveEnabled = feature?.ProductionMoveEnabled ?? false,
            BaseSerialLpnEnabled = feature?.SerialLpnEnabled ?? false,
            BaseScanRetentionDays = feature?.ScanRetentionDays ?? 180,
            BaseFeatureRowVersion = currentRowVersion,
            TargetProductionMoveEnabled = request.ProductionMoveEnabled,
            TargetSerialLpnEnabled = request.SerialLpnEnabled,
            TargetScanRetentionDays = request.ScanRetentionDays,
            Reason = request.Reason.Trim(),
            ChangeTicket = request.ChangeTicket.Trim(),
            EvidenceUri = NormalizeOptional(request.EvidenceUri),
            Status = WmsFeatureFlagChangeStatus.Pending,
            RequestedById = requestedById,
            RequestedAtUtc = now,
            FlowInstanceId = flowInstanceId,
            Creator = requestedBy,
            CreateDate = now,
        };
        _db.WmsFeatureFlagChanges.Add(row);

        await _approval.SubmitAsync(
            ApprovalBizType,
            row.Id.ToString(),
            requestedById,
            new
            {
                row.OperationId,
                row.WarehouseCd,
                row.BaseProductionMoveEnabled,
                row.BaseSerialLpnEnabled,
                row.BaseScanRetentionDays,
                row.TargetProductionMoveEnabled,
                row.TargetSerialLpnEnabled,
                row.TargetScanRetentionDays,
                row.Reason,
                row.ChangeTicket,
                row.EvidenceUri,
            },
            flowInstanceId);

        return Map(row);
    }

    public async Task<IReadOnlyList<WmsFeatureFlagChangeDto>> GetAsync(
        WmsFeatureFlagChangeQuery query,
        CancellationToken ct = default)
    {
        var rows = _db.WmsFeatureFlagChanges.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query.WarehouseCd))
        {
            var warehouseCd = NormalizeWarehouse(query.WarehouseCd);
            rows = rows.Where(x => x.WarehouseCd == warehouseCd);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToUpperInvariant();
            rows = rows.Where(x => x.Status == status);
        }
        var materialized = await rows.OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);
        return materialized.Select(Map).ToList();
    }

    public async Task CancelAsync(
        Guid id,
        Guid requestedById,
        string? requestedBy,
        CancellationToken ct = default)
    {
        var row = await _db.WmsFeatureFlagChanges
                      .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                  ?? throw Error("WM-FEATURE-CHANGE-NOT-FOUND");
        if (row.RequestedById != requestedById)
            throw Error("WM-FEATURE-CHANGE-CANCEL-FORBIDDEN");
        if (row.Status != WmsFeatureFlagChangeStatus.Pending)
            throw Error("WM-FEATURE-CHANGE-NOT-PENDING");

        row.Status = WmsFeatureFlagChangeStatus.Cancelled;
        row.Modifier = requestedBy;
        row.ModifyDate = DateTime.UtcNow;
        await _taskCenter.WithdrawAsync(row.FlowInstanceId, requestedById);
    }

    public async Task ApplyApprovedAsync(
        Guid id,
        ApprovalCallbackContext context,
        CancellationToken ct = default)
    {
        var row = await RequiredPendingAsync(id, context, ct);
        if (context.DecidedById is null || context.DecidedById == row.RequestedById)
            throw Error("WM-FEATURE-APPROVER-SEPARATION");

        row.DecidedById = context.DecidedById;
        row.DecidedAtUtc = DateTime.UtcNow;
        row.Modifier = context.DecidedById.Value.ToString();
        row.ModifyDate = DateTime.UtcNow;

        var feature = await _db.WmsFeatureFlags
            .SingleOrDefaultAsync(x => !x.IsDeleted && x.WarehouseCd == row.WarehouseCd, ct);
        if (!string.Equals(
                row.BaseFeatureRowVersion,
                EncodeRowVersion(feature?.RowVersion),
                StringComparison.Ordinal))
        {
            row.Status = WmsFeatureFlagChangeStatus.Stale;
            row.FailureCode = "WM-FEATURE-CHANGE-STALE";
            return;
        }

        var failureCode = ValidateTargetForApplication(
            feature?.ProductionMoveEnabled ?? false,
            feature?.SerialLpnEnabled ?? false,
            row.TargetProductionMoveEnabled,
            row.TargetSerialLpnEnabled,
            row.EvidenceUri);
        if (failureCode is not null)
        {
            row.Status = WmsFeatureFlagChangeStatus.Failed;
            row.FailureCode = failureCode;
            return;
        }

        if (feature is null)
        {
            feature = new WmsFeatureFlag
            {
                WarehouseCd = row.WarehouseCd,
                Creator = context.DecidedById.Value.ToString(),
                CreateDate = DateTime.UtcNow,
            };
            _db.WmsFeatureFlags.Add(feature);
        }
        feature.ProductionMoveEnabled = row.TargetProductionMoveEnabled;
        feature.SerialLpnEnabled = row.TargetSerialLpnEnabled;
        feature.ScanRetentionDays = row.TargetScanRetentionDays;
        feature.Modifier = context.DecidedById.Value.ToString();
        feature.ModifyDate = DateTime.UtcNow;

        row.Status = WmsFeatureFlagChangeStatus.Applied;
        row.AppliedAtUtc = DateTime.UtcNow;
        row.FailureCode = null;
    }

    public async Task ApplyRejectedAsync(
        Guid id,
        ApprovalCallbackContext context,
        CancellationToken ct = default)
    {
        var row = await RequiredPendingAsync(id, context, ct);
        if (context.DecidedById is null || context.DecidedById == row.RequestedById)
            throw Error("WM-FEATURE-APPROVER-SEPARATION");

        row.Status = WmsFeatureFlagChangeStatus.Rejected;
        row.DecidedById = context.DecidedById;
        row.DecidedAtUtc = DateTime.UtcNow;
        row.FailureCode = null;
        row.Modifier = context.DecidedById.Value.ToString();
        row.ModifyDate = DateTime.UtcNow;
    }

    private async Task<WmsFeatureFlagChange> RequiredPendingAsync(
        Guid id,
        ApprovalCallbackContext context,
        CancellationToken ct)
    {
        var row = await _db.WmsFeatureFlagChanges
                      .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                  ?? throw Error("WM-FEATURE-CHANGE-NOT-FOUND");
        if (row.FlowInstanceId != context.InstanceId)
            throw Error("WM-FEATURE-APPROVAL-INSTANCE-MISMATCH");
        if (row.Status != WmsFeatureFlagChangeStatus.Pending)
            throw Error("WM-FEATURE-CHANGE-NOT-PENDING");
        return row;
    }

    private static void ValidateBaseRowVersion(string supplied, string current)
    {
        supplied = supplied?.Trim() ?? string.Empty;
        if (supplied.Length > 0)
        {
            try { _ = Convert.FromBase64String(supplied); }
            catch (FormatException) { throw Error("WM-FEATURE-ROWVERSION-INVALID"); }
        }
        if (!string.Equals(supplied, current, StringComparison.Ordinal))
            throw Error("WM-FEATURE-CHANGE-STALE");
    }

    private static void ValidateTarget(
        bool currentMove,
        bool currentSerial,
        bool targetMove,
        bool targetSerial,
        int retentionDays,
        string reason,
        string changeTicket,
        string? evidenceUri)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw Error("WM-FEATURE-REASON-REQUIRED");
        if (reason.Trim().Length > 500)
            throw Error("WM-FEATURE-REASON-INVALID");
        if (string.IsNullOrWhiteSpace(changeTicket) || changeTicket.Trim().Length > 100)
            throw Error("WM-FEATURE-TICKET-REQUIRED");
        if (retentionDays is < 30 or > 3650)
            throw Error("WM-FEATURE-RETENTION-INVALID");

        var failure = ValidateTargetForApplication(
            currentMove, currentSerial, targetMove, targetSerial, evidenceUri);
        if (failure is not null) throw Error(failure);
    }

    private static string? ValidateTargetForApplication(
        bool currentMove,
        bool currentSerial,
        bool targetMove,
        bool targetSerial,
        string? evidenceUri)
    {
        if (targetSerial && !targetMove)
            return "WM-FEATURE-SERIAL-REQUIRES-MOVE";
        if (currentSerial && !targetMove)
            return "WM-FEATURE-DISABLE-SERIAL-FIRST";
        if (!currentSerial && targetSerial && !IsEvidenceUri(evidenceUri))
            return "WM-FEATURE-R2A-EVIDENCE-REQUIRED";
        return null;
    }

    private static bool IsEvidenceUri(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals("s3", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeWarehouse(string value)
    {
        var result = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (result.Length is < 1 or > 10)
            throw Error("WM-FEATURE-WAREHOUSE-INVALID");
        return result;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EncodeRowVersion(byte[]? rowVersion)
        => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    private static WmsFeatureFlagChangeException Error(string code) => new(code);

    private static WmsFeatureFlagChangeDto Map(WmsFeatureFlagChange row) => new()
    {
        Id = row.Id,
        OperationId = row.OperationId,
        WarehouseCd = row.WarehouseCd,
        BaseProductionMoveEnabled = row.BaseProductionMoveEnabled,
        BaseSerialLpnEnabled = row.BaseSerialLpnEnabled,
        BaseScanRetentionDays = row.BaseScanRetentionDays,
        BaseFeatureRowVersion = row.BaseFeatureRowVersion,
        TargetProductionMoveEnabled = row.TargetProductionMoveEnabled,
        TargetSerialLpnEnabled = row.TargetSerialLpnEnabled,
        TargetScanRetentionDays = row.TargetScanRetentionDays,
        Reason = row.Reason,
        ChangeTicket = row.ChangeTicket,
        EvidenceUri = row.EvidenceUri,
        Status = row.Status,
        RequestedById = row.RequestedById,
        RequestedAtUtc = row.RequestedAtUtc,
        FlowInstanceId = row.FlowInstanceId,
        DecidedById = row.DecidedById,
        DecidedAtUtc = row.DecidedAtUtc,
        AppliedAtUtc = row.AppliedAtUtc,
        FailureCode = row.FailureCode,
    };
}
