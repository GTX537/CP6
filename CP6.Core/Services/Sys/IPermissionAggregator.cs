namespace CP6.Core.Services.Sys;

/// <summary>
/// 权限聚合器 —— 把某用户全部角色的四粒度权限合并为一个 <see cref="UserPermissionContext"/>。
/// PUB 章01 §3/§9。每次读取当前租户的存储，不使用跟踪实体或授权缓存。
/// </summary>
public interface IPermissionAggregator
{
    /// <summary>聚合启用用户存在且启用的主/附加角色；菜单/操作并集、数据范围MAX、字段存行MIN。</summary>
    Task<UserPermissionContext> BuildAsync(Guid userId);

    /// <summary>Same read with cancellation propagated to the actual database commands.</summary>
    Task<UserPermissionContext> BuildAsync(Guid userId, CancellationToken cancellationToken);
}
