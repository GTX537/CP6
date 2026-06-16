using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 银行账户（章03 §2）。付款/收款的资金账户，绑定一个 GL 银行存款科目（<see cref="GlAccountId"/>）。
/// 付款凭证的贷方银行行据此账户取科目（自动凭证 HeaderAccount 模式）。
/// </summary>
[Table("Fin_BankAccount")]
public class BankAccount : BaseTenantEntity
{
    /// <summary>账户编码（唯一）</summary>
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;

    /// <summary>账户名称</summary>
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;

    /// <summary>开户行</summary>
    [MaxLength(100)] public string? BankName { get; set; }

    /// <summary>银行账号</summary>
    [MaxLength(50)] public string? AccountNo { get; set; }

    /// <summary>账户币种，null/JPY=本位币</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }

    /// <summary>对应的 GL 银行存款科目 Id</summary>
    public Guid GlAccountId { get; set; }

    /// <summary>停用账户不能再选</summary>
    public bool IsActive { get; set; } = true;
}
