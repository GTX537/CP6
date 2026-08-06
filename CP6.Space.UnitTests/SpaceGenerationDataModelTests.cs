using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceGenerationDataModelTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModelVersionId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SourceId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid JobId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TargetFloorLogicalId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTime Now =
        new(2026, 7, 30, 18, 0, 0, DateTimeKind.Utc);

    private static readonly string SourceHash = new('a', 64);
    private static readonly string IdempotencyHash = new('b', 64);
    private static readonly string BusinessHash = new('c', 64);
    private static readonly string ProviderRequestHash = new('d', 64);

    [Fact]
    public void Run_creation_pins_replay_inputs_and_starts_current()
    {
        var run = NewRun();

        Assert.Equal(TenantId, run.TenantId);
        Assert.Equal(SiteId, run.SiteId);
        Assert.Equal(ModelVersionId, run.ModelVersionId);
        Assert.Equal(SourceId, run.SourceId);
        Assert.Equal(SourceHash, run.SourceHash);
        Assert.Equal(7, run.BaseContentRevision);
        Assert.Equal(SpaceGenerationRunStatus.Queued, run.Status);
        Assert.Equal(0, run.Progress);
        Assert.True(run.IsCurrent);
        Assert.Equal(JobId, run.JobId);
        Assert.Equal(TargetFloorLogicalId, run.TargetFloorLogicalId);
    }

    [Fact]
    public void Run_happy_path_is_forward_only_and_records_provider_identity()
    {
        var run = NewRun();

        run.ReportProgress(5);
        run.BeginPreparing();
        run.ReportProgress(20);
        run.BeginInferring();
        run.RecordProviderResult("local-v1", "warehouse-v1", "1.0");
        run.ReportProgress(50);
        run.BeginValidating();
        run.MarkAwaitingReview();
        run.MarkReviewCompleted(Now);
        BeginApplying(run);
        run.RecordApplyPlan(new string('f', 64), Now.AddSeconds(1));
        run.MarkSucceeded(8, "{\"proposals\":1}");

        Assert.Equal(SpaceGenerationRunStatus.Succeeded, run.Status);
        Assert.Equal(100, run.Progress);
        Assert.Equal("local-v1", run.ProviderCode);
        Assert.Equal("warehouse-v1", run.ProviderModel);
        Assert.Equal("1.0", run.OutputSchemaVersion);
        Assert.Equal(Now, run.ReviewCompletedAtUtc);
        Assert.Equal(8, run.AppliedContentRevision);
        Assert.Equal("{\"proposals\":1}", run.AppliedCountsJson);
        Assert.False(run.IsCurrent);
        Assert.Throws<SpaceGenerationStateException>(
            () => run.ReportProgress(100));
    }

    [Fact]
    public void Run_rejects_invalid_transitions_and_backward_progress()
    {
        var run = NewRun();

        run.ReportProgress(10);

        Assert.Throws<SpaceGenerationStateException>(
            () => run.BeginInferring());
        Assert.Throws<SpaceGenerationStateException>(
            () => run.ReportProgress(9));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.ReportProgress(101));
    }

    [Fact]
    public void Failed_run_retries_as_the_same_run_and_business_key()
    {
        var run = NewRun();
        var runId = run.Id;

        run.BeginPreparing();
        run.ReportProgress(25);
        run.MarkFailed("PROVIDER_TIMEOUT", "Timed out.");
        Assert.Throws<SpaceGenerationStateException>(
            () => run.ReportProgress(30));
        run.Retry();

        Assert.Equal(runId, run.Id);
        Assert.Equal(BusinessHash, run.BusinessKeyHash);
        Assert.Equal(JobId, run.JobId);
        Assert.Equal(SpaceGenerationRunStatus.Queued, run.Status);
        Assert.Equal(25, run.Progress);
        Assert.Null(run.FailureCode);
        Assert.Null(run.FailureSummary);
        Assert.True(run.IsCurrent);
    }

    [Fact]
    public void Cancellation_can_wait_for_provider_and_then_complete()
    {
        var run = NewRun();
        run.BeginPreparing();
        run.BeginInferring();

        run.RequestCancellation(Now, providerResponsePending: true);

        Assert.True(run.CancelPending);
        Assert.Equal(Now, run.CancelRequestedAtUtc);
        run.CompleteCancellation(Now.AddSeconds(3));
        Assert.Equal(SpaceGenerationRunStatus.Cancelled, run.Status);
        Assert.False(run.CancelPending);
        Assert.False(run.IsCurrent);
    }

    [Theory]
    [InlineData(SpaceGenerationRunStatus.AwaitingReview)]
    [InlineData(SpaceGenerationRunStatus.Failed)]
    [InlineData(SpaceGenerationRunStatus.Stale)]
    public void Review_failure_and_stale_runs_can_be_discarded(
        SpaceGenerationRunStatus status)
    {
        var run = RunAt(status);

        run.Discard(Now);

        Assert.Equal(SpaceGenerationRunStatus.Cancelled, run.Status);
        Assert.Equal(Now, run.CancelledAtUtc);
        Assert.False(run.IsCurrent);
    }

    [Fact]
    public void Stale_is_limited_to_review_or_apply()
    {
        var queued = NewRun();
        var review = RunAt(SpaceGenerationRunStatus.AwaitingReview);

        Assert.Throws<SpaceGenerationStateException>(
            queued.MarkStale);
        review.MarkStale();
        Assert.Equal(SpaceGenerationRunStatus.Stale, review.Status);
        Assert.False(review.IsCurrent);
    }

    [Fact]
    public void Accepted_proposal_applies_exactly_once()
    {
        var proposal = NewProposal();
        var logicalId = Guid.NewGuid();

        proposal.Accept();
        proposal.MarkApplied(logicalId);

        Assert.Equal(SpaceGenerationProposalStatus.Applied, proposal.Status);
        Assert.Equal(logicalId, proposal.AppliedLogicalId);
        Assert.Throws<SpaceProposalStateException>(
            () => proposal.MarkApplied(Guid.NewGuid()));
        Assert.Throws<SpaceProposalStateException>(
            proposal.MarkObsolete);
    }

    [Fact]
    public void Blocking_proposal_can_be_rejected_but_not_accepted_or_modified()
    {
        var accepted = NewProposal(hasBlockingIssue: true);
        var modified = NewProposal(hasBlockingIssue: true);
        var rejected = NewProposal(hasBlockingIssue: true);

        Assert.Throws<SpaceProposalStateException>(accepted.Accept);
        Assert.Throws<SpaceProposalStateException>(
            () => modified.Modify("[]", "[]"));
        rejected.Reject();
        Assert.Equal(
            SpaceGenerationProposalStatus.Rejected,
            rejected.Status);
    }

    [Fact]
    public void Review_completion_is_server_owned_and_recorded_once()
    {
        var run = RunAt(SpaceGenerationRunStatus.AwaitingReview);

        Assert.Throws<SpaceGenerationStateException>(
            () => BeginApplying(run));
        run.MarkReviewCompleted(Now);
        Assert.Equal(Now, run.ReviewCompletedAtUtc);
        Assert.Throws<SpaceGenerationStateException>(
            () => run.MarkReviewCompleted(Now.AddSeconds(1)));
    }

    [Fact]
    public void Allowlisted_repair_can_clear_a_blocking_proposal()
    {
        var proposal = NewProposal(hasBlockingIssue: true);

        proposal.Modify(
            """[{"op":"replace","path":"/attributes/name","value":"Rack A"}]""",
            """["/attributes/name"]""",
            resolvesBlockingIssues: true);

        Assert.False(proposal.HasBlockingIssue);
        Assert.Equal(SpaceGenerationProposalStatus.Modified, proposal.Status);
    }

    [Fact]
    public void Modified_proposal_preserves_original_and_human_final_values()
    {
        var proposal = NewProposal();

        proposal.Modify(
            """[{"op":"replace","path":"/type","value":"rack"}]""",
            """["/type"]""");

        Assert.Equal(
            SpaceGenerationProposalStatus.Modified,
            proposal.Status);
        Assert.Equal("""{"kind":"zone"}""", proposal.SuggestedAttributesJson);
        Assert.Contains("replace", proposal.HumanPatchJson);
        Assert.Equal("""["/type"]""", proposal.LockedFieldsJson);
    }

    [Fact]
    public void Proposal_requires_valid_json_and_defined_confidence_band()
    {
        Assert.Throws<ArgumentException>(
            () => NewProposal(suggestedGeometryJson: "{"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewProposal(
                confidenceBand: (SpaceConfidenceBand)99));
    }

    [Fact]
    public void Decision_shape_is_validated_before_append()
    {
        Assert.Throws<ArgumentException>(
            () => NewDecision(
                SpaceProposalDecisionType.Accept,
                afterJson: null));
        Assert.Throws<ArgumentException>(
            () => NewDecision(
                SpaceProposalDecisionType.Reject,
                afterJson: "{}"));
        Assert.Throws<ArgumentException>(
            () => NewDecision(
                SpaceProposalDecisionType.Modify,
                afterJson: "{"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewDecision(
                (SpaceProposalDecisionType)99,
                afterJson: null));

        var decision = NewDecision(
            SpaceProposalDecisionType.Modify,
            """{"kind":"rack"}""");
        Assert.Equal("""{"kind":"zone"}""", decision.BeforeJson);
        Assert.Equal("""{"kind":"rack"}""", decision.AfterJson);
        Assert.Equal("reviewed", decision.Comment);
    }

    [Theory]
    [InlineData("human reviewed", "reviewed")]
    [InlineData("HUMAN_REVIEWED", "password=do-not-store")]
    [InlineData("HUMAN_REVIEWED", "authorization: Bearer token")]
    [InlineData("HUMAN_REVIEWED", "line\nbreak")]
    public void Decision_rejects_unstable_reason_codes_and_sensitive_comments(
        string reasonCode,
        string comment)
    {
        Assert.Throws<ArgumentException>(
            () => SpaceProposalDecision.Create(
                TenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                SpaceProposalDecisionType.Modify,
                """{"kind":"zone"}""",
                """{"kind":"rack"}""",
                """["/kind"]""",
                reasonCode,
                comment,
                Guid.NewGuid()));
    }

    [Fact]
    public void Same_source_locked_fact_preserves_decision_lineage()
    {
        var runId = Guid.NewGuid();
        var basedOnRunId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();

        var fact = SpaceGenerationLockedFact.CreateSameSource(
            TenantId,
            runId,
            basedOnRunId,
            proposalId,
            decisionId,
            SourceHash,
            "rack-1",
            "Rack",
            "/attributes/rackType",
            "\"DriveIn\"");

        Assert.Equal(runId, fact.RunId);
        Assert.Equal(basedOnRunId, fact.BasedOnRunId);
        Assert.Equal(decisionId, fact.SourceDecisionId);
        Assert.Equal(SpaceLockedFactMatchMethod.SameSourceIdentity, fact.MatchMethod);
        Assert.Equal(1m, fact.MatchScore);
        Assert.True(fact.IsConfirmed);
        Assert.Throws<ArgumentException>(() =>
            SpaceGenerationLockedFact.CreateSameSource(
                TenantId,
                runId,
                basedOnRunId,
                proposalId,
                decisionId,
                SourceHash,
                "rack-1",
                "Rack",
                "/geometry/x",
                "1"));
    }

    [Fact]
    public void Usage_normalizes_currency_and_rejects_invalid_accounting()
    {
        var usage = NewUsage(currency: " usd ");

        Assert.Equal("USD", usage.Currency);
        Assert.Equal(TenantId, usage.TenantId);
        Assert.Equal(SpaceAiUsageOutcome.Succeeded, usage.Outcome);
        Assert.Throws<ArgumentException>(
            () => NewUsage(currency: null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewUsage(actualCostMinor: -1));
        Assert.Throws<ArgumentException>(
            () => NewUsage(
                currency: "USD",
                recordedAtUtc: DateTime.SpecifyKind(
                    Now,
                    DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewUsage(
                currency: "USD",
                outcome: (SpaceAiUsageOutcome)99));
    }

    [Fact]
    public void Run_hashes_and_policy_snapshot_fail_closed()
    {
        Assert.Throws<ArgumentException>(
            () => NewRun(sourceHash: new string('A', 64)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewRun(
                policySnapshot: (SpaceAiPolicySnapshot)99));
        Assert.Throws<ArgumentException>(
            () => NewRun(
                policySnapshot: SpaceAiPolicySnapshot.Disabled,
                providerConfigVersionId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(
            () => NewRun(pinProviderConfig: false));
    }

    private static SpaceGenerationRun NewRun(
        string? sourceHash = null,
        SpaceAiPolicySnapshot policySnapshot =
            SpaceAiPolicySnapshot.StructuredFeatures,
        Guid? providerConfigVersionId = null,
        bool pinProviderConfig = true)
    {
        Guid? pinnedProviderConfigVersionId = null;
        if (pinProviderConfig)
        {
            pinnedProviderConfigVersionId =
                providerConfigVersionId ??
                (policySnapshot == SpaceAiPolicySnapshot.Disabled
                    ? null
                    : Guid.Parse(
                        "66666666-6666-6666-6666-666666666666"));
        }
        return SpaceGenerationRun.Create(
            new SpaceGenerationRunDefinition(
                TenantId,
                SiteId,
                ModelVersionId,
                SourceId,
                sourceHash ?? SourceHash,
                7,
                IdempotencyHash,
                BusinessHash,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "rules-1",
                policySnapshot,
                pinnedProviderConfigVersionId,
                "1.0",
                JobId,
                TargetFloorLogicalId));
    }

    private static void BeginApplying(SpaceGenerationRun run) =>
        run.BeginApplying(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('e', 64),
            Convert.ToBase64String([1, 2, 3, 4]));

    private static SpaceGenerationRun RunAt(
        SpaceGenerationRunStatus status)
    {
        var run = NewRun();
        if (status == SpaceGenerationRunStatus.Failed)
        {
            run.MarkFailed("FAILED", "Failed.");
            return run;
        }

        run.BeginPreparing();
        run.BeginInferring();
        run.BeginValidating();
        run.MarkAwaitingReview();
        if (status == SpaceGenerationRunStatus.AwaitingReview)
            return run;
        if (status == SpaceGenerationRunStatus.Stale)
        {
            run.MarkStale();
            return run;
        }

        throw new ArgumentOutOfRangeException(nameof(status));
    }

    private static SpaceGenerationProposal NewProposal(
        bool hasBlockingIssue = false,
        string suggestedGeometryJson = "{}",
        SpaceConfidenceBand confidenceBand = SpaceConfidenceBand.High) =>
        SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                TenantId,
                Guid.NewGuid(),
                ModelVersionId,
                7,
                SourceHash,
                "layer:racks/block:standard",
                "Rack",
                suggestedGeometryJson,
                """{"kind":"zone"}""",
                "[]",
                "[]",
                "[]",
                "{}",
                0.95m,
                confidenceBand,
                hasBlockingIssue));

    private static SpaceProposalDecision NewDecision(
        SpaceProposalDecisionType decisionType,
        string? afterJson) =>
        SpaceProposalDecision.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            decisionType,
            """{"kind":"zone"}""",
            afterJson,
            """["/kind"]""",
            "REVIEWED",
            "reviewed",
            Guid.NewGuid());

    private static SpaceAiUsageRecord NewUsage(
        long? actualCostMinor = 11,
        string? currency = "USD",
        DateTime? recordedAtUtc = null,
        SpaceAiUsageOutcome outcome =
            SpaceAiUsageOutcome.Succeeded) =>
        SpaceAiUsageRecord.Create(
            TenantId,
            Guid.NewGuid(),
            "local-v1",
            "warehouse-v1",
            ProviderRequestHash,
            10,
            5,
            10,
            actualCostMinor,
            currency,
            120,
            outcome,
            recordedAtUtc ?? Now);
}
