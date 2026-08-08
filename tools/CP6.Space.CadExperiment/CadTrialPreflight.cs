using System.Security.Cryptography;
using System.Text.Json;

namespace CP6.Space.CadExperiment;

public sealed record CadTrialDatasetConfig(
    string ManifestPath,
    string Stress50MiBPath,
    string StressOneMillionPath);

public sealed record CadTrialLegalConfig(
    string ApprovalReference,
    bool MultiTenantSaasApproved,
    bool ScaledWorkersApproved,
    bool DisasterRecoveryApproved,
    bool NonProductionApproved,
    bool RedistributionOrHostedServiceApproved);

public sealed record CadTrialIsolationConfig(
    string EvidenceReference,
    int WorkerVcpu,
    int WorkerMemoryMiB,
    string NetworkPolicy,
    bool RestrictedServiceIdentity,
    bool NoBusinessCredentials,
    bool DedicatedTemporaryDirectory,
    bool OutOfProcess,
    bool ProcessTreeKillVerified);

public sealed record CadTrialPackageConfig(
    string Platform,
    string Path,
    string Sha256);

public sealed record CadTrialControlledServiceConfig(
    string ApprovedRegion,
    string RegionApprovalReference,
    string DpaReference,
    string RetentionDeletionReference,
    string EngineVersion);

public sealed record CadTrialPreflightConfig(
    int SchemaVersion,
    string CandidateId,
    string CandidateVersion,
    string DeploymentMode,
    CadTrialDatasetConfig Dataset,
    CadTrialLegalConfig Legal,
    CadTrialIsolationConfig Isolation,
    IReadOnlyList<CadTrialPackageConfig> Packages,
    IReadOnlyList<string> RequiredSecretEnvironmentVariables,
    CadTrialControlledServiceConfig? ControlledService);

public sealed record CadTrialPreflightGate(
    string Id,
    bool Passed,
    string Evidence);

public sealed record CadTrialPackageAudit(
    string Platform,
    string SourcePath,
    bool Exists,
    long? SizeBytes,
    string? ExpectedSha256,
    string? ActualSha256,
    bool HashMatches);

public sealed record CadTrialSecretAudit(
    string EnvironmentVariable,
    bool Configured);

public sealed record CadTrialPreflightReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string ConfigPath,
    string CandidateId,
    string CandidateVersion,
    string DeploymentMode,
    bool Passed,
    IReadOnlyList<CadTrialPreflightGate> Gates,
    DatasetAuditReport? DatasetAudit,
    IReadOnlyList<CadTrialPackageAudit> Packages,
    IReadOnlyList<CadTrialSecretAudit> Secrets);

public static class CadTrialPreflight
{
    private const string EmbeddedSdk = "EmbeddedSdk";
    private const string ControlledService = "ControlledService";

    public static async Task<CadTrialPreflightReport> AuditAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        var fullConfigPath = Path.GetFullPath(configPath);
        var configDirectory = Path.GetDirectoryName(fullConfigPath)
            ?? throw new InvalidDataException("The preflight config directory is invalid.");
        CadTrialPreflightConfig config;
        await using (var stream = File.OpenRead(fullConfigPath))
        {
            config = await JsonSerializer.DeserializeAsync<CadTrialPreflightConfig>(
                    stream,
                    CadExperimentJson.Options,
                    cancellationToken)
                ?? throw new InvalidDataException("The preflight config is empty.");
        }

        ValidateRequiredFields(config);
        var gates = new List<CadTrialPreflightGate>
        {
            new(
                "config-schema",
                config.SchemaVersion == 1,
                $"Observed schema {config.SchemaVersion}; required schema 1."),
            new(
                "candidate-identity",
                HasConcreteValue(config.CandidateId)
                && HasConcreteValue(config.CandidateVersion),
                $"candidateId={Display(config.CandidateId)}; "
                + $"candidateVersion={Display(config.CandidateVersion)}."),
            BuildLegalGate(config.Legal),
            BuildIsolationGate(config.DeploymentMode, config.Isolation)
        };

        var datasetPaths = new[]
        {
            Resolve(configDirectory, config.Dataset.ManifestPath),
            Resolve(configDirectory, config.Dataset.Stress50MiBPath),
            Resolve(configDirectory, config.Dataset.StressOneMillionPath)
        };
        var datasetInputsExist = datasetPaths.All(File.Exists);
        gates.Add(new CadTrialPreflightGate(
            "dataset-inputs",
            datasetInputsExist,
            $"manifest={File.Exists(datasetPaths[0])}; "
            + $"stress50MiB={File.Exists(datasetPaths[1])}; "
            + $"stressOneMillion={File.Exists(datasetPaths[2])}."));

        DatasetAuditReport? datasetAudit = null;
        if (datasetInputsExist)
        {
            datasetAudit = await DatasetAuditor.AuditAsync(
                datasetPaths[0],
                datasetPaths[1],
                datasetPaths[2],
                cancellationToken);
        }

