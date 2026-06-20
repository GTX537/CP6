using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>匹配组（A4 · spec §2.3）。统一承载 1:1/1:N/N:1/N:M；组内 Σ流水 SignedAmount == Σ凭证银行侧 SignedAmount。</summary>
[Table("Fin_BankReconMatch")]
public class BankReconMatch : BaseTenantEntity
{
    public Guid StatementId { get; set; }
    /// <summary>Auto=1 / Manual=2</summary>
    public BankReconMatchType MatchType { get; set; }
    /// <summary>组内流水行 ΣSignedAmount（=组内凭证行银行侧带方向合计，必相等）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal StmtSignedSum { get; set; }
    public DateTime MatchedAt { get; set; }
    [MaxLength(100)] public string MatchedBy { get; set; } = string.Empty;
    [MaxLength(500)] public string? Note { get; set; }

    /// <summary>乐观并发（撮合台核心实体）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}

public enum BankReconMatchType { Auto = 1, Manual = 2 }
