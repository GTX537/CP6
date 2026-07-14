using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CP6.Entity;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>租户级连接器（WFS infra ④，spec §5.1）。解析键 Name（unique(TenantId,Name)）；
/// AuthJsonEncrypted＝DataProtection 密文，读接口永不回显明文。</summary>
[Table("Wf_Connector")]
public class Wf_Connector : BaseTenantEntity
{
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(200)] public string DisplayName { get; set; } = "";
    [MaxLength(500)] public string BaseUrl { get; set; } = "";

    /// <summary>DataProtection 密文：{type:"apiKey|basic|bearer", ...}。null=无认证。</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? AuthJsonEncrypted { get; set; }

    public int TimeoutSec { get; set; } = 30;
    public bool Enabled { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }
}
