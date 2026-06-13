namespace CP6.Entity.DTOs.Sys;

/// <summary>菜单操作点（PUB 章02）。GET 返回 + PUT 维护复用。</summary>
public class MenuActionDto
{
    public string ActionCode { get; set; } = "";
    public string ActionName { get; set; } = "";
    public int Sort { get; set; }
}

/// <summary>菜单操作点（含 MenuId）—— 批量取全部操作点定义供配置页。</summary>
public class MenuActionFullDto
{
    public int MenuId { get; set; }
    public string ActionCode { get; set; } = "";
    public string ActionName { get; set; } = "";
    public int Sort { get; set; }
}

/// <summary>角色授权一条操作点（菜单 + 操作码）。</summary>
public class RoleActionItem
{
    public int MenuId { get; set; }
    public string ActionCode { get; set; } = "";
}

/// <summary>角色功能权限（菜单集合 + 操作点集合）。GET 返回 + PUT 请求复用。</summary>
public class RolePermDto
{
    public List<int> MenuIds { get; set; } = new();
    public List<RoleActionItem> Actions { get; set; } = new();
}
