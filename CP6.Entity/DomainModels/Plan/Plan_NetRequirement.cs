using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Plan;

/// <summary>
/// 净需求明细（MRP P1 · spec §3.2）。逐 品目 × 日桶 净算的留痕：
/// 净需求 = 毛需求 − 库存 − 在途 − 在制 − 已确认计划订单 − 安全库存。供看板钻取"毛-供给-净"。
/// </summary>
[Table("Plan_NetRequirement")]
public class Plan_NetRequirement : BaseBizEntity
{
    /// <summary>所属运算批次（FK → Plan_MrpRun.Id）</summary>
    public Guid MrpRunId { get; set; }

    /// <summary>品目CD</summary>
    [Required, MaxLength(20)]
    public string ItemCd { get; set; } = string.Empty;

    /// <summary>日桶（需求归集的时间桶，P1 日级）</summary>
    public DateTime Bucket { get; set; }

    /// <summary>毛需求</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Gross { get; set; }

    /// <summary>现有库存</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal OnHand { get; set; }

    /// <summary>在途（采购未到）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal InTransit { get; set; }

    /// <summary>在制（已下达工单供给）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal InWip { get; set; }

    /// <summary>已确认/已转单计划订单供给（scheduled receipt）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal FirmPlanned { get; set; }

    /// <summary>安全库存</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal SafetyStock { get; set; }

    /// <summary>净需求 = max(0, Gross − OnHand − InTransit − InWip − FirmPlanned − SafetyStock)</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Net { get; set; }
}
