using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public partial class FinBridgeHook
{
    internal static bool IsLegacyWorkOrderCompletionEvent(IntegrationEvent evt)
        => evt.SourceModule == "MES" && evt.TargetModule == "FIN" && evt.HookName == nameof(OnWorkOrderCompletedAsync);

    internal async Task ApplyMesCompletionFinanceAsync(IntegrationEvent evt, OperationsAccessV1 parent, CancellationToken ct)
    {
        if (_cost is not CostCollectService collect || !collect.Owns(Db)
            || _settle is not CostSettleService settle || !settle.Owns(Db))
            throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);

        var collector = await parent.ActionAsync(OperationsOwnerV1.FinCost, "collect", ct);
        var settler = await parent.ActionAsync(OperationsOwnerV1.FinCost, "settle", ct);
        var collected = await collect.CollectWithinOwnerAsync(evt.SourceNo, collector, ct);
        if (!collected.Ok)
        {
            if (collected.Code != "E-FIN-402")
                throw new OrderPriceCorrectionException(collected.Code!, 422);
            evt.Status = IntegrationEventStatus.Skipped;
            evt.LastError = collected.Code;
            return;
        }

        var sheet = Db.CostSheets.Local.Single(row => row.WorkOrderNo == evt.SourceNo);
        evt.TargetNo = sheet.No;
        if (sheet.TotalActual <= 0m)
        {
            evt.Status = IntegrationEventStatus.Skipped;
            evt.LastError = "zero total cost";
            return;
        }

        var settled = await settle.SettleForSubjectAsync(evt.SourceNo, settler.Actor, settler.ActorId);
        if (settled.Code == "COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED")
        {
            evt.Status = IntegrationEventStatus.Skipped;
            evt.LastError = settled.Code;
            return;
        }
        if (!settled.Ok)
            throw new OrderPriceCorrectionException(settled.Code!, 422);
        evt.Status = IntegrationEventStatus.Success;
    }
}
