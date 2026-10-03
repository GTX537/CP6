using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceDesignV1SqlServerTests(SpaceRelationalFixture database, ITestOutputHelper output)
{
    private const string KeyHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string RequestHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [SpaceMigrationFact]
    public async Task Migration_creates_only_the_idempotency_table_and_indexes()
    {
        await WithMigrationDatabaseAsync(async context =>
        {
            var table = await context.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT [name] AS [Value]
                    FROM sys.tables
                    WHERE [name] = 'Space_IdempotencyRecord'
                    """)
                .SingleAsync();
            Assert.Equal("Space_IdempotencyRecord", table);

            var indexes = await context.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT [name] AS [Value]
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID('Space_IdempotencyRecord')
                      AND [name] IS NOT NULL
                    """)
                .ToListAsync();
            Assert.Contains(
                "IX_Space_IdempotencyRecord_Tenant_Retention",
                indexes);
            Assert.Contains(
                "UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key",
                indexes);

            var migration = await context.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT [MigrationId] AS [Value]
                    FROM [__EFMigrationsHistory_Space]
                    WHERE [MigrationId] =
                        '20260726092519_SpaceE01S05DesignApiIdempotency'
                    """)
                .SingleAsync();
            Assert.Equal(
                "20260726092519_SpaceE01S05DesignApiIdempotency",
                migration);
        });
    }

    [SqlServerFact]
    public async Task Idempotency_key_is_unique_per_tenant_principal_and_operation()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            context.IdempotencyRecords.Add(
                NewRecord(
                    execution.TenantId,
                    execution.ActorId,
                    clock.UtcNow));
            await context.SaveChangesAsync();

            context.IdempotencyRecords.Add(
                NewRecord(
                    execution.TenantId,
                    execution.ActorId,
                    clock.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            context.ChangeTracker.Clear();
            context.IdempotencyRecords.Add(
                NewRecord(
                    execution.TenantId,
                    Guid.NewGuid(),
                    clock.UtcNow));
            await context.SaveChangesAsync();

            Assert.Equal(
                2,
                await context.IdempotencyRecords.CountAsync());
        });
    }

    private static SpaceIdempotencyRecord NewRecord(
        Guid tenantId,
        Guid principalId,
        DateTime nowUtc) =>
        SpaceIdempotencyRecord.Create(
            tenantId,
            principalId,
            "create-version:site",
            KeyHash,
            RequestHash,
            """{"id":"result"}""",
            202,
            nowUtc.AddHours(24),
            nowUtc.AddDays(90));

    private async Task WithMigrationDatabaseAsync(Func<SpaceContext, Task> action)
    {
        var selected = await SpaceMigrationTestDatabase.OpenIfSelectedAsync(output);
        if (selected is null)
        {
            await WithLegacyDatabaseAsync((context, _, _) => action(context));
            return;
        }

        await using var context = selected.CreateContext(
            new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid()), new TestClock());
        await selected.MigrateAsync(context);
        try
        {
            await action(context);
        }
        finally
        {
            await selected.RecordStateAsync(context, "design-idempotency-final");
        }
    }

    private async Task WithDatabaseAsync(
        Func<SpaceContext, TestExecutionContext, TestClock, Task> action)
    {
        if (!SpaceRelationalFixture.IsSelected)
        {
            await WithLegacyDatabaseAsync(action);
            return;
        }

        database.WriteSetupEvidence(output);
        var execution = new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid());
        var clock = new TestClock();
        await using var context = database.CreateSpaceContext(execution, clock,
            new SpaceNativeFailureObserver(database.Database.Provider, output.WriteLine, "design-idempotency"));
        await action(context, execution, clock);
    }

    private static async Task WithLegacyDatabaseAsync(
        Func<SpaceContext, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceDesignV1_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        await using var context = new SpaceContext(
            options,
            execution,
            clock);
        try
        {
            await context.Database.MigrateAsync();
            await action(context, execution, clock);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }
}
