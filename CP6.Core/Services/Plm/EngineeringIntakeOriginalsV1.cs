using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Core.Services.Plm;

public sealed class EngineeringIntakeOriginalsV1(CP6Context db, IDataProtectionProvider protection,
    IEngineeringIntakeAuthorityV1? authority = null)
{
    private readonly EngineeringIntakeStoreV1 store = new(db, protection);

    internal async Task<byte[]> ReadRequirementOriginAsync(OperationsAccessV1 access, EngineeringSourceRefV1 reference,
        CancellationToken ct)
    {
        if (authority is null || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is null || reference.Owner != SourceOwner)
            throw Unproven();
        var original = await store.ReadRequestAsync(reference.Id, ct);
        if (original is null || original.Envelope.SourceBinding.SourceVersion != reference.Version
            || original.SourceBodyHash != reference.Digest) throw Unproven();
        var deadline = await HoldReadAsync(access, original.Source, ct);
        access.AtCommit(token => RequireBeforeAsync(deadline, token));
        return SourceBytes(original.Bytes);
    }

    internal async Task<DateTimeOffset> HoldReadAsync(OperationsAccessV1 access, EngineeringIntakeSourceLocationV1 location, CancellationToken ct)
    {
        var expires = authority is null ? null : await authority.HoldOriginalReadAsync(db, access.ActorId, access.IntakeCaller, location, ct);
        if (expires is not { } deadline) throw Unproven();
        await RequireBeforeAsync(deadline, ct);
        return deadline;
    }

    private async Task RequireBeforeAsync(DateTimeOffset deadline, CancellationToken ct)
    {
        if (deadline <= await store.NowAsync(ct)) throw Unproven();
    }

    private static CP6.Core.Services.Erp.OrderPriceCorrectionException Unproven() =>
        Failure("ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);
}
