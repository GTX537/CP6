using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePublishRecoveryMigrationTests(ITestOutputHelper output)
{
    [SpaceMigrationFact]
    public async Task Recovery_migration_refuses_an_active_E06_S03_publish()
    {
        var selected = await SpaceMigrationTestDatabase.OpenIfSelectedAsync(output);
        var tenantId = Guid.NewGuid();
        var connectionString = selected?.ConnectionString ?? new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)!)
        {
            InitialCatalog = $"CP6SpaceE06S04Gate_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = selected is null
            ? CreateContext(connectionString, tenantId)
            : selected.CreateContext(new TestExecutionContext(tenantId, Guid.NewGuid()), new SystemSpaceClock());
        try
        {
            const string historicalStart = "20260807135544_SpaceE06S03PublishOrchestration";
            if (selected is null)
                await context.Database.GetService<IMigrator>().MigrateAsync(historicalStart);
            else
                await selected.MigrateAsync(context, historicalStart);
            await context.Database.OpenConnectionAsync();
            var planId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var targetVersionId = Guid.NewGuid();
            var validationRunId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            await ExecuteAsync(
                context,
                "ALTER TABLE [Space_PublishPlan] NOCHECK CONSTRAINT ALL; " +
                "ALTER TABLE [Space_PublishAttempt] NOCHECK CONSTRAINT ALL; " +
                "INSERT INTO [Space_PublishPlan] " +
                "([Id],[SiteId],[TargetVersionId],[ValidationRunId],[ContentHash]," +
                "[AdapterId],[CapabilityHash],[PlanHash],[ItemCount],[PlanJson]," +
                "[TenantId],[CreatedAtUtc],[IsDeleted]) VALUES " +
                $"('{planId:D}',NEWID(),'{targetVersionId:D}','{validationRunId:D}'," +
                "REPLICATE('a',64),N'cp6-wms-v1',REPLICATE('b',64)," +
                "REPLICATE('c',64),0,N'{}'," +
                $"'{tenantId:D}',SYSUTCDATETIME(),0); " +
                "INSERT INTO [Space_PublishAttempt] " +
                "([Id],[SiteId],[PublishPlanId],[TargetVersionId],[AdapterId]," +
                "[Status],[CurrentStep],[BusinessIdempotencyKey],[RequestHash]," +
                "[OwnsPublishSlot],[StartedAtUtc],[RequestedBy],[CorrelationId]," +
                "[TenantId],[CreatedAtUtc],[IsDeleted]) VALUES " +
                $"('{attemptId:D}',NEWID(),'{planId:D}','{targetVersionId:D}'," +
                "N'cp6-wms-v1',0,0,N'active-gate',REPLICATE('d',64),1," +
                $"SYSUTCDATETIME(),'{actorId:D}',NEWID(),'{tenantId:D}'," +
                "SYSUTCDATETIME(),0);");
            var script = await ReadScriptAsync();

            var failure = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteBatchesAsync(context, script));

            Assert.Equal(51020, failure.Number);
            output.WriteLine($"PublishRecoveryGuardNativeCode={failure.Number}.");
            Assert.Equal(
                0,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM [__EFMigrationsHistory_Space] " +
                    "WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery';"));
            Assert.Equal(
                0,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'JobId' " +
                    "AND [object_id] = OBJECT_ID(N'Space_PublishAttempt');"));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            if (selected is null)
                await context.Database.EnsureDeletedAsync();
            else
                await selected.RecordStateAsync(context, "publish-recovery-gate-final");
        }
    }

    [SpaceMigrationFact]
    public async Task Idempotent_recovery_script_runs_twice_from_E06_S03()
    {
        var selected = await SpaceMigrationTestDatabase.OpenIfSelectedAsync(output);
        var tenantId = Guid.NewGuid();
        var connectionString = selected?.ConnectionString ?? new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)!)
        {
            InitialCatalog = $"CP6SpaceE06S04_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = selected is null
            ? CreateContext(connectionString, tenantId)
            : selected.CreateContext(new TestExecutionContext(tenantId, Guid.NewGuid()), new SystemSpaceClock());
        try
        {
            const string historicalStart = "20260807135544_SpaceE06S03PublishOrchestration";
            if (selected is null)
                await context.Database.GetService<IMigrator>().MigrateAsync(historicalStart);
            else
                await selected.MigrateAsync(context, historicalStart);
            var script = await ReadScriptAsync();

            await context.Database.OpenConnectionAsync();
            await ExecuteBatchesAsync(context, script);
            await ExecuteBatchesAsync(context, script);
            output.WriteLine("PublishRecoveryScriptExecutions=2.");

            Assert.Equal(
                1,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM [__EFMigrationsHistory_Space] " +
                    "WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery';"));
            Assert.Equal(
                1,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM sys.tables " +
                    "WHERE [object_id] = OBJECT_ID(N'Space_PublishAuditEvent');"));
            Assert.Equal(
                6,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM sys.columns WHERE [name] IN " +
                    "(N'JobId', N'RequestJson', N'QueuedAtUtc', N'ManualRetryCount', " +
                    "N'LastRetriedAtUtc', N'LastRetriedBy') AND " +
                    "[object_id] = OBJECT_ID(N'Space_PublishAttempt');"));
            Assert.Equal(
                2,
                await ScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM sys.columns WHERE [name] IN " +
                    "(N'RequestJson', N'BatchAttemptNo') AND " +
                    "[object_id] = OBJECT_ID(N'Space_PublishBatch');"));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            if (selected is null)
                await context.Database.EnsureDeletedAsync();
            else
                await selected.RecordStateAsync(context, "publish-recovery-script-final");
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new SystemSpaceClock());

    private static async Task ExecuteBatchesAsync(
        SpaceContext context,
        string script)
    {
        foreach (var batch in Regex.Split(
                     script,
                     @"^\s*GO\s*$",
                     RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteAsync(
        SpaceContext context,
        string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private Task<string> ReadScriptAsync() => SpaceMigrationTestDatabase.ReadScriptAsync(
        "CP6.Space.Infrastructure/Migrations/Scripts/20260807144532_SpaceE06S04PublishRecovery.sql", output);

    private static async Task<int> ScalarAsync(
        SpaceContext context,
        string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;
}
