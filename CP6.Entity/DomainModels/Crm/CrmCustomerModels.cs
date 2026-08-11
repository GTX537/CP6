using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Crm;

[Table("Crm_Account")]
public class CrmAccount : BaseBizEntity, IDataScoped, IAuditable
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string NormalizedName { get; set; } = string.Empty;
    [MaxLength(200)] public string? Website { get; set; }
    [MaxLength(100)] public string? Industry { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? DeptId { get; set; }
    [MaxLength(20)] public string? BusinessPartnerCd { get; set; }
    public List<CrmContact> Contacts { get; set; } = new();
}

[Table("Crm_Contact")]
public class CrmContact : BaseBizEntity, IDataScoped, IAuditable
{
    public Guid? AccountId { get; set; }
    [Required, MaxLength(160), PiiField(Mode = PiiErase.Placeholder)]
    public string FullName { get; set; } = string.Empty;
    [MaxLength(200), PiiField(Mode = PiiErase.Null)] public string? Email { get; set; }
    [MaxLength(200), PiiField(Mode = PiiErase.Null)] public string? NormalizedEmail { get; set; }
    [MaxLength(50), PiiField(Mode = PiiErase.Null)] public string? Phone { get; set; }
    [MaxLength(50), PiiField(Mode = PiiErase.Null)] public string? NormalizedPhone { get; set; }
    [MaxLength(100)] public string? JobTitle { get; set; }
    public bool PrivacyConsent { get; set; }
    public DateTime? PrivacyConsentAt { get; set; }
    [MaxLength(50)] public string? PrivacyPolicyVersion { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? DeptId { get; set; }
    public CrmAccount? Account { get; set; }
}
