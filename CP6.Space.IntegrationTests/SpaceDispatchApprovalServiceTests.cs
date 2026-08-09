using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDispatchApprovalServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task Submit_and_separate_approval_assign_task_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();

        var submitted = await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var duplicate = await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], " Release approved work "));

        Assert.Equal("Submitted", submitted.Outcome);
        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(SpaceDispatchApprovalStatus.PendingApproval,
            submitted.ApprovalRequest.Status);
        Assert.Equal(1, fixture.Approvals.SubmitCount);

        await fixture.Service.ApplyApprovedAsync(
            requestId,
            fixture.Callback(fixture.ApproverId));
        await fixture.Core.SaveChangesAsync();

        var applied = await fixture.Service.GetAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId);
        Assert.Equal(SpaceDispatchApprovalStatus.Applied, applied.Status);
        Assert.Equal(Cp6SpaceDispatchTaskAdapter.AdapterVersion, applied.AdapterId);
        Assert.Single(applied.Receipts);
        Assert.Equal("Applied", applied.Receipts[0].Outcome);
        Assert.Equal("worker1", (await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
        Assert.Single(await fixture.Core.MobileTaskEvents.ToListAsync());
        Assert.Single(await fixture.Core.TaskCommandReceipts.ToListAsync());
    }

    [Fact]
    public async Task Personnel_change_after_submit_marks_stale_with_zero_task_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var state = await fixture.Space.PersonnelStates.SingleAsync();
        state.Apply(PersonnelEvent(
            fixture.TenantId,
            fixture.SiteId,
            fixture.PersonUserId,
            "WORK-BUSY",
            SpacePersonnelEventKind.WorkStateChanged,
            SpacePersonnelWorkState.Busy,
            sequence: 2,
            Now.AddSeconds(-10)));
        await fixture.Space.SaveChangesAsync();

        await fixture.Service.ApplyApprovedAsync(
            requestId,
            fixture.Callback(fixture.ApproverId));
        await fixture.Core.SaveChangesAsync();

        var row = await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync();
        Assert.Equal(SpaceDispatchApprovalStatus.Stale, row.Status);
        Assert.Equal("SPACE_DISPATCH_PERSON_STALE", row.FailureCode);
        Assert.Null((await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
        Assert.Empty(await fixture.Core.MobileTaskEvents.ToListAsync());
    }

    [Fact]
    public async Task Recommendation_snapshot_mismatch_marks_stale_with_zero_task_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var approval = await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync();
        approval.RecommendationRequestHash = new string('b', 64);
        await fixture.Core.SaveChangesAsync();

        await fixture.Service.ApplyApprovedAsync(
            requestId,
            fixture.Callback(fixture.ApproverId));
        await fixture.Core.SaveChangesAsync();

        var row = await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync();
        Assert.Equal(SpaceDispatchApprovalStatus.Stale, row.Status);
        Assert.Equal("SPACE_DISPATCH_RECOMMENDATION_STALE", row.FailureCode);
        Assert.Null((await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
        Assert.Empty(await fixture.Core.MobileTaskEvents.ToListAsync());
    }

    [Fact]
    public async Task Requester_cannot_approve_own_dispatch_request()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ApplyApprovedAsync(
                requestId,
                fixture.Callback(fixture.RequesterId)));

        Assert.Equal("SPACE_DISPATCH_APPROVER_SEPARATION", error.Message);
        Assert.Null((await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
    }

    [Fact]
    public async Task Requester_can_cancel_pending_request_but_not_a_terminal_request()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));

        await fixture.Service.CancelAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId);

        Assert.Equal(1, fixture.TaskCenter.WithdrawCount);
        Assert.Equal(SpaceDispatchApprovalStatus.Cancelled,
            (await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync()).Status);
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CancelAsync(
                fixture.SiteId,
                fixture.RecommendationId,
                requestId));
        Assert.Equal(SpaceErrorCodes.DispatchApprovalNotPending, error.Code);
    }

    [Fact]
    public async Task Approved_callback_replay_is_idempotent_and_execution_is_assigned()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var callback = fixture.Callback(fixture.ApproverId);

        await fixture.Service.ApplyApprovedAsync(requestId, callback);
        await fixture.Core.SaveChangesAsync();
        await fixture.Service.ApplyApprovedAsync(requestId, callback);
        await fixture.Core.SaveChangesAsync();

        var execution = await fixture.Service.GetExecutionAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId);
        Assert.Equal("Assigned", execution.Status);
        Assert.True(execution.CanCompensate);
        Assert.Equal(1, execution.AssignedCount);
        Assert.Equal("Assigned", Assert.Single(execution.Tasks).State);
        Assert.Single(await fixture.Core.MobileTaskEvents.ToListAsync());
        Assert.Single(await fixture.Core.TaskCommandReceipts.ToListAsync());
    }

    [Fact]
    public async Task Failed_no_effect_can_retry_once_and_replay_action_without_duplicate_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var approval = await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync();
        approval.Status = SpaceDispatchApprovalStatus.FailedNoEffect;
        approval.DecidedById = fixture.ApproverId;
        approval.DecidedAtUtc = Now;
        approval.FailureCode = "SPACE_DISPATCH_ADAPTER_TEMPORARY";
        await fixture.Core.SaveChangesAsync();
        var actionId = Guid.NewGuid();

        var first = await fixture.Service.RetryAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            actionId,
            new SubmitSpaceDispatchExecutionActionRequest("Retry after review"));
        var replay = await fixture.Service.RetryAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            actionId,
            new SubmitSpaceDispatchExecutionActionRequest(" Retry after review "));

        Assert.Equal("Executed", first.Outcome);
        Assert.Equal("Duplicate", replay.Outcome);
        Assert.Equal(SpaceDispatchExecutionActionStatus.Applied, first.Action.Status);
        Assert.Equal("Assigned", first.Execution.Status);
        Assert.Equal(1, first.Execution.RetryAttemptCount);
        Assert.Single(await fixture.Core.SpaceDispatchExecutionActions.ToListAsync());
        Assert.Single(await fixture.Core.MobileTaskEvents.ToListAsync());
        Assert.Single(await fixture.Core.TaskCommandReceipts.ToListAsync());
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.RetryAsync(
                fixture.SiteId,
                fixture.RecommendationId,
                requestId,
                actionId,
                new SubmitSpaceDispatchExecutionActionRequest("Different payload")));
        Assert.Equal(SpaceErrorCodes.DispatchExecutionConflict, conflict.Code);
    }

    [Fact]
    public async Task Retry_limit_counts_distinct_failed_actions_but_not_replays()
    {
        await using var fixture = await Fixture.CreateAsync(
            (_, _) => new AlwaysFailingAdapter());
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        var approval = await fixture.Core.SpaceDispatchApprovalRequests.SingleAsync();
        approval.Status = SpaceDispatchApprovalStatus.FailedNoEffect;
        approval.DecidedById = fixture.ApproverId;
        approval.DecidedAtUtc = Now;
        await fixture.Core.SaveChangesAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = await fixture.Service.RetryAsync(
                fixture.SiteId,
                fixture.RecommendationId,
                requestId,
                Guid.NewGuid(),
                new SubmitSpaceDispatchExecutionActionRequest($"Retry {attempt}"));
            Assert.Equal(
                SpaceDispatchExecutionActionStatus.FailedNoEffect,
                result.Action.Status);
            Assert.Equal(attempt, result.Execution.RetryAttemptCount);
        }

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.RetryAsync(
                fixture.SiteId,
                fixture.RecommendationId,
                requestId,
                Guid.NewGuid(),
                new SubmitSpaceDispatchExecutionActionRequest("Retry 4")));
        Assert.Equal(SpaceErrorCodes.DispatchExecutionRetryLimit, error.Code);
        Assert.Equal(3, await fixture.Core.SpaceDispatchExecutionActions.CountAsync());
        Assert.Null((await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
    }

    [Fact]
    public async Task Safe_compensation_is_atomic_idempotent_and_preserves_task_execution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        await fixture.Service.ApplyApprovedAsync(
            requestId,
            fixture.Callback(fixture.ApproverId));
        await fixture.Core.SaveChangesAsync();
        var actionId = Guid.NewGuid();

        var first = await fixture.Service.CompensateAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            actionId,
            new SubmitSpaceDispatchExecutionActionRequest("Undo unstarted assignment"));
        var replay = await fixture.Service.CompensateAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            actionId,
            new SubmitSpaceDispatchExecutionActionRequest(" Undo unstarted assignment "));

        Assert.Equal("Executed", first.Outcome);
        Assert.Equal("Duplicate", replay.Outcome);
        Assert.Equal(SpaceDispatchApprovalStatus.Compensated,
            first.Execution.ApprovalStatus);
        Assert.Equal("Compensated", first.Execution.Status);
        Assert.Equal("Compensated", Assert.Single(first.Execution.Tasks).State);
        var task = await fixture.Core.MobileTasks.SingleAsync();
        Assert.Null(task.AssignedTo);
        Assert.Equal(MobileTaskStatus.Pending, task.Status);
        Assert.Equal(3, task.ExecutionVersion);
        Assert.Equal(2, await fixture.Core.MobileTaskEvents.CountAsync());
        Assert.Equal(2, await fixture.Core.TaskCommandReceipts.CountAsync());
        Assert.Single(await fixture.Core.SpaceDispatchExecutionActions.ToListAsync());
    }

    [Fact]
    public async Task Started_task_blocks_compensation_with_zero_task_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        await fixture.Service.SubmitAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            new SubmitSpaceDispatchApprovalRequest([1], "Release approved work"));
        await fixture.Service.ApplyApprovedAsync(
            requestId,
            fixture.Callback(fixture.ApproverId));
        await fixture.Core.SaveChangesAsync();
        var task = await fixture.Core.MobileTasks.SingleAsync();
        task.Status = MobileTaskStatus.InProgress;
        task.StartedAt = Now.AddMinutes(1);
        await fixture.Core.SaveChangesAsync();

        var before = await fixture.Service.GetExecutionAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId);
        var result = await fixture.Service.CompensateAsync(
            fixture.SiteId,
            fixture.RecommendationId,
            requestId,
            Guid.NewGuid(),
            new SubmitSpaceDispatchExecutionActionRequest("Attempt safe compensation"));

        Assert.Equal("Executing", before.Status);
        Assert.False(before.CanCompensate);
        Assert.Equal(
            SpaceDispatchExecutionActionStatus.RejectedNoEffect,
            result.Action.Status);
        Assert.Equal("SPACE_DISPATCH_COMPENSATION_NOT_SAFE", result.Action.FailureCode);
        Assert.Equal("Executing", result.Execution.Status);
        Assert.Equal("worker1", (await fixture.Core.MobileTasks.SingleAsync()).AssignedTo);
        Assert.Equal(1, await fixture.Core.MobileTaskEvents.CountAsync());
        Assert.Equal(1, await fixture.Core.TaskCommandReceipts.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            Guid tenantId,
            Guid siteId,
            Guid recommendationId,
            Guid requesterId,
            Guid approverId,
            Guid personUserId,
            SpaceContext space,
            CP6Context core,
            SpaceDispatchApprovalService service,
            RecordingApprovalService approvals,
            RecordingTaskCenter taskCenter)
        {
            TenantId = tenantId;
            SiteId = siteId;
            RecommendationId = recommendationId;
            RequesterId = requesterId;
            ApproverId = approverId;
            PersonUserId = personUserId;
            Space = space;
            Core = core;
            Service = service;
            Approvals = approvals;
            TaskCenter = taskCenter;
        }

        public Guid TenantId { get; }
        public Guid SiteId { get; }
        public Guid RecommendationId { get; }
        public Guid RequesterId { get; }
        public Guid ApproverId { get; }
        public Guid PersonUserId { get; }
        public SpaceContext Space { get; }
        public CP6Context Core { get; }
        public SpaceDispatchApprovalService Service { get; }
        public RecordingApprovalService Approvals { get; }
        public RecordingTaskCenter TaskCenter { get; }

        public ApprovalCallbackContext Callback(Guid decidedBy) => new()
        {
            BizType = SpaceDispatchApprovalService.ApprovalBizType,
            BizId = Core.SpaceDispatchApprovalRequests.Single().Id.ToString("D"),
            InstanceId = Core.SpaceDispatchApprovalRequests.Single().FlowInstanceId,
            StarterId = RequesterId,
            DecidedById = decidedBy,
        };

        public static async Task<Fixture> CreateAsync(
            Func<CP6Context, IWmsAccessScopeProvider, ISpaceDispatchTaskAdapter>?
                adapterFactory = null)
        {
            var tenantId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var recommendationId = Guid.NewGuid();
            var requesterId = Guid.NewGuid();
            var approverId = Guid.NewGuid();
            var personUserId = Guid.NewGuid();
            var execution = new TestExecution(tenantId, requesterId, false);
            var clock = new TestClock();
            var space = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                clock);
            var core = new CP6Context(
                new DbContextOptionsBuilder<CP6Context>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                new TenantContext { CurrentTenantId = tenantId });
            var assignment = await SeedSpaceAsync(
                space, tenantId, siteId, recommendationId, personUserId);
            await SeedCoreAsync(core, requesterId, approverId, personUserId);
            var recommendation = Recommendation(
                recommendationId,
                siteId,
                (await space.DispatchRecommendations.AsNoTracking().SingleAsync())
                    .PublishedVersionId,
                assignment);
            var fakeRecommendations = new FixedRecommendationService(recommendation);
            var approvals = new RecordingApprovalService(core);
            var taskCenter = new RecordingTaskCenter(core);
            var scopes = new FixedWmsAccessScopeProvider(WmsAccessScope.All);
            var adapter = adapterFactory?.Invoke(core, scopes) ??
                new Cp6SpaceDispatchTaskAdapter(core, scopes);
            var service = new SpaceDispatchApprovalService(
                space,
                core,
                fakeRecommendations,
                adapter,
                approvals,
                taskCenter,
                scopes,
                execution,
                clock,
                new RecordingAccess(),
                new SpacePersonnelRuntimeOptions());
            return new Fixture(
                tenantId, siteId, recommendationId, requesterId, approverId,
                personUserId, space, core, service, approvals, taskCenter);
        }

        public async ValueTask DisposeAsync()
        {
            await Space.DisposeAsync();
            await Core.DisposeAsync();
        }
    }

    private static async Task<SpaceDispatchRecommendationAssignmentDto>
        SeedSpaceAsync(
            SpaceContext context,
            Guid tenantId,
            Guid siteId,
            Guid recommendationId,
            Guid personUserId)
    {
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId, model.Id, 1, "Published dispatch model");
        context.AddRange(model, version);
        await context.SaveChangesAsync();
        version.BeginValidation();
        version.MarkReady(Hash, "space-v1", Hash);
        version.BeginPublishing();
        version.MarkPublished(Guid.NewGuid(), Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(version);
        model.ActivateDesignV1();

        var position = PersonnelEvent(
            tenantId,
            siteId,
            personUserId,
            "POSITION-1",
            SpacePersonnelEventKind.PositionObserved,
            null,
            1,
            Now.AddMinutes(-2),
            Guid.NewGuid(),
            Guid.NewGuid());
        var state = SpacePersonnelCurrentState.Create(position);
        state.Apply(PersonnelEvent(
            tenantId,
            siteId,
            personUserId,
            "WORK-1",
            SpacePersonnelEventKind.WorkStateChanged,
            SpacePersonnelWorkState.Idle,
            1,
            Now.AddMinutes(-1)));
        context.PersonnelStates.Add(state);

        var assignment = Assignment(state);
        var row = SpaceDispatchRecommendation.Create(
            tenantId,
            recommendationId,
            new SpaceDispatchRecommendationData(
                siteId,
                version.Id,
                "WH-01",
                Now,
                Guid.NewGuid(),
                SpaceDispatchRecommendationService.DefinitionVersion,
                "AssignmentsGenerated",
                1, 1, 1, 1, 1, 1, 1,
                false,
                false,
                JsonSerializer.Serialize(new GenerateSpaceDispatchRecommendationRequest()),
                "{}",
                "{}",
                "[]",
                JsonSerializer.Serialize(new[] { assignment }),
                "[]",
                Hash));
        context.DispatchRecommendations.Add(row);
        await context.SaveChangesAsync();
        return assignment;
    }

    private static async Task SeedCoreAsync(
        CP6Context context,
        Guid requesterId,
        Guid approverId,
        Guid personUserId)
    {
        context.Sys_Users.AddRange(
            User(requesterId, "requester"),
            User(approverId, "approver"),
            User(personUserId, "worker1"));
        context.MobileTasks.Add(new MobileTask
        {
            Id = Guid.NewGuid(),
            MobileTaskNo = "TASK-1",
            TaskType = MobileTaskType.Pick,
            WarehouseCd = "WH-01",
            AreaCd = "A-01",
            FromLocationCd = "F1-L01",
            Status = MobileTaskStatus.Pending,
            ContractVersion = 2,
            ExecutionVersion = 3,
            RowVersion = [1, 2, 3, 4],
        });
        await context.SaveChangesAsync();
    }

    private static Sys_User User(Guid id, string name) => new()
    {
        Id = id,
        UserName = name,
        Password = "test",
        Enable = true,
    };

    private static SpacePersonnelEvent PersonnelEvent(
        Guid tenantId,
        Guid siteId,
        Guid userId,
        string sourceEventId,
        SpacePersonnelEventKind kind,
        SpacePersonnelWorkState? workState,
        long sequence,
        DateTime occurredAt,
        Guid? floorId = null,
        Guid? locationId = null) =>
        SpacePersonnelEvent.Create(
            tenantId,
            siteId,
            "PDA-01",
            SpacePersonnelSourceKind.Real,
            sourceEventId,
            "PERSON-1",
            userId,
            kind,
            workState,
            floorId,
            locationId,
            null,
            null,
            null,
            null,
            sequence,
            occurredAt,
            occurredAt,
            Hash);

    private static SpaceDispatchRecommendationAssignmentDto Assignment(
        SpacePersonnelCurrentState state) =>
        new(
            1,
            "TASK-1",
            MobileTaskType.Pick,
            "Pending",
            1,
            2,
            3,
            "AQIDBA==",
            "Source",
            Guid.NewGuid(),
            "F1-L01",
            state.FloorLogicalId!.Value,
            "F1",
            "Floor 1",
            1,
            null,
            null,
            null,
            null,
            1,
            "SKU-1",
            "person-key",
            state.SourceId,
            "Real",
            state.PersonExternalId,
            state.LocationLogicalId,
            state.FloorLogicalId.Value,
            null,
            new DateTimeOffset(state.PositionOccurredAtUtc!.Value),
            new DateTimeOffset(state.PositionReceivedAtUtc!.Value),
            new DateTimeOffset(state.WorkStateOccurredAtUtc!.Value),
            new DateTimeOffset(state.WorkStateReceivedAtUtc!.Value),
            true,
            false,
            1,
            ["SAME_FLOOR"]);

    private static SpaceDispatchRecommendationDto Recommendation(
        Guid recommendationId,
        Guid siteId,
        Guid publishedVersionId,
        SpaceDispatchRecommendationAssignmentDto assignment) =>
        new(
            recommendationId,
            siteId,
            publishedVersionId,
            "WH-01",
            new DateTimeOffset(Now),
            Guid.NewGuid(),
            SpaceDispatchRecommendationService.DefinitionVersion,
            "AssignmentsGenerated",
            new GenerateSpaceDispatchRecommendationRequest(),
            new SpaceDispatchRecommendationSourcesDto(
                new SpaceWmsRuntimeSourceDto(
                    "Real", "cp6-wms-v1", "CP6_WMS",
                    new DateTimeOffset(Now), new DateTimeOffset(Now),
                    0, 0, false, true),
                new SpaceDispatchPersonnelSourceDto(
                    new DateTimeOffset(Now), 300, 1, 1, 0, false, [])),
            1, 1, 1, 1, 1, 1, 1,
            false,
            new SpaceDispatchRecommendationExclusionsDto(
                0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0),
            false,
            [],
            [assignment],
            []);

    private sealed record TestExecution(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AlwaysFailingAdapter : ISpaceDispatchTaskAdapter
    {
        public string AdapterId => "test-failing-dispatch-adapter-v1";

        public Task<SpaceDispatchTaskAdapterResult> StageAssignmentsAsync(
            SpaceDispatchTaskAdapterCommand command,
            CancellationToken cancellationToken = default) =>
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_ADAPTER_TEMPORARY");

        public Task<SpaceDispatchTaskAdapterResult> StageCompensationAsync(
            SpaceDispatchTaskCompensationCommand command,
            CancellationToken cancellationToken = default) =>
            throw new SpaceDispatchTaskAdapterException(
                "SPACE_DISPATCH_ADAPTER_TEMPORARY");
    }

    private sealed class RecordingAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class FixedRecommendationService(
        SpaceDispatchRecommendationDto value) : ISpaceDispatchRecommendationService
    {
        public Task<GenerateSpaceDispatchRecommendationResponse> GenerateAsync(
            Guid siteId,
            Guid recommendationId,
            GenerateSpaceDispatchRecommendationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceDispatchRecommendationDto> GetAsync(
            Guid siteId,
            Guid recommendationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(value);
    }

    public sealed class RecordingApprovalService(CP6Context context)
        : IApprovalService
    {
        public int SubmitCount { get; private set; }

        public async Task<Guid> SubmitAsync(
            string bizType,
            string bizId,
            Guid starterId,
            object? formSnapshot = null,
            Guid? instanceId = null)
        {
            SubmitCount++;
            await context.SaveChangesAsync();
            return instanceId ?? Guid.NewGuid();
        }

        public Task<ApprovalStatus> GetStatusAsync(string bizType, string bizId) =>
            Task.FromResult(ApprovalStatus.Running);
    }

    public sealed class RecordingTaskCenter(CP6Context context)
        : ITaskCenterService
    {
        public int WithdrawCount { get; private set; }

        public Task<List<TodoItem>> MyTodosAsync(Guid userId) =>
            Task.FromResult(new List<TodoItem>());

        public Task<List<MyApplicationItem>> MyApplicationsAsync(Guid userId) =>
            Task.FromResult(new List<MyApplicationItem>());

        public async Task WithdrawAsync(Guid instanceId, Guid userId)
        {
            WithdrawCount++;
            await context.SaveChangesAsync();
        }
    }
}
