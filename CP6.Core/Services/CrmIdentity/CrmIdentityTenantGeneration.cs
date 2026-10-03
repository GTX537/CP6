namespace CP6.Core.Services.CrmIdentity;

/// <summary>Durable tenant directory generation advanced by the snapshot mutation trigger in the same transaction.</summary>
public sealed class CrmIdentityTenantGeneration
{
    public Guid TenantId { get; set; }
    public long Generation { get; set; }
}
