using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceRackGenerationProfileService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceCursorCodec cursorCodec) : ISpaceRackGenerationProfileService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const string CreateOperation =
        "space.rack-generation-profile.create";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpacePage<SpaceRackGenerationProfileDto>>
        GetProfilesAsync(
            string? scope,
            int limit,
            string? cursor,
            CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        limit = NormalizeLimit(limit);
        var parsedScope = ParseOptionalScope(scope);
        var filterHash = Hash(
            $"scope={scope?.Trim().ToLowerInvariant() ?? string.Empty}\nlimit={limit}");
        var offset = ReadOffset(cursor, filterHash);
        var query = context.RackGenerationProfiles.AsNoTracking()
            .Where(profile =>
                profile.Status == SpaceRackGenerationProfileStatus.Active);
        if (parsedScope.HasValue)
            query = query.Where(profile => profile.Scope == parsedScope.Value);

        var profiles = await query
            .OrderBy(profile => profile.Scope)
            .ThenBy(profile => profile.ProfileCode)
            .ThenBy(profile => profile.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var pageProfiles = profiles.Take(limit).ToArray();
        var profileIds = pageProfiles.Select(profile => profile.Id).ToArray();
        var versions = profileIds.Length == 0
            ? []
            : await context.RackGenerationProfileVersions.AsNoTracking()
                .Where(version =>
                    profileIds.Contains(version.ProfileId) &&
                    version.Status ==
                        SpaceRackGenerationProfileVersionStatus.Ready)
                .OrderByDescending(version => version.VersionNo)
                .ThenBy(version => version.Id)
                .ToArrayAsync(cancellationToken);
        var latestByProfile = versions
            .GroupBy(version => version.ProfileId)
            .ToDictionary(group => group.Key, group => group.First());
        var items = pageProfiles.Select(profile => ToDto(
            profile,
            latestByProfile.TryGetValue(profile.Id, out var latest)
                ? latest
                : throw new InvalidOperationException(
                    "A rack generation profile is missing its ready version.")))
            .ToArray();
        var nextCursor = profiles.Count > limit
            ? cursorCodec.Encode(new SpaceCursorState(
                "rack-generation-profiles",
                filterHash,
                offset + limit))
            : null;
        return new SpacePage<SpaceRackGenerationProfileDto>(items, nextCursor);
    }

    public async Task<SpaceRackGenerationProfileVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty)
            throw Invalid("versionId", "A non-empty version ID is required.");
        var version = await context.RackGenerationProfileVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == versionId,
                cancellationToken) ?? throw new SpaceProblemException(
                SpaceErrorCodes.RackGenerationProfileNotFound,
                404,
                "The rack generation profile version was not found.",
                recoveryAction: "select-rack-generation-profile");
        return ToDto(version);
    }

    public async Task<CreateSpaceRackGenerationProfileResponse> CreateAsync(
        CreateSpaceRackGenerationProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Levels);
        EnsureExecutionContext();
        if (request.Levels.Count is < 1 or > 1_000 ||
            request.Levels.Any(level => level is null))
        {
            throw Invalid(
                "levels",
                "Between 1 and 1000 non-null levels are required.");
        }
        if (!string.Equals(
                request.Scope?.Trim(),
                SpaceRackGenerationProfileScope.Tenant.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.RackGenerationProfileScopeDenied,
                403,
                "System rack generation profiles cannot be created through the tenant API.",
                recoveryAction: "use-tenant-scope");
        }

        SpaceRackGenerationProfile profile;
        SpaceRackGenerationProfileVersion version;
        var now = RequireUtcNow();
        try
        {
            profile = SpaceRackGenerationProfile.CreateTenant(
                execution.TenantId,
                request.ProfileCode,
                request.Name,
                request.Description,
                execution.ActorId,
                now);
            version = SpaceRackGenerationProfileVersion.CreateReady(
                profile,
                1,
                request.RackWidthMillimeters,
                request.RackDepthMillimeters,
                request.RackHeightMillimeters,
                request.Levels.Select(ToDomain).ToArray(),
                execution.ActorId,
                now);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("profile", exception.Message);
        }

        var normalizedRequest = new CreateSpaceRackGenerationProfileRequest(
            profile.ProfileCode,
            profile.Name,
            version.RackWidthMillimeters,
            version.RackDepthMillimeters,
            version.RackHeightMillimeters,
            version.ReadLevels().Select(ToDto).ToArray(),
            profile.Description,
            SpaceRackGenerationProfileScope.Tenant.ToString());
        var requestHash = Hash(
            JsonSerializer.Serialize(normalizedRequest, JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
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

            context.RackGenerationProfiles.Add(profile);
            context.RackGenerationProfileVersions.Add(version);
            await context.SaveChangesAsync(cancellationToken);
            var response = new CreateSpaceRackGenerationProfileResponse(
                ToDto(profile, version),
                IdempotentReplay: false);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                CreateOperation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                201,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw new SpaceProblemException(
                SpaceErrorCodes.RackGenerationProfileConflict,
                409,
                "A rack generation profile with this code already exists.",
                recoveryAction: "choose-another-profile-code");
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<CreateSpaceRackGenerationProfileResponse?>
        ReadReplayAsync(
            string keyHash,
            string requestHash,
            CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == execution.ActorId &&
                        item.Operation == CreateOperation &&
                        item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key cannot be replayed with this request.",
                recoveryAction: "use-new-idempotency-key");
        }
        var response = JsonSerializer.Deserialize<
            CreateSpaceRackGenerationProfileResponse>(
                record.ResponseJson,
                JsonOptions) ?? throw new InvalidOperationException(
                "The rack generation profile replay is invalid.");
        return response with { IdempotentReplay = true };
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        try
        {
            var value = cursorCodec.Decode(
                cursor,
                "rack-generation-profiles",
                filterHash);
            if (value.Offset < 0)
                throw new InvalidOperationException();
            return value.Offset;
        }
        catch (SpaceProblemException)
        {
            throw;
        }
        catch
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CursorInvalid,
                400,
                "The rack generation profile cursor is invalid.",
                recoveryAction: "restart-list-query");
        }
    }

    private static SpaceRackGenerationProfileDto ToDto(
        SpaceRackGenerationProfile profile,
        SpaceRackGenerationProfileVersion latestVersion) =>
        new(
            profile.Id,
            profile.Scope.ToString(),
            profile.ProfileCode,
            profile.Name,
            profile.Description,
            profile.Status.ToString(),
            ToDto(latestVersion),
            RowVersion(profile.RowVersion));

    private static SpaceRackGenerationProfileVersionDto ToDto(
        SpaceRackGenerationProfileVersion version) =>
        new(
            version.Id,
            version.ProfileId,
            version.Scope.ToString(),
            version.VersionNo,
            version.RackWidthMillimeters,
            version.RackDepthMillimeters,
            version.RackHeightMillimeters,
            version.ReadLevels().Select(ToDto).ToArray(),
            version.LocationCount,
            version.ContentHash,
            version.Status.ToString(),
            RowVersion(version.RowVersion));

    private static SpaceRackGenerationProfileLevel ToDomain(
        SpaceRackGenerationProfileLevelDto value) =>
        new(
            value.LevelNo,
            value.BottomZMillimeters,
            value.ClearHeightMillimeters,
            value.BinCount,
            value.DepthCount,
            value.CellWidthMillimeters,
            value.CellDepthMillimeters,
            value.BeamHeightMillimeters,
            value.MaxLoadKilograms);

    private static SpaceRackGenerationProfileLevelDto ToDto(
        SpaceRackGenerationProfileLevel value) =>
        new(
            value.LevelNo,
            value.BottomZMillimeters,
            value.ClearHeightMillimeters,
            value.BinCount,
            value.DepthCount,
            value.CellWidthMillimeters,
            value.CellDepthMillimeters,
            value.BeamHeightMillimeters,
            value.MaxLoadKilograms);

    private void EnsureExecutionContext()
    {
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return DefaultPageSize;
        if (limit is < 1 or > MaxPageSize)
            throw Invalid("limit", $"limit must be between 1 and {MaxPageSize}.");
        return limit;
    }

    private static SpaceRackGenerationProfileScope? ParseOptionalScope(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<SpaceRackGenerationProfileScope>(
                value.Trim(),
                ignoreCase: true,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw Invalid("scope", $"'{value}' is not a supported scope.");
        }
        return parsed;
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
        return Hash(
            $"{execution.TenantId:D}\n{CreateOperation}\n{normalized}");
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string RowVersion(byte[] value) =>
        Convert.ToBase64String(value ?? []);

    private static SpaceProblemException Invalid(
        string field,
        string detail) =>
        new(
            SpaceErrorCodes.RackGenerationProfileInvalid,
            400,
            "The rack generation profile request is invalid.",
            $"{field}: {detail}",
            "correct-rack-generation-profile");
}
