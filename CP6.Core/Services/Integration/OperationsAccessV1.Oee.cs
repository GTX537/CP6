using CP6.Entity.DomainModels.Mes;

namespace CP6.Core.Services.Integration;

/// <summary>Independent sources for the legacy whole-machine/day calculation. Never sum a filtered subset.</summary>
internal sealed record OeeSourcesV1(IQueryable<Machine> Machines,
    IQueryable<MachineDowntime> Downtimes, IQueryable<ProductionResult> Production);

internal sealed partial class OperationsAccessV1
{
    internal async Task<OeeSourcesV1> OeeSources(CancellationToken ct)
    {
        var machines = await SourceAsync(OperationsOwnerV1.Machine, ct);
        var production = await SourceAsync(OperationsOwnerV1.Production, ct);
        var workOrders = await SourceAsync(OperationsOwnerV1.Mes, ct);
        var machineScope = machines.Machines();
        var orderScope = workOrders.WorkOrders();
        // The OEE object's scope is the exact existing machine identity; source grants remain independent.
        return new(Machines().Where(m => machineScope.Any(s => s.Id == m.Id)),
            await machines.MachineDowntimes(ct),
            production.ProductionResults().Where(p => orderScope.Any(w => w.WorkOrderNo == p.WorkOrderNo)));
    }
}
