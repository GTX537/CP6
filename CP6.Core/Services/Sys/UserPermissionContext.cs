namespace CP6.Core.Services.Sys;

/// <summary>
/// 会话级权限聚合上下文 —— PUB 章01~04 四粒度的"已合并结论"。
/// 登录时由 <see cref="PermissionAggregator"/> 一次性把多角色的菜单/操作/数据/字段权限
/// 按「操作并集、数据最宽、字段最宽」合并并缓存；后端三个强校验点零额外查库直接读它。
/// </summary>
public class UserPermissionContext
{
    /// <summary>当前用户 Id（Sys_User.Id）</summary>
    public Guid UserId { get; set; }

    /// <summary>登录名（章03 数据权限"本人"范围 = Creator==UserName）</summary>
    public string? UserName { get; set; }

    /// <summary>所属部门（章03"本部门"范围）</summary>
    public Guid? DeptId { get; set; }

    /// <summary>所属部门物化路径（章03"本部门及下级"= 记录部门 Path 以此为前缀）</summary>
    public string DeptPath { get; set; } = "";

    /// <summary>全部角色 Id（主角色 ∪ 附加角色，去重）。int 键（B1-D1）</summary>
    public List<int> RoleIds { get; set; } = new();

    /// <summary>可访问菜单（稳定业务键 MenuKey 并集）</summary>
    public HashSet<string> MenuKeys { get; set; } = new();

    /// <summary>可执行操作（"menuKey:actionCode" 并集，章02）</summary>
    public HashSet<string> ActionKeys { get; set; } = new();

    /// <summary>数据范围：resourceKey → 最宽 ScopeType（章03，多角色取 MAX）</summary>
    public Dictionary<string, int> DataScopes { get; set; } = new();

    /// <summary>自定义部门数据范围：resourceKey → 部门 Id 并集（章03 ScopeType=4）</summary>
    public Dictionary<string, List<Guid>> CustomDeptIds { get; set; } = new();

    /// <summary>字段权限：resourceKey → (fieldName → 最宽 Access)（章04，多角色取 MIN=最可见）</summary>
    public Dictionary<string, Dictionary<string, int>> FieldPerms { get; set; } = new();
}
