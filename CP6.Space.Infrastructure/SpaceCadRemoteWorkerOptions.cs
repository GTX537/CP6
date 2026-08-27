using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadRemoteWorkerOptions
{
    public const string SectionName = "Space:Cad:RemoteWorker";

    public bool Enabled { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderVersion { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SpaceCadProviderDeploymentMode DeploymentMode { get; set; }
    public SpaceCadProviderDataBoundary DataBoundary { get; set; }
    public bool SupportsDwg { get; set; }
    public bool SupportsDxf { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string ServerCertificateSha256 { get; set; } = string.Empty;
    public string ClientCertificateThumbprint { get; set; } = string.Empty;
    public string ClientCertificateStoreLocation { get; set; } = "LocalMachine";
    public string ClientCertificateStoreName { get; set; } = "My";
    public string ApprovalManifestPath { get; set; } = string.Empty;
    public string ApprovalManifestSha256 { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 300;
    public long MaximumSourceBytes { get; set; } =
        SpaceCadWorkerProtocolVersions.MaximumSourceBytes;
    public long MaximumResponseBytes { get; set; } =
        SpaceCadWorkerProtocolVersions.MaximumResponseBytes;

    public Uri ValidateRuntime()
    {
        if (!Enabled)
            throw new InvalidOperationException("The remote CAD Worker is disabled.");
        ProviderKey = SpaceCadProviderKey.Normalize(ProviderKey);
        ProviderVersion = SpaceCadProviderVersion.Normalize(ProviderVersion);
        DisplayName = DisplayName?.Trim() ?? string.Empty;
        if (DisplayName.Length is < 1 or > 120 ||
            !Enum.IsDefined(DeploymentMode) ||
            !Enum.IsDefined(DataBoundary) ||
            (!SupportsDwg && !SupportsDxf))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker registration is incomplete.");
        }

        if (!Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !endpoint.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker endpoint must be an HTTPS base URI ending in '/'.");
        }
        Endpoint = endpoint.AbsoluteUri;
        ServerCertificateSha256 = NormalizeSha256(
            ServerCertificateSha256,
            "server certificate SHA-256");
        ClientCertificateThumbprint = NormalizeThumbprint(
            ClientCertificateThumbprint);
        if (!Enum.TryParse<System.Security.Cryptography.X509Certificates.StoreLocation>(
                ClientCertificateStoreLocation,
                ignoreCase: true,
                out _) ||
            !Enum.TryParse<System.Security.Cryptography.X509Certificates.StoreName>(
                ClientCertificateStoreName,
                ignoreCase: true,
                out _))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker client-certificate store is invalid.");
        }
        ApprovalManifestPath = Path.GetFullPath(
            ApprovalManifestPath?.Trim() ?? string.Empty);
        if (!File.Exists(ApprovalManifestPath))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker approval Manifest does not exist.");
        }
        ApprovalManifestSha256 = NormalizeSha256(
            ApprovalManifestSha256,
            "approval Manifest SHA-256");
        if (TimeoutSeconds is < 1 or > 1_800 ||
            MaximumSourceBytes is < 1 or > SpaceCadWorkerProtocolVersions.MaximumSourceBytes ||
            MaximumResponseBytes is < 1 or > SpaceCadWorkerProtocolVersions.MaximumResponseBytes)
        {
            throw new InvalidOperationException(
                "The remote CAD Worker resource limits are invalid.");
        }
        return endpoint;
    }

    public SpaceCadRemoteWorkerApprovalManifestV1 LoadApprovalManifest(
        DateTime nowUtc)
    {
        var endpoint = ValidateRuntime();
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Approval time evaluation must use UTC.", nameof(nowUtc));
        var bytes = File.ReadAllBytes(ApprovalManifestPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!actualHash.Equals(ApprovalManifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker approval Manifest hash is invalid.");
        }

        SpaceCadRemoteWorkerApprovalManifestV1 manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SpaceCadRemoteWorkerApprovalManifestV1>(
                           bytes,
                           ApprovalJsonOptions)
                       ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The remote CAD Worker approval Manifest is invalid JSON.",
                exception);
        }
        ValidateManifest(manifest, endpoint, nowUtc);
        return manifest;
    }

    private void ValidateManifest(
        SpaceCadRemoteWorkerApprovalManifestV1 manifest,
        Uri endpoint,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1 ||
            manifest.ProviderKey != ProviderKey ||
            manifest.ProviderVersion != ProviderVersion ||
            manifest.DisplayName != DisplayName ||
            manifest.DeploymentMode != DeploymentMode.ToString() ||
            manifest.DataBoundary != DataBoundary.ToString() ||
            manifest.SupportsDwg != SupportsDwg ||
            manifest.SupportsDxf != SupportsDxf ||
            manifest.Endpoint != endpoint.AbsoluteUri ||
            manifest.ServerCertificateSha256 != ServerCertificateSha256 ||
            NormalizeThumbprint(manifest.ClientCertificateThumbprint) !=
                ClientCertificateThumbprint ||
            !IsSha256(manifest.WorkerReleaseSha256) ||
            manifest.QualificationScore < 80 ||
            !IsSha256(manifest.GoldenDatasetSha256) ||
            !IsSha256(manifest.FrozenEnvironmentSha256) ||
            manifest.ValidFromUtc.Kind != DateTimeKind.Utc ||
            manifest.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
            manifest.ValidFromUtc > nowUtc ||
            manifest.ExpiresAtUtc <= nowUtc ||
            manifest.ExpiresAtUtc <= manifest.ValidFromUtc ||
            !manifest.MutuallyAuthenticatedTls ||
            !manifest.OutboundNetworkDisabled ||
            !manifest.BusinessCredentialsUnavailable ||
            !manifest.RawCadDeletedOnCompletion ||
            !manifest.ArtifactOnlyResponse ||
            !manifest.SourceHashVerifiedBeforeConversion ||
            !manifest.ConverterContractRunnerEnforced)
        {
            throw new InvalidOperationException(
                "The remote CAD Worker approval Manifest does not match the frozen runtime.");
        }

        RequireEvidence(manifest.LicensingApprovalReference);
        RequireEvidence(manifest.SecurityApprovalReference);
        RequireEvidence(manifest.DataRegionApprovalReference);
        RequireEvidence(manifest.DeletionRetentionApprovalReference);
        RequireEvidence(manifest.WorkerIdentityReference);
        RequireEvidence(manifest.ClientCertificateReference);
        RequireEvidence(manifest.QualificationRubricVersion);
        RequireEvidence(manifest.QualificationEvidenceReference);
    }

    private static void RequireEvidence(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 500 ||
            normalized.Contains("example", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("fixture", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("todo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The remote CAD Worker approval evidence is incomplete.");
        }
    }

    private static string NormalizeSha256(string value, string label)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (!IsSha256(normalized))
            throw new InvalidOperationException($"A valid {label} is required.");
        return normalized!;
    }

    internal static string NormalizeThumbprint(string value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && character != ':')
            .ToArray())
            .ToUpperInvariant();
        if (normalized.Length is not (40 or 64) || !normalized.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                "A valid client-certificate thumbprint is required.");
        }
        return normalized;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static readonly JsonSerializerOptions ApprovalJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

public sealed record SpaceCadRemoteWorkerApprovalManifestV1(
    int SchemaVersion,
    string ProviderKey,
    string ProviderVersion,
    string DisplayName,
    string DeploymentMode,
    string DataBoundary,
    bool SupportsDwg,
    bool SupportsDxf,
    string Endpoint,
    string ServerCertificateSha256,
    string ClientCertificateThumbprint,
    string ClientCertificateReference,
    string WorkerReleaseSha256,
    string WorkerIdentityReference,
    bool MutuallyAuthenticatedTls,
    bool OutboundNetworkDisabled,
    bool BusinessCredentialsUnavailable,
    bool RawCadDeletedOnCompletion,
    bool ArtifactOnlyResponse,
    bool SourceHashVerifiedBeforeConversion,
    bool ConverterContractRunnerEnforced,
    string LicensingApprovalReference,
    string SecurityApprovalReference,
    string DataRegionApprovalReference,
    string DeletionRetentionApprovalReference,
    int QualificationScore,
    string QualificationRubricVersion,
    string GoldenDatasetSha256,
    string FrozenEnvironmentSha256,
    string QualificationEvidenceReference,
    DateTime ValidFromUtc,
    DateTime ExpiresAtUtc);
