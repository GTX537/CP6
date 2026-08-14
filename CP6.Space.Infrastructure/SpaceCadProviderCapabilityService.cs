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

public sealed class SpaceCadProviderCapabilityService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceCadProviderRegistry registry,
    ISpaceClock clock) : ISpaceCadProviderCapabilityService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceCadSiteCapabilityDto> GetAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        EnsureAccess(siteId, write: false);
        return await LoadCapabilityAsync(siteId, RequireUtcNow(), cancellationToken);
    }

    public async Task<ReplaceSpaceCadProviderConfigurationResponse> ReplaceAsync(
        Guid siteId,
        ReplaceSpaceCadProviderConfigurationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAccess(siteId, write: true);
        var normalized = Normalize(request);
        var operation = $"cad-provider-config:{siteId:N}";
        var keyHash = IdempotencyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(normalized, JsonOptions));
        var replay = await ReadReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay with { IdempotentReplay = true };

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireSiteLockAsync(siteId, cancellationToken);
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay with { IdempotentReplay = true };
            }

            var current = await context.CadProviderConfigurations
                .SingleOrDefaultAsync(
                    item => item.SiteId == siteId && item.IsCurrent,
                    cancellationToken);
            var currentRevision = current?.ConfigurationRevision ?? 0;
            if (currentRevision != normalized.ExpectedConfigurationRevision)
                throw RevisionConflict(currentRevision);

            if (current is not null)
            {
                current.Supersede();
                await context.SaveChangesAsync(cancellationToken);
            }

            var now = RequireUtcNow();
            var next = SpaceCadSiteProviderConfiguration.Create(
                execution.TenantId,
                siteId,
                checked(currentRevision + 1),
                normalized.Reason,
                execution.ActorId,
                now);
            context.CadProviderConfigurations.Add(next);
            var certifications = normalized.Certifications
                .Select(item => SpaceCadSiteProviderCertification.Create(
                    execution.TenantId,
                    next.Id,
                    siteId,
                    item.ProviderKey,
                    item.Role,
                    item.DeploymentMode,
                    item.DataBoundary,
                    item.ApprovalEvidenceReference,
                    item.SecretReference,
                    item.ValidFromUtc,
                    item.ExpiresAtUtc,
                    item.SupportsDwg,
                    item.SupportsDxf,
                    item.LicensingApproved,
                    item.SecurityApproved,
                    item.DataRegionApproved,
                    item.DeletionRetentionApproved,
                    item.QualificationScore,
                    item.QualificationRubricVersion,
                    item.GoldenDatasetSha256,
                    item.FrozenEnvironmentSha256,
                    item.QualificationEvidenceReference))
                .ToArray();
            context.CadProviderCertifications.AddRange(certifications);
            await context.SaveChangesAsync(cancellationToken);

            var response = new ReplaceSpaceCadProviderConfigurationResponse(
                ToCapability(next, certifications, now),
                IdempotentReplay: false);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                200,
                now.AddHours(24),
                now.AddDays(365)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SpaceCadSiteCapabilityDto> LoadCapabilityAsync(
        Guid siteId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var configuration = await context.CadProviderConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SiteId == siteId && item.IsCurrent,
                cancellationToken);
        if (configuration is null)
            return Missing(siteId, now);
        var certifications = await context.CadProviderCertifications
            .AsNoTracking()
            .Where(item => item.ConfigurationId == configuration.Id)
            .OrderBy(item => item.Role)
            .ToArrayAsync(cancellationToken);
        return ToCapability(configuration, certifications, now);
    }

    private SpaceCadSiteCapabilityDto ToCapability(
        SpaceCadSiteProviderConfiguration configuration,
        IReadOnlyList<SpaceCadSiteProviderCertification> certifications,
        DateTime now)
    {
        var primary = certifications.SingleOrDefault(
            item => item.Role == SpaceCadProviderRole.Primary);
        var backup = certifications.SingleOrDefault(
            item => item.Role == SpaceCadProviderRole.Backup);
        var primaryDto = primary is null ? null : ToSlot(primary, now);
        var backupDto = backup is null ? null : ToSlot(backup, now);
        var blockers = new List<string>();
        AddBlockers(primaryDto, "PRIMARY", blockers);
        AddBlockers(backupDto, "BACKUP", blockers);
        if (primaryDto is not null && backupDto is not null &&
            primaryDto.ProviderKey == backupDto.ProviderKey)
            blockers.Add("CAD_PROVIDER_KEYS_NOT_DISTINCT");
        AddPairQualificationBlockers(primaryDto, backupDto, blockers);
        var canPrepare = primaryDto is
                { Qualified: true, RuntimeAvailable: true, CurrentlyValid: true } &&
            (primaryDto.SupportsDwg || primaryDto.SupportsDxf) ||
            backupDto is
                { Qualified: true, RuntimeAvailable: true, CurrentlyValid: true } &&
            (backupDto.SupportsDwg || backupDto.SupportsDxf);
        var gaReady = blockers.Count == 0 &&
            primaryDto is { SupportsDwg: true, SupportsDxf: true } &&
            backupDto is { SupportsDwg: true, SupportsDxf: true };
        return new SpaceCadSiteCapabilityDto(
            configuration.SiteId,
            configuration.ConfigurationRevision,
            canPrepare,
            gaReady,
            primaryDto,
            backupDto,
            blockers.Distinct(StringComparer.Ordinal).ToArray(),
            now,
            configuration.ApprovedAtUtc,
            configuration.ApprovedBy);
    }

    private SpaceCadProviderSlotDto ToSlot(
        SpaceCadSiteProviderCertification value,
        DateTime now)
    {
        var runtime = registry.TryGet(value.ProviderKey, out var registration) &&
            registration is not null &&
            registration.DeploymentMode == value.DeploymentMode &&
            registration.DataBoundary == value.DataBoundary &&
            (!value.SupportsDwg || registration.SupportsDwg) &&
            (!value.SupportsDxf || registration.SupportsDxf);
        return new SpaceCadProviderSlotDto(
            value.ProviderKey,
            registration?.DisplayName ?? value.ProviderKey,
            value.Role.ToString(),
            value.DeploymentMode.ToString(),
            value.DataBoundary.ToString(),
            value.ApprovalEvidenceReference,
            value.SecretReference is not null,
            value.ValidFromUtc,
            value.ExpiresAtUtc,
            value.SupportsDwg,
            value.SupportsDxf,
            value.LicensingApproved,
            value.SecurityApproved,
            value.DataRegionApproved,
            value.DeletionRetentionApproved,
            value.QualificationScore,
            value.QualificationRubricVersion,
            value.GoldenDatasetSha256,
            value.FrozenEnvironmentSha256,
            value.QualificationEvidenceReference,
            value.HasCompleteQualification,
            runtime,
            value.IsValidAt(now));
    }

    private static void AddBlockers(
        SpaceCadProviderSlotDto? value,
        string role,
        ICollection<string> blockers)
    {
        if (value is null)
        {
            blockers.Add($"CAD_{role}_PROVIDER_MISSING");
            return;
        }
        if (!value.CurrentlyValid)
            blockers.Add($"CAD_{role}_CERTIFICATION_NOT_CURRENT");
        if (!value.Qualified)
            blockers.Add($"CAD_{role}_QUALIFICATION_INCOMPLETE");
        if (!value.RuntimeAvailable)
            blockers.Add($"CAD_{role}_RUNTIME_UNAVAILABLE");
        if (!value.SupportsDwg || !value.SupportsDxf)
            blockers.Add($"CAD_{role}_FORMAT_COVERAGE_INCOMPLETE");
    }

    private static void AddPairQualificationBlockers(
        SpaceCadProviderSlotDto? primary,
        SpaceCadProviderSlotDto? backup,
        ICollection<string> blockers)
    {
        if (primary is not { Qualified: true } || backup is not { Qualified: true })
            return;
        if (!string.Equals(
                primary.QualificationRubricVersion,
                backup.QualificationRubricVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                primary.GoldenDatasetSha256,
                backup.GoldenDatasetSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                primary.FrozenEnvironmentSha256,
                backup.FrozenEnvironmentSha256,
                StringComparison.Ordinal))
        {
            blockers.Add("CAD_PROVIDER_QUALIFICATION_BASELINE_MISMATCH");
        }
        if (primary.QualificationScore == backup.QualificationScore)
            blockers.Add("CAD_PROVIDER_QUALIFICATION_SCORE_TIE");
        else if (primary.QualificationScore < backup.QualificationScore)
            blockers.Add("CAD_PROVIDER_QUALIFICATION_RANKING_INVALID");
    }

    private NormalizedRequest Normalize(ReplaceSpaceCadProviderConfigurationRequest request)
    {
        if (request.ExpectedConfigurationRevision < 0)
            throw Invalid("ExpectedConfigurationRevision cannot be negative.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw Invalid("A bounded configuration reason is required.");
        var values = request.Certifications ?? throw Invalid(
            "Certifications are required; use an empty list to disable CAD.");
        if (values.Count > 2)
            throw Invalid("A Site supports at most one primary and one backup Provider.");

        var normalized = values.Select(NormalizeCertification).ToArray();
        if (normalized.Select(item => item.ProviderKey).Distinct(StringComparer.Ordinal).Count() !=
            normalized.Length || normalized.Select(item => item.Role).Distinct().Count() !=
            normalized.Length)
            throw Invalid("Provider keys and roles must be unique within a Site configuration.");
        if (normalized.Length != 0 &&
            normalized.All(item => item.Role != SpaceCadProviderRole.Primary))
            throw Invalid("A non-empty configuration requires a primary Provider.");
        ValidateQualificationRanking(normalized);
        return new NormalizedRequest(
            request.ExpectedConfigurationRevision,
            reason,
            normalized.OrderBy(item => item.Role).ToArray());
    }

    private NormalizedCertification NormalizeCertification(
        SpaceCadProviderCertificationInputDto request)
    {
        try
        {
            var key = SpaceCadProviderKey.Normalize(request.ProviderKey);
            if (!Enum.TryParse<SpaceCadProviderRole>(request.Role, true, out var role) ||
                !Enum.IsDefined(role) ||
                !Enum.TryParse<SpaceCadProviderDeploymentMode>(
                    request.DeploymentMode,
                    true,
                    out var deployment) ||
                !Enum.IsDefined(deployment) ||
                !Enum.TryParse<SpaceCadProviderDataBoundary>(
                    request.DataBoundary,
                    true,
                    out var boundary) ||
                !Enum.IsDefined(boundary))
                throw Invalid("Provider role, deployment mode or data boundary is invalid.");
            if (!registry.TryGet(key, out var registration) || registration is null)
                throw new SpaceProblemException(
                    SpaceErrorCodes.CadProviderNotCertified,
                    422,
                    "The Provider is not registered in this deployment.",
                    recoveryAction: "register-cad-provider-deployment");
            if (registration.DeploymentMode != deployment ||
                registration.DataBoundary != boundary ||
                request.SupportsDwg && !registration.SupportsDwg ||
                request.SupportsDxf && !registration.SupportsDxf)
                throw Invalid(
                    "Certification metadata exceeds or conflicts with the deployment registration.");

            _ = SpaceCadSiteProviderCertification.Create(
                execution.TenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                key,
                role,
                deployment,
                boundary,
                request.ApprovalEvidenceReference,
                request.SecretReference,
                request.ValidFromUtc,
                request.ExpiresAtUtc,
                request.SupportsDwg,
                request.SupportsDxf,
                request.LicensingApproved,
                request.SecurityApproved,
                request.DataRegionApproved,
                request.DeletionRetentionApproved,
                request.QualificationScore,
                request.QualificationRubricVersion,
                request.GoldenDatasetSha256,
                request.FrozenEnvironmentSha256,
                request.QualificationEvidenceReference);
            return new NormalizedCertification(
                key,
                role,
                deployment,
                boundary,
                request.ApprovalEvidenceReference.Trim(),
                string.IsNullOrWhiteSpace(request.SecretReference)
                    ? null
                    : request.SecretReference.Trim(),
                request.ValidFromUtc,
                request.ExpiresAtUtc,
                request.SupportsDwg,
                request.SupportsDxf,
                request.LicensingApproved,
                request.SecurityApproved,
                request.DataRegionApproved,
                request.DeletionRetentionApproved,
                request.QualificationScore,
                request.QualificationRubricVersion.Trim(),
                request.GoldenDatasetSha256.Trim().ToLowerInvariant(),
                request.FrozenEnvironmentSha256.Trim().ToLowerInvariant(),
                request.QualificationEvidenceReference.Trim());
        }
        catch (SpaceProblemException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Invalid(exception.Message);
        }
    }

    private static void ValidateQualificationRanking(
        IReadOnlyList<NormalizedCertification> values)
    {
        if (values.Count != 2)
            return;
        var primary = values.Single(item => item.Role == SpaceCadProviderRole.Primary);
        var backup = values.Single(item => item.Role == SpaceCadProviderRole.Backup);
        if (!primary.QualificationRubricVersion.Equals(
                backup.QualificationRubricVersion,
                StringComparison.Ordinal) ||
            !primary.GoldenDatasetSha256.Equals(
                backup.GoldenDatasetSha256,
                StringComparison.Ordinal) ||
            !primary.FrozenEnvironmentSha256.Equals(
                backup.FrozenEnvironmentSha256,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "Primary and backup Providers must use the same approved rubric, " +
                "golden dataset and frozen evaluation environment.");
        }
        if (primary.QualificationScore == backup.QualificationScore)
            throw Invalid(
                "Primary and backup qualification scores must be distinct so the " +
                "highest-ranked Provider is unambiguous.");
        if (primary.QualificationScore < backup.QualificationScore)
            throw Invalid(
                "The primary Provider must have the higher qualification score.");
    }

    private async Task<ReplaceSpaceCadProviderConfigurationResponse?> ReadReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == execution.ActorId &&
                        item.Operation == operation &&
                        item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!record.RequestHash.Equals(requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was reused with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        return JsonSerializer.Deserialize<ReplaceSpaceCadProviderConfigurationResponse>(
                   record.ResponseJson,
                   JsonOptions) ?? throw new InvalidDataException(
                   "The stored CAD Provider configuration replay is invalid.");
    }

    private async Task AcquireSiteLockAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return;
        var transaction = context.Database.CurrentTransaction ??
            throw new InvalidOperationException("A transaction is required for the Site lock.");
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            SELECT @result;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = $"space:cad-provider:{execution.TenantId:N}:{siteId:N}";
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
            throw new SpaceProblemException(
                SpaceErrorCodes.CadProviderRevisionConflict,
                409,
                "The Site CAD Provider configuration is busy.",
                recoveryAction: "reload-cad-provider-configuration",
                retryable: true);
    }

    private void EnsureAccess(Guid siteId, bool write)
    {
        if (siteId == Guid.Empty)
            throw Invalid("Site is required.");
        if (execution.IsExternal)
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot access CAD Provider configuration.",
                recoveryAction: "use-published-runtime");
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        access.EnsureSiteAccess(siteId, write);
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceCadSiteCapabilityDto Missing(Guid siteId, DateTime now) =>
        new(
            siteId,
            0,
            CanPrepareCad: false,
            CadGaReady: false,
            Primary: null,
            Backup: null,
            ["CAD_PRIMARY_PROVIDER_MISSING", "CAD_BACKUP_PROVIDER_MISSING"],
            now,
            UpdatedAtUtc: null,
            UpdatedBy: null);

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.CadProviderConfigurationInvalid,
            422,
            "The Site CAD Provider configuration is invalid.",
            detail,
            "correct-cad-provider-configuration");

    private static SpaceProblemException RevisionConflict(long currentRevision) =>
        new(
            SpaceErrorCodes.CadProviderRevisionConflict,
            409,
            "The Site CAD Provider configuration changed.",
            $"Current configuration revision is {currentRevision}.",
            "reload-cad-provider-configuration");

    private static string IdempotencyHash(string operation, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            throw Invalid("A bounded Idempotency-Key is required.");
        return Hash($"{operation}:{key.Trim()}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record NormalizedRequest(
        long ExpectedConfigurationRevision,
        string Reason,
        IReadOnlyList<NormalizedCertification> Certifications);

    private sealed record NormalizedCertification(
        string ProviderKey,
        SpaceCadProviderRole Role,
        SpaceCadProviderDeploymentMode DeploymentMode,
        SpaceCadProviderDataBoundary DataBoundary,
        string ApprovalEvidenceReference,
        string? SecretReference,
        DateTime ValidFromUtc,
        DateTime ExpiresAtUtc,
        bool SupportsDwg,
        bool SupportsDxf,
        bool LicensingApproved,
        bool SecurityApproved,
        bool DataRegionApproved,
        bool DeletionRetentionApproved,
        int QualificationScore,
        string QualificationRubricVersion,
        string GoldenDatasetSha256,
        string FrozenEnvironmentSha256,
        string QualificationEvidenceReference);
}
