using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Tests.Space;

public sealed class SpaceRetryLeaseMigrationTests
{
    [Fact]
    public void Retry_lease_migration_is_after_audit_and_has_reversible_contract()
    {
        var migration =
            new SpaceIntegrationEventRetryLeaseFence();
        var id = migration.GetType()
            .GetCustomAttribute<MigrationAttribute>()!
            .Id;
        Assert.True(
            string.CompareOrdinal(
                id,
                "20260725144609_SpaceE00S04ObservabilityAudit") >
            0);

        var up = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Up", up);
        var add = Assert.Single(
            up.Operations.OfType<AddColumnOperation>());
        Assert.Equal("RetryLeaseId", add.Name);
        Assert.Equal("T_IntegrationEvent", add.Table);
        Assert.True(add.IsNullable);
        var createIndex = Assert.Single(
            up.Operations.OfType<CreateIndexOperation>());
        Assert.Equal(
            "IX_T_IntegrationEvent_TenantId_RetryLeaseId",
            createIndex.Name);
        Assert.Equal(
            ["TenantId", "RetryLeaseId"],
            createIndex.Columns);

        var down = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Down", down);
        Assert.Single(
            down.Operations.OfType<DropIndexOperation>());
        var drop = Assert.Single(
            down.Operations.OfType<DropColumnOperation>());
        Assert.Equal("RetryLeaseId", drop.Name);
        Assert.Equal("T_IntegrationEvent", drop.Table);
    }

    [Fact]
    public void Completion_and_dead_letter_outbox_migration_is_strictly_incremental()
    {
        var migration =
            new SpaceRetryCompletionAndDeadLetterOutbox();
        var id = migration.GetType()
            .GetCustomAttribute<MigrationAttribute>()!
            .Id;
        Assert.Equal(
            "20260725181400_SpaceRetryCompletionAndDeadLetterOutbox",
            id);
        Assert.True(
            string.CompareOrdinal(
                id,
                "20260725174242_SpaceIntegrationEventRetryLeaseFence") >
            0);

        var up = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Up", up);
        var added = up.Operations
            .OfType<AddColumnOperation>()
            .ToDictionary(operation => operation.Name);
        Assert.Equal(
            [
                "DeadLetterNotificationLeaseId",
                "DeadLetterNotificationLeaseUntilUtc",
                "DeadLetterNotifiedAtUtc",
                "RetryCompletionLeaseId",
                "RetryCompletionSucceeded",
            ],
            added.Keys.OrderBy(name => name));
        Assert.All(added.Values, operation =>
        {
            Assert.Equal("T_IntegrationEvent", operation.Table);
            Assert.True(operation.IsNullable);
        });

        var createIndex = Assert.Single(
            up.Operations.OfType<CreateIndexOperation>());
        Assert.Equal(
            "IX_T_IntegrationEvent_TenantId_Status_DeadLetterNotifiedAtUtc_DeadLetterNotificationLeaseUntilUtc",
            createIndex.Name);
        Assert.Equal(
            [
                "TenantId",
                "Status",
                "DeadLetterNotifiedAtUtc",
                "DeadLetterNotificationLeaseUntilUtc",
            ],
            createIndex.Columns);
        var baseline = Assert.Single(
            up.Operations.OfType<SqlOperation>());
        Assert.Equal(
            "UPDATE [T_IntegrationEvent] " +
            "SET [DeadLetterNotifiedAtUtc] = SYSUTCDATETIME() " +
            "WHERE [SourceModule] = N'SPACE' " +
            "AND [Status] = N'DEAD' " +
            "AND [DeadLetterNotifiedAtUtc] IS NULL;",
            Regex.Replace(
                baseline.Sql,
                @"\s+",
                " ").Trim());
        Assert.True(
            up.Operations.IndexOf(baseline) <
            up.Operations.IndexOf(createIndex));

        var down = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Down", down);
        Assert.Single(
            down.Operations.OfType<DropIndexOperation>());
        Assert.Empty(
            down.Operations.OfType<SqlOperation>());
        Assert.Equal(
            added.Keys.OrderBy(name => name),
            down.Operations
                .OfType<DropColumnOperation>()
                .Select(operation => operation.Name)
                .OrderBy(name => name));
    }

