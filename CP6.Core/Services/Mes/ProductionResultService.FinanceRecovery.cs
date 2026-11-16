using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Integration;

namespace CP6.Core.Services.Mes;

public partial class ProductionResultService
{
    private bool HasDurableFinanceCompletion => _backflush is BackflushService && _finBridge is FinBridgeHook;

    internal async Task<IntegrationEvent?> ResumeMesCompletionFinanceAsync(Guid id, DateTime dueBefore, CancellationToken ct)
    {
        if (_backflush is not BackflushService backflush || !backflush.Owns(_db) || _finBridge is not FinBridgeHook finance)
            throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);

        while (true)
        {
            string? material = null;
            BackflushService.BackflushMaterialEffects? effects = null;
            var progress = await Owner.ExecuteMesCompletionStepAsync(id, dueBefore, finance: false, ct,
                async (evt, intent, access, token) =>
                {
                    if (intent.RemainingMaterials is { Length: 0 })
                        return intent;
                    var remaining = intent.RemainingMaterials ?? await backflush.ReadMaterialsAsync(evt.SourceNo, token);
                    if (remaining.Length == 0)
                        return intent with { RemainingMaterials = remaining };

                    material = remaining[0];
                    effects = await backflush.ApplyMaterialAsync(evt.SourceNo, material, access, inheritedCompletion: true, token);
                    return intent with { RemainingMaterials = remaining[1..] };
                });
            if (progress is not { } current)
                return null;
            if (current.Event.Status != IntegrationEventStatus.Pending)
                return current.Event;
            if (effects != null)
                await backflush.PublishMaterialAsync(current.Event.SourceNo, material!, effects);
            if (current.Intent!.RemainingMaterials!.Length == 0)
                break;
        }

        var outcome = await Owner.ExecuteMesCompletionStepAsync(id, dueBefore, finance: true, ct,
            async (evt, intent, access, token) =>
            {
                if (intent.RemainingMaterials is not { Length: 0 })
                    throw new OrderPriceCorrectionException("BACKFLUSH_NOT_CONFIRMED", 409);

                await finance.ApplyMesCompletionFinanceAsync(evt, access, token);
                return intent;
            });
        return outcome?.Event;
    }
}
