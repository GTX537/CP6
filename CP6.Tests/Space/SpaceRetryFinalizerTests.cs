using System.Text.RegularExpressions;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Space;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Space;

public sealed class SpaceRetryFinalizerTests
{
    private static readonly Guid Tenant =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Finalizer_commits_audit_and_owned_event_together()
    {
        await using var database = NewDatabase();
        var leaseId = Guid.NewGuid();
        var eventId = await SeedOwnedEventAsync(
            database.Options,
            database.Tenant,
            leaseId,
            attempts: 2);
        var accessor = NewAccessor(out var execution);
        using (execution)
        {
            var finalizer = NewFinalizer(
                new TestFactory(
                    database.Options,
                    database.Tenant),
                accessor);

            var result = await finalizer.TryFinalizeAsync(
                Input(
                    eventId,
                    leaseId,
                    expectedAttempts: 2,
                    IntegrationEventStatus.Success,
                    null,
                    null,
                    SpaceAuditOutcome.Succeeded));

            Assert.Equal(
                SpaceRetryFinalizationResult.Committed,
                result);
        }

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        var saved = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Success,
            saved.Status);
        Assert.Null(saved.RetryLeaseId);
        Assert.Null(saved.NextRetryAt);
        var audit = await assertDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(
            SpaceAuditOutcome.Succeeded,
            audit.Outcome);
        Assert.Equal(2, audit.AttemptNo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finalizer_rolls_back_audit_when_fence_update_matches_zero_rows(
        bool mismatchLease)
    {
        await using var database = NewDatabase();
        var leaseId = Guid.NewGuid();
        var eventId = await SeedOwnedEventAsync(
            database.Options,
            database.Tenant,
            leaseId,
            attempts: 3);
        var accessor = NewAccessor(out var execution);
        using (execution)
        {
            var finalizer = NewFinalizer(
                new TestFactory(
                    database.Options,
                    database.Tenant),
                accessor);

            var result = await finalizer.TryFinalizeAsync(
                Input(
                    eventId,
                    mismatchLease ? Guid.NewGuid() : leaseId,
                    mismatchLease ? 3 : 2,
                    IntegrationEventStatus.DeadLetter,
                    "SPACE_RETRY_DEAD_LETTER",
                    null,
                    SpaceAuditOutcome.Failed));

            Assert.Equal(
                SpaceRetryFinalizationResult.LostLease,
                result);
        }

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Empty(await assertDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .ToListAsync());
        var saved = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.Equal(3, saved.Attempts);
        Assert.Equal(leaseId, saved.RetryLeaseId);
    }

    [Fact]
    public async Task Finalizer_leaves_event_unchanged_when_audit_save_fails()
    {
        await using var database = NewDatabase();
        var leaseId = Guid.NewGuid();
        var eventId = await SeedOwnedEventAsync(
            database.Options,
            database.Tenant,
            leaseId,
            attempts: 1);
        var accessor = NewAccessor(out var execution);
        using (execution)
        {
            var finalizer = NewFinalizer(
                new TestFactory(
                    database.Options,
                    database.Tenant,
                    failAuditSave: true),
                accessor);

            var result = await finalizer.TryFinalizeAsync(
                Input(
                    eventId,
                    leaseId,
                    expectedAttempts: 1,
                    IntegrationEventStatus.Success,
                    null,
                    null,
                    SpaceAuditOutcome.Succeeded));

            Assert.Equal(
                SpaceRetryFinalizationResult.AuditUnavailable,
                result);
        }

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Empty(await assertDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .ToListAsync());
        var saved = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.Equal(leaseId, saved.RetryLeaseId);
        Assert.Equal(1, saved.Attempts);
    }

    [Fact]
    public async Task Finalizer_rejects_succeeded_audit_for_dead_letter()
    {
        await using var database = NewDatabase();
        var leaseId = Guid.NewGuid();
        var eventId = await SeedOwnedEventAsync(
            database.Options,
            database.Tenant,
            leaseId,
            attempts: 5);
        var accessor = NewAccessor(out var execution);
        using (execution)
        {
            var finalizer = NewFinalizer(
                new TestFactory(
                    database.Options,
                    database.Tenant),
                accessor);

            var result = await finalizer.TryFinalizeAsync(
                Input(
                    eventId,
                    leaseId,
                    expectedAttempts: 5,
                    IntegrationEventStatus.DeadLetter,
                    "SPACE_RETRY_DEAD_LETTER",
                    null,
                    SpaceAuditOutcome.Succeeded));

            Assert.Equal(
                SpaceRetryFinalizationResult.AuditUnavailable,
                result);
        }

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Empty(await assertDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .ToListAsync());
        Assert.Equal(
            IntegrationEventStatus.Failed,
            (await assertDb.IntegrationEvents
                .IgnoreQueryFilters()
                .SingleAsync()).Status);
    }

    [Fact]
    public async Task Finalizer_recovers_commit_unknown_with_stable_completion_audit_id()
    {
        await using var database = NewDatabase();
        var completionLeaseId = Guid.NewGuid();
        var startedAuditId = Guid.NewGuid();
        var eventId = await SeedOwnedEventAsync(
            database.Options,
            database.Tenant,
            completionLeaseId,
            attempts: 5);
        var accessor = NewAccessor(out var execution);
        using (execution)
        {
            await using (var seedDb =
                         new CP6Context(
                             database.Options,
                             database.Tenant))
            {
                var evt = await seedDb.IntegrationEvents
                    .IgnoreQueryFilters()
                    .SingleAsync();
                evt.RetryCompletionLeaseId =
                    completionLeaseId;
                evt.RetryCompletionSucceeded = true;
                seedDb.SpaceAuditEvents.Add(
                    SpaceAuditWriter.Materialize(
                        new SpaceAuditEventInput(
                            "space.integration-event.retry",
                            "IntegrationEvent",
                            eventId.ToString(),
                            SpaceAuditOutcome.Started,
                            AttemptNo: 5,
                            ClientType: "Worker"),
                        accessor.RequireCurrent(),
                        DateTime.UtcNow,
                        auditId: startedAuditId));
                await seedDb.SaveChangesAsync();
            }

            var commitUnknown =
                new CommitUnknownInterceptor();
            var finalizerOptions =
                new DbContextOptionsBuilder<CP6Context>(
                        database.Options)
                    .AddInterceptors(commitUnknown)
                    .Options;
            var finalizer = NewFinalizer(
                new TestFactory(
                    finalizerOptions,
                    database.Tenant),
                accessor);
            commitUnknown.Arm();

            var result = await finalizer.TryFinalizeAsync(
                Input(
                    eventId,
                    completionLeaseId,
                    expectedAttempts: 5,
                    IntegrationEventStatus.Success,
                    null,
                    null,
                    SpaceAuditOutcome.Succeeded,
                    expectedCompletionLeaseId:
                        completionLeaseId,
                    expectedCompletionSucceeded: true));

            Assert.Equal(
                SpaceRetryFinalizationResult.Committed,
                result);
        }

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        var saved = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Success,
            saved.Status);
        Assert.Null(saved.RetryLeaseId);
        Assert.Null(saved.RetryCompletionLeaseId);
        Assert.Null(saved.RetryCompletionSucceeded);
        var audits = await assertDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(
            audits,
            audit => audit.Id == startedAuditId &&
                     audit.Outcome ==
                         SpaceAuditOutcome.Started);
        Assert.Contains(
            audits,
            audit => audit.Id == completionLeaseId &&
                     audit.Outcome ==
                         SpaceAuditOutcome.Succeeded);
    }

