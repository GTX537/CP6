using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Space;

public sealed class SpaceAuditWriterTests
{
    private static readonly Guid Tenant =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Correlation =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Job =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Run =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PublishAttempt =
        Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Writer_maps_context_and_only_serializes_typed_evidence()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        var logger = new CapturingLogger<SpaceAuditWriter>();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(factory, accessor, logger);
        var floorId = Guid.NewGuid();

        var input = new SpaceAuditEventInput(
            Action: "space.floor.publish",
            ResourceType: "Floor",
            ResourceId: floorId.ToString(),
            Outcome: SpaceAuditOutcome.Started,
            Evidence: new SpaceAuditEvidence(
                PermissionCode: "space-publish:publish",
                AuthorizationResult: "Allowed",
                ItemCount: 3,
                Status: "Pending"));

        Assert.True(await writer.TryAppendAsync(input));

        await using var assertDb = factory.CreateDbContext();
        var row = await assertDb.SpaceAuditEvents.SingleAsync();
        Assert.Equal(Tenant, row.TenantId);
        Assert.Equal(Correlation, row.CorrelationId);
        Assert.Equal("trace-user", row.TraceId);
        Assert.Equal(SpaceExecutionContext.UserActor, row.ActorType);
        Assert.Equal("u-1", row.ActorId);
        Assert.Equal("alice", row.ActorName);
        Assert.Equal("org-1", row.OrganizationContextId);
        Assert.Equal(Job, row.JobId);
        Assert.Equal(Run, row.RunId);
        Assert.Equal(PublishAttempt, row.PublishAttemptId);
        Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, row.CreateDate.Kind);
        Assert.Contains("space-publish:publish", row.AuthorizationEvidenceJson);
        Assert.DoesNotContain("requestBody", row.AuthorizationEvidenceJson);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Writer_uses_current_for_started_and_latest_outcome_for_final_result()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var rootScope = accessor.Push(UserContext());
        var secondJob = Guid.NewGuid();
        var secondAttempt = Guid.NewGuid();
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
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        Assert.True(await writer.TryAppendAsync(
            BasicInput() with { Outcome = SpaceAuditOutcome.Started }));
        Assert.True(await writer.TryAppendAsync(
            BasicInput() with { Outcome = SpaceAuditOutcome.Failed }));

        await using var assertDb = factory.CreateDbContext();
        var rows = await assertDb.SpaceAuditEvents.ToListAsync();
        var started = Assert.Single(
            rows,
            row => row.Outcome == SpaceAuditOutcome.Started);
        var failed = Assert.Single(
            rows,
            row => row.Outcome == SpaceAuditOutcome.Failed);
        Assert.Equal(Job, started.JobId);
        Assert.Equal(PublishAttempt, started.PublishAttemptId);
        Assert.Equal(secondJob, failed.JobId);
        Assert.Equal(secondAttempt, failed.PublishAttemptId);
    }

    [Fact]
    public async Task Writer_uses_a_new_db_context_for_each_append()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var inner = NewFactory(tenantContext);
        var factory = new CountingFactory(inner);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        Assert.True(await writer.TryAppendAsync(BasicInput()));
        Assert.True(await writer.TryAppendAsync(BasicInput()));

        Assert.Equal(2, factory.CreateCount);
        await using var assertDb = inner.CreateDbContext();
        Assert.Equal(2, await assertDb.SpaceAuditEvents.CountAsync());
    }

    [Fact]
    public async Task Writer_truncates_fields_and_replaces_oversized_evidence()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext() with
        {
            ActorName = new string('n', 140),
            OrganizationContextId = new string('o', 140),
        });
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        var result = await writer.TryAppendAsync(new SpaceAuditEventInput(
            Action: new string('a', 140),
            ResourceType: new string('r', 90),
            ResourceId: new string('i', 150),
            Outcome: SpaceAuditOutcome.Failed,
            ReasonCode: new string('R', 100),
            Evidence: new SpaceAuditEvidence(Status: new string('e', 9_000)),
            ClientType: new string('c', 50),
            IpAddress: new string('p', 80),
            UserAgent: new string('u', 300)));

        Assert.True(result);
        await using var assertDb = factory.CreateDbContext();
        var row = await assertDb.SpaceAuditEvents.SingleAsync();
        Assert.Equal(100, row.Action.Length);
        Assert.Equal(64, row.ResourceType.Length);
        Assert.Equal(128, row.ResourceId!.Length);
        Assert.Equal(100, row.ActorName!.Length);
        Assert.Equal(100, row.OrganizationContextId!.Length);
        Assert.Equal(100, row.ReasonCode!.Length);
        Assert.Equal(32, row.ClientType!.Length);
        Assert.Equal(64, row.IpAddress!.Length);
        Assert.Equal(256, row.UserAgent!.Length);
        Assert.Equal("""{"status":"EvidenceTruncated"}""", row.AuthorizationEvidenceJson);
    }

    [Fact]
    public async Task Writer_returns_false_when_factory_tenant_does_not_match_execution_tenant()
    {
        var wrongTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var tenantContext = new TenantContext { CurrentTenantId = wrongTenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        var logger = new CapturingLogger<SpaceAuditWriter>();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(factory, accessor, logger);

        Assert.False(await writer.TryAppendAsync(BasicInput()));

        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.IgnoreQueryFilters().ToListAsync());
        Assert.Single(logger.Messages);
        Assert.DoesNotContain(Tenant.ToString(), logger.Messages[0]);
        Assert.DoesNotContain(wrongTenant.ToString(), logger.Messages[0]);
    }

    [Fact]
    public async Task Writer_failure_returns_false_and_log_contains_no_exception_message()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var logger = new CapturingLogger<SpaceAuditWriter>();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            new ThrowingFactory(
                new InvalidOperationException("secret request body bearer-token")),
            accessor,
            logger);

        var result = await writer.TryAppendAsync(BasicInput());

        Assert.False(result);
        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request body", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SPACE_AUDIT_WRITE_FAILED", message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.Matches("[0-9A-F]{64}", message);
        var state = Assert.Single(logger.States);
        Assert.Equal(Correlation, state["CorrelationId"]);
        Assert.DoesNotContain(
            "secret",
            string.Join("|", state.Values),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "Floor", "Started")]
    [InlineData(" ", "Floor", "Started")]
    [InlineData("space.floor.publish", "", "Started")]
    [InlineData("space.floor.publish", " ", "Started")]
    [InlineData("space.floor.publish", "Floor", "Unknown")]
    public async Task Writer_fails_closed_for_missing_required_values_or_invalid_outcome(
        string action,
        string resourceType,
        string outcome)
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        var result = await writer.TryAppendAsync(new SpaceAuditEventInput(
            action,
            resourceType,
            null,
            outcome));

        Assert.False(result);
        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public async Task Writer_fails_closed_for_invalid_sha256_hash(string invalidHash)
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        Assert.False(await writer.TryAppendAsync(
            BasicInput() with { BeforeHash = invalidHash }));

        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Writer_fails_closed_for_invalid_evidence_fingerprint()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        Assert.False(await writer.TryAppendAsync(
            BasicInput() with
            {
                Evidence = new SpaceAuditEvidence(
                    ExceptionType: nameof(InvalidOperationException),
                    ErrorFingerprint: "not-a-sha256"),
            }));

        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("secret request body")]
    [InlineData("SPACE_PERMISSION_DENIED\nsecret-token")]
    [InlineData("SPACE:PERMISSION:DENIED")]
    [InlineData("1_SPACE_PERMISSION_DENIED")]
    public async Task Writer_rejects_unstable_reason_code_without_persisting_or_echoing(
        string reasonCode)
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        var logger = new CapturingLogger<SpaceAuditWriter>();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(factory, accessor, logger);

        Assert.False(await writer.TryAppendAsync(
            BasicInput() with { ReasonCode = reasonCode }));

        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.ToListAsync());
        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain(
            reasonCode,
            message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "secret-token",
            message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writer_rejects_reason_code_above_storage_limit_instead_of_truncating()
    {
        var tenantContext = new TenantContext { CurrentTenantId = Tenant };
        var factory = NewFactory(tenantContext);
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            factory,
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        Assert.False(await writer.TryAppendAsync(
            BasicInput() with { ReasonCode = $"S{new string('A', 100)}" }));

        await using var assertDb = factory.CreateDbContext();
        Assert.Empty(await assertDb.SpaceAuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Writer_propagates_cancellation_when_requested()
    {
        var accessor = new SpaceExecutionContextAccessor();
        using var execution = accessor.Push(UserContext());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var writer = new SpaceAuditWriter(
            new ThrowingFactory(new OperationCanceledException(cts.Token)),
            accessor,
            new CapturingLogger<SpaceAuditWriter>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.TryAppendAsync(BasicInput(), cts.Token));
    }

    [Fact]
    public async Task Writer_treats_unrequested_operation_cancellation_as_safe_failure()
    {
        var accessor = new SpaceExecutionContextAccessor();
        var logger = new CapturingLogger<SpaceAuditWriter>();
        using var execution = accessor.Push(UserContext());
        var writer = new SpaceAuditWriter(
            new ThrowingFactory(new OperationCanceledException("secret cancellation detail")),
            accessor,
            logger);

        Assert.False(await writer.TryAppendAsync(BasicInput()));
        Assert.DoesNotContain(
            "secret cancellation detail",
            Assert.Single(logger.Messages),
            StringComparison.OrdinalIgnoreCase);
    }

    private static SpaceExecutionContext UserContext() =>
        SpaceExecutionContext.ForUser(
            Tenant,
            "u-1",
            "alice",
            Correlation,
            "trace-user",
            "org-1") with
        {
            JobId = Job,
            RunId = Run,
            PublishAttemptId = PublishAttempt,
        };

    private static SpaceAuditEventInput BasicInput() =>
        new(
            Action: "space.floor.publish",
            ResourceType: "Floor",
            ResourceId: Guid.NewGuid().ToString(),
            Outcome: SpaceAuditOutcome.Started);

    private static TestFactory NewFactory(TenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestFactory(options, tenant);
    }

    private sealed class TestFactory : ISpaceAuditDbContextFactory
    {
        private readonly DbContextOptions<CP6Context> _options;
        private readonly ITenantContext _tenant;

        public TestFactory(
            DbContextOptions<CP6Context> options,
            ITenantContext tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        public CP6Context CreateDbContext() =>
            new(_options, _tenant, new StubCurrentUser());
    }

    private sealed class CountingFactory : ISpaceAuditDbContextFactory
    {
        private readonly ISpaceAuditDbContextFactory _inner;

        public CountingFactory(ISpaceAuditDbContextFactory inner) => _inner = inner;

        public int CreateCount { get; private set; }

        public CP6Context CreateDbContext()
        {
            CreateCount++;
            return _inner.CreateDbContext();
        }
    }

    private sealed class ThrowingFactory : ISpaceAuditDbContextFactory
    {
        private readonly Exception _exception;

        public ThrowingFactory(Exception exception) => _exception = exception;

        public CP6Context CreateDbContext() => throw _exception;
    }

    private sealed class StubCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId => null;
        public string? UserName => null;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> States { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            var values = Assert.IsAssignableFrom<
                IEnumerable<KeyValuePair<string, object?>>>(state);
            States.Add(values.ToDictionary(x => x.Key, x => x.Value));
            Assert.Null(exception);
        }
    }
}
