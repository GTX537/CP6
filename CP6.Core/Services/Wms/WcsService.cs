using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class WcsService : IWcsService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "WCS";

    public WcsService(CP6Context db, IWmsSequenceService seq) : this(db, seq, null, null) { }

    private async Task<List<WcsTask>> SearchCoreAsync(WcsTaskSearchQuery q)
    {
        var query = _db.WcsTasks.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.TaskNo))    query = query.Where(x => x.TaskNo.Contains(q.TaskNo));
        if (!string.IsNullOrWhiteSpace(q.TaskType))  query = query.Where(x => x.TaskType == q.TaskType);
        if (!string.IsNullOrWhiteSpace(q.DeviceCd))  query = query.Where(x => x.DeviceCd == q.DeviceCd);
        if (q.Status.HasValue)                       query = query.Where(x => x.Status == q.Status.Value);
        if (!string.IsNullOrWhiteSpace(q.RelatedNo)) query = query.Where(x => x.RelatedNo == q.RelatedNo);
        return await query.OrderByDescending(x => x.Priority).ThenByDescending(x => x.CreatedAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    private async Task<WcsTask?> GetCoreAsync(string taskNo)
        => await _db.WcsTasks.AsNoTracking().FirstOrDefaultAsync(x => x.TaskNo == taskNo && !x.IsDeleted);

    private async Task<string> CreateCoreAsync(WcsTaskDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.TaskType))
            throw new InvalidOperationException("TaskType required");

        var no = await _seq.NextAsync(Prefix);
        _db.WcsTasks.Add(new WcsTask
        {
            TaskNo = no,
            TaskType = dto.TaskType, Priority = dto.Priority,
            Status = WcsTaskStatus.Created,
            RelatedNo = dto.RelatedNo, RelatedType = dto.RelatedType,
            FromWarehouseCd = dto.FromWarehouseCd, FromLocationCd = dto.FromLocationCd,
            ToWarehouseCd = dto.ToWarehouseCd, ToLocationCd = dto.ToLocationCd,
            ProductCd = dto.ProductCd, LotNo = dto.LotNo,
            Qty = dto.Qty, UnitCd = dto.UnitCd,
            CreatedAt = DateTime.Now,
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    private async Task DispatchCoreAsync(string taskNo, string deviceCd, string? userName)
    {
        if (string.IsNullOrWhiteSpace(deviceCd)) throw new InvalidOperationException("deviceCd required");
        var t = await GetTracked(taskNo);
        if (t.Status != WcsTaskStatus.Created)
            throw new InvalidOperationException("WM-MSG-043: 作成状态のみ払出可");
        t.Status = WcsTaskStatus.Dispatched;
        t.DeviceCd = deviceCd;
        t.DispatchedAt = DateTime.Now;
        t.Modifier = userName; t.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task StartCoreAsync(string taskNo, string? userName)
    {
        var t = await GetTracked(taskNo);
        if (t.Status != WcsTaskStatus.Dispatched)
            throw new InvalidOperationException("WM-MSG-043: 払出済のみ実行可");
        t.Status = WcsTaskStatus.Executing;
        t.StartedAt = DateTime.Now;
        t.Modifier = userName; t.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task CompleteCoreAsync(string taskNo, string? userName)
    {
        var t = await GetTracked(taskNo);
        if (t.Status != WcsTaskStatus.Executing)
            throw new InvalidOperationException("WM-MSG-043: 実行中のみ完了可");
        t.Status = WcsTaskStatus.Completed;
        t.CompletedAt = DateTime.Now;
        t.Modifier = userName; t.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task FailCoreAsync(string taskNo, string errorMessage, string? userName)
    {
        var t = await GetTracked(taskNo);
        if (t.Status == WcsTaskStatus.Completed || t.Status == WcsTaskStatus.Failed)
            throw new InvalidOperationException("WM-MSG-043: 完了/失敗以降は変更不可");
        t.Status = WcsTaskStatus.Failed;
        t.ErrorMessage = errorMessage;
        t.CompletedAt = DateTime.Now;
        t.Modifier = userName; t.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task DeleteCoreAsync(string taskNo, string? userName)
    {
        var t = await GetTracked(taskNo);
        if (t.Status == WcsTaskStatus.Executing)
            throw new InvalidOperationException("WM-MSG-043: 実行中は削除不可");
        t.IsDeleted = true;
        t.Modifier = userName; t.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<WcsTask> GetTracked(string taskNo)
        => await _db.WcsTasks.FirstOrDefaultAsync(x => x.TaskNo == taskNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
