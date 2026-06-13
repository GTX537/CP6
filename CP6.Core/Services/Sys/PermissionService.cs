namespace CP6.Core.Services.Sys;

/// <summary>功能权限查询实现 —— PUB 章02。零额外查库（读会话级缓存的上下文）。</summary>
public class PermissionService : IPermissionService
{
    private readonly ICurrentPermissionContext _cur;
    public PermissionService(ICurrentPermissionContext cur) => _cur = cur;

    public async Task<bool> HasActionAsync(string menu, string action) =>
        (await _cur.GetAsync()).ActionKeys.Contains($"{menu}:{action}");

    public async Task<bool> HasMenuAsync(string menu) =>
        (await _cur.GetAsync()).MenuKeys.Contains(menu);
}
