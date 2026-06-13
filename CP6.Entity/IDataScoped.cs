namespace CP6.Entity;

/// <summary>
/// 数据权限可过滤标记 —— PUB 章03。业务实体实现后即可被 IDataScopeFilter 注入范围过滤。
/// Creator（创建人 = 本人范围）来自 BaseEntity；DeptId（归属部门 = 本部门/及下级/自定义范围）需业务实体自补。
/// </summary>
public interface IDataScoped
{
    /// <summary>创建人（登录名）—— "本人" 范围比对。</summary>
    string? Creator { get; }

    /// <summary>归属部门 → Sys_Dept.Id —— "本部门 / 及下级 / 自定义" 范围比对。</summary>
    Guid? DeptId { get; }
}
