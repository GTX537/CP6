namespace CP6.Entity.DTOs.Erp;

/// <summary>PA130 取込区分</summary>
public enum SheetPriceImportDiv
{
    /// <summary>シート単価 → T_SheetUnitPrice</summary>
    Standard = 1,
    /// <summary>シート単価(見積用) → T_SheetUnitPriceEstimate</summary>
    Estimate = 2,
}

/// <summary>PA130 シート単価 1 行（一覧表示・更新共通）</summary>
public class SheetUnitPriceDto
{
    public int RowNo { get; set; }
    /// <summary>選択(更新対象 CheckBox)</summary>
    public bool Selected { get; set; }

    public DateTime RevisionDate { get; set; }
    public string BaseCd { get; set; } = string.Empty;
    public string? BaseName { get; set; }
    public string? SalesStaffCd { get; set; }
    public string? SalesStaffName { get; set; }
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string SheetFlute { get; set; } = string.Empty;
    public string PaperCdF { get; set; } = string.Empty;
    public string PrintCdF { get; set; } = string.Empty;
    public string EmbossCdF { get; set; } = string.Empty;
    public string PaperCdC { get; set; } = string.Empty;
    public string PrintCdC { get; set; } = string.Empty;
    public string EmbossCdC { get; set; } = string.Empty;
    public string PaperCdB { get; set; } = string.Empty;
    public string PrintCdB { get; set; } = string.Empty;
    public string EmbossCdB { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    /// <summary>取込時のソース行番号（バリデーションエラー報告用）</summary>
    public int? SourceLine { get; set; }
}

/// <summary>PA130 取込結果</summary>
public class SheetPriceImportResultDto
{
    public bool Ok { get; set; }
    public List<SheetUnitPriceDto> Rows { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int? ErrorStep { get; set; }       // 1〜7（エラー発生 step）
    /// <summary>同一データ重複の有無（フロントで上書き確認ダイアログ表示）</summary>
    public bool HasDuplicates { get; set; }
}

/// <summary>PA130 一括更新リクエスト</summary>
public class SheetPriceBatchUpdateDto
{
    public SheetPriceImportDiv ImportDiv { get; set; }
    public List<SheetUnitPriceDto> Rows { get; set; } = new();
}

public class SheetPriceQueryDto
{
    public DateTime BaseDate { get; set; }
    public string BaseCd { get; set; } = string.Empty;
    public SheetPriceImportDiv ImportDiv { get; set; } = SheetPriceImportDiv.Standard;
    public string? CustomerCd { get; set; }
    public string? SalesStaffCd { get; set; }
}
