using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME060 / 070 — 品質検査 業務サービス実装
/// </summary>
/// <remarks>
/// 自動合否判定（仕様書 §6.3）：
///   IF 上限値 IS NOT NULL AND 下限値 IS NOT NULL THEN
///       IF 計測値 BETWEEN 下限値 AND 上限値 THEN 合格 ELSE 不合格
///   ELSE IF 規格値 IS NOT NULL THEN
///       IF 計測値 = 規格値 THEN 合格 ELSE 不合格
///   ELSE 手動判定
/// 総合：全項目合格→1=合格、1項目でも不合格→2=不合格、保留含→9=保留
/// </remarks>
public partial class QualityInspectionService : IQualityInspectionService
{
    private readonly CP6Context _db;
    private readonly IMesSequenceService _seq;
    private readonly IStockQcService? _stockQc;
    private readonly ILogger<QualityInspectionService> _logger;
    private readonly OperationsOwnerV1? _owner;
    private const string QcSeqKey = "QC";

    public QualityInspectionService(
        CP6Context db,
        IMesSequenceService seq,
        IStockQcService? stockQc = null,
        ILogger<QualityInspectionService>? logger = null, OperationsOwnerV1? owner=null)
    {
        _db = db;
        _owner = owner;
        _seq = seq;
        _stockQc = stockQc;
        _logger = logger ?? NullLogger<QualityInspectionService>.Instance;
    }

    private async Task<QualityInspectionDto?> GetByNoCoreAsync(string inspectionNo, IQueryable<QualityInspection>? allowed=null)
    {
        var qi = await (allowed ?? _db.QualityInspections.AsNoTracking())
            .FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted);
        if (qi == null) return null;

        var items = await _db.QualityInspectionItems.AsNoTracking()
            .Where(x => x.InspectionNo == inspectionNo && !x.IsDeleted)
            .OrderBy(x => x.ItemSeqNo)
            .ToListAsync();

        var dto = ToDto(qi);
        dto.Items = items.Select(ToDto).ToList();
        CalcStats(dto);

