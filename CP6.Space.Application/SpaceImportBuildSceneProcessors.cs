using System.Collections.ObjectModel;
using System.Text.Json;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceJobStepExecution(
    SpaceJobLease Lease,
    int StepNo,
    string StepCode);

public sealed record SpaceJobStepOutput(
    string CheckpointJson,
    string OutputHash);

public interface ISpaceImportJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceBuildSceneJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceJobProcessor
{
    SpaceJobType JobType { get; }
    SpaceJobSubjectType SubjectType { get; }
    string ProcessorVersion { get; }
    IReadOnlyList<string> StepCodes { get; }

    Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceJobProcessorRunner
{
    Task<bool> RunNextAsync(
        SpaceJobType jobType,
        string workerId,
        CancellationToken cancellationToken = default);

    Task RunClaimedAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceJobProcessingException : Exception
{
    public SpaceJobProcessingException(
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError)
        : base(RequireText(sanitizedError, 1000, nameof(sanitizedError)))
    {
        if (!Enum.IsDefined(failureKind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        FailureKind = failureKind;
        ErrorCode = RequireText(errorCode, 100, nameof(errorCode));
        SanitizedError = Message;
    }

    public SpaceJobFailureKind FailureKind { get; }
    public string ErrorCode { get; }
    public string SanitizedError { get; }

    private static string RequireText(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"A value up to {maxLength} characters is required.",
                parameterName);
        }
        return normalized;
    }
}

public sealed class SpaceJobProcessorOptions
{
    public TimeSpan LeaseDuration { get; init; } =
        TimeSpan.FromSeconds(60);
    public TimeSpan HeartbeatInterval { get; init; } =
        TimeSpan.FromSeconds(20);
    public TimeSpan ImportTimeout { get; init; } =
        TimeSpan.FromMinutes(30);
    public TimeSpan BuildSceneTimeout { get; init; } =
        TimeSpan.FromMinutes(30);
    public TimeSpan ExcelPreviewTimeout { get; init; } =
        TimeSpan.FromMinutes(15);
    public TimeSpan CadParseTimeout { get; init; } =
        TimeSpan.FromMinutes(30);
    public TimeSpan ValidationTimeout { get; init; } =
        TimeSpan.FromMinutes(30);
    public TimeSpan ApplyGenerationTimeout { get; init; } =
        TimeSpan.FromMinutes(10);
    public TimeSpan AiRetentionCleanupTimeout { get; init; } =
        TimeSpan.FromMinutes(10);
    public TimeSpan PublishTimeout { get; init; } =
        TimeSpan.FromMinutes(30);
    public TimeSpan ReconcileTimeout { get; init; } =
        TimeSpan.FromMinutes(30);

    public void Validate()
    {
        if (LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (HeartbeatInterval <= TimeSpan.Zero ||
            HeartbeatInterval >= LeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HeartbeatInterval),
                "Heartbeat must be positive and shorter than the lease.");
        }
        if (ImportTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ImportTimeout));
        if (BuildSceneTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BuildSceneTimeout));
        }
        if (ExcelPreviewTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExcelPreviewTimeout));
        }
        if (CadParseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CadParseTimeout));
        }
        if (ValidationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ValidationTimeout));
        }
        if (ApplyGenerationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ApplyGenerationTimeout));
        }
        if (AiRetentionCleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AiRetentionCleanupTimeout));
        }
        if (PublishTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PublishTimeout));
        if (ReconcileTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReconcileTimeout));
    }

    internal TimeSpan TimeoutFor(SpaceJobType jobType) =>
        jobType switch
        {
            SpaceJobType.CadParse => CadParseTimeout,
            SpaceJobType.Validate => ValidationTimeout,
            SpaceJobType.ExcelPreview => ExcelPreviewTimeout,
            SpaceJobType.Import => ImportTimeout,
            SpaceJobType.BuildScene => BuildSceneTimeout,
            SpaceJobType.ApplyGeneration => ApplyGenerationTimeout,
            SpaceJobType.AiRetentionCleanup => AiRetentionCleanupTimeout,
            SpaceJobType.Publish => PublishTimeout,
            SpaceJobType.Reconcile => ReconcileTimeout,
            _ => throw new ArgumentOutOfRangeException(nameof(jobType)),
        };
}

