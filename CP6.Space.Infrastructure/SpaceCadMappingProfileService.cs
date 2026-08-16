using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadMappingProfileService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock) : ISpaceCadMappingProfileService
{
    private const string Operation = "space.cad-mapping-profile.save";
    private const int MaximumDefinitionBytes = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SpaceCadMappingProfileV1>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var stored = await LoadCurrentAsync(cancellationToken);
        return
        [
            StandardSpaceCadMappingProfileCatalog.SystemProfile,
            .. stored.Select(item => item.Profile),
        ];
    }

    public async Task<SpaceCadMappingProfileV1?> FindAsync(
        Guid profileId,
        int version,
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        if (profileId == system.ProfileId)
        {
            return version == system.Version
                ? system
                : null;
        }

        if (profileId == Guid.Empty || version <= 0)
            return null;

        var item = await context.CadMappingProfileVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProfileId == profileId &&
                    candidate.Version == version,
                cancellationToken);
        return item is null ? null : DeserializeStored(item);
    }

    public async Task<IReadOnlyList<SpaceCadMappingProfileDto>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var stored = await LoadCurrentAsync(cancellationToken);
        return
        [
            SystemDto(),
            .. stored.Select(item => ToDto(
                item.Container,
                item.Version,
                item.Profile)),
        ];
    }

    public async Task<SpaceCadMappingProfileDto> GetProfileAsync(
        Guid profileId,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        if (profileId == system.ProfileId)
        {
            if (version is not null &&
                version != system.Version)
            {
                throw NotFound();
            }

            return SystemDto();
        }

        if (profileId == Guid.Empty || version <= 0)
            throw NotFound();

        var container = await context.CadMappingProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == profileId,
                cancellationToken) ?? throw NotFound();
        var selectedVersion = version ?? container.CurrentVersion;
        var item = await context.CadMappingProfileVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProfileId == profileId &&
                    candidate.Version == selectedVersion,
                cancellationToken) ?? throw NotFound();
        return ToDto(container, item, DeserializeStored(item));
    }

    public async Task<SaveSpaceCadMappingProfileResponse> SaveProfileAsync(
        SaveSpaceCadMappingProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireTenant();
        var actorId = RequireActor();
        if (request.ProfileId ==
            StandardSpaceCadMappingProfileCatalog.SystemProfile.ProfileId)
            throw ReadOnly();

        var name = RequireName(request.Name);
        var rules = NormalizeRules(request.Rules, tenantId, name, request.IsEnabled);
        var normalizedRequest = request with
        {
            Name = name,
            Rules = rules,
        };
        var requestHash = Hash(JsonSerializer.Serialize(
            normalizedRequest,
            JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            SpaceCadMappingProfile container;
            SpaceCadMappingProfileV1 sealedProfile;
            var created = !request.ProfileId.HasValue;
            if (created)
            {
                await ValidateCopySourceAsync(
                    request.CopyFromProfileId,
                    request.CopyFromVersion,
                    cancellationToken);
                container = SpaceCadMappingProfile.Create(tenantId, name);
                sealedProfile = SealTenantProfile(
                    tenantId,
                    container.Id,
                    1,
                    name,
                    request.IsEnabled,
                    request.CopyFromProfileId,
                    request.CopyFromVersion,
                    rules);
                context.CadMappingProfiles.Add(container);
            }
            else
            {
                if (request.CopyFromProfileId.HasValue || request.CopyFromVersion.HasValue)
                {
                    throw Invalid(
                        "An existing CAD mapping profile version keeps its original copy lineage.");
                }

                container = await context.CadMappingProfiles
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == request.ProfileId,
                        cancellationToken) ?? throw NotFound();
                ApplyExpectedRowVersion(container, request.ExpectedRowVersion);
                var currentVersion = await context.CadMappingProfileVersions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.ProfileId == container.Id &&
                            candidate.Version == container.CurrentVersion,
                        cancellationToken) ?? throw NotFound();
                var current = DeserializeStored(currentVersion);
                sealedProfile = SpaceCadMapping.CreateNextTenantVersion(
                    current,
                    tenantId,
                    rules,
                    name,
                    request.IsEnabled);
            }

            container.Advance(name, sealedProfile.Version);
            var definitionJson = SpaceCadMapping.SerializeProfile(sealedProfile);
            if (Encoding.UTF8.GetByteCount(definitionJson) > MaximumDefinitionBytes)
                throw Invalid("The sealed CAD mapping profile is too large.");
            var versionEntity = SpaceCadMappingProfileVersion.Create(
                tenantId,
                container.Id,
                sealedProfile.Version,
                definitionJson,
                sealedProfile.DefinitionSha256,
                sealedProfile.BasedOnProfileId,
                sealedProfile.BasedOnVersion);
            context.CadMappingProfileVersions.Add(versionEntity);
            await context.SaveChangesAsync(cancellationToken);

            var response = new SaveSpaceCadMappingProfileResponse(
                ToDto(container, versionEntity, sealedProfile),
                created,
                IdempotentReplay: false);
            var now = RequireUtcNow();
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                tenantId,
                actorId,
                Operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                200,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException exception)
            when (exception.GetBaseException() is SqlException
                  {
                      Number: 2601 or 2627,
                  })
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict();
        }
    }

    private async Task<StoredProfile[]> LoadCurrentAsync(
        CancellationToken cancellationToken)
    {
        var containers = await context.CadMappingProfiles
            .AsNoTracking()
            .OrderBy(item => item.NormalizedName)
            .ToArrayAsync(cancellationToken);
        var profileIds = containers.Select(item => item.Id).ToArray();
        if (profileIds.Length == 0)
            return [];
        var versions = await context.CadMappingProfileVersions
            .AsNoTracking()
            .Where(item => profileIds.Contains(item.ProfileId))
            .ToArrayAsync(cancellationToken);
        var byKey = versions.ToDictionary(item => (item.ProfileId, item.Version));
        return containers.Select(container =>
        {
            if (!byKey.TryGetValue(
                    (container.Id, container.CurrentVersion),
                    out var version))
            {
                throw new InvalidOperationException(
                    "The current CAD mapping profile version is missing.");
            }

            return new StoredProfile(
                container,
                version,
                DeserializeStored(version));
        }).ToArray();
    }

    private async Task ValidateCopySourceAsync(
        Guid? profileId,
        int? version,
        CancellationToken cancellationToken)
    {
        if (profileId.HasValue != version.HasValue ||
            version.HasValue && version.Value <= 0)
        {
            throw Invalid(
                "Copy source profile identity and version must be supplied together.");
        }

        if (!profileId.HasValue)
            return;
        if (await FindAsync(profileId.Value, version!.Value, cancellationToken) is null)
            throw NotFound();
    }

    private async Task<SaveSpaceCadMappingProfileResponse?> ReadReplayAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var actorId = RequireActor();
        var record = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == actorId &&
                    item.Operation == Operation &&
                    item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }

        return (JsonSerializer.Deserialize<SaveSpaceCadMappingProfileResponse>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The CAD mapping idempotency response is invalid."))
            with
            {
                IdempotentReplay = true,
            };
    }

    private static IReadOnlyList<SpaceCadMappingRuleV1> NormalizeRules(
        IReadOnlyList<SpaceCadMappingRuleV1>? rules,
        Guid tenantId,
        string name,
        bool isEnabled)
    {
        try
        {
            return SealTenantProfile(
                tenantId,
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                1,
                name,
                isEnabled,
                null,
                null,
                rules ?? []).Rules;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or OverflowException)
        {
            throw Invalid(exception.Message);
        }
    }

    private static SpaceCadMappingProfileV1 SealTenantProfile(
        Guid tenantId,
        Guid profileId,
        int version,
        string name,
        bool isEnabled,
        Guid? basedOnProfileId,
        int? basedOnVersion,
        IReadOnlyList<SpaceCadMappingRuleV1> rules)
    {
        try
        {
            return SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
                SpaceCadMappingVersions.SchemaVersion,
                profileId,
                version,
                name,
                SpaceCadMappingScope.Tenant,
                tenantId,
                isEnabled,
                basedOnProfileId,
                basedOnVersion,
                rules));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or OverflowException)
        {
            throw Invalid(exception.Message);
        }
    }

    private static SpaceCadMappingProfileV1 DeserializeStored(
        SpaceCadMappingProfileVersion version)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<SpaceCadMappingProfileV1>(
                version.DefinitionJson,
                JsonOptions) ?? throw new InvalidDataException(
                "Stored CAD mapping profile JSON is empty.");
            SpaceCadMapping.Validate(profile);
            if (profile.Scope != SpaceCadMappingScope.Tenant ||
                profile.TenantId != version.TenantId ||
                profile.ProfileId != version.ProfileId ||
                profile.Version != version.Version ||
                !string.Equals(
                    profile.DefinitionSha256,
                    version.DefinitionHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Stored CAD mapping profile identity does not match its row.");
            }

            return profile;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or InvalidDataException)
        {
            throw new InvalidOperationException(
                "Stored CAD mapping profile evidence is invalid.",
                exception);
        }
    }

    private static SpaceCadMappingProfileDto SystemDto()
    {
        var profile = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        return new SpaceCadMappingProfileDto(
            profile.ProfileId,
            profile.Name,
            profile.Scope,
            profile.Version,
            IsReadOnly: true,
            profile.IsEnabled,
            profile.DefinitionSha256,
            profile.Rules,
            profile.BasedOnProfileId,
            profile.BasedOnVersion,
            RowVersion: null,
            CreatedAtUtc: null,
            CreatedBy: null);
    }

    private static SpaceCadMappingProfileDto ToDto(
        SpaceCadMappingProfile container,
        SpaceCadMappingProfileVersion version,
        SpaceCadMappingProfileV1 profile) =>
        new(
            profile.ProfileId,
            profile.Name,
            profile.Scope,
            profile.Version,
            IsReadOnly: false,
            profile.IsEnabled,
            profile.DefinitionSha256,
            profile.Rules,
            profile.BasedOnProfileId,
            profile.BasedOnVersion,
            Convert.ToBase64String(container.RowVersion),
            version.CreatedAtUtc,
            version.CreatedBy);

    private void ApplyExpectedRowVersion(
        SpaceCadMappingProfile profile,
        string? expected)
    {
        if (expected is null)
            throw Conflict("ExpectedRowVersion is required when adding a version.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(expected);
        }
        catch (FormatException)
        {
            throw Conflict("ExpectedRowVersion is invalid.");
        }

        if (!profile.RowVersion.SequenceEqual(bytes))
            throw Conflict();
        context.Entry(profile).Property(item => item.RowVersion).OriginalValue = bytes;
    }

    private string IdempotencyKeyHash(string idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }

        return Hash($"{execution.TenantId:D}\n{Operation}\n{normalized}");
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return null;
        return await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    private Guid RequireTenant()
    {
        if (execution.TenantId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        }

        return execution.TenantId;
    }

    private Guid RequireActor()
    {
        if (execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space actor is required.");
        }

        return execution.ActorId;
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string RequireName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200 || name.Any(char.IsControl))
            throw Invalid("CAD mapping profile name is invalid.");
        return name;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.CadMappingProfileNotFound,
            404,
            "The CAD mapping profile was not found.",
            recoveryAction: "select-current-cad-mapping-profile");

    private static SpaceProblemException ReadOnly() =>
        new(
            SpaceErrorCodes.CadMappingProfileReadOnly,
            409,
            "The system CAD mapping profile is read-only.",
            recoveryAction: "copy-system-cad-mapping-profile");

    private static SpaceProblemException Conflict(string? detail = null) =>
        new(
            SpaceErrorCodes.CadMappingProfileConflict,
            409,
            "The CAD mapping profile conflicts with current data.",
            detail,
            "reload-cad-mapping-profile");

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.CadMappingProfileInvalid,
            422,
            "The CAD mapping profile is invalid.",
            detail,
            "correct-cad-mapping-profile");

    private sealed record StoredProfile(
        SpaceCadMappingProfile Container,
        SpaceCadMappingProfileVersion Version,
        SpaceCadMappingProfileV1 Profile);
}
