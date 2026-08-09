using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Space.Infrastructure.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiRetentionTests
{
    private static readonly DateTime Now =
        new(2026, 8, 6, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string SourceHash = new('a', 64);

    [Fact]
    public async Task Cleanup_purges_payloads_but_preserves_audit_identity()
    {
        var tenantId = Guid.NewGuid();
        var root = new InMemoryDatabaseRoot();
        var clock = new MutableClock(Now.AddDays(-400));
        await using var context = CreateContext(root, tenantId, clock);
        var graph = NewGraph(tenantId, clock.UtcNow);
        context.AddRange(graph.Entities);
        await context.SaveChangesAsync();

        clock.UtcNow = Now;
        var store = new EfSpaceAiRetentionStore(context, clock);
        var first = await store.PurgeAsync(
            tenantId,
            SpaceAiRetentionJobPayload.Create(
                new SpaceAiRetentionOptions { BatchSize = 25 },
                Now));

        Assert.Equal(1, first.CandidateRuns);
        Assert.Equal(1, first.RunPayloadsPurged);
        Assert.Equal(1, first.ProposalPayloadsPurged);
        Assert.Equal(1, first.DiagnosticPayloadsPurged);
        Assert.Equal(1, first.StagingRowsDeleted);
        Assert.Equal(1, first.UsageRowsArchived);

        var run = await context.GenerationRuns.SingleAsync();
        var proposal = await context.GenerationProposals.SingleAsync();
        var issue = await context.Issues.SingleAsync();
        var decision = await context.ProposalDecisions.SingleAsync();
        var archivedUsage = await context.AiUsageRecords
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.TenantId == tenantId);

        Assert.Equal(graph.Run.Id, run.Id);
        Assert.Equal(SourceHash, run.SourceHash);
        Assert.Equal(SpaceGenerationRunStatus.Stale, run.Status);
        Assert.Equal(Now, run.PayloadPurgedAtUtc);
        Assert.Equal(graph.Proposal.Id, proposal.Id);
        Assert.Equal(SourceHash, proposal.SourceHash);
        Assert.Equal("{}", proposal.SuggestedAttributesJson);
        Assert.Equal(graph.Issue.Id, issue.Id);
        Assert.Equal("AI_LOW_CONFIDENCE", issue.Code);
        Assert.Equal("{}", issue.MessageArgsJson);
        Assert.Equal(graph.Decision.Id, decision.Id);
        Assert.Equal("audit retained", decision.Comment);
        Assert.Empty(context.GenerationStagingElements);
        Assert.Empty(context.AiUsageRecords);
        Assert.Equal(Now, archivedUsage.ArchivedAtUtc);

        var second = await store.PurgeAsync(
            tenantId,
            SpaceAiRetentionJobPayload.Create(
                new SpaceAiRetentionOptions { BatchSize = 25 },
                Now));
        Assert.Equal(
            new SpaceAiRetentionCleanupResult(0, 0, 0, 0, 0, 0),
            second);
        Assert.Single(context.ProposalDecisions);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Cleanup_skips_retention_holds_and_published_history(
        bool publishVersion,
        bool applyHold)
    {
        var tenantId = Guid.NewGuid();
        var root = new InMemoryDatabaseRoot();
        var clock = new MutableClock(Now.AddDays(-400));
        await using var context = CreateContext(root, tenantId, clock);
        var graph = NewGraph(
            tenantId,
            clock.UtcNow,
            applyHold ? Now.AddDays(30) : null);
        context.AddRange(graph.Entities);
        await context.SaveChangesAsync();
        if (publishVersion)
        {
            graph.Version.BeginValidation();
            graph.Version.MarkReady(
                new string('b', 64),
                "rules-1",
                new string('c', 64));
            graph.Version.BeginPublishing();
            graph.Version.MarkPublished(Guid.NewGuid(), clock.UtcNow);
            await context.SaveChangesAsync();
        }

        clock.UtcNow = Now;
        var result = await new EfSpaceAiRetentionStore(context, clock)
            .PurgeAsync(
                tenantId,
                SpaceAiRetentionJobPayload.Create(
                    new SpaceAiRetentionOptions(),
                    Now));

        Assert.Equal(0, result.CandidateRuns);
        Assert.Null((await context.GenerationRuns.SingleAsync()).PayloadPurgedAtUtc);
        Assert.Null((await context.GenerationProposals.SingleAsync()).PayloadPurgedAtUtc);
        Assert.Single(context.GenerationStagingElements);
        if (applyHold)
        {
            Assert.Single(context.AiUsageRecords);
        }
        else
        {
            Assert.Empty(context.AiUsageRecords);
            Assert.NotNull((await context.AiUsageRecords
                .IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.TenantId == tenantId))
                .ArchivedAtUtc);
        }
    }

    [Fact]
    public void Migration_down_is_fail_closed_and_never_drops_audit_schema()
    {
        var migration = new SpaceE13S17AiRetention();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(SpaceE13S17AiRetention)
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var operation = Assert.Single(builder.Operations);
        var sql = Assert.IsType<SqlOperation>(operation).Sql;
        Assert.Contains("THROW 51017", sql, StringComparison.Ordinal);
        Assert.Contains("forward-fix", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            builder.Operations,
            candidate => candidate is DropColumnOperation or DropTableOperation);
    }

    [SqlServerFact]
    public async Task SqlServer_migration_and_cleanup_are_retry_safe()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = NewSqlServerDatabase();
        var clock = new MutableClock(Now.AddDays(-400));
        await using var context = CreateSqlContext(connectionString, tenantId, clock);
        try
        {
            await context.Database.MigrateAsync();
            var graph = NewGraph(tenantId, clock.UtcNow);
            context.AddRange(graph.Entities);
            await context.SaveChangesAsync();

            clock.UtcNow = Now;
            var store = new EfSpaceAiRetentionStore(context, clock);
            var payload = SpaceAiRetentionJobPayload.Create(
                new SpaceAiRetentionOptions { BatchSize = 25 },
                Now);
            var first = await store.PurgeAsync(tenantId, payload);
            var second = await store.PurgeAsync(tenantId, payload);

            Assert.Equal(1, first.RunPayloadsPurged);
            Assert.Equal(1, first.ProposalPayloadsPurged);
            Assert.Equal(1, first.DiagnosticPayloadsPurged);
            Assert.Equal(1, first.StagingRowsDeleted);
            Assert.Equal(1, first.UsageRowsArchived);
            Assert.Equal(
                new SpaceAiRetentionCleanupResult(0, 0, 0, 0, 0, 0),
                second);
            Assert.Single(await context.ProposalDecisions.AsNoTracking().ToListAsync());
            var retiredStaging = Assert.Single(await context.GenerationStagingElements
                .IgnoreQueryFilters()
                .Where(candidate => candidate.TenantId == tenantId && candidate.IsDeleted)
                .ToListAsync());
            Assert.Equal("{}", retiredStaging.NormalizedPayloadJson);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task SqlServer_tenant_lock_rejects_concurrent_cleanup()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = NewSqlServerDatabase();
        var clock = new MutableClock(Now);
        await using var owner = CreateSqlContext(connectionString, tenantId, clock);
        try
        {
            await owner.Database.MigrateAsync();
            await using var transaction = await owner.Database.BeginTransactionAsync();
            var resource = $"cp6:space:ai-retention:{tenantId:N}";
            await owner.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;");

            await using var contender = CreateSqlContext(
                connectionString,
                tenantId,
                clock);
            var store = new EfSpaceAiRetentionStore(contender, clock);
            await Assert.ThrowsAsync<SpaceAiRetentionBusyException>(() =>
                store.PurgeAsync(
                    tenantId,
                    SpaceAiRetentionJobPayload.Create(
                        new SpaceAiRetentionOptions(),
                        Now)));
        }
        finally
        {
            await owner.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task SqlServer_idempotent_deployment_script_runs_twice()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = NewSqlServerDatabase();
        var clock = new MutableClock(Now);
        await using var context = CreateSqlContext(connectionString, tenantId, clock);
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(
                "20260806110504_SpaceE13S10AtomicApply");
            var repositoryRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var script = await File.ReadAllTextAsync(
                Path.Combine(
                    repositoryRoot,
                    "CP6.Space.Infrastructure",
                    "Migrations",
                    "Scripts",
                    "20260806160931_SpaceE13S17AiRetention.sql"));

            await context.Database.OpenConnectionAsync();
            await ExecuteSqlBatchesAsync(context, script);
            await ExecuteSqlBatchesAsync(context, script);

            var historyCount = await ExecuteScalarAsync(
                context,
                "SELECT COUNT(*) FROM [__EFMigrationsHistory_Space] " +
                "WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention';");
            var retentionColumnCount = await ExecuteScalarAsync(
                context,
                "SELECT COUNT(*) FROM sys.columns WHERE [name] IN " +
                "(N'PayloadPurgedAtUtc', N'RetentionHoldUntilUtc', N'ArchivedAtUtc') " +
                "AND [object_id] IN " +
                "(OBJECT_ID(N'Space_ModelIssue'), OBJECT_ID(N'Space_GenerationRun'), " +
                "OBJECT_ID(N'Space_GenerationProposal'), OBJECT_ID(N'Space_AiUsageRecord')); ");
            Assert.Equal(1, historyCount);
            Assert.Equal(5, retentionColumnCount);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static RetentionGraph NewGraph(
        Guid tenantId,
        DateTime recordedAtUtc,
        DateTime? holdUntilUtc = null)
    {
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "AI retention draft");
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            height: 6_000);
        var source = SpaceModelSource.CreateInlineSource(
            tenantId,
            version.Id,
            SpaceSourceType.Editor,
            "AI-safe normalized features",
            SourceHash);
        var job = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.BuildScene,
            SpaceJobSubjectType.ModelVersion,
            version.Id,
            new string('d', 64),
            SourceHash,
            50,
            3,
            Guid.NewGuid(),
            recordedAtUtc,
            Guid.NewGuid());
        var run = SpaceGenerationRun.Create(
            new SpaceGenerationRunDefinition(
                tenantId,
                model.SiteId,
                version.Id,
                source.Id,
                SourceHash,
                0,
                new string('e', 64),
                new string('f', 64),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "rules-1",
                SpaceAiPolicySnapshot.StructuredFeatures,
                Guid.NewGuid(),
                "1.0",
                job.Id,
                floor.LogicalId));
        run.BeginPreparing();
        run.BeginInferring();
        run.RecordDegradedReason("provider-timeout");
        run.BeginValidating();
        run.MarkAwaitingReview();
        run.MarkStale();
        if (holdUntilUtc.HasValue)
            run.ExtendRetentionHold(holdUntilUtc.Value);
        var proposal = SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                tenantId,
                run.Id,
                version.Id,
                0,
                SourceHash,
                "layer:racks/block:42",
                "Rack",
                "{\"type\":\"Polygon\"}",
                "{\"name\":\"Rack 42\"}",
                "[]",
                "[\"entity:42\"]",
                "[{\"confidence\":0.4}]",
                "{\"name\":\"ai\"}",
                0.4m,
                SpaceConfidenceBand.Low,
                true));
        var decision = SpaceProposalDecision.Create(
            tenantId,
            run.Id,
            proposal.Id,
            SpaceProposalDecisionType.Reject,
            "{\"name\":\"Rack 42\"}",
            null,
            null,
            "LOW_CONFIDENCE",
            "audit retained",
            Guid.NewGuid());
        var issue = SpaceModelIssue.Create(
            tenantId,
            version.Id,
            source.Id,
            job.Id,
            SpaceIssueSeverity.Warning,
            "AI_LOW_CONFIDENCE",
            "layer:racks/block:42",
            messageArgsJson: "{\"confidence\":0.4}",
            generationRunId: run.Id,
            generationProposalId: proposal.Id);
        var staging = SpaceGenerationStagingElement.Create(
            tenantId,
            run.Id,
            proposal.Id,
            version.Id,
            0,
            Guid.NewGuid(),
            floor.LogicalId,
            "Rack",
            "{\"name\":\"Rack 42\"}");
        var usage = SpaceAiUsageRecord.Create(
            tenantId,
            run.Id,
            "local-v1",
            "warehouse-v1",
            new string('1', 64),
            10,
            5,
            7,
            7,
            "USD",
            120,
            SpaceAiUsageOutcome.Succeeded,
            recordedAtUtc);
        return new RetentionGraph(
            model,
            version,
            floor,
            source,
            job,
            run,
            proposal,
            decision,
            issue,
            staging,
            usage);
    }

    private static SpaceContext CreateContext(
        InMemoryDatabaseRoot root,
        Guid tenantId,
        MutableClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase($"retention-{tenantId:N}", root)
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);

    private static SpaceContext CreateSqlContext(
        string connectionString,
        Guid tenantId,
        MutableClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);

    private static string NewSqlServerDatabase()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        return new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE13S17_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    private static async Task ExecuteSqlBatchesAsync(
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

    private static async Task<int> ExecuteScalarAsync(
        SpaceContext context,
        string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record RetentionGraph(
        SpaceModel Model,
        SpaceModelVersion Version,
        SpaceFloorRevision Floor,
        SpaceModelSource Source,
        SpaceJob Job,
        SpaceGenerationRun Run,
        SpaceGenerationProposal Proposal,
        SpaceProposalDecision Decision,
        SpaceModelIssue Issue,
        SpaceGenerationStagingElement Staging,
        SpaceAiUsageRecord Usage)
    {
        public object[] Entities =>
        [
            Model,
            Version,
            Floor,
            Source,
            Job,
            Run,
            Proposal,
            Decision,
            Issue,
            Staging,
            Usage,
        ];
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class MutableClock(DateTime utcNow) : ISpaceClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
