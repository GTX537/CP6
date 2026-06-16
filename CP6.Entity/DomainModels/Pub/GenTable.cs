using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pub;

/// <summary>代码生成 — 表元数据 —— PUB 章08 §3。</summary>
[Table("Pub_GenTable")]
public class GenTable : BaseTenantEntity
{
    /// <summary>实体类名（如 Demo）</summary>
    [MaxLength(80)] public string EntityName { get; set; } = "";

    /// <summary>模块/命名空间段（如 Erp）</summary>
    [MaxLength(40)] public string Module { get; set; } = "";

    /// <summary>数据库表名（如 Erp_Demo）</summary>
    [MaxLength(120)] public string TableName { get; set; } = "";

    /// <summary>权限资源键（如 demo）</summary>
    [MaxLength(80)] public string ResourceKey { get; set; } = "";

    /// <summary>采番业务键（空=不自动采番）</summary>
    [MaxLength(50)] public string? SeqBizKey { get; set; }

    /// <summary>采番号写入字段名</summary>
    [MaxLength(80)] public string? CodeField { get; set; }

    /// <summary>REST 路由（如 api/erp/demo）</summary>
    [MaxLength(160)] public string RoutePath { get; set; } = "";

    /// <summary>菜单名（生成菜单脚本用）</summary>
    [MaxLength(80)] public string? MenuName { get; set; }
}
