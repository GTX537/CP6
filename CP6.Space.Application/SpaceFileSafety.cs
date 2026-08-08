using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceWorkerSandboxPolicy(
    bool OutboundNetworkDisabled,
    bool InputReadOnly,
    bool OutputSeparated,
    bool KillProcessTreeOnTimeout,
    bool DeleteWorkspaceOnExit,
    bool UseShortLivedObjectCredentials,
    TimeSpan CpuTimeLimit,
    long MemoryLimitBytes,
    int ProcessLimit,
    long TemporaryDiskLimitBytes,
    TimeSpan WallClockLimit)
{
    public static SpaceWorkerSandboxPolicy FileSafetyDefault { get; } =
        new(
            OutboundNetworkDisabled: true,
            InputReadOnly: true,
            OutputSeparated: true,
            KillProcessTreeOnTimeout: true,
            DeleteWorkspaceOnExit: true,
            UseShortLivedObjectCredentials: true,
            CpuTimeLimit: TimeSpan.FromMinutes(2),
            MemoryLimitBytes: 512L * 1024 * 1024,
            ProcessLimit: 4,
            TemporaryDiskLimitBytes: 512L * 1024 * 1024,
            WallClockLimit: TimeSpan.FromMinutes(5));

    public void Validate()
    {
        if (!OutboundNetworkDisabled ||
            !InputReadOnly ||
            !OutputSeparated ||
            !KillProcessTreeOnTimeout ||
            !DeleteWorkspaceOnExit ||
            !UseShortLivedObjectCredentials)
        {
            throw new InvalidOperationException(
                "The file-safety sandbox cannot weaken the frozen isolation controls.");
        }
        if (CpuTimeLimit <= TimeSpan.Zero ||
            MemoryLimitBytes <= 0 ||
            ProcessLimit <= 0 ||
            TemporaryDiskLimitBytes <= 0 ||
            WallClockLimit <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The file-safety sandbox resource limits must be positive.");
        }
    }
}

public sealed record FileScanRequest(
    Guid TenantId,
    Guid FileId,
    Guid AttemptId,
    Guid WorkspaceId,
    string StorageKey,
    string OriginalName,
    string DetectedContentType,
    string Extension,
    long SizeBytes,
    string Sha256,
    SpaceWorkerSandboxPolicy Sandbox)
{
    public void Validate()
    {
        if (TenantId == Guid.Empty ||
            FileId == Guid.Empty ||
            AttemptId == Guid.Empty ||
            WorkspaceId == Guid.Empty)
        {
            throw new ArgumentException(
                "The scan requires tenant, file, attempt, and workspace identities.");
        }
        if (string.IsNullOrWhiteSpace(StorageKey) ||
            string.IsNullOrWhiteSpace(OriginalName) ||
            string.IsNullOrWhiteSpace(DetectedContentType) ||
            string.IsNullOrWhiteSpace(Extension))
        {
            throw new ArgumentException(
                "The scan requires server-owned file metadata.");
        }
        if (SizeBytes < 0 ||
            string.IsNullOrEmpty(Sha256) ||
            Sha256.Length != 64 ||
            !Sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "The scan requires a valid size and SHA-256 hash.");
        }
        Sandbox.Validate();
    }
}

public enum FileSafetyDisposition
{
    Safe,
    Rejected,
    Deferred,
}

public sealed record FileSafetyResult(
    FileSafetyDisposition Disposition,
    string ResultCode,
    string ScanEngine,
    string SignatureVersion,
    SpaceJobFailureKind? FailureKind,
    string SanitizedSummary)
{
    public static FileSafetyResult Clean(
        string engine,
        string signatureVersion,
        string resultCode = "CLEAN") =>
        new(
            FileSafetyDisposition.Safe,
            resultCode,
            engine,
            signatureVersion,
            null,
            "The file passed safety scanning.");

    public static FileSafetyResult Reject(
        string resultCode,
        SpaceJobFailureKind failureKind,
        string engine,
        string signatureVersion,
        string sanitizedSummary)
    {
        if (failureKind is not (
                SpaceJobFailureKind.Security or
                SpaceJobFailureKind.Input or
                SpaceJobFailureKind.Resource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                "A rejected file requires a terminal failure classification.");
        }

        return new FileSafetyResult(
            FileSafetyDisposition.Rejected,
            resultCode,
            engine,
            signatureVersion,
            failureKind,
            sanitizedSummary);
    }

    public static FileSafetyResult Defer(
        string engine,
        string signatureVersion,
        string sanitizedSummary,
        SpaceJobFailureKind failureKind = SpaceJobFailureKind.Transient)
    {
        if (failureKind is not (
                SpaceJobFailureKind.Transient or
                SpaceJobFailureKind.Bug))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                "A deferred scan must remain automatically retryable.");
        }

        return new FileSafetyResult(
            FileSafetyDisposition.Deferred,
            SpaceErrorCodes.FileQuarantined,
            engine,
            signatureVersion,
            failureKind,
            sanitizedSummary);
    }
}

