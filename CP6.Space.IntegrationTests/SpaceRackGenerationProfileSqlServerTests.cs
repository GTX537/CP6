using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceRackGenerationProfileSqlServerTests(
    SpaceRelationalFixture database,
    ITestOutputHelper output)
{
    [SqlServerFact]
    public async Task Store_is_migrated_idempotent_immutable_and_tenant_scoped()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid versionId;
            await using (var context = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewService(context, execution, clock);
                var request = Request("TENANT-RACK");
                var created = await service.CreateAsync(request, "create-1");
                var replay = await service.CreateAsync(request, "create-1");
                versionId = created.Profile.LatestVersion.Id;

                Assert.False(created.IdempotentReplay);
                Assert.True(replay.IdempotentReplay);
                Assert.Equal(created.Profile.Id, replay.Profile.Id);
                Assert.NotEmpty(created.Profile.RowVersion);
                Assert.NotEmpty(created.Profile.LatestVersion.RowVersion);

                var duplicate = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.CreateAsync(request, "create-2"));
                Assert.Equal(
                    SpaceErrorCodes.RackGenerationProfileConflict,
                    duplicate.Code);

                var profile = await context.RackGenerationProfiles.SingleAsync();
                context.Entry(profile).State = EntityState.Modified;
                var immutable = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.SaveChangesAsync());
                Assert.Contains(
                    "immutable",
                    immutable.Message,
                    StringComparison.OrdinalIgnoreCase);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using (var otherContext = CreateContext(
                             connectionString,
                             otherExecution,
                             clock))
            {
                var otherService = NewService(
                    otherContext,
                    otherExecution,
                    clock);
                var hidden = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => otherService.GetVersionAsync(versionId));
                Assert.Equal(
                    SpaceErrorCodes.RackGenerationProfileNotFound,
                    hidden.Code);

                var other = await otherService.CreateAsync(
                    Request("TENANT-RACK"),
                    "other-tenant-create");
                Assert.NotEqual(versionId, other.Profile.LatestVersion.Id);
            }

            await using (var constraintContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                    constraintContext.Database.ExecuteSqlInterpolatedAsync(
                        constraintContext.Database.IsNpgsql() ?
                        (FormattableString)$"""
                        INSERT INTO "Space_RackGenerationProfile"
                            ("Id", "Scope", "OwnerTenantId", "ProfileCode",
                             "Name", "Description", "Status", "CreatedAtUtc",
                             "CreatedBy", "IsDeleted")
                        VALUES
                            ({Guid.NewGuid()},
                             {(short)SpaceRackGenerationProfileScope.System},
                             {execution.TenantId}, 'INVALID-SYSTEM',
                             'Invalid system owner', NULL,
                             {(short)SpaceRackGenerationProfileStatus.Active},
                             {clock.UtcNow}, {execution.ActorId}, FALSE);
                        """ :
                        $"""
                        INSERT INTO [Space_RackGenerationProfile]
                            ([Id], [Scope], [OwnerTenantId], [ProfileCode],
                             [Name], [Description], [Status], [CreatedAtUtc],
                             [CreatedBy], [IsDeleted])
                        VALUES
                            ({Guid.NewGuid()},
                             {(short)SpaceRackGenerationProfileScope.System},
                             {execution.TenantId}, N'INVALID-SYSTEM',
                             N'Invalid system owner', NULL,
                             {(short)SpaceRackGenerationProfileStatus.Active},
                             {clock.UtcNow}, {execution.ActorId}, 0);
                        """));
                var failure = DatabaseFailureClassifier.Classify(error);
                Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
                Assert.True(failure.MatchesConstraint("CK_Space_RackGenerationProfile_ScopeOwner"));
                if (constraintContext.Database.IsNpgsql())
                {
                    Assert.IsType<PostgresException>(error);
                    Assert.Equal("23514", failure.SqlState);
                }
                else
                {
                    Assert.IsType<SqlException>(error);
                    Assert.Equal(547, failure.DatabaseErrorCode);
                }
            }
        });
    }

    private static SpaceRackGenerationProfileService NewService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock) =>
        new(context, execution, clock, new TestCursorCodec());

    private static CreateSpaceRackGenerationProfileRequest Request(
        string code) =>
        new(
            code,
            "Tenant rack",
            2400,
            1000,
            5000,
            [new(1, 0, 2200, 4, 2, 600, 500, 100, 1000)]);

    private async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            database.WriteSetupEvidence(output);
            await action(database.ConnectionString,
                new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid()), new TestClock());
            return;
        }

        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceRackProfiles_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await setup.Database.MigrateAsync();
            await ExecuteIdempotentMigrationScriptTwiceAsync(setup);
            await action(connectionString, execution, clock);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task ExecuteIdempotentMigrationScriptTwiceAsync(
        SpaceContext context)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var scriptPath = Path.Combine(
            repositoryRoot,
            "CP6.Space.Infrastructure",
            "Migrations",
            "Scripts",
            "20260808164544_SpaceE13RackGenerationProfiles.sql");
        var batches = Regex.Split(
                await File.ReadAllTextAsync(scriptPath),
                @"(?im)^\s*GO\s*$")
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

        await context.Database.OpenConnectionAsync();
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var batch in batches)
                    await context.Database.ExecuteSqlRawAsync(batch);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            Assert.True(string.Equals(database.ConnectionString, connectionString, StringComparison.Ordinal),
                "Selected rack profile contexts must use the fixture-owned connection.");
            return database.CreateSpaceContext(execution, clock,
                new SpaceNativeFailureObserver(database.Database.Provider, output.WriteLine, "rack-profile-business"));
        }

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
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } =
            new(2026, 8, 8, 20, 0, 0, DateTimeKind.Utc);
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }
}
