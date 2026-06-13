using CP6.Entity;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 数据权限查询注入 —— PUB 章03 §5。把当前用户对某资源的数据范围翻译为 IQueryable 过滤。
/// 业务查询：<c>filter.Apply(_db.Orders, "order", ctx)</c>。
/// </summary>
public interface IDataScopeFilter
{
    /// <summary>按 ctx 对 resource 的数据范围过滤 query。</summary>
    IQueryable<T> Apply<T>(IQueryable<T> query, string resource, UserPermissionContext ctx) where T : class, IDataScoped;
}
