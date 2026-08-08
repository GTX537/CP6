using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程定义（OA 章03 §3）。SchemaJson = 节点+边有向图，驱动流程引擎状态机。
/// 绑定一张表单（FormKey）。改版只升 Version，在途实例按其快照运行（阶段1 简化：实例不存 schema 快照，按 FlowKey 取最新——单租户手配场景可接受，多版本并行留扩展）。
/// </summary>
// [审计纳入] 流程定义＝设计期治理配置（SchemaJson 节点/边有向图、FlowKey 绑定、版本）。高危键 oa-designer:*
// 的落库对象，schema/启停变更影响所有在途流程走向——须字段级留痕谁在何时改了流程结构。贴 IAuditable。
[Table("Wf_FlowDef")]
public class Wf_FlowDef : BaseTenantEntity, IAuditable
{
    /// <summary>流程稳定业务键（本阶段全局唯一）。建后不可改</summary>
    [Required, MaxLength(100)]
    public string FlowKey { get; set; } = string.Empty;

    /// <summary>流程名称</summary>
    [Required, MaxLength(200)]
    public string FlowName { get; set; } = string.Empty;

    /// <summary>绑定的表单定义 FormKey</summary>
    [MaxLength(100)]
    public string? FormKey { get; set; }

    /// <summary>流程 schema（节点/边 JSON）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string SchemaJson { get; set; } = "{}";

    /// <summary>版本号（schema 变更自增）</summary>
    public int Version { get; set; } = 1;

    /// <summary>是否启用</summary>
    public bool Enable { get; set; } = true;

    /// <summary>功能码（MSBBPA010 式程序/功能标准码，设计器组织主键，umbrella §2.7）。租户内唯一（可空过渡）。</summary>
    [MaxLength(50)] public string? FunctionId { get; set; }

    /// <summary>流程编号（Delta 式 2887/2889 人面编号，umbrella §2.7）。租户内唯一（可空过渡）。</summary>
    [MaxLength(50)] public string? FlowCode { get; set; }
}
