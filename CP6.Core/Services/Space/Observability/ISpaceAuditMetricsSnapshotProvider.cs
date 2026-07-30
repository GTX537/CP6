using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space.Observability;

/// <summary>
/// Produces a tenant-agnostic operational snapshot of the append-only Space
/// audit ledger. The snapshot intentionally has no tenant dimension.
/// </summary>
public interface ISpaceAuditMetricsSnapshotProvider
{
    Task<SpaceAuditMetricsSnapshot> GetSnapshotAsync(
        CancellationToken ct = default);
}
