namespace CP6.Entity.DTOs.Sys;

/// <summary>部门 CRUD 请求 DTO（PUB 章00）</summary>
public class DeptDto
{
    public Guid? Id { get; set; }
    public Guid? ParentId { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public Guid? LeaderId { get; set; }
    public int Sort { get; set; }
    public bool Enable { get; set; } = true;
}

/// <summary>部门树节点（含负责人名，供前端 el-tree）</summary>
public class DeptTreeNode
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public Guid? LeaderId { get; set; }
    public string? LeaderName { get; set; }
    public int Sort { get; set; }
    public bool Enable { get; set; }
    public List<DeptTreeNode> Children { get; set; } = new();
}

/// <summary>用户组织字段维护（DeptId/ManagerId/Email）</summary>
public class UserOrgDto
{
    public Guid? DeptId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Email { get; set; }
}
