namespace CP6.Core.Services.Sys;

/// <summary>功能权限查询实现 —— 每次读取当前存储；无法确定当前权限时拒绝。</summary>
public class PermissionService : IPermissionService
{
    private readonly ICurrentPermissionContext _cur;
    public PermissionService(ICurrentPermissionContext cur) => _cur = cur;

    public async Task<bool> HasActionAsync(string menu, string action)
    {
        try { return (await _cur.GetAsync()).ActionKeys.Contains($"{menu}:{action}"); }
        catch (CurrentPermissionUnavailableException) { return false; }
    }

    public async Task<bool> HasMenuAsync(string menu)
    {
        try { return (await _cur.GetAsync()).MenuKeys.Contains(menu); }
        catch (CurrentPermissionUnavailableException) { return false; }
    }
}
