using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceAssetTests
{
    private static readonly Guid TenantA =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid TenantB =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid ActorId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly DateTime NowUtc =
        new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string Hash = new('a', 64);

    [Fact]
    public void System_asset_version_is_visible_to_every_tenant()
    {
        var version = NewVersion(
            SpaceAsset.CreateSystem(
                "SYS-RACK",
                "System Rack",
                "Rack",
                null,
                ActorId,
                NowUtc));

        Assert.Equal(SpaceAssetScope.System, version.Scope);
        Assert.Equal(Guid.Empty, version.OwnerTenantId);
        Assert.True(version.IsVisibleTo(TenantA));
        Assert.True(version.IsVisibleTo(TenantB));
    }

    [Fact]
    public void Tenant_asset_version_is_visible_only_to_its_owner()
    {
        var version = NewVersion(NewTenantAsset(TenantA));

        Assert.True(version.IsVisibleTo(TenantA));
        Assert.False(version.IsVisibleTo(TenantB));
    }

    [Fact]
    public void Asset_version_rejects_unsafe_content_references()
    {
        var asset = NewTenantAsset(TenantA);

        Assert.Throws<ArgumentException>(
            () => SpaceAssetVersion.CreateReady(
                asset,
                1,
                SpaceAssetFormat.Glb,
                "{}",
                "https://example.test/preview.png",
                "assets/model.glb",
                Hash,
                ActorId,
                NowUtc));
        Assert.Throws<ArgumentException>(
            () => SpaceAssetVersion.CreateReady(
                asset,
                1,
                SpaceAssetFormat.Glb,
                "[]",
                "assets/preview.png",
                "assets/model.glb",
                Hash,
                ActorId,
                NowUtc));
    }

    [Fact]
    public void Element_accepts_own_tenant_and_system_asset_versions()
    {
        var ownVersion = NewVersion(NewTenantAsset(TenantA));
        var systemVersion = NewVersion(
            SpaceAsset.CreateSystem(
                "SYS-RACK",
                "System Rack",
                "Rack",
                null,
                ActorId,
                NowUtc));
        var element = NewElement(TenantA);

        element.AttachAsset(ownVersion);
        Assert.Equal(ownVersion.Id, element.ModelAssetId);
        Assert.Equal(SpaceAssetScope.Tenant, element.ModelAssetScope);
        Assert.Equal(TenantA, element.ModelAssetOwnerTenantId);

        element.AttachAsset(systemVersion);
        Assert.Equal(systemVersion.Id, element.ModelAssetId);
        Assert.Equal(SpaceAssetScope.System, element.ModelAssetScope);
        Assert.Equal(Guid.Empty, element.ModelAssetOwnerTenantId);
    }

    [Fact]
    public void Element_rejects_cross_tenant_asset_version()
    {
        var foreignVersion = NewVersion(NewTenantAsset(TenantB));
        var element = NewElement(TenantA);

        Assert.Throws<SpaceTenantScopeException>(
            () => element.AttachAsset(foreignVersion));
    }

    [Fact]
    public void Asset_geometry_must_match_the_attached_concrete_version()
    {
        var expectedVersion = NewVersion(NewTenantAsset(TenantA));
        var otherVersion = NewVersion(
            SpaceAsset.CreateTenant(
                TenantA,
                "TENANT-CONVEYOR",
                "Conveyor",
                "Equipment",
                null,
                ActorId,
                NowUtc));
        var element = NewElement(
            TenantA,
            AssetGeometry(expectedVersion.Id));

        Assert.Throws<InvalidOperationException>(
            element.EnsureAssetReferenceConsistency);
        Assert.Throws<InvalidOperationException>(
            () => element.AttachAsset(otherVersion));
        element.AttachAsset(expectedVersion);
        element.EnsureAssetReferenceConsistency();
        Assert.Throws<InvalidOperationException>(
            () => element.Retype(SpaceElementTypes.Wall));
        Assert.Throws<InvalidOperationException>(
            () => element.UpdateGeometry(
                AssetGeometry(otherVersion.Id)));
        Assert.Throws<InvalidOperationException>(element.DetachAsset);
    }

    [Fact]
    public void Placement_no_longer_accepts_an_unverified_asset_identity()
    {
        var parameters = typeof(SpaceElementRevision)
            .GetMethod(nameof(SpaceElementRevision.ConfigurePlacement))!
            .GetParameters();

        Assert.Equal(7, parameters.Length);
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType == typeof(Guid?));
        Assert.Null(
            typeof(SpaceElementRevision).GetMethod("SetModelAsset"));
    }

    [Fact]
    public void Asset_heads_and_versions_expose_no_tenant_callable_mutators()
    {
        var declaredPublicInstanceMethods =
            typeof(SpaceAsset)
                .GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly)
                .Concat(
                    typeof(SpaceAssetVersion).GetMethods(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly));

        Assert.DoesNotContain(
            declaredPublicInstanceMethods,
            method => method.ReturnType == typeof(void));
    }

    private static SpaceAsset NewTenantAsset(Guid tenantId) =>
        SpaceAsset.CreateTenant(
            tenantId,
            "TENANT-RACK",
            "Tenant Rack",
            "Rack",
            "Tenant owned",
            ActorId,
            NowUtc);

    private static SpaceAssetVersion NewVersion(SpaceAsset asset) =>
        SpaceAssetVersion.CreateReady(
            asset,
            1,
            SpaceAssetFormat.Glb,
            """{"type":"object","additionalProperties":false}""",
            "assets/preview.png",
            "assets/model.glb",
            Hash,
            ActorId,
            NowUtc);

    private static SpaceElementRevision NewElement(
        Guid tenantId,
        string geometryJson =
            """
            {"schemaVersion":1,"kind":"box","width":800,"height":2200,"depth":400}
            """) =>
        SpaceElementRevision.Create(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SpaceElementTypes.StaticEquipment,
            geometryJson);

    private static string AssetGeometry(Guid assetVersionId) =>
        """
        {"schemaVersion":1,"kind":"asset","assetVersionId":"ASSET_ID","transform":{}}
        """
            .Replace(
                "ASSET_ID",
                assetVersionId.ToString(),
                StringComparison.Ordinal);
}
