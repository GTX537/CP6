using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Fin;

public partial class FinBridgeHook
{
    internal static bool IsLegacyWorkOrderCompletionEvent(IntegrationEvent evt)
        => evt.SourceModule == "MES" && evt.TargetModule == "FIN" && evt.HookName == nameof(OnWorkOrderCompletedAsync);

    internal Task<FinBridgeResult> OnWorkOrderCompletedOwnedAsync(OperationsDelegationV1 ticket)
    {
        if (_operations?.Owns(Db) != true || _cost is not CostCollectService collect || !collect.Owns(Db)
            || _settle is not CostSettleService settle || !settle.Owns(Db) || ticket.CompletionResultId is not Guid completionId)
            throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);

        return _operations.ExecuteAsync(OperationsOwnerV1.FinCost, "settle", true, async (parent, ct) =>
        {
            var previous = await Db.IntegrationEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == completionId, ct);
            if (previous != null)
            {
                if (previous.SourceModule != "MES" || previous.TargetModule != "FIN"
                    || previous.HookName != nameof(OnWorkOrderCompletedOwnedAsync) || previous.SourceNo != ticket.Number)
                    throw new OrderPriceCorrectionException("COMPLETION_FINANCE_EVENT_CONFLICT", 409);
                return CompletionResult(previous);
            }

            // 原完工动作核验制造事实，财务动作分别核验当前归集和结转权限。
            var txn = Db.Database.CurrentTransaction!;
            const string effects = "mes_finance_completion";
            var original = Db.ChangeTracker.Entries().ToDictionary(entry => entry.Entity,
                entry => entry.CurrentValues.Clone(), ReferenceEqualityComparer.Instance);
            await txn.CreateSavepointAsync(effects, ct);

            CompletionOutcome outcome;
            try
            {
                var collector = await parent.ActionAsync(OperationsOwnerV1.FinCost, "collect", ct);
                var settler = await parent.ActionAsync(OperationsOwnerV1.FinCost, "settle", ct);
                outcome = await ApplyCompletionFinanceAsync(collect, settle, collector, settler, ticket.Number, ct);
            }
            catch (OrderPriceCorrectionException error) when (error.Status is 403 or 409 or 422)
            {
                outcome = new(IntegrationEventStatus.DeadLetter, null, error.Message);
            }
            if (outcome.Status == IntegrationEventStatus.DeadLetter)
            {
                await txn.RollbackToSavepointAsync(effects, ct);
                foreach (var entry in Db.ChangeTracker.Entries().ToArray())
                {
                    if (!original.TryGetValue(entry.Entity, out var values))
                        entry.State = EntityState.Detached;
                    else
                    {
                        entry.CurrentValues.SetValues(values);
                        entry.OriginalValues.SetValues(values);
                        entry.State = EntityState.Unchanged;
                    }
                }
            }
            await txn.ReleaseSavepointAsync(effects, ct);

            var evt = new IntegrationEvent
            {
                Id = completionId, TenantId = Db.CurrentTenantId, CorrelationId = completionId,
                SourceModule = "MES", TargetModule = "FIN", HookName = nameof(OnWorkOrderCompletedOwnedAsync),
                SourceNo = ticket.Number, TargetNo = outcome.SheetNo, Status = outcome.Status, LastError = outcome.Error,
                Attempts = 1, PayloadJson = JsonSerializer.Serialize(new { workOrderNo = ticket.Number, completionId }),
                Creator = parent.Actor, CreateDate = DateTime.Now,
            };
            Db.IntegrationEvents.Add(evt);
            await Db.SaveChangesAsync(ct);
            return CompletionResult(evt);
        }, delegation: ticket);
    }

    private async Task<CompletionOutcome> ApplyCompletionFinanceAsync(CostCollectService collect, CostSettleService settle,
        OperationsAccessV1 collector, OperationsAccessV1 settler, string number, CancellationToken ct)
    {
        var collected = await collect.CollectWithinOwnerAsync(number, collector, ct);
        if (!collected.Ok)
            return new(collected.Code == "E-FIN-402" ? IntegrationEventStatus.Skipped : IntegrationEventStatus.DeadLetter,
                null, collected.Code);

        var sheet = Db.CostSheets.Local.Single(row => row.WorkOrderNo == number);
        if (sheet.TotalActual <= 0m)
            return new(IntegrationEventStatus.Skipped, sheet.No, "zero total cost");

        var settled = await settle.SettleAsync(number, settler.Actor);
        return settled.Ok
            ? new(IntegrationEventStatus.Success, sheet.No, null)
            : new(IntegrationEventStatus.DeadLetter, null, settled.Code);
    }

    private static FinBridgeResult CompletionResult(IntegrationEvent evt) => evt.Status switch
    {
        IntegrationEventStatus.Success => FinBridgeResult.Ok(evt.TargetNo),
        IntegrationEventStatus.Skipped => FinBridgeResult.Skipped(evt.LastError!),
        _ => FinBridgeResult.Failed(evt.LastError!),
    };

    private sealed record CompletionOutcome(string Status, string? SheetNo, string? Error);
}
