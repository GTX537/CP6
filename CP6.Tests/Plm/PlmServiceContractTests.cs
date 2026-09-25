using CP6.Entity.DTOs.Plm;
using CP6.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Plm;

[Collection(PlmSqlServerCollection.Name)]
public sealed class PlmServiceContractTests(PlmSqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task BusinessVersionDistinctFromRowVersion()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, firstRowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var capture = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, firstRowVersion), CancellationToken.None);
        Assert.Equal(1, capture.Value.CandidateVersion);
        Assert.NotEqual(firstRowVersion, capture.Value.CandidateRowVersion);
        var validated = await service.ValidateTechnicalManifestAsync(fixture.TenantA, "test", candidateId, capture.Value.ManifestId,
            new(1, capture.Value.CandidateRowVersion, capture.Value.ManifestDigest), CancellationToken.None);
        Assert.Equal(1, validated.CandidateVersion);
        Assert.NotEqual(capture.Value.CandidateRowVersion, validated.CandidateRowVersion);
        var recapture = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, validated.CandidateRowVersion), CancellationToken.None);
        Assert.Equal(2, recapture.Value.CandidateVersion);
        Assert.NotEqual(capture.Value.ManifestId, recapture.Value.ManifestId);
        Assert.Equal(capture.Value.ManifestDigest, recapture.Value.ManifestDigest);
    }

    [SqlServerFact]
    public async Task DualDigestRecapture()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (iterationId, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var first = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        await using (var owner = fixture.Owner(fixture.TenantA))
        {
            var current = await owner.ProductMasters.SingleAsync(x => x.ProductCd == product);
            current.ProductShape = "BOX";
            await owner.SaveChangesAsync();
        }
        var second = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, first.Value.CandidateRowVersion), CancellationToken.None);
        Assert.NotEqual(first.Value.ManifestDigest, second.Value.ManifestDigest);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.Equal(new string('a', 64), (await db.IterationCandidates.SingleAsync(x => x.Id == iterationId)).ReviewPackageDigest);
        Assert.Equal(first.Value.ManifestDigest, (await db.TechnicalManifests.SingleAsync(x => x.Id == first.Value.ManifestId)).ManifestDigest);
        Assert.Equal(second.Value.ManifestDigest, (await db.TechnicalManifests.SingleAsync(x => x.Id == second.Value.ManifestId)).ManifestDigest);
    }

    [SqlServerFact]
    public async Task CaptureRetryAndConflict()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var request = new CaptureTechnicalManifestCommand(Guid.NewGuid(), 1, rowVersion);
        var first = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId, request, CancellationToken.None);
        var retry = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId, request, CancellationToken.None);
        Assert.False(retry.Created);
        Assert.Equal(first.Value.ManifestId, retry.Value.ManifestId);
        var changed = request with { ExpectedCandidateRowVersion = first.Value.CandidateRowVersion };
        Assert.Equal("PLM_REQUEST_PAYLOAD_CONFLICT", (await Assert.ThrowsAsync<PlmException>(() =>
            service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId, changed, CancellationToken.None))).Code);
    }

    [SqlServerFact]
    public async Task OwnerChangedRejectsFreeze()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var capture = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        var validated = await service.ValidateTechnicalManifestAsync(fixture.TenantA, "test", candidateId, capture.Value.ManifestId,
            new(1, capture.Value.CandidateRowVersion, capture.Value.ManifestDigest), CancellationToken.None);
        await using (var owner = fixture.Owner(fixture.TenantA))
        {
            var current = await owner.ProductMasters.SingleAsync(x => x.ProductCd == product);
            current.ProductShape = "BOX";
            await owner.SaveChangesAsync();
        }
        Assert.Equal("PLM_OWNER_CHANGED", (await Assert.ThrowsAsync<PlmException>(() =>
            service.FreezeEngineeringBaselineAsync(fixture.TenantA, "test", candidateId,
                new(Guid.NewGuid(), 1, validated.CandidateRowVersion, capture.Value.ManifestId, capture.Value.ManifestDigest), CancellationToken.None))).Code);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.False(await db.EngineeringBaselines.AnyAsync(x => x.EngineeringCandidateId == candidateId));
    }
}
