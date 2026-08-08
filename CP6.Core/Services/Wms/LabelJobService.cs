using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using System.Transactions;

namespace CP6.Core.Services.Wms;

public sealed class LabelJobService : ILabelJobService
{
    private readonly CP6Context _db;
    public LabelJobService(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<LabelTemplateDto>> GetTemplatesAsync(
        CancellationToken ct = default)
        => (await _db.LabelTemplates.AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.TemplateName)
                .ToListAsync(ct))
            .Select(MapTemplate).ToList();

    public async Task<LabelTemplateDto> UpsertTemplateAsync(
        UpsertLabelTemplateRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateTemplate(request);
        LabelTemplate? row = null;
        if (request.Id.HasValue)
            row = await _db.LabelTemplates.FirstOrDefaultAsync(
                x => x.Id == request.Id && !x.IsDeleted, ct);
        row ??= await _db.LabelTemplates.FirstOrDefaultAsync(
            x => x.TemplateName == request.TemplateName && !x.IsDeleted, ct);
        if (row is null)
        {
            row = new LabelTemplate { Creator = userName };
            _db.LabelTemplates.Add(row);
        }
        else
        {
            ApplyRowVersion(row, request.RowVersion);
            row.Modifier = userName;
            row.ModifyDate = DateTime.Now;
        }
        row.TemplateName = request.TemplateName.Trim();
        row.Format = request.Format.Trim().ToUpperInvariant();
        row.TemplateBody = request.TemplateBody;
        row.Language = NullIfWhiteSpace(request.Language);
        row.IsEnabled = request.IsEnabled;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        return MapTemplate(row);
    }

