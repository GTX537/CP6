using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pub;

/// <summary>
/// 富采番规则 —— PUB 章05 §3。按业务键配置：前缀 + 日期段 + 流水补零 + 周期重置。
/// 与既有静态 <c>DocNumber</c> 并存（不破坏既有调用方）；新模块用 <c>ISeqService</c>。
/// </summary>
[Table("Pub_DocSequence")]
public class Pub_DocSequence : BaseTenantEntity
{
    /// <summary>业务键（如 PO / SO / WO），采番入口标识</summary>
    [MaxLength(50)]
    public string BizKey { get; set; } = "";

    /// <summary>号码前缀（如 PO）</summary>
    [MaxLength(20)]
    public string Prefix { get; set; } = "";

    /// <summary>日期段格式（如 yyyyMMdd；空=号码不含日期）</summary>
    [MaxLength(20)]
    public string? DateFormat { get; set; }

    /// <summary>流水补零位数</summary>
    public int SeqLength { get; set; } = 4;

    /// <summary>重置周期 0不重置 / 1每日 / 2每月 / 3每年</summary>
    public int ResetCycle { get; set; }

    /// <summary>上次采番的周期键（与当前不同则流水重置）</summary>
    [MaxLength(20)]
    public string? CurrentPeriod { get; set; }

    /// <summary>当前流水值</summary>
    public int CurrentValue { get; set; }
}
