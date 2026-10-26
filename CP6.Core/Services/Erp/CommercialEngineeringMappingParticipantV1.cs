using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Erp;

// The original ERP product Owner participates in the same Main transaction with its own explicit action.
internal static class CommercialEngineeringMappingParticipantV1
{
    internal static async Task<EngineeringOwnerConfirmationV1> Confirm(CP6Context db, OperationsAccessV1 root,
        EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        var access = await root.ActionAsync(OperationsOwnerV1.Product, "confirm-engineering", ct);
        var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, access, reference.Id, null, ct);
        if (reference.Owner != BusinessOwner || product.Version != reference.Version || product.Digest != reference.Digest)
            throw Error("ENGINEERING_BUSINESS_SOURCE_CHANGED", 412);
        if (!product.SourcePresent || product.Source.GetProperty("isDeleted").GetBoolean()) throw Error("ENGINEERING_BUSINESS_SOURCE_UNAVAILABLE");
        root.AtCommit(async token => { if (!await access.Products().AnyAsync(row => row.Id == reference.Id, token)) throw Error("OBJECT_FORBIDDEN", 403); });
        return access.EngineeringConfirmation(OperationsOwnerV1.Product, "confirm-engineering", BusinessOwner);
    }
}
