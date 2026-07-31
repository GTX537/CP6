using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAssetLibrarySqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Library_is_scope_aware_idempotent_and_cross_tenant_safe()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SpaceAssetVersion systemVersion;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var systemAsset = SpaceAsset.CreateSystem(
                    "SYS-RACK",
                    "System Rack",
                    "Rack",
                    "Platform public",
                    execution.ActorId,
                    clock.UtcNow);
                systemVersion = NewAssetVersion(
                    systemAsset,
                    execution.ActorId,
                    clock.UtcNow);
                seed.AddRange(systemAsset, systemVersion);
                await seed.SaveChangesAsync();
            }

            Guid tenantAssetId;
            await using (var tenantContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewService(
                    tenantContext,
                    execution,
                    clock);
                var request = NewAssetRequest("TENANT-RACK");
                var created = await service.CreateAssetAsync(
                    request,
                    "asset-key");
                var replay = await service.CreateAssetAsync(
                    request,
                    "asset-key");

                tenantAssetId = created.Asset.Id;
                Assert.False(created.IdempotentReplay);
                Assert.True(replay.IdempotentReplay);
                Assert.Equal(created.Asset.Id, replay.Asset.Id);
                Assert.Equal("Tenant", created.Asset.Scope);
                Assert.Equal(1, created.Asset.LatestVersion.VersionNo);

                var assets = await service.GetAssetsAsync(
                    null,
                    null,
                    50,
                    null);
                Assert.Equal(2, assets.Items.Count);
                Assert.Contains(
                    assets.Items,
                    asset =>
                        asset.Id == systemVersion.AssetId &&
                        asset.Scope == "System");
                Assert.Contains(
                    assets.Items,
                    asset =>
                        asset.Id == tenantAssetId &&
                        asset.Scope == "Tenant");

                var firstPage = await service.GetAssetsAsync(
                    null,
                    null,
                    1,
                    null);
                Assert.Single(firstPage.Items);
                Assert.NotNull(firstPage.NextCursor);
                var secondPage = await service.GetAssetsAsync(
                    null,
                    null,
                    1,
                    firstPage.NextCursor);
                Assert.Single(secondPage.Items);
                Assert.NotEqual(
                    firstPage.Items[0].Id,
                    secondPage.Items[0].Id);

                var systemOnly = await service.GetAssetsAsync(
                    "System",
                    "Rack",
                    50,
                    null);
                Assert.Single(systemOnly.Items);
                Assert.Equal(
                    systemVersion.AssetId,
                    systemOnly.Items[0].Id);

                var forged = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.CreateAssetAsync(
                        request with { Scope = "System" },
                        "forged-system"));
                Assert.Equal(
                    SpaceErrorCodes.AssetScopeDenied,
                    forged.Code);
                Assert.Equal(403, forged.StatusCode);

                var keyConflict =
                    await Assert.ThrowsAsync<SpaceProblemException>(
                        () => service.CreateAssetAsync(
                            request with { Name = "Changed name" },
                            "asset-key"));
                Assert.Equal(
                    SpaceErrorCodes.IdempotencyConflict,
                    keyConflict.Code);
                Assert.Equal(409, keyConflict.StatusCode);

                var duplicate =
                    await Assert.ThrowsAsync<SpaceProblemException>(
                        () => service.CreateAssetAsync(
                            request,
                            "different-key"));
                Assert.Equal(
                    SpaceErrorCodes.AssetConflict,
                    duplicate.Code);
                Assert.Equal(409, duplicate.StatusCode);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            SpaceAssetVersion foreignVersion;
            await using (var otherContext = CreateContext(
                             connectionString,
                             otherExecution,
                             clock))
            {
                var otherAsset = SpaceAsset.CreateTenant(
                    otherExecution.TenantId,
                    "OTHER-RACK",
                    "Other Tenant Rack",
                    "Rack",
                    null,
                    otherExecution.ActorId,
                    clock.UtcNow);
                foreignVersion = NewAssetVersion(
                    otherAsset,
                    otherExecution.ActorId,
                    clock.UtcNow);
                otherContext.AddRange(otherAsset, foreignVersion);
                await otherContext.SaveChangesAsync();

                var otherService = NewService(
                    otherContext,
                    otherExecution,
                    clock);
                var visible = await otherService.GetAssetsAsync(
                    null,
                    null,
                    50,
                    null);
                Assert.Equal(2, visible.Items.Count);
                Assert.Contains(
                    visible.Items,
                    asset => asset.Id == systemVersion.AssetId);
                Assert.Contains(
                    visible.Items,
                    asset => asset.Id == otherAsset.Id);
                Assert.DoesNotContain(
                    visible.Items,
                    asset => asset.Id == tenantAssetId);
            }

            await using var tenantVerify = CreateContext(
                connectionString,
                execution,
                clock);
            var forgedTenantAsset = SpaceAsset.CreateTenant(
                otherExecution.TenantId,
                "FORGED-RACK",
                "Forged Tenant Rack",
                "Rack",
                null,
                execution.ActorId,
                clock.UtcNow);
            tenantVerify.Add(forgedTenantAsset);
            await Assert.ThrowsAsync<SpaceTenantScopeException>(
                () => tenantVerify.SaveChangesAsync());
            tenantVerify.ChangeTracker.Clear();

            var seeded = await SeedDesignModelAsync(
                tenantVerify,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Asset reference guard",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1");
            var element = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.StaticEquipment,
                """
                {"schemaVersion":1,"kind":"box","width":1,"height":1,"depth":1}
                """);
            Assert.Throws<SpaceTenantScopeException>(
                () => element.AttachAsset(foreignVersion));

            tenantVerify.AddRange(draft, floor, element);
            await tenantVerify.SaveChangesAsync();

            var unattachedAssetElement = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.StaticEquipment,
                AssetGeometry(Guid.NewGuid()));
            tenantVerify.Add(unattachedAssetElement);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => tenantVerify.SaveChangesAsync());
            tenantVerify.ChangeTracker.Clear();

            await Assert.ThrowsAsync<SqlException>(
                () => tenantVerify.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [Space_ElementRevision]
                    SET [ModelAssetScope] = {(short)SpaceAssetScope.Tenant},
                        [ModelAssetOwnerTenantId] = {otherExecution.TenantId},
                        [ModelAssetId] = {foreignVersion.Id}
                    WHERE [Id] = {element.Id};
                    """));
        });
    }

    [SqlServerFact]
    public async Task Migration_fails_closed_for_unverified_legacy_asset_ids()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceAssetUpgrade_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var context = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await context.Database
                .GetService<IMigrator>()
                .MigrateAsync(
                    "20260731001924_SpaceE05S02RackLevelSpecification");

            var model = SpaceModel.Create(
                execution.TenantId,
                Guid.NewGuid());
            var version = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                model.Id,
                1,
                "Legacy asset reference");
            context.AddRange(model, version);
            await context.SaveChangesAsync();

            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                version.Id,
                Guid.NewGuid(),
                model.SiteId,
                1,
                "F1",
                "Floor 1");
            context.Add(floor);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlInterpolatedAsync(
                $$"""
                INSERT INTO [Space_ElementRevision]
                    ([Id], [ModelVersionId], [LogicalId], [SourceId],
                     [SourceRef], [LifecycleState], [FloorLogicalId],
                     [ParentLogicalId], [ElementType], [GeometryJson],
                     [ModelAssetId], [X], [Y], [Z], [RotationZ], [Width],
                     [Height], [Depth], [BusinessCode], [LinkedEntityType],
                     [LinkedLogicalId], [TenantId], [CreatedAtUtc],
                     [CreatedBy], [ModifiedAtUtc], [ModifiedBy], [IsDeleted])
                VALUES
                    ({{Guid.NewGuid()}}, {{version.Id}}, {{Guid.NewGuid()}}, NULL,
                     NULL, 0, {{floor.LogicalId}}, NULL, N'Column',
                     N'{"schemaVersion":1,"kind":"box","width":1,"height":1,"depth":1}',
                     {{Guid.NewGuid()}}, 0, 0, 0, 0, 1, 1, 1, NULL, NULL, NULL,
                     {{execution.TenantId}}, {{clock.UtcNow}}, {{execution.ActorId}},
                     NULL, NULL, 0);
                """);

            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.Database.MigrateAsync());
            Assert.Equal(51000, error.Number);
            Assert.Contains(
                "legacy ModelAssetId",
                error.Message,
                StringComparison.Ordinal);

            var applied = await context.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT [MigrationId] AS [Value]
                    FROM [__EFMigrationsHistory_Space]
                    WHERE [MigrationId] =
                        '20260731010047_SpaceE05S04AssetLibrary'
                    """)
                .ToListAsync();
            Assert.Empty(applied);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceDesignV1Service NewService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock)
    {
        var cloneStore = new EfSpaceVersionCloneStore(
            context,
            execution,
            clock);
        return new SpaceDesignV1Service(
            context,
            execution,
            clock,
            new TestCursorCodec(),
            new TestAccessEvaluator(),
            new SpaceVersionCloneCoordinator(execution, cloneStore),
            new SpaceSourceCoordinator(execution));
    }

    private static async Task<(SpaceModel Model, SpaceModelVersion Published)>
        SeedDesignModelAsync(
            SpaceContext context,
            Guid actorId,
            DateTime nowUtc)
    {
        var model = SpaceModel.Create(
            context.CurrentTenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            context.CurrentTenantId,
            model.Id,
            1,
            "Published baseline");
        context.AddRange(model, published);
        await context.SaveChangesAsync();

        published.BeginValidation();
        published.MarkReady(ContentHash, "space-v1", WmsHash);
        published.BeginPublishing();
        published.MarkPublished(actorId, nowUtc);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();
        return (model, published);
    }

    private static CreateSpaceAssetRequest NewAssetRequest(string assetCode) =>
        new(
            assetCode,
            "Tenant Rack",
            "Rack",
            "Glb",
            """{"type":"object","additionalProperties":false}""",
            new string('c', 64),
            "Tenant private",
            "assets/preview.png",
            "assets/model.glb");

    private static SpaceAssetVersion NewAssetVersion(
        SpaceAsset asset,
        Guid actorId,
        DateTime nowUtc) =>
        SpaceAssetVersion.CreateReady(
            asset,
            1,
            SpaceAssetFormat.Glb,
            """{"type":"object","additionalProperties":false}""",
            "assets/preview.png",
            "assets/model.glb",
            new string('d', 64),
            actorId,
            nowUtc);

    private static string AssetGeometry(Guid assetVersionId) =>
        """
        {"schemaVersion":1,"kind":"asset","assetVersionId":"ASSET_ID","transform":{}}
        """
            .Replace(
                "ASSET_ID",
                assetVersionId.ToString(),
                StringComparison.Ordinal);

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceAssets_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await setup.Database.MigrateAsync();
            await action(connectionString, execution, clock);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }

    private sealed class TestAccessEvaluator :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        private readonly Dictionary<string, SpaceCursorState> _states = [];

        public string Encode(SpaceCursorState state)
        {
            var token = Guid.NewGuid().ToString("N");
            _states[token] = state;
            return token;
        }

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            if (!_states.TryGetValue(cursor, out var state) ||
                state.Resource != expectedResource ||
                state.FilterHash != expectedFilterHash)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.CursorScopeMismatch,
                    400,
                    "Cursor scope mismatch.");
            }
            return state;
        }
    }
}