        // 関連製品名 / 工程名 を補完
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WorkOrderNo == qi.WorkOrderNo);
        if (wo != null)
        {
            dto.ProductCd = wo.ProductCd;
            dto.ProductName = wo.ProductName;
        }
        if (!string.IsNullOrEmpty(qi.ProcessCd))
        {
            dto.ProcessName = await _db.WorkOrderProcesses.AsNoTracking()
                .Where(p => p.WorkOrderNo == qi.WorkOrderNo && p.ProcessCd == qi.ProcessCd)
                .Select(p => p.ProcessName)
                .FirstOrDefaultAsync();
        }

        return dto;
    }

    private async Task<PagedResultDto<QualityInspectionDto>> SearchCoreAsync(QualityInspectionSearchQuery q, IQueryable<QualityInspection>? allowed=null)
    {
        var query = (allowed ?? _db.QualityInspections.AsNoTracking()).Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.InspectionNo)) query = query.Where(x => x.InspectionNo.Contains(q.InspectionNo));
        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo.Contains(q.WorkOrderNo));
        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) query = query.Where(x => x.ProcessCd == q.ProcessCd);
        if (!string.IsNullOrWhiteSpace(q.InspectorCd)) query = query.Where(x => x.InspectorCd == q.InspectorCd);
        if (q.InspectionType.HasValue) query = query.Where(x => x.InspectionType == q.InspectionType);
        if (q.OverallResult.HasValue) query = query.Where(x => x.OverallResult == q.OverallResult);
        if (q.DateFrom.HasValue) query = query.Where(x => x.InspectionDate >= q.DateFrom);
        if (q.DateTo.HasValue) query = query.Where(x => x.InspectionDate <= q.DateTo);

        var total = await query.CountAsync();
        var page = q.PageIndex <= 0 ? 1 : q.PageIndex;
        var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 500);

        var items = await query
            .OrderByDescending(x => x.CreateDate)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();

        var ids = items.Select(x => x.Id).ToArray();
        // Native database reference equality also applies when stored keys differ in spelling.
        var itemStat = await _db.QualityInspections.AsNoTracking().Where(q => ids.Contains(q.Id))
            .Select(q => new { q.Id,
                Total = _db.QualityInspectionItems.Count(i => i.InspectionNo == q.InspectionNo && !i.IsDeleted),
                Pass = _db.QualityInspectionItems.Count(i => i.InspectionNo == q.InspectionNo && !i.IsDeleted && i.Result == 1) })
            .ToDictionaryAsync(x => x.Id);
        var woMap = await (from inspection in _db.QualityInspections.AsNoTracking()
            join w in _db.WorkOrders.AsNoTracking() on inspection.WorkOrderNo equals w.WorkOrderNo
            where ids.Contains(inspection.Id)
            select new { inspection.Id, w.ProductCd, w.ProductName }).ToDictionaryAsync(x => x.Id);

        var dtos = items.Select(x =>
        {
            var dto = ToDto(x);
            if (woMap.TryGetValue(x.Id, out var w))
            {
                dto.ProductCd = w.ProductCd;
                dto.ProductName = w.ProductName;
            }
            if (itemStat.TryGetValue(x.Id, out var s))
            {
                dto.ItemCount = s.Total;
                dto.PassCount = s.Pass;
                dto.PassRate = s.Total > 0 ? Math.Round((decimal)s.Pass / s.Total * 100m, 2) : 0;
            }
            return dto;
        }).ToList();

        return new PagedResultDto<QualityInspectionDto>
        {
            Total = total, PageIndex = page, PageSize = size, Items = dtos,
        };
    }

    private async Task<string> CreateCoreAsync(QualityInspectionDto dto, string? userName, bool owned=false)
    {
        if (string.IsNullOrWhiteSpace(dto.WorkOrderNo)) throw new InvalidOperationException("ME-MSG-020");
        if (string.IsNullOrWhiteSpace(dto.InspectorCd)) throw new InvalidOperationException("ME-MSG-021");

        // 全項目 計測値入力チェック（警告レベル：1件でも未入力なら警告 — 但しサーバー側は許可）
        // 不合格時：処置内容必須
        AutoJudge(dto);
        if (dto.OverallResult == 2 && !dto.DispositionAction.HasValue)
            throw new InvalidOperationException("ME-MSG-023");

        await using var tx = owned ? null : await _db.Database.BeginTransactionAsync();
        var no = await _seq.NextAsync(QcSeqKey);
        var now = DateTime.Now;

        var qi = new QualityInspection
        {
            InspectionNo = no,
            WorkOrderNo = dto.WorkOrderNo,
            ProcessCd = dto.ProcessCd,
            InspectionDate = dto.InspectionDate == default ? DateTime.Today : dto.InspectionDate,
            InspectorCd = dto.InspectorCd,
            InspectorName = dto.InspectorName,
            InspectionType = dto.InspectionType <= 0 ? 3 : dto.InspectionType,
            TemplateCd = dto.TemplateCd,
            InspectionQty = dto.InspectionQty,
            SampleQty = dto.SampleQty,
            OverallResult = dto.OverallResult,
            DispositionAction = dto.DispositionAction,
            JudgmentReason = dto.JudgmentReason,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = now,
        };
        _db.QualityInspections.Add(qi);

        int seq = 1;
        foreach (var it in dto.Items ?? new())
        {
            _db.QualityInspectionItems.Add(new QualityInspectionItem
            {
                InspectionNo = no,
                ItemSeqNo = it.ItemSeqNo > 0 ? it.ItemSeqNo : seq,
                ItemName = it.ItemName,
                InspectionMethod = it.InspectionMethod,
                StandardValue = it.StandardValue,
                UpperLimit = it.UpperLimit,
                LowerLimit = it.LowerLimit,
                MeasuredValue = it.MeasuredValue,
                MeasuredText = it.MeasuredText,
                Result = it.Result,
                Unit = it.Unit,
                Remarks = it.Remarks,
                Creator = userName,
                CreateDate = now,
            });
            seq++;
        }

        // 不合格 → 自動で T_DefectRecord を起票（仕様書 §6.1）
        if (qi.OverallResult == 2)
        {
            var defNo = await _seq.NextAsync("DF");
            _db.DefectRecords.Add(new DefectRecord
            {
                DefectNo = defNo,
                WorkOrderNo = qi.WorkOrderNo,
                ProcessCd = qi.ProcessCd,
                InspectionNo = no,
                OccurDate = qi.InspectionDate,
                ReporterCd = qi.InspectorCd,
                CategoryCd = "D07", // 検査由来：その他（後で人手で調整）
                DetailCd = null,
                DefectQty = qi.InspectionQty ?? 0,
                DefectDescription = $"品質検査 {no} 不合格自動起票",
                Status = 0, // 起票済
                Creator = userName,
                CreateDate = now,
            });
        }

        // 工程ステータス確認：全工程完了済の場合、指図ステータス→6=検査済
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.WorkOrderNo == qi.WorkOrderNo && !w.IsDeleted);
        if (wo != null && wo.Status == 4 && qi.OverallResult == 1)
        {
            wo.Status = 6;
            wo.Modifier = userName;
            wo.ModifyDate = now;
        }

        await _db.SaveChangesAsync();
        if(tx!=null)await tx.CommitAsync();

        if (!owned && qi.OverallResult == 2 && !string.IsNullOrWhiteSpace(qi.WorkOrderNo))
        {
            await TryMarkLinkedStockFailedAsync(qi.WorkOrderNo, no, userName);
        }

        return no;
    }

    private async Task UpdateCoreAsync(string inspectionNo, QualityInspectionDto dto, string? userName, bool owned=false)
    {
        var qi = await _db.QualityInspections.FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        AutoJudge(dto);
        if (dto.OverallResult == 2 && !dto.DispositionAction.HasValue)
            throw new InvalidOperationException("ME-MSG-023");

        await using var tx = owned ? null : await _db.Database.BeginTransactionAsync();
        var now = DateTime.Now;

        qi.WorkOrderNo = dto.WorkOrderNo;
        qi.ProcessCd = dto.ProcessCd;
        qi.InspectionDate = dto.InspectionDate;
        qi.InspectorCd = dto.InspectorCd;
        qi.InspectorName = dto.InspectorName;
        qi.InspectionType = dto.InspectionType;
        qi.TemplateCd = dto.TemplateCd;
        qi.InspectionQty = dto.InspectionQty;
        qi.SampleQty = dto.SampleQty;
        qi.OverallResult = dto.OverallResult;
        qi.DispositionAction = dto.DispositionAction;
        qi.JudgmentReason = dto.JudgmentReason;
        qi.Remarks = dto.Remarks;
        qi.Modifier = userName;
        qi.ModifyDate = now;

        var oldItems = await _db.QualityInspectionItems.Where(x => x.InspectionNo == inspectionNo).ToListAsync();
        _db.QualityInspectionItems.RemoveRange(oldItems);

        int seq = 1;
        foreach (var it in dto.Items ?? new())
        {
            var originalItem = owned ? oldItems.FirstOrDefault(x => x.ItemSeqNo == (it.ItemSeqNo > 0 ? it.ItemSeqNo : seq)) : null;
            _db.QualityInspectionItems.Add(new QualityInspectionItem
            {
                InspectionNo = inspectionNo,
                ItemSeqNo = it.ItemSeqNo > 0 ? it.ItemSeqNo : seq,
                ItemName = it.ItemName,
                InspectionMethod = it.InspectionMethod,
                StandardValue = it.StandardValue,
                UpperLimit = it.UpperLimit,
                LowerLimit = it.LowerLimit,
                MeasuredValue = it.MeasuredValue,
                MeasuredText = it.MeasuredText,
                Result = it.Result,
                Unit = it.Unit,
                Remarks = it.Remarks,
                Creator = originalItem != null ? originalItem.Creator : userName,
                CreateDate = originalItem?.CreateDate ?? now,
            });
            seq++;
        }

        await _db.SaveChangesAsync();
        if(tx!=null)await tx.CommitAsync();
    }

    private async Task<List<InspectionTemplateDto>> GetTemplateCoreAsync(string templateCd)
    {
        var rows = await _db.InspectionTemplates.AsNoTracking()
            .Where(t => t.TemplateCd == templateCd && t.ActiveFlg && !t.IsDeleted)
            .OrderBy(t => t.ItemSeqNo)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    private async Task<List<string>> GetAllTemplateCodesCoreAsync()
    {
        return await _db.InspectionTemplates.AsNoTracking()
            .Where(t => t.ActiveFlg && !t.IsDeleted)
            .Select(t => t.TemplateCd)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper
    // ═══════════════════════════════════════════════════════════

    /// <summary>合否自動判定（仕様書 §6.3）</summary>
    private async Task TryMarkLinkedStockFailedAsync(string workOrderNo, string inspectionNo, string? userName)
    {
        if (_stockQc == null)
        {
            _logger.LogWarning(
                "[QualityInspection] Stock QC service not configured; skipped auto FAILED mark for WO {WorkOrderNo}, inspection {InspectionNo}",
                workOrderNo,
                inspectionNo);
            return;
        }

        try
        {
            var affected = await _stockQc.MarkLinkedStockByWorkOrderAsync(
                workOrderNo,
                StockQcStatus.Failed,
                $"QC NG: inspection {inspectionNo}",
                userName);

            _logger.LogInformation(
                "[QualityInspection] Auto-marked {Count} stock rows FAILED for WO {WorkOrderNo}, inspection {InspectionNo}",
                affected,
                workOrderNo,
                inspectionNo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[QualityInspection] Failed to auto-mark linked stock for WO {WorkOrderNo}, inspection {InspectionNo}; QI save remains committed",
                workOrderNo,
                inspectionNo);
        }
    }

    private static void AutoJudge(QualityInspectionDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0) return;

        foreach (var it in dto.Items)
        {
            // 既に手動で設定されている場合はそのまま
            if (it.Result.HasValue) continue;

            if (it.UpperLimit.HasValue && it.LowerLimit.HasValue)
            {
                if (it.MeasuredValue.HasValue)
                    it.Result = (it.MeasuredValue >= it.LowerLimit && it.MeasuredValue <= it.UpperLimit) ? 1 : 2;
            }
            else if (it.StandardValue.HasValue)
            {
                if (it.MeasuredValue.HasValue)
                    it.Result = (it.MeasuredValue == it.StandardValue) ? 1 : 2;
            }
            // else: 手動判定（残置）
        }

        // 総合判定：全1=合格、1件でも2=不合格、それ以外=保留 (overall=null→使用者明示時のみ)
        if (!dto.OverallResult.HasValue)
        {
            if (dto.Items.Any(i => i.Result == 2)) dto.OverallResult = 2;
            else if (dto.Items.All(i => i.Result == 1)) dto.OverallResult = 1;
            else dto.OverallResult = 9; // 保留
        }
    }

    private static void CalcStats(QualityInspectionDto dto)
    {
        dto.ItemCount = dto.Items?.Count ?? 0;
        dto.PassCount = dto.Items?.Count(i => i.Result == 1) ?? 0;
        dto.PassRate = dto.ItemCount > 0 ? Math.Round((decimal)dto.PassCount / dto.ItemCount * 100m, 2) : 0;
    }

    private static QualityInspectionDto ToDto(QualityInspection x) => new()
    {
        Id = x.Id,
        InspectionNo = x.InspectionNo,
        WorkOrderNo = x.WorkOrderNo,
        ProcessCd = x.ProcessCd,
        InspectionDate = x.InspectionDate,
        InspectorCd = x.InspectorCd,
        InspectorName = x.InspectorName,
        InspectionType = x.InspectionType,
        TemplateCd = x.TemplateCd,
        InspectionQty = x.InspectionQty,
        SampleQty = x.SampleQty,
        OverallResult = x.OverallResult,
        DispositionAction = x.DispositionAction,
        JudgmentReason = x.JudgmentReason,
        Remarks = x.Remarks,
        CreateDate = x.CreateDate,
    };

    private static QualityInspectionItemDto ToDto(QualityInspectionItem x) => new()
    {
        Id = x.Id,
        InspectionNo = x.InspectionNo,
        ItemSeqNo = x.ItemSeqNo,
        ItemName = x.ItemName,
        InspectionMethod = x.InspectionMethod,
        StandardValue = x.StandardValue,
        UpperLimit = x.UpperLimit,
        LowerLimit = x.LowerLimit,
        MeasuredValue = x.MeasuredValue,
        MeasuredText = x.MeasuredText,
        Result = x.Result,
        Unit = x.Unit,
        Remarks = x.Remarks,
    };

    private static InspectionTemplateDto ToDto(InspectionTemplate x) => new()
    {
        Id = x.Id,
        TemplateCd = x.TemplateCd,
        ItemSeqNo = x.ItemSeqNo,
        TemplateName = x.TemplateName,
        ItemName = x.ItemName,
        InspectionMethod = x.InspectionMethod,
        StandardValue = x.StandardValue,
        UpperLimit = x.UpperLimit,
        LowerLimit = x.LowerLimit,
        Unit = x.Unit,
        ProcessCd = x.ProcessCd,
        RequiredFlg = x.RequiredFlg,
        ActiveFlg = x.ActiveFlg,
    };
}
