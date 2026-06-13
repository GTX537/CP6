namespace CP6.Entity.DTOs.Sys;

/// <summary>用户角色分配（PUB 章01）。GET 返回 + PUT 请求复用。</summary>
public class UserRolesDto
{
    /// <summary>全部角色 Id（含主角色）</summary>
    public List<int> RoleIds { get; set; } = new();

    /// <summary>主角色 Id（默认/显示用，必须 ∈ RoleIds）</summary>
    public int? PrimaryRoleId { get; set; }
}
