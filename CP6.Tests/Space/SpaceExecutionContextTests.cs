using CP6.Core.Services.Space.Observability;
using Xunit;

namespace CP6.Tests.Space;

public class SpaceExecutionContextTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Correlation = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Push_exposes_context_and_restores_previous_value()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForUser(Tenant, "u-1", "alice", Correlation, "trace-a");
        var inner = outer with { TraceId = "trace-b", RunId = Guid.NewGuid() };

        using (accessor.Push(outer))
        {
            Assert.Same(outer, accessor.Current);
            using (accessor.Push(inner))
            {
                Assert.Same(inner, accessor.Current);
            }

            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Push_scope_dispose_is_idempotent()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var context = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var scope = accessor.Push(context);

        scope.Dispose();
        scope.Dispose();

        Assert.Null(accessor.Current);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("correlation")]
    [InlineData("trace")]
    [InlineData("actor")]
    [InlineData("actor-type")]
    public void Push_revalidates_context_before_replacing_current(string invalidField)
    {
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var candidate = SpaceExecutionContext.ForUser(
            Tenant, "u-2", "bob", Correlation, "trace-b");
        candidate = invalidField switch
        {
            "tenant" => candidate with { TenantId = Guid.Empty },
            "correlation" => candidate with { CorrelationId = Guid.Empty },
            "trace" => candidate with { TraceId = " " },
            "actor" => candidate with { ActorId = " " },
            "actor-type" => candidate with { ActorType = "External" },
            _ => throw new InvalidOperationException("Unexpected test case"),
        };

        using var scope = accessor.Push(outer);

        Assert.Throws<ArgumentException>(() => accessor.Push(candidate));
        Assert.Same(outer, accessor.Current);
    }

    [Fact]
    public void Enrich_keeps_identity_and_sets_optional_identifiers_once()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var attempt = Guid.NewGuid();
        var job = Guid.NewGuid();
        var run = Guid.NewGuid();
        using var scope = accessor.Push(
            SpaceExecutionContext.ForUser(Tenant, "u-1", "alice", Correlation, "trace-a"));

        accessor.Enrich(jobId: job, runId: run, publishAttemptId: attempt, traceId: "trace-a");
        accessor.Enrich(jobId: job, runId: run, publishAttemptId: attempt, traceId: "trace-a");

        Assert.Equal(Tenant, accessor.Current!.TenantId);
        Assert.Equal(Correlation, accessor.Current.CorrelationId);
        Assert.Equal(SpaceExecutionContext.UserActor, accessor.Current.ActorType);
        Assert.Equal("u-1", accessor.Current.ActorId);
        Assert.Equal(job, accessor.Current.JobId);
        Assert.Equal(run, accessor.Current.RunId);
        Assert.Equal(attempt, accessor.Current.PublishAttemptId);
    }

    [Fact]
    public void Enrich_rejects_conflicting_optional_identifiers_and_trace()
    {
        var accessor = new SpaceExecutionContextAccessor();
        using var scope = accessor.Push(
            SpaceExecutionContext.ForUser(
                Tenant,
                "u-1",
                "alice",
                Correlation,
                "trace-a") with
            {
                JobId = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                PublishAttemptId = Guid.NewGuid(),
            });

        var jobError = Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(jobId: Guid.NewGuid()));
        var runError = Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(runId: Guid.NewGuid()));
        var attemptError = Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(publishAttemptId: Guid.NewGuid()));
        var traceError = Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(traceId: "trace-b"));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", jobError.Message);
        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", runError.Message);
        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", attemptError.Message);
        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", traceError.Message);
    }

    [Fact]
    public void RequireCurrent_and_Enrich_fail_when_context_is_missing()
    {
        var accessor = new SpaceExecutionContextAccessor();

        var requireError = Assert.Throws<InvalidOperationException>(
            () => accessor.RequireCurrent());
        var outcomeError = Assert.Throws<InvalidOperationException>(
            () => accessor.RequireOutcomeCurrent());
        var enrichError = Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(jobId: Guid.NewGuid()));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_REQUIRED", requireError.Message);
        Assert.Equal("SPACE_EXECUTION_CONTEXT_REQUIRED", outcomeError.Message);
        Assert.Equal("SPACE_EXECUTION_CONTEXT_REQUIRED", enrichError.Message);
    }

    [Fact]
    public void Outcome_starts_from_root_and_tracks_root_enrichment()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var root = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var jobId = Guid.NewGuid();
        var publishAttemptId = Guid.NewGuid();

        using var scope = accessor.Push(root);

        Assert.Same(root, accessor.OutcomeCurrent);
        accessor.Enrich(
            jobId: jobId,
            publishAttemptId: publishAttemptId);

        Assert.Equal(jobId, accessor.Current!.JobId);
        Assert.Equal(jobId, accessor.OutcomeCurrent!.JobId);
        Assert.Equal(
            publishAttemptId,
            accessor.RequireOutcomeCurrent().PublishAttemptId);
    }

    [Fact]
    public void Derived_enrichment_survives_restore_only_as_outcome()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var firstJob = Guid.NewGuid();
        var firstAttempt = Guid.NewGuid();
        var secondJob = Guid.NewGuid();
        var secondAttempt = Guid.NewGuid();
        using var rootScope = accessor.Push(SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a"));
        accessor.Enrich(
            jobId: firstJob,
            publishAttemptId: firstAttempt);
        var root = accessor.RequireCurrent();
        var derived = new SpaceExecutionContext(
            root.CorrelationId,
            root.TraceId,
            root.TenantId,
            root.ActorType,
            root.ActorId,
            root.ActorName,
            root.OrganizationContextId,
            JobId: null,
            RunId: root.RunId,
            PublishAttemptId: null);

        var derivedScope = accessor.PushDerived(derived);
        accessor.Enrich(
            jobId: secondJob,
            publishAttemptId: secondAttempt);
        derivedScope.Dispose();

        Assert.Equal(firstJob, accessor.Current!.JobId);
        Assert.Equal(firstAttempt, accessor.Current.PublishAttemptId);
        Assert.Equal(secondJob, accessor.OutcomeCurrent!.JobId);
        Assert.Equal(secondAttempt, accessor.OutcomeCurrent.PublishAttemptId);
    }

    [Fact]
    public async Task Disposed_derived_scope_captured_branch_cannot_read_shared_outcome()
    {
        var accessor = new SpaceExecutionContextAccessor();
        using var rootScope = accessor.Push(SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-root"));
        var root = accessor.RequireCurrent();
        var derived = new SpaceExecutionContext(
            root.CorrelationId,
            root.TraceId,
            root.TenantId,
            root.ActorType,
            root.ActorId,
            root.ActorName,
            root.OrganizationContextId);
        var derivedScope = accessor.PushDerived(derived);
        var derivedAttempt = Guid.NewGuid();
        accessor.Enrich(publishAttemptId: derivedAttempt);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = Task.Run(async () =>
        {
            await release.Task;
            var current = accessor.Current;
            var outcome = accessor.OutcomeCurrent;
            var currentError = Record.Exception(
                () => accessor.RequireCurrent());
            var outcomeError = Record.Exception(
                () => accessor.RequireOutcomeCurrent());
            return (current, outcome, currentError, outcomeError);
        });

        derivedScope.Dispose();
        Assert.Same(root, accessor.Current);
        Assert.Equal(
            derivedAttempt,
            accessor.OutcomeCurrent!.PublishAttemptId);
        release.SetResult();

        var capturedValues = await captured;
        Assert.Null(capturedValues.current);
        Assert.Null(capturedValues.outcome);
        Assert.Equal(
            "SPACE_EXECUTION_CONTEXT_REQUIRED",
            Assert.IsType<InvalidOperationException>(
                capturedValues.currentError).Message);
        Assert.Equal(
            "SPACE_EXECUTION_CONTEXT_REQUIRED",
            Assert.IsType<InvalidOperationException>(
                capturedValues.outcomeError).Message);
        Assert.Equal(
            derivedAttempt,
            accessor.RequireOutcomeCurrent().PublishAttemptId);
    }

    [Fact]
    public void Ordinary_push_has_independent_outcome_and_does_not_pollute_parent()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var outerAttempt = Guid.NewGuid();
        var innerAttempt = Guid.NewGuid();
        using var outerScope = accessor.Push(SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-outer"));
        accessor.Enrich(publishAttemptId: outerAttempt);
        var inner = SpaceExecutionContext.ForSystem(
            Tenant,
            "space-worker:test",
            Guid.NewGuid(),
            "trace-inner");

        var innerScope = accessor.Push(inner);
        accessor.Enrich(publishAttemptId: innerAttempt);
        Assert.Equal(innerAttempt, accessor.OutcomeCurrent!.PublishAttemptId);
        innerScope.Dispose();

        Assert.Equal(outerAttempt, accessor.Current!.PublishAttemptId);
        Assert.Equal(outerAttempt, accessor.OutcomeCurrent!.PublishAttemptId);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("correlation")]
    [InlineData("actor-type")]
    [InlineData("actor-id")]
    [InlineData("actor-name")]
    [InlineData("organization")]
    public void PushDerived_rejects_identity_changes_without_polluting_outcome(
        string changedField)
    {
        var accessor = new SpaceExecutionContextAccessor();
        var root = SpaceExecutionContext.ForUser(
            Tenant,
            "u-1",
            "alice",
            Correlation,
            "trace-a",
            "org-1");
        using var rootScope = accessor.Push(root);
        var candidate = changedField switch
        {
            "tenant" => root with { TenantId = Guid.NewGuid() },
            "correlation" => root with { CorrelationId = Guid.NewGuid() },
            "actor-type" => root with
            {
                ActorType = SpaceExecutionContext.SystemActor
            },
            "actor-id" => root with { ActorId = "u-2" },
            "actor-name" => root with { ActorName = "bob" },
            "organization" => root with
            {
                OrganizationContextId = "org-2"
            },
            _ => throw new InvalidOperationException(
                "Unexpected test case")
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => accessor.PushDerived(candidate));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", error.Message);
        Assert.Same(root, accessor.Current);
        Assert.Same(root, accessor.OutcomeCurrent);
    }

    [Fact]
    public void Factories_validate_required_identity_fields()
    {
        Assert.Throws<ArgumentException>(
            () => SpaceExecutionContext.ForUser(
                Guid.Empty, "u-1", "alice", Correlation, "trace-a"));
        Assert.Throws<ArgumentException>(
            () => SpaceExecutionContext.ForUser(
                Tenant, "u-1", "alice", Guid.Empty, "trace-a"));
        Assert.Throws<ArgumentException>(
            () => SpaceExecutionContext.ForUser(
                Tenant, "u-1", "alice", Correlation, " "));
        Assert.Throws<ArgumentException>(
            () => SpaceExecutionContext.ForUser(
                Tenant, " ", "alice", Correlation, "trace-a"));
    }

    [Fact]
    public void ForSystem_uses_stable_system_actor_and_identifiers()
    {
        var job = Guid.NewGuid();
        var run = Guid.NewGuid();
        var attempt = Guid.NewGuid();

        var context = SpaceExecutionContext.ForSystem(
            Tenant,
            "space-worker:test",
            Correlation,
            "trace-worker",
            job,
            run,
            attempt);

        Assert.Equal(SpaceExecutionContext.SystemActor, context.ActorType);
        Assert.Equal("space-worker:test", context.ActorId);
        Assert.Equal("space-worker:test", context.ActorName);
        Assert.Equal(job, context.JobId);
        Assert.Equal(run, context.RunId);
        Assert.Equal(attempt, context.PublishAttemptId);
        Assert.Null(context.OrganizationContextId);
    }

    [Fact]
    public async Task Current_flows_across_await_and_exceptional_nested_scope_restores_parent()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var outer = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var inner = SpaceExecutionContext.ForSystem(
            Tenant, "space-worker:test", Correlation, "trace-worker");

        using (accessor.Push(outer))
        {
            await Task.Yield();
            Assert.Same(outer, accessor.Current);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using (accessor.Push(inner))
                {
                    await Task.Yield();
                    Assert.Same(inner, accessor.Current);
                    throw new InvalidOperationException("expected");
                }
            });

            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Enrich_in_awaited_child_is_visible_to_parent_caller()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var parent = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var jobId = Guid.NewGuid();
        var publishAttemptId = Guid.NewGuid();

        using var scope = accessor.Push(parent);

        await EnrichAfterYieldAsync(accessor, jobId, publishAttemptId);

        Assert.Equal(jobId, accessor.Current!.JobId);
        Assert.Equal(publishAttemptId, accessor.Current.PublishAttemptId);
    }

    [Fact]
    public async Task Parallel_children_same_enrichment_is_idempotent_and_conflict_preserves_first_value()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var parent = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a");
        var firstJob = Guid.NewGuid();
        var conflictingJob = Guid.NewGuid();

        using var scope = accessor.Push(parent);
        accessor.Enrich(jobId: firstJob);

        var same = Task.Run(() => accessor.Enrich(jobId: firstJob));
        var conflict = Task.Run(() => Assert.Throws<InvalidOperationException>(
            () => accessor.Enrich(jobId: conflictingJob)));
        await same;
        var conflictError = await conflict;

        Assert.Equal(
            "SPACE_EXECUTION_CONTEXT_CONFLICT",
            conflictError.Message);
        Assert.Equal(firstJob, accessor.Current!.JobId);
    }

    [Fact]
    public async Task Parallel_children_with_independent_pushes_do_not_pollute_each_other_or_parent()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var parent = SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-parent");
        var first = SpaceExecutionContext.ForSystem(
            Tenant, "space-worker:first", Guid.NewGuid(), "trace-first");
        var second = SpaceExecutionContext.ForSystem(
            Tenant, "space-worker:second", Guid.NewGuid(), "trace-second");
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var parentScope = accessor.Push(parent);

        var firstTask = Task.Run(async () =>
        {
            using (accessor.Push(first))
            {
                await start.Task;
                await Task.Yield();
                return accessor.Current;
            }
        });
        var secondTask = Task.Run(async () =>
        {
            using (accessor.Push(second))
            {
                await start.Task;
                await Task.Yield();
                return accessor.Current;
            }
        });

        start.SetResult();
        var values = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(first, values[0]);
        Assert.Same(second, values[1]);
        Assert.Same(parent, accessor.Current);
    }

    [Fact]
    public async Task Disposed_scope_is_cleared_from_previously_captured_branch()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var scope = accessor.Push(SpaceExecutionContext.ForUser(
            Tenant, "u-1", "alice", Correlation, "trace-a"));

        var captured = Task.Run(async () =>
        {
            await release.Task;
            return (accessor.Current, accessor.OutcomeCurrent);
        });

        scope.Dispose();
        release.SetResult();

        var capturedValues = await captured;
        Assert.Null(capturedValues.Current);
        Assert.Null(capturedValues.OutcomeCurrent);
        Assert.Null(accessor.Current);
        Assert.Null(accessor.OutcomeCurrent);
    }

    private static async Task EnrichAfterYieldAsync(
        SpaceExecutionContextAccessor accessor,
        Guid jobId,
        Guid publishAttemptId)
    {
        await Task.Yield();
        accessor.Enrich(
            jobId: jobId,
            publishAttemptId: publishAttemptId);
    }
}
