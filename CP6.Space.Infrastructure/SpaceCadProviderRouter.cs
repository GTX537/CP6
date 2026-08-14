using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadProviderRouter(
    SpaceContext context,
    ISpaceCadProviderRegistry registry,
    ISpaceClock clock,
    ILogger<SpaceCadProviderRouter> logger) :
    ISpaceCadPreparationProvider,
    ISpaceCadParseProvider
{
    public async Task<SpaceCadIrPackageV1> InspectAsync(
        SpaceCadPreparationProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        var candidates = await LoadCandidatesAsync(
            request.SiteId,
            request.SourceFormat,
            preferredProviderKey: null,
            cancellationToken);
        var initialPosition = source.CanSeek ? source.Position : (long?)null;
        Exception? last = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            try
            {
                var package = await candidate.Registration.PreparationProvider
                    .InspectAsync(request, source, cancellationToken);
                if (!package.Document.ConverterId.Equals(
                        candidate.Registration.ProviderKey,
                        StringComparison.Ordinal))
                {
                    throw new SpaceProblemException(
                        SpaceErrorCodes.CadProviderFailoverDenied,
                        502,
                        "The CAD Provider output identity does not match its registration.",
                        recoveryAction: "inspect-cad-provider-deployment");
                }
                return package;
            }
            catch (Exception exception) when (IsRetryable(exception))
            {
                last = exception;
                if (index + 1 >= candidates.Count ||
                    !TryResetForFallback(source, initialPosition))
                    break;
                logger.LogWarning(
                    exception,
                    "CAD preparation Provider {ProviderKey} failed for Site {SiteId}; " +
                    "using certified fallback {FallbackProviderKey}.",
                    candidate.Registration.ProviderKey,
                    request.SiteId,
                    candidates[index + 1].Registration.ProviderKey);
            }
        }
        throw PreparationUnavailable(last);
    }

    public async Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
        SpaceCadParseProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        var siteId = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == request.Payload.ModelVersionId
                select model.SiteId)
            .SingleOrDefaultAsync(cancellationToken);
        if (siteId == Guid.Empty)
            throw ParseUnavailable("The CAD parse Site could not be resolved.");
        IReadOnlyList<Candidate> candidates;
        try
        {
            candidates = await LoadCandidatesAsync(
                siteId,
                request.Payload.SourceFormat,
                request.Payload.PreferredProviderKey,
                cancellationToken);
        }
        catch (SpaceProblemException exception)
        {
            throw ParseUnavailable(exception.Title);
        }
        var initialPosition = source.CanSeek ? source.Position : (long?)null;
        Exception? last = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            try
            {
                return await candidate.Registration.ParseProvider.GenerateAsync(
                    request,
                    source,
                    cancellationToken);
            }
            catch (Exception exception) when (IsRetryable(exception))
            {
                last = exception;
                if (index + 1 >= candidates.Count)
                    break;
                if (!TryResetForFallback(source, initialPosition))
                    break;
                logger.LogWarning(
                    exception,
                    "CAD parse Provider {ProviderKey} failed for Site {SiteId}; " +
                    "using certified fallback {FallbackProviderKey}.",
                    candidate.Registration.ProviderKey,
                    siteId,
                    candidates[index + 1].Registration.ProviderKey);
            }
        }
        throw ParseUnavailable(
            last is null
                ? "No certified CAD Provider could execute this parse."
                : "All certified CAD Providers were unavailable.");
    }

    private async Task<IReadOnlyList<Candidate>> LoadCandidatesAsync(
        Guid siteId,
        SpaceCadSourceFormat format,
        string? preferredProviderKey,
        CancellationToken cancellationToken)
    {
        var configuration = await context.CadProviderConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SiteId == siteId && item.IsCurrent,
                cancellationToken);
        if (configuration is null)
            throw new SpaceProblemException(
                SpaceErrorCodes.CadProviderNotCertified,
                409,
                "This Site has no current CAD Provider certification.",
                recoveryAction: "configure-site-cad-provider");
        var certifications = await context.CadProviderCertifications.AsNoTracking()
            .Where(item => item.ConfigurationId == configuration.Id)
            .OrderBy(item => item.Role)
            .ToArrayAsync(cancellationToken);
        var now = RequireUtcNow();
        var eligible = certifications.Select(certification =>
            {
                if (!certification.IsValidAt(now) ||
                    !Supports(certification, format) ||
                    !registry.TryGet(certification.ProviderKey, out var registration) ||
                    registration is null ||
                    registration.DeploymentMode != certification.DeploymentMode ||
                    registration.DataBoundary != certification.DataBoundary ||
                    !Supports(registration, format))
                    return null;
                return new Candidate(certification, registration);
            })
            .Where(item => item is not null)
            .Cast<Candidate>()
            .ToList();
        if (!string.IsNullOrWhiteSpace(preferredProviderKey))
        {
            var normalized = SpaceCadProviderKey.Normalize(preferredProviderKey);
            var preferred = eligible.SingleOrDefault(item =>
                item.Registration.ProviderKey == normalized);
            if (preferred is null)
                throw new SpaceProblemException(
                    SpaceErrorCodes.CadProviderFailoverDenied,
                    409,
                    "The Provider sealed by CAD preparation is no longer certified.",
                    "Run CAD preparation again under the current Site certification.",
                    "restart-cad-preparation");
            eligible = eligible
                .Where(item =>
                    (int)item.Certification.Role >=
                    (int)preferred.Certification.Role)
                .OrderBy(item => item.Certification.Role)
                .ToList();
        }
        if (eligible.Count == 0)
            throw new SpaceProblemException(
                SpaceErrorCodes.CadProviderUnavailable,
                503,
                "No current certified CAD Provider is available for this format.",
                recoveryAction: "repair-site-cad-provider",
                retryable: true);
        return eligible;
    }

    private static bool Supports(
        SpaceCadSiteProviderCertification value,
        SpaceCadSourceFormat format) =>
        format switch
        {
            SpaceCadSourceFormat.Dwg => value.SupportsDwg,
            SpaceCadSourceFormat.Dxf => value.SupportsDxf,
            _ => false,
        };

    private static bool Supports(
        SpaceCadProviderRegistration value,
        SpaceCadSourceFormat format) =>
        format switch
        {
            SpaceCadSourceFormat.Dwg => value.SupportsDwg,
            SpaceCadSourceFormat.Dxf => value.SupportsDxf,
            _ => false,
        };

    private static bool IsRetryable(Exception exception) =>
        exception is TimeoutException or IOException or HttpRequestException ||
        exception is SpaceProblemException { Retryable: true } ||
        exception is SpaceJobProcessingException
        {
            FailureKind: SpaceJobFailureKind.Resource,
        };

    private static bool TryResetForFallback(
        Stream source,
        long? initialPosition)
    {
        if (initialPosition.HasValue && source.CanSeek)
        {
            source.Position = initialPosition.Value;
            return true;
        }
        return false;
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceProblemException PreparationUnavailable(Exception? exception) =>
        new(
            SpaceErrorCodes.CadProviderUnavailable,
            503,
            "All certified CAD preparation Providers were unavailable.",
            exception is null ? null : "Retry the operation or repair the Site Provider chain.",
            recoveryAction: "retry-or-repair-site-cad-provider",
            retryable: true);

    private static SpaceJobProcessingException ParseUnavailable(string detail) =>
        new(
            SpaceJobFailureKind.Resource,
            SpaceErrorCodes.CadProviderUnavailable,
            detail);

    private sealed record Candidate(
        SpaceCadSiteProviderCertification Certification,
        SpaceCadProviderRegistration Registration);
}
