using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME040 / 050 — 製造実績 業務サービス実装
/// </summary>
/// <remarks>
/// 実績種別：1=開始 2=中断 3=中断解除 4=完了 5=数量報告
/// </remarks>
public class ProductionResultService : IProductionResultService
{
    private readonly CP6Context _db;
    private readonly IMesSequenceService _seq;
    private readonly IWorkOrderService _woService;
    private readonly IMesNotifier _notifier;
    private readonly IWmsBridgeHook _wmsBridge;
    private const string ResultSeqKey = "PR";

    public ProductionResultService(CP6Context db, IMesSequenceService seq, IWorkOrderService woService, IMesNotifier notifier, IWmsBridgeHook wmsBridge)
    {
        _db = db;
        _seq = seq;
        _woService = woService;
        _notifier = notifier;
        _wmsBridge = wmsBridge;
    }

    // ═══════════════════════════════════════════════════════════
    //  ME050 — 実績一覧
    // ═══════════════════════════════════════════════════════════

    public async Task<PagedResultDto<ProductionResultDto>> SearchAsync(ProductionResultSearchQuery q)
    {
        var query = _db.ProductionResults.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo == q.WorkOrderNo);
        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) query = query.Where(x => x.ProcessCd == q.ProcessCd);
        if (!string.IsNullOrWhiteSpace(q.OperatorCd)) query = query.Where(x => x.OperatorCd == q.OperatorCd);
        if (q.DateFrom.HasValue) query = query.Where(x => x.ActualStartTime >= q.DateFrom || x.CreateDate >= q.DateFrom);
        if (q.DateTo.HasValue) query = query.Where(x => x.ActualEndTime <= q.DateTo || x.CreateDate <= q.DateTo);
        if (q.ResultType.HasValue) query = query.Where(x => x.ResultType == q.ResultType);

        var total = await query.CountAsync();
        var page = q.PageIndex <= 0 ? 1 : q.PageIndex;
        var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 500);

        var items = await query
            .OrderByDescending(x => x.CreateDate)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();

        // 関連 製品名 / 工程名 を補完
        var nos = items.Select(x => x.WorkOrderNo).Distinct().ToList();
        var woMap = await _db.WorkOrders.AsNoTracking()
            .Where(w => nos.Contains(w.WorkOrderNo))
            .ToDictionaryAsync(w => w.WorkOrderNo, w => w.ProductName);

        var procKeys = items.Select(x => new { x.WorkOrderNo, x.ProcessCd }).Distinct().ToList();
        var procNameMap = new Dictionary<(string, string), string?>();
        foreach (var k in procKeys)
        {
            var name = await _db.WorkOrderProcesses.AsNoTracking()
                .Where(p => p.WorkOrderNo == k.WorkOrderNo && p.ProcessCd == k.ProcessCd)
                .Select(p => p.ProcessName)
                .FirstOrDefaultAsync();
            procNameMap[(k.WorkOrderNo, k.ProcessCd)] = name;
        }

        var dtos = items.Select(x =>
        {
            var dto = ToDto(x);
            dto.ProductName = woMap.GetValueOrDefault(x.WorkOrderNo);
            dto.ProcessName = procNameMap.GetValueOrDefault((x.WorkOrderNo, x.ProcessCd));
            return dto;
        }).ToList();

        return new PagedResultDto<ProductionResultDto>
        {
            Total = total,
            PageIndex = page,
            PageSize = size,
            Items = dtos,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  ME040 — 開始 / 中断 / 解除 / 完了 / 数量
    // ═══════════════════════════════════════════════════════════

    public Task<string> StartAsync(ProductionResultRequest req, string? userName) =>
        WriteAsync(req, userName, 1);

    public Task<string> SuspendAsync(ProductionResultRequest req, string? userName) =>
        WriteAsync(req, userName, 2);

    public Task<string> ResumeAsync(ProductionResultRequest req, string? userName) =>
        WriteAsync(req, userName, 3);

    public Task<string> CompleteAsync(ProductionResultRequest req, string? userName) =>
        WriteAsync(req, userName, 4);

    public Task<string> ReportAsync(ProductionResultRequest req, string? userName) =>
        WriteAsync(req, userName, 5);

    private async Task<string> WriteAsync(ProductionResultRequest req, string? userName, int resultType)
    {
        if (string.IsNullOrWhiteSpace(req.WorkOrderNo)) throw new InvalidOperationException("ME-MSG-001");
        if (string.IsNullOrWhiteSpace(req.OperatorCd)) throw new InvalidOperationException("ME-MSG-011");

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderNo == req.WorkOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-040");

        // 発行済以降のみ実績入力可
        if (wo.Status < 2) throw new InvalidOperationException("ME-MSG-042");

        var proc = await _db.WorkOrderProcesses.FirstOrDefaultAsync(p =>
            p.WorkOrderNo == req.WorkOrderNo && p.ProcessCd == req.ProcessCd && !p.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-040");

        var now = DateTime.Now;
        var justCompleted = false; // 本実績で全工程完了に遷移したか（WMS完成品入庫トリガ）

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 各種ルール
        switch (resultType)
        {
            case 1: // 開始
                if (proc.ProcessStatus != 0)
                    throw new InvalidOperationException("ME-MSG-042");
                // 前工程未完了 警告（実装は警告のみ。クライアント側で確認後送信）
                proc.ProcessStatus = 1;
                proc.ActualStartTime = now;
                wo.Status = 3; // 着手中
                if (wo.ActualStartDate == null) wo.ActualStartDate = now;
                break;
            case 2: // 中断
                if (proc.ProcessStatus != 1)
                    throw new InvalidOperationException("ME-MSG-042");
                if (string.IsNullOrWhiteSpace(req.SuspendReasonCd))
                    throw new InvalidOperationException("ME-MSG-024"); // 判定理由を入力してください（流用）
                proc.ProcessStatus = 3;
                wo.Status = 5; // 中断中
                break;
            case 3: // 中断解除
                if (proc.ProcessStatus != 3)
                    throw new InvalidOperationException("ME-MSG-042");
                proc.ProcessStatus = 1;
                wo.Status = 3; // 着手中
                break;
            case 4: // 完了
                if (proc.ProcessStatus != 1)
                    throw new InvalidOperationException("ME-MSG-042");
                if (req.GoodQty <= 0)
                    throw new InvalidOperationException("ME-MSG-012");
                if (req.DefectQty > 0 && string.IsNullOrWhiteSpace(req.DefectReasonCd))
                    throw new InvalidOperationException("ME-MSG-014");
                if (req.ActualEndTime.HasValue && proc.ActualStartTime.HasValue && req.ActualEndTime < proc.ActualStartTime)
                    throw new InvalidOperationException("ME-MSG-016");
                proc.ProcessStatus = 2;
                proc.ActualEndTime = req.ActualEndTime ?? now;
                proc.GoodQty += req.GoodQty;
                proc.DefectQty += req.DefectQty;

                // 全工程完了？
                var procs = await _db.WorkOrderProcesses
                    .Where(p => p.WorkOrderNo == req.WorkOrderNo && !p.IsDeleted)
                    .ToListAsync();
                var allDone = procs.All(p =>
                    (p.Id == proc.Id) ? true : p.ProcessStatus == 2);
                if (allDone)
                {
                    wo.Status = 4;
                    wo.ActualEndDate = now;
                    justCompleted = true;
                }
                break;
            case 5: // 数量報告
                if (req.GoodQty <= 0 && req.DefectQty <= 0)
                    throw new InvalidOperationException("ME-MSG-012");
                proc.GoodQty += req.GoodQty;
                proc.DefectQty += req.DefectQty;
                break;
        }

        // 指図ヘッダ累計
        if (resultType == 4 || resultType == 5)
        {
            wo.CompletedQty += req.GoodQty;
            wo.DefectQty += req.DefectQty;
        }

        wo.Modifier = userName;
        wo.ModifyDate = now;
        proc.Modifier = userName;
        proc.ModifyDate = now;

        // 実績レコード作成
        var totalQty = req.GoodQty + req.DefectQty;
        var lossRate = totalQty > 0 ? Math.Round(req.DefectQty / totalQty * 100m, 4) : (decimal?)null;
        var resultNo = await _seq.NextAsync(ResultSeqKey);

        var pr = new ProductionResult
        {
            ResultNo = resultNo,
            WorkOrderNo = req.WorkOrderNo,
            ProcessCd = req.ProcessCd,
            TaskCd = req.TaskCd ?? proc.TaskCd,
            ResultType = resultType,
            OperatorCd = req.OperatorCd,
            OperatorName = req.OperatorName,
            ActualStartTime = resultType == 1 ? now : req.ActualStartTime,
            ActualEndTime = resultType == 4 ? (req.ActualEndTime ?? now) : req.ActualEndTime,
            GoodQty = req.GoodQty,
            DefectQty = req.DefectQty,
            ActualLossRate = lossRate,
            DefectReasonCd = req.DefectReasonCd,
            SuspendReasonCd = req.SuspendReasonCd,
            MachineCd = req.MachineCd ?? proc.MachineCd,
            ResultNote = req.ResultNote,
            Creator = userName,
            CreateDate = now,
        };
        _db.ProductionResults.Add(pr);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // SignalR 実時間推送（コミット後）
        try
        {
            await _notifier.NotifyProductionReportedAsync(req.WorkOrderNo, req.ProcessCd, req.GoodQty, req.DefectQty);
            // 指図ステータス遷移の場合も別途通知
            if (resultType == 1 || resultType == 2 || resultType == 3 || resultType == 4)
                await _notifier.NotifyWorkOrderStatusChangedAsync(req.WorkOrderNo, wo.Status);
        }
        catch { /* notifier 失敗は業務に影響させない */ }

        // ERP→MES→WMS 接缝：全工程完了時、完成品（累計良品数）を WMS へ自動入庫（best-effort）
        if (justCompleted)
            await _wmsBridge.OnProductionCompletedAsync(req.WorkOrderNo, wo.CompletedQty, userName);

        return resultNo;
    }

    public Task<WorkOrderDto?> GetWorkOrderSummaryAsync(string workOrderNo)
        => _woService.GetByNoAsync(workOrderNo);

    // ═══════════════════════════════════════════════════════════
    //  Helper
    // ═══════════════════════════════════════════════════════════

    private static ProductionResultDto ToDto(ProductionResult x) => new()
    {
        Id = x.Id,
        ResultNo = x.ResultNo,
        WorkOrderNo = x.WorkOrderNo,
        ProcessCd = x.ProcessCd,
        TaskCd = x.TaskCd,
        ResultType = x.ResultType,
        OperatorCd = x.OperatorCd,
        OperatorName = x.OperatorName,
        ActualStartTime = x.ActualStartTime,
        ActualEndTime = x.ActualEndTime,
        GoodQty = x.GoodQty,
        DefectQty = x.DefectQty,
        ActualLossRate = x.ActualLossRate,
        DefectReasonCd = x.DefectReasonCd,
        SuspendReasonCd = x.SuspendReasonCd,
        MachineCd = x.MachineCd,
        ResultNote = x.ResultNote,
        CreateDate = x.CreateDate,
    };
}
