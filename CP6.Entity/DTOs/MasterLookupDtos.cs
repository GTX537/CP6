namespace CP6.Entity.DTOs;

/// <summary>
/// 共通 Master Popup（参照ダイアログ）レスポンス枠
/// </summary>
/// <typeparam name="T">行 DTO 型</typeparam>
/// <remarks>
/// MSBBPACOM 共通設計書 §項目定義「テ」(テーブル参照) パターンに対応する共通検索結果。
/// </remarks>
public class MasterLookupResult<T>
{
    public List<T> Rows { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>得意先ルックアップ行（Quotation/ProductMaster の CustomerCd を集約）</summary>
public class CustomerLookupDto
{
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    /// <summary>該当 顧客CD で登録された見積/製品の件数（参考表示）</summary>
    public int UsageCount { get; set; }
}

/// <summary>製品マスタルックアップ行（セット品検索 / 既存製品検索 共通）</summary>
public class ProductLookupDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string? SetProductName { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public int Status { get; set; }
    public DateTime? ModifyDate { get; set; }
}
