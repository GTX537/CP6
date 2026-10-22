using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class MachineService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true && _seq is MesSequenceService sequence && sequence.Owns(_db)
        ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    public Task<List<MachineDto>> SearchAsync(MachineSearchQuery query)
    {
        if (!_db.Database.IsNpgsql()) return SearchCoreAsync(query);
        var frozen = OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine, "query", false, (a, ct) => SearchCoreAsync(frozen, a, ct));
    }

    public Task<MachineDto?> GetByCdAsync(string code) => !_db.Database.IsNpgsql() ? GetByCdCoreAsync(code) :
        Owner.ExecuteAsync(OperationsOwnerV1.Machine, "query", false, async (a, ct) =>
        { await a.RequireMachine(code, ct); return await GetByCdCoreAsync(code); });

    public Task<string> CreateAsync(MachineDto dto, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return CreateCoreAsync(dto, userName);
        var frozen = OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine, "add", true, async (a, ct) =>
        {
            var code = await CreateCoreAsync(frozen, a.Actor);
            // Department-scoped creation needs a separately trusted binding; never infer the actor's department.
            a.AtCommit(t => a.RequireMachine(code, t)); return code;
        });
    }

    public Task UpdateAsync(string code, MachineDto dto, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return UpdateCoreAsync(code, dto, userName);
        var frozen = OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine, "edit", true, async (a, ct) =>
        {
            await a.RequireMachine(code, ct); await UpdateCoreAsync(code, frozen, a.Actor);
            a.AtCommit(t => a.RequireMachine(code, t)); return true;
        });
    }

    public Task DeleteAsync(string code, string? userName) => !_db.Database.IsNpgsql() ? DeleteCoreAsync(code, userName) :
        Owner.ExecuteAsync(OperationsOwnerV1.Machine, "del", true, async (a, ct) =>
        {
            await a.RequireMachine(code, ct); await DeleteCoreAsync(code, a.Actor);
            a.AtCommit(t => a.RequireMachine(code, t, includeDeleted: true)); return true;
        });

    public async Task ChangeStatusAsync(string code, int status, string? userName)
    {
        if (!_db.Database.IsNpgsql()) { await ChangeStatusCoreAsync(code, status, userName); return; }
        await Owner.ExecuteAsync(OperationsOwnerV1.Machine, "status", true, async (a, ct) =>
        {
            await a.RequireMachine(code, ct); await ChangeStatusCoreAsync(code, status, a.Actor, notify: false);
            a.AtCommit(t => a.RequireMachine(code, t)); return true;
        });
        try { await _notifier.NotifyMachineStatusChangedAsync(code, status); } catch { }
    }

    public Task<PagedResultDto<MachineDowntimeDto>> SearchDowntimesAsync(DowntimeSearchQuery query)
    {
        if (!_db.Database.IsNpgsql()) return SearchDowntimesCoreAsync(query);
        var frozen = OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine, "query", false,
            async (a, ct) => await SearchDowntimesCoreAsync(frozen, await a.MachineDowntimes(ct)));
    }

    public async Task<string> RegisterDowntimeAsync(MachineDowntimeDto dto, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return await RegisterDowntimeCoreAsync(dto, userName);
        var frozen = OperationsOwnerV1.Freeze(dto);
        var result = await Owner.ExecuteAsync(OperationsOwnerV1.Machine, "downtime", true, async (a, ct) =>
        {
            await a.RequireMachine(frozen.MachineCd, ct); await a.RequireMachineOrder(frozen.WorkOrderNo, ct);
            var number = await RegisterDowntimeCoreAsync(frozen, a.Actor, notify: false);
            a.AtCommit(async t => { await a.RequireMachine(frozen.MachineCd, t); await a.RequireMachineOrder(frozen.WorkOrderNo, t); });
            var status = await _db.Machines.Where(m => m.MachineCd == frozen.MachineCd && !m.IsDeleted).Select(m => m.Status).SingleAsync(ct);
            return (Number: number, Status: status);
        });
        try
        {
            await _notifier.NotifyDowntimeRegisteredAsync(result.Number, frozen.MachineCd, frozen.DowntimeType);
            await _notifier.NotifyMachineStatusChangedAsync(frozen.MachineCd, result.Status);
        }
        catch { }
        return result.Number;
    }

    public async Task CloseDowntimeAsync(string number, DateTime endTime, string? recoveryOperatorCd, string? userName)
    {
        if (!_db.Database.IsNpgsql()) { await CloseDowntimeCoreAsync(number, endTime, recoveryOperatorCd, userName); return; }
        var result = await Owner.ExecuteAsync(OperationsOwnerV1.Machine, "downtime", true, async (a, ct) =>
        {
            await a.RequireMachineDowntime(number, ct);
            await CloseDowntimeCoreAsync(number, endTime, recoveryOperatorCd, a.Actor);
            a.AtCommit(t => a.RequireMachineDowntime(number, t));
            var code = await _db.MachineDowntimes.Where(d => d.DowntimeNo == number).Select(d => d.MachineCd).SingleAsync(ct);
            var status = await _db.Machines.Where(m => m.MachineCd == code && !m.IsDeleted).Select(m => m.Status).SingleAsync(ct);
            return (Code: code, Status: status);
        });
        try { await _notifier.NotifyMachineStatusChangedAsync(result.Code, result.Status); } catch { }
    }
}
