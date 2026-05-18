using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME080 — 不良品管理 業務サービス実装
/// </summary>
/// <remarks>
/// ステータス遷移：0=起票済 → 1=分析中 → 2=是正中 → 3=完了
/// </remarks>
public class DefectRecordService : IDefectRecordService
{
    private readonly CP6Context _db;
    private readonly IMesSequenceService _seq;
    private readonly IMesNotifier _notifier;
    private const string DfSeqKey = "DF";

    public DefectRecordService(CP6Context db, IMesSequenceService seq, IMesNotifier notifier)
    {
        _db = db;
        _seq = seq;
        _notifier = notifier;
    }

    public async Task<DefectRecordDto?> GetByNoAsync(string defectNo)
    {
        var x = await _db.DefectRecords.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefectNo == defectNo && !d.IsDeleted);
        if (x == null) return null;
        var dto = ToDto(x);
        await EnrichAsync(new[] { dto });
        return dto;
    }

    public async Task<PagedResultDto<DefectRecordDto>> SearchAsync(DefectRecordSearchQuery q)
    {
        var query = _db.DefectRecords.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.DefectNo)) query = query.Where(x => x.DefectNo.Contains(q.DefectNo));
        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo.Contains(q.WorkOrderNo));
        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) query = query.Where(x => x.ProcessCd == q.ProcessCd);
        if (!string.IsNullOrWhiteSpace(q.CategoryCd)) query = query.Where(x => x.CategoryCd == q.CategoryCd);
        if (!string.IsNullOrWhiteSpace(q.DetailCd)) query = query.Where(x => x.DetailCd == q.DetailCd);
        if (q.Statuses?.Any() == true) query = query.Where(x => q.Statuses.Contains(x.Status));
        if (!string.IsNullOrWhiteSpace(q.AssigneeCd)) query = query.Where(x => x.AssigneeCd == q.AssigneeCd);
        if (q.OccurDateFrom.HasValue) query = query.Where(x => x.OccurDate >= q.OccurDateFrom);
        if (q.OccurDateTo.HasValue) query = query.Where(x => x.OccurDate <= q.OccurDateTo);

        var total = await query.CountAsync();
        var page = q.PageIndex <= 0 ? 1 : q.PageIndex;
        var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 500);

        var items = await query
            .OrderByDescending(x => x.CreateDate)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();

        var dtos = items.Select(ToDto).ToList();
        await EnrichAsync(dtos);

        return new PagedResultDto<DefectRecordDto>
        {
            Total = total, PageIndex = page, PageSize = size, Items = dtos,
        };
    }

    public async Task<string> CreateAsync(DefectRecordDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.WorkOrderNo)) throw new InvalidOperationException("ME-MSG-020");
        if (string.IsNullOrWhiteSpace(dto.CategoryCd)) throw new InvalidOperationException("ME-MSG-030");
        if (string.IsNullOrWhiteSpace(dto.DefectDescription)) throw new InvalidOperationException("ME-MSG-031");

        var no = await _seq.NextAsync(DfSeqKey);
        var now = DateTime.Now;

        _db.DefectRecords.Add(new DefectRecord
        {
            DefectNo = no,
            WorkOrderNo = dto.WorkOrderNo,
            ProcessCd = dto.ProcessCd,
            InspectionNo = dto.InspectionNo,
            OccurDate = dto.OccurDate == default ? DateTime.Today : dto.OccurDate,
            ReporterCd = dto.ReporterCd,
            CategoryCd = dto.CategoryCd,
            DetailCd = dto.DetailCd,
            DefectQty = dto.DefectQty,
            DefectDescription = dto.DefectDescription,
            CauseAnalysis = dto.CauseAnalysis,
            CorrectiveAction = dto.CorrectiveAction,
            AssigneeCd = dto.AssigneeCd,
            DueDate = dto.DueDate,
            CompletedDate = dto.CompletedDate,
            Status = dto.Status,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = now,
        });
        await _db.SaveChangesAsync();

        try { await _notifier.NotifyDefectIssuedAsync(no, dto.WorkOrderNo, dto.CategoryCd); } catch { }

        return no;
    }

    public async Task UpdateAsync(string defectNo, DefectRecordDto dto, string? userName)
    {
        var x = await _db.DefectRecords.FirstOrDefaultAsync(d => d.DefectNo == defectNo && !d.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        if (string.IsNullOrWhiteSpace(dto.CategoryCd)) throw new InvalidOperationException("ME-MSG-030");
        if (string.IsNullOrWhiteSpace(dto.DefectDescription)) throw new InvalidOperationException("ME-MSG-031");

        x.WorkOrderNo = dto.WorkOrderNo;
        x.ProcessCd = dto.ProcessCd;
        x.InspectionNo = dto.InspectionNo;
        x.OccurDate = dto.OccurDate;
        x.ReporterCd = dto.ReporterCd;
        x.CategoryCd = dto.CategoryCd;
        x.DetailCd = dto.DetailCd;
        x.DefectQty = dto.DefectQty;
        x.DefectDescription = dto.DefectDescription;
        x.CauseAnalysis = dto.CauseAnalysis;
        x.CorrectiveAction = dto.CorrectiveAction;
        x.AssigneeCd = dto.AssigneeCd;
        x.DueDate = dto.DueDate;
        x.CompletedDate = dto.CompletedDate;
        x.Status = dto.Status;
        // 完了に変更時、完了日が未指定なら自動セット
        if (dto.Status == 3 && !x.CompletedDate.HasValue) x.CompletedDate = DateTime.Now;
        x.Remarks = dto.Remarks;
        x.Modifier = userName;
        x.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string defectNo, string? userName)
    {
        var x = await _db.DefectRecords.FirstOrDefaultAsync(d => d.DefectNo == defectNo && !d.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");
        x.IsDeleted = true;
        x.Modifier = userName;
        x.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<DefectCategoryDto>> GetAllCategoriesAsync()
    {
        return await _db.DefectCategories.AsNoTracking()
            .Where(x => x.ActiveFlg && !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.CategoryCd).ThenBy(x => x.DetailCd)
            .Select(x => new DefectCategoryDto
            {
                Id = x.Id,
                CategoryCd = x.CategoryCd,
                DetailCd = x.DetailCd,
                CategoryName = x.CategoryName,
                DetailName = x.DetailName,
                SortOrder = x.SortOrder,
                ActiveFlg = x.ActiveFlg,
            })
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper
    // ═══════════════════════════════════════════════════════════

    private async Task EnrichAsync(IEnumerable<DefectRecordDto> dtos)
    {
        var list = dtos.ToList();
        if (list.Count == 0) return;

        // 製品名・工程名
        var woNos = list.Select(d => d.WorkOrderNo).Distinct().ToList();
        var woMap = await _db.WorkOrders.AsNoTracking()
            .Where(w => woNos.Contains(w.WorkOrderNo))
            .Select(w => new { w.WorkOrderNo, w.ProductCd, w.ProductName })
            .ToDictionaryAsync(w => w.WorkOrderNo);

        // 不良分類名（category + detail）
        var catKeys = list.Select(d => new { d.CategoryCd, d.DetailCd }).Distinct().ToList();
        var allCats = await _db.DefectCategories.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync();
        var catMap = allCats.GroupBy(c => c.CategoryCd).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var dto in list)
        {
            if (woMap.TryGetValue(dto.WorkOrderNo, out var w))
            {
                dto.ProductCd = w.ProductCd;
                dto.ProductName = w.ProductName;
            }
            if (catMap.TryGetValue(dto.CategoryCd, out var cats))
            {
                dto.CategoryName = cats.FirstOrDefault()?.CategoryName;
                if (!string.IsNullOrEmpty(dto.DetailCd))
                    dto.DetailName = cats.FirstOrDefault(c => c.DetailCd == dto.DetailCd)?.DetailName;
            }
        }
    }

    private static DefectRecordDto ToDto(DefectRecord x) => new()
    {
        Id = x.Id,
        DefectNo = x.DefectNo,
        WorkOrderNo = x.WorkOrderNo,
        ProcessCd = x.ProcessCd,
        InspectionNo = x.InspectionNo,
        OccurDate = x.OccurDate,
        ReporterCd = x.ReporterCd,
        CategoryCd = x.CategoryCd,
        DetailCd = x.DetailCd,
        DefectQty = x.DefectQty,
        DefectDescription = x.DefectDescription,
        CauseAnalysis = x.CauseAnalysis,
        CorrectiveAction = x.CorrectiveAction,
        AssigneeCd = x.AssigneeCd,
        DueDate = x.DueDate,
        CompletedDate = x.CompletedDate,
        Status = x.Status,
        Remarks = x.Remarks,
        CreateDate = x.CreateDate,
    };
}
