using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME020 / 030 — 製造指図 業務サービス実装
/// </summary>
public class WorkOrderService : IWorkOrderService
{
    private readonly CP6Context _db;
    private readonly IMesSequenceService _seq;
    private const string WorkOrderSeqKey = "WO";

    public WorkOrderService(CP6Context db, IMesSequenceService seq)
    {
        _db = db;
        _seq = seq;
    }

    // ═══════════════════════════════════════════════════════════
    //  Read
    // ═══════════════════════════════════════════════════════════

    public async Task<WorkOrderDto?> GetByNoAsync(string workOrderNo)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted);
        if (wo == null) return null;

        var procs = await _db.WorkOrderProcesses.AsNoTracking()
            .Where(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var mats = await _db.WorkOrderMaterials.AsNoTracking()
            .Where(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var dto = ToDto(wo);
        dto.Processes = procs.Select(ToDto).ToList();
        dto.Materials = mats.Select(ToDto).ToList();
        dto.ProcessCount = procs.Count;
        dto.CompletedProcessCount = procs.Count(p => p.ProcessStatus == 2);
        dto.ProgressRate = wo.ProductionQty > 0
            ? Math.Round(wo.CompletedQty / wo.ProductionQty * 100m, 2)
            : 0m;
        dto.DelayDays = CalcDelayDays(wo);

        return dto;
    }

    public async Task<PagedResultDto<WorkOrderDto>> SearchAsync(WorkOrderSearchQuery q)
    {
        var query = _db.WorkOrders.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.BaseCd)) query = query.Where(x => x.BaseCd == q.BaseCd);
        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo.Contains(q.WorkOrderNo));
        if (!string.IsNullOrWhiteSpace(q.OrderNo))
        {
            var on = q.OrderNo;
            query = query.Where(x => x.OrderNo1 == on || x.OrderNo2 == on || x.OrderNo3 == on || x.WebOrderNo == on);
        }
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (q.DeliveryDateFrom.HasValue) query = query.Where(x => x.DeliveryDate >= q.DeliveryDateFrom);
        if (q.DeliveryDateTo.HasValue) query = query.Where(x => x.DeliveryDate <= q.DeliveryDateTo);
        if (q.PlanStartDateFrom.HasValue) query = query.Where(x => x.PlanStartDate >= q.PlanStartDateFrom);
        if (q.PlanStartDateTo.HasValue) query = query.Where(x => x.PlanStartDate <= q.PlanStartDateTo);
        if (q.Statuses?.Any() == true) query = query.Where(x => q.Statuses.Contains(x.Status));
        if (q.Priority.HasValue) query = query.Where(x => x.Priority == q.Priority);

        if (q.DelayedOnly)
        {
            var today = DateTime.Today;
            query = query.Where(x => x.PlanEndDate < today && x.Status != 4 && x.Status != 6 && x.Status != 9);
        }

        // 工程CD/WG で絞込（子表 join）
        if (!string.IsNullOrWhiteSpace(q.ProcessCd) || !string.IsNullOrWhiteSpace(q.WgCd))
        {
            var subQ = _db.WorkOrderProcesses.AsNoTracking().Where(p => !p.IsDeleted);
            if (!string.IsNullOrWhiteSpace(q.ProcessCd)) subQ = subQ.Where(p => p.ProcessCd == q.ProcessCd);
            if (!string.IsNullOrWhiteSpace(q.WgCd)) subQ = subQ.Where(p => p.WgCd == q.WgCd);
            var nos = subQ.Select(p => p.WorkOrderNo).Distinct();
            query = query.Where(x => nos.Contains(x.WorkOrderNo));
        }

        var total = await query.CountAsync();

        var page = q.PageIndex <= 0 ? 1 : q.PageIndex;
        var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 500);

        var items = await query
            .OrderByDescending(x => x.CreateDate)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();

        // 进度统计（批量）
        var nos2 = items.Select(x => x.WorkOrderNo).ToList();
        var procCnt = await _db.WorkOrderProcesses.AsNoTracking()
            .Where(p => nos2.Contains(p.WorkOrderNo) && !p.IsDeleted)
            .GroupBy(p => p.WorkOrderNo)
            .Select(g => new
            {
                WorkOrderNo = g.Key,
                Total = g.Count(),
                Completed = g.Count(x => x.ProcessStatus == 2),
            })
            .ToDictionaryAsync(x => x.WorkOrderNo);

        var today2 = DateTime.Today;
        var dtos = items.Select(wo =>
        {
            var dto = ToDto(wo);
            if (procCnt.TryGetValue(wo.WorkOrderNo, out var c))
            {
                dto.ProcessCount = c.Total;
                dto.CompletedProcessCount = c.Completed;
            }
            dto.ProgressRate = wo.ProductionQty > 0
                ? Math.Round(wo.CompletedQty / wo.ProductionQty * 100m, 2)
                : 0m;
            dto.DelayDays = CalcDelayDays(wo);
            return dto;
        }).ToList();

        return new PagedResultDto<WorkOrderDto>
        {
            Total = total,
            PageIndex = page,
            PageSize = size,
            Items = dtos,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Create / Update / Delete
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CreateAsync(WorkOrderDto dto, string? userName)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        var no = await _seq.NextAsync(WorkOrderSeqKey);
        var now = DateTime.Now;

        var wo = new WorkOrder
        {
            WorkOrderNo = no,
            Status = dto.Status >= 0 ? dto.Status : 0,
            OrderNo1 = dto.OrderNo1,
            OrderNo2 = dto.OrderNo2,
            OrderNo3 = dto.OrderNo3,
            WebOrderNo = dto.WebOrderNo,
            CustomerCd = dto.CustomerCd,
            ProductCd = dto.ProductCd,
            ProductName = dto.ProductName,
            ProductionQty = dto.ProductionQty,
            DeliveryDate = dto.DeliveryDate,
            PlanStartDate = dto.PlanStartDate,
            PlanEndDate = dto.PlanEndDate,
            Priority = dto.Priority <= 0 ? 1 : dto.Priority,
            LotNo = dto.LotNo,
            BaseCd = dto.BaseCd,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = now,
        };
        _db.WorkOrders.Add(wo);

        foreach (var p in dto.Processes ?? new())
        {
            _db.WorkOrderProcesses.Add(new WorkOrderProcess
            {
                WorkOrderNo = no,
                ProcessCd = p.ProcessCd,
                TaskCd = p.TaskCd,
                ProcessName = p.ProcessName,
                SortOrder = p.SortOrder,
                ProcessStatus = 0,
                MachineCd = p.MachineCd,
                WgCd = p.WgCd,
                PlanStartTime = p.PlanStartTime,
                PlanEndTime = p.PlanEndTime,
                PlanQty = p.PlanQty ?? dto.ProductionQty,
                StdLossRate = p.StdLossRate,
                LeadTime = p.LeadTime,
                PrevProcessCd = p.PrevProcessCd,
                Remarks = p.Remarks,
                Creator = userName,
                CreateDate = now,
            });
        }

        foreach (var m in dto.Materials ?? new())
        {
            _db.WorkOrderMaterials.Add(new WorkOrderMaterial
            {
                WorkOrderNo = no,
                ProcessCd = m.ProcessCd,
                MaterialCd = m.MaterialCd,
                MaterialName = m.MaterialName,
                MaterialTypeDiv = m.MaterialTypeDiv,
                PlanQty = m.PlanQty,
                Unit = m.Unit,
                SupplyStatus = m.SupplyStatus,
                SortOrder = m.SortOrder,
                Remarks = m.Remarks,
                Creator = userName,
                CreateDate = now,
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return no;
    }

    public async Task UpdateAsync(string workOrderNo, WorkOrderDto dto, string? userName)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        if (wo.Status != 0 && wo.Status != 1)
            throw new InvalidOperationException("ME-MSG-042"); // このステータスでは操作できません

        await using var tx = await _db.Database.BeginTransactionAsync();
        var now = DateTime.Now;

        wo.OrderNo1 = dto.OrderNo1;
        wo.OrderNo2 = dto.OrderNo2;
        wo.OrderNo3 = dto.OrderNo3;
        wo.WebOrderNo = dto.WebOrderNo;
        wo.CustomerCd = dto.CustomerCd;
        wo.ProductCd = dto.ProductCd;
        wo.ProductName = dto.ProductName;
        wo.ProductionQty = dto.ProductionQty;
        wo.DeliveryDate = dto.DeliveryDate;
        wo.PlanStartDate = dto.PlanStartDate;
        wo.PlanEndDate = dto.PlanEndDate;
        wo.Priority = dto.Priority <= 0 ? 1 : dto.Priority;
        wo.LotNo = dto.LotNo;
        wo.BaseCd = dto.BaseCd;
        wo.Remarks = dto.Remarks;
        wo.Status = dto.Status >= 0 ? dto.Status : wo.Status;
        wo.Modifier = userName;
        wo.ModifyDate = now;

        // 子表：全削除→全挿入 方式
        var oldProcs = await _db.WorkOrderProcesses.Where(x => x.WorkOrderNo == workOrderNo).ToListAsync();
        _db.WorkOrderProcesses.RemoveRange(oldProcs);
        var oldMats = await _db.WorkOrderMaterials.Where(x => x.WorkOrderNo == workOrderNo).ToListAsync();
        _db.WorkOrderMaterials.RemoveRange(oldMats);

        foreach (var p in dto.Processes ?? new())
        {
            _db.WorkOrderProcesses.Add(new WorkOrderProcess
            {
                WorkOrderNo = workOrderNo,
                ProcessCd = p.ProcessCd,
                TaskCd = p.TaskCd,
                ProcessName = p.ProcessName,
                SortOrder = p.SortOrder,
                ProcessStatus = p.ProcessStatus,
                MachineCd = p.MachineCd,
                WgCd = p.WgCd,
                PlanStartTime = p.PlanStartTime,
                PlanEndTime = p.PlanEndTime,
                PlanQty = p.PlanQty,
                StdLossRate = p.StdLossRate,
                LeadTime = p.LeadTime,
                PrevProcessCd = p.PrevProcessCd,
                Remarks = p.Remarks,
                Creator = userName,
                CreateDate = now,
            });
        }
        foreach (var m in dto.Materials ?? new())
        {
            _db.WorkOrderMaterials.Add(new WorkOrderMaterial
            {
                WorkOrderNo = workOrderNo,
                ProcessCd = m.ProcessCd,
                MaterialCd = m.MaterialCd,
                MaterialName = m.MaterialName,
                MaterialTypeDiv = m.MaterialTypeDiv,
                PlanQty = m.PlanQty,
                Unit = m.Unit,
                SupplyStatus = m.SupplyStatus,
                SortOrder = m.SortOrder,
                Remarks = m.Remarks,
                Creator = userName,
                CreateDate = now,
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(string workOrderNo, byte[]? rowVersion, string? userName)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        if (wo.Status != 0 && wo.Status != 1)
            throw new InvalidOperationException("ME-MSG-042"); // 発行済み以降は削除不可

        wo.IsDeleted = true;
        wo.Modifier = userName;
        wo.ModifyDate = DateTime.Now;
        if (rowVersion != null) wo.RowVersion = rowVersion;

        await _db.SaveChangesAsync();
    }

    public async Task IssueAsync(string workOrderNo, string? userName)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        if (wo.Status >= 2) return; // already issued
        if (wo.Status != 0 && wo.Status != 1)
            throw new InvalidOperationException("ME-MSG-042");

        // 発行前チェック：工程が1件以上ある事
        var procCount = await _db.WorkOrderProcesses.CountAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted);
        if (procCount == 0) throw new InvalidOperationException("ME-MSG-006"); // 登録する工程がありません

        wo.Status = 2;
        wo.Modifier = userName;
        wo.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public Task<string> NextSequenceAsync() => _seq.NextAsync(WorkOrderSeqKey);

    // ═══════════════════════════════════════════════════════════
    //  ME020 — 受注 → 指図 自動展開
    // ═══════════════════════════════════════════════════════════

    public async Task<List<string>> ExpandFromOrderAsync(ExpandFromOrderRequest req, string? userName)
    {
        if (string.IsNullOrWhiteSpace(req.WebOrderNo))
            throw new InvalidOperationException("ME-MSG-001"); // 手配NOが未入力です

        var order = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WebOrderNo == req.WebOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-040"); // 検索結果がありません

        var detailQ = _db.OrderDetails.AsNoTracking().Where(x => x.WebOrderNo == req.WebOrderNo && !x.IsDeleted);
        if (req.WebOrderDetailNos?.Any() == true)
            detailQ = detailQ.Where(d => req.WebOrderDetailNos.Contains(d.WebOrderDetailNo));
        var details = await detailQ.ToListAsync();
        if (details.Count == 0) throw new InvalidOperationException("ME-MSG-040");

        var createdNos = new List<string>();

        await using var tx = await _db.Database.BeginTransactionAsync();
        var now = DateTime.Now;

        foreach (var d in details)
        {
            // 同一手配NOの指図 重複チェック
            var dup = await _db.WorkOrders.AnyAsync(w =>
                w.WebOrderNo == req.WebOrderNo
                && w.ProductCd == d.ProductCd
                && !w.IsDeleted
                && w.Status != 9);
            if (dup) throw new InvalidOperationException("ME-MSG-005"); // 該当手配NOは既に指図が作成されています

            var no = await _seq.NextAsync(WorkOrderSeqKey);

            // 製品マスタの工程・材料を展開
            var prodProcs = await _db.ProductProcesses.AsNoTracking()
                .Where(p => p.ProductCd == d.ProductCd && !p.IsDeleted)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();

            var prodMats = await _db.ProductMaterials.AsNoTracking()
                .Where(m => m.ProductCd == d.ProductCd && !m.IsDeleted)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var qty = d.Quantity ?? 0m;

            var wo = new WorkOrder
            {
                WorkOrderNo = no,
                Status = 1, // 確定済（受注展開直後）
                OrderNo1 = d.HaibaiNo1,
                OrderNo2 = d.HaibaiNo2,
                OrderNo3 = d.HaibaiNo3,
                WebOrderNo = req.WebOrderNo,
                CustomerCd = order.CustomerCd,
                ProductCd = d.ProductCd ?? string.Empty,
                ProductName = d.CpItemName1 ?? d.CustomerItemName1,
                ProductionQty = qty,
                DeliveryDate = d.CustomerDeliveryDate ?? order.CustomerDeliveryDate,
                PlanEndDate = (d.CustomerDeliveryDate ?? order.CustomerDeliveryDate)?.AddDays(-1),
                PlanStartDate = (d.CustomerDeliveryDate ?? order.CustomerDeliveryDate)?.AddDays(-7),
                Priority = req.Priority,
                LotNo = null,
                BaseCd = req.BaseCd,
                Creator = userName,
                CreateDate = now,
            };
            _db.WorkOrders.Add(wo);

            foreach (var pp in prodProcs)
            {
                _db.WorkOrderProcesses.Add(new WorkOrderProcess
                {
                    WorkOrderNo = no,
                    ProcessCd = pp.ProcessCd,
                    TaskCd = pp.TaskCd,
                    ProcessName = pp.Spec01,
                    SortOrder = pp.SortOrder,
                    ProcessStatus = 0,
                    MachineCd = pp.MachineOrVendor,
                    WgCd = pp.WgCd,
                    PlanQty = qty,
                    StdLossRate = pp.LossRate,
                    LeadTime = pp.LeadTime,
                    Creator = userName,
                    CreateDate = now,
                });
            }

            foreach (var pm in prodMats)
            {
                _db.WorkOrderMaterials.Add(new WorkOrderMaterial
                {
                    WorkOrderNo = no,
                    ProcessCd = pm.ProcessCd,
                    MaterialCd = pm.MaterialCd,
                    MaterialName = pm.ItemCd,
                    MaterialTypeDiv = pm.MaterialTypeDiv,
                    PlanQty = qty,
                    SupplyStatus = 0,
                    SortOrder = pm.SortOrder,
                    Creator = userName,
                    CreateDate = now,
                });
            }

            createdNos.Add(no);
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return createdNos;
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper
    // ═══════════════════════════════════════════════════════════

    private static int CalcDelayDays(WorkOrder wo)
    {
        if (wo.PlanEndDate == null) return 0;
        if (wo.Status == 4 || wo.Status == 6 || wo.Status == 9) return 0;
        var diff = (DateTime.Today - wo.PlanEndDate.Value.Date).Days;
        return diff > 0 ? diff : 0;
    }

    private static WorkOrderDto ToDto(WorkOrder x) => new()
    {
        Id = x.Id,
        WorkOrderNo = x.WorkOrderNo,
        Status = x.Status,
        OrderNo1 = x.OrderNo1,
        OrderNo2 = x.OrderNo2,
        OrderNo3 = x.OrderNo3,
        WebOrderNo = x.WebOrderNo,
        CustomerCd = x.CustomerCd,
        ProductCd = x.ProductCd,
        ProductName = x.ProductName,
        ProductionQty = x.ProductionQty,
        CompletedQty = x.CompletedQty,
        DefectQty = x.DefectQty,
        DeliveryDate = x.DeliveryDate,
        PlanStartDate = x.PlanStartDate,
        PlanEndDate = x.PlanEndDate,
        ActualStartDate = x.ActualStartDate,
        ActualEndDate = x.ActualEndDate,
        Priority = x.Priority,
        LotNo = x.LotNo,
        BaseCd = x.BaseCd,
        Remarks = x.Remarks,
        CreateDate = x.CreateDate,
    };

    private static WorkOrderProcessDto ToDto(WorkOrderProcess x) => new()
    {
        Id = x.Id,
        WorkOrderNo = x.WorkOrderNo,
        ProcessCd = x.ProcessCd,
        TaskCd = x.TaskCd,
        ProcessName = x.ProcessName,
        SortOrder = x.SortOrder,
        ProcessStatus = x.ProcessStatus,
        MachineCd = x.MachineCd,
        WgCd = x.WgCd,
        PlanStartTime = x.PlanStartTime,
        PlanEndTime = x.PlanEndTime,
        ActualStartTime = x.ActualStartTime,
        ActualEndTime = x.ActualEndTime,
        PlanQty = x.PlanQty,
        GoodQty = x.GoodQty,
        DefectQty = x.DefectQty,
        StdLossRate = x.StdLossRate,
        LeadTime = x.LeadTime,
        PrevProcessCd = x.PrevProcessCd,
        Remarks = x.Remarks,
    };

    private static WorkOrderMaterialDto ToDto(WorkOrderMaterial x) => new()
    {
        Id = x.Id,
        WorkOrderNo = x.WorkOrderNo,
        ProcessCd = x.ProcessCd,
        MaterialCd = x.MaterialCd,
        MaterialName = x.MaterialName,
        MaterialTypeDiv = x.MaterialTypeDiv,
        PlanQty = x.PlanQty,
        ActualQty = x.ActualQty,
        Unit = x.Unit,
        SupplyStatus = x.SupplyStatus,
        SortOrder = x.SortOrder,
        Remarks = x.Remarks,
    };
}