        gates.Add(new CadTrialPreflightGate(
            "dataset-e02-ready",
            datasetAudit?.E02ReadinessPassed == true,
            datasetAudit is null
                ? "Dataset audit was not run because one or more inputs are missing."
                : $"integrity={datasetAudit.IntegrityPassed}; "
                + $"e02Ready={datasetAudit.E02ReadinessPassed}."));

        var packageAudits = await AuditPackagesAsync(
            configDirectory,
            config.Packages,
            cancellationToken);
        AddDeploymentGates(config, packageAudits, gates);

        var secretAudits = config.RequiredSecretEnvironmentVariables
            .Select(variable => new CadTrialSecretAudit(
                variable,
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))))
            .ToArray();
        gates.Add(new CadTrialPreflightGate(
            "secret-material",
            secretAudits.Length > 0 && secretAudits.All(secret => secret.Configured),
            secretAudits.Length == 0
                ? "No required secret environment variables were declared."
                : string.Join(
                    ", ",
                    secretAudits.Select(
                        secret => $"{secret.EnvironmentVariable}={secret.Configured}"))));

        return new CadTrialPreflightReport(
            1,
            DateTimeOffset.UtcNow,
            fullConfigPath,
            config.CandidateId,
            config.CandidateVersion,
            config.DeploymentMode,
            gates.All(gate => gate.Passed),
            gates,
            datasetAudit,
            packageAudits,
            secretAudits);
    }

    private static CadTrialPreflightGate BuildLegalGate(CadTrialLegalConfig legal)
    {
        var rightsApproved = legal.MultiTenantSaasApproved
            && legal.ScaledWorkersApproved
            && legal.DisasterRecoveryApproved
            && legal.NonProductionApproved
            && legal.RedistributionOrHostedServiceApproved;
        var referencePresent = HasEvidenceReference(legal.ApprovalReference);
        return new CadTrialPreflightGate(
            "legal-deployment-rights",
            rightsApproved && referencePresent,
            $"approvalReference={referencePresent}; "
            + $"multiTenantSaas={legal.MultiTenantSaasApproved}; "
            + $"scaledWorkers={legal.ScaledWorkersApproved}; "
            + $"disasterRecovery={legal.DisasterRecoveryApproved}; "
            + $"nonProduction={legal.NonProductionApproved}; "
            + "redistributionOrHostedService="
            + $"{legal.RedistributionOrHostedServiceApproved}.");
    }

    private static CadTrialPreflightGate BuildIsolationGate(
        string deploymentMode,
        CadTrialIsolationConfig isolation)
    {
        var expectedNetworkPolicy = string.Equals(
            deploymentMode,
            EmbeddedSdk,
            StringComparison.Ordinal)
            ? "DenyAll"
            : "ApprovedEndpointsOnly";
        var passed = HasEvidenceReference(isolation.EvidenceReference)
            && isolation.WorkerVcpu == 8
            && isolation.WorkerMemoryMiB == 32768
            && string.Equals(
                isolation.NetworkPolicy,
                expectedNetworkPolicy,
                StringComparison.Ordinal)
            && isolation.RestrictedServiceIdentity
            && isolation.NoBusinessCredentials
            && isolation.DedicatedTemporaryDirectory
            && isolation.OutOfProcess
            && isolation.ProcessTreeKillVerified;
        return new CadTrialPreflightGate(
            "frozen-worker-isolation",
            passed,
            $"evidenceReference={HasEvidenceReference(isolation.EvidenceReference)}; "
            + $"vCpu={isolation.WorkerVcpu}; memoryMiB={isolation.WorkerMemoryMiB}; "
            + $"networkPolicy={Display(isolation.NetworkPolicy)}; "
            + $"expectedNetworkPolicy={expectedNetworkPolicy}; "
            + $"restrictedIdentity={isolation.RestrictedServiceIdentity}; "
            + $"noBusinessCredentials={isolation.NoBusinessCredentials}; "
            + $"dedicatedTemp={isolation.DedicatedTemporaryDirectory}; "
            + $"outOfProcess={isolation.OutOfProcess}; "
            + $"processTreeKill={isolation.ProcessTreeKillVerified}.");
    }

    private static void AddDeploymentGates(
        CadTrialPreflightConfig config,
        IReadOnlyList<CadTrialPackageAudit> packages,
        ICollection<CadTrialPreflightGate> gates)
    {
        if (string.Equals(
                config.DeploymentMode,
                EmbeddedSdk,
                StringComparison.Ordinal))
        {
            var requiredPlatforms = new[] { "windows-x64", "linux-x64" };
            gates.Add(new CadTrialPreflightGate(
                "embedded-sdk-packages",
                requiredPlatforms.All(platform => packages.Any(
                    package => string.Equals(
                            package.Platform,
                            platform,
                            StringComparison.OrdinalIgnoreCase)
                        && package.Exists
                        && package.HashMatches)),
                string.Join(
                    ", ",
                    requiredPlatforms.Select(platform =>
                    {
                        var package = packages.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.Platform,
                                platform,
                                StringComparison.OrdinalIgnoreCase));
                        return $"{platform}="
                            + $"{package is { Exists: true, HashMatches: true }}";
                    }))));
            gates.Add(new CadTrialPreflightGate(
                "controlled-service-governance",
                true,
                "Not applicable to EmbeddedSdk."));
            return;
        }

        var service = config.ControlledService;
        var servicePassed = service is not null
            && HasConcreteValue(service.ApprovedRegion)
            && HasEvidenceReference(service.RegionApprovalReference)
            && HasEvidenceReference(service.DpaReference)
            && HasEvidenceReference(service.RetentionDeletionReference)
            && HasConcreteValue(service.EngineVersion);
        gates.Add(new CadTrialPreflightGate(
            "embedded-sdk-packages",
            true,
            "Not applicable to ControlledService."));
        gates.Add(new CadTrialPreflightGate(
            "controlled-service-governance",
            servicePassed,
            service is null
                ? "Controlled service configuration is missing."
                : $"region={Display(service.ApprovedRegion)}; "
                + $"regionApproval={HasEvidenceReference(service.RegionApprovalReference)}; "
                + $"dpa={HasEvidenceReference(service.DpaReference)}; "
                + "retentionDeletion="
                + $"{HasEvidenceReference(service.RetentionDeletionReference)}; "
                + $"engineVersion={Display(service.EngineVersion)}."));
    }

    private static async Task<IReadOnlyList<CadTrialPackageAudit>> AuditPackagesAsync(
        string configDirectory,
        IReadOnlyList<CadTrialPackageConfig> packages,
        CancellationToken cancellationToken)
    {
        var results = new List<CadTrialPackageAudit>();
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Resolve(configDirectory, package.Path);
            var exists = File.Exists(fullPath);
            string? actualHash = null;
            if (exists)
            {
                await using var stream = File.OpenRead(fullPath);
                actualHash = Convert.ToHexString(
                        await SHA256.HashDataAsync(stream, cancellationToken))
                    .ToLowerInvariant();
            }

            results.Add(new CadTrialPackageAudit(
                package.Platform,
                fullPath,
                exists,
                exists ? new FileInfo(fullPath).Length : null,
                IsSha256(package.Sha256) ? package.Sha256.ToLowerInvariant() : null,
                actualHash,
                actualHash is not null
                && actualHash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)));
        }

        return results;
    }

    private static void ValidateRequiredFields(CadTrialPreflightConfig config)
    {
        if (config.Dataset is null
            || config.Legal is null
            || config.Isolation is null
            || config.Packages is null
            || config.RequiredSecretEnvironmentVariables is null)
        {
            throw new InvalidDataException(
                "Preflight config is missing a required object or collection.");
        }

        if (!string.Equals(
                config.DeploymentMode,
                EmbeddedSdk,
                StringComparison.Ordinal)
            && !string.Equals(
                config.DeploymentMode,
                ControlledService,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Deployment mode must be '{EmbeddedSdk}' or '{ControlledService}'.");
        }

        if (!HasValue(config.Dataset.ManifestPath)
            || !HasValue(config.Dataset.Stress50MiBPath)
            || !HasValue(config.Dataset.StressOneMillionPath))
        {
            throw new InvalidDataException(
                "Dataset manifest and stress asset paths are required.");
        }

        if (config.Packages.Any(package =>
                package is null
                || !HasValue(package.Platform)
                || !HasValue(package.Path)
                || !HasValue(package.Sha256)))
        {
            throw new InvalidDataException(
                "Each package requires a platform, path, and expected SHA-256.");
        }

        if (config.RequiredSecretEnvironmentVariables.Any(
                variable => !IsEnvironmentVariableName(variable)))
        {
            throw new InvalidDataException(
                "Secret environment variable names may contain only A-Z, 0-9, and underscore.");
        }
    }

    private static string Resolve(string configDirectory, string path)
    {
        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(configDirectory, path));
    }

    private static bool IsEnvironmentVariableName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character =>
                character is >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_');
    }

    private static bool IsSha256(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length == 64
            && value.All(Uri.IsHexDigit);
    }

    private static bool HasEvidenceReference(string value)
    {
        return HasConcreteValue(value);
    }

    private static bool HasConcreteValue(string value)
    {
        return HasValue(value)
            && !value.Contains('<', StringComparison.Ordinal)
            && !value.Contains('>', StringComparison.Ordinal);
    }

    private static bool HasValue(string value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Display(string value)
    {
        return HasValue(value) ? value : "<missing>";
    }
}
