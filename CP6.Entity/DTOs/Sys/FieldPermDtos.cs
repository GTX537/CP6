namespace CP6.Entity.DTOs.Sys;

/// <summary>可控字段定义（注册表项）。</summary>
public class FieldDefDto
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>字段权限资源（资源键 + 其可控字段）—— 配置页渲染。</summary>
public class FieldResourceDto
{
    public string Key { get; set; } = "";
    public List<FieldDefDto> Fields { get; set; } = new();
}

/// <summary>角色字段权限（GET 返回 + PUT 请求复用）。Access 1可读写/2只读/3隐藏。</summary>
public class RoleFieldPermDto
{
    public string ResourceKey { get; set; } = "";
    public string FieldName { get; set; } = "";
    public int Access { get; set; }
}
