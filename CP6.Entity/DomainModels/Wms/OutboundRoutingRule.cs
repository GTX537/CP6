using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 出庫ルーティングルール（多倉庫引当 Gap 4.2 / T14）
/// </summary>
/// <remarks>
/// 得意先・製品（CD接頭辞＝品類）・出庫区分の条件にマッチした出庫指示を、
/// 指定の優先倉庫(<see cref="TargetWarehouseCd"/>)から優先的に引き当てるための設定。
///
/// 引当の倉庫候補順は 3 段階：
///   ① マッチしたルール（<see cref="SortOrder"/> 昇順）の <see cref="TargetWarehouseCd"/>
///   ② 残りの倉庫を <c>Warehouse.OutboundPriority</c> 昇順
///   ③ 各倉庫内で FEFO（期限→受入日→ロット）
///
/// 条件カラムは null = ワイルドカード（全件マッチ）。非 null の条件は AND で評価。
/// </remarks>
[Table("T_OutboundRoutingRule")]
public class OutboundRoutingRule : BaseBizEntity
{
    // PK は BaseEntity.Id（Guid）を継承。

    /// <summary>ルール名（管理画面表示用）</summary>
    [Required, MaxLength(100)]
    public string RuleName { get; set; } = string.Empty;

    /// <summary>評価順（昇順、小さいほど優先）。同条件の競合時に決定的な順序を与える</summary>
    public int SortOrder { get; set; } = 100;

    /// <summary>条件：得意先CD（null = 全得意先）</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>条件：製品CD接頭辞＝品類（null = 全製品。例 "M" は材料コード全般にマッチ）</summary>
    [MaxLength(20)] public string? ProductCdPrefix { get; set; }

    /// <summary>条件：出庫区分（null = 全区分。<see cref="OutboundType"/>）</summary>
    public int? OutboundType { get; set; }

    /// <summary>引当先の優先倉庫CD</summary>
    [Required, MaxLength(10)]
    public string TargetWarehouseCd { get; set; } = string.Empty;

    /// <summary>有効フラグ（false = 評価対象外）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
