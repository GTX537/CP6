using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpaceQuarantineStore
{
    Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceFileStore
{
    Task<Stream> OpenQuarantinedReadAsync(
        Guid tenantId,
        Guid fileId,
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid tenantId,
        Guid fileId,
        string storageKey,
        CancellationToken cancellationToken = default);
}

public interface ISpaceQuarantineWriteSession : IAsyncDisposable
{
    string StorageKey { get; }
    Stream Content { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);
    Task AbortAsync(CancellationToken cancellationToken = default);
}

public interface ISpaceFileCatalog
{
    Task<SpaceFile?> FindReusableAsync(
        Guid tenantId,
        string sha256,
        SpaceFileRetentionClass retentionClass,
        CancellationToken cancellationToken = default);

    Task AddQuarantinedWithScanJobAsync(
        SpaceFile file,
        SpaceJob scanJob,
        CancellationToken cancellationToken = default);
}

public interface ISpaceSourceCatalog
{
    Task<IReadOnlyList<SpaceModelSource>> FindByHashAsync(
        Guid tenantId,
        string sha256,
        CancellationToken cancellationToken = default);
}
