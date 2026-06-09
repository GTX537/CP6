using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA100 FSC 製品化チェックシート発行サービス
/// </summary>
public interface IFscChecklistService
{
    /// <summary>多条件検索（仕様書 §4 — 7 テーブル JOIN）</summary>
    Task<(List<FscChecklistItemDto> Rows, int Total)> SearchAsync(FscChecklistQueryDto query);

    /// <summary>
    /// チェックシート発行（仕様書 §5）
    /// 1. FSC 管理 NO 採番（頭文字 + シーケンス）
    /// 2. 御見積書.FSCチェックシート発行日 = 本日
    /// 3. 御見積書見積計算書 (確定 FLG=1) に FSC 管理 NO 更新
    /// 4. Excel 帳票生成
    /// </summary>
    Task<FscIssueResultDto> IssueAsync(FscIssueRequestDto req, string? userName);

    /// <summary>発行済 Excel ダウンロード</summary>
    Task<(byte[] Content, string FileName)?> DownloadAsync(string fscManagementNo);

    /// <summary>web.config 出力フォーマット一覧</summary>
    IList<(string Name, string TemplatePath)> GetFormats();
}
