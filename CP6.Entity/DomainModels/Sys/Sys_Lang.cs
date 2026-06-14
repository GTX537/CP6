using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 多语言词条表，每行一个key，列对应各语言翻译
/// </summary>
public class Sys_Lang
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// 租户 ID。i18n 优化 P3：null = 全局默认值；非 null = 该租户对同一 key 的覆盖值。
    /// 解析按「租户覆盖 → 全局」回退。当前系统单租户，恒为 null（多租户上线后写入）。
    /// </summary>
    public int? TenantId { get; set; }

    /// <summary>
    /// 词条key，如 login.title、table.add
    /// </summary>
    [MaxLength(200)]
    [Required]
    public string LangKey { get; set; } = string.Empty;

    /// <summary>
    /// i18n 优化 P3：审校状态。draft=待审校，reviewed=已审校。种子/系统词条默认 reviewed，管理页手动新增默认 draft。
    /// 当前下发不按状态过滤（serve all），状态门控留 P5。
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "reviewed";

    /// <summary>i18n 优化 P3：最后修改人（审计）。</summary>
    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    /// <summary>i18n 优化 P3：最后修改时间（审计）。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 简体中文
    /// </summary>
    [MaxLength(500)]
    public string? ZhCN { get; set; }

    /// <summary>
    /// 繁体中文
    /// </summary>
    [MaxLength(500)]
    public string? ZhTW { get; set; }

    /// <summary>
    /// 英语
    /// </summary>
    [MaxLength(500)]
    public string? En { get; set; }

    /// <summary>
    /// 日语
    /// </summary>
    [MaxLength(500)]
    public string? Ja { get; set; }

    /// <summary>
    /// 韩语
    /// </summary>
    [MaxLength(500)]
    public string? Ko { get; set; }
}
