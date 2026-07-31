using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceSqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Migration_creates_only_design_tables_and_separate_history()
    {
        await WithDatabaseAsync(async context =>
        {
            var tables = await ReadTableNamesAsync(context);

            Assert.Contains("Space_Model", tables);
            Assert.Contains("Space_ModelVersion", tables);
            Assert.Contains("Space_File", tables);
            Assert.Contains("Space_ModelSource", tables);
            Assert.Contains("Space_Artifact", tables);
            Assert.Contains("Space_Job", tables);
            Assert.Contains("Space_JobAttempt", tables);
            Assert.Contains("Space_JobStep", tables);
            Assert.Contains("Space_ModelIssue", tables);
            Assert.Contains(SpaceContext.MigrationsHistoryTable, tables);
            Assert.DoesNotContain("__EFMigrationsHistory", tables);
            Assert.DoesNotContain("Space_Site", tables);
        });
    }

    [SqlServerFact]
    public async Task File_hash_deduplication_is_enforced_per_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var hash = new string('c', 64);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await using (var first = CreateContext(
                                 connectionString,
                                 tenantA,
                                 Guid.NewGuid()))
                {
                    first.Files.Add(NewCleanFile(
                        tenantA,
                        ".pdf",
                        SpaceFileRetentionClass.Source,
                        hash));
                    await first.SaveChangesAsync();

                    first.Files.Add(NewCleanFile(
                        tenantA,
                        ".pdf",
                        SpaceFileRetentionClass.Source,
                        hash));
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => first.SaveChangesAsync());
                }

                await using var second = CreateContext(
                    connectionString,
                    tenantB,
                    Guid.NewGuid());
                second.Files.Add(NewCleanFile(
                    tenantB,
                    ".pdf",
                    SpaceFileRetentionClass.Source,
                    hash));
                await second.SaveChangesAsync();
            });
    }

    [SqlServerFact]
    public async Task Same_file_can_feed_multiple_versions_and_source_hash_lookup()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var hash = new string('d', 64);

        await WithDatabaseAsync(
            async context =>
            {
                var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                var first = SpaceModelVersion.CreateDraft(
                    tenantId,
                    model.Id,
                    1,
                    "First");
                var second = SpaceModelVersion.CreateDraft(
                    tenantId,
                    model.Id,
                    2,
                    "Second");
                var file = NewCleanFile(
                    tenantId,
                    ".pdf",
                    SpaceFileRetentionClass.Source,
                    hash);
                var firstSource = SpaceModelSource.CreateFileSource(
                    tenantId,
                    first.Id,
                    SpaceSourceType.Pdf,
                    file,
                    "First import");
                var secondSource = SpaceModelSource.CreateFileSource(
                    tenantId,
                    second.Id,
                    SpaceSourceType.Pdf,
                    file,
                    "Second import");
                context.AddRange(
                    model,
                    first,
                    second,
                    file,
                    firstSource,
                    secondSource);
                await context.SaveChangesAsync();

                var matches = await new EfSpaceSourceCatalog(context)
                    .FindByHashAsync(tenantId, hash);

                Assert.Equal(2, matches.Count);
                Assert.All(matches, source => Assert.Equal(file.Id, source.FileId));
                Assert.Equal(
                    2,
                    await context.Sources
                        .Select(source => source.ModelVersionId)
                        .Distinct()
                        .CountAsync());
            },
            tenantId,
            actorId);
    }

    [SqlServerFact]
    public async Task Composite_foreign_keys_reject_cross_tenant_source_lineage()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid versionB;
                Guid fileA;
                await using (var contextA = CreateContext(
                                 connectionString,
                                 tenantA,
                                 Guid.NewGuid()))
                {
                    var file = NewCleanFile(
                        tenantA,
                        ".pdf",
                        SpaceFileRetentionClass.Source);
                    contextA.Files.Add(file);
                    await contextA.SaveChangesAsync();
                    fileA = file.Id;
                }

                await using (var contextB = CreateContext(
                                 connectionString,
                                 tenantB,
                                 Guid.NewGuid()))
                {
                    var model = SpaceModel.Create(tenantB, Guid.NewGuid());
                    var version = SpaceModelVersion.CreateDraft(
                        tenantB,
                        model.Id,
                        1,
                        "Tenant B");
                    contextB.AddRange(model, version);
                    await contextB.SaveChangesAsync();
                    versionB = version.Id;
                }

                await using var context = CreateContext(
                    connectionString,
                    tenantA,
                    Guid.NewGuid());
                var sourceId = Guid.NewGuid();
                var sql = $"""
                    INSERT INTO [Space_ModelSource]
                        ([Id], [ModelVersionId], [SourceType], [FileId],
                         [DisplayName], [Sha256], [State], [TenantId],
                         [CreatedAtUtc], [IsDeleted])
                    VALUES
                        ('{sourceId}', '{versionB}', 2, '{fileA}',
                         'Cross tenant', '{new string('e', 64)}', 2, '{tenantA}',
                         SYSUTCDATETIME(), 0)
                    """;

                await Assert.ThrowsAsync<SqlException>(
                    () => context.Database.ExecuteSqlRawAsync(sql));
            });
    }

    [SqlServerFact]
    public async Task Database_restricts_physical_delete_of_a_referenced_file()
    {
        var tenantId = Guid.NewGuid();

        await WithDatabaseAsync(
            async context =>
            {
                var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                var version = SpaceModelVersion.CreateDraft(
                    tenantId,
                    model.Id,
                    1,
                    "Draft");
                var file = NewCleanFile(
                    tenantId,
                    ".pdf",
                    SpaceFileRetentionClass.Source);
                var source = SpaceModelSource.CreateFileSource(
                    tenantId,
                    version.Id,
                    SpaceSourceType.Pdf,
                    file,
                    "Floor");
                context.AddRange(model, version, file, source);
                await context.SaveChangesAsync();

                await Assert.ThrowsAsync<SqlException>(
                    () => context.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM [Space_File] WHERE [Id] = {file.Id}"));
            },
            tenantId);
    }

    [SqlServerFact]
    public async Task Tenant_site_and_version_number_constraints_are_enforced()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        await WithDatabaseAsync(
            async context =>
            {
                var model = SpaceModel.Create(tenantId, siteId);
                var coordinator = new SpaceModelVersionCoordinator(
                    new TestExecutionContext(tenantId, actorId));
                context.Models.Add(model);
                await context.SaveChangesAsync();

                var version = coordinator.CreateDraft(model, 1, "Draft");
                context.Versions.Add(version);
                await context.SaveChangesAsync();

                context.Models.Add(SpaceModel.Create(tenantId, siteId));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
                context.ChangeTracker.Clear();

                var duplicateVersion =
                    SpaceModelVersion.CreateDraft(tenantId, model.Id, 1, "Duplicate");
                context.Versions.Add(duplicateVersion);
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());
            },
            tenantId,
            actorId);
    }

    [SqlServerFact]
    public async Task Same_site_id_is_isolated_between_tenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await using (var first = CreateContext(
                                 connectionString,
                                 tenantA,
                                 Guid.NewGuid()))
                {
                    first.Models.Add(SpaceModel.Create(tenantA, siteId));
                    await first.SaveChangesAsync();
                }

                await using (var second = CreateContext(
                                 connectionString,
                                 tenantB,
                                 Guid.NewGuid()))
                {
                    second.Models.Add(SpaceModel.Create(tenantB, siteId));
                    await second.SaveChangesAsync();
                    Assert.Single(await second.Models.ToListAsync());
                }
            });
    }

    [SqlServerFact]
    public async Task RowVersion_rejects_the_second_concurrent_model_update()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid modelId;
                await using (var seed = CreateContext(connectionString, tenantId, actorId))
                {
                    var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                    seed.Models.Add(model);
                    await seed.SaveChangesAsync();
                    modelId = model.Id;
                }

                await using var first = CreateContext(connectionString, tenantId, actorId);
                await using var second = CreateContext(connectionString, tenantId, actorId);
                var a = await first.Models.SingleAsync(x => x.Id == modelId);
                var b = await second.Models.SingleAsync(x => x.Id == modelId);

                a.BeginCutover(Guid.NewGuid());
                await first.SaveChangesAsync();

                b.BeginCutover(Guid.NewGuid());
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => second.SaveChangesAsync());
            });
    }

    [SqlServerFact]
    public async Task Published_version_cannot_be_mutated_through_the_context()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid versionId;
                await using (var seed = CreateContext(connectionString, tenantId, actorId))
                {
                    var model = SpaceModel.Create(tenantId, Guid.NewGuid());
                    seed.Models.Add(model);
                    await seed.SaveChangesAsync();

                    var version = SpaceModelVersion.CreateDraft(
                        tenantId, model.Id, 1, "Published");
                    version.BeginValidation();
                    version.MarkReady(ContentHash, "space-v1", WmsHash);
                    version.BeginPublishing();
                    version.MarkPublished(actorId, DateTime.UtcNow);
                    seed.Versions.Add(version);
                    await seed.SaveChangesAsync();

                    model.SetPublishedVersion(version, ContentHash);
                    await seed.SaveChangesAsync();
                    versionId = version.Id;
                }

                await using var context = CreateContext(connectionString, tenantId, actorId);
                var published = await context.Versions.SingleAsync(x => x.Id == versionId);
                context.Remove(published);

                await Assert.ThrowsAsync<SpaceVersionStateException>(
                    () => context.SaveChangesAsync());
            });
    }

    [SqlServerFact]
    public async Task Common_elements_and_attributes_persist_with_tenant_isolation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                Guid versionId;
                Guid floorLogicalId;
                await using (var writer = CreateContext(
                                 connectionString,
                                 tenantA,
                                 actorId))
                {
                    var model = SpaceModel.Create(tenantA, Guid.NewGuid());
                    var version = SpaceModelVersion.CreateDraft(
                        tenantA,
                        model.Id,
                        1,
                        "Element draft");
                    var floor = SpaceFloorRevision.Create(
                        tenantA,
                        version.Id,
                        Guid.NewGuid(),
                        model.SiteId,
                        1,
                        "F1",
                        "Floor 1");
                    versionId = version.Id;
                    floorLogicalId = floor.LogicalId;
                    var types = new[]
                    {
                        SpaceElementTypes.Wall,
                        SpaceElementTypes.Column,
                        SpaceElementTypes.Door,
                        SpaceElementTypes.Dock,
                        SpaceElementTypes.Pallet,
                        SpaceElementTypes.Device,
                    };
                    var elements = types
                        .Select((type, index) =>
                        {
                            var element = SpaceElementRevision.Create(
                                tenantA,
                                version.Id,
                                Guid.NewGuid(),
                                floor.LogicalId,
                                type,
                                """
                                {"schemaVersion":1,"kind":"box","width":800,"height":2200,"depth":400}
                                """);
                            element.ConfigurePlacement(
                                index * 1000,
                                0,
                                0,
                                0,
                                800,
                                2200,
                                400);
                            return element;
                        })
                        .ToArray();
                    var attribute = SpaceElementAttribute.Create(
                        tenantA,
                        elements[5],
                        SpaceElementAttributeNamespaces.Manufacturer,
                        "ratedPower",
                        SpaceElementAttributeValueTypes.Decimal,
                        "12.500",
                        "kW");

                    writer.AddRange(model, version, floor);
                    writer.ElementRevisions.AddRange(elements);
                    writer.ElementAttributes.Add(attribute);
                    await writer.SaveChangesAsync();

                    Assert.Equal(
                        types,
                        await writer.ElementRevisions
                            .OrderBy(element => element.X)
                            .Select(element => element.ElementType)
                            .ToArrayAsync());
                    Assert.Equal(
                        "12.5",
                        await writer.ElementAttributes
                            .Select(item => item.Value)
                            .SingleAsync());

                    writer.ElementAttributes.Add(
                        SpaceElementAttribute.Create(
                            tenantA,
                            elements[5],
                            SpaceElementAttributeNamespaces.Manufacturer,
                            "ratedPower",
                            SpaceElementAttributeValueTypes.Decimal,
                            "13",
                            "kW"));
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => writer.SaveChangesAsync());
                }

                await using (var otherTenant = CreateContext(
                                 connectionString,
                                 tenantB,
                                 Guid.NewGuid()))
                {
                    Assert.Empty(await otherTenant.ElementRevisions.ToListAsync());
                    Assert.Empty(await otherTenant.ElementAttributes.ToListAsync());
                    Assert.Equal(
                        6,
                        await otherTenant.ElementRevisions
                            .IgnoreQueryFilters()
                            .CountAsync());
                    Assert.Single(
                        await otherTenant.ElementAttributes
                            .IgnoreQueryFilters()
                            .ToListAsync());
                }

                await using var crossTenant = CreateContext(
                    connectionString,
                    tenantB,
                    Guid.NewGuid());
                crossTenant.ElementRevisions.Add(
                    SpaceElementRevision.Create(
                        tenantB,
                        versionId,
                        Guid.NewGuid(),
                        floorLogicalId,
                        SpaceElementTypes.Column,
                        """
                        {"schemaVersion":1,"kind":"box","width":200,"height":3000,"depth":200}
                        """));
                await Assert.ThrowsAsync<SpaceVersionStateException>(
                    () => crossTenant.SaveChangesAsync());
            },
            tenantA,
            actorId);
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, Task> action,
        Guid? tenantId = null,
        Guid? actorId = null)
    {
        await WithDatabaseAsync(
            async (context, _) => await action(context),
            tenantId,
            actorId);
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, string, Task> action,
        Guid? tenantId = null,
        Guid? actorId = null)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE01_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using var context = CreateContext(
            connectionString,
            tenantId ?? Guid.NewGuid(),
            actorId ?? Guid.NewGuid());

        try
        {
            await context.Database.MigrateAsync();
            await action(context, connectionString);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        Guid tenantId,
        Guid actorId)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(
            options,
            new TestExecutionContext(tenantId, actorId),
            new TestClock());
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        SpaceContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT [name] FROM sys.tables ORDER BY [name]";
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private static SpaceFile NewCleanFile(
        Guid tenantId,
        string extension,
        SpaceFileRetentionClass retentionClass,
        string? hash = null)
    {
        var contentType = extension == ".pdf" ? "application/pdf" : "image/png";
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenantId,
            $"quarantine/{Guid.NewGuid():N}",
            $"input{extension}",
            contentType,
            retentionClass);
        file.CompleteQuarantine(
            contentType,
            extension,
            12,
            hash ?? Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()
                .PadRight(64, '0'));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        return file;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
