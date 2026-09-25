using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Erp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit.Sdk;

namespace CP6.Tests.Plm;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlmSqlServerCollection : ICollectionFixture<PlmSqlServerFixture>
{
    public const string Name = "WP0 isolated SQL Server";
}

public sealed class PlmSqlServerFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();
    private bool _ownsDatabase;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CP6_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new SqlConnectionStringBuilder(configured) { TrustServerCertificate = true, MultipleActiveResultSets = true };
        if (!string.IsNullOrWhiteSpace(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WP0 SQL gate requires a master-only connection; refusing a pre-existing database.");
        var databaseName = Environment.GetEnvironmentVariable("CP6_WP0_TEST_DATABASE_NAME")
            ?? $"CP6_WP0_TEST_{Guid.NewGuid():N}";
        if (!databaseName.StartsWith("CP6_WP0_TEST_", StringComparison.Ordinal) || databaseName.Length > 120 ||
            databaseName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("WP0 SQL gate database name must be a disposable CP6_WP0_TEST_* name.");
        var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await using var check = new SqlCommand("SELECT DB_ID(@name)", master);
            check.Parameters.AddWithValue("@name", databaseName);
            if (await check.ExecuteScalarAsync() is not DBNull)
                throw new InvalidOperationException("WP0 disposable database name already exists; refusing to reuse it.");
        }
        builder.InitialCatalog = databaseName;
        Console.WriteLine($"WP0_TEST_DATABASE={databaseName}");
        ConnectionString = builder.ConnectionString;
        _ownsDatabase = true;
        await using (var owner = Owner(TenantA)) await owner.Database.MigrateAsync();
        await using (var owner = Owner(TenantA))
            await owner.Database.ExecuteSqlRawAsync("IF SCHEMA_ID(N'plm') IS NULL EXEC(N'CREATE SCHEMA [plm]')");
        await using (var plm = Plm(TenantA)) await plm.Database.MigrateAsync();
    }

    public CP6Context Owner(Guid tenant)
    {
        EnsureConfigured();
        return new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(180)).Options,
            new TenantContext { CurrentTenantId = tenant });
    }

    public PlmContext Plm(Guid tenant)
    {
        EnsureConfigured();
        return new PlmContext(new DbContextOptionsBuilder<PlmContext>()
            .UseSqlServer(ConnectionString, sql => { sql.MigrationsHistoryTable("__EFMigrationsHistory_Plm", "plm"); sql.CommandTimeout(180); }).Options,
            new TenantContext { CurrentTenantId = tenant });
    }

    public IPlmEngineeringService Service(Guid tenant) => new PlmEngineeringService(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString
        }).Build(), new TenantContext { CurrentTenantId = tenant }, TimeProvider.System);

    public async Task<string> SeedOwnerAsync(Guid tenant, bool withChildren = true)
    {
        var code = $"P-{Guid.NewGuid():N}"[..20];
        await using var db = Owner(tenant);
        db.ProductMasters.Add(new ProductMaster
        {
            ProductCd = code, ItemCd = "ITEM-1", SetProductCd = code, ParentChildDiv = "0",
            CustomerCd = "C-1", SetRatio = 1m, CpItemName1 = "Cafe\u0301", TrackingMode = 0
        });
        if (withChildren)
        {
            db.ProductMaterials.Add(new ProductMaterial
            {
                ProductCd = code, ProcessCd = "PROC-A", MaterialCd = "MAT-A", MaterialTypeDiv = "1",
                UsageType = 1, UnitUsage = 1m, SortOrder = 10
            });
            db.ProductProcesses.Add(new ProductProcess
            {
                ProductCd = code, TaskCd = "TASK-A", ProcessCd = "PROC-A", SortOrder = 10
            });
        }
        await db.SaveChangesAsync();
        return code;
    }

    public async Task<(Guid IterationId, Guid CandidateId, byte[] RowVersion)> CreateCandidateAsync(Guid tenant, string productCd)
    {
        var service = Service(tenant);
        var iteration = await service.CreateIterationCandidateAsync(tenant, "wp0-test",
            new($"ITER-{Guid.NewGuid():N}", 1, new string('a', 64)), CancellationToken.None);
        var candidate = await service.CreateEngineeringCandidateAsync(tenant, "wp0-test",
            new(iteration.Value.IterationCandidateId, $"CAND-{Guid.NewGuid():N}", productCd), CancellationToken.None);
        return (iteration.Value.IterationCandidateId, candidate.Value.CandidateId, candidate.Value.RowVersion);
    }

    public async Task<(Guid CandidateId, int Version, byte[] RowVersion, Guid ManifestId, string Digest, Guid BaselineId)> FreezeAsync(Guid tenant, string productCd)
    {
        var (_, candidateId, rowVersion) = await CreateCandidateAsync(tenant, productCd);
        var service = Service(tenant);
        var capture = await service.CaptureTechnicalManifestAsync(tenant, "wp0-test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        var validated = await service.ValidateTechnicalManifestAsync(tenant, "wp0-test", candidateId, capture.Value.ManifestId,
            new(1, capture.Value.CandidateRowVersion, capture.Value.ManifestDigest), CancellationToken.None);
        var freeze = await service.FreezeEngineeringBaselineAsync(tenant, "wp0-test", candidateId,
            new(Guid.NewGuid(), 1, validated.CandidateRowVersion, capture.Value.ManifestId, capture.Value.ManifestDigest), CancellationToken.None);
        return (candidateId, 1, validated.CandidateRowVersion, capture.Value.ManifestId, capture.Value.ManifestDigest, freeze.Value.BaselineId);
    }

    public async Task DisposeAsync()
    {
        if (!_ownsDatabase) return;
        await using var db = Owner(TenantA);
        await db.Database.EnsureDeletedAsync();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw SkipException.ForSkip("CP6_TEST_SQLSERVER master connection is required for the isolated WP0 SQL gate.");
    }
}