    private static SpaceRetryFinalizer NewFinalizer(
        ISpaceAuditDbContextFactory factory,
        ISpaceExecutionContextAccessor accessor) =>
        new(
            factory,
            accessor,
            NullLogger<SpaceRetryFinalizer>.Instance);

    private static SpaceRetryFinalizationInput Input(
        Guid eventId,
        Guid leaseId,
        int expectedAttempts,
        string status,
        string? error,
        DateTime? nextRetryAt,
        string outcome,
        Guid? expectedCompletionLeaseId = null,
        bool? expectedCompletionSucceeded = null) =>
        new(
            eventId,
            Tenant,
            leaseId,
            expectedAttempts,
            status,
            error,
            nextRetryAt,
            new SpaceAuditEventInput(
                "space.integration-event.retry",
                "IntegrationEvent",
                eventId.ToString(),
                outcome,
                ReasonCode: error,
                Evidence: new SpaceAuditEvidence(
                    Status: status),
                AttemptNo: expectedAttempts,
                ClientType: "Worker"),
            AuditId: leaseId,
            ExpectedCompletionLeaseId:
                expectedCompletionLeaseId,
            ExpectedCompletionSucceeded:
                expectedCompletionSucceeded);

    private static SpaceExecutionContextAccessor NewAccessor(
        out IDisposable execution)
    {
        var accessor = new SpaceExecutionContextAccessor();
        execution = accessor.Push(
            SpaceExecutionContext.ForSystem(
                Tenant,
                "space-worker:test",
                Guid.NewGuid(),
                "0123456789abcdef",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()));
        return accessor;
    }

