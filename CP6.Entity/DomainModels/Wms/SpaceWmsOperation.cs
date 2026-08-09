using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// Durable operation-key ledger for the CP6 implementation of the Space WMS
/// adapter. The WMS mutations and their replayable result are committed in
/// the same transaction.
/// </summary>
[Table("T_SpaceWmsOperation")]
public sealed class SpaceWmsOperation : BaseBizEntity
{
    [Required, MaxLength(250)]
    public string OperationKey { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    public int State { get; set; }

    [MaxLength(100)]
    public string? ExternalOperationId { get; set; }

    public string ResultJson { get; set; } = "{}";

    public DateTime ObservedAtUtc { get; set; }
}
