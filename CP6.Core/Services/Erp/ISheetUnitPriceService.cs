using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA130 Web シート単価マスタ業務サービス契約
/// </summary>
public interface ISheetUnitPriceService
{
    /// <summary>参照検索：基準日 ≥ DB.改定日 の最新データを取得（取込区分で表切替）</summary>
    Task<List<SheetUnitPriceDto>> SearchAsync(SheetPriceQueryDto query);

    /// <summary>Excel 取込（7-step バリデーション）— 仕様書 §4</summary>
    Task<SheetPriceImportResultDto> ImportExcelAsync(
        Stream excelStream, DateTime baseDate, string baseCd, SheetPriceImportDiv importDiv);

    /// <summary>選択行のバッチ更新（UPSERT；改定日が PK と一致したら上書き）</summary>
    Task<int> BatchUpdateAsync(SheetPriceBatchUpdateDto request, string? userName);
}
