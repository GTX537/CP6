using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.Infrastructure;

public sealed class UnavailableSpaceCadPreparationProvider :
    ISpaceCadPreparationProvider
{
    public Task<SpaceCadIrPackageV1> InspectAsync(
        SpaceCadPreparationProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default) =>
        throw new SpaceProblemException(
            SpaceErrorCodes.CadPreparationUnavailable,
            409,
            "CAD preparation is not available for this Site.",
            "Configure an approved isolated CAD Provider before starting the wizard.",
            "configure-site-cad-provider");
}

public sealed class StandardSpaceCadMappingProfileCatalog :
    ISpaceCadMappingProfileCatalog
{
    private static readonly SpaceCadMappingProfileV1 Standard =
        SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.Parse("22a84af8-8712-4d44-a8c8-e017db9bfec4"),
            1,
            "CP6 standard warehouse",
            SpaceCadMappingScope.System,
            TenantId: null,
            IsEnabled: true,
            BasedOnProfileId: null,
            BasedOnVersion: null,
            Rules:
            [
                Rule("rack-block", 120, SpaceCadMappingSourceKind.Block,
                    SpaceCadMappingMatchKind.Glob, "*RACK*",
                    SpaceCadSemanticTarget.Rack, SpaceCadGeometryRule.BlockFootprint, .95m),
                Rule("wall-layer", 110, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*WALL*",
                    SpaceCadSemanticTarget.Wall, SpaceCadGeometryRule.Centerline, .95m,
                    height: 3_000m, thickness: 200m),
                Rule("column-layer", 110, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*COLUMN*",
                    SpaceCadSemanticTarget.Column, SpaceCadGeometryRule.DirectGeometry, .95m),
                Rule("door-layer", 110, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*DOOR*",
                    SpaceCadSemanticTarget.Door, SpaceCadGeometryRule.DirectGeometry, .95m),
                Rule("dock-layer", 110, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*DOCK*",
                    SpaceCadSemanticTarget.Dock, SpaceCadGeometryRule.ClosedBoundary, .95m),
                Rule("zone-layer", 100, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*ZONE*",
                    SpaceCadSemanticTarget.Zone, SpaceCadGeometryRule.ClosedBoundary, .90m),
                Rule("aisle-layer", 100, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*AISLE*",
                    SpaceCadSemanticTarget.Aisle, SpaceCadGeometryRule.DirectGeometry, .90m),
                Rule("rack-layer", 100, SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Glob, "*RACK*",
                    SpaceCadSemanticTarget.Rack, SpaceCadGeometryRule.DirectGeometry, .90m),
            ]));

    public static SpaceCadMappingProfileV1 SystemProfile =>
        Standard with { Rules = Standard.Rules.ToArray() };

    public Task<IReadOnlyList<SpaceCadMappingProfileV1>> ListAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SpaceCadMappingProfileV1>>([SystemProfile]);

    public Task<SpaceCadMappingProfileV1?> FindAsync(
        Guid profileId,
        int version,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            profileId == Standard.ProfileId && version == Standard.Version
                ? SystemProfile
                : null);

    private static SpaceCadMappingRuleV1 Rule(
        string id,
        int priority,
        SpaceCadMappingSourceKind sourceKind,
        SpaceCadMappingMatchKind matchKind,
        string pattern,
        SpaceCadSemanticTarget target,
        SpaceCadGeometryRule geometry,
        decimal confidence,
        decimal? height = null,
        decimal? thickness = null) =>
        new(
            id,
            priority,
            sourceKind,
            matchKind,
            pattern,
            AttributeName: null,
            AttributeMatchKind: null,
            AttributePattern: null,
            target,
            TargetSubtype: null,
            geometry,
            height,
            thickness,
            confidence,
            IsRequired: false);
}
