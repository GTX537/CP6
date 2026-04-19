using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 汎用マスタ（通用代码主数据）
/// MSBBPA010 中几乎所有"XX区分""XX CD"类下拉数据统一存这张表：
///   - 受注区分 / 親子区分 / FSC 区分 / 製品区分 / 物流区分 / 製品形状
///   - シート段 / 枚葉印刷 / 識別表示 / AD 形状
///   - 原紙 CD / 印刷 CD / エンボス CD / 工程 CD / 作業 CD / 単位
/// 用 GroupCode 区分不同的代码组，Code+GroupCode 组合唯一
/// </summary>
[Table("M_GenericCode")]
public class MasterGenericCode : BaseBizEntity
{
    /// <summary>代码组（如 M014=印刷，M038=工程，M044=阈值，M067=段成率，OrderType=受注区分…）</summary>
    [Required, MaxLength(20)]
    public string GroupCode { get; set; } = string.Empty;

    /// <summary>代码值</summary>
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>显示名称（日文或本地化）</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>附加字段 1（例：印刷マスタ的 インキ性質区分=0/1；原紙マスタ的段原紙区分）</summary>
    [MaxLength(100)]
    public string? Attr1 { get; set; }

    /// <summary>附加字段 2（例：FSC 区分；段成率百分比；坪量等）</summary>
    [MaxLength(100)]
    public string? Attr2 { get; set; }

    /// <summary>数値1（例：M044 据点阈值、M067 段成率）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Num1 { get; set; }

    /// <summary>数値2（备用）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Num2 { get; set; }

    public int SortOrder { get; set; }
}
