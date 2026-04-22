using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 御見積書見積計算書（中间表） - MSBBPA030 §2.9.2
/// </summary>
/// <remarks>
/// 关联 Quotation ⇄ EstimateCalc（M:N）。
/// 每行保存一次「使用」勾选，并承载 マスタ確定FLG 以支持行级的確定登録/取消。
/// </remarks>
[Table("T_QuotationCalc")]
public class QuotationCalc : BaseBizEntity
{
    /// <summary>御見積書NO（复合主键一部分；FK → T_Quotation）</summary>
    [Required]
    [MaxLength(11)]
    public string QtnNo { get; set; } = string.Empty;

    /// <summary>見積計算書NO（复合主键一部分；FK → T_EstimateCalc）</summary>
    [Required]
    [MaxLength(11)]
    public string QtnCalcNo { get; set; } = string.Empty;

    /// <summary>見積確認FLG：0/9</summary>
    public int EstimateCheckFlg { get; set; } = 0;

    /// <summary>見積確認日</summary>
    public DateTime? EstimateCheckDate { get; set; }

    /// <summary>マスタ確定FLG：0/1/9</summary>
    public int MasterConfirmFlg { get; set; } = 0;

    /// <summary>マスタ確定日</summary>
    public DateTime? MasterConfirmDate { get; set; }
}
