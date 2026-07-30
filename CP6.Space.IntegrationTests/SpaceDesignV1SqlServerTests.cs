using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDesignV1SqlServerTests
{
    private const string KeyHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string RequestHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [SqlServerFact]
    public async Task Migration_creates_only_the_idempotency_table_and_indexes()
    {
        await WithDatabaseAsync(async (context, _, _) =>
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

    private static async Task WithDatabaseAsync(
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
