using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(Bug150RelationalCollection.Name)]
public sealed class SpaceMappingProfileConcurrencyTests(Bug150RelationalFixture database, ITestOutputHelper output)
{
    [Bug150RelationalFact]
    public async Task Cad_same_key_concurrent_create_returns_one_profile_and_rejects_changed_payload()
    {
        var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
        var clock = new TestClock();
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        var request = new SaveSpaceCadMappingProfileRequest(null, $"BUG-150 CAD {Guid.NewGuid():N}", true,
            system.Rules, CopyFromProfileId: system.ProfileId, CopyFromVersion: system.Version);
        var key = $"bug150-cad-{Guid.NewGuid():N}";
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await RaceAsync(execution, clock, (context, token) =>
            new SpaceCadMappingProfileService(context, execution, clock).SaveProfileAsync(request, key, token), budget);

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
        Assert.Equal(JsonSerializer.Serialize(responses[0] with { IdempotentReplay = false }),
            JsonSerializer.Serialize(responses[1] with { IdempotentReplay = false }));

        await using var verify = database.CreateContext(execution, clock);
        var profile = Assert.Single(await verify.CadMappingProfiles.AsNoTracking().ToArrayAsync());
        var version = Assert.Single(await verify.CadMappingProfileVersions.AsNoTracking().ToArrayAsync());
        var ledger = Assert.Single(await verify.IdempotencyRecords.AsNoTracking().ToArrayAsync());
        Assert.Equal(responses[0].Profile.Id, profile.Id);
        Assert.Equal(1, profile.CurrentVersion);
        Assert.Equal(profile.Id, version.ProfileId);
        Assert.Equal(1, version.Version);
        Assert.Equal(execution.ActorId, ledger.PrincipalId);
        Assert.Equal("space.cad-mapping-profile.save", ledger.Operation);
        Assert.Equal(200, ledger.HttpStatusCode);
        var before = JsonSerializer.Serialize(new { profile, version, ledger });
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            new SpaceCadMappingProfileService(verify, execution, clock)
                .SaveProfileAsync(request with { IsEnabled = false }, key));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(SpaceErrorCodes.IdempotencyConflict, conflict.Code);

