using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

[Table("T_CreditNote")]
public class CreditNote : BaseBizEntity
{
    [Required, MaxLength(20)] public string CreditNoteNo { get; set; } = string.Empty;
    [MaxLength(20)] public string? WebOrderNo { get; set; }
    [MaxLength(20)] public string? RmaNo { get; set; }
    [Required, MaxLength(20)] public string Type { get; set; } = CreditNoteType.Refund;
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = string.Empty;
    [MaxLength(20)] public string? ProductCd { get; set; }
    [MaxLength(30)] public string? LotNo { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.Today;
}

public static class CreditNoteType
{
    public const string Refund = "REFUND";
    public const string Exchange = "EXCHANGE";
    public const string Scrap = "SCRAP";
}
