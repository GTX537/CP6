using System.Data;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadPreparationService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceCadPreparationProvider provider,
    ISpaceCadMappingProfileCatalog profiles,
    ISpaceFileStore files,
    SpaceWorkerSandboxPolicy sandbox,
    ISpaceClock clock) : ISpaceCadPreparationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    public async Task<SpaceCadPreparationStatusDto> GetStatusAsync(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        _ = await LoadVersionAsync(versionId, cancellationToken);
        var source = await context.Sources.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == sourceId && item.ModelVersionId == versionId,
            cancellationToken) ?? throw NotFound();
        if (source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            source.FileId is null)
            throw Invalid("The selected source is not a DWG or DXF upload.");

        var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == source.FileId,
            cancellationToken) ?? throw NotFound();
        var effectiveSourceState = source.State == SpaceSourceState.Scanning
            ? file.State switch
            {
                SpaceFileState.Clean => SpaceSourceState.Ready,
                SpaceFileState.Rejected => SpaceSourceState.Rejected,
                _ => SpaceSourceState.Scanning,
            }
            : source.State;
        var ready = (effectiveSourceState is SpaceSourceState.Ready or
                        SpaceSourceState.PreviewReady) &&
                    file.State == SpaceFileState.Clean &&
                    !file.IsDeleted &&
                    file.Sha256?.Equals(source.Sha256, StringComparison.Ordinal) == true;
        return new SpaceCadPreparationStatusDto(
            source.Id,
            effectiveSourceState.ToString(),
            file.State.ToString(),
            ready,
            effectiveSourceState == SpaceSourceState.Rejected ||
            file.State == SpaceFileState.Rejected || file.IsDeleted
                ? SpaceErrorCodes.SourceUnsafe
                : null);
    }

    public async Task<IReadOnlyList<SpaceCadMappingProfileSummaryDto>>
        ListProfilesAsync(
            Guid versionId,
            CancellationToken cancellationToken = default)
    {
        _ = await LoadVersionAsync(versionId, cancellationToken);
        return (await profiles.ListAsync(cancellationToken))
            .Where(item => item.IsEnabled &&
                (item.Scope == SpaceCadMappingScope.System ||
                 item.TenantId == execution.TenantId))
            .OrderBy(item => item.Scope)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<PreviewSpaceCadPreparationResponse> PreviewAsync(
        Guid versionId,
        Guid sourceId,
        PreviewSpaceCadPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceOriginInSourceUnits);
        ArgumentNullException.ThrowIfNull(request.FloorOriginMillimeters);
        ArgumentNullException.ThrowIfNull(request.LayerOverrides);
        if (sourceId == Guid.Empty || request.FloorLogicalId == Guid.Empty ||
            request.MappingProfileId == Guid.Empty ||
            request.MappingProfileVersion <= 0 ||
            request.ConfirmedUnit == SpaceCadUnit.Unknown)
        {
            throw Invalid("Floor, unit and mapping profile confirmation are required.");
        }
        if (request.LayerOverrides.Count > SpaceCadMappingVersions.MaximumOverrides)
            throw Invalid("CAD layer overrides exceed the supported limit.");

        var scope = await LoadScopeAsync(versionId, sourceId, request, cancellationToken);
        var profile = await profiles.FindAsync(
            request.MappingProfileId,
            request.MappingProfileVersion,
            cancellationToken) ?? throw new SpaceProblemException(
                SpaceErrorCodes.CadPreparationInvalid,
                422,
                "The selected CAD mapping profile does not exist.",
                recoveryAction: "select-cad-mapping-profile");
        if (!profile.IsEnabled || profile.Scope == SpaceCadMappingScope.Tenant &&
            profile.TenantId != execution.TenantId)
            throw Invalid("The selected CAD mapping profile is not available to this tenant.");

        sandbox.Validate();
        SpaceCadIrPackageV1 package;
        await using (var content = await files.OpenQuarantinedReadAsync(
                         execution.TenantId,
                         scope.File.Id,
                         scope.File.StorageKey,
                         cancellationToken))
        {
            package = await provider.InspectAsync(
                new SpaceCadPreparationProviderRequest(
                    execution.TenantId,
                    scope.Model.SiteId,
                    scope.File.Id,
                    scope.Source.Id,
                    scope.Source.Sha256,
                    scope.Format,
                    sandbox),
                content,
                cancellationToken);
        }

        var conversion = new SpaceCadConversionRequest(
            execution.TenantId,
            scope.File.Id,
            scope.Source.Id,
            scope.Source.Sha256,
            scope.Format,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        SpaceCadConversionContract.ValidatePackage(conversion, package);
        var analysis = SpaceCadCoordinatePreparation.Analyze(conversion, package);
        var confirmation = new SpaceCadCoordinateConfirmationV1(
            scope.Source.Sha256,
            UnitConfirmed: true,
            request.ConfirmedUnit,
            request.SourceOriginInSourceUnits,
            request.FloorOriginMillimeters,
            request.RotationZDegrees,
            FloorAssignment(scope.Floor));
        var prepared = SpaceCadCoordinatePreparation.Prepare(
            conversion,
            package,
            confirmation);
        var metadataJson = SpaceCadCoordinatePreparation.SerializeMetadata(prepared.Metadata);
        if (!prepared.ReadyForParsing)
        {
            return Incomplete(
                scope.Version,
                analysis,
                prepared.Metadata,
                profile,
                inventory: null,
                mapping: null,
                semantic: null);
        }

        var inventory = SpaceCadInventory.Build(conversion, prepared);
        var mapping = SpaceCadMapping.Preview(
            execution.TenantId,
            inventory,
            profile,
            request.LayerOverrides);
        if (!mapping.ReadyForSemanticParsing)
        {
            return Incomplete(
                scope.Version,
                analysis,
                prepared.Metadata,
                profile,
                inventory,
                mapping,
                semantic: null);
        }

        var semantic = SpaceCadSemanticParser.Parse(
            conversion,
            prepared,
            inventory,
            profile,
            mapping);
        if (!semantic.ReadyForConfirmation)
        {
            return Incomplete(
                scope.Version,
                analysis,
                prepared.Metadata,
                profile,
                inventory,
                mapping,
                semantic);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var currentBaseline = await context.Versions.AsNoTracking()
            .Where(item => item.Id == scope.Version.Id)
            .Select(item => new { item.ContentRevision, item.ContentHash })
            .SingleAsync(cancellationToken);
        if (currentBaseline.ContentRevision != scope.Version.ContentRevision ||
            !string.Equals(
                currentBaseline.ContentHash,
                scope.Version.ContentHash,
                StringComparison.Ordinal))
            throw new SpaceProblemException(
                SpaceErrorCodes.ParseChangesetStale,
                409,
                "The Draft changed while CAD preparation was running.",
                "Run the preparation preview again against the current Draft.",
                "restart-cad-preparation");

        var now = RequireUtcNow();
        var row = SpaceCadParsePreparation.Create(
            execution.TenantId,
            scope.Version.Id,
            scope.Source.Id,
            scope.Source.Sha256,
            scope.Floor.LogicalId,
            prepared.Metadata.ConfirmedUnit.ToString(),
            prepared.Metadata.ConfirmedScaleToMillimeters,
            metadataJson,
            prepared.Metadata.TransformSha256,
            profile.ProfileId,
            profile.Version,
            profile.DefinitionSha256,
            mapping.PreviewSha256,
            semantic.SemanticPreviewSha256,
            package.Document.ConverterId,
            package.Document.ConverterVersion,
            readyForParsing: true,
            scope.Version.ContentRevision,
            scope.Version.ContentHash,
            now.Add(Lifetime));
        context.CadParsePreparations.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var start = new StartSpaceCadParseRequest(
            row.Id,
            row.FloorLogicalId,
            prepared.Metadata.ConfirmedUnit,
            prepared.Metadata.ConfirmedScaleToMillimeters,
            metadataJson,
            prepared.Metadata.TransformSha256,
            profile.ProfileId,
            profile.Version,
            profile.DefinitionSha256,
            mapping.PreviewSha256);
        return new PreviewSpaceCadPreparationResponse(
            row.Id,
            row.ExpiresAtUtc,
            row.BaseContentRevision,
            row.BaseContentHash,
            ReadyForParsing: true,
            analysis,
            prepared.Metadata,
            inventory.Summary,
            ToSummary(profile),
            mapping,
            semantic,
            start);
    }

    private async Task<(SpaceModelVersion Version, SpaceModel Model)>
        LoadVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        EnsureInternal();
        var result = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { version, model })
            .SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        access.EnsureSiteAccess(result.model.SiteId, write: true);
        if (result.version.Status != SpaceVersionStatus.Draft)
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can prepare a CAD parse.",
                recoveryAction: "open-or-create-draft");
        return (result.version, result.model);
    }

    private async Task<Scope> LoadScopeAsync(
        Guid versionId,
        Guid sourceId,
        PreviewSpaceCadPreparationRequest request,
        CancellationToken cancellationToken)
    {
        var (version, model) = await LoadVersionAsync(versionId, cancellationToken);
        var source = await context.Sources.SingleOrDefaultAsync(
            item => item.Id == sourceId && item.ModelVersionId == versionId,
            cancellationToken) ?? throw NotFound();
        var floor = await context.FloorRevisions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ModelVersionId == versionId &&
                    item.LogicalId == request.FloorLogicalId &&
                    item.LifecycleState == SpaceLifecycleState.Active,
            cancellationToken) ?? throw Invalid("The selected Floor is not active in this Draft.");
        if (source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            source.FileId is null)
            throw Invalid("The selected source is not a DWG or DXF upload.");
        var file = await context.Files.SingleOrDefaultAsync(
            item => item.Id == source.FileId,
            cancellationToken) ?? throw NotFound();
        if (source.State == SpaceSourceState.Scanning &&
            file.State is SpaceFileState.Clean or SpaceFileState.Rejected)
        {
            source.CompleteFileScan(file);
            await context.SaveChangesAsync(cancellationToken);
        }
        if (source.State is not (SpaceSourceState.Ready or SpaceSourceState.PreviewReady))
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD source is not ready for preparation.",
                "Wait for a clean safety scan before continuing.",
                "wait-for-source-ready",
                retryable: true);
        if (file.State != SpaceFileState.Clean || file.IsDeleted ||
            !file.Sha256!.Equals(source.Sha256, StringComparison.Ordinal))
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD source file is not clean.",
                recoveryAction: "wait-for-source-ready",
                retryable: true);
        return new Scope(
            version,
            model,
            source,
            file,
            floor,
            source.SourceType == SpaceSourceType.Dwg
                ? SpaceCadSourceFormat.Dwg
                : SpaceCadSourceFormat.Dxf);
    }

    private static SpaceCadFloorAssignmentV1 FloorAssignment(SpaceFloorRevision floor) =>
        new(
            floor.LogicalId,
            floor.FloorCode,
            floor.Level,
            floor.Elevation,
            SpaceCadCoordinateVersions.TargetCoordinateSystem,
            BoundaryBounds(floor.BoundaryJson));

    private static SpaceCadBoundsV1 BoundaryBounds(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var points = root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("points", out var nested)
                ? nested
                : root;
            var values = points.EnumerateArray().Select(point =>
            {
                if (point.ValueKind == JsonValueKind.Array)
                    return (X: point[0].GetDecimal(), Y: point[1].GetDecimal());
                return (
                    X: point.GetProperty("x").GetDecimal(),
                    Y: point.GetProperty("y").GetDecimal());
            }).ToArray();
            if (values.Length < 3)
                throw new JsonException();
            return new SpaceCadBoundsV1(
                values.Min(item => item.X),
                values.Min(item => item.Y),
                values.Max(item => item.X),
                values.Max(item => item.Y));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or
            KeyNotFoundException or IndexOutOfRangeException or FormatException)
        {
            throw Invalid("The selected Floor boundary cannot be used for CAD coordinates.");
        }
    }

    private static PreviewSpaceCadPreparationResponse Incomplete(
        SpaceModelVersion version,
        SpaceCadCoordinateAnalysisV1 analysis,
        SpaceCadCoordinateMetadataV1 metadata,
        SpaceCadMappingProfileV1 profile,
        SpaceCadInventoryV1? inventory,
        SpaceCadMappingPreviewV1? mapping,
        SpaceCadSemanticPreviewV1? semantic) =>
        new(
            PreparationId: null,
            ExpiresAtUtc: null,
            version.ContentRevision,
            version.ContentHash,
            ReadyForParsing: false,
            analysis,
            metadata,
            inventory?.Summary,
            ToSummary(profile),
            mapping,
            semantic,
            StartRequest: null);

    private static SpaceCadMappingProfileSummaryDto ToSummary(
        SpaceCadMappingProfileV1 value) =>
        new(
            value.ProfileId,
            value.Version,
            value.Name,
            value.Scope.ToString(),
            value.IsEnabled,
            value.DefinitionSha256,
            value.Rules.Count);

    private void EnsureInternal()
    {
        if (execution.IsExternal)
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot access CAD preparation.",
                recoveryAction: "use-published-runtime");
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.CadPreparationInvalid,
            422,
            "CAD preparation is invalid.",
            detail,
            "correct-cad-preparation");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.CadPreparationNotFound,
            404,
            "The CAD preparation scope was not found.",
            recoveryAction: "reload-cad-wizard");

    private sealed record Scope(
        SpaceModelVersion Version,
        SpaceModel Model,
        SpaceModelSource Source,
        SpaceFile File,
        SpaceFloorRevision Floor,
        SpaceCadSourceFormat Format);
}
