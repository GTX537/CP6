using CP6.Entity.DomainModels.Mes;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task<IQueryable<WorkOrder>> PlanningOrders(CancellationToken ct)
    {
        var work = (await SourceAsync(OperationsOwnerV1.Mes, ct)).WorkOrders();
        // Stored work-order/process totals contain production facts. Require the whole source order's
        // current scope, rather than removing hidden reports and treating the remainder as its total.
        var production = (await SourceAsync(OperationsOwnerV1.Production, ct)).ProductionWorkOrders();
        return WorkOrders().Where(w => work.Any(s => s.Id == w.Id) && production.Any(s => s.Id == w.Id));
    }
}
