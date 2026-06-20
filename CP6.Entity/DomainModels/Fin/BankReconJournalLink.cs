using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>匹配组 ↔ 凭证行（A4 · spec §2.4）。不动不可变凭证；JournalLineId 唯一（一行只对账一次，并发守卫 §8.4）。无 RowVersion（靠唯一约束+事务）。</summary>
[Table("Fin_BankReconJournalLink")]
public class BankReconJournalLink : BaseTenantEntity
{
    public Guid MatchGroupId { get; set; }
    /// <summary>→ Fin_JournalLine.Id（账面侧）</summary>
    public Guid JournalLineId { get; set; }
    /// <summary>冗余凭证头 Id（便于按凭证查/守卫）</summary>
    public Guid JournalEntryId { get; set; }
    /// <summary>该凭证行银行侧带方向金额（Debit=+,Credit=−）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal BankSignedAmount { get; set; }
}