        await using var after = database.CreateContext(execution, clock);
        Assert.Equal(before, JsonSerializer.Serialize(new
        {
            profile = Assert.Single(await after.CadMappingProfiles.AsNoTracking().ToArrayAsync()),
            version = Assert.Single(await after.CadMappingProfileVersions.AsNoTracking().ToArrayAsync()),
            ledger = Assert.Single(await after.IdempotencyRecords.AsNoTracking().ToArrayAsync())
        }));
    }

    [Bug150RelationalFact]
    public async Task Excel_same_key_concurrent_create_returns_one_profile_and_rejects_changed_payload()
    {
        var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
        var clock = new TestClock();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        SaveSpaceExcelMappingProfileRequest request;
        await using (var setup = database.CreateContext(execution, clock))
        {
            var system = await new SpaceExcelMappingService(setup, execution, clock)
                .GetProfileAsync(SpaceExcelMappingService.SystemStandardProfileId, cancellationToken: budget.Token);
            request = new(null, $"BUG-150 Excel {Guid.NewGuid():N}", system.Definition,
                CopyFromProfileId: system.Id, CopyFromVersion: system.Version);
        }
        var key = $"bug150-excel-{Guid.NewGuid():N}";
        var responses = await RaceAsync(execution, clock, (context, token) =>
            new SpaceExcelMappingService(context, execution, clock).SaveProfileAsync(request, key, token), budget);

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
        Assert.Equal(JsonSerializer.Serialize(responses[0] with { IdempotentReplay = false }),
            JsonSerializer.Serialize(responses[1] with { IdempotentReplay = false }));

        await using var verify = database.CreateContext(execution, clock);
        var profile = Assert.Single(await verify.ExcelMappingProfiles.AsNoTracking().ToArrayAsync());
        var version = Assert.Single(await verify.ExcelMappingProfileVersions.AsNoTracking().ToArrayAsync());
        var ledger = Assert.Single(await verify.IdempotencyRecords.AsNoTracking().ToArrayAsync());
        Assert.Equal(responses[0].Profile.Id, profile.Id);
        Assert.Equal(1, profile.CurrentVersion);
        Assert.Equal(profile.Id, version.ProfileId);
        Assert.Equal(1, version.Version);
        Assert.Equal(execution.ActorId, ledger.PrincipalId);
        Assert.Equal("space.excel-mapping-profile.save", ledger.Operation);
        Assert.Equal(200, ledger.HttpStatusCode);
        var before = JsonSerializer.Serialize(new { profile, version, ledger });
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            new SpaceExcelMappingService(verify, execution, clock)
                .SaveProfileAsync(request with { Name = request.Name + " changed" }, key));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(SpaceErrorCodes.IdempotencyConflict, conflict.Code);

        await using var after = database.CreateContext(execution, clock);
        Assert.Equal(before, JsonSerializer.Serialize(new
        {
            profile = Assert.Single(await after.ExcelMappingProfiles.AsNoTracking().ToArrayAsync()),
            version = Assert.Single(await after.ExcelMappingProfileVersions.AsNoTracking().ToArrayAsync()),
            ledger = Assert.Single(await after.IdempotencyRecords.AsNoTracking().ToArrayAsync())
        }));
    }

    private async Task<T[]> RaceAsync<T>(TestExecution execution, TestClock clock,
        Func<SpaceContext, CancellationToken, Task<T>> save, CancellationTokenSource budget)
    {
        database.WriteEvidence(output);
        var barrier = new FirstProfileSaveBarrier();
        var observer = new NativeFailureObserver(database.Database.Provider, output);
        await using var first = database.CreateContext(execution, clock, barrier, observer);
        await using var second = database.CreateContext(execution, clock, barrier, observer);
        Task<T>[] started = [];
        try
        {
            await first.Database.OpenConnectionAsync(budget.Token);
            await second.Database.OpenConnectionAsync(budget.Token);
            Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
            var firstSession = await SessionIdAsync(first, budget.Token);
            var secondSession = await SessionIdAsync(second, budget.Token);
            Assert.NotEqual(firstSession, secondSession);
            output.WriteLine($"Physical sessions={firstSession},{secondSession}; first-save barrier; no test-layer retry.");
            started = [save(first, budget.Token), save(second, budget.Token)];
            var arrived = await Task.WhenAny(barrier.BothArrived, started[0], started[1])
                .WaitAsync(TimeSpan.FromSeconds(15), budget.Token);
            if (arrived != barrier.BothArrived)
            {
                await arrived;
                throw new InvalidOperationException("A mapping save completed before both first-save participants arrived.");
            }
            Assert.Equal(2, barrier.ParticipantCount);
            barrier.Release();
            return await Task.WhenAll(started).WaitAsync(budget.Token);
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
                // The body preserves the original failure; join both sessions before context disposal.
            }
        }
    }

    private static async Task<int> SessionIdAsync(SpaceContext context, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = context.Database.IsNpgsql() ? "SELECT pg_backend_pid()" : "SELECT @@SPID";
        command.CommandTimeout = 15;
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    private sealed class FirstProfileSaveBarrier : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> participants = new();
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int participantCount;
        public Task BothArrived => bothArrived.Task;
        public int ParticipantCount => Volatile.Read(ref participantCount);
        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = Assert.IsType<SpaceContext>(eventData.Context);
            if (participants.TryAdd(context.ContextId.InstanceId, 0))
            {
                var transaction = Assert.IsAssignableFrom<IDbContextTransaction>(context.Database.CurrentTransaction);
                Assert.Equal(IsolationLevel.Serializable, transaction.GetDbTransaction().IsolationLevel);
                var added = context.ChangeTracker.Entries().Where(item => item.State == EntityState.Added).ToArray();
                Assert.Single(added.Where(item => item.Entity is SpaceCadMappingProfile or SpaceExcelMappingProfile));
                Assert.Single(added.Where(item => item.Entity is SpaceCadMappingProfileVersion or SpaceExcelMappingProfileVersion));
                Assert.DoesNotContain(added, item => item.Entity is SpaceIdempotencyRecord);
                if (Interlocked.Increment(ref participantCount) == 2) bothArrived.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class NativeFailureObserver(DatabaseProvider provider, ITestOutputHelper output) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var failure = DatabaseFailureClassifier.Classify(eventData.Exception);
            output.WriteLine($"Provider={provider}; Stage=mapping-save; SQLSTATE={failure.SqlState ?? "none"}; NativeCode={failure.DatabaseErrorCode?.ToString() ?? "none"}; Kind={failure.Kind}.");
            return Task.CompletedTask;
        }
    }

    private sealed record TestExecution(Guid TenantId, Guid ActorId) : ISpaceExecutionContext;
    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    }
}