    [Fact]
    public async Task Completion_outbox_baseline_updates_only_historical_space_dead_rows()
    {
        var migration =
            new SpaceRetryCompletionAndDeadLetterOutbox();
        var up = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Up", up);
        var baseline = Assert.Single(
            up.Operations.OfType<SqlOperation>());

        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText =
                """
                CREATE TABLE "T_IntegrationEvent" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "SourceModule" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "DeadLetterNotifiedAtUtc" TEXT NULL
                );
                INSERT INTO "T_IntegrationEvent" VALUES
                    ('space-dead-pending', 'SPACE', 'DEAD', NULL),
                    ('mes-dead-pending', 'MES', 'DEAD', NULL),
                    ('space-failed-pending', 'SPACE', 'FAILED', NULL),
                    ('space-dead-notified', 'SPACE', 'DEAD', '2026-01-01T00:00:00Z');
                """;
            await setup.ExecuteNonQueryAsync();
        }

        await using (var apply = connection.CreateCommand())
        {
            apply.CommandText = baseline.Sql
                .Replace(
                    "SYSUTCDATETIME()",
                    "CURRENT_TIMESTAMP",
                    StringComparison.Ordinal)
                .Replace(
                    "N'",
                    "'",
                    StringComparison.Ordinal);
            Assert.Equal(1, await apply.ExecuteNonQueryAsync());
        }

        var values = new Dictionary<string, string?>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText =
                """
                SELECT "Id", "DeadLetterNotifiedAtUtc"
                FROM "T_IntegrationEvent"
                ORDER BY "Id";
                """;
            await using var reader =
                await query.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(
                    reader.GetString(0),
                    reader.IsDBNull(1)
                        ? null
                        : reader.GetString(1));
            }
        }

        Assert.NotNull(values["space-dead-pending"]);
        Assert.Null(values["mes-dead-pending"]);
        Assert.Null(values["space-failed-pending"]);
        Assert.Equal(
            "2026-01-01T00:00:00Z",
            values["space-dead-notified"]);
    }

    [Fact]
    public void Occurred_at_utc_migration_is_strict_incremental_and_reversible()
    {
        var migration =
            new SpaceIntegrationEventOccurredAtUtc();
        var id = migration.GetType()
            .GetCustomAttribute<MigrationAttribute>()!
            .Id;
        Assert.Equal(
            "20260725203000_SpaceIntegrationEventOccurredAtUtc",
            id);
        Assert.True(
            string.CompareOrdinal(
                id,
                "20260725181400_SpaceRetryCompletionAndDeadLetterOutbox") >
            0);

        var up = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Up", up);
        var add = Assert.Single(
            up.Operations.OfType<AddColumnOperation>());
        Assert.Equal("OccurredAtUtc", add.Name);
        Assert.Equal("T_IntegrationEvent", add.Table);
        Assert.True(add.IsNullable);
        var baseline = Assert.Single(
            up.Operations.OfType<SqlOperation>());
        Assert.Contains(
            "[SourceModule] = N'SPACE'",
            baseline.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "[OccurredAtUtc] IS NULL",
            baseline.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "[JobId] <> [Id]",
            baseline.Sql,
            StringComparison.Ordinal);

        var indexes = up.Operations
            .OfType<CreateIndexOperation>()
            .OrderBy(x => x.Name)
            .ToList();
        Assert.Equal(2, indexes.Count);
        Assert.Equal(
            [
                "TenantId",
                "SourceModule",
                "CorrelationId",
                "OccurredAtUtc",
                "Id",
            ],
            indexes[0].Columns);
        Assert.Equal(
            [false, false, false, true, true],
            indexes[0].IsDescending);
        Assert.Equal(
            [
                "TenantId",
                "SourceModule",
                "OccurredAtUtc",
                "Id",
            ],
            indexes[1].Columns);
        Assert.Equal(
            [false, false, true, true],
            indexes[1].IsDescending);

        var down = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        Invoke(migration, "Down", down);
        Assert.Equal(
            2,
            down.Operations.OfType<DropIndexOperation>().Count());
        var drop = Assert.Single(
            down.Operations.OfType<DropColumnOperation>());
        Assert.Equal("OccurredAtUtc", drop.Name);
        Assert.Equal("T_IntegrationEvent", drop.Table);
    }

    private static void Invoke(
        Migration migration,
        string method,
        MigrationBuilder builder)
    {
        migration.GetType()
            .GetMethod(
                method,
                BindingFlags.Instance |
                BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
    }
}
