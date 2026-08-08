namespace CP6.Core.Services.Wms;

public sealed class WmsRoleScopeDto
{
    public int RoleId { get; init; }
    public string WarehouseCd { get; init; } = string.Empty;
    public string? AreaCd { get; init; }
}
public sealed class ReplaceWmsRoleScopesRequest
{
    public IReadOnlyList<WmsRoleScopeItem> Scopes { get; set; } =
        Array.Empty<WmsRoleScopeItem>();
}

public sealed class WmsRoleScopeItem
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string? AreaCd { get; set; }
}

public interface IWmsRoleScopeService
{
    Task<IReadOnlyList<WmsRoleScopeDto>> GetAsync(
        int roleId,
        CancellationToken ct = default);

    Task<IReadOnlyList<WmsRoleScopeDto>> ReplaceAsync(
        int roleId,
        ReplaceWmsRoleScopesRequest request,
        string? userName,
        CancellationToken ct = default);
}
