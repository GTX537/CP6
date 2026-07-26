using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceContextTenantTests
{
    private static readonly DateTime Now =
        new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Query_filter_prevents_cross_tenant_reads()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var first = CreateContext(root, database, tenantA))
        {
            first.Models.Add(SpaceModel.Create(tenantA, Guid.NewGuid()));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext(root, database, tenantB);
        Assert.Empty(await second.Models.ToListAsync());
        Assert.Single(await second.Models.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task File_source_and_artifact_filters_prevent_cross_tenant_reads()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var first = CreateContext(root, database, tenantA))
        {
            var model = SpaceModel.Create(tenantA, Guid.NewGuid());
            var version = SpaceModelVersion.CreateDraft(
                tenantA,
                model.Id,
                1,
                "Draft");
            var sourceFile = NewCleanFile(
                tenantA,
                ".pdf",
                SpaceFileRetentionClass.Source);
            var artifactFile = NewCleanFile(
                tenantA,
                ".png",
                SpaceFileRetentionClass.Artifact);
            var source = SpaceModelSource.CreateFileSource(
                tenantA,
                version.Id,
                SpaceSourceType.Pdf,
                sourceFile,
                "Floor");
            var artifact = SpaceArtifact.Create(
                tenantA,
                version.Id,
                source,
                artifactFile,
                SpaceArtifactType.Thumbnail,
                "v1");

            first.AddRange(model, version, sourceFile, artifactFile, source, artifact);
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext(root, database, tenantB);
        Assert.Empty(await second.Files.ToListAsync());
        Assert.Empty(await second.Sources.ToListAsync());
        Assert.Empty(await second.Artifacts.ToListAsync());
        Assert.Equal(2, await second.Files.IgnoreQueryFilters().CountAsync());
        Assert.Single(await second.Sources.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await second.Artifacts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Source_catalog_queries_by_tenant_and_sha256()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var hash = new string('a', 64);
        await using var context = CreateContext(root, database, tenantId);
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Draft");
        var file = NewCleanFile(
            tenantId,
            ".pdf",
            SpaceFileRetentionClass.Source,
            hash);
        var source = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Pdf,
            file,
            "Floor");
        context.AddRange(model, version, file, source);
        await context.SaveChangesAsync();

        var catalog = new EfSpaceSourceCatalog(context);
        var matches = await catalog.FindByHashAsync(tenantId, hash);

        Assert.Single(matches);
        Assert.Equal(source.Id, matches[0].Id);
        Assert.Empty(await catalog.FindByHashAsync(tenantId, new string('b', 64)));
    }

    [Fact]
    public async Task Save_rejects_cross_tenant_entities()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();

        await using var context = CreateContext(root, database, tenantA);
        context.Models.Add(SpaceModel.Create(Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<SpaceTenantScopeException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_fails_closed_without_verified_tenant()
    {
        var root = new InMemoryDatabaseRoot();
        var options = NewOptions(root, Guid.NewGuid().ToString("N"));
        await using var context = new SpaceContext(
            options,
            new TestExecutionContext(Guid.Empty, Guid.NewGuid()),
            new FixedClock());
        context.Models.Add(SpaceModel.Create(Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<SpaceTenantScopeException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_stamps_utc_audit_fields()
    {
        var root = new InMemoryDatabaseRoot();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var context = new SpaceContext(
            NewOptions(root, Guid.NewGuid().ToString("N")),
            new TestExecutionContext(tenantId, actorId),
            new FixedClock());
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        context.Models.Add(model);

        await context.SaveChangesAsync();

        Assert.Equal(Now, model.CreatedAtUtc);
        Assert.Equal(actorId, model.CreatedBy);
    }

    [Fact]
    public void Ef_model_uses_frozen_tables_filters_and_constraints()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(
            new InMemoryDatabaseRoot(),
            Guid.NewGuid().ToString("N"),
            tenantId);

        var model = context.Model.FindEntityType(typeof(SpaceModel))!;
        var version = context.Model.FindEntityType(typeof(SpaceModelVersion))!;
        var file = context.Model.FindEntityType(typeof(SpaceFile))!;
        var source = context.Model.FindEntityType(typeof(SpaceModelSource))!;
        var artifact = context.Model.FindEntityType(typeof(SpaceArtifact))!;

        Assert.Equal("Space_Model", model.GetTableName());
        Assert.Equal("Space_ModelVersion", version.GetTableName());
        Assert.Equal("Space_File", file.GetTableName());
        Assert.Equal("Space_ModelSource", source.GetTableName());
        Assert.Equal("Space_Artifact", artifact.GetTableName());
        Assert.NotNull(model.GetQueryFilter());
        Assert.NotNull(version.GetQueryFilter());
        Assert.NotNull(file.GetQueryFilter());
        Assert.NotNull(source.GetQueryFilter());
        Assert.NotNull(artifact.GetQueryFilter());
        Assert.Contains(
            model.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "SiteId" }));
        Assert.Contains(
            model.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ActiveDraftVersionId" }));
        Assert.Contains(
            version.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ModelId", "VersionNo" }));
        Assert.Contains(
            version.GetForeignKeys(),
            x => x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "ModelId" }));
        Assert.Contains(
            file.GetIndexes(),
            x => x.IsUnique &&
                 x.Properties.Select(p => p.Name)
                     .SequenceEqual(new[] { "TenantId", "Sha256", "RetentionClass" }));
        Assert.Contains(
            source.GetIndexes(),
            x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "TenantId", "Sha256" }));
        Assert.Contains(
            source.GetForeignKeys(),
            x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "TenantId", "ModelVersionId" }));
        Assert.Contains(
            artifact.GetForeignKeys(),
            x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "TenantId", "ModelVersionId", "SourceId" }));
        Assert.True(model.FindProperty(nameof(SpaceModel.RowVersion))!.IsConcurrencyToken);
        Assert.True(version.FindProperty(nameof(SpaceModelVersion.RowVersion))!.IsConcurrencyToken);
        Assert.True(file.FindProperty(nameof(SpaceFile.RowVersion))!.IsConcurrencyToken);
        Assert.True(source.FindProperty(nameof(SpaceModelSource.RowVersion))!.IsConcurrencyToken);
    }

    private static SpaceContext CreateContext(
        InMemoryDatabaseRoot root,
        string database,
        Guid tenantId) =>
        new(
            NewOptions(root, database),
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private static DbContextOptions<SpaceContext> NewOptions(
        InMemoryDatabaseRoot root,
        string database) =>
        new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(database, root)
            .Options;

    private static SpaceFile NewCleanFile(
        Guid tenantId,
        string extension,
        SpaceFileRetentionClass retentionClass,
        string? hash = null)
    {
        var file = SpaceFile.CreateUploading(
            Guid.NewGuid(),
            tenantId,
            $"quarantine/{Guid.NewGuid():N}",
            $"input{extension}",
            extension == ".pdf" ? "application/pdf" : "image/png",
            retentionClass);
        file.CompleteQuarantine(
            extension == ".pdf" ? "application/pdf" : "image/png",
            extension,
            12,
            hash ?? new string('a', 64));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        return file;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
