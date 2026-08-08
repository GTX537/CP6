using CP6.Space.Application;

namespace CP6.Space.Infrastructure;

public sealed class SpaceFileStorageOptions
{
    public required string RootPath { get; init; }
}

public sealed class FileSystemSpaceFileStore :
    ISpaceQuarantineStore,
    ISpaceFileStore
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public FileSystemSpaceFileStore(SpaceFileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException(
                "The Space file storage root is required.");
        }

        _rootPath = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_rootPath);
        _rootPrefix = _rootPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireIdentity(tenantId, nameof(tenantId));
        RequireIdentity(fileId, nameof(fileId));

        var directory = ResolveDirectory(tenantId, fileId);
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var storageKey =
            $"{tenantId:N}/{fileId:N}/{token}.content";
        var finalPath = ResolveStoragePath(
            tenantId,
            fileId,
            storageKey);
        var temporaryPath = finalPath + ".upload";
        ISpaceQuarantineWriteSession session =
            new FileSystemWriteSession(
                storageKey,
                temporaryPath,
                finalPath);
        return Task.FromResult(session);
    }

    public Task<Stream> OpenQuarantinedReadAsync(
        Guid tenantId,
        Guid fileId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(tenantId, fileId, storageKey);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        Guid tenantId,
        Guid fileId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(tenantId, fileId, storageKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveDirectory(Guid tenantId, Guid fileId)
    {
        var path = Path.GetFullPath(
            Path.Combine(
                _rootPath,
                tenantId.ToString("N"),
                fileId.ToString("N")));
        EnsureWithinRoot(path);
        return path;
    }

    private string ResolveStoragePath(
        Guid tenantId,
        Guid fileId,
        string storageKey)
    {
        RequireIdentity(tenantId, nameof(tenantId));
        RequireIdentity(fileId, nameof(fileId));
        if (string.IsNullOrWhiteSpace(storageKey) ||
            Path.IsPathRooted(storageKey))
        {
            throw new InvalidOperationException(
                "The Space storage key is invalid.");
        }

        var normalizedKey = storageKey.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var expectedPrefix =
            $"{tenantId:N}{Path.DirectorySeparatorChar}" +
            $"{fileId:N}{Path.DirectorySeparatorChar}";
        if (!normalizedKey.StartsWith(
                expectedPrefix,
                StringComparison.Ordinal) ||
            normalizedKey.Contains(
                $"{Path.DirectorySeparatorChar}.." +
                Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            normalizedKey.EndsWith(
                $"{Path.DirectorySeparatorChar}..",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Space storage key crossed its tenant/file boundary.");
        }

        var path = Path.GetFullPath(
            Path.Combine(_rootPath, normalizedKey));
        EnsureWithinRoot(path);
        return path;
    }

    private void EnsureWithinRoot(string path)
    {
        if (!path.StartsWith(
                _rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Space storage path escaped the configured root.");
        }
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private sealed class FileSystemWriteSession :
        ISpaceQuarantineWriteSession
    {
        private readonly string _temporaryPath;
        private readonly string _finalPath;
        private FileStream? _stream;
        private bool _committed;

        public FileSystemWriteSession(
            string storageKey,
            string temporaryPath,
            string finalPath)
        {
            StorageKey = storageKey;
            _temporaryPath = temporaryPath;
            _finalPath = finalPath;
            _stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
        }

        public string StorageKey { get; }

        public Stream Content =>
            _stream ??
            throw new ObjectDisposedException(
                nameof(FileSystemWriteSession));

        public async Task CommitAsync(
            CancellationToken cancellationToken = default)
        {
            if (_committed)
                return;
            var stream = _stream ??
                         throw new ObjectDisposedException(
                             nameof(FileSystemWriteSession));
            await stream.FlushAsync(cancellationToken);
            await stream.DisposeAsync();
            _stream = null;
            File.Move(_temporaryPath, _finalPath);
            _committed = true;
        }

        public async Task AbortAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stream is not null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }
            if (!_committed && File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
                await AbortAsync(CancellationToken.None);
        }
    }
}
