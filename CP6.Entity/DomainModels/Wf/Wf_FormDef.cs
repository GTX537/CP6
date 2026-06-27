using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 表单定义（OA 章02）。SchemaJson 驱动前端动态渲染 + 后端 required/类型复核。
/// JSON 列直存 nvarchar(max)，不用 EAV（OA-D4 / 08 章）。继承 BaseEntity，Enable 软停用，
/// 本阶段不带 TenantId（OA 阶段4 多租户统一加）。
/// </summary>
[Table("Wf_FormDef")]
public class Wf_FormDef : BaseTenantEntity
{
    /// <summary>表单稳定业务键（本阶段全局唯一；多租户后改租户内唯一）。建后不可改</summary>
    [Required, MaxLength(100)]
    public string FormKey { get; set; } = string.Empty;

    /// <summary>表单名称</summary>
    [Required, MaxLength(200)]
    public string FormName { get; set; } = string.Empty;

    /// <summary>表单 schema（字段定义 JSON）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string SchemaJson { get; set; } = "{}";

    /// <summary>版本号（schema 变更自增；旧 FormData 留痕不受影响）</summary>
    public int Version { get; set; } = 1;

    /// <summary>是否启用（软停用）</summary>
    public bool Enable { get; set; } = true;

    /// <summary>填單分类（机能大类，umbrella §2.5）。</summary>
    [MaxLength(100)] public string? Category { get; set; }

    /// <summary>填單子分类。</summary>
    [MaxLength(100)] public string? SubCategory { get; set; }
}
