using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// FSC製品化チェックシート発行履歴（MSBBPA100）
/// </summary>
/// <remarks>
/// 仕様書 §第一部分 PA100 — 発行ごとに 1 行を記録（FSC 管理 NO 採番管理）
/// 業務 PK：FscManagementNo
/// 実体は「発行履歴 + ダウンロード元」として機能。御見積書側の FscManagementNo / FSCチェックシート発行日 は別更新。
/// </remarks>
[Table("T_FscChecklist")]
public class FscChecklist : BaseBizEntity
{
    /// <summary>FSC 管理 NO（PK；採番ルール：頭文字 + ORACLEシーケンス）</summary>
    [Required, MaxLength(20)]
    public string FscManagementNo { get; set; } = string.Empty;

    /// <summary>御見積書 NO</summary>
    [Required, MaxLength(20)]
    public string QtnNo { get; set; } = string.Empty;

    /// <summary>見積計算書 NO</summary>
    [Required, MaxLength(20)]
    public string QtnCalcNo { get; set; } = string.Empty;

    /// <summary>得意先 CD</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>担当者 CD</summary>
    [MaxLength(20)] public string? StaffCd { get; set; }

    /// <summary>拠点 CD</summary>
    [MaxLength(10)] public string? BaseCd { get; set; }

    /// <summary>発行日（=作成日）</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>出力フォーマット名（web.config 定義）</summary>
    [MaxLength(100)] public string? FormatName { get; set; }

    /// <summary>テンプレートパス（web.config 定義の右側）</summary>
    [MaxLength(500)] public string? TemplatePath { get; set; }

    /// <summary>生成された Excel ファイル名</summary>
    [MaxLength(200)] public string? ExcelFileName { get; set; }

    /// <summary>FSC 製品区分：1=社外 / 2=社内 / 3=マーク無し</summary>
    [MaxLength(1)] public string? FscProductDiv { get; set; }
}
