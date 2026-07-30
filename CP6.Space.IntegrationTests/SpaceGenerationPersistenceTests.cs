using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceGenerationPersistenceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 18, 30, 0, DateTimeKind.Utc);

    private static readonly string SourceHash = new('a', 64);
    private static readonly string IdempotencyHash = new('b', 64);
    private static readonly string BusinessHash = new('c', 64);
    private static readonly string JobHash = new('d', 64);
    private static readonly string RequestHash = new('e', 64);

    [Fact]
    public async Task Generation_records_are_hidden_from_other_tenants()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seed = CreateContext(
                         root,
                         database,
                         tenantA))
        {
            var graph = NewGraph(tenantA);
            seed.AddRange(graph.Entities);
            await seed.SaveChangesAsync();
        }

        await using var other = CreateContext(
            root,
            database,
            tenantB);
        Assert.Empty(await other.GenerationRuns.ToListAsync());
        Assert.Empty(await other.GenerationProposals.ToListAsync());
        Assert.Empty(await other.ProposalDecisions.ToListAsync());
        Assert.Empty(await other.AiUsageRecords.ToListAsync());
        Assert.Single(
            await other.GenerationRuns
                .IgnoreQueryFilters()
                .ToListAsync());
        Assert.Single(
            await other.GenerationProposals
                .IgnoreQueryFilters()
                .ToListAsync());
        Assert.Single(
            await other.ProposalDecisions
                .IgnoreQueryFilters()
                .ToListAsync());
        Assert.Single(
            await other.AiUsageRecords
                .IgnoreQueryFilters()
                .ToListAsync());
    }

    [Fact]
    public async Task Proposal_decisions_are_append_only()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using (var seed = CreateContext(
                         root,
                         database,
                         tenantId))
        {
            seed.AddRange(NewGraph(tenantId).Entities);
            await seed.SaveChangesAsync();
        }

        await using (var update = CreateContext(
                         root,
                         database,
                         tenantId))
        {
            var decision = await update.ProposalDecisions.SingleAsync();
            var comment = update.Entry(decision)
                .Property(nameof(SpaceProposalDecision.Comment));
            comment.CurrentValue = "tampered";
            comment.IsModified = true;

            await Assert.ThrowsAsync<SpaceProposalStateException>(
                () => update.SaveChangesAsync());
        }

        await using (var delete = CreateContext(
                         root,
                         database,
                         tenantId))
        {
            var decision = await delete.ProposalDecisions.SingleAsync();
            delete.Remove(decision);

            await Assert.ThrowsAsync<SpaceProposalStateException>(
                () => delete.SaveChangesAsync());
        }
    }

    [Fact]
    public void Ef_model_freezes_generation_tables_keys_and_guards()
    {
        var root = new InMemoryDatabaseRoot();
        using var context = CreateContext(
            root,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid());
        var designModel = context
            .GetService<IDesignTimeModel>()
            .Model;
        var entities = new[]
        {
            designModel.FindEntityType(typeof(SpaceGenerationRun))!,
            designModel.FindEntityType(typeof(SpaceGenerationProposal))!,
            designModel.FindEntityType(typeof(SpaceProposalDecision))!,
            designModel.FindEntityType(typeof(SpaceAiUsageRecord))!,
        };

        Assert.Equal(
            [
                "Space_GenerationRun",
                "Space_GenerationProposal",
                "Space_ProposalDecision",
                "Space_AiUsageRecord",
            ],
            entities.Select(entity => entity.GetTableName()));
        Assert.All(
            entities,
            entity =>
            {
                Assert.NotNull(entity.GetQueryFilter());
                Assert.True(
                    entity.FindProperty("RowVersion")!
                        .IsConcurrencyToken);
                Assert.All(
                    entity.GetForeignKeys(),
                    foreignKey => Assert.Contains(
                        foreignKey.Properties,
                        property => property.Name == "TenantId"));
            });

        var run = entities[0];
        var proposal = entities[1];
        var usage = entities[3];
        var currentRun = Assert.Single(
            run.GetIndexes(),
            index => index.GetDatabaseName() ==
                "UX_GenerationRun_Tenant_Business_Current");
        Assert.True(currentRun.IsUnique);
        Assert.Equal(
            "[IsCurrent] = 1 AND [IsDeleted] = 0",
            currentRun.GetFilter());
        Assert.Contains(
            proposal.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                        [
                            "TenantId",
                            "RunId",
                            "SourceKey",
                            "ProposalType",
                        ]));
        Assert.Contains(
            usage.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                        [
                            "TenantId",
                            "ProviderRequestIdHash",
                        ]));

        Assert.Contains(
            run.GetCheckConstraints(),
            constraint =>
                constraint.Name == "CK_Space_GenerationRun_Progress");
        Assert.Contains(
            proposal.GetCheckConstraints(),
            constraint =>
                constraint.Name ==
                    "CK_Space_GenerationProposal_Confidence");
        Assert.Equal(
            3,
            usage.GetCheckConstraints().Count());
    }

    [SqlServerFact]
    public async Task Migration_creates_tables_and_enforces_charge_and_run_deduplication()
    {
        var tenantId = Guid.NewGuid();

        await WithDatabaseAsync(
            tenantId,
            async context =>
            {
                var tables = await ReadTableNamesAsync(context);
                Assert.Contains("Space_GenerationRun", tables);
                Assert.Contains("Space_GenerationProposal", tables);
                Assert.Contains("Space_ProposalDecision", tables);
                Assert.Contains("Space_AiUsageRecord", tables);

                var graph = NewGraph(tenantId);
                context.AddRange(graph.Entities);
                await context.SaveChangesAsync();

                context.AiUsageRecords.Add(
                    NewUsage(tenantId, graph.Run.Id));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
                context.ChangeTracker.Clear();

                var secondJob = NewJob(
                    tenantId,
                    graph.Version.Id,
                    new string('f', 64));
                context.Jobs.Add(secondJob);
                await context.SaveChangesAsync();
                context.GenerationRuns.Add(
                    NewRun(
                        tenantId,
                        graph.Model.SiteId,
                        graph.Version.Id,
                        graph.Source.Id,
                        secondJob.Id,
                        new string('1', 64),
                        BusinessHash));

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
            });
    }

    private static GenerationGraph NewGraph(Guid tenantId)
    {
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "AI draft");
        var source = SpaceModelSource.CreateInlineSource(
            tenantId,
            version.Id,
            SpaceSourceType.Editor,
            "AI-safe normalized features",
            SourceHash);
        var job = NewJob(tenantId, version.Id, JobHash);
        var run = NewRun(
            tenantId,
            model.SiteId,
            version.Id,
            source.Id,
            job.Id,
            IdempotencyHash,
            BusinessHash);
        var proposal = NewProposal(
            tenantId,
            run.Id,
            version.Id);
        var decision = SpaceProposalDecision.Create(
            tenantId,
            run.Id,
            proposal.Id,
            SpaceProposalDecisionType.Accept,
            """{"type":"Rack"}""",
            """{"type":"Rack"}""",
            null,
            null,
            "accepted",
            Guid.NewGuid());
        var usage = NewUsage(tenantId, run.Id);

        return new GenerationGraph(
            model,
            version,
            source,
            job,
            run,
            proposal,
            decision,
            usage);
    }

    private static SpaceJob NewJob(
        Guid tenantId,
        Guid versionId,
        string businessKey) =>
        SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.BuildScene,
            SpaceJobSubjectType.ModelVersion,
            versionId,
            businessKey,
            SourceHash,
            50,
            3,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

    private static SpaceGenerationRun NewRun(
        Guid tenantId,
        Guid siteId,
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        string idempotencyHash,
        string businessHash) =>
        SpaceGenerationRun.Create(
            new SpaceGenerationRunDefinition(
                tenantId,
                siteId,
                versionId,
                sourceId,
                SourceHash,
                0,
                idempotencyHash,
                businessHash,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "rules-1",
                SpaceAiPolicySnapshot.StructuredFeatures,
                Guid.NewGuid(),
                "1.0",
                jobId));

    private static SpaceGenerationProposal NewProposal(
        Guid tenantId,
        Guid runId,
        Guid versionId) =>
        SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                tenantId,
                runId,
                versionId,
                0,
                SourceHash,
                "layer:racks/block:standard",
                "Rack",
                "{}",
                """{"type":"Rack"}""",
                "[]",
                "[]",
                "[]",
                "{}",
                0.95m,
                SpaceConfidenceBand.High,
                false));

    private static SpaceAiUsageRecord NewUsage(
        Guid tenantId,
        Guid runId) =>
        SpaceAiUsageRecord.Create(
            tenantId,
            runId,
            "local-v1",
            "warehouse-v1",
            RequestHash,
            10,
            5,
            10,
            9,
            "USD",
            120,
            SpaceAiUsageOutcome.Succeeded,
            Now);

    private static SpaceContext CreateContext(
        InMemoryDatabaseRoot root,
        string database,
        Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(database, root)
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private static async Task WithDatabaseAsync(
        Guid tenantId,
        Func<SpaceContext, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE13_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

        try
        {
            await context.Database.MigrateAsync();
            await action(context);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        SpaceContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command =
            context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT [name] FROM sys.tables ORDER BY [name]";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private sealed record GenerationGraph(
        SpaceModel Model,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        SpaceJob Job,
        SpaceGenerationRun Run,
        SpaceGenerationProposal Proposal,
        SpaceProposalDecision Decision,
        SpaceAiUsageRecord Usage)
    {
        public object[] Entities =>
        [
            Model,
            Version,
            Source,
            Job,
            Run,
            Proposal,
            Decision,
            Usage,
        ];
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
