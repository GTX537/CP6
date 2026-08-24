using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceEditLeaseSqlServerTests
{
    [SqlServerFact]
    public async Task Lifecycle_uses_database_time_and_fences_browser_sessions()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId, versionId, floorId) =>
        {
            var actorId = Guid.NewGuid();
            var firstClient = Guid.NewGuid();
            var secondClient = Guid.NewGuid();
            var execution = new TestExecutionContext(
                tenantId,
                actorId,
                "Warehouse editor",
                "integration-test");
            var skewedClock = new FixedClock(
                new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            await using var context = CreateContext(
                connectionString,
                execution,
                skewedClock);
            var service = new SpaceEditLeaseService(
                context,
                execution,
                skewedClock,
                new AllowSiteAccess(siteId),
                new FixedCorrelation(Guid.NewGuid()));

            var acquired = await service.AcquireAsync(
                versionId,
                floorId,
                new AcquireSpaceEditLeaseRequest(firstClient));
            Assert.NotNull(acquired.LeaseId);
            Assert.Equal("Warehouse editor", acquired.HolderDisplayName);
            Assert.True(acquired.IsOwnedByCurrentActor);
            Assert.InRange(
                acquired.AcquiredAtUtc!.Value,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow.AddMinutes(1));

            var redacted = await service.GetAsync(versionId, floorId);
            Assert.True(redacted.IsOwnedByCurrentActor);
            Assert.Null(redacted.LeaseId);
            Assert.Null(redacted.ClientInstanceId);
            Assert.Null(redacted.RowVersion);

            var wrongSession = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.RenewAsync(
                    versionId,
                    floorId,
                    acquired.LeaseId!.Value,
                    new ContinueSpaceEditLeaseRequest(secondClient)));
            Assert.Equal(SpaceErrorCodes.EditLeaseLost, wrongSession.Code);

            var renewed = await service.RenewAsync(
                versionId,
                floorId,
                acquired.LeaseId.Value,
                new ContinueSpaceEditLeaseRequest(firstClient));
            Assert.Equal(acquired.LeaseId, renewed.LeaseId);

            var takeover = await service.TakeoverAsync(
                versionId,
                floorId,
                new TakeoverSpaceEditLeaseRequest(
                    secondClient,
                    "Recovered the abandoned browser tab"));
            Assert.NotEqual(acquired.LeaseId, takeover.LeaseId);
            Assert.Equal(secondClient, takeover.ClientInstanceId);

            var staleRelease = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.ReleaseAsync(
                    versionId,
                    floorId,
                    acquired.LeaseId.Value,
                    new ContinueSpaceEditLeaseRequest(firstClient)));
            Assert.Equal(SpaceErrorCodes.EditLeaseLost, staleRelease.Code);

            var released = await service.ReleaseAsync(
                versionId,
                floorId,
                takeover.LeaseId!.Value,
                new ContinueSpaceEditLeaseRequest(secondClient));
            Assert.True(released.IsAvailable);

            var audit = await context.EditLeaseTakeoverAudits
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal("Recovered the abandoned browser tab", audit.Reason);
            Assert.Equal("integration-test", audit.RequestSource);
            Assert.NotEqual(Guid.Empty, audit.CorrelationId);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_acquire_preserves_one_floor_slot()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId, versionId, floorId) =>
        {
            async Task<SpaceEditLeaseDto> AcquireAsync(Guid actorId)
            {
                var execution = new TestExecutionContext(
                    tenantId,
                    actorId,
                    actorId.ToString("N"),
                    "concurrent-test");
                var clock = new FixedClock(DateTime.UtcNow);
                await using var context = CreateContext(
                    connectionString,
                    execution,
                    clock);
                var service = new SpaceEditLeaseService(
                    context,
                    execution,
                    clock,
                    new AllowSiteAccess(siteId));
                return await service.AcquireAsync(
                    versionId,
                    floorId,
                    new AcquireSpaceEditLeaseRequest(Guid.NewGuid()));
            }

            var attempts = await Task.WhenAll(
                CaptureAsync(() => AcquireAsync(Guid.NewGuid())),
                CaptureAsync(() => AcquireAsync(Guid.NewGuid())));

            Assert.Single(attempts, result => result.Lease is not null);
            var denied = Assert.Single(attempts, result => result.Problem is not null);
            Assert.Equal(SpaceErrorCodes.EditLeaseHeld, denied.Problem!.Code);

            var readExecution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                "reader",
                "integration-test");
            await using var verify = CreateContext(
                connectionString,
                readExecution,
                new FixedClock(DateTime.UtcNow));
            Assert.Single(await verify.EditLeases.AsNoTracking().ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Expired_lease_can_be_reacquired_using_database_time()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId, versionId, floorId) =>
        {
            var firstActor = Guid.NewGuid();
            var firstClient = Guid.NewGuid();
            var firstExecution = new TestExecutionContext(
                tenantId,
                firstActor,
                "First editor",
                "expiry-test");
            var skewedClock = new FixedClock(
                new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await using (var firstContext = CreateContext(
                             connectionString,
                             firstExecution,
                             skewedClock))
            {
                var firstService = NewService(
                    firstContext,
                    firstExecution,
                    skewedClock,
                    siteId);
                await firstService.AcquireAsync(
                    versionId,
                    floorId,
                    new AcquireSpaceEditLeaseRequest(firstClient));
                await firstContext.Database.ExecuteSqlRawAsync(
                    "UPDATE [Space_EditLease] SET [ExpiresAtUtc] = DATEADD(second, -1, SYSUTCDATETIME())");
            }

            var secondExecution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                "Second editor",
                "expiry-test");
            await using var secondContext = CreateContext(
                connectionString,
                secondExecution,
                skewedClock);
            var secondService = NewService(
                secondContext,
                secondExecution,
                skewedClock,
                siteId);
            var reacquired = await secondService.AcquireAsync(
                versionId,
                floorId,
                new AcquireSpaceEditLeaseRequest(Guid.NewGuid()));

            Assert.Equal(secondExecution.ActorId, reacquired.OwnerUserId);
            Assert.Equal("Second editor", reacquired.HolderDisplayName);
            Assert.InRange(
                reacquired.ExpiresAtUtc!.Value,
                DateTime.UtcNow.AddSeconds(80),
                DateTime.UtcNow.AddSeconds(100));
        });
    }

    [SqlServerFact]
    public async Task Renew_and_takeover_compete_without_deadlock_or_duplicate_slot()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId, versionId, floorId) =>
        {
            var ownerExecution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                "Lease owner",
                "race-test");
            var clientId = Guid.NewGuid();
            var clock = new FixedClock(DateTime.UtcNow);
            Guid leaseId;
            await using (var ownerContext = CreateContext(
                             connectionString,
                             ownerExecution,
                             clock))
            {
                var ownerService = NewService(
                    ownerContext,
                    ownerExecution,
                    clock,
                    siteId);
                leaseId = (await ownerService.AcquireAsync(
                    versionId,
                    floorId,
                    new AcquireSpaceEditLeaseRequest(clientId))).LeaseId!.Value;
            }

            var takeoverExecution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                "Recovery editor",
                "race-test");
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var renewTask = CaptureAsync(async () =>
            {
                await using var context = CreateContext(
                    connectionString,
                    ownerExecution,
                    clock);
                await start.Task;
                return await NewService(
                    context,
                    ownerExecution,
                    clock,
                    siteId).RenewAsync(
                    versionId,
                    floorId,
                    leaseId,
                    new ContinueSpaceEditLeaseRequest(clientId));
            });
            var takeoverTask = CaptureAsync(async () =>
            {
                await using var context = CreateContext(
                    connectionString,
                    takeoverExecution,
                    clock);
                await start.Task;
                return await NewService(
                    context,
                    takeoverExecution,
                    clock,
                    siteId).TakeoverAsync(
                    versionId,
                    floorId,
                    new TakeoverSpaceEditLeaseRequest(
                        Guid.NewGuid(),
                        "Concurrent session recovery"));
            });
            start.SetResult();
            var outcomes = await Task.WhenAll(renewTask, takeoverTask)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Contains(outcomes, result =>
                result.Lease?.OwnerUserId == takeoverExecution.ActorId);
            Assert.DoesNotContain(outcomes, result =>
                result.Problem is { StatusCode: >= 500 });
            await using var verify = CreateContext(
                connectionString,
                takeoverExecution,
                clock);
            var active = Assert.Single(await verify.EditLeases.AsNoTracking().ToListAsync());
            Assert.Equal(takeoverExecution.ActorId, active.OwnerUserId);
            Assert.Single(await verify.EditLeaseTakeoverAudits.AsNoTracking().ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Sql_takeover_audit_is_immutable_for_async_update_and_delete()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId, versionId, floorId) =>
        {
            var execution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                "Initial editor",
                "audit-test");
            var clock = new FixedClock(DateTime.UtcNow);
            await using var context = CreateContext(connectionString, execution, clock);
            var service = NewService(context, execution, clock, siteId);
            await service.AcquireAsync(
                versionId,
                floorId,
                new AcquireSpaceEditLeaseRequest(Guid.NewGuid()));

            var takeoverExecution = execution with
            {
                ActorId = Guid.NewGuid(),
                ActorDisplayName = "Recovery editor",
            };
            await using (var takeoverContext = CreateContext(
                             connectionString,
                             takeoverExecution,
                             clock))
            {
                await NewService(
                    takeoverContext,
                    takeoverExecution,
                    clock,
                    siteId).TakeoverAsync(
                    versionId,
                    floorId,
                    new TakeoverSpaceEditLeaseRequest(
                        Guid.NewGuid(),
                        "Audit immutability test"));
            }

            context.ChangeTracker.Clear();
            var audit = await context.EditLeaseTakeoverAudits.SingleAsync();
            context.Entry(audit).Property(item => item.Reason).CurrentValue =
                "Tampered";
            var updateError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync());
            Assert.Equal(
                "Edit lease takeover audit records are immutable.",
                updateError.Message);

            context.ChangeTracker.Clear();
            audit = await context.EditLeaseTakeoverAudits.SingleAsync();
            context.Remove(audit);
            var deleteError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync());
            Assert.Equal(
                "Edit lease takeover audit records are immutable.",
                deleteError.Message);
        });
    }

    private static async Task<LeaseAttempt> CaptureAsync(
        Func<Task<SpaceEditLeaseDto>> action)
    {
        try
        {
            return new LeaseAttempt(await action(), null);
        }
        catch (SpaceProblemException problem)
        {
            return new LeaseAttempt(null, problem);
        }
    }

    private static SpaceEditLeaseService NewService(
        SpaceContext context,
        TestExecutionContext execution,
        ISpaceClock clock,
        Guid siteId) =>
        new(
            context,
            execution,
            clock,
            new AllowSiteAccess(siteId),
            new FixedCorrelation(Guid.NewGuid()));

    private static async Task WithDatabaseAsync(
        Func<string, Guid, Guid, Guid, Guid, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceLease_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var execution = new TestExecutionContext(
            tenantId,
            actorId,
            "setup",
            "integration-test");
        var clock = new FixedClock(DateTime.UtcNow);
        await using var setup = CreateContext(connectionString, execution, clock);
        try
        {
            await setup.Database.MigrateAsync();
            var siteId = Guid.NewGuid();
            var model = SpaceModel.Create(tenantId, siteId);
            var published = SpaceModelVersion.CreateDraft(
                tenantId,
                model.Id,
                1,
                "Published");
            published.BeginValidation();
            published.MarkReady(
                new string('a', 64),
                "space-v1",
                new string('b', 64));
            published.BeginPublishing();
            published.MarkPublished(actorId, clock.UtcNow);
            setup.AddRange(model, published);
            await setup.SaveChangesAsync();
            model.BeginCutover(Guid.NewGuid());
            model.MarkFrozen();
            model.MarkBootstrapping();
            model.MarkVerified(published);
            model.ActivateDesignV1();
            await setup.SaveChangesAsync();
            var draft = SpaceModelVersion.CreateDraft(
                tenantId,
                model.Id,
                2,
                "Draft",
                published.Id);
            model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                tenantId,
                draft.Id,
                Guid.NewGuid(),
                siteId,
                1,
                "F1",
                "Floor 1",
                0,
                6000);
            setup.AddRange(draft, floor);
            await setup.SaveChangesAsync();
            model.ReserveDraft(draft);
            await setup.SaveChangesAsync();

            await action(
                connectionString,
                tenantId,
                siteId,
                draft.Id,
                floor.LogicalId);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        ISpaceExecutionContext execution,
        ISpaceClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        string? ActorDisplayName,
        string RequestSource) : ISpaceExecutionContext;

    private sealed record FixedCorrelation(Guid CorrelationId) :
        ISpaceCorrelationContext;

    private sealed record FixedClock(DateTime UtcNow) : ISpaceClock;

    private sealed class AllowSiteAccess(Guid siteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid candidateSiteId, bool write)
        {
            if (candidateSiteId != siteId)
                throw new UnauthorizedAccessException();
        }
    }

    private sealed record LeaseAttempt(
        SpaceEditLeaseDto? Lease,
        SpaceProblemException? Problem);
}