public static class SpaceImportJobSteps
{
    public const string VerifySourceSafe = nameof(VerifySourceSafe);
    public const string ConvertCad = nameof(ConvertCad);
    public const string ParseCadIr = nameof(ParseCadIr);
    public const string BuildLayerAndBlockSummary =
        nameof(BuildLayerAndBlockSummary);
    public const string RunRuleRecognition = nameof(RunRuleRecognition);
    public const string PersistArtifacts = nameof(PersistArtifacts);

    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>(
        [
            VerifySourceSafe,
            ConvertCad,
            ParseCadIr,
            BuildLayerAndBlockSummary,
            RunRuleRecognition,
            PersistArtifacts,
        ]);
}

public static class SpaceBuildSceneJobSteps
{
    public const string LoadPinnedInputs = nameof(LoadPinnedInputs);
    public const string LoadLockedFacts = nameof(LoadLockedFacts);
    public const string EnforceTenantPolicyAndQuota =
        nameof(EnforceTenantPolicyAndQuota);
    public const string MinimizeStructuredFeatures =
        nameof(MinimizeStructuredFeatures);
    public const string InvokeProvider = nameof(InvokeProvider);
    public const string ValidateProviderOutput =
        nameof(ValidateProviderOutput);
    public const string FuseRulesAndAi = nameof(FuseRulesAndAi);
    public const string SynthesizeDeterministicGeometry =
        nameof(SynthesizeDeterministicGeometry);
    public const string ValidateProposalSet = nameof(ValidateProposalSet);
    public const string PersistProposalsAndIssues =
        nameof(PersistProposalsAndIssues);
    public const string RecordUsage = nameof(RecordUsage);
    public const string AwaitReview = nameof(AwaitReview);

    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>(
        [
            LoadPinnedInputs,
            LoadLockedFacts,
            EnforceTenantPolicyAndQuota,
            MinimizeStructuredFeatures,
            InvokeProvider,
            ValidateProviderOutput,
            FuseRulesAndAi,
            SynthesizeDeterministicGeometry,
            ValidateProposalSet,
            PersistProposalsAndIssues,
            RecordUsage,
            AwaitReview,
        ]);
}

public sealed class SpaceImportJobProcessor : ISpaceJobProcessor
{
    public const string Version = "space-import-v1";

    private readonly ISpaceImportJobStepExecutor _executor;

    public SpaceImportJobProcessor(ISpaceImportJobStepExecutor executor)
    {
        _executor = executor;
    }

    public SpaceJobType JobType => SpaceJobType.Import;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.ModelSource;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes => SpaceImportJobSteps.All;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(execution, cancellationToken);
}

public sealed class SpaceBuildSceneJobProcessor : ISpaceJobProcessor
{
    public const string Version = "space-build-scene-v1";

    private readonly ISpaceBuildSceneJobStepExecutor _executor;

    public SpaceBuildSceneJobProcessor(
        ISpaceBuildSceneJobStepExecutor executor)
    {
        _executor = executor;
    }

    public SpaceJobType JobType => SpaceJobType.BuildScene;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.ModelVersion;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes =>
        SpaceBuildSceneJobSteps.All;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(execution, cancellationToken);
}

public sealed class UnavailableSpaceImportJobStepExecutor
    : ISpaceImportJobStepExecutor
{
    public Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        throw Unavailable("Import");

    private static SpaceJobProcessingException Unavailable(string pipeline) =>
        new(
            SpaceJobFailureKind.Resource,
            SpaceErrorCodes.JobProcessorUnavailable,
            $"{pipeline} processing is not configured.");
}

public sealed class UnavailableSpaceBuildSceneJobStepExecutor
    : ISpaceBuildSceneJobStepExecutor
{
    public Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Resource,
            SpaceErrorCodes.JobProcessorUnavailable,
            "BuildScene processing is not configured.");
}

public sealed class SpaceJobProcessorRunner : ISpaceJobProcessorRunner
{
    private readonly ISpaceJobLeaseStore _store;
    private readonly IReadOnlyDictionary<SpaceJobType, ISpaceJobProcessor>
        _processors;
    private readonly SpaceJobProcessorOptions _options;

    public SpaceJobProcessorRunner(
        ISpaceJobLeaseStore store,
        IEnumerable<ISpaceJobProcessor> processors,
        SpaceJobProcessorOptions? options = null)
    {
        _store = store;
        ArgumentNullException.ThrowIfNull(processors);
        _processors = BuildRegistry(processors);
        _options = options ?? new SpaceJobProcessorOptions();
        _options.Validate();
    }

