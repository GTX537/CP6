namespace CP6.Core.Services.Wms;

public interface ILpnService
{
    Task<PagedResult<LogisticsUnitDto>> GetAsync(
        string? warehouseCd,
        string? locationCd,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<LogisticsUnitDto?> GetOneAsync(string lpnNo, CancellationToken ct = default);
    Task<LogisticsUnitDto> CreateAsync(CreateLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LogisticsUnitDto> PackAsync(string lpnNo, PackLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LogisticsUnitDto> UnpackAsync(string lpnNo, UnpackLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LogisticsUnitDto> MoveAsync(string lpnNo, MoveLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LogisticsUnitDto> SplitAsync(string lpnNo, SplitLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LogisticsUnitDto> MergeAsync(string lpnNo, MergeLpnRequest request, string? userName, CancellationToken ct = default);
    Task<LpnPolicyRequest> UpsertPolicyAsync(LpnPolicyRequest request, string? userName, CancellationToken ct = default);
}
