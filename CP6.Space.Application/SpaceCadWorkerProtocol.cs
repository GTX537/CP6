using System.Security.Cryptography;
using System.Text.Json;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpaceCadWorkerProtocolVersions
{
    public const int SchemaVersion = 2;
    public const long MaximumSourceBytes = 200L * 1024L * 1024L;
    public const long MaximumResponseBytes = 200L * 1024L * 1024L;
}

/// <summary>
/// CAD-only request sent to an isolated conversion Worker. Tenant, Site,
/// model, user, database, mapping, and object-storage identities are
/// deliberately excluded from this boundary.
/// </summary>
public sealed record SpaceCadWorkerConversionRequestV2(
    int SchemaVersion,
    Guid AttemptId,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    string ProviderKey,
    string ProviderVersion,
    string WorkerReleaseSha256);

public sealed record SpaceCadWorkerConversionResponseV2(
    int SchemaVersion,
    Guid AttemptId,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    string ProviderKey,
    string ProviderVersion,
    string WorkerReleaseSha256,
    string PackageSha256,
    SpaceCadIrPackageV1 Package);

public static class SpaceCadWorkerProtocol
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void ValidateRequest(SpaceCadWorkerConversionRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != SpaceCadWorkerProtocolVersions.SchemaVersion ||
            request.AttemptId == Guid.Empty ||
            !IsSha256(request.SourceSha256) ||
            !IsSha256(request.WorkerReleaseSha256) ||
            !Enum.IsDefined(request.SourceFormat))
        {
            throw new InvalidDataException(
                "The isolated CAD Worker request identity is invalid.");
        }
        _ = SpaceCadProviderKey.Normalize(request.ProviderKey);
        _ = SpaceCadProviderVersion.Normalize(request.ProviderVersion);
    }

    public static void ValidateResponse(
        SpaceCadWorkerConversionRequestV2 request,
        SpaceCadWorkerConversionResponseV2 response)
    {
        ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(response.Package);
        ArgumentNullException.ThrowIfNull(response.Package.Document);
        if (response.SchemaVersion != SpaceCadWorkerProtocolVersions.SchemaVersion ||
            response.AttemptId != request.AttemptId ||
            !response.SourceSha256.Equals(request.SourceSha256, StringComparison.Ordinal) ||
            response.SourceFormat != request.SourceFormat ||
            !response.ProviderKey.Equals(request.ProviderKey, StringComparison.Ordinal) ||
            !response.ProviderVersion.Equals(request.ProviderVersion, StringComparison.Ordinal) ||
            !string.Equals(
                response.WorkerReleaseSha256,
                request.WorkerReleaseSha256,
                StringComparison.Ordinal) ||
            !response.Package.Document.SourceSha256.Equals(
                request.SourceSha256,
                StringComparison.Ordinal) ||
            response.Package.Document.SourceFormat != request.SourceFormat ||
            !response.Package.Document.ConverterId.Equals(
                request.ProviderKey,
                StringComparison.Ordinal) ||
            !response.Package.Document.ConverterVersion.Equals(
                request.ProviderVersion,
                StringComparison.Ordinal) ||
            !IsSha256(response.PackageSha256) ||
            !response.PackageSha256.Equals(
                ComputePackageSha256(response.Package),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The isolated CAD Worker response does not match its request.");
        }
    }

    public static byte[] SerializePackage(SpaceCadIrPackageV1 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return JsonSerializer.SerializeToUtf8Bytes(package, CanonicalJsonOptions);
    }

    public static string ComputePackageSha256(SpaceCadIrPackageV1 package) =>
        Convert.ToHexString(SHA256.HashData(SerializePackage(package)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
