using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 全社統一採番カウンタ（永不重置・グローバル累計）
/// </summary>
/// <remarks>
/// 新採番形式：機能コード(3) + 年(4) + 月(2) + 自増(4) = 13 桁
///   例：EMC2026050001 / QTN2026050007 / PRD2026050003
/// 訂正・コピー時は ERP 主表のみ末尾に枝番 "-NN" を付与（見積計算書 / 御見積書）。
/// 本テーブルは機能コードごとに最終自増値のみ保持。年月で 0 戻ししない。
/// </remarks>
[Table("T_DocSequence")]
public class DocSequence : BaseEntity
{
    /// <summary>機能コード（3 桁、PK 相当の業務キー）：EMC/QTN/PRD/ORD/MLD/FSC ...</summary>
    [Required, MaxLength(3)]
    public string FuncCode { get; set; } = string.Empty;

    /// <summary>最終自増値（永不重置）</summary>
    public int LastSeq { get; set; } = 0;
}
