using CP6.Entity.DomainModels.Plm;
using CP6.Entity.DTOs.Plm;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Plm;

[Collection(PlmSqlServerCollection.Name)]
public sealed class PlmSqlServerMigrationTests(PlmSqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task ContextAndSchema()
    {
        Assert.Equal(5, await ScalarAsync("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name='plm' AND t.name IN ('IterationCandidate','EngineeringCandidate','TechnicalManifest','TechnicalManifestItem','EngineeringBaseline')"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name='plm' AND t.name='__EFMigrationsHistory_Plm'"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID('plm')) AND referenced_object_id IN (OBJECT_ID('dbo.T_ProductMaster'),OBJECT_ID('dbo.T_ProductMaterial'),OBJECT_ID('dbo.T_ProductProcess'))"));
        await using var plm = fixture.Plm(fixture.TenantA);
        await plm.Database.MigrateAsync();
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM [plm].[__EFMigrationsHistory_Plm] WHERE MigrationId='20260925150000_PlmWp0Foundation'"));
    }

    [SqlServerFact]
    public async Task DistinctIterationAndEngineeringCandidateIdentities()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (iterationId, candidateId, _) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        Assert.NotEqual(iterationId, candidateId);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.Equal(iterationId, (await db.EngineeringCandidates.SingleAsync(x => x.Id == candidateId)).IterationCandidateId);
        Assert.Equal(1, await db.IterationCandidates.CountAsync(x => x.Id == iterationId));
        Assert.Equal(1, await db.EngineeringCandidates.CountAsync(x => x.Id == candidateId));
    }

    [SqlServerFact]
    public async Task EngineeringCandidateTracesIterationVersion()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var service = fixture.Service(fixture.TenantA);
        var iteration = await service.CreateIterationCandidateAsync(fixture.TenantA, "wp0-test", new($"ITER-{Guid.NewGuid():N}", 7, new string('a', 64)), CancellationToken.None);
        var candidate = await service.CreateEngineeringCandidateAsync(fixture.TenantA, "wp0-test", new(iteration.Value.IterationCandidateId, $"CAND-{Guid.NewGuid():N}", product), CancellationToken.None);
        await using var db = fixture.Plm(fixture.TenantA);
        var trace = await (from c in db.EngineeringCandidates join i in db.IterationCandidates on c.IterationCandidateId equals i.Id where c.Id == candidate.Value.CandidateId select new { i.Id, i.IterationVersion }).SingleAsync();
        Assert.Equal(iteration.Value.IterationCandidateId, trace.Id);
        Assert.Equal(7, trace.IterationVersion);
        await using var other = fixture.Plm(fixture.TenantB);
        other.EngineeringCandidates.Add(new PlmEngineeringCandidate
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantB, IterationCandidateId = iteration.Value.IterationCandidateId,
            CandidateKey = $"CAND-{Guid.NewGuid():N}", ProductCd = product, CandidateVersion = 1, Status = "DRAFT",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, CreatedBy = "test", UpdatedBy = "test"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task SeparateReviewAndTechnicalDigests()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (iterationId, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var capture = await fixture.Service(fixture.TenantA).CaptureTechnicalManifestAsync(fixture.TenantA, "wp0-test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        await using var db = fixture.Plm(fixture.TenantA);
        var iteration = await db.IterationCandidates.SingleAsync(x => x.Id == iterationId);
        var manifest = await db.TechnicalManifests.SingleAsync(x => x.Id == capture.Value.ManifestId);
        Assert.Equal(new string('a', 64), iteration.ReviewPackageDigest);
        Assert.Equal(capture.Value.ManifestDigest, manifest.ManifestDigest);
        Assert.NotEqual(iteration.ReviewPackageDigest, manifest.ManifestDigest);
    }

    [SqlServerFact]
    public async Task ItemIdentityUnique()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var manifest = await fixture.Service(fixture.TenantA).CaptureTechnicalManifestAsync(fixture.TenantA, "wp0-test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        await using var db = fixture.Plm(fixture.TenantA);
        var items = await db.TechnicalManifestItems.Where(x => x.ManifestId == manifest.Value.ManifestId).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(3, items.Select(x => x.IdentityKey).Distinct(StringComparer.Ordinal).Count());
        var bom = items.Single(x => x.ItemType == "BOM");
        db.TechnicalManifestItems.Add(new PlmTechnicalManifestItem
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantA, ManifestId = bom.ManifestId, ItemType = bom.ItemType,
            SourceOwnerType = bom.SourceOwnerType, IdentityKey = bom.IdentityKey, ProductCd = bom.ProductCd,
            ProcessCd = bom.ProcessCd, MaterialCd = bom.MaterialCd, SemanticSortOrder = 20,
            ItemCanonicalBytes = bom.ItemCanonicalBytes, ItemDigest = bom.ItemDigest, SourceOwnerId = bom.SourceOwnerId,
            CapturedAtUtc = DateTime.UtcNow, CapturedBy = "test"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task BaselineBindsCandidateAndManifestVersion()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        await using var db = fixture.Plm(fixture.TenantA);
        var baseline = await db.EngineeringBaselines.SingleAsync(x => x.Id == frozen.BaselineId);
        var manifest = await db.TechnicalManifests.SingleAsync(x => x.Id == baseline.ManifestId);
        Assert.Equal(frozen.CandidateId, baseline.EngineeringCandidateId);
        Assert.Equal(frozen.Version, baseline.CandidateVersion);
        Assert.Equal(manifest.CandidateVersion, baseline.CandidateVersion);
        Assert.Equal(manifest.ManifestDigest, baseline.ManifestDigest);
        Assert.Equal("plm-technical-manifest-v1", manifest.SchemaVersion);
    }

    [SqlServerFact]
    public async Task DistinctCandidatesMayShareTechnicalDigest()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var first = await fixture.FreezeAsync(fixture.TenantA, product);
        var second = await fixture.FreezeAsync(fixture.TenantA, product);
        Assert.NotEqual(first.CandidateId, second.CandidateId);
        Assert.NotEqual(first.BaselineId, second.BaselineId);
        Assert.Equal(first.Digest, second.Digest);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.Equal(2, await db.EngineeringBaselines.CountAsync(x => x.ManifestDigest == first.Digest));
    }

    [SqlServerFact]
    public async Task StartupOrderAndTriggers()
    {
        Assert.Equal(5, await ScalarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id IN (SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID('plm'))"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('plm.EngineeringBaseline') AND name='UX_PlmBaseline_Tenant_Candidate_Version' AND is_unique=1 AND has_filter=0"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM [plm].[__EFMigrationsHistory_Plm] WHERE MigrationId='20260925150000_PlmWp0Foundation'"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE MigrationId='20260925150000_PlmWp0Foundation'"));
    }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
