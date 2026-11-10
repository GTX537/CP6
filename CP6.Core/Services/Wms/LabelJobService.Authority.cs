using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class LabelJobService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;

    public LabelJobService(CP6Context db, IamAdministrationV1? administration, TenantManagementSessionV1? session)
    { _db = db; _administration = administration; _session = session; }

    public Task<IReadOnlyList<LabelTemplateDto>> GetTemplatesAsync(CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyGetTemplatesAsync(ct)
            : CurrentLabel("label-manage", true, (_, token) => LegacyGetTemplatesAsync(token), ct);

    public Task<LabelTemplateDto> UpsertTemplateAsync(UpsertLabelTemplateRequest request, string? userName, CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyUpsertTemplateAsync(request, userName, ct)
            : CurrentLabel("label-manage", false, (actor, token) => LegacyUpsertTemplateAsync(request, actor.UserName, token), ct);

    public Task<PagedResult<LabelJobDto>> GetJobsAsync(string? status, string? warehouseCd, int page, int pageSize, CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyGetJobsAsync(status, warehouseCd, page, pageSize, ct)
            : CurrentLabel("label-print", true, (_, token) => CurrentJobs(status, warehouseCd, page, pageSize, token), ct);

    public Task<LabelJobDto> CreateJobAsync(CreateLabelJobRequest request, string? userName, CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyCreateJobAsync(request, userName, ct)
            : CurrentLabel("label-print", false, (actor, token) => CurrentCreate(request, actor.UserName, token), ct);

    public Task<LabelJobDto> ClaimAsync(string jobNo, LabelJobCommand request, string? userName, CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyClaimAsync(jobNo, request, userName, ct)
            : CurrentLabel("label-print", false, (actor, token) => CurrentCommand(jobNo, request, "label-claim", actor.UserName, token), ct);

    public Task<LabelJobDto> CompleteAsync(string jobNo, LabelJobCommand request, bool success, string? userName, CancellationToken ct = default)
        => !_db.Database.IsNpgsql() ? LegacyCompleteAsync(jobNo, request, success, userName, ct)
            : CurrentLabel("label-print", false, (actor, token) => CurrentCommand(jobNo, request, success ? "label-complete" : "label-fail", actor.UserName, token), ct);

    private async Task<T> CurrentLabel<T>(string action, bool read, Func<UserPermissionContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("LABEL_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-mobile:" + action, _session.CurrentActor, async (actor, token) =>
            {
                var tables = "LOCK TABLE public.\"T_LabelTemplate\", public.\"T_LabelJob\", public.\"T_TaskCommandReceipt\" IN "
                    + (read ? "SHARE" : "SHARE ROW EXCLUSIVE") + " MODE; LOCK TABLE public.\"T_ClientDevice\" IN SHARE MODE";
                await _db.Database.ExecuteSqlRawAsync(tables, token);
                var result = await body(actor, token);
                if (await _session.CurrentActor(token) != actor.UserId)
                    throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                return result;
            }, ct);
        }
        catch (IamAdministrationExceptionV1 error)
        { throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN" ? "LABEL_OUTCOME_UNKNOWN" : error.Message, error.Status, error); }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("LABEL_AUTHORITY_UNAVAILABLE", 503, error); }
    }

    private async Task<PagedResult<LabelJobDto>> CurrentJobs(string? status, string? warehouseCd, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.LabelJobs.AsNoTracking().Where(j => !j.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) query = query.Where(j => j.WarehouseCd == warehouseCd);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(j => j.RequestedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var operations = rows.Select(j => j.OperationId).ToArray();
        var frames = await _db.TaskCommandReceipts.AsNoTracking().Where(r => operations.Contains(r.OperationId) && r.CommandName == "label-create").ToDictionaryAsync(r => r.OperationId, ct);
        var names = rows.Where(j => !frames.ContainsKey(j.OperationId)).Select(j => j.TemplateName).Distinct().ToArray();
        var templates = await _db.LabelTemplates.AsNoTracking().Where(t => !t.IsDeleted && names.Contains(t.TemplateName)).ToDictionaryAsync(t => t.TemplateName, ct);
        return new PagedResult<LabelJobDto> { Total = total, Page = page, PageSize = pageSize,
            Items = rows.Select(j => MapJob(j, templates.GetValueOrDefault(j.TemplateName), frames.TryGetValue(j.OperationId, out var frame) ? LabelReceiptV1.Read(frame.ResultJson, j.JobNo) : null)).ToList() };
    }

    private async Task<LabelJobDto> CurrentCreate(CreateLabelJobRequest request, string? actor, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.WarehouseCd) || string.IsNullOrWhiteSpace(request.TemplateName))
            throw new ArgumentException("WM-LABEL-JOB-DATA");
        try { using var parsed = JsonDocument.Parse(request.PayloadJson); }
        catch (JsonException) { throw new ArgumentException("WM-LABEL-PAYLOAD-JSON"); }
        var binding = LabelReceiptV1.Bind(new { Command = "label-create-v1", request.WarehouseCd, request.TemplateName, request.PayloadJson,
            PrinterName = NullIfWhiteSpace(request.PrinterName), DeviceId = NullIfWhiteSpace(request.DeviceId) });
        var receipt = await _db.TaskCommandReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.OperationId == request.OperationId, ct);
        if (receipt != null)
        {
            if (receipt.CommandName != "label-create") throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
            return LabelReceiptV1.Read(receipt.ResultJson, receipt.TaskNo, binding);
        }
        var existing = await _db.LabelJobs.AsNoTracking().SingleOrDefaultAsync(j => j.OperationId == request.OperationId, ct);
        if (existing != null)
        {
            // An older job has no original create snapshot. Preserve its legacy result without fabricating one.
            var legacyTemplate = await _db.LabelTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateName == existing.TemplateName, ct);
            return MapJob(existing, legacyTemplate);
        }
        if (!await _db.WmsFeatureFlags.AsNoTracking().AnyAsync(f => !f.IsDeleted && f.WarehouseCd == request.WarehouseCd && f.SerialLpnEnabled, ct))
            throw new MobileTaskConflictException("WM-R2B-DISABLED");
        var template = await _db.LabelTemplates.AsNoTracking().SingleOrDefaultAsync(t => !t.IsDeleted && t.IsEnabled && t.TemplateName == request.TemplateName, ct)
            ?? throw new ArgumentException("WM-LABEL-TEMPLATE-NOT-FOUND");
        var job = new LabelJob { JobNo = $"LBL{DateTime.UtcNow:yyMMdd}{Guid.NewGuid():N}"[..25], OperationId = request.OperationId,
            WarehouseCd = request.WarehouseCd.Trim(), TemplateName = template.TemplateName, PayloadJson = request.PayloadJson,
            PrinterName = NullIfWhiteSpace(request.PrinterName), RequestedDeviceId = NullIfWhiteSpace(request.DeviceId), Status = LabelJobStatus.Pending,
            RequestedBy = actor, RequestedAt = DateTime.UtcNow, Creator = actor };
        _db.LabelJobs.Add(job); await _db.SaveChangesAsync(ct);
        var result = MapJob(job, template);
        StoreCurrentReceipt(job.JobNo, request.OperationId, "label-create", binding, result);
        await _db.SaveChangesAsync(ct); return result;
    }

    private async Task<LabelJobDto> CurrentCommand(string jobNo, LabelJobCommand request, string command, string? actor, CancellationToken ct)
    {
        ValidateCommand(request);
        var job = await LoadAsync(jobNo, ct);
        if (!string.IsNullOrWhiteSpace(job.RequestedDeviceId) && job.RequestedDeviceId != request.DeviceId)
            throw new MobileTaskConflictException(command == "label-claim" ? "WM-LABEL-JOB-DEVICE-SCOPE" : "WM-LABEL-JOB-NOT-OWNED");
        var device = await _db.ClientDevices.AsNoTracking().SingleOrDefaultAsync(d => !d.IsDeleted && d.DeviceId == request.DeviceId, ct);
        if (device == null || device.Platform != "Windows" || device.Status != ClientDeviceStatus.Active)
            throw new MobileTaskConflictException("WM-LABEL-GATEWAY-NOT-ACTIVE");
        if (device.WarehouseCd != null && device.WarehouseCd != job.WarehouseCd)
            throw new MobileTaskConflictException("WM-LABEL-GATEWAY-SCOPE");
        var binding = LabelReceiptV1.Bind(new { Command = command, JobNo = jobNo, request.DeviceId, request.RowVersion, ResultMessage = NullIfWhiteSpace(request.ResultMessage) });
        var receipt = await _db.TaskCommandReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.OperationId == request.OperationId, ct);
        if (receipt != null)
        {
            if (receipt.TaskNo != jobNo || receipt.CommandName != command) throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
            var original = LabelReceiptV1.Read(receipt.ResultJson, jobNo, binding);
            if (original.RequestedDeviceId != request.DeviceId) throw new MobileTaskConflictException("WM-LABEL-JOB-NOT-OWNED");
            return original;
        }
        ApplyRowVersion(job, request.RowVersion);
        if (command == "label-claim")
        {
            if (job.Status != LabelJobStatus.Pending) throw new MobileTaskConflictException("WM-LABEL-JOB-NOT-PENDING");
            job.Status = LabelJobStatus.Printing; job.RequestedDeviceId = device.DeviceId; job.AttemptCount++;
        }
        else
        {
            if (job.Status != LabelJobStatus.Printing || job.RequestedDeviceId != request.DeviceId)
                throw new MobileTaskConflictException("WM-LABEL-JOB-NOT-OWNED");
            job.Status = command == "label-complete" ? LabelJobStatus.Completed : LabelJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow; job.ResultMessage = NullIfWhiteSpace(request.ResultMessage);
        }
        job.Modifier = actor; job.ModifyDate = DateTime.Now;
        await SaveWithConflictAsync(ct);
        var createFrame = await _db.TaskCommandReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.OperationId == job.OperationId && r.CommandName == "label-create", ct);
        var frozen = createFrame == null ? null : LabelReceiptV1.Read(createFrame.ResultJson, jobNo);
        var template = frozen == null ? await GetTemplateAsync(job.TemplateName, ct) : null;
        var result = MapJob(job, template, frozen);
        StoreCurrentReceipt(jobNo, request.OperationId, command, binding, result);
        await _db.SaveChangesAsync(ct); return result;
    }

    private void StoreCurrentReceipt(string jobNo, Guid operation, string command, string binding, LabelJobDto result)
        => _db.TaskCommandReceipts.Add(new TaskCommandReceipt { OperationId = operation, TaskNo = jobNo,
            CommandName = command, ResultJson = LabelReceiptV1.Encode(binding, result) });
}
