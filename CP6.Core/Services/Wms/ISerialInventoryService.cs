namespace CP6.Core.Services.Wms;

public interface ISerialInventoryService
{
    Task<PagedResult<StockSerialDto>> GetAsync(
        string? productCd,
        string? serialNo,
        string? warehouseCd,
        string? locationCd,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<SerialOperationResult> EnableTrackingAsync(
        EnableSerialTrackingRequest request,
        string? userName,
        CancellationToken ct = default);
    Task<SerialOperationResult> PostAsync(
        SerialLifecycleRequest request,
        string? userName,
        CancellationToken ct = default);
}
