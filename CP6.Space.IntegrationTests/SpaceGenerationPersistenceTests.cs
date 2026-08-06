using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
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
        var root = SpaceTestDatabaseRoots.InMemory;
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
        var root = SpaceTestDatabaseRoots.InMemory;
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
        var root = SpaceTestDatabaseRoots.InMemory;
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

        var issue = designModel.FindEntityType(typeof(SpaceModelIssue))!;
        Assert.Contains(
            issue.GetCheckConstraints(),
            constraint => constraint.Name ==
                "CK_Space_ModelIssue_GenerationScope");
        Assert.Contains(
            issue.GetCheckConstraints(),
            constraint => constraint.Name ==
                "CK_Space_ModelIssue_Resolution");
        Assert.Contains(
            issue.GetIndexes(),
            index => index.GetDatabaseName() ==
                "IX_Space_ModelIssue_Tenant_Run_Proposal_Status");

        var lockedFact = designModel.FindEntityType(
            typeof(SpaceGenerationLockedFact))!;
        Assert.Equal("Space_GenerationLockedFact", lockedFact.GetTableName());
        Assert.NotNull(lockedFact.GetQueryFilter());
        Assert.True(lockedFact.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Contains(
            lockedFact.GetIndexes(),
            index => index.IsUnique && index.GetDatabaseName() ==
                "UX_GenerationLockedFact_Tenant_Run_Source_Type_Field");
    }

    [SqlServerFact]
    public async Task Decision_service_accepts_once_completes_review_and_replays()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            tenantId,
            async context =>
            {
                var graph = NewGraph(tenantId);
                graph.Run.BeginPreparing();
                graph.Run.BeginInferring();
                graph.Run.BeginValidating();
                graph.Run.MarkAwaitingReview();
                context.AddRange(
                    graph.Model,
                    graph.Version,
                    graph.Source,
                    graph.Job,
                    graph.Run,
                    graph.Proposal);
                await context.SaveChangesAsync();

                var execution = new TestExecutionContext(tenantId, actorId);
                var service = new SpaceAiProposalDecisionService(
                    context,
                    execution,
                    new AllowAccess(),
                    new TestCursorCodec(),
                    new FixedClock(),
                    new SpaceAiProposalReviewOptions());
                var request = new CreateSpaceAiProposalDecisionRequest(
                    graph.Proposal.Id,
                    "Accept",
                    Convert.ToBase64String(graph.Proposal.RowVersion),
                    null,
                    null,
                    "REVIEWED",
                    "Checked against the normalized source.");

                var first = await service.CreateDecisionAsync(
                    graph.Run.Id,
                    request,
                    "accept-rack-1");
                var replay = await service.CreateDecisionAsync(
                    graph.Run.Id,
                    request,
                    "accept-rack-1");

                Assert.False(first.IdempotentReplay);
                Assert.True(first.Review.ReviewCompleted);
                Assert.Equal("review-completed", first.Outcome);
                Assert.True(replay.IdempotentReplay);
                Assert.Equal(first.DecisionBatchId, replay.DecisionBatchId);
                Assert.Single(await context.ProposalDecisions.ToListAsync());
                Assert.Equal(
                    SpaceGenerationProposalStatus.Accepted,
                    (await context.GenerationProposals.SingleAsync()).Status);
                Assert.NotNull(
                    (await context.GenerationRuns.SingleAsync())
                    .ReviewCompletedAtUtc);
            });
    }

    [SqlServerFact]
    public async Task Modified_values_materialize_once_for_a_same_source_rerun()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            tenantId,
            async context =>
            {
                var graph = NewGraph(tenantId, proposalBlocking: true);
                graph.Run.BeginPreparing();
                graph.Run.BeginInferring();
                graph.Run.BeginValidating();
                graph.Run.MarkAwaitingReview();
                var blockingIssue = SpaceModelIssue.Create(
                    tenantId,
                    graph.Version.Id,
                    graph.Source.Id,
                    graph.Job.Id,
                    SpaceIssueSeverity.Blocking,
                    "AI_BUSINESS_ENUM_INVALID",
                    sourceRef: "rack-1",
                    generationRunId: graph.Run.Id,
                    generationProposalId: graph.Proposal.Id);
                context.AddRange(
                    graph.Model,
                    graph.Version,
                    graph.Source,
                    graph.Job,
                    graph.Run,
                    graph.Proposal,
                    blockingIssue);
                await context.SaveChangesAsync();

                using var value = System.Text.Json.JsonDocument.Parse(
                    "\"DriveIn\"");
                var execution = new TestExecutionContext(tenantId, actorId);
                var decisions = new SpaceAiProposalDecisionService(
                    context,
                    execution,
                    new AllowAccess(),
                    new TestCursorCodec(),
                    new FixedClock(),
                    new SpaceAiProposalReviewOptions());
                var response = await decisions.CreateDecisionAsync(
                    graph.Run.Id,
                    new CreateSpaceAiProposalDecisionRequest(
                        graph.Proposal.Id,
                        "Modify",
                        Convert.ToBase64String(graph.Proposal.RowVersion),
                        [new SpaceAiProposalPatchOperationDto(
                            "replace",
                            "/attributes/rackType",
                            value.RootElement.Clone())],
                        ["/attributes/rackType"],
                        "HUMAN_CORRECTION",
                        "Verified rack family."),
                    "modify-rack-1");

                var nextJob = NewJob(
                    tenantId,
                    graph.Version.Id,
                    new string('9', 64));
                var nextRun = NewRun(
                    tenantId,
                    graph.Model.SiteId,
                    graph.Version.Id,
                    graph.Source.Id,
                    nextJob.Id,
                    new string('8', 64),
                    new string('7', 64),
                    basedOnRunId: graph.Run.Id);
                context.AddRange(nextJob, nextRun);
                await context.SaveChangesAsync();

                var lockedFacts = new SpaceAiLockedFactService(
                    context,
                    execution,
                    new AllowAccess());
                var first = await lockedFacts.MaterializeAsync(nextRun.Id);
                var replay = await lockedFacts.MaterializeAsync(nextRun.Id);

                var fact = Assert.Single(first);
                Assert.Single(replay);
                Assert.Equal(response.Decisions.Single().DecisionId,
                    fact.SourceDecisionId);
                Assert.Equal("/attributes/rackType", fact.FieldPath);
                Assert.Equal("DriveIn", fact.Value.GetString());
                Assert.Equal("SameSourceIdentity", fact.MatchMethod);
                Assert.True(fact.IsConfirmed);
                Assert.Single(await context.GenerationLockedFacts.ToListAsync());
                var resolvedIssue = await context.Issues.SingleAsync();
                Assert.Equal(SpaceIssueStatus.Resolved, resolvedIssue.Status);
                Assert.Equal(
                    SpaceIssueResolutionKind.ProposalDecision,
                    resolvedIssue.ResolutionKind);
                Assert.Equal(
                    response.Decisions.Single().DecisionId,
                    resolvedIssue.ResolutionDecisionId);
            });
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

    private static GenerationGraph NewGraph(
        Guid tenantId,
        bool proposalBlocking = false)
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
            version.Id,
            proposalBlocking);
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
        string businessHash,
        Guid? basedOnRunId = null) =>
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
                basedOnRunId,
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
        Guid versionId,
        bool hasBlockingIssue = false) =>
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
                """{"name":"AI Rack","rackType":"Selective"}""",
                """{"zoneSourceKey":"zone-1","aisleSourceKey":"aisle-1"}""",
                "[]",
                "[]",
                "{}",
                0.95m,
                SpaceConfidenceBand.High,
                hasBlockingIssue));

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

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $"{state.Resource}|{state.FilterHash}|{state.Offset}"));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }
}