public interface IFileSafetyScanner
{
    Task<FileSafetyResult> ScanAsync(
        FileScanRequest request,
        CancellationToken cancellationToken = default);
}

public enum SpaceMalwareDisposition
{
    Clean,
    Detected,
    Unavailable,
}

public sealed record SpaceMalwareScanResult(
    SpaceMalwareDisposition Disposition,
    string Engine,
    string SignatureVersion,
    string SanitizedSummary)
{
    public static SpaceMalwareScanResult Clean(
        string engine,
        string signatureVersion) =>
        new(
            SpaceMalwareDisposition.Clean,
            engine,
            signatureVersion,
            "No malware signature was detected.");

    public static SpaceMalwareScanResult Detected(
        string engine,
        string signatureVersion) =>
        new(
            SpaceMalwareDisposition.Detected,
            engine,
            signatureVersion,
            "The malware engine rejected the file.");

    public static SpaceMalwareScanResult Unavailable(
        string engine = "unconfigured",
        string signatureVersion = "unavailable") =>
        new(
            SpaceMalwareDisposition.Unavailable,
            engine,
            signatureVersion,
            "The malware engine is unavailable.");
}

public interface ISpaceMalwareScanner
{
    Task<SpaceMalwareScanResult> ScanAsync(
        FileScanRequest request,
        Stream content,
        CancellationToken cancellationToken = default);
}

public sealed class UnavailableSpaceMalwareScanner : ISpaceMalwareScanner
{
    public Task<SpaceMalwareScanResult> ScanAsync(
        FileScanRequest request,
        Stream content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SpaceMalwareScanResult.Unavailable());
}

public sealed record SpaceFileScanTarget(
    Guid TenantId,
    Guid FileId,
    string StorageKey,
    string OriginalName,
    string DetectedContentType,
    string Extension,
    long SizeBytes,
    string Sha256);

public interface ISpaceFileScanStateStore
{
    Task<SpaceFileScanTarget> BeginScanAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default);

    Task FinishScanAsync(
        SpaceJobLease lease,
        FileSafetyResult result,
        CancellationToken cancellationToken = default);
}

public interface ISpaceFileScanProcessor
{
    Task<FileSafetyResult> ProcessAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceFileScanProcessor : ISpaceFileScanProcessor
{
    public const string ProcessorVersion = "space-file-safety-v1";

    private readonly ISpaceFileScanStateStore _state;
    private readonly IFileSafetyScanner _scanner;
    private readonly SpaceWorkerSandboxPolicy _sandbox;

    public SpaceFileScanProcessor(
        ISpaceFileScanStateStore state,
        IFileSafetyScanner scanner,
        SpaceWorkerSandboxPolicy? sandbox = null)
    {
        _state = state;
        _scanner = scanner;
        _sandbox = sandbox ?? SpaceWorkerSandboxPolicy.FileSafetyDefault;
        _sandbox.Validate();
    }

    public async Task<FileSafetyResult> ProcessAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default)
    {
        if (lease.JobType != SpaceJobType.FileScan ||
            lease.SubjectType != SpaceJobSubjectType.File ||
            lease.SubjectId == Guid.Empty)
        {
            throw new SpaceJobLeaseLostException(
                "The lease is not a file-safety Job.");
        }

        var target = await _state.BeginScanAsync(lease, cancellationToken);
        var request = new FileScanRequest(
            target.TenantId,
            target.FileId,
            lease.AttemptId,
            Guid.NewGuid(),
            target.StorageKey,
            target.OriginalName,
            target.DetectedContentType,
            target.Extension,
            target.SizeBytes,
            target.Sha256,
            _sandbox);
        request.Validate();
        FileSafetyResult result;
        try
        {
            result = await _scanner.ScanAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = FileSafetyResult.Defer(
                "scanner-failure",
                "unknown",
                "The safety scanner failed; the file remains quarantined.",
                SpaceJobFailureKind.Bug);
        }
        await _state.FinishScanAsync(lease, result, cancellationToken);
        return result;
    }
}

public sealed class QuarantiningFileSafetyScanner : IFileSafetyScanner
{
    public Task<FileSafetyResult> ScanAsync(
        FileScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return Task.FromResult(
            FileSafetyResult.Defer(
                "unconfigured",
                "unavailable",
                "The malware scanner is unavailable; the file remains quarantined."));
    }
}