    private static async Task<Guid> SeedOwnedEventAsync(
        DbContextOptions<CP6Context> options,
        ITenantContext tenant,
        Guid leaseId,
        int attempts)
    {
        var eventId = Guid.NewGuid();
        await using var db = new CP6Context(options, tenant);
        db.IntegrationEvents.Add(new IntegrationEvent
        {
            Id = eventId,
            TenantId = Tenant,
            SourceModule = "SPACE",
            TargetModule = "WMS",
            HookName = "OnLocationPublishedAsync",
            SourceNo = "LPUB-FINALIZER",
            Status = IntegrationEventStatus.Failed,
            Attempts = attempts,
            NextRetryAt = DateTime.UtcNow.AddMinutes(15),
            RetryLeaseId = leaseId,
            CorrelationId = Guid.NewGuid(),
            PayloadJson = """{"batchNo":"LPUB-FINALIZER","items":[]}""",
            Creator = "test",
            CreateDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return eventId;
    }

    private static TestDatabase NewDatabase()
    {
        var name = $"space-finalizer-{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source={name};Mode=Memory;Cache=Shared";
        var anchor = new SqliteConnection(connectionString);
        anchor.Open();
        var options =
            new DbContextOptionsBuilder<CP6Context>()
                .UseSqlite(connectionString)
                .Options;
        var tenant =
            new TenantContext { CurrentTenantId = Tenant };
        using (var setup = new CP6Context(options, tenant))
        {
            var script = Regex.Replace(
                setup.Database.GenerateCreateScript(),
                "n?varchar\\(max\\)",
                "TEXT",
                RegexOptions.IgnoreCase);
            using var command = anchor.CreateCommand();
            command.CommandText = script;
            command.ExecuteNonQuery();
        }
        return new TestDatabase(anchor, options, tenant);
    }

    private sealed record TestDatabase(
        SqliteConnection Anchor,
        DbContextOptions<CP6Context> Options,
        TenantContext Tenant) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            Anchor.DisposeAsync();
    }

    private sealed class TestFactory :
        ISpaceAuditDbContextFactory
    {
        private readonly DbContextOptions<CP6Context> _options;
        private readonly ITenantContext _tenant;
        private readonly bool _failAuditSave;

        public TestFactory(
            DbContextOptions<CP6Context> options,
            ITenantContext tenant,
            bool failAuditSave = false)
        {
            _options = options;
            _tenant = tenant;
            _failAuditSave = failAuditSave;
        }

        public CP6Context CreateDbContext() =>
            _failAuditSave
                ? new AuditSaveFailingContext(
                    _options,
                    _tenant)
                : new CP6Context(_options, _tenant);
    }

    private sealed class AuditSaveFailingContext :
        CP6Context
    {
        public AuditSaveFailingContext(
            DbContextOptions<CP6Context> options,
            ITenantContext tenant)
            : base(options, tenant)
        {
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChangeTracker
                .Entries<Space_AuditEvent>()
                .Any(x => x.State == EntityState.Added))
            {
                throw new DbUpdateException(
                    "secret audit storage failure");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CommitUnknownInterceptor :
        DbTransactionInterceptor
    {
        private int _armed;

        public void Arm() =>
            Interlocked.Exchange(ref _armed, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                throw new TimeoutException(
                    "simulated acknowledgement loss after commit");
            }
            return Task.CompletedTask;
        }
    }
}
