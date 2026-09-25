using System.Data;
using System.Data.SqlTypes;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs.Plm;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Plm;

[Collection(PlmSqlServerCollection.Name)]
public sealed class PlmSqlServerConcurrencyTests(PlmSqlServerFixture fixture)
{
    private async Task<(Guid CandidateId, TechnicalManifestResult Manifest, TechnicalManifestResult Validated)> PreparedAsync(string product)
    {
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var captured = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        var validated = await service.ValidateTechnicalManifestAsync(fixture.TenantA, "test", candidateId, captured.Value.ManifestId,
            new(1, captured.Value.CandidateRowVersion, captured.Value.ManifestDigest), CancellationToken.None);
        return (candidateId, captured.Value, validated);
    }

    [SqlServerFact]
    public async Task TwoFreezeRequestsOneCandidateVersion()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var ready = await PreparedAsync(product);
        var service = fixture.Service(fixture.TenantA);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(EngineeringBaselineResult? Baseline, string? Error)> Attempt(Guid key)
        {
            await barrier.Task;
            try
            {
                var result = await service.FreezeEngineeringBaselineAsync(fixture.TenantA, "test", ready.CandidateId,
                    new(key, 1, ready.Validated.CandidateRowVersion, ready.Manifest.ManifestId, ready.Manifest.ManifestDigest), CancellationToken.None);
                return (result.Value, null);
            }
            catch (PlmException e) { return (null, e.Code); }
        }
        var first = Attempt(Guid.NewGuid());
        var second = Attempt(Guid.NewGuid());
        barrier.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, x => x.Baseline is not null);
        Assert.Single(outcomes, x => x.Error == "PLM_BASELINE_EXISTS");
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.Equal(1, await db.EngineeringBaselines.CountAsync(x => x.EngineeringCandidateId == ready.CandidateId && x.CandidateVersion == 1));
        var winner = outcomes.Single(x => x.Baseline is not null).Baseline!;
        var original = await db.EngineeringBaselines.SingleAsync(x => x.Id == winner.BaselineId);
        var sameRequest = await service.FreezeEngineeringBaselineAsync(fixture.TenantA, "test", ready.CandidateId,
            new(original.FreezeRequestId, 1, ready.Validated.CandidateRowVersion, ready.Manifest.ManifestId, ready.Manifest.ManifestDigest), CancellationToken.None);
        Assert.False(sameRequest.Created);
        Assert.Equal(winner.BaselineId, sameRequest.Value.BaselineId);
    }

    [SqlServerFact]
    public async Task ConcurrentSameFreezeRequestIsIdempotent()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var ready = await PreparedAsync(product);
        var service = fixture.Service(fixture.TenantA);
        var command = new FreezeEngineeringBaselineCommand(Guid.NewGuid(), 1,
            ready.Validated.CandidateRowVersion, ready.Manifest.ManifestId, ready.Manifest.ManifestDigest);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PlmOperation<EngineeringBaselineResult>> Attempt()
        {
            await barrier.Task;
            return await service.FreezeEngineeringBaselineAsync(fixture.TenantA, "test", ready.CandidateId, command, CancellationToken.None);
        }
        var one = Attempt(); var two = Attempt();
        barrier.SetResult();
        var results = await Task.WhenAll(one, two);
        Assert.Equal(results[0].Value.BaselineId, results[1].Value.BaselineId);
        Assert.Single(results, x => x.Created);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.Equal(1, await db.EngineeringBaselines.CountAsync(x => x.EngineeringCandidateId == ready.CandidateId && x.CandidateVersion == 1));
    }

    [SqlServerFact]
    public async Task ManifestDigestTamperBlocksFreeze()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var ready = await PreparedAsync(product);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await new SqlCommand("DISABLE TRIGGER [TR_PlmManifest_Immutable] ON [plm].[TechnicalManifest]", connection).ExecuteNonQueryAsync();
            try
            {
                await using var update = new SqlCommand("UPDATE [plm].[TechnicalManifest] SET [ManifestDigest]=@digest WHERE [Id]=@id", connection);
                update.Parameters.AddWithValue("@digest", new string('b', 64));
                update.Parameters.AddWithValue("@id", ready.Manifest.ManifestId);
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
            }
            finally
            {
                await new SqlCommand("ENABLE TRIGGER [TR_PlmManifest_Immutable] ON [plm].[TechnicalManifest]", connection).ExecuteNonQueryAsync();
            }
        }
        var service = fixture.Service(fixture.TenantA);
        var error = await Assert.ThrowsAsync<PlmException>(() => service.FreezeEngineeringBaselineAsync(fixture.TenantA, "test", ready.CandidateId,
            new(Guid.NewGuid(), 1, ready.Validated.CandidateRowVersion, ready.Manifest.ManifestId, ready.Manifest.ManifestDigest), CancellationToken.None));
        Assert.Equal("PLM_MANIFEST_INTEGRITY", error.Code);
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.False(await db.EngineeringBaselines.AnyAsync(x => x.EngineeringCandidateId == ready.CandidateId));
    }

    [SqlServerFact]
    public async Task FrozenBaselineAndManifestImmutable()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        async Task Reject(string sql)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@id", frozen.ManifestId);
            command.Parameters.AddWithValue("@baseline", frozen.BaselineId);
            await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        }
        await Reject("UPDATE [plm].[TechnicalManifest] SET [ManifestDigest]=[ManifestDigest] WHERE [Id]=@id");
        await Reject("DELETE FROM [plm].[TechnicalManifestItem] WHERE [ManifestId]=@id");
        await Reject("UPDATE [plm].[EngineeringBaseline] SET [ManifestDigest]=[ManifestDigest] WHERE [Id]=@baseline");
        var baseline = await fixture.Service(fixture.TenantA).GetEngineeringBaselineAsync(fixture.TenantA, frozen.BaselineId, CancellationToken.None);
        Assert.Equal("FROZEN", baseline.Status);
        Assert.Equal(frozen.Digest, baseline.ManifestDigest);
    }

    [SqlServerFact]
    public async Task SupersededBaselineStillResolves()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var before = await service.GetEngineeringBaselineAsync(fixture.TenantA, frozen.BaselineId, CancellationToken.None);
        var supersedeCommand = new SupersedeEngineeringBaselineCommand(Guid.NewGuid(), before.RowVersion, "new candidate version");
        var superseded = await service.SupersedeEngineeringBaselineAsync(fixture.TenantA, "test", frozen.BaselineId,
            supersedeCommand, CancellationToken.None);
        Assert.Equal("SUPERSEDED", superseded.Status);
        var replay = await service.SupersedeEngineeringBaselineAsync(fixture.TenantA, "test", frozen.BaselineId,
            supersedeCommand, CancellationToken.None);
        Assert.Equal(superseded.BaselineId, replay.BaselineId);
        Assert.Equal("PLM_REQUEST_PAYLOAD_CONFLICT", (await Assert.ThrowsAsync<PlmException>(() =>
            service.SupersedeEngineeringBaselineAsync(fixture.TenantA, "test", frozen.BaselineId,
                supersedeCommand with { Reason = "changed reason" }, CancellationToken.None))).Code);
        await using (var db = fixture.Plm(fixture.TenantA))
        {
            var candidate = await db.EngineeringCandidates.AsNoTracking().SingleAsync(x => x.Id == frozen.CandidateId);
            var recapture = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", frozen.CandidateId,
                new(Guid.NewGuid(), 1, candidate.RowVersion), CancellationToken.None);
            Assert.Equal(2, recapture.Value.CandidateVersion);
        }
        var resolved = await service.ResolveBaselineIdentityAsync(fixture.TenantA, frozen.CandidateId, 1, CancellationToken.None);
        Assert.Equal(frozen.BaselineId, resolved.BaselineId);
        Assert.Equal(frozen.ManifestId, resolved.ManifestId);
        Assert.Equal(frozen.Digest, resolved.ManifestDigest);
        Assert.Equal("SUPERSEDED", resolved.Status);
    }

    [SqlServerFact]
    public async Task SemanticMutationChangesDigestWithoutChangingOwnerIdentity()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var digests = new HashSet<string> { frozen.Digest };
        var version = 1;
        foreach (var change in new[] { "PRODUCT", "BOM", "ROUTING" })
        {
            await using (var owner = fixture.Owner(fixture.TenantA))
            {
                if (change == "PRODUCT") (await owner.ProductMasters.SingleAsync(x => x.ProductCd == product)).ProductShape = "BOX";
                if (change == "BOM") (await owner.ProductMaterials.SingleAsync(x => x.ProductCd == product)).UnitUsage = 2m;
                if (change == "ROUTING") (await owner.ProductProcesses.SingleAsync(x => x.ProductCd == product)).MachineFixedFlg = true;
                await owner.SaveChangesAsync();
            }
            await using var db = fixture.Plm(fixture.TenantA);
            var candidate = await db.EngineeringCandidates.AsNoTracking().SingleAsync(x => x.Id == frozen.CandidateId);
            var capture = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidate.Id,
                new(Guid.NewGuid(), version, candidate.RowVersion), CancellationToken.None);
            version++;
            Assert.Equal(version, capture.Value.CandidateVersion);
            Assert.True(digests.Add(capture.Value.ManifestDigest));
        }
        var historical = await service.GetEngineeringBaselineAsync(fixture.TenantA, frozen.BaselineId, CancellationToken.None);
        Assert.Equal(frozen.Digest, historical.ManifestDigest);
    }

    [SqlServerFact]
    public async Task MetadataOnlyChangePreservesBaseline()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        await using (var owner = fixture.Owner(fixture.TenantA))
        {
            var master = await owner.ProductMasters.SingleAsync(x => x.ProductCd == product);
            master.Modifier = "metadata-only";
            master.ModifyDate = DateTime.UtcNow;
            var material = await owner.ProductMaterials.SingleAsync(x => x.ProductCd == product);
            owner.ProductMaterials.Remove(material);
            await owner.SaveChangesAsync();
        }
        await using (var owner = fixture.Owner(fixture.TenantA))
        {
            owner.ProductMaterials.Add(new ProductMaterial
            {
                ProductCd = product, ProcessCd = "PROC-A", MaterialCd = "MAT-A", MaterialTypeDiv = "1",
                UsageType = 1, UnitUsage = 1m, SortOrder = 10
            });
            await owner.SaveChangesAsync();
        }
        await using var db = fixture.Plm(fixture.TenantA);
        var candidate = await db.EngineeringCandidates.AsNoTracking().SingleAsync(x => x.Id == frozen.CandidateId);
        var next = await fixture.Service(fixture.TenantA).CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidate.Id,
            new(Guid.NewGuid(), 1, candidate.RowVersion), CancellationToken.None);
        Assert.Equal(frozen.Digest, next.Value.ManifestDigest);
        var old = await fixture.Service(fixture.TenantA).GetEngineeringBaselineAsync(fixture.TenantA, frozen.BaselineId, CancellationToken.None);
        Assert.Equal(frozen.BaselineId, old.BaselineId);
        Assert.Equal(frozen.Digest, old.ManifestDigest);
    }

    [SqlServerFact]
    public async Task OwnerRangeLockQueryPlansUseBusinessKeyIndexes()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await new SqlCommand("SET STATISTICS XML ON", connection).ExecuteNonQueryAsync();
        try
        {
            foreach (var (table, index) in new[]
            {
                ("T_ProductMaterial", "IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd"),
                ("T_ProductProcess", "IX_T_ProductProcess_ProductCd_TaskCd")
            })
            {
                await using var command = new SqlCommand($"SELECT * FROM [dbo].[{table}] WITH (UPDLOCK,HOLDLOCK,INDEX({index})) WHERE [TenantId]=@tenant AND [ProductCd]=@product AND [IsDeleted]=CAST(0 AS bit)", connection);
                command.Parameters.AddWithValue("@tenant", fixture.TenantA);
                command.Parameters.AddWithValue("@product", "P-NONEXISTENT");
                string? plan = null;
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    do
                    {
                        while (await reader.ReadAsync())
                        {
                            var value = reader.GetValue(0);
                            var text = value is SqlXml xml ? xml.Value : value?.ToString();
                            if (text?.Contains("ShowPlanXML", StringComparison.Ordinal) == true) plan = text;
                        }
                    } while (await reader.NextResultAsync());
                }
                Assert.Contains(index, plan, StringComparison.Ordinal);
            }
        }
        finally { await new SqlCommand("SET STATISTICS XML OFF", connection).ExecuteNonQueryAsync(); }
    }

    [SqlServerFact]
    public async Task EmptyChildRangeLockBlocksConcurrentInsert()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA, withChildren: false);
        await using var reader = fixture.Owner(fixture.TenantA);
        await using var tx = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var empty = await reader.ProductMaterials.FromSqlInterpolated($"SELECT * FROM [dbo].[T_ProductMaterial] WITH (UPDLOCK,HOLDLOCK,INDEX(IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd)) WHERE [TenantId]={fixture.TenantA} AND [ProductCd]={product} AND [IsDeleted]=CAST(0 AS bit)").ToListAsync();
        Assert.Empty(empty);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var insert = Task.Run(async () =>
        {
            await using var writer = fixture.Owner(fixture.TenantA);
            writer.ProductMaterials.Add(new ProductMaterial
            {
                ProductCd = product, ProcessCd = "PROC-A", MaterialCd = "MAT-A", MaterialTypeDiv = "1",
                UsageType = 1, UnitUsage = 1m, SortOrder = 10
            });
            started.SetResult();
            await writer.SaveChangesAsync();
        });
        await started.Task;
        await Task.Delay(300);
        Assert.False(insert.IsCompleted);
        await tx.CommitAsync();
        await insert;
        await using var verify = fixture.Owner(fixture.TenantA);
        Assert.Equal(1, await verify.ProductMaterials.CountAsync(x => x.ProductCd == product));
    }

    [SqlServerFact]
    public async Task CaptureVsOwnerMutation()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA, withChildren: false);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        await using var owner = fixture.Owner(fixture.TenantA);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var empty = await owner.ProductMaterials.FromSqlInterpolated($"SELECT * FROM [dbo].[T_ProductMaterial] WITH (UPDLOCK,HOLDLOCK,INDEX(IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd)) WHERE [ProductCd]={product} AND [TenantId]={fixture.TenantA} AND [IsDeleted]=CAST(0 AS bit)").ToListAsync();
        Assert.Empty(empty);
        (await owner.ProductMasters.SingleAsync(x => x.ProductCd == product)).ProductShape = "BOX";
        owner.ProductMaterials.Add(new ProductMaterial
        {
            ProductCd = product, ProcessCd = "PROC-A", MaterialCd = "MAT-A", MaterialTypeDiv = "1",
            UsageType = 1, UnitUsage = 1m, SortOrder = 10
        });
        await owner.SaveChangesAsync(); // One uncommitted owner aggregate mutation.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureTask = Task.Run(async () =>
        {
            start.SetResult();
            return await fixture.Service(fixture.TenantA).CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
                new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        });
        await start.Task;
        await Task.Delay(300);
        Assert.False(captureTask.IsCompleted); // PLM waits for the uncommitted owner aggregate.
        await transaction.CommitAsync();
        var capture = await captureTask;
        Assert.Equal(2, capture.Value.ItemCount);
        var persisted = await fixture.Service(fixture.TenantA).GetTechnicalManifestAsync(fixture.TenantA,
            capture.Value.ManifestId, CancellationToken.None);
        var bytes = System.Text.Encoding.UTF8.GetString(persisted.Manifest.CanonicalBytes);
        Assert.Contains("\"productShape\":\"BOX\"", bytes, StringComparison.Ordinal);
        Assert.Contains("\"materialCd\":\"MAT-A\"", bytes, StringComparison.Ordinal);
    }
}
