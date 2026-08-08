using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceFileSafetyRetentionTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTime Now =
        new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Scan_processor_passes_the_frozen_sandbox_and_finishes_clean()
    {
        var state = new FakeScanStateStore();
        var scanner = new CapturingScanner(
            FileSafetyResult.Clean("clamav", "daily-20260726"));
        var processor = new SpaceFileScanProcessor(state, scanner);
        var lease = NewLease();

        var result = await processor.ProcessAsync(lease);

        Assert.Equal(FileSafetyDisposition.Safe, result.Disposition);
        Assert.NotNull(scanner.Request);
        Assert.True(scanner.Request!.Sandbox.OutboundNetworkDisabled);
        Assert.True(scanner.Request.Sandbox.InputReadOnly);
        Assert.True(scanner.Request.Sandbox.OutputSeparated);
        Assert.True(scanner.Request.Sandbox.KillProcessTreeOnTimeout);
        Assert.True(scanner.Request.Sandbox.DeleteWorkspaceOnExit);
        Assert.True(scanner.Request.Sandbox.UseShortLivedObjectCredentials);
        Assert.Equal(lease.AttemptId, scanner.Request.AttemptId);
        Assert.NotEqual(Guid.Empty, scanner.Request.WorkspaceId);
        Assert.Same(result, state.FinishedResult);
    }

    [Fact]
    public async Task Unconfigured_scanner_fails_closed_and_keeps_file_quarantined()
    {
        var scanner = new QuarantiningFileSafetyScanner();
        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(FileSafetyDisposition.Deferred, result.Disposition);
        Assert.Equal(SpaceErrorCodes.FileQuarantined, result.ResultCode);
        Assert.Equal(SpaceJobFailureKind.Transient, result.FailureKind);
    }

    [Fact]
    public async Task Scanner_crash_is_sanitized_and_returns_to_quarantine()
    {
        var state = new FakeScanStateStore();
        var processor = new SpaceFileScanProcessor(
            state,
            new ThrowingScanner());

        var result = await processor.ProcessAsync(NewLease());

        Assert.Equal(FileSafetyDisposition.Deferred, result.Disposition);
        Assert.Equal(SpaceJobFailureKind.Bug, result.FailureKind);
        Assert.Equal(SpaceErrorCodes.FileQuarantined, result.ResultCode);
        Assert.DoesNotContain(
            "secret",
            result.SanitizedSummary,
            StringComparison.OrdinalIgnoreCase);
        Assert.Same(result, state.FinishedResult);
    }

    [Fact]
    public void Sandbox_policy_rejects_any_weakened_isolation_control()
    {
        var weakened = SpaceWorkerSandboxPolicy.FileSafetyDefault with
        {
            OutboundNetworkDisabled = false,
        };

        Assert.Throws<InvalidOperationException>(() => weakened.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new SpaceFileScanProcessor(
                new FakeScanStateStore(),
                new CapturingScanner(
                    FileSafetyResult.Clean("scanner", "v1")),
                weakened));
    }

    [Fact]
    public void Retention_policy_is_tenant_configurable_by_file_class()
    {
        var options = new SpaceFileRetentionOptions
        {
            SourceRetention = TimeSpan.FromDays(365),
            ArtifactRetention = TimeSpan.FromDays(7),
            TemporaryRetention = TimeSpan.FromHours(6),
        };

        Assert.Equal(
            Now.AddDays(365),
            options.GetRetainUntilUtc(
                SpaceFileRetentionClass.Source,
                Now));
        Assert.Equal(
            Now.AddDays(7),
            options.GetRetainUntilUtc(
                SpaceFileRetentionClass.Artifact,
                Now));
        Assert.Equal(
            Now.AddHours(6),
            options.GetRetainUntilUtc(
                SpaceFileRetentionClass.Temporary,
                Now));
        Assert.Null(
            new SpaceFileRetentionOptions().GetRetainUntilUtc(
                SpaceFileRetentionClass.Source,
                Now));
    }

    [Fact]
    public void File_scan_can_defer_but_a_referenced_file_cannot_be_tombstoned()
    {
        var file = NewQuarantinedFile();
        file.BeginScanning();
        file.DeferScan(
            SpaceErrorCodes.FileQuarantined,
            "clamav",
            "daily");
        Assert.Equal(SpaceFileState.Quarantined, file.State);
        Assert.Equal(SpaceErrorCodes.FileQuarantined, file.ScanResultCode);

        Assert.Throws<SpaceFileReferenceException>(
            () => file.RequestDeletion(1, Now));
        Assert.False(file.IsDeleted);
    }

    [Fact]
    public async Task Cleanup_skips_references_and_retries_failed_object_deletion()
    {
        var expiredA = Guid.NewGuid();
        var expiredB = Guid.NewGuid();
        var candidate = new SpaceFileDeletionCandidate(
            TenantId,
            expiredB,
            "quarantine/b");
        var retention = new FakeRetentionStore
        {
            ExpiredIds = [expiredA, expiredB],
            Tombstones =
            {
                [expiredA] = new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.Referenced),
                [expiredB] = new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.Tombstoned,
                    candidate),
            },
            Pending = [candidate],
        };
        var files = new FakeFileStore { FailDeletes = true };
        var service = NewCleanupService(retention, files);

        var failed = await service.RunAsync();

        Assert.Equal(2, failed.ExpiredCandidates);
        Assert.Equal(1, failed.Tombstoned);
        Assert.Equal(1, failed.ReferencedSkipped);
        Assert.Equal(0, failed.ObjectsDeleted);
        Assert.Equal(1, failed.ObjectDeleteFailures);
        Assert.Empty(retention.ContentDeleted);

        files.FailDeletes = false;
        retention.ExpiredIds = [];
        var retried = await service.RunAsync();

        Assert.Equal(1, retried.ObjectsDeleted);
        Assert.Contains(expiredB, retention.ContentDeleted);
    }

    [Fact]
    public async Task Cleanup_requires_service_principal_and_rejects_cross_tenant_rows()
    {
        var unauthorized = new SpaceFileRetentionCleanupService(
            new TestExecutionContext(TenantId, Guid.NewGuid()),
            new CleanupAuthorization(false),
            new FakeRetentionStore(),
            new FakeFileStore(),
            new TestClock());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => unauthorized.RunAsync());

        var retention = new FakeRetentionStore
        {
            Pending =
            [
                new SpaceFileDeletionCandidate(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "quarantine/other"),
            ],
        };
        var service = NewCleanupService(retention, new FakeFileStore());
        await Assert.ThrowsAsync<SpaceTenantScopeException>(
            () => service.RunAsync());
    }

    private static SpaceFileRetentionCleanupService NewCleanupService(
        FakeRetentionStore retention,
        FakeFileStore files) =>
        new(
            new TestExecutionContext(TenantId, Guid.NewGuid()),
            new CleanupAuthorization(true),
            retention,
            files,
            new TestClock());

    private static SpaceJobLease NewLease()
    {
        var fileId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        return new SpaceJobLease(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "worker-1",
            SpaceJobType.FileScan,
            SpaceJobSubjectType.File,
            fileId,
            new string('a', 64),
            Now.AddMinutes(1),
            []);
    }

    private static FileScanRequest NewRequest() =>
        new(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "quarantine/server-key",
            "floor.pdf",
            "application/pdf",
            ".pdf",
            42,
            new string('a', 64),
            SpaceWorkerSandboxPolicy.FileSafetyDefault);

    private static SpaceFile NewQuarantinedFile()
    {
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            TenantId,
            "quarantine/server-key",
            "floor.pdf",
            "application/pdf",
            SpaceFileRetentionClass.Source,
            Now.AddDays(30));
        file.CompleteQuarantine(
            "application/pdf",
            ".pdf",
            42,
            new string('a', 64));
        return file;
    }

    private sealed class FakeScanStateStore : ISpaceFileScanStateStore
    {
        public FileSafetyResult? FinishedResult { get; private set; }

        public Task<SpaceFileScanTarget> BeginScanAsync(
            SpaceJobLease lease,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new SpaceFileScanTarget(
                    lease.TenantId,
                    lease.SubjectId,
                    "quarantine/server-key",
                    "floor.pdf",
                    "application/pdf",
                    ".pdf",
                    42,
                    lease.InputHash));

        public Task FinishScanAsync(
            SpaceJobLease lease,
            FileSafetyResult result,
            CancellationToken cancellationToken = default)
        {
            FinishedResult = result;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingScanner : IFileSafetyScanner
    {
        private readonly FileSafetyResult _result;

        public CapturingScanner(FileSafetyResult result)
        {
            _result = result;
        }

        public FileScanRequest? Request { get; private set; }

        public Task<FileSafetyResult> ScanAsync(
            FileScanRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingScanner : IFileSafetyScanner
    {
        public Task<FileSafetyResult> ScanAsync(
            FileScanRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret scanner detail");
    }

    private sealed class FakeRetentionStore : ISpaceFileRetentionStore
    {
        public IReadOnlyList<Guid> ExpiredIds { get; set; } = [];
        public IReadOnlyList<SpaceFileDeletionCandidate> Pending { get; init; } = [];
        public Dictionary<Guid, SpaceFileTombstoneResult> Tombstones { get; } = [];
        public HashSet<Guid> ContentDeleted { get; } = [];

        public Task<IReadOnlyList<Guid>> FindExpiredFileIdsAsync(
            Guid tenantId,
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExpiredIds);

        public Task<IReadOnlyList<SpaceFileDeletionCandidate>>
            FindPendingContentDeletionAsync(
                Guid tenantId,
                int batchSize,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending);

        public Task<SpaceFileTombstoneResult> TryTombstoneAsync(
            Guid tenantId,
            Guid fileId,
            DateTime nowUtc,
            bool requireExpired,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Tombstones.TryGetValue(fileId, out var result)
                    ? result
                    : new SpaceFileTombstoneResult(
                        SpaceFileTombstoneStatus.NotFound));

        public Task MarkContentDeletedAsync(
            SpaceFileDeletionCandidate candidate,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            ContentDeleted.Add(candidate.FileId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileStore : ISpaceFileStore
    {
        public bool FailDeletes { get; set; }

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            FailDeletes
                ? Task.FromException(new IOException("object store unavailable"))
                : Task.CompletedTask;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed record CleanupAuthorization(bool IsRetentionServicePrincipal)
        : ISpaceFileCleanupAuthorization;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
