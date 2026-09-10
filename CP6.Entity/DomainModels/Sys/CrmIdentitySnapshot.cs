namespace CP6.Entity.DomainModels.Sys;

/// <summary>Minimal authorization facts retained for reconciliation and deletion tombstones.</summary>
public sealed class CrmIdentitySnapshot
{
    public Guid TenantId { get; set; }
    public string AggregateId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int Version { get; set; }
    public string PayloadJson { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>No raw token or client credential is persisted.</summary>
public sealed class CrmServiceTokenRecord
{
    public string Issuer { get; set; } = "";
    public string Jti { get; set; } = "";
    public string ClientId { get; set; } = "";
    public Guid TenantId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CrmIdentityBootstrapState
{
    public Guid TenantId { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string ContractBundleSha256 { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}
