namespace CP6.Core.Services.Sys;

/// <summary>
/// 权限聚合器 —— 把某用户全部角色的四粒度权限合并为一个 <see cref="UserPermissionContext"/>。
/// PUB 章01 §3/§9。无缓存（缓存由 <c>ICurrentPermissionContext</c> 负责）。
/// </summary>
public interface IPermissionAggregator
{
    /// <summary>聚合指定用户的权限上下文（主角色 ∪ 附加角色；菜单/操作并集、数据/字段最宽）。</summary>
    Task<UserPermissionContext> BuildAsync(Guid userId);
}
