using CP6.Entity.DTOs.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>角色授权配置 —— PUB 章02（功能权限部分）。章03/04 的数据/字段授权另见后续阶段。</summary>
public interface IRolePermService
{
    /// <summary>取某菜单的操作点定义。</summary>
    Task<List<MenuActionDto>> GetMenuActionsAsync(int menuId);

    /// <summary>取全部菜单的操作点定义（含 MenuId）—— 配置页一次性加载。</summary>
    Task<List<MenuActionFullDto>> GetAllMenuActionsAsync();

    /// <summary>维护某菜单的操作点（按 ActionCode diff 增删改）。</summary>
    Task SaveMenuActionsAsync(int menuId, List<MenuActionDto> actions, string? @operator);

    /// <summary>取某角色的功能权限（菜单集合 + 操作点集合）。</summary>
    Task<RolePermDto> GetRolePermAsync(int roleId);

    /// <summary>保存某角色的功能权限（diff RoleMenu/RoleAction；操作点须 ⊆ 已授菜单 E-PUB-021；失效该角色缓存）。</summary>
    Task SaveRolePermAsync(int roleId, RolePermDto dto, string? @operator);

    /// <summary>当前登录用户的全部操作键（"menuKey:action"）—— 前端 my-actions。</summary>
    Task<List<string>> MyActionsAsync();

    // ───── 章03 数据权限配置 ─────

    /// <summary>取已注册的数据权限资源（配置页渲染）。</summary>
    List<DataScopeResourceDto> GetDataScopeResources();

    /// <summary>取某角色的数据范围配置。</summary>
    Task<List<RoleDataScopeDto>> GetRoleDataScopesAsync(int roleId);

    /// <summary>保存某角色的数据范围（全量替换）。范围须 ∈ supports(E-PUB-031)；自定义须选部门(E-PUB-032)；失效该角色缓存。</summary>
    Task SaveRoleDataScopesAsync(int roleId, List<RoleDataScopeDto> items, string? @operator);

    // ───── 章04 字段权限配置 ─────

    /// <summary>取已注册的字段权限资源及其可控字段（配置页渲染）。</summary>
    List<FieldResourceDto> GetFieldResources();

    /// <summary>取某角色的字段权限配置。</summary>
    Task<List<RoleFieldPermDto>> GetRoleFieldPermsAsync(int roleId);

    /// <summary>保存某角色的字段权限（全量替换；仅存非默认 Access≠1；字段须 ∈ 注册表 E-PUB-041；失效该角色缓存）。</summary>
    Task SaveRoleFieldPermsAsync(int roleId, List<RoleFieldPermDto> items, string? @operator);

    /// <summary>当前用户对某资源的只读字段（Access=2）—— 前端置灰输入。</summary>
    Task<List<string>> MyReadonlyAsync(string resourceKey);
}
