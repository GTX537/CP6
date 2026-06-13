using CP6.Core.EFDbContext;
using CP6.Entity;

namespace CP6.Core.Services.Sys;

/// <summary>数据权限查询注入实现 —— PUB 章03 §5。五范围：本人/本部门/及下级/自定义/全部。</summary>
public class DataScopeFilter : IDataScopeFilter
{
    private readonly CP6Context _db;
    public DataScopeFilter(CP6Context db) => _db = db;

    public IQueryable<T> Apply<T>(IQueryable<T> query, string resource, UserPermissionContext ctx)
        where T : class, IDataScoped
    {
        var scope = ctx.DataScopes.TryGetValue(resource, out var s) ? s : DataScopeRegistry.DefaultScope(resource);
        switch (scope)
        {
            case 5:   // 全部
                return query;

            case 2:   // 本部门
                return query.Where(x => x.DeptId == ctx.DeptId);

            case 3:   // 本部门及下级（记录部门 Path 以 ctx.DeptPath 为前缀）
                var path = ctx.DeptPath;
                return query.Where(x => x.DeptId != null &&
                    _db.Sys_Depts.Any(d => d.Id == x.DeptId && d.Path.StartsWith(path)));

            case 4:   // 自定义部门
                var ids = ctx.CustomDeptIds.TryGetValue(resource, out var l) ? l : new List<Guid>();
                return query.Where(x => x.DeptId != null && ids.Contains(x.DeptId.Value));

            case 1:   // 本人
            default:  // 未配置/未知 → 本人（最严）
                return query.Where(x => x.Creator == ctx.UserName);
        }
    }
}
