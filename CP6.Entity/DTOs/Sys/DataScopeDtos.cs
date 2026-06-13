namespace CP6.Entity.DTOs.Sys;

/// <summary>数据权限资源（注册表项）—— 配置页渲染用。</summary>
public class DataScopeResourceDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int[] Supports { get; set; } = Array.Empty<int>();
    public int Default { get; set; }
}

/// <summary>角色对某资源的数据范围（GET 返回 + PUT 请求复用）。CustomDeptIds 用 Guid 列表（前端友好）。</summary>
public class RoleDataScopeDto
{
    public string ResourceKey { get; set; } = "";
    public int ScopeType { get; set; }
    public List<Guid> CustomDeptIds { get; set; } = new();
}
