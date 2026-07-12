using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 每关卡表单快照（WFS 读模型）。每到一关 / 每次办结存一份当时表单字段值（不可变留痕）。
/// 区别 Wf_FormData（整单最新）/ VarsJson（流程变量，会被覆盖）：本表按 StepSeq 串"每步变化轨迹"。
/// </summary>
// [审计豁免] 每关卡不可变表单快照：按 StepSeq 追加"每步变化轨迹"留痕(建后不改)，运行时读模型非治理配置。
// 不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_FlowData")]
public class Wf_FlowData : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public int StepSeq { get; set; }
    [MaxLength(100)] public string NodeId { get; set; } = string.Empty;
    [Column(TypeName = "nvarchar(max)")] public string DataJson { get; set; } = "{}";
}
