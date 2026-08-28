using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceCadProviderSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly string DatasetSha256 = new('d', 64);
    private static readonly string EnvironmentSha256 = new('e', 64);

    [SqlServerFact]
    public async Task Concurrent_replace_preserves_one_current_revision_and_immutable_evidence()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<ReplaceAttempt> ReplaceAsync(string key)
            {
                var execution = new TestExecution(tenantId, Guid.NewGuid());
                await using var context = CreateContext(
                    connectionString,
                    execution);
                var service = NewService(context, execution);
                await start.Task;
                try
                {
                    return new ReplaceAttempt(
                        await service.ReplaceAsync(
                            siteId,
                            Configuration(expectedRevision: 0),
                            key),
                        null);
                }
                catch (SpaceProblemException problem)
                {
                    return new ReplaceAttempt(null, problem);
                }
            }

            var first = ReplaceAsync("provider-config-a");
            var second = ReplaceAsync("provider-config-b");
            start.SetResult();
            var outcomes = await Task.WhenAll(first, second)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Single(outcomes, outcome => outcome.Response is not null);
            var conflict = Assert.Single(
                outcomes,
                outcome => outcome.Problem is not null);
            Assert.Equal(
                SpaceErrorCodes.CadProviderRevisionConflict,
                conflict.Problem!.Code);

            var verifierExecution = new TestExecution(tenantId, Guid.NewGuid());
            await using var verifier = CreateContext(
                connectionString,
                verifierExecution);
            var configuration = Assert.Single(
                await verifier.CadProviderConfigurations
                    .AsNoTracking()
                    .ToListAsync());
            Assert.True(configuration.IsCurrent);
            Assert.Equal(1, configuration.ConfigurationRevision);
            Assert.Equal(
                2,
                await verifier.CadProviderCertifications.CountAsync());
            Assert.Equal(1, await verifier.IdempotencyRecords.CountAsync());

            var service = NewService(verifier, verifierExecution);
            var replaced = await service.ReplaceAsync(
                siteId,
                Configuration(expectedRevision: 1),
                "provider-config-next");
            Assert.Equal(2, replaced.Capability.ConfigurationRevision);
            Assert.True(replaced.Capability.CadGaReady);
            Assert.Equal(92, replaced.Capability.Primary!.QualificationScore);
            Assert.Equal("1.0", replaced.Capability.Primary.ProviderVersion);
            Assert.Equal(86, replaced.Capability.Backup!.QualificationScore);
            Assert.True(replaced.Capability.Primary.Qualified);
            Assert.True(replaced.Capability.Backup.Qualified);

            verifier.ChangeTracker.Clear();
            var history = await verifier.CadProviderConfigurations
                .AsNoTracking()
                .OrderBy(item => item.ConfigurationRevision)
                .ToArrayAsync();
            Assert.Equal(2, history.Length);
            Assert.False(history[0].IsCurrent);
            Assert.True(history[1].IsCurrent);
            Assert.Equal(
                1,
                await verifier.CadProviderConfigurations
                    .CountAsync(item => item.IsCurrent));

            var certification = await verifier.CadProviderCertifications
                .OrderBy(item => item.ProviderKey)
                .FirstAsync();
            verifier.Entry(certification)
                .Property(item => item.ApprovalEvidenceReference)
                .CurrentValue = "evidence://tampered";
            var immutable = await Assert.ThrowsAsync<InvalidOperationException>(
                () => verifier.SaveChangesAsync());
            Assert.Contains(
                "immutable",
                immutable.Message,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [SqlServerFact]
    public async Task Legacy_certification_without_qualification_is_fail_closed()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            var service = NewService(context, execution);
            _ = await service.ReplaceAsync(
                siteId,
                Configuration(expectedRevision: 0),
                "qualified-config");
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE [Space_CadSiteProviderCertification]
                SET [LicensingApproved] = 0,
                    [SecurityApproved] = 0,
                    [DataRegionApproved] = 0,
                    [DeletionRetentionApproved] = 0,
                    [QualificationScore] = NULL,
                    [QualificationRubricVersion] = NULL,
                    [GoldenDatasetSha256] = NULL,
                    [FrozenEnvironmentSha256] = NULL,
                    [QualificationEvidenceReference] = NULL
                WHERE [TenantId] = {0} AND [SiteId] = {1}
                """, tenantId, siteId);
            context.ChangeTracker.Clear();

            var capability = await service.GetAsync(siteId);
            Assert.False(capability.CanPrepareCad);
            Assert.False(capability.CadGaReady);
            Assert.Contains(
                "CAD_PRIMARY_QUALIFICATION_INCOMPLETE",
                capability.BlockingCodes);

            var router = new SpaceCadProviderRouter(
                context,
                Registry(),
                new FixedClock(),
                Microsoft.Extensions.Logging.Abstractions
                    .NullLogger<SpaceCadProviderRouter>.Instance);
            await using var source = new MemoryStream([1, 2, 3]);
            var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                router.InspectAsync(
                    new SpaceCadPreparationProviderRequest(
                        tenantId,
                        siteId,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        new string('a', 64),
                        SpaceCadSourceFormat.Dwg,
                        SpaceWorkerSandboxPolicy.FileSafetyDefault),
                    source));
            Assert.Equal(SpaceErrorCodes.CadProviderUnavailable, problem.Code);
        });
    }

    [SqlServerFact]
    public async Task Legacy_certification_without_provider_version_is_fail_closed()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            var service = NewService(context, execution);
            _ = await service.ReplaceAsync(
                siteId,
                Configuration(expectedRevision: 0),
                "qualified-config");
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE [Space_CadSiteProviderCertification]
                SET [ProviderVersion] = ''
                WHERE [TenantId] = {0} AND [SiteId] = {1}
                """, tenantId, siteId);
            context.ChangeTracker.Clear();

            var capability = await service.GetAsync(siteId);

            Assert.False(capability.CanPrepareCad);
            Assert.False(capability.CadGaReady);
            Assert.False(capability.Primary!.Qualified);
            Assert.False(capability.Backup!.Qualified);
            Assert.Contains(
                "CAD_PRIMARY_QUALIFICATION_INCOMPLETE",
                capability.BlockingCodes);
        });
    }

    private static ReplaceSpaceCadProviderConfigurationRequest Configuration(
        long expectedRevision) =>
        new(
            expectedRevision,
            "Approved SQL Server test configuration",
            [
                new SpaceCadProviderCertificationInputDto(
                    "primary.local",
                    "1.0",
                    "Primary",
                    "OnPremisesIsolatedWorker",
                    "SiteLocal",
                    "evidence://security/primary",
                    SecretReference: null,
                    Now.AddDays(-1),
                    Now.AddDays(90),
                    SupportsDwg: true,
                    SupportsDxf: true,
                    LicensingApproved: true,
                    SecurityApproved: true,
                    DataRegionApproved: true,
                    DeletionRetentionApproved: true,
                    QualificationScore: 92,
                    QualificationRubricVersion: "cad-ga-v1",
                    GoldenDatasetSha256: DatasetSha256,
                    FrozenEnvironmentSha256: EnvironmentSha256,
                    QualificationEvidenceReference: "evidence://qualification/primary"),
                new SpaceCadProviderCertificationInputDto(
                    "backup.cloud",
                    "1.0",
                    "Backup",
                    "ApprovedCloudService",
                    "CustomerApprovedCloudRegion",
                    "evidence://security/backup",
                    "keyvault://cad/backup",
                    Now.AddDays(-1),
                    Now.AddDays(90),
                    SupportsDwg: true,
                    SupportsDxf: true,
                    LicensingApproved: true,
                    SecurityApproved: true,
                    DataRegionApproved: true,
                    DeletionRetentionApproved: true,
                    QualificationScore: 86,
                    QualificationRubricVersion: "cad-ga-v1",
                    GoldenDatasetSha256: DatasetSha256,
                    FrozenEnvironmentSha256: EnvironmentSha256,
                    QualificationEvidenceReference: "evidence://qualification/backup"),
            ]);

    private static SpaceCadProviderCapabilityService NewService(
        SpaceContext context,
        TestExecution execution) =>
        new(
            context,
            execution,
            new AllowAccess(),
            Registry(),
            new FixedClock());

    private static ISpaceCadProviderRegistry Registry()
    {
        var primary = new NoopProvider();
        var backup = new NoopProvider();
        return new SpaceCadProviderRegistry(
        [
            new SpaceCadProviderRegistration(
                "primary.local",
                "1.0",
                "Primary local Provider",
                SpaceCadProviderDeploymentMode.OnPremisesIsolatedWorker,
                SpaceCadProviderDataBoundary.SiteLocal,
                supportsDwg: true,
                supportsDxf: true,
                primary,
                primary),
            new SpaceCadProviderRegistration(
                "backup.cloud",
                "1.0",
                "Approved cloud backup",
                SpaceCadProviderDeploymentMode.ApprovedCloudService,
                SpaceCadProviderDataBoundary.CustomerApprovedCloudRegion,
                supportsDwg: true,
                supportsDxf: true,
                backup,
                backup),
        ]);
    }

    private static async Task WithDatabaseAsync(
        Func<string, Guid, Guid, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceCadProviders_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var execution = new TestExecution(tenantId, Guid.NewGuid());
        await using var setup = CreateContext(connectionString, execution);
        try
        {
            await setup.Database.MigrateAsync();
            await ExecuteIdempotentMigrationScriptTwiceAsync(setup);
            await action(connectionString, tenantId, siteId);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task ExecuteIdempotentMigrationScriptTwiceAsync(
        SpaceContext context)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        await context.Database.OpenConnectionAsync();
        try
        {
            foreach (var scriptName in new[]
                     {
                         "20260814011926_SpaceCadProviderRouting.sql",
                         "20260814051514_SpaceCadProviderQualificationEvidence.sql",
                         "20260814063519_SpaceCadProviderVersionFence.sql",
                     })
            {
                var scriptPath = Path.Combine(
                    repositoryRoot,
                    "CP6.Space.Infrastructure",
                    "Migrations",
                    "Scripts",
                    scriptName);
                var batches = Regex.Split(
                        await File.ReadAllTextAsync(scriptPath),
                        @"(?im)^\s*GO\s*$")
                    .Where(batch => !string.IsNullOrWhiteSpace(batch))
                    .ToArray();
                for (var pass = 0; pass < 2; pass++)
                {
                    foreach (var batch in batches)
                        await context.Database.ExecuteSqlRawAsync(batch);
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        ISpaceExecutionContext execution)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, new FixedClock());
    }

    private sealed record TestExecution(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => false;
        public string? ExternalSubjectType => null;
        public Guid? ExternalOrganizationId => null;
        public string ActorDisplayName => "CAD Provider SQL test";
    }

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

    private sealed class NoopProvider :
        ISpaceCadPreparationProvider,
        ISpaceCadParseProvider
    {
        public Task<SpaceCadIrPackageV1> InspectAsync(
            SpaceCadPreparationProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
            SpaceCadParseProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record ReplaceAttempt(
        ReplaceSpaceCadProviderConfigurationResponse? Response,
        SpaceProblemException? Problem);
}
