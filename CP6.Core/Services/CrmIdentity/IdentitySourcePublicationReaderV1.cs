using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

public interface IIdentitySourcePublicationReaderV1
{
    Task<byte[]?> ReadPublicationAsync(Guid tenant, long revision, CancellationToken ct = default);
}

internal sealed class IdentitySourcePublicationReaderV1(CP6Context db, CrmIdentityRuntime runtime) : IIdentitySourcePublicationReaderV1
{
    public async Task<byte[]?> ReadPublicationAsync(Guid tenant, long revision, CancellationToken ct = default)
    {
        if (revision < 1) throw new IdentityReadException("C02_SOURCE_VERSION_INVALID");
        await using var lease = await IdentitySourceAuthorityV1.ReadLeaseAsync(db, runtime, tenant, ct);
        return await db.Database.SqlQuery<byte[]>($"""
            SELECT cp6_identity_source_v1.read_publication({tenant},{revision}) AS "Value"
            """).SingleOrDefaultAsync(ct);
    }
}
