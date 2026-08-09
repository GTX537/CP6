using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDispatchOutcomeEvaluationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Published_geometry_and_live_evidence_are_composed_read_only()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.GetAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            fixture.ApprovalId);

        Assert.Equal("Available", result.PlannedDistance.Status);
        Assert.Equal(20m, result.PlannedDistance.StableOrderBaselineMeters);
        Assert.Equal(0m, result.PlannedDistance.OptimizedMeters);
        Assert.Equal("Improved", result.PlannedDistance.Outcome);
        Assert.Equal(fixture.Execution.ObservedAtUtc, result.EvaluatedAtUtc);
        Assert.Equal(1, fixture.Recommendations.GetCalls);
        Assert.Equal(1, fixture.Approvals.GetCalls);
        Assert.Equal(1, fixture.Executions.GetCalls);
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Cross_resource_evidence_mismatch_maps_to_stable_problem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var selections = fixture.Approval.Selections.ToArray();
        selections[0] = selections[0] with { TaskId = "OTHER" };
        fixture.Approvals.Value = fixture.Approval with
        {
            Selections = selections,
        };

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetAsync(
                fixture.SiteId,
                fixture.RecommendationId,
                fixture.ApprovalId));

        Assert.Equal(502, problem.StatusCode);
        Assert.Equal(
            SpaceErrorCodes.DispatchEvaluationEvidenceInvalid,
            problem.Code);
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            SpaceDispatchOutcomeEvaluationService service,
            StubRecommendationService recommendations,
            StubApprovalService approvals,
            StubExecutionService executions,
            SpaceDispatchRecommendationDto recommendation,
            SpaceDispatchApprovalRequestDto approval,
            SpaceDispatchExecutionDto execution)
        {
            Context = context;
            Service = service;
            Recommendations = recommendations;
            Approvals = approvals;
            Executions = executions;
            Recommendation = recommendation;
            Approval = approval;
            Execution = execution;
        }

        public SpaceContext Context { get; }
        public SpaceDispatchOutcomeEvaluationService Service { get; }
        public StubRecommendationService Recommendations { get; }
        public StubApprovalService Approvals { get; }
        public StubExecutionService Executions { get; }
        public SpaceDispatchRecommendationDto Recommendation { get; }
        public SpaceDispatchApprovalRequestDto Approval { get; }
        public SpaceDispatchExecutionDto Execution { get; }
        public Guid SiteId => Recommendation.SiteId;
        public Guid RecommendationId => Recommendation.RecommendationId;
        public Guid ApprovalId => Approval.ApprovalRequestId;

        public static async Task<Fixture> CreateAsync()
        {
            var executionContext = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                executionContext,
                new TestClock());
            var seeded = await SeedAsync(context);
            context.ChangeTracker.Clear();
            var values = Dtos(seeded);
            var recommendations = new StubRecommendationService(
                values.Recommendation);
            var approvals = new StubApprovalService(values.Approval);
            var executions = new StubExecutionService(values.Execution);
            var service = new SpaceDispatchOutcomeEvaluationService(
                context,
                recommendations,
                approvals,
                executions,
                new SpaceDispatchOutcomeEvaluationEngine());
            return new Fixture(
                context,
                service,
                recommendations,
                approvals,
                executions,
                values.Recommendation,
                values.Approval,
                values.Execution);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static async Task<Seeded> SeedAsync(SpaceContext context)
    {
        var tenantId = context.CurrentTenantId;
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Evaluation geometry");
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            siteId,
            1,
            "F1",
            "Floor 1");
        var zone = SpaceZoneRevision.Create(
            tenantId,
            version.Id,
            zoneId,
            floorId,
            "Z1",
            1);
        var rack = SpaceRackRevision.Create(
            tenantId,
            version.Id,
            rackId,
            floorId,
            zoneId,
            "R1");
        rack.ConfigureGeometry(0, 0, 0, 0, 20_000, 1_000, 3_000);
        var level = SpaceRackLevelRevision.Create(
            tenantId,
            version.Id,
            Guid.NewGuid(),
            rackId,
            levelNo: 1,
            bottomZ: 0,
            clearHeight: 1_000,
            binCount: 2,
            depthCount: 1,
            cellWidth: 10_000,
            cellDepth: 1_000);
        var locations = Enumerable.Range(1, 2)
            .Select(index => SpaceLocationRevision.Create(
                tenantId,
                version.Id,
                Guid.NewGuid(),
                floorId,
                rackId,
                $"F1-L0{index}",
                index,
                1,
                1,
                10_000,
                1_000,
                1_000))
            .ToArray();
        context.AddRange(model, version, floor, zone, rack, level);
        context.AddRange(locations);
        await context.SaveChangesAsync();
        return new Seeded(
            siteId,
            version.Id,
            floorId,
            locations[0].LogicalId,
            locations[1].LogicalId);
    }

    private static DtoSet Dtos(Seeded seeded)
    {
        var recommendationId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var assignments = new[]
        {
            Assignment(1, "TASK-1", "PERSON-2", seeded.LocationTwo,
                seeded.LocationTwo, seeded.FloorId),
            Assignment(2, "TASK-2", "PERSON-1", seeded.LocationOne,
                seeded.LocationOne, seeded.FloorId),
        };
        var recommendation = new SpaceDispatchRecommendationDto(
            recommendationId,
            seeded.SiteId,
            seeded.VersionId,
            "WH1",
            Now.AddMinutes(-10),
            Guid.NewGuid(),
            "space-dispatch-v1",
            "AssignmentsGenerated",
            new GenerateSpaceDispatchRecommendationRequest(
                AllowCrossFloor: false,
                MaximumAssignments: 20),
            null!,
            2,
            2,
            2,
            2,
            4,
            2,
            2,
            false,
            null!,
            false,
            [],
            assignments,
            []);
        var selections = assignments.Select(value =>
            new SpaceDispatchApprovalSelectionDto(
                value.Rank,
                value.TaskId,
                value.TaskType,
                value.PersonSourceId,
                value.PersonExternalId,
                value.TargetLocationCode)).ToArray();
        var receipts = assignments.Select(value =>
            new SpaceDispatchTaskAdaptationReceiptDto(
                value.Rank,
                value.TaskId,
                value.PersonExternalId,
                Guid.NewGuid(),
                "Applied")).ToArray();
        var approval = new SpaceDispatchApprovalRequestDto(
            approvalId,
            seeded.SiteId,
            recommendationId,
            seeded.VersionId,
            "WH1",
            "space-dispatch-v1",
            "Applied",
            "approved",
            Guid.NewGuid(),
            Now.AddMinutes(-9),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now.AddMinutes(-8),
            Now.AddMinutes(-7),
            "cp6-mobile-task-assignment-v1",
            2,
            selections,
            receipts,
            null);
        var tasks = assignments.Select((value, index) =>
            new SpaceDispatchExecutionTaskDto(
                value.Rank,
                value.TaskId,
                value.PersonSourceId,
                value.PersonExternalId,
                receipts[index].OperationId,
                2,
                "Completed",
                0,
                Now.AddMinutes(-6 + index),
                Now.AddMinutes(-2 + index),
                "Completed",
                Now.AddMinutes(-2 + index))).ToArray();
        var execution = new SpaceDispatchExecutionDto(
            approvalId,
            seeded.SiteId,
            recommendationId,
            "Applied",
            "Completed",
            Now,
            2,
            0,
            0,
            2,
            0,
            false,
            0,
            3,
            false,
            null,
            null,
            tasks,
            []);
        return new DtoSet(recommendation, approval, execution);
    }

    private static SpaceDispatchRecommendationAssignmentDto Assignment(
        int rank,
        string taskId,
        string personId,
        Guid targetLocation,
        Guid personLocation,
        Guid floorId) =>
        new(
            rank,
            taskId,
            "PICK",
            "Pending",
            2,
            2,
            0,
            "row-version",
            "From",
            targetLocation,
            $"LOC-{rank}",
            floorId,
            "F1",
            "Floor 1",
            1,
            null,
            null,
            null,
            null,
            1m,
            null,
            $"SOURCE:{personId}",
            "SOURCE",
            "Real",
            personId,
            personLocation,
            floorId,
            null,
            Now.AddMinutes(-12),
            Now.AddMinutes(-12),
            Now.AddMinutes(-12),
            Now.AddMinutes(-12),
            true,
            false,
            0m,
            []);

    private sealed record Seeded(
        Guid SiteId,
        Guid VersionId,
        Guid FloorId,
        Guid LocationOne,
        Guid LocationTwo);

    private sealed record DtoSet(
        SpaceDispatchRecommendationDto Recommendation,
        SpaceDispatchApprovalRequestDto Approval,
        SpaceDispatchExecutionDto Execution);

    private sealed record TestExecution(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext
    {
        public bool IsExternal => false;
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now.UtcDateTime;
    }

    private sealed class StubRecommendationService(
        SpaceDispatchRecommendationDto value) : ISpaceDispatchRecommendationService
    {
        public int GetCalls { get; private set; }

        public Task<GenerateSpaceDispatchRecommendationResponse> GenerateAsync(
            Guid siteId,
            Guid recommendationId,
            GenerateSpaceDispatchRecommendationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceDispatchRecommendationDto> GetAsync(
            Guid siteId,
            Guid recommendationId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(value);
        }
    }

    private sealed class StubApprovalService(
        SpaceDispatchApprovalRequestDto value) : ISpaceDispatchApprovalService
    {
        public SpaceDispatchApprovalRequestDto Value { get; set; } = value;
        public int GetCalls { get; private set; }

        public Task<SubmitSpaceDispatchApprovalResponse> SubmitAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            SubmitSpaceDispatchApprovalRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceDispatchApprovalRequestDto> GetAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(Value);
        }

        public Task CancelAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubExecutionService(
        SpaceDispatchExecutionDto value) : ISpaceDispatchExecutionService
    {
        public int GetCalls { get; private set; }

        public Task<SpaceDispatchExecutionDto> GetExecutionAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(value);
        }

        public Task<SpaceDispatchExecutionActionResponse> RetryAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            Guid actionId,
            SubmitSpaceDispatchExecutionActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceDispatchExecutionActionResponse> CompensateAsync(
            Guid siteId,
            Guid recommendationId,
            Guid approvalRequestId,
            Guid actionId,
            SubmitSpaceDispatchExecutionActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
