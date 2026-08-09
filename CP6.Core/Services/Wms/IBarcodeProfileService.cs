namespace CP6.Core.Services.Wms;

public interface IBarcodeProfileService
{
    Task<IReadOnlyList<BarcodeProfileDto>> GetAsync(CancellationToken ct = default);
    Task<BarcodeProfileDto> UpsertAsync(UpsertBarcodeProfileRequest request, string? userName, CancellationToken ct = default);
    Task<CompoundBarcodeResult> ParseAsync(ParseCompoundBarcodeRequest request, CancellationToken ct = default);
}
