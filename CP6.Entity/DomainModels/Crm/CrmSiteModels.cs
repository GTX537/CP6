using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Crm;

[Table("Crm_Site")]
public class CrmSite : BaseBizEntity, IAuditable
{
    [Required, MaxLength(80)] public string SiteKey { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string SiteName { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string DefaultLocale { get; set; } = "zh-CN";
    [Required, MaxLength(200)] public string EnabledLocales { get; set; } = "zh-CN";
    public CrmSiteStatus Status { get; set; } = CrmSiteStatus.Draft;
    public Guid? DefaultFormId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
}

[Table("Crm_SitePage")]
public class CrmSitePage : BaseBizEntity, IAuditable
{
    public Guid SiteId { get; set; }
    public CrmPageType PageType { get; set; }
    [Required, MaxLength(120)] public string PageKey { get; set; } = string.Empty;
    public Guid? PublishedRevisionId { get; set; }
    public int SortOrder { get; set; }
    public bool Enable { get; set; } = true;
}

[Table("Crm_PageRevision")]
public class CrmPageRevision : BaseTenantEntity, IAuditable
{
    public Guid PageId { get; set; }
    public int Version { get; set; }
    public CrmPublicationStatus Status { get; set; } = CrmPublicationStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
}

[Table("Crm_PageTranslation")]
public class CrmPageTranslation : BaseTenantEntity, IAuditable
{
    public Guid SiteId { get; set; }
    public Guid RevisionId { get; set; }
    [Required, MaxLength(10)] public string Locale { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(500)] public string? Summary { get; set; }
    [Required] public string BodyJson { get; set; } = "{}";
    [MaxLength(200)] public string? SeoTitle { get; set; }
    [MaxLength(500)] public string? SeoDescription { get; set; }
}

[Table("Crm_MediaAsset")]
public class CrmMediaAsset : BaseTenantEntity, IAuditable
{
    public Guid SiteId { get; set; }
    [Required, MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string StorePath { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    [Required, MaxLength(64)] public string FileHash { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
}

[Table("Crm_PublicForm")]
public class CrmPublicForm : BaseTenantEntity, IAuditable
{
    public Guid SiteId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "Contact";
    public Guid? IntakeConfigId { get; set; }
    [Required, MaxLength(50)] public string PrivacyPolicyVersion { get; set; } = "1";
    public bool Enable { get; set; } = true;
    public DateTime? TokenRotatedAt { get; set; }
}

/// <summary>
/// Public request routing registry. It intentionally has no global tenant query filter and stores no business/PII data.
/// Resolve the route here, set ITenantContext, then query the tenant-filtered target entity.
/// </summary>
[Table("Crm_PublicRoute")]
public class CrmPublicRoute : BaseEntity
{
    public Guid TenantId { get; set; }
    [Required, MaxLength(20)] public string RouteType { get; set; } = string.Empty;
    [MaxLength(100)] public string? PublicKey { get; set; }
    [MaxLength(64)] public string? TokenHash { get; set; }
    public Guid TargetId { get; set; }
    public bool Enable { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
}