    public async Task<bool> RunNextAsync(
        SpaceJobType jobType,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var processor = RequireProcessor(jobType);
        var normalizedWorkerId = RequireWorkerId(workerId);
        var lease = await _store.TryClaimNextAsync(
            normalizedWorkerId,
            processor.ProcessorVersion,
            _options.LeaseDuration,
            [jobType],
            cancellationToken);
        if (lease is null)
            return false;

        await RunClaimedAsync(lease, cancellationToken);
        return true;
    }

    public async Task RunClaimedAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var processor = RequireProcessor(lease.JobType);
        if (lease.SubjectType != processor.SubjectType ||
            lease.SubjectId == Guid.Empty)
        {
            throw new SpaceJobLeaseLostException(
                "The claimed Job subject does not match its processor.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.TimeoutFor(lease.JobType));
        await ExecuteClaimedAsync(
            processor,
            lease,
            timeout.Token,
            cancellationToken);
    }

    private async Task ExecuteClaimedAsync(
        ISpaceJobProcessor processor,
        SpaceJobLease initialLease,
        CancellationToken executionToken,
        CancellationToken hostToken)
    {
        var lease = initialLease;
        Guid? runningStepId = null;
        try
        {
            for (var index = 0; index < processor.StepCodes.Count; index++)
            {
                lease = await _store.RenewAsync(
                    lease,
                    _options.LeaseDuration,
                    executionToken);
                if (lease.CancellationRequested)
                {
                    await _store.AcknowledgeCancellationAsync(
                        lease,
                        CancellationToken.None);
                    return;
                }

                var stepNo = checked(index + 1);
                var stepCode = processor.StepCodes[index];
                var reusable =
                    await _store.FindReusableCheckpointAsync(
                        lease,
                        stepCode,
                        executionToken);
                if (reusable is not null)
                {
                    lease = await _store.ReuseStepAsync(
                        lease,
                        stepNo,
                        stepCode,
                        reusable.CheckpointJson,
                        reusable.OutputHash,
                        executionToken);
                    lease = await _store.ReportProgressAsync(
                        lease,
                        Math.Max(lease.ProgressDone, stepNo),
                        processor.StepCodes.Count,
                        stepCode,
                        executionToken);
                    continue;
                }

                var started = await _store.StartStepAsync(
                    lease,
                    stepNo,
                    stepCode,
                    executionToken);
                lease = started.Lease;
                runningStepId = started.StepId;
                var execution = new SpaceJobStepExecution(
                    lease,
                    stepNo,
                    stepCode);
                var result = await ExecuteWithHeartbeatAsync(
                    processor,
                    execution,
                    lease,
                    executionToken);
                lease = result.Lease;
                if (result.CancellationRequested)
                {
                    lease = await _store.FailStepAsync(
                        lease,
                        started.StepId,
                        CancellationToken.None);
                    runningStepId = null;
                    await _store.AcknowledgeCancellationAsync(
                        lease,
                        CancellationToken.None);
                    return;
                }

                var output = result.Output ??
                    throw new SpaceJobProcessingException(
                        SpaceJobFailureKind.Bug,
                        SpaceErrorCodes.JobProcessorFailed,
                        "The Job processor returned no step output.");
                lease = await _store.CompleteStepAsync(
                    lease,
                    started.StepId,
                    output.CheckpointJson,
                    output.OutputHash,
                    executionToken);
                runningStepId = null;
                lease = await _store.ReportProgressAsync(
                    lease,
                    Math.Max(lease.ProgressDone, stepNo),
                    processor.StepCodes.Count,
                    stepCode,
                    executionToken);
            }

            lease = await _store.RenewAsync(
                lease,
                _options.LeaseDuration,
                executionToken);
            if (lease.CancellationRequested)
            {
                await _store.AcknowledgeCancellationAsync(
                    lease,
                    CancellationToken.None);
                return;
            }

            var summary = JsonSerializer.Serialize(
                new
                {
                    processorVersion = processor.ProcessorVersion,
                    stepCount = processor.StepCodes.Count,
                });
            await _store.CompleteJobAsync(
                lease,
                summary,
                executionToken);
        }
        catch (SpaceJobLeaseLostException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (hostToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (executionToken.IsCancellationRequested)
        {
            await FailAsync(
                lease,
                runningStepId,
                lease.JobType is SpaceJobType.Publish or SpaceJobType.Reconcile
                    ? SpaceJobFailureKind.Transient
                    : SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobTimeout,
                "The Space Job exceeded its processing timeout.");
        }
        catch (SpaceJobProcessingException exception)
        {
            await FailAsync(
                lease,
                runningStepId,
                exception.FailureKind,
                exception.ErrorCode,
                exception.SanitizedError);
        }
        catch
        {
            await FailAsync(
                lease,
                runningStepId,
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                "The Space Job processor failed.");
        }
    }

    private async Task<StepExecutionResult> ExecuteWithHeartbeatAsync(
        ISpaceJobProcessor processor,
        SpaceJobStepExecution execution,
        SpaceJobLease initialLease,
        CancellationToken executionToken)
    {
        var lease = initialLease;
        using var stepCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(executionToken);
        var executionTask = processor.ExecuteStepAsync(
            execution,
            stepCancellation.Token);
        try
        {
            while (!executionTask.IsCompleted)
            {
                var heartbeat = Task.Delay(
                    _options.HeartbeatInterval,
                    executionToken);
                var completed = await Task.WhenAny(
                    executionTask,
                    heartbeat);
                if (completed == executionTask)
                    break;

                await heartbeat;
                lease = await _store.RenewAsync(
                    lease,
                    _options.LeaseDuration,
                    executionToken);

                if (!lease.CancellationRequested)
                    continue;

                stepCancellation.Cancel();
                await ObserveCancellationAsync(executionTask);
                return new StepExecutionResult(
                    lease,
                    Output: null,
                    CancellationRequested: true);
            }

            return new StepExecutionResult(
                lease,
                await executionTask,
                CancellationRequested: false);
        }
        catch
        {
            stepCancellation.Cancel();
            await ObserveCancellationAsync(executionTask);
            throw;
        }
    }

    private async Task FailAsync(
        SpaceJobLease lease,
        Guid? runningStepId,
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError)
    {
        if (runningStepId.HasValue)
        {
            lease = await _store.FailStepAsync(
                lease,
                runningStepId.Value,
                CancellationToken.None);
        }
        await _store.FailJobAsync(
            lease,
            failureKind,
            errorCode,
            sanitizedError,
            cancellationToken: CancellationToken.None);
    }

    private ISpaceJobProcessor RequireProcessor(SpaceJobType jobType)
    {
        if (jobType is not (
                SpaceJobType.CadParse or
                SpaceJobType.ExcelPreview or
                SpaceJobType.Import or
                SpaceJobType.Validate or
                SpaceJobType.BuildScene or
                SpaceJobType.ApplyGeneration or
                SpaceJobType.AiRetentionCleanup or
                SpaceJobType.Publish or
                SpaceJobType.Reconcile) ||
            !_processors.TryGetValue(jobType, out var processor))
        {
            throw new InvalidOperationException(
                $"Space Job type {jobType} has no registered processor.");
        }
        return processor;
    }

    private static IReadOnlyDictionary<
        SpaceJobType,
        ISpaceJobProcessor> BuildRegistry(
        IEnumerable<ISpaceJobProcessor> processors)
    {
        var registry = new Dictionary<
            SpaceJobType,
            ISpaceJobProcessor>();
        foreach (var processor in processors)
        {
            ArgumentNullException.ThrowIfNull(processor);
            if (!registry.TryAdd(processor.JobType, processor))
            {
                throw new InvalidOperationException(
                    $"Space Job type {processor.JobType} has multiple processors.");
            }
            if (processor.StepCodes.Count == 0 ||
                processor.StepCodes.Any(string.IsNullOrWhiteSpace) ||
                processor.StepCodes.Distinct(StringComparer.Ordinal).Count() !=
                processor.StepCodes.Count)
            {
                throw new InvalidOperationException(
                    $"Space Job processor {processor.JobType} has invalid steps.");
            }
        }
        return registry;
    }

    private static string RequireWorkerId(string workerId)
    {
        var normalized = workerId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 200)
        {
            throw new ArgumentException(
                "A Worker ID up to 200 characters is required.",
                nameof(workerId));
        }
        return normalized;
    }

    private static async Task ObserveCancellationAsync(
        Task<SpaceJobStepOutput> executionTask)
    {
        try
        {
            await executionTask;
        }
        catch
        {
            // The lease/cancellation decision is authoritative.
        }
    }

    private sealed record StepExecutionResult(
        SpaceJobLease Lease,
        SpaceJobStepOutput? Output,
        bool CancellationRequested);
}
