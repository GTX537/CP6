using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class OeeService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static DateTime Day(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    private sealed record Inputs(Machine Machine, DateTime Date, List<MachineDowntime> Downtime,
        List<ProductionResult> Production, byte[] Bytes);

    public Task<List<OeeDailyDto>> SearchAsync(OeeSearchQuery query)
    {
        if (!_db.Database.IsNpgsql()) return SearchCoreAsync(query);
        var frozen = OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Oee, "query", false,
            async (a, ct) => await SearchOwned(frozen, await a.OeeSources(ct), ct));
    }

    public Task<List<OeeDailyDto>> CalculateTodayAsync(string? code = null)
    {
        if (!_db.Database.IsNpgsql()) return CalculateTodayCoreAsync(code);
        return Owner.ExecuteAsync(OperationsOwnerV1.Oee, "query", false, async (a, ct) =>
        {
            var sources = await a.OeeSources(ct);
            var machines = await SelectMachines(sources, code, true, ct);
            var observed = DateTime.Now; var date = Day(observed); var result = new List<OeeDailyDto>();
            foreach (var machine in machines)
            {
                var input = await Capture(machine, date, sources, ct);
                if (input == null) { if (!string.IsNullOrWhiteSpace(code)) throw Incomplete(); continue; }
                result.Add(Calculate(input, observed));
            }
            return result;
        });
    }

    public Task<Dictionary<string, List<OeeDailyDto>>> GetTrendAsync(int days, string? code)
    {
        if (!_db.Database.IsNpgsql()) return GetTrendCoreAsync(days, code);
        return Owner.ExecuteAsync(OperationsOwnerV1.Oee, "query", false, async (a, ct) =>
        {
            var date = Day(DateTime.Today);
            var result = await SearchOwned(new() { MachineCd = code, DateFrom = date.AddDays(-days + 1), DateTo = date }, await a.OeeSources(ct), ct);
            return result.OrderBy(x => x.OeeDate).GroupBy(x => x.MachineCd).ToDictionary(g => g.Key, g => g.ToList());
        });
    }

    public Task<int> RecalculateAsync(OeeRecalcRequest request, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return RecalculateCoreAsync(request, userName);
        var frozen = OperationsOwnerV1.Freeze(request);
        return Owner.ExecuteAsync(OperationsOwnerV1.Oee, "recalculate", true, async (a, ct) =>
        {
            var sources = await a.OeeSources(ct);
            var inputs = await RecalculateOwned(frozen, sources, a.ActorId, a.Actor, ct);
            a.AtCommit(t => ValidateInputs(inputs, sources, t)); return inputs.Count;
        });
    }

    // Called only from the fixed host worker, after TenantScopeRunner has established the fresh scope.
    // There is no user-name, no-HttpContext or anonymous-session bypass on the public service surface.
    Task<int> IOeeWorkerV1.RecalculateAsync(OeeRecalcRequest request, Guid enumeratedTenant, CancellationToken ct)
        => RecalculateWorkerAsync(request, new(_db, enumeratedTenant), ct);

    internal Task<int> RecalculateWorkerAsync(OeeRecalcRequest request, OeeWorkerInvocationV1 invocation, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql()) return RecalculateCoreAsync(request, "OeeWorker");
        var frozen = OperationsOwnerV1.Freeze(request);
        return Owner.ExecuteOeeWorkerAsync(invocation, async token =>
        {
            var sources = WorkerSources();
            var inputs = await RecalculateOwned(frozen, sources, null, "Main.Oee.DailyWorker.v1", token);
            await ValidateInputs(inputs, sources, token); return inputs.Count;
        }, ct);
    }

    private OeeSourcesV1 WorkerSources()
    {
        var machines = _db.Machines.AsNoTracking().Where(m => !m.IsDeleted);
        var orders = _db.WorkOrders.AsNoTracking().Where(w => !w.IsDeleted
            && (w.WebOrderNo == null || w.WebOrderNo == "" || _db.Orders.Any(o => o.WebOrderNo == w.WebOrderNo && !o.IsDeleted)));
        var downtime = _db.MachineDowntimes.AsNoTracking().Where(d => !d.IsDeleted
            && machines.Any(m => m.MachineCd == d.MachineCd)
            && (d.WorkOrderNo == null || d.WorkOrderNo == "" || orders.Any(w => w.WorkOrderNo == d.WorkOrderNo)));
        var production = _db.ProductionResults.AsNoTracking().Where(p => !p.IsDeleted && orders.Any(w => w.WorkOrderNo == p.WorkOrderNo));
        return new(machines, downtime, production);
    }

    private static OrderPriceCorrectionException Incomplete() => new("OEE_SOURCE_INCOMPLETE_OR_FORBIDDEN", 403);
    private async Task<List<Machine>> SelectMachines(OeeSourcesV1 sources, string? code, bool active, CancellationToken ct)
    {
        var query = sources.Machines.Where(m => !active || m.ActiveFlg);
        if (!string.IsNullOrWhiteSpace(code)) query = query.Where(m => m.MachineCd == code);
        var machines = await query.OrderBy(m => m.MachineCd).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(code) && machines.Count == 0) throw Incomplete();
        return machines;
    }

    private async Task<Inputs?> Capture(Machine machine, DateTime date, OeeSourcesV1 sources, CancellationToken ct)
    {
        var next = date.AddDays(1);
        var downtime = _db.MachineDowntimes.AsNoTracking().Where(d => !d.IsDeleted && d.MachineCd == machine.MachineCd
            && d.StartTime < next && (d.EndTime == null || d.EndTime >= date));
        var production = _db.ProductionResults.AsNoTracking().Where(p => !p.IsDeleted && p.MachineCd == machine.MachineCd
            && p.CreateDate >= date && p.CreateDate < next);
        if (!await sources.Machines.AnyAsync(m => m.Id == machine.Id, ct)
            || await downtime.AnyAsync(d => !sources.Downtimes.Any(allowed => allowed.Id == d.Id), ct)
            || await production.AnyAsync(p => !sources.Production.Any(allowed => allowed.Id == p.Id), ct)) return null;
        var allDowntime = await downtime.ToListAsync(ct); var allProduction = await production.ToListAsync(ct);
        return new(machine, date, allDowntime, allProduction, OeeEvidenceV1.Inputs(machine, date, allDowntime, allProduction));
    }

    private async Task ValidateInputs(List<Inputs> inputs, OeeSourcesV1 sources, CancellationToken ct)
    {
        foreach (var input in inputs)
        {
            var machine = await sources.Machines.SingleOrDefaultAsync(m => m.Id == input.Machine.Id, ct);
            var final = machine == null ? null : await Capture(machine, input.Date, sources, ct);
            if (final == null || !input.Bytes.SequenceEqual(final.Bytes)) throw Incomplete();
        }
    }

    private async Task<List<OeeDailyDto>> SearchOwned(OeeSearchQuery query, OeeSourcesV1 sources, CancellationToken ct)
    {
        var rows = _db.OeeDailies.AsNoTracking().Where(o => !o.IsDeleted && sources.Machines.Any(m => m.MachineCd == o.MachineCd));
        if (!string.IsNullOrWhiteSpace(query.MachineCd)) rows = rows.Where(o => o.MachineCd == query.MachineCd);
        if (query.DateFrom.HasValue) { var from = Day(query.DateFrom.Value); rows = rows.Where(o => o.OeeDate >= from); }
        if (query.DateTo.HasValue) { var to = Day(query.DateTo.Value); rows = rows.Where(o => o.OeeDate <= to); }
        var result = new List<OeeDailyDto>();
        foreach (var row in await rows.OrderByDescending(o => o.OeeDate).ThenBy(o => o.MachineCd).ToListAsync(ct))
        {
            var machine = await sources.Machines.SingleAsync(m => m.MachineCd == row.MachineCd, ct);
            var input = await Capture(machine, Day(row.OeeDate), sources, ct);
            if (input == null || !await OeeEvidenceV1.Matches(_db, machine, row, input.Bytes, ct)) continue;
            var dto = ToDto(row); dto.MachineName = machine.MachineName; dto.SourceEvidenceVersion = OeeEvidenceV1.Version; result.Add(dto);
        }
        return result;
    }

    private async Task<List<Inputs>> RecalculateOwned(OeeRecalcRequest request, OeeSourcesV1 sources,
        Guid? actor, string actorName, CancellationToken ct)
    {
        var date = Day(request.TargetDate); var observed = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        var inputs = new List<Inputs>();
        foreach (var machine in await SelectMachines(sources, request.MachineCd, true, ct))
        {
            var input = await Capture(machine, date, sources, ct) ?? throw Incomplete(); inputs.Add(input);
            var dto = Calculate(input, observed);
            var row = await _db.OeeDailies.SingleOrDefaultAsync(o => o.OeeDate == date && o.MachineCd == machine.MachineCd, ct);
            if (row?.IsDeleted == true) throw new OrderPriceCorrectionException("OEE_DAILY_UNAVAILABLE", 409);
            if (row == null)
            {
                row = new() { OeeDate = date, MachineCd = machine.MachineCd, Creator = actorName, CreateDate = observed };
                _db.OeeDailies.Add(row);
            }
            else { row.Modifier = actorName; row.ModifyDate = observed; }
            row.PlannedRunMinutes = dto.PlannedRunMinutes; row.ActualRunMinutes = dto.ActualRunMinutes;
            row.DowntimeMinutes = dto.DowntimeMinutes; row.GoodQty = dto.GoodQty; row.DefectQty = dto.DefectQty;
            row.Availability = dto.Availability; row.Performance = dto.Performance; row.Quality = dto.Quality; row.Oee = dto.Oee;
            await _db.SaveChangesAsync(ct);
            await OeeEvidenceV1.Save(_db, machine, row, input.Bytes, observed, actor, actorName, ct);
        }
        return inputs;
    }

    private static OeeDailyDto Calculate(Inputs input, DateTime observed)
    {
        var m = input.Machine; var next = input.Date.AddDays(1);
        var planned = m.PlannedRunMinutesPerDay > 0 ? m.PlannedRunMinutesPerDay : 480;
        var downtime = 0;
        foreach (var d in input.Downtime)
        {
            var start = d.StartTime < input.Date ? input.Date : d.StartTime;
            var end = d.EndTime ?? observed; if (end > next) end = next;
            if (end > start) downtime += (int)(end - start).TotalMinutes;
        }
        if (downtime > planned) downtime = planned;
        var actual = planned - downtime; var good = input.Production.Sum(p => p.GoodQty); var defect = input.Production.Sum(p => p.DefectQty);
        var total = good + defect;
        var availability = planned > 0 ? Math.Round((decimal)actual / planned * 100m, 4) : 0m;
        var quality = total > 0 ? Math.Round(good / total * 100m, 4) : 0m;
        decimal performance = 0m;
        if (m.CapacityPerHour.HasValue && m.CapacityPerHour > 0 && actual > 0)
        {
            var capacity = m.CapacityPerHour.Value * actual / 60m;
            if (capacity > 0) performance = Math.Round(total / capacity * 100m, 4);
            if (performance > 100m) performance = 100m;
        }
        else performance = total > 0 ? 100m : 0m;
        return new() { OeeDate = input.Date, MachineCd = m.MachineCd, MachineName = m.MachineName,
            PlannedRunMinutes = planned, ActualRunMinutes = actual, DowntimeMinutes = downtime,
            GoodQty = good, DefectQty = defect, Availability = availability, Performance = performance,
            Quality = quality, Oee = Math.Round(availability * performance * quality / 10000m, 4), SourceEvidenceVersion = OeeEvidenceV1.Version };
    }
}
