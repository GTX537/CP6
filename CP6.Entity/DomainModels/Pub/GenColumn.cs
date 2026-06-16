using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pub;

/// <summary>代码生成 — 列元数据 —— PUB 章08 §3。</summary>
[Table("Pub_GenColumn")]
public class GenColumn : BaseTenantEntity
{
    /// <summary>所属表 → GenTable.Id</summary>
    public Guid GenTableId { get; set; }

    /// <summary>属性名（如 DemoNo）</summary>
    [MaxLength(80)] public string Name { get; set; } = "";

    /// <summary>CLR 类型（string / int / decimal? / DateTime? / Guid?）</summary>
    [MaxLength(40)] public string ClrType { get; set; } = "string";

    /// <summary>显示名</summary>
    [MaxLength(80)] public string Label { get; set; } = "";

    /// <summary>是否必填</summary>
    public bool Required { get; set; }

    /// <summary>列表显示</summary>
    public bool InList { get; set; } = true;

    /// <summary>表单可填</summary>
    public bool InForm { get; set; } = true;

    /// <summary>排序</summary>
    public int Sort { get; set; }
}
