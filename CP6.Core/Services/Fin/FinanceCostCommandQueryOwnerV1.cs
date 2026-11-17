using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

public sealed class FinanceCostCommandQueryOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope,
    ManufacturingStandardDocumentOwnerV1 documents, ManufacturingCostMasterOwnerV1 masters)
{
    private readonly FinanceCostCommandJournalV1 journal = new(db);

    public Task<object> ReadAsync(Guid context, Guid command, bool result, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) =>
        {
            var binding = await journal.FindAsync(ledger.Id, command, token);
            if (binding == null)
            {
                if (result)
                    throw Error("NOT_FOUND_OR_NOT_VISIBLE", 404);
                try
                {
                    await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-view", token);
                }
                catch (OrderPriceCorrectionException error) when (error.Status == 403 && error.Message == "ACTION_FORBIDDEN")
                {
                    await access.ActionAsync(OperationsOwnerV1.FinCost, ManufacturingCostMasterV1.View, token);
                }
                return new
                {
                    kind = "NOT_OBSERVED", commandId = command,
                    observedAt = FinanceCostCommandCanonicalV1.Time(DateTimeOffset.UtcNow),
                    provesPermanentNoEffect = false,
                };
            }

            var master = ManufacturingCostMasterV1.OwnsCommandRoute(binding.Route);
            if (master)
                await access.ActionAsync(OperationsOwnerV1.FinCost, ManufacturingCostMasterV1.View, token);
            else
                await documents.RequireCommandVisibilityAsync(ledger.Id, command, access, token);
            if (!binding.AcceptedAt.HasValue)
                throw Error("OWNER_PARTICIPANT_UNPROVEN", 503);

            var terminal = await journal.ReadTerminalAsync(ledger.Id, command, token)
                ?? throw Error("AUTHORITY_UNAVAILABLE", 503);
            if (master)
                await masters.RequireCommandSourcesAsync(ledger, ManufacturingCostMasterOwnerV1.CommandVersion(terminal), access, token);

            return result
                ? FinanceCostCommandJournalV1.JsonBody(terminal.Result)
                : FinanceCostCommandJournalV1.Found(terminal.Receipt);
        }, ct);
}