    public async Task<PagedResult<LabelJobDto>> GetJobsAsync(
        string? status,
        string? warehouseCd,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.LabelJobs.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query = query.Where(x => x.WarehouseCd == warehouseCd);
        var total = await query.CountAsync(ct);
        var jobs = await query.OrderBy(x => x.RequestedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = jobs.Select(x => x.TemplateName).Distinct().ToList();
        var templates = await _db.LabelTemplates.AsNoTracking()
            .Where(x => !x.IsDeleted && names.Contains(x.TemplateName))
            .ToDictionaryAsync(x => x.TemplateName, ct);
        return new PagedResult<LabelJobDto>
        {
            Items = jobs.Select(x => MapJob(x, templates.GetValueOrDefault(x.TemplateName)))
                .ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LabelJobDto> CreateJobAsync(
        CreateLabelJobRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.WarehouseCd)
            || string.IsNullOrWhiteSpace(request.TemplateName))
            throw new ArgumentException("WM-LABEL-JOB-DATA");
        try { _ = JsonDocument.Parse(request.PayloadJson); }
        catch (JsonException) { throw new ArgumentException("WM-LABEL-PAYLOAD-JSON"); }
        var existing = await _db.LabelJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == request.OperationId, ct);
        if (existing is not null)
        {
            var existingTemplate = await _db.LabelTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TemplateName == existing.TemplateName, ct);
            return MapJob(existing, existingTemplate);
        }
        if (!await _db.WmsFeatureFlags.AsNoTracking().AnyAsync(
            x => !x.IsDeleted
                 && x.WarehouseCd == request.WarehouseCd
                 && x.SerialLpnEnabled, ct))
            throw new MobileTaskConflictException("WM-R2B-DISABLED");
        var template = await _db.LabelTemplates.FirstOrDefaultAsync(
            x => !x.IsDeleted
                 && x.IsEnabled
                 && x.TemplateName == request.TemplateName, ct)
            ?? throw new ArgumentException("WM-LABEL-TEMPLATE-NOT-FOUND");
        var job = new LabelJob
        {
            JobNo = $"LBL{DateTime.UtcNow:yyMMdd}{Guid.NewGuid():N}"[..25],
            OperationId = request.OperationId,
            WarehouseCd = request.WarehouseCd.Trim(),
            TemplateName = template.TemplateName,
            PayloadJson = request.PayloadJson,
            PrinterName = NullIfWhiteSpace(request.PrinterName),
            Status = LabelJobStatus.Pending,
            RequestedDeviceId = NullIfWhiteSpace(request.DeviceId),
            RequestedBy = userName,
            RequestedAt = DateTime.UtcNow,
            Creator = userName
        };
        _db.LabelJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return MapJob(job, template);
    }

    public async Task<LabelJobDto> ClaimAsync(
        string jobNo,
        LabelJobCommand request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateCommand(request);
        var replay = await ReplayAsync(jobNo, request.OperationId, "label-claim", ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        var job = await LoadAsync(jobNo, ct);
        ApplyRowVersion(job, request.RowVersion);
        if (job.Status != LabelJobStatus.Pending)
            throw new MobileTaskConflictException("WM-LABEL-JOB-NOT-PENDING");
        if (!string.IsNullOrWhiteSpace(job.RequestedDeviceId)
            && !string.Equals(job.RequestedDeviceId, request.DeviceId, StringComparison.Ordinal))
            throw new MobileTaskConflictException("WM-LABEL-JOB-DEVICE-SCOPE");
        var device = await _db.ClientDevices.AsNoTracking().FirstOrDefaultAsync(
            x => !x.IsDeleted
                 && x.DeviceId == request.DeviceId
                 && x.Platform == "Windows"
                 && x.Status == ClientDeviceStatus.Active, ct)
            ?? throw new MobileTaskConflictException("WM-LABEL-GATEWAY-NOT-ACTIVE");
        if (device.WarehouseCd is not null && device.WarehouseCd != job.WarehouseCd)
            throw new MobileTaskConflictException("WM-LABEL-GATEWAY-SCOPE");
        job.Status = LabelJobStatus.Printing;
        job.RequestedDeviceId = device.DeviceId;
        job.AttemptCount++;
        job.Modifier = userName;
        job.ModifyDate = DateTime.Now;
        await SaveWithConflictAsync(ct);
        var template = await GetTemplateAsync(job.TemplateName, ct);
        var result = MapJob(job, template);
        AddReceipt(jobNo, request.OperationId, "label-claim", result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    public async Task<LabelJobDto> CompleteAsync(
        string jobNo,
        LabelJobCommand request,
        bool success,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateCommand(request);
        var command = success ? "label-complete" : "label-fail";
        var replay = await ReplayAsync(jobNo, request.OperationId, command, ct);
        if (replay is not null) return replay;
        using var scope = BeginAmbientTransaction();
        var job = await LoadAsync(jobNo, ct);
        ApplyRowVersion(job, request.RowVersion);
        if (job.Status != LabelJobStatus.Printing
            || job.RequestedDeviceId != request.DeviceId)
            throw new MobileTaskConflictException("WM-LABEL-JOB-NOT-OWNED");
        job.Status = success ? LabelJobStatus.Completed : LabelJobStatus.Failed;
        job.CompletedAt = DateTime.UtcNow;
        job.ResultMessage = NullIfWhiteSpace(request.ResultMessage);
        job.Modifier = userName;
        job.ModifyDate = DateTime.Now;
        await SaveWithConflictAsync(ct);
        var template = await GetTemplateAsync(job.TemplateName, ct);
        var result = MapJob(job, template);
        AddReceipt(jobNo, request.OperationId, command, result);
        await _db.SaveChangesAsync(ct);
        scope?.Complete();
        return result;
    }

    private Task<LabelTemplate> GetTemplateAsync(string name, CancellationToken ct)
        => _db.LabelTemplates.AsNoTracking().FirstAsync(
            x => !x.IsDeleted && x.TemplateName == name, ct);

    private async Task<LabelJob> LoadAsync(string jobNo, CancellationToken ct)
        => await _db.LabelJobs.FirstOrDefaultAsync(
               x => !x.IsDeleted && x.JobNo == jobNo, ct)
           ?? throw new MobileTaskNotFoundException();

    private async Task<LabelJobDto?> ReplayAsync(
        string jobNo,
        Guid operationId,
        string command,
        CancellationToken ct)
    {
        var receipt = await _db.TaskCommandReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.TaskNo != jobNo || receipt.CommandName != command)
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
        return JsonSerializer.Deserialize<LabelJobDto>(receipt.ResultJson);
    }

    private void AddReceipt(
        string jobNo,
        Guid operationId,
        string command,
        LabelJobDto result)
        => _db.TaskCommandReceipts.Add(new TaskCommandReceipt
        {
            OperationId = operationId,
            TaskNo = jobNo,
            CommandName = command,
            ResultJson = JsonSerializer.Serialize(result)
        });

    private async Task SaveWithConflictAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
    }

    private void ApplyRowVersion<T>(T row, string? encoded)
        where T : CP6.Entity.BaseBizEntity
    {
        var current = row.RowVersion ?? Array.Empty<byte>();
        if (current.Length == 0) return;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(encoded ?? string.Empty); }
        catch (FormatException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        if (!CryptographicOperations.FixedTimeEquals(current, supplied))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        _db.Entry(row).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private static void ValidateTemplate(UpsertLabelTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateName)
            || string.IsNullOrWhiteSpace(request.TemplateBody))
            throw new ArgumentException("WM-LABEL-TEMPLATE-DATA");
        request.Format = request.Format.Trim().ToUpperInvariant();
        if (request.Format is not ("ZPL" or "TSPL" or "PDF"))
            throw new ArgumentException("WM-LABEL-TEMPLATE-FORMAT");
    }

    private static void ValidateCommand(LabelJobCommand request)
    {
        if (request.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.RowVersion)
            || string.IsNullOrWhiteSpace(request.DeviceId))
            throw new ArgumentException("WM-LABEL-COMMAND-DATA");
    }

    private static LabelTemplateDto MapTemplate(LabelTemplate x) => new()
    {
        Id = x.Id,
        TemplateName = x.TemplateName,
        Format = x.Format,
        TemplateBody = x.TemplateBody,
        Language = x.Language,
        IsEnabled = x.IsEnabled,
        RowVersion = Encode(x.RowVersion)
    };

    private static LabelJobDto MapJob(LabelJob x, LabelTemplate? template) => new()
    {
        JobNo = x.JobNo,
        OperationId = x.OperationId,
        WarehouseCd = x.WarehouseCd,
        TemplateName = x.TemplateName,
        Format = template?.Format ?? string.Empty,
        TemplateBody = template?.TemplateBody ?? string.Empty,
        PayloadJson = x.PayloadJson,
        PrinterName = x.PrinterName,
        Status = x.Status,
        RequestedDeviceId = x.RequestedDeviceId,
        RequestedBy = x.RequestedBy,
        RequestedAt = x.RequestedAt,
        CompletedAt = x.CompletedAt,
        AttemptCount = x.AttemptCount,
        ResultMessage = x.ResultMessage,
        RowVersion = Encode(x.RowVersion)
    };

    private static string Encode(byte[]? value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;
    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
}
