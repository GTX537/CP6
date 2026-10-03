using System.Collections.Concurrent;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceCadMappingProfileSqlServerTests(
    SpaceRelationalFixture database,
    ITestOutputHelper output)
{
    [SqlServerFact]
    public async Task Sql_migration_persists_rowversion_versions_and_tenant_scope()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantA = Guid.NewGuid();
            Guid profileId;
            await using (var fixture = CreateContext(connectionString, tenantA))
            {
                var service = new SpaceCadMappingProfileService(
                    fixture.Context,
                    fixture.Execution,
                    fixture.Clock);
                var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
                var created = await service.SaveProfileAsync(
                    new(
                        null,
                        "SQL CAD mapping",
                        true,
                        system.Rules,
                        CopyFromProfileId: system.ProfileId,
                        CopyFromVersion: system.Version),
                    "sql-cad-mapping-v1");
                Assert.False(string.IsNullOrEmpty(created.Profile.RowVersion));
                profileId = created.Profile.Id;

                var updated = await service.SaveProfileAsync(
                    new(
                        profileId,
                        "SQL CAD mapping",
                        false,
                        created.Profile.Rules,
                        created.Profile.RowVersion),
                    "sql-cad-mapping-v2");
                Assert.Equal(2, updated.Profile.Version);
                Assert.False(updated.Profile.IsEnabled);
                Assert.NotEqual(created.Profile.RowVersion, updated.Profile.RowVersion);
            }

            await using (var verify = CreateContext(connectionString, tenantA))
            {
                Assert.Equal(
                    new[] { 1, 2 },
                    await verify.Context.CadMappingProfileVersions
                        .Where(item => item.ProfileId == profileId)
                        .OrderBy(item => item.Version)
                        .Select(item => item.Version)
                        .ToArrayAsync());
            }

            await using (var tenantB = CreateContext(
                connectionString,
                Guid.NewGuid()))
            {
                Assert.Empty(await tenantB.Context.CadMappingProfiles.ToListAsync());
                Assert.Empty(await tenantB.Context.CadMappingProfileVersions.ToListAsync());
            }
        });
    }

    [SqlServerFact]
    public async Task Concurrent_same_key_create_returns_one_profile_and_replay()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenant = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
            var request = new SaveSpaceCadMappingProfileRequest(
                null,
                $"Concurrent CAD {tenant:N}",
                true,
                system.Rules,
                CopyFromProfileId: system.ProfileId,
                CopyFromVersion: system.Version);
            var key = $"concurrent-cad-{Guid.NewGuid():N}";
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var barrier = new FirstProfileSaveBarrier();
            await using var first = CreateContext(connectionString, tenant, actor, barrier);
            await using var second = CreateContext(connectionString, tenant, actor, barrier);
            Task<SaveSpaceCadMappingProfileResponse>[] started = [];
            try
            {
                await first.Context.Database.OpenConnectionAsync(budget.Token);
                await second.Context.Database.OpenConnectionAsync(budget.Token);
                Assert.NotSame(first.Context.Database.GetDbConnection(), second.Context.Database.GetDbConnection());
                var firstSession = await SessionIdAsync(first.Context, budget.Token);
                var secondSession = await SessionIdAsync(second.Context, budget.Token);
                Assert.NotEqual(firstSession, secondSession);
                output.WriteLine($"CAD mapping physical sessions: {firstSession}, {secondSession}.");

                var firstService = new SpaceCadMappingProfileService(first.Context, first.Execution, first.Clock);
                var secondService = new SpaceCadMappingProfileService(second.Context, second.Execution, second.Clock);
                started =
                [
                    firstService.SaveProfileAsync(request, key, budget.Token),
                    secondService.SaveProfileAsync(request, key, budget.Token),
                ];
                var arrivalOrCompletion = await Task.WhenAny(barrier.BothArrived, started[0], started[1])
                    .WaitAsync(budget.Token);
                if (arrivalOrCompletion != barrier.BothArrived)
                {
                    await arrivalOrCompletion;
                    throw new InvalidOperationException("A mapping command completed before both first-save participants arrived.");
                }
                Assert.NotNull(first.Context.Database.CurrentTransaction);
                Assert.NotNull(second.Context.Database.CurrentTransaction);
                output.WriteLine("Both commands read empty idempotency before their first profile INSERT; no test retry.");
                barrier.Release();
                var responses = await Task.WhenAll(started).WaitAsync(budget.Token);
                Assert.Single(responses.Where(item => !item.IdempotentReplay));
                Assert.Single(responses.Where(item => item.IdempotentReplay));
                Assert.All(responses, item =>
                {
                    Assert.True(item.Created);
                    Assert.Equal(1, item.Profile.Version);
                    Assert.False(string.IsNullOrEmpty(item.Profile.RowVersion));
                });
                Assert.Equal(responses[0].Profile.Id, responses[1].Profile.Id);
                Assert.Equal(responses[0].Profile.RowVersion, responses[1].Profile.RowVersion);

                await using var verify = CreateContext(connectionString, tenant, actor);
                var profile = Assert.Single(await verify.Context.CadMappingProfiles.AsNoTracking().ToArrayAsync(budget.Token));
                Assert.Equal(responses[0].Profile.Id, profile.Id);
                Assert.Equal(1, profile.CurrentVersion);
                var version = Assert.Single(await verify.Context.CadMappingProfileVersions.AsNoTracking().ToArrayAsync(budget.Token));
                Assert.Equal(profile.Id, version.ProfileId);
                Assert.Equal(1, version.Version);
                Assert.Single(await verify.Context.IdempotencyRecords.AsNoTracking().Where(item =>
                    item.PrincipalId == actor && item.Operation == "space.cad-mapping-profile.save").ToArrayAsync(budget.Token));

                var verifyService = new SpaceCadMappingProfileService(verify.Context, verify.Execution, verify.Clock);
                var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                    verifyService.SaveProfileAsync(request with { IsEnabled = false }, key, budget.Token));
                Assert.Equal(409, conflict.StatusCode);
                Assert.Equal(SpaceErrorCodes.IdempotencyConflict, conflict.Code);
                Assert.Equal(1, await verify.Context.CadMappingProfiles.CountAsync(budget.Token));
                Assert.Equal(1, await verify.Context.CadMappingProfileVersions.CountAsync(budget.Token));
                Assert.Equal(1, await verify.Context.IdempotencyRecords.CountAsync(budget.Token));
            }
            finally
            {
                barrier.Release();
                await budget.CancelAsync();
                try
                {
                    await Task.WhenAll(started);
                }
                catch
                {
                    // The original native failure is propagated by the test body; every participant is joined here.
                }
            }
        });
    }

    private static async Task<int> SessionIdAsync(SpaceContext context, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = context.Database.IsNpgsql() ? "SELECT pg_backend_pid()" : "SELECT @@SPID";
        command.CommandTimeout = 30;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed class FirstProfileSaveBarrier : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> participants = new();
        private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivalCount;

        public Task BothArrived => arrived.Task;
        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = Assert.IsType<SpaceContext>(eventData.Context);
            if (participants.TryAdd(context.ContextId.InstanceId, 0))
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                if (Interlocked.Increment(ref arrivalCount) == 2)
                    arrived.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private async Task WithDatabaseAsync(Func<string, Task> action)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            database.WriteSetupEvidence(output);
            await action(database.ConnectionString);
            return;
        }

        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_CadMap_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var migration = CreateContext(
            connectionString,
            Guid.NewGuid());
        try
        {
            await migration.Context.Database.MigrateAsync();
            await action(connectionString);
        }
        finally
        {
            await migration.Context.Database.EnsureDeletedAsync();
        }
    }

    private ContextFixture CreateContext(
        string connectionString,
        Guid tenantId,
        Guid? actorId = null,
        params IInterceptor[] interceptors)
    {
        var execution = new TestExecutionContext(tenantId, actorId ?? Guid.NewGuid());
        var clock = new FixedClock();
        if (SpaceRelationalFixture.IsSelected)
        {
            Assert.True(string.Equals(database.ConnectionString, connectionString, StringComparison.Ordinal),
                "Selected cad-mapping contexts must use the fixture-owned connection.");
            IInterceptor[] allInterceptors =
            [
                new SpaceNativeFailureObserver(database.Database.Provider, output.WriteLine, "cad-mapping-business"),
                .. interceptors,
            ];
            return new ContextFixture(database.CreateSpaceContext(execution, clock, allInterceptors),
                execution, clock);
        }

        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable));
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        var context = new SpaceContext(
            options.Options,
            execution,
            clock);
        return new ContextFixture(context, execution, clock);
    }

    private sealed record ContextFixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        FixedClock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow { get; } =
            new(2026, 8, 16, 15, 0, 0, DateTimeKind.Utc);
    }
}
