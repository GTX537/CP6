namespace CP6.Core.Services.Sys;

/// <summary>
/// 请求级当前用户权限上下文 —— PUB 章01 §8。
/// 经 IHttpContextAccessor 解析当前登录用户 → 命中缓存或重建（<see cref="IPermissionAggregator"/>）。
/// 后端三个强校验点（RequirePermission / DataScopeFilter / FieldMask）统一从这里取已合并结论。
/// </summary>
public interface ICurrentPermissionContext
{
    /// <summary>取当前请求用户的权限上下文（缓存命中或重建）。未登录 / 用户不存在抛异常。</summary>
    Task<UserPermissionContext> GetAsync();

    /// <summary>用户角色发生变更 → 失效该用户缓存（下次重建）。</summary>
    void Invalidate(Guid userId);

    /// <summary>角色权限发生变更 → 失效该角色的全部用户缓存。</summary>
    void InvalidateByRole(int roleId);
}
