using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>导入列映射模板（A4 · spec §2.5）。入款列/出款列业务语义命名，不采银行 Debit/Credit 记账视角。</summary>
[Table("Fin_BankImportProfile")]
public class BankImportProfile : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    /// <summary>绑定账户（null=通用）</summary>
    public Guid? BankAccountId { get; set; }
    /// <summary>Csv=1 / Excel=2</summary>
    public BankFileFormat FileFormat { get; set; } = BankFileFormat.Csv;
    [MaxLength(20)] public string Encoding { get; set; } = "UTF-8";
    [MaxLength(4)] public string Delimiter { get; set; } = ",";
    public int SkipHeaderRows { get; set; }
    [MaxLength(40)] public string DateField { get; set; } = string.Empty;
    [MaxLength(40)] public string DateFormat { get; set; } = "yyyy/MM/dd";
    /// <summary>SignedSingle=1（单列带符号） / DepositWithdrawalColumns=2（入款列/出款列）</summary>
    public BankAmountMode AmountMode { get; set; } = BankAmountMode.SignedSingle;
    [MaxLength(40)] public string? AmountField { get; set; }
    /// <summary>入款列（业务语义命名）</summary>
    [MaxLength(40)] public string? DepositAmountField { get; set; }
    /// <summary>出款列（业务语义命名）</summary>
    [MaxLength(40)] public string? WithdrawalAmountField { get; set; }
    /// <summary>SignedSingle 时：PositiveIsDeposit=1 / PositiveIsWithdrawal=2</summary>
    public BankSignRule SignRule { get; set; } = BankSignRule.PositiveIsDeposit;
    [MaxLength(40)] public string? DescriptionField { get; set; }
    [MaxLength(40)] public string? CounterpartyField { get; set; }
    [MaxLength(40)] public string? RefNoField { get; set; }
    [MaxLength(40)] public string? BalanceField { get; set; }
    [MaxLength(2)] public string DecimalSeparator { get; set; } = ".";
    [MaxLength(2)] public string ThousandsSeparator { get; set; } = ",";
    public bool IsActive { get; set; } = true;

    [Timestamp] public byte[]? RowVersion { get; set; }
}

public enum BankFileFormat { Csv = 1, Excel = 2 }
public enum BankAmountMode { SignedSingle = 1, DepositWithdrawalColumns = 2 }
public enum BankSignRule { PositiveIsDeposit = 1, PositiveIsWithdrawal = 2 }
