using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceCadProviderRoutingTests
{
    private static readonly DateTime Now =
        new(2026, 8, 14, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Atomic_configuration_replacement_reports_two_provider_GA_and_replays()
    {
        await using var fixture = Fixture.Create();
        var request = Configuration(0, includeBackup: true);

        var first = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            request,
            "configure-1");
        var replay = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            request,
            "configure-1");
        var read = await fixture.Service.GetAsync(fixture.SiteId);

        Assert.False(first.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(1, read.ConfigurationRevision);
        Assert.True(read.CanPrepareCad);
        Assert.True(read.CadGaReady);
        Assert.Empty(read.BlockingCodes);
        Assert.Equal("primary.local", read.Primary!.ProviderKey);
        Assert.Equal("backup.cloud", read.Backup!.ProviderKey);
        Assert.Equal(2, await fixture.Context.CadProviderCertifications.CountAsync());
    }

    [Fact]
    public async Task Revision_conflict_preserves_the_current_configuration()
    {
        await using var fixture = Fixture.Create();
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: false),
            "configure-1");

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ReplaceAsync(
                fixture.SiteId,
                Configuration(0, includeBackup: true),
                "stale-config"));

        Assert.Equal(SpaceErrorCodes.CadProviderRevisionConflict, problem.Code);
        Assert.Equal(409, problem.StatusCode);
        Assert.Single(await fixture.Context.CadProviderConfigurations.ToListAsync());
        Assert.Single(await fixture.Context.CadProviderCertifications.ToListAsync());
    }

    [Fact]
    public async Task Retryable_primary_failure_uses_only_the_certified_backup()
    {
        var primary = new TestProvider("primary.local", failPreparation: true);
        var backup = new TestProvider("backup.cloud");
        await using var fixture = Fixture.Create(primary, backup);
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: true),
            "configure-1");
        var router = new SpaceCadProviderRouter(
            fixture.Context,
            fixture.Registry,
            fixture.Clock,
            NullLogger<SpaceCadProviderRouter>.Instance);

        await using var source = new MemoryStream([1, 2, 3]);
        var result = await router.InspectAsync(
            new SpaceCadPreparationProviderRequest(
                fixture.Execution.TenantId,
                fixture.SiteId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new string('a', 64),
                SpaceCadSourceFormat.Dxf,
                SpaceWorkerSandboxPolicy.FileSafetyDefault),
            source);

        Assert.Equal("backup.cloud", result.Document.ConverterId);
        Assert.Equal(1, primary.PreparationCalls);
        Assert.Equal(1, backup.PreparationCalls);
    }

    [Fact]
    public async Task Uncertified_registered_provider_is_never_used_as_fallback()
    {
        var primary = new TestProvider("primary.local", failPreparation: true);
        var backup = new TestProvider("backup.cloud");
        await using var fixture = Fixture.Create(primary, backup);
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: false),
            "primary-only");
        var router = new SpaceCadProviderRouter(
            fixture.Context,
            fixture.Registry,
            fixture.Clock,
            NullLogger<SpaceCadProviderRouter>.Instance);

        await using var source = new MemoryStream([1, 2, 3]);
        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            router.InspectAsync(
                new SpaceCadPreparationProviderRequest(
                    fixture.Execution.TenantId,
                    fixture.SiteId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new string('a', 64),
                    SpaceCadSourceFormat.Dwg,
                    SpaceWorkerSandboxPolicy.FileSafetyDefault),
                source));

        Assert.Equal(SpaceErrorCodes.CadProviderUnavailable, problem.Code);
        Assert.Equal(0, backup.PreparationCalls);
    }

    [Fact]
    public async Task Parse_uses_the_preparation_sealed_provider_then_certified_backup()
    {
        var primary = new TestProvider("primary.local", failParse: true);
        var backup = new TestProvider("backup.cloud");
        await using var fixture = Fixture.Create(primary, backup);
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: true),
            "configure-1");
        var model = SpaceModel.Create(
            fixture.Execution.TenantId,
            fixture.SiteId);
        var version = SpaceModelVersion.CreateDraft(
            fixture.Execution.TenantId,
            model.Id,
            1,
            "CAD routing test");
        fixture.Context.AddRange(model, version);
        await fixture.Context.SaveChangesAsync();
        var router = new SpaceCadProviderRouter(
            fixture.Context,
            fixture.Registry,
            fixture.Clock,
            NullLogger<SpaceCadProviderRouter>.Instance);
        var payload = new SpaceCadParseJobPayload(
            SpaceCadParsePayloadVersions.Current,
            version.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            SpaceCadSourceFormat.Dwg,
            Guid.NewGuid(),
            SpaceCadUnit.Millimeter,
            1,
            "{}",
            new string('b', 64),
            Guid.NewGuid(),
            1,
            new string('c', 64),
            new string('d', 64),
            0,
            null,
            "primary.local",
            new string('e', 64));

        await using var source = new MemoryStream([1, 2, 3]);
        var artifacts = await router.GenerateAsync(
            new SpaceCadParseProviderRequest(
                fixture.Execution.TenantId,
                Guid.NewGuid(),
                payload),
            source);

        Assert.Empty(artifacts);
        Assert.Equal(1, primary.ParseCalls);
        Assert.Equal(1, backup.ParseCalls);
    }

    [Fact]
    public async Task Backup_sealed_by_preparation_never_falls_back_to_primary()
    {
        var primary = new TestProvider("primary.local");
        var backup = new TestProvider("backup.cloud", failParse: true);
        await using var fixture = Fixture.Create(primary, backup);
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: true),
            "configure-1");
        var model = SpaceModel.Create(
            fixture.Execution.TenantId,
            fixture.SiteId);
        var version = SpaceModelVersion.CreateDraft(
            fixture.Execution.TenantId,
            model.Id,
            1,
            "CAD routing test");
        fixture.Context.AddRange(model, version);
        await fixture.Context.SaveChangesAsync();
        var router = new SpaceCadProviderRouter(
            fixture.Context,
            fixture.Registry,
            fixture.Clock,
            NullLogger<SpaceCadProviderRouter>.Instance);
        var payload = Payload(version.Id, "backup.cloud");

        await using var source = new MemoryStream([1, 2, 3]);
        var problem = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            router.GenerateAsync(
                new SpaceCadParseProviderRequest(
                    fixture.Execution.TenantId,
                    Guid.NewGuid(),
                    payload),
                source));

        Assert.Equal(SpaceErrorCodes.CadProviderUnavailable, problem.ErrorCode);
        Assert.Equal(0, primary.ParseCalls);
        Assert.Equal(1, backup.ParseCalls);
    }

    [Fact]
    public async Task Certification_evidence_is_immutable_after_insert()
    {
        await using var fixture = Fixture.Create();
        _ = await fixture.Service.ReplaceAsync(
            fixture.SiteId,
            Configuration(0, includeBackup: false),
            "configure-1");
        var certification = await fixture.Context.CadProviderCertifications.SingleAsync();
        fixture.Context.Entry(certification)
            .Property(item => item.ApprovalEvidenceReference)
            .CurrentValue = "evidence://tampered";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Context.SaveChangesAsync());

        Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ReplaceSpaceCadProviderConfigurationRequest Configuration(
        long revision,
        bool includeBackup)
    {
        var certifications = new List<SpaceCadProviderCertificationInputDto>
        {
            new(
                "primary.local",
                "Primary",
                "OnPremisesIsolatedWorker",
                "SiteLocal",
                "evidence://security/primary",
                SecretReference: null,
                Now.AddDays(-1),
                Now.AddDays(90),
                SupportsDwg: true,
                SupportsDxf: true),
        };
        if (includeBackup)
        {
            certifications.Add(new SpaceCadProviderCertificationInputDto(
                "backup.cloud",
                "Backup",
                "ApprovedCloudService",
                "CustomerApprovedCloudRegion",
                "evidence://security/backup",
                "keyvault://cad/backup",
                Now.AddDays(-1),
                Now.AddDays(90),
                SupportsDwg: true,
                SupportsDxf: true));
        }
        return new ReplaceSpaceCadProviderConfigurationRequest(
            revision,
            "Approved test configuration",
            certifications);
    }

    private static SpaceCadParseJobPayload Payload(
        Guid versionId,
        string providerKey) =>
        new(
            SpaceCadParsePayloadVersions.Current,
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            SpaceCadSourceFormat.Dwg,
            Guid.NewGuid(),
            SpaceCadUnit.Millimeter,
            1,
            "{}",
            new string('b', 64),
            Guid.NewGuid(),
            1,
            new string('c', 64),
            new string('d', 64),
            0,
            null,
            providerKey,
            new string('e', 64));

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            FixedClock clock,
            Guid siteId,
            ISpaceCadProviderRegistry registry,
            SpaceCadProviderCapabilityService service)
        {
            Context = context;
            Execution = execution;
            Clock = clock;
            SiteId = siteId;
            Registry = registry;
            Service = service;
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public FixedClock Clock { get; }
        public Guid SiteId { get; }
        public ISpaceCadProviderRegistry Registry { get; }
        public SpaceCadProviderCapabilityService Service { get; }

        public static Fixture Create(
            TestProvider? primary = null,
            TestProvider? backup = null)
        {
            primary ??= new TestProvider("primary.local");
            backup ??= new TestProvider("backup.cloud");
            var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
            var clock = new FixedClock();
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString("N"),
                        SpaceTestDatabaseRoots.InMemory)
                    .ConfigureWarnings(warnings => warnings.Ignore(
                        InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                execution,
                clock);
            var registry = new SpaceCadProviderRegistry(
            [
                Registration(
                    primary,
                    SpaceCadProviderDeploymentMode.OnPremisesIsolatedWorker,
                    SpaceCadProviderDataBoundary.SiteLocal),
                Registration(
                    backup,
                    SpaceCadProviderDeploymentMode.ApprovedCloudService,
                    SpaceCadProviderDataBoundary.CustomerApprovedCloudRegion),
            ]);
            var siteId = Guid.NewGuid();
            var service = new SpaceCadProviderCapabilityService(
                context,
                execution,
                new AllowAccess(),
                registry,
                clock);
            return new Fixture(context, execution, clock, siteId, registry, service);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private static SpaceCadProviderRegistration Registration(
            TestProvider provider,
            SpaceCadProviderDeploymentMode deployment,
            SpaceCadProviderDataBoundary boundary) =>
            new(
                provider.ProviderKey,
                provider.ProviderKey,
                deployment,
                boundary,
                supportsDwg: true,
                supportsDxf: true,
                provider,
                provider);
    }

    private sealed class TestProvider(
        string providerKey,
        bool failPreparation = false,
        bool failParse = false) :
        ISpaceCadPreparationProvider,
        ISpaceCadParseProvider
    {
        public string ProviderKey { get; } = providerKey;
        public int PreparationCalls { get; private set; }
        public int ParseCalls { get; private set; }

        public Task<SpaceCadIrPackageV1> InspectAsync(
            SpaceCadPreparationProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            PreparationCalls++;
            if (failPreparation)
                throw new SpaceProblemException(
                    SpaceErrorCodes.CadProviderUnavailable,
                    503,
                    "Provider unavailable.",
                    retryable: true);
            return Task.FromResult(new SpaceCadIrPackageV1(
                new SpaceCadIrDocumentV1(
                    SpaceCadIrVersions.SchemaVersion,
                    request.SourceSha256,
                    request.SourceFormat,
                    "AC1032",
                    SpaceCadUnit.Millimeter,
                    1,
                    SpaceCadIrVersions.CoordinateSystem,
                    new SpaceCadBoundsV1(0, 0, 10, 10),
                    ProviderKey,
                    "1.0"),
                [],
                [],
                [],
                [],
                new SpaceCadIrSummaryV1(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new SpaceCadBoundsV1(0, 0, 10, 10))));
        }

        public Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
            SpaceCadParseProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            ParseCalls++;
            if (failParse)
                throw new SpaceProblemException(
                    SpaceErrorCodes.CadProviderUnavailable,
                    503,
                    "Provider unavailable.",
                    retryable: true);
            return Task.FromResult<IReadOnlyList<SpaceCadGeneratedArtifact>>([]);
        }
    }

    private sealed record TestExecution(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => false;
        public string? ExternalSubjectType => null;
        public Guid? ExternalOrganizationId => null;
        public string ActorDisplayName => "CAD Provider test";
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
}
