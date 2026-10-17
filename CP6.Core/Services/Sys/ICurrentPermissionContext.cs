namespace CP6.Core.Services.Sys;

/// <summary>
/// 请求级当前用户权限上下文 —— PUB 章01 §8。
/// 经 IHttpContextAccessor 绑定已认证用户GUID与租户，每次从存储重建（<see cref="IPermissionAggregator"/>）。
/// 后端三个强校验点（RequirePermission / DataScopeFilter / FieldMask）统一从这里取已合并结论。
/// </summary>
public interface ICurrentPermissionContext
{
    /// <summary>读取当前启用用户的有效权限。主体/租户无效或查询故障抛 CurrentPermissionUnavailableException。</summary>
    Task<UserPermissionContext> GetAsync();

    /// <summary>兼容登录入口：在当前数据库租户内按 userId 重读权限，不保存后续请求的授权缓存。</summary>
    Task<UserPermissionContext> PrewarmAsync(Guid userId);

    /// <summary>兼容旧写入者：移除历史用户缓存；GetAsync 不依赖调用此方法才能反映撤权。</summary>
    void Invalidate(Guid userId);

    /// <summary>兼容旧写入者：移除当前租户角色用户的历史缓存。</summary>
    void InvalidateByRole(int roleId);
}
