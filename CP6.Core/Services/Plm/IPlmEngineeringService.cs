using CP6.Entity.DTOs.Plm;

namespace CP6.Core.Services.Plm;

public interface IPlmEngineeringService
{
    Task<PlmOperation<IterationCandidateResult>> CreateIterationCandidateAsync(Guid tenantId, string actor, CreateIterationCandidateCommand command, CancellationToken ct);
    Task<PlmOperation<EngineeringCandidateResult>> CreateEngineeringCandidateAsync(Guid tenantId, string actor, CreateEngineeringCandidateCommand command, CancellationToken ct);
    Task<PlmOperation<TechnicalManifestResult>> CaptureTechnicalManifestAsync(Guid tenantId, string actor, Guid candidateId, CaptureTechnicalManifestCommand command, CancellationToken ct);
    Task<TechnicalManifestResult> ValidateTechnicalManifestAsync(Guid tenantId, string actor, Guid candidateId, Guid manifestId, ValidateTechnicalManifestCommand command, CancellationToken ct);
    Task<PlmOperation<EngineeringBaselineResult>> FreezeEngineeringBaselineAsync(Guid tenantId, string actor, Guid candidateId, FreezeEngineeringBaselineCommand command, CancellationToken ct);
    Task<TechnicalManifestDetailResult> GetTechnicalManifestAsync(Guid tenantId, Guid manifestId, CancellationToken ct);
    Task<EngineeringBaselineResult> GetEngineeringBaselineAsync(Guid tenantId, Guid baselineId, CancellationToken ct);
    Task<EngineeringBaselineResult> ResolveBaselineIdentityAsync(Guid tenantId, Guid candidateId, int candidateVersion, CancellationToken ct);
    Task<TechnicalManifestDetailResult> ResolveCaptureRequestAsync(Guid tenantId, Guid captureRequestId, CancellationToken ct);
    Task<EngineeringBaselineResult> ResolveFreezeRequestAsync(Guid tenantId, Guid freezeRequestId, CancellationToken ct);
    Task<EngineeringBaselineResult> SupersedeEngineeringBaselineAsync(Guid tenantId, string actor, Guid baselineId, SupersedeEngineeringBaselineCommand command, CancellationToken ct);
}
