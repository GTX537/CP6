using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingStandardBasisComparisonV1(StandardBasisReferenceV1 reference, StandardBasisBodyV1 body)
{
    private readonly Dictionary<(Guid Source, CostElement Element), StandardBasisLineV1> lines =
        body.Lines.ToDictionary(line => (line.InputId, line.Element));

    internal StandardBasisReferenceV1 Reference => reference;

    internal static async Task<ManufacturingStandardBasisComparisonV1?> SelectAsync(CP6Context db, WorkOrder order,
        Guid productId, string? outputUnit, ManufacturingRateConsumptionV1.Selection published, OperationsAccessV1 access,
        OperationsAccessV1 rateAccess, CancellationToken ct)
    {
        var row = await new ManufacturingStandardBasisStoreV1(db).SelectedAsync(published.LedgerId, order.Id, ct);
        if (row == null)
            return null;
        await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-use", ct);
        if (row.State == "REVOKED")
            throw Error("STANDARD_BASIS_REVOKED", 409);
        var day = DateOnly.FromDateTime(published.Date);
        if (row.From > day || row.ToExclusive <= day)
            throw Error("STANDARD_BASIS_NOT_EFFECTIVE", 409);

        var body = row.ReadBody();
        if (body.ProductId != productId || body.Currency != published.Currency)
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 409);
        if (body.OutputUnit != outputUnit)
            throw Error("STANDARD_BASIS_UNIT_MISMATCH", 409);
        if (order.Status != WorkOrderStatus.Completed || body.TargetQuantity != order.CompletedQty)
            throw Error("STANDARD_BASIS_TARGET_MISMATCH", 409);
        if (published.Evidence.Any(rate => rate.PolicyVersionId != row.PolicyVersionId))
            throw Error("STANDARD_BASIS_POLICY_MISMATCH", 409);
        await ManufacturingStandardBasisSourcesV1.RequireOriginalRatesAsync(rateAccess, body, ct);
        return new(row.Reference, body);
    }

    internal void ApplyMaterial(Guid occurrence, string? unit, CostSheetLine line)
    {
        var standard = Take(occurrence, CostElement.Material);
        if (standard.Unit != unit)
            throw Error("STANDARD_BASIS_UNIT_MISMATCH", 409);
        line.PlanQty = standard.Quantity;
        line.StandardAmount = standard.Amount;
    }

    internal void ApplyTime(Guid step, CostCollectionBasisV1.RateUseV1 actualRate, CostSheetLine line)
    {
        var standard = Take(step, line.Element);
        if (standard.OperationId != actualRate.OperationProfileId || standard.WorkCenterId != actualRate.WorkCenterId)
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 409);
        line.StandardHours = standard.Quantity;
        line.StandardAmount = standard.Amount;
    }

    internal void RequireComplete()
    {
        if (lines.Count != 0)
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 409);
    }

    private StandardBasisLineV1 Take(Guid source, CostElement element)
        => lines.Remove((source, element), out var line) ? line : throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 409);
}
