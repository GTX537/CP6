using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceJobProcessorTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now =
        new(2026, 7, 30, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Frozen_step_catalogs_match_the_import_build_and_publish_design()
    {
        Assert.Equal(
            [
                "VerifySourceSafe",
                "ConvertCad",
                "ParseCadIr",
                "BuildLayerAndBlockSummary",
                "RunRuleRecognition",
                "PersistArtifacts",
            ],
            SpaceImportJobSteps.All);
        Assert.Equal(
            [
                "LoadPinnedInputs",
                "LoadLockedFacts",
                "EnforceTenantPolicyAndQuota",
                "MinimizeStructuredFeatures",
                "InvokeProvider",
                "ValidateProviderOutput",
                "FuseRulesAndAi",
                "SynthesizeDeterministicGeometry",
                "ValidateProposalSet",
                "PersistProposalsAndIssues",
                "RecordUsage",
                "AwaitReview",
            ],
            SpaceBuildSceneJobSteps.All);
        Assert.Equal(
            [SpacePublishJobSteps.ExecutePublishSaga],
            new SpacePublishJobProcessor(new RecordingExecutor()).StepCodes);
        Assert.Equal(
            [SpacePublishJobSteps.ReconcilePublishSaga],
            new SpacePublishReconciliationJobProcessor(
                new RecordingExecutor()).StepCodes);
    }

    [Theory]
    [InlineData(SpaceJobType.Publish, "ExecutePublishSaga")]
    [InlineData(SpaceJobType.Reconcile, "ReconcilePublishSaga")]
    public async Task Publish_runners_complete_the_single_resumable_saga_step(
        SpaceJobType jobType,
        string expectedStep)
    {
        var store = new FakeLeaseStore(
            Lease(jobType, SpaceJobSubjectType.PublishAttempt));
        var executor = new RecordingExecutor();

        var ran = await Runner(store, executor).RunNextAsync(
            jobType,
            "worker-publish");

        Assert.True(ran);
        Assert.Equal([jobType], store.SupportedJobTypes);
        Assert.Equal([expectedStep], store.CompletedSteps);
        Assert.True(store.JobCompleted);
    }

    [Fact]
    public async Task Import_runner_claims_only_import_and_completes_six_steps()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource));
        var executor = new RecordingExecutor();
        var runner = Runner(store, executor);

        var ran = await runner.RunNextAsync(
            SpaceJobType.Import,
            "  worker-a  ");

        Assert.True(ran);
        Assert.Equal(
            [SpaceJobType.Import],
            store.SupportedJobTypes);
        Assert.Equal(SpaceImportJobProcessor.Version, store.ProcessorVersion);
        Assert.Equal(SpaceImportJobSteps.All, executor.StepCodes);
        Assert.Equal(SpaceImportJobSteps.All, store.StartedSteps);
        Assert.Equal(SpaceImportJobSteps.All, store.CompletedSteps);
        Assert.Equal(6, store.Progress.Count);
        Assert.True(store.JobCompleted);
        Assert.False(store.JobFailed);
        Assert.Contains(
            SpaceImportJobProcessor.Version,
            store.ResultSummaryJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_scene_runner_completes_all_twelve_steps()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion));
        var executor = new RecordingExecutor();
        var runner = Runner(store, executor);

        await runner.RunNextAsync(
            SpaceJobType.BuildScene,
            "worker-b");

        Assert.Equal(SpaceBuildSceneJobSteps.All, executor.StepCodes);
        Assert.Equal(SpaceBuildSceneJobSteps.All, store.CompletedSteps);
        Assert.Equal(12, store.Progress.Count);
        Assert.True(store.JobCompleted);
    }

    [Fact]
    public async Task Cad_parse_runner_completes_frozen_artifact_and_finalize_steps()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.CadParse,
                SpaceJobSubjectType.ModelSource));
        var executor = new RecordingExecutor();

        await Runner(store, executor).RunNextAsync(
            SpaceJobType.CadParse,
            "worker-cad");

        Assert.Equal(
            [
                SpaceCadParseJobProcessor.GenerateArtifacts,
                SpaceCadParseJobProcessor.FinalizePreview,
            ],
            executor.StepCodes);
        Assert.Equal(executor.StepCodes, store.CompletedSteps);
        Assert.True(store.JobCompleted);
    }

    [Fact]
    public async Task Matching_checkpoint_is_audited_as_reused_without_execution()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource));
        store.Reusable[SpaceImportJobSteps.VerifySourceSafe] =
            new SpaceReusableCheckpoint(
                Guid.NewGuid(),
                Guid.NewGuid(),
                SpaceImportJobSteps.VerifySourceSafe,
                """{"artifact":"safe-source"}""",
                new string('a', 64));
        var executor = new RecordingExecutor();
        var runner = Runner(store, executor);

        await runner.RunNextAsync(
            SpaceJobType.Import,
            "worker-a");

        Assert.Equal(
            [SpaceImportJobSteps.VerifySourceSafe],
            store.ReusedSteps);
        Assert.DoesNotContain(
            SpaceImportJobSteps.VerifySourceSafe,
            executor.StepCodes);
        Assert.Equal(5, executor.StepCodes.Count);
        Assert.True(store.JobCompleted);
    }

    [Fact]
    public async Task Typed_failure_preserves_safe_classification_and_fails_step()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource));
        var executor = new RecordingExecutor
        {
            Handler = (_, _) =>
                throw new SpaceJobProcessingException(
                    SpaceJobFailureKind.Input,
                    SpaceErrorCodes.ParseFailed,
                    "The normalized CAD input is invalid."),
        };

        await Runner(store, executor).RunNextAsync(
            SpaceJobType.Import,
            "worker-a");

        Assert.True(store.JobFailed);
        Assert.Equal(
            SpaceJobFailureKind.Input,
            store.FailureKind);
        Assert.Equal(SpaceErrorCodes.ParseFailed, store.ErrorCode);
        Assert.Equal(
            "The normalized CAD input is invalid.",
            store.SanitizedError);
        Assert.Equal(
            [SpaceImportJobSteps.VerifySourceSafe],
            store.FailedSteps);
        Assert.False(store.JobCompleted);
    }

    [Fact]
    public async Task Unexpected_failure_never_persists_exception_details()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion));
        var executor = new RecordingExecutor
        {
            Handler = (_, _) =>
                throw new InvalidOperationException(
                    "secret-key=do-not-persist"),
        };

        await Runner(store, executor).RunNextAsync(
            SpaceJobType.BuildScene,
            "worker-a");

        Assert.Equal(
            SpaceJobFailureKind.Bug,
            store.FailureKind);
        Assert.Equal(
            SpaceErrorCodes.JobProcessorFailed,
            store.ErrorCode);
        Assert.Equal(
            "The Space Job processor failed.",
            store.SanitizedError);
        Assert.DoesNotContain(
            "secret-key",
            store.SanitizedError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_executor_fails_closed_without_downstream_work()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource));
        var processor = new SpaceImportJobProcessor(
            new UnavailableSpaceImportJobStepExecutor());
        var runner = new SpaceJobProcessorRunner(
            store,
            [processor],
            Options());

        await runner.RunNextAsync(
            SpaceJobType.Import,
            "worker-a");

        Assert.Equal(
            SpaceJobFailureKind.Resource,
            store.FailureKind);
        Assert.Equal(
            SpaceErrorCodes.JobProcessorUnavailable,
            store.ErrorCode);
        Assert.False(store.JobCompleted);
    }

    [Fact]
    public async Task Heartbeat_observes_job_cancellation_and_stops_step()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource))
        {
            CancelOnRenewNumber = 2,
        };
        var executor = new RecordingExecutor
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Output("never");
            },
        };

        await Runner(
                store,
                executor,
                Options(
                    heartbeat: TimeSpan.FromMilliseconds(10),
                    lease: TimeSpan.FromMilliseconds(100)))
            .RunNextAsync(SpaceJobType.Import, "worker-a");

        Assert.True(store.CancellationAcknowledged);
        Assert.Equal(
            [SpaceImportJobSteps.VerifySourceSafe],
            store.FailedSteps);
        Assert.False(store.JobFailed);
        Assert.False(store.JobCompleted);
    }

    [Fact]
    public async Task Lease_loss_cancels_inflight_step_without_terminal_write()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource))
        {
            LoseOnRenewNumber = 2,
        };
        var executor = new RecordingExecutor
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Output("never");
            },
        };

        await Assert.ThrowsAsync<SpaceJobLeaseLostException>(
            () => Runner(
                    store,
                    executor,
                    Options(
                        heartbeat: TimeSpan.FromMilliseconds(10),
                        lease: TimeSpan.FromMilliseconds(100)))
                .RunNextAsync(SpaceJobType.Import, "worker-a"));

        Assert.False(store.JobFailed);
        Assert.False(store.JobCompleted);
        Assert.False(store.CancellationAcknowledged);
    }

    [Fact]
    public async Task Host_shutdown_leaves_job_for_lease_takeover()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelSource));
        var executor = new RecordingExecutor
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Output("never");
            },
        };
        using var shutdown = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner(
                    store,
                    executor,
                    Options(
                        heartbeat: TimeSpan.FromMilliseconds(100),
                        lease: TimeSpan.FromMilliseconds(200)))
                .RunNextAsync(
                    SpaceJobType.Import,
                    "worker-a",
                    shutdown.Token));

        Assert.False(store.JobFailed);
        Assert.False(store.JobCompleted);
    }

    [Fact]
    public async Task Hard_timeout_is_a_resource_failure_with_no_raw_details()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion));
        var executor = new RecordingExecutor
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Output("never");
            },
        };

        await Runner(
                store,
                executor,
                Options(
                    heartbeat: TimeSpan.FromMilliseconds(100),
                    lease: TimeSpan.FromMilliseconds(200),
                    buildSceneTimeout: TimeSpan.FromMilliseconds(20)))
            .RunNextAsync(SpaceJobType.BuildScene, "worker-a");

        Assert.Equal(
            SpaceJobFailureKind.Resource,
            store.FailureKind);
        Assert.Equal(SpaceErrorCodes.JobTimeout, store.ErrorCode);
        Assert.Equal(
            [SpaceBuildSceneJobSteps.LoadPinnedInputs],
            store.FailedSteps);
    }

    [Fact]
    public async Task Publish_timeout_is_transient_so_the_job_can_retry()
    {
        var store = new FakeLeaseStore(
            Lease(
                SpaceJobType.Publish,
                SpaceJobSubjectType.PublishAttempt));
        var executor = new RecordingExecutor
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Output("never");
            },
        };

        await Runner(
                store,
                executor,
                Options(
                    heartbeat: TimeSpan.FromMilliseconds(100),
                    lease: TimeSpan.FromMilliseconds(200),
                    publishTimeout: TimeSpan.FromMilliseconds(20)))
            .RunNextAsync(SpaceJobType.Publish, "worker-publish");

        Assert.Equal(SpaceJobFailureKind.Transient, store.FailureKind);
        Assert.Equal(SpaceErrorCodes.JobTimeout, store.ErrorCode);
        Assert.Equal(
            [SpacePublishJobSteps.ExecutePublishSaga],
            store.FailedSteps);
    }

    [Fact]
    public async Task Invalid_subject_or_unsupported_type_is_never_processed()
    {
        var invalidSubject = new FakeLeaseStore(
            Lease(
                SpaceJobType.Import,
                SpaceJobSubjectType.ModelVersion));
        var executor = new RecordingExecutor();

        await Assert.ThrowsAsync<SpaceJobLeaseLostException>(
            () => Runner(invalidSubject, executor).RunNextAsync(
                SpaceJobType.Import,
                "worker-a"));

        var unsupported = new FakeLeaseStore(
            Lease(
                SpaceJobType.Validate,
                SpaceJobSubjectType.ModelVersion));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner(unsupported, executor).RunNextAsync(
                SpaceJobType.Validate,
                "worker-a"));
        Assert.Equal(0, unsupported.ClaimCount);
    }

    [Fact]
    public void Duplicate_processor_or_invalid_heartbeat_is_rejected()
    {
        var executor = new RecordingExecutor();
        var duplicate = new ISpaceJobProcessor[]
        {
            new SpaceImportJobProcessor(executor),
            new SpaceImportJobProcessor(executor),
        };

        Assert.Throws<InvalidOperationException>(
            () => new SpaceJobProcessorRunner(
                new FakeLeaseStore(
                    Lease(
                        SpaceJobType.Import,
                        SpaceJobSubjectType.ModelSource)),
                duplicate,
                Options()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Options(
                heartbeat: TimeSpan.FromSeconds(60),
                lease: TimeSpan.FromSeconds(60)).Validate());
    }

    private static SpaceJobProcessorRunner Runner(
        FakeLeaseStore store,
        RecordingExecutor executor,
        SpaceJobProcessorOptions? options = null) =>
        new(
            store,
            [
                new SpaceCadParseJobProcessor(executor),
                new SpaceImportJobProcessor(executor),
                new SpaceBuildSceneJobProcessor(executor),
                new SpacePublishJobProcessor(executor),
                new SpacePublishReconciliationJobProcessor(executor),
            ],
            options ?? Options());

    private static SpaceJobProcessorOptions Options(
        TimeSpan? heartbeat = null,
        TimeSpan? lease = null,
        TimeSpan? importTimeout = null,
        TimeSpan? buildSceneTimeout = null,
        TimeSpan? publishTimeout = null) =>
        new()
        {
            HeartbeatInterval =
                heartbeat ?? TimeSpan.FromMilliseconds(100),
            LeaseDuration = lease ?? TimeSpan.FromSeconds(1),
            ImportTimeout = importTimeout ?? TimeSpan.FromSeconds(5),
            BuildSceneTimeout =
                buildSceneTimeout ?? TimeSpan.FromSeconds(5),
            PublishTimeout =
                publishTimeout ?? TimeSpan.FromSeconds(5),
        };

    private static SpaceJobLease Lease(
        SpaceJobType jobType,
        SpaceJobSubjectType subjectType) =>
        new(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "worker-a",
            jobType,
            subjectType,
            Guid.NewGuid(),
            new string('a', 64),
            Now.AddMinutes(1),
            []);

    private static SpaceJobStepOutput Output(string stepCode) =>
        new(
            JsonSerializer.Serialize(new { stepCode }),
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(stepCode)))
                .ToLowerInvariant());

    private sealed class RecordingExecutor
        : ISpaceImportJobStepExecutor,
           ISpaceBuildSceneJobStepExecutor,
           ISpaceCadParseJobStepExecutor,
           ISpacePublishJobExecutor
    {
        public Func<
            SpaceJobStepExecution,
            CancellationToken,
            Task<SpaceJobStepOutput>> Handler
        { get; init; } =
            (execution, _) =>
                Task.FromResult(Output(execution.StepCode));

        public List<string> StepCodes { get; } = [];

        public async Task<SpaceJobStepOutput> ExecuteAsync(
            SpaceJobStepExecution execution,
            CancellationToken cancellationToken = default)
        {
            StepCodes.Add(execution.StepCode);
            return await Handler(execution, cancellationToken);
        }
    }

    private sealed class FakeLeaseStore : ISpaceJobLeaseStore
    {
        private SpaceJobLease? _nextLease;
        private readonly Dictionary<Guid, string> _stepCodes = [];

        public FakeLeaseStore(SpaceJobLease nextLease)
        {
            _nextLease = nextLease;
        }

        public IReadOnlyCollection<SpaceJobType>? SupportedJobTypes
        {
            get;
            private set;
        }
        public string? ProcessorVersion { get; private set; }
        public int ClaimCount { get; private set; }
        public int RenewCount { get; private set; }
        public int? CancelOnRenewNumber { get; init; }
        public int? LoseOnRenewNumber { get; init; }
        public List<string> StartedSteps { get; } = [];
        public List<string> CompletedSteps { get; } = [];
        public List<string> ReusedSteps { get; } = [];
        public List<string> FailedSteps { get; } = [];
        public List<(long Done, long Total, string Stage)> Progress
        {
            get;
        } = [];
        public Dictionary<string, SpaceReusableCheckpoint> Reusable
        {
            get;
        } = new(StringComparer.Ordinal);
        public bool JobCompleted { get; private set; }
        public bool JobFailed { get; private set; }
        public bool CancellationAcknowledged { get; private set; }
        public string? ResultSummaryJson { get; private set; }
        public SpaceJobFailureKind? FailureKind { get; private set; }
        public string? ErrorCode { get; private set; }
        public string? SanitizedError { get; private set; }

        public Task<SpaceJobLease?> TryClaimNextAsync(
            string workerId,
            string processorVersion,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            ClaimCount++;
            ProcessorVersion = processorVersion;
            return TakeLeaseAsync();
        }

        public Task<SpaceJobLease?> TryClaimNextAsync(
            string workerId,
            string processorVersion,
            TimeSpan leaseDuration,
            IReadOnlyCollection<SpaceJobType> supportedJobTypes,
            CancellationToken cancellationToken = default)
        {
            ClaimCount++;
            ProcessorVersion = processorVersion;
            SupportedJobTypes = supportedJobTypes.ToArray();
            if (_nextLease is not null &&
                !supportedJobTypes.Contains(_nextLease.JobType))
            {
                return Task.FromResult<SpaceJobLease?>(null);
            }
            return TakeLeaseAsync();
        }

        public Task<SpaceJobLease> RenewAsync(
            SpaceJobLease lease,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenewCount++;
            if (LoseOnRenewNumber == RenewCount)
            {
                throw new SpaceJobLeaseLostException(
                    "The test lease was lost.");
            }
            return Task.FromResult(
                lease with
                {
                    LockExpiresAtUtc = Now.Add(leaseDuration),
                    CancellationRequested =
                        lease.CancellationRequested ||
                        CancelOnRenewNumber == RenewCount,
                });
        }

        public Task<SpaceJobLease> ReportProgressAsync(
            SpaceJobLease lease,
            long done,
            long total,
            string stage,
            CancellationToken cancellationToken = default)
        {
            Progress.Add((done, total, stage));
            return Task.FromResult(
                lease with
                {
                    ProgressDone = done,
                    ProgressTotal = total,
                });
        }

        public Task<SpaceJobStepStartResult> StartStepAsync(
            SpaceJobLease lease,
            int stepNo,
            string stepCode,
            CancellationToken cancellationToken = default)
        {
            var stepId = Guid.NewGuid();
            _stepCodes.Add(stepId, stepCode);
            StartedSteps.Add(stepCode);
            return Task.FromResult(
                new SpaceJobStepStartResult(stepId, lease));
        }

        public Task<SpaceJobLease> CompleteStepAsync(
            SpaceJobLease lease,
            Guid stepId,
            string checkpointJson,
            string outputHash,
            CancellationToken cancellationToken = default)
        {
            CompletedSteps.Add(_stepCodes[stepId]);
            return Task.FromResult(lease);
        }

        public Task<SpaceJobLease> ReuseStepAsync(
            SpaceJobLease lease,
            int stepNo,
            string stepCode,
            string checkpointJson,
            string outputHash,
            CancellationToken cancellationToken = default)
        {
            ReusedSteps.Add(stepCode);
            return Task.FromResult(lease);
        }

        public Task<SpaceJobLease> FailStepAsync(
            SpaceJobLease lease,
            Guid stepId,
            CancellationToken cancellationToken = default)
        {
            FailedSteps.Add(_stepCodes[stepId]);
            return Task.FromResult(lease);
        }

        public Task<SpaceReusableCheckpoint?>
            FindReusableCheckpointAsync(
                SpaceJobLease lease,
                string stepCode,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Reusable.TryGetValue(stepCode, out var checkpoint)
                    ? checkpoint
                    : null);

        public Task CompleteJobAsync(
            SpaceJobLease lease,
            string? resultSummaryJson = null,
            CancellationToken cancellationToken = default)
        {
            JobCompleted = true;
            ResultSummaryJson = resultSummaryJson;
            return Task.CompletedTask;
        }

        public Task FailJobAsync(
            SpaceJobLease lease,
            SpaceJobFailureKind failureKind,
            string errorCode,
            string sanitizedError,
            Guid? diagnosticArtifactId = null,
            string? resourceUsageJson = null,
            CancellationToken cancellationToken = default)
        {
            JobFailed = true;
            FailureKind = failureKind;
            ErrorCode = errorCode;
            SanitizedError = sanitizedError;
            return Task.CompletedTask;
        }

        public Task AcknowledgeCancellationAsync(
            SpaceJobLease lease,
            CancellationToken cancellationToken = default)
        {
            CancellationAcknowledged = true;
            return Task.CompletedTask;
        }

        private Task<SpaceJobLease?> TakeLeaseAsync()
        {
            var lease = _nextLease;
            _nextLease = null;
            return Task.FromResult(lease);
        }
    }
}
