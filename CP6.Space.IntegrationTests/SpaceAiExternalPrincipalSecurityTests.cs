using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiExternalPrincipalSecurityTests
{
    [Theory]
    [InlineData("Customer", "bbbbbbbb-0000-0000-0000-000000000001")]
    [InlineData("Supplier", "bbbbbbbb-0000-0000-0000-000000000002")]
    [InlineData("3PL", "bbbbbbbb-0000-0000-0000-000000000003")]
    public async Task Every_ai_operation_denies_external_principals_before_data_access(
        string role,
        string organizationId)
    {
        var execution = new ExternalExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Parse(organizationId));
        var clock = new FixedClock();
        await using var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            execution,
            clock);
        var access = new ThrowingAccess();
        var lockedFacts = new SpaceAiLockedFactService(
            context,
            execution,
            access);
        var administration = new SpaceAiAdministrationService(
            context,
            execution,
            clock,
            new WarehouseGenerationProviderRegistry([]));
        var apply = new SpaceAiAtomicApplyService(
            context,
            execution,
            access,
            clock);
        var recovery = new SpaceAiRunRecoveryService(
            context,
            execution,
            access,
            clock,
            lockedFacts);
        var proposals = new SpaceAiProposalDecisionService(
            context,
            execution,
            access,
            new ThrowingCursorCodec(),
            clock,
            new SpaceAiProposalReviewOptions());
        var runId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var action = new SpaceAiRunActionRequest("row-version");

        IReadOnlyList<(string Name, Func<Task> Invoke)> operations =
        [
            ("GET ai-policy", async () =>
                _ = await administration.GetPolicyAsync()),
            ("PUT ai-policy", async () =>
                _ = await administration.UpdatePolicyAsync(
                    new UpdateSpaceAiPolicyRequest(
                        0, "Disabled", [], [], 3, false,
                        null, null, null),
                    "external-policy")),
            ("GET ai-usage", async () =>
                _ = await administration.GetUsageAsync(new SpaceAiUsageQuery())),
            ("GET generation-run", async () =>
                _ = await apply.GetRunAsync(runId)),
            ("POST generation-run apply", async () =>
                _ = await apply.QueueAsync(
                    runId,
                    new CreateSpaceAiAtomicApplyRequest(0, "row", "review"),
                    "external-apply")),
            ("POST generation-run cancel", async () =>
                _ = await recovery.CancelAsync(
                    runId, action, "external-cancel")),
            ("POST generation-run retry", async () =>
                _ = await recovery.RetryAsync(
                    runId, action, "external-retry")),
            ("POST generation-run discard", async () =>
                _ = await recovery.DiscardAsync(
                    runId, action, "external-discard")),
            ("POST generation-run reconcile", async () =>
                _ = await recovery.ReconcileAsync(
                    runId, action, "external-reconcile")),
            ("POST generation-run recover", async () =>
                _ = await recovery.RecoverAsync(
                    versionId,
                    new CreateSpaceAiGenerationRecoveryRequest(
                        runId, 0, "row", "SamePolicy"),
                    "external-recover")),
            ("GET proposal review", async () =>
                _ = await proposals.GetReviewAsync(runId)),
            ("GET proposals", async () =>
                _ = await proposals.GetProposalsAsync(
                    runId, new SpaceAiProposalQuery())),
            ("GET proposal issues", async () =>
                _ = await proposals.GetIssuesAsync(
                    runId, new SpaceAiProposalIssueQuery())),
            ("GET proposal decisions", async () =>
                _ = await proposals.GetDecisionsAsync(runId, null, 25)),
            ("POST proposal decision", async () =>
                _ = await proposals.CreateDecisionAsync(
                    runId,
                    new CreateSpaceAiProposalDecisionRequest(
                        Guid.NewGuid(), "Accept", "row", null, null,
                        null, null),
                    "external-decision")),
            ("POST proposal batch decision", async () =>
                _ = await proposals.CreateBatchDecisionAsync(
                    runId,
                    new CreateSpaceAiProposalBatchDecisionRequest(
                        [Guid.NewGuid()], null, "Accept", "review",
                        null, null),
                    "external-batch-decision")),
        ];

        Assert.Equal(16, operations.Count);
        foreach (var operation in operations)
        {
            var error = await Assert.ThrowsAsync<SpaceProblemException>(
                operation.Invoke);
            Assert.True(
                error.Code == SpaceErrorCodes.ExternalSubjectDenied,
                $"{role} received {error.Code} from {operation.Name}.");
            Assert.True(
                error.StatusCode == 403,
                $"{role} received HTTP {error.StatusCode} from {operation.Name}.");
        }

        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(context.GenerationRuns);
        Assert.Empty(context.GenerationProposals);
        Assert.Empty(context.AiTenantPolicies);
        Assert.Empty(context.AiUsageRecords);
        Assert.Empty(context.Jobs);
        Assert.Empty(context.IdempotencyRecords);
    }

    private sealed record ExternalExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid OrganizationId) : ISpaceExecutionContext
    {
        public bool IsExternal => true;
        public Guid? OrganizationContextId => OrganizationId;
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow { get; } =
            new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class ThrowingAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write) =>
            throw new InvalidOperationException(
                "External principal reached site data access.");
    }

    private sealed class ThrowingCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            throw new InvalidOperationException(
                "External principal reached cursor encoding.");

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new InvalidOperationException(
                "External principal reached cursor decoding.");
    }
}
