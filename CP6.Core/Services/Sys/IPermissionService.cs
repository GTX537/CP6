namespace CP6.Core.Services.Sys;

/// <summary>
/// 功能权限查询 —— PUB 章02 §5。读当前用户已聚合的 <see cref="UserPermissionContext"/>，
/// 供 [RequirePermission] 特性 / 业务代码 / 前端 my-actions 判定。
/// </summary>
public interface IPermissionService
{
    /// <summary>当前用户是否可执行 menuKey 下的 action（命中 "menuKey:action"）。</summary>
    Task<bool> HasActionAsync(string menu, string action);

    /// <summary>当前用户是否可访问菜单 menuKey。</summary>
    Task<bool> HasMenuAsync(string menu);
}
