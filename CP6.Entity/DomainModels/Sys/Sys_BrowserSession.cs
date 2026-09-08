using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>Durable identity of one opted-in browser login, shared by all of its refresh rotations.</summary>
public sealed class Sys_BrowserSession : BaseTenantEntity
{
    public Guid UserId { get; set; }
    /// <summary>Account authentication state proved at the original login; never upgraded by refresh.</summary>
    [MaxLength(64)] public string AuthenticationVersion { get; set; } = "";
    /// <summary>Explicit family logout, distinguished from rotated-token reuse/compromise handling.</summary>
    public DateTime? LoggedOutAtUtc { get; set; }
}
