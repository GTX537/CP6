using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>設備管理サービス実装</summary>
public class MachineService : IMachineService
{
    private readonly CP6Context _db;
    private readonly IMesSequenceService _seq;
    private readonly IMesNotifier _notifier;
    private const string DowntimeSeqKey = "DT";

    public MachineService(CP6Context db, IMesSequenceService seq, IMesNotifier notifier)
    {
        _db = db;
        _seq = seq;
        _notifier = notifier;
    }

    // ═══════════════════════════════════════════════════════════
    //  設備マスタ
    // ═══════════════════════════════════════════════════════════

    public async Task<List<MachineDto>> SearchAsync(MachineSearchQuery q)
    {
        var query = _db.Machines.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.MachineCd)) query = query.Where(x => x.MachineCd.Contains(q.MachineCd));
        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) query = query.Where(x => x.ProcessCd == q.ProcessCd);
        if (!string.IsNullOrWhiteSpace(q.WgCd)) query = query.Where(x => x.WgCd == q.WgCd);
        if (!string.IsNullOrWhiteSpace(q.BaseCd)) query = query.Where(x => x.BaseCd == q.BaseCd);
        if (q.Statuses?.Any() == true) query = query.Where(x => q.Statuses.Contains(x.Status));
        if (q.ActiveOnly == true) query = query.Where(x => x.ActiveFlg);

        var machines = await query.OrderBy(x => x.MachineCd).ToListAsync();

        // 現在稼働指図情報の補完
        var machineCds = machines.Select(m => m.MachineCd).ToList();
        var activeProcs = await _db.WorkOrderProcesses.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ProcessStatus == 1 && p.MachineCd != null && machineCds.Contains(p.MachineCd!))
            .Select(p => new { p.MachineCd, p.WorkOrderNo, p.ProcessCd })
            .ToListAsync();
        var activeMap = activeProcs.GroupBy(p => p.MachineCd!).ToDictionary(g => g.Key, g => g.First());

        // 本日 OEE 補完
        var today = DateTime.Today;
        var todayOee = await _db.OeeDailies.AsNoTracking()
            .Where(o => o.OeeDate == today && machineCds.Contains(o.MachineCd))
            .ToDictionaryAsync(o => o.MachineCd, o => o.Oee);

        return machines.Select(m =>
        {
            var dto = ToDto(m);
            if (activeMap.TryGetValue(m.MachineCd, out var p))
            {
                dto.CurrentWorkOrderNo = p.WorkOrderNo;
                dto.CurrentProcessCd = p.ProcessCd;
            }
            if (todayOee.TryGetValue(m.MachineCd, out var o)) dto.TodayOee = o;
            return dto;
        }).ToList();
    }

    public async Task<MachineDto?> GetByCdAsync(string machineCd)
    {
        var m = await _db.Machines.AsNoTracking().FirstOrDefaultAsync(x => x.MachineCd == machineCd && !x.IsDeleted);
        return m == null ? null : ToDto(m);
    }

    public async Task<string> CreateAsync(MachineDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.MachineCd)) throw new InvalidOperationException("ME-MSG-001");
        if (string.IsNullOrWhiteSpace(dto.MachineName)) throw new InvalidOperationException("ME-MSG-031");

        if (await _db.Machines.AnyAsync(x => x.MachineCd == dto.MachineCd && !x.IsDeleted))
            throw new InvalidOperationException("ME-MSG-005"); // 既に存在

        var now = DateTime.Now;
        _db.Machines.Add(new Machine
        {
            MachineCd = dto.MachineCd,
            MachineName = dto.MachineName,
            MachineType = dto.MachineType,
            ProcessCd = dto.ProcessCd,
            WgCd = dto.WgCd,
            BaseCd = dto.BaseCd,
            Status = dto.Status,
            PlannedRunMinutesPerDay = dto.PlannedRunMinutesPerDay <= 0 ? 480 : dto.PlannedRunMinutesPerDay,
            StandardCycleSec = dto.StandardCycleSec,
            CapacityPerHour = dto.CapacityPerHour,
            InstallDate = dto.InstallDate,
            LastMaintenanceDate = dto.LastMaintenanceDate,
            NextMaintenanceDate = dto.NextMaintenanceDate,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            Remarks = dto.Remarks,
            ActiveFlg = dto.ActiveFlg,
            Creator = userName,
            CreateDate = now,
        });
        await _db.SaveChangesAsync();
        return dto.MachineCd;
    }

    public async Task UpdateAsync(string machineCd, MachineDto dto, string? userName)
    {
        var m = await _db.Machines.FirstOrDefaultAsync(x => x.MachineCd == machineCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        m.MachineName = dto.MachineName;
        m.MachineType = dto.MachineType;
        m.ProcessCd = dto.ProcessCd;
        m.WgCd = dto.WgCd;
        m.BaseCd = dto.BaseCd;
        m.PlannedRunMinutesPerDay = dto.PlannedRunMinutesPerDay <= 0 ? 480 : dto.PlannedRunMinutesPerDay;
        m.StandardCycleSec = dto.StandardCycleSec;
        m.CapacityPerHour = dto.CapacityPerHour;
        m.InstallDate = dto.InstallDate;
        m.LastMaintenanceDate = dto.LastMaintenanceDate;
        m.NextMaintenanceDate = dto.NextMaintenanceDate;
        m.Manufacturer = dto.Manufacturer;
        m.Model = dto.Model;
        m.Remarks = dto.Remarks;
        m.ActiveFlg = dto.ActiveFlg;
        m.Modifier = userName;
        m.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string machineCd, string? userName)
    {
        var m = await _db.Machines.FirstOrDefaultAsync(x => x.MachineCd == machineCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");
        m.IsDeleted = true;
        m.Modifier = userName;
        m.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(string machineCd, int status, string? userName)
    {
        var m = await _db.Machines.FirstOrDefaultAsync(x => x.MachineCd == machineCd && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");
        m.Status = status;
        m.Modifier = userName;
        m.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        try { await _notifier.NotifyMachineStatusChangedAsync(machineCd, status); } catch { }
    }

    // ═══════════════════════════════════════════════════════════
    //  停止記録
    // ═══════════════════════════════════════════════════════════

    public async Task<PagedResultDto<MachineDowntimeDto>> SearchDowntimesAsync(DowntimeSearchQuery q)
    {
        var query = _db.MachineDowntimes.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.MachineCd)) query = query.Where(x => x.MachineCd == q.MachineCd);
        if (q.DateFrom.HasValue) query = query.Where(x => x.StartTime >= q.DateFrom);
        if (q.DateTo.HasValue) query = query.Where(x => x.StartTime <= q.DateTo);
        if (q.DowntimeType.HasValue) query = query.Where(x => x.DowntimeType == q.DowntimeType);
        if (q.OnlyOpen == true) query = query.Where(x => x.EndTime == null);

        var total = await query.CountAsync();
        var page = q.PageIndex <= 0 ? 1 : q.PageIndex;
        var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 500);

        var items = await query
            .OrderByDescending(x => x.StartTime)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();

        var mcds = items.Select(x => x.MachineCd).Distinct().ToList();
        var nameMap = await _db.Machines.AsNoTracking()
            .Where(m => mcds.Contains(m.MachineCd))
            .ToDictionaryAsync(m => m.MachineCd, m => m.MachineName);

        var dtos = items.Select(x =>
        {
            var dto = ToDto(x);
            dto.MachineName = nameMap.GetValueOrDefault(x.MachineCd);
            return dto;
        }).ToList();

        return new PagedResultDto<MachineDowntimeDto>
        {
            Total = total, PageIndex = page, PageSize = size, Items = dtos,
        };
    }

    public async Task<string> RegisterDowntimeAsync(MachineDowntimeDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.MachineCd)) throw new InvalidOperationException("ME-MSG-001");

        var no = await _seq.NextAsync(DowntimeSeqKey);
        var now = DateTime.Now;

        var startTime = dto.StartTime == default ? now : dto.StartTime;
        var endTime = dto.EndTime;
        int? minutes = endTime.HasValue
            ? (int)Math.Max(0, (endTime.Value - startTime).TotalMinutes)
            : (int?)null;

        _db.MachineDowntimes.Add(new MachineDowntime
        {
            DowntimeNo = no,
            MachineCd = dto.MachineCd,
            WorkOrderNo = dto.WorkOrderNo,
            StartTime = startTime,
            EndTime = endTime,
            DowntimeMinutes = minutes,
            DowntimeType = dto.DowntimeType <= 0 ? 9 : dto.DowntimeType,
            ReasonCd = dto.ReasonCd,
            Description = dto.Description,
            OperatorCd = dto.OperatorCd,
            RecoveryOperatorCd = dto.RecoveryOperatorCd,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = now,
        });

        // 設備状態を「故障」or「メンテ」に変更
        var machine = await _db.Machines.FirstOrDefaultAsync(x => x.MachineCd == dto.MachineCd && !x.IsDeleted);
        if (machine != null && !endTime.HasValue)
        {
            machine.Status = dto.DowntimeType == 2 ? 2 : (dto.DowntimeType == 1 ? 3 : 0);
            machine.Modifier = userName;
            machine.ModifyDate = now;
        }

        await _db.SaveChangesAsync();
        try
        {
            await _notifier.NotifyDowntimeRegisteredAsync(no, dto.MachineCd, dto.DowntimeType);
            if (machine != null) await _notifier.NotifyMachineStatusChangedAsync(machine.MachineCd, machine.Status);
        }
        catch { }
        return no;
    }

    public async Task CloseDowntimeAsync(string downtimeNo, DateTime endTime, string? recoveryOperatorCd, string? userName)
    {
        var dt = await _db.MachineDowntimes.FirstOrDefaultAsync(x => x.DowntimeNo == downtimeNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        if (dt.EndTime.HasValue)
            throw new InvalidOperationException("ME-MSG-042"); // 既に終了

        dt.EndTime = endTime;
        dt.DowntimeMinutes = (int)Math.Max(0, (endTime - dt.StartTime).TotalMinutes);
        if (!string.IsNullOrWhiteSpace(recoveryOperatorCd)) dt.RecoveryOperatorCd = recoveryOperatorCd;
        dt.Modifier = userName;
        dt.ModifyDate = DateTime.Now;

        // 設備を稼働中に戻す
        var machine = await _db.Machines.FirstOrDefaultAsync(x => x.MachineCd == dt.MachineCd && !x.IsDeleted);
        if (machine != null && (machine.Status == 2 || machine.Status == 3))
        {
            machine.Status = 1;
            machine.Modifier = userName;
            machine.ModifyDate = DateTime.Now;
        }

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper
    // ═══════════════════════════════════════════════════════════

    private static MachineDto ToDto(Machine x) => new()
    {
        Id = x.Id,
        MachineCd = x.MachineCd,
        MachineName = x.MachineName,
        MachineType = x.MachineType,
        ProcessCd = x.ProcessCd,
        WgCd = x.WgCd,
        BaseCd = x.BaseCd,
        Status = x.Status,
        PlannedRunMinutesPerDay = x.PlannedRunMinutesPerDay,
        StandardCycleSec = x.StandardCycleSec,
        CapacityPerHour = x.CapacityPerHour,
        InstallDate = x.InstallDate,
        LastMaintenanceDate = x.LastMaintenanceDate,
        NextMaintenanceDate = x.NextMaintenanceDate,
        Manufacturer = x.Manufacturer,
        Model = x.Model,
        Remarks = x.Remarks,
        ActiveFlg = x.ActiveFlg,
    };

    private static MachineDowntimeDto ToDto(MachineDowntime x) => new()
    {
        Id = x.Id,
        DowntimeNo = x.DowntimeNo,
        MachineCd = x.MachineCd,
        WorkOrderNo = x.WorkOrderNo,
        StartTime = x.StartTime,
        EndTime = x.EndTime,
        DowntimeMinutes = x.DowntimeMinutes,
        DowntimeType = x.DowntimeType,
        ReasonCd = x.ReasonCd,
        Description = x.Description,
        OperatorCd = x.OperatorCd,
        RecoveryOperatorCd = x.RecoveryOperatorCd,
        Remarks = x.Remarks,
        CreateDate = x.CreateDate,
    };
}
