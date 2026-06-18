using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>工作中心主数据（A2 · spec §3.2）。费率与产能挂载点；产能字段=CRP 地基。</summary>
[Table("T_WorkCenter")]
public class WorkCenter : BaseBizEntity
{
    /// <summary>工作中心CD（业务键，唯一；= ProductProcess.WgCd / WorkOrderProcess.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>工作中心名称</summary>
    [MaxLength(100)] public string? WgName { get; set; }
    /// <summary>日可用产能（h/日）——CRP 入参地基，A2 只维护不消费</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? DailyCapacityHours { get; set; }
    /// <summary>启用</summary>
    public bool Enable { get; set; } = true;
}
