using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<Machine> Machines(bool includeDeleted = false)
    {
        var rows = db.Machines.AsNoTracking().Where(m => includeDeleted || !m.IsDeleted);
        if (authority.Scope == 1) return rows.Where(m => m.Creator == Actor);
        if (authority.Scope == 5) return rows;
        var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "machine");
        return rows.Where(m => ids.Contains(m.Id));
    }

    internal async Task RequireMachine(string code, CancellationToken ct, bool includeDeleted = false)
    {
        if (!await Machines(includeDeleted).AnyAsync(m => m.MachineCd == code, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    internal async Task<IQueryable<WorkOrder>> OptionalMachineOrders(CancellationToken ct)
    {
        try { return (await SourceAsync(OperationsOwnerV1.Mes, ct)).WorkOrders(); }
        // Machine-only readers can still read machines, but never inherit work-order authority.
        catch (OrderPriceCorrectionException ex) when (ex.Message == "ACTION_FORBIDDEN")
        { return db.WorkOrders.AsNoTracking().Where(_ => false); }
    }

    internal async Task<IQueryable<MachineDowntime>> MachineDowntimes(CancellationToken ct)
    {
        var machines = Machines();
        var orders = await OptionalMachineOrders(ct);
        var rows = db.MachineDowntimes.AsNoTracking().Where(d => !d.IsDeleted
            && machines.Any(m => m.MachineCd == d.MachineCd)
            && (d.WorkOrderNo == null || d.WorkOrderNo == "" || orders.Any(w => w.WorkOrderNo == d.WorkOrderNo)));
        return authority.Scope == 1 ? rows.Where(d => d.Creator == Actor) : rows;
    }

    internal async Task RequireMachineOrder(string? number, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(number)) return;
        var source = await SourceAsync(OperationsOwnerV1.Mes, ct);
        await source.RequireWorkOrder(number, false, ct);
    }

    internal async Task RequireMachineDowntime(string number, CancellationToken ct)
    {
        if (!await (await MachineDowntimes(ct)).AnyAsync(d => d.DowntimeNo == number, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
}
