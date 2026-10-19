namespace CP6.Core.Services.CrmIdentity.Baselines;

public interface IIdentityRawSnapshotReader
{
    Task<IdentityBaselinePageV1> ReadBaselinePageAsync(Guid tenant, string? cursor, int size = 200, CancellationToken ct = default);
    Task<IdentityRawSnapshotV1?> ReadRawSnapshotAsync(Guid tenant, string aggregate, CancellationToken ct = default);
}
