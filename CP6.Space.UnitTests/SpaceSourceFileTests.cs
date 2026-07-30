using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceSourceFileTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Upload_streams_hashes_and_never_uses_the_client_path_as_storage_key()
    {
        var payload = Encoding.ASCII.GetBytes("%PDF-1.7\nspace");
        var input = new TrackingReadStream(payload);
        var quarantine = new FakeQuarantineStore();
        var catalog = new FakeFileCatalog();
        var service = NewUploadService(quarantine, catalog);

        var result = await service.UploadAsync(
            new SpaceFileUploadRequest(
                SpaceSourceType.Pdf,
                @"C:\users\alice\secret\floor.pdf",
                "application/pdf; charset=binary"),
            input);

        Assert.False(result.Reused);
        Assert.Equal("floor.pdf", result.File.OriginalName);
        Assert.Equal(".pdf", result.File.Extension);
        Assert.Equal("application/pdf", result.File.DeclaredContentType);
        Assert.Equal("application/pdf", result.File.DetectedContentType);
        Assert.Equal(SpaceFileState.Quarantined, result.File.State);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            result.File.Sha256);
        Assert.DoesNotContain("floor.pdf", result.File.StorageKey);
        Assert.DoesNotContain("alice", result.File.StorageKey);
        Assert.Equal(payload, quarantine.Sessions.Single().Bytes);
        Assert.True(input.MaxRequestedBytes <= 64 * 1024);
        Assert.Equal(1, quarantine.Sessions.Single().CommitCount);
        Assert.Equal(0, quarantine.Sessions.Single().AbortCount);
    }

    [Fact]
    public async Task Duplicate_hash_reuses_metadata_and_aborts_the_second_object()
    {
        var payload = Encoding.ASCII.GetBytes("%PDF-1.7\nsame");
        var quarantine = new FakeQuarantineStore();
        var catalog = new FakeFileCatalog();
        var service = NewUploadService(quarantine, catalog);
        var request = new SpaceFileUploadRequest(
            SpaceSourceType.Pdf,
            "same.pdf",
            "application/pdf");

        var first = await service.UploadAsync(request, new MemoryStream(payload));
        var second = await service.UploadAsync(request, new MemoryStream(payload));

        Assert.False(first.Reused);
        Assert.True(second.Reused);
        Assert.Same(first.File, second.File);
        Assert.Single(catalog.Files);
        Assert.Equal(1, catalog.SaveCount);
        Assert.Equal(1, quarantine.Sessions[0].CommitCount);
        Assert.Equal(1, quarantine.Sessions[1].AbortCount);
    }

    [Fact]
    public async Task Upload_stops_while_reading_when_the_tenant_limit_is_exceeded()
    {
        var payload = Encoding.ASCII.GetBytes("%PDF-1.7\nthis content is too large");
        var input = new TrackingReadStream(payload, maxChunkBytes: 4);
        var quarantine = new FakeQuarantineStore();
        var catalog = new FakeFileCatalog();
        var service = NewUploadService(
            quarantine,
            catalog,
            new SpaceFileUploadLimits
            {
                PlatformMaxBytes = 8,
                TenantMaxBytes = 8,
                ExcelMaxBytes = 8,
            });

        var error = await Assert.ThrowsAsync<SpaceFileValidationException>(
            () => service.UploadAsync(
                new SpaceFileUploadRequest(
                    SpaceSourceType.Pdf,
                    "large.pdf",
                    "application/pdf"),
                input));

        Assert.Equal(SpaceErrorCodes.FileTooLarge, error.Code);
        Assert.True(input.BytesRead < payload.Length);
        Assert.Equal(1, quarantine.Sessions.Single().AbortCount);
        Assert.Empty(catalog.Files);
    }

    [Theory]
    [InlineData("floor.png", "application/pdf")]
    [InlineData("floor.pdf", "image/png")]
    public async Task Upload_rejects_extension_or_declared_mime_mismatch(
        string name,
        string declaredContentType)
    {
        var quarantine = new FakeQuarantineStore();
        var service = NewUploadService(quarantine, new FakeFileCatalog());

        var error = await Assert.ThrowsAsync<SpaceFileValidationException>(
            () => service.UploadAsync(
                new SpaceFileUploadRequest(
                    SpaceSourceType.Pdf,
                    name,
                    declaredContentType),
                new MemoryStream("%PDF-1.7"u8.ToArray())));

        Assert.Equal(SpaceErrorCodes.FileTypeMismatch, error.Code);
        Assert.Empty(quarantine.Sessions);
    }

    [Fact]
    public async Task Upload_rejects_magic_number_mismatch_after_quarantine_write()
    {
        var quarantine = new FakeQuarantineStore();
        var service = NewUploadService(quarantine, new FakeFileCatalog());

        var error = await Assert.ThrowsAsync<SpaceFileValidationException>(
            () => service.UploadAsync(
                new SpaceFileUploadRequest(
                    SpaceSourceType.Pdf,
                    "fake.pdf",
                    "application/pdf"),
                new MemoryStream("not a pdf"u8.ToArray())));

        Assert.Equal(SpaceErrorCodes.FileTypeMismatch, error.Code);
        Assert.Equal(1, quarantine.Sessions.Single().AbortCount);
    }

    [Fact]
    public void Source_coordinator_requires_clean_same_tenant_file_and_touches_version()
    {
        var version = SpaceModelVersion.CreateDraft(
            TenantId,
            Guid.NewGuid(),
            1,
            "Draft");
        var file = NewCleanFile(TenantId, ".pdf", SpaceFileRetentionClass.Source);
        var coordinator = new SpaceSourceCoordinator(
            new TestExecutionContext(TenantId, ActorId));

        var source = coordinator.AddFileSource(
            version,
            file,
            SpaceSourceType.Pdf,
            "Floor plan");

        Assert.Equal(version.Id, source.ModelVersionId);
        Assert.Equal(file.Id, source.FileId);
        Assert.Equal(file.Sha256, source.Sha256);
        Assert.Equal(SpaceSourceState.Ready, source.State);
        Assert.Equal(1, version.ContentRevision);
    }

    [Fact]
    public void Source_coordinator_rejects_quarantined_cross_tenant_and_wrong_type_files()
    {
        var version = SpaceModelVersion.CreateDraft(
            TenantId,
            Guid.NewGuid(),
            1,
            "Draft");
        var coordinator = new SpaceSourceCoordinator(
            new TestExecutionContext(TenantId, ActorId));
        var quarantined = NewQuarantinedFile(
            TenantId,
            ".pdf",
            SpaceFileRetentionClass.Source);

        var unsafeError = Assert.Throws<SpaceFileValidationException>(() =>
            coordinator.AddFileSource(
                version,
                quarantined,
                SpaceSourceType.Pdf,
                "Unsafe"));
        Assert.Equal(SpaceErrorCodes.SourceUnsafe, unsafeError.Code);

        var otherTenant = NewCleanFile(
            Guid.NewGuid(),
            ".pdf",
            SpaceFileRetentionClass.Source);
        Assert.Throws<SpaceTenantScopeException>(() =>
            coordinator.AddFileSource(
                version,
                otherTenant,
                SpaceSourceType.Pdf,
                "Forbidden"));

        var png = NewCleanFile(
            TenantId,
            ".png",
            SpaceFileRetentionClass.Source);
        Assert.Throws<SpaceFileStateException>(() =>
            coordinator.AddFileSource(
                version,
                png,
                SpaceSourceType.Pdf,
                "Wrong type"));
    }

    [Fact]
    public async Task Referenced_file_cannot_be_deleted()
    {
        var file = NewCleanFile(TenantId, ".pdf", SpaceFileRetentionClass.Source);
        var catalog = new FakeFileCatalog { ReferenceCount = 1 };
        var service = new SpaceFileLifecycleService(
            new TestExecutionContext(TenantId, ActorId),
            catalog);

        await Assert.ThrowsAsync<SpaceFileReferenceException>(
            () => service.DeleteAsync(file));

        Assert.False(file.IsDeleted);
        Assert.Equal(SpaceFileState.Clean, file.State);
        Assert.Equal(0, catalog.SaveCount);
    }

    [Fact]
    public void Artifact_records_source_version_and_artifact_file_lineage()
    {
        var version = SpaceModelVersion.CreateDraft(
            TenantId,
            Guid.NewGuid(),
            1,
            "Draft");
        var sourceFile = NewCleanFile(
            TenantId,
            ".pdf",
            SpaceFileRetentionClass.Source);
        var artifactFile = NewCleanFile(
            TenantId,
            ".png",
            SpaceFileRetentionClass.Artifact);
        var coordinator = new SpaceSourceCoordinator(
            new TestExecutionContext(TenantId, ActorId));
        var source = coordinator.AddFileSource(
            version,
            sourceFile,
            SpaceSourceType.Pdf,
            "Floor plan");

        var artifact = coordinator.AddArtifact(
            version,
            source,
            artifactFile,
            SpaceArtifactType.Thumbnail,
            "preview-v1");

        Assert.Equal(version.Id, artifact.ModelVersionId);
        Assert.Equal(source.Id, artifact.SourceId);
        Assert.Equal(artifactFile.Id, artifact.FileId);
        Assert.Equal(SpaceArtifactType.Thumbnail, artifact.ArtifactType);
    }

    private static SpaceFileUploadService NewUploadService(
        FakeQuarantineStore quarantine,
        FakeFileCatalog catalog,
        SpaceFileUploadLimits? limits = null) =>
        new(
            new TestExecutionContext(TenantId, ActorId),
            quarantine,
            catalog,
            limits);

    private static SpaceFile NewCleanFile(
        Guid tenantId,
        string extension,
        SpaceFileRetentionClass retentionClass)
    {
        var file = NewQuarantinedFile(tenantId, extension, retentionClass);
        file.BeginScanning();
        file.MarkClean("test-scanner", "signatures-v1");
        return file;
    }

    private static SpaceFile NewQuarantinedFile(
        Guid tenantId,
        string extension,
        SpaceFileRetentionClass retentionClass)
    {
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenantId,
            $"quarantine/{Guid.NewGuid():N}",
            $"input{extension}",
            ContentType(extension),
            retentionClass);
        file.CompleteQuarantine(
            ContentType(extension),
            extension,
            12,
            new string('a', 64));
        return file;
    }

    private static string ContentType(string extension) =>
        extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class FakeFileCatalog : ISpaceFileCatalog
    {
        public List<SpaceFile> Files { get; } = [];
        public int ReferenceCount { get; init; }
        public int SaveCount { get; private set; }

        public Task<SpaceFile?> FindReusableAsync(
            Guid tenantId,
            string sha256,
            SpaceFileRetentionClass retentionClass,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Files.SingleOrDefault(file =>
                    file.TenantId == tenantId &&
                    file.Sha256 == sha256 &&
                    file.RetentionClass == retentionClass));

        public Task<int> CountActiveReferencesAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferenceCount);

        public void Add(SpaceFile file)
        {
            Files.Add(file);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQuarantineStore : ISpaceQuarantineStore
    {
        public List<FakeWriteSession> Sessions { get; } = [];

        public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var session = new FakeWriteSession(
                $"quarantine/{tenantId:N}/{fileId:N}/{Guid.NewGuid():N}");
            Sessions.Add(session);
            return Task.FromResult<ISpaceQuarantineWriteSession>(session);
        }
    }

    private sealed class FakeWriteSession : ISpaceQuarantineWriteSession
    {
        private readonly MemoryStream _content = new();

        public FakeWriteSession(string storageKey)
        {
            StorageKey = storageKey;
        }

        public string StorageKey { get; }
        public Stream Content => _content;
        public byte[] Bytes => _content.ToArray();
        public int CommitCount { get; private set; }
        public int AbortCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            AbortCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _content.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maxChunkBytes;

        public TrackingReadStream(byte[] bytes, int maxChunkBytes = int.MaxValue)
        {
            _inner = new MemoryStream(bytes);
            _maxChunkBytes = maxChunkBytes;
        }

        public long BytesRead { get; private set; }
        public int MaxRequestedBytes { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxRequestedBytes = Math.Max(MaxRequestedBytes, count);
            var read = _inner.Read(buffer, offset, Math.Min(count, _maxChunkBytes));
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            MaxRequestedBytes = Math.Max(MaxRequestedBytes, buffer.Length);
            var count = Math.Min(buffer.Length, _maxChunkBytes);
            var read = _inner.Read(buffer.Span[..count]);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
