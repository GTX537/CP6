namespace CP6.Core.Services.CrmIdentity;

/// <summary>Directory DTOs only; the host supplies the reader and its trusted storage context.</summary>
public interface IIdentitySnapshotReader
{
    Task<IdentityVersionPage> ReadVersionsAsync(Guid tenant, string? cursor, int size = 200, CancellationToken cancellationToken = default);
    Task<IdentitySnapshot?> ReadSnapshotAsync(Guid tenant, string aggregate, CancellationToken cancellationToken = default);
}
