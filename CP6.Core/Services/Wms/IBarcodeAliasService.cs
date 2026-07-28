namespace CP6.Core.Services.Wms;

public interface IBarcodeAliasService
{
    Task<PagedResult<BarcodeAliasDto>> GetAsync(
        string? search,
        string? barcodeType,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<BarcodeAliasDto> UpsertAsync(
        UpsertBarcodeAliasRequest request,
        string? userName,
        CancellationToken ct = default);
    Task<BarcodeImportResult> ImportAsync(
        Stream workbook,
        bool commit,
        string? userName,
        CancellationToken ct = default);
}
