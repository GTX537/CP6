using System.Linq.Expressions;

namespace CP6.Core.Services;

/// <summary>
/// 一覧画面用 服务端动态排序辅助。
///
/// 设计要点：
/// 1. 按属性名 + 升/降序动态生成 OrderBy 表达式，EF Core 可翻译成 SQL ORDER BY
///    （而非内存排序，保证跨分页正确）。
/// 2. 白名单（列名 → 实体属性路径）防止任意属性访问 / SQL 注入。
/// 3. sortField 缺失或不在白名单时，回退到各画面的默认排序。
/// 4. Element Plus el-table @sort-change 传来的 order 为 ascending/descending，
///    前端归一化为 asc/desc，本辅助两种写法都兼容。
/// </summary>
public static class QuerySort
{
    public static IQueryable<T> Apply<T>(
        IQueryable<T> source,
        string? sortField,
        string? sortOrder,
        IReadOnlyDictionary<string, string> allowed,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder)
    {
        if (!string.IsNullOrWhiteSpace(sortField) && allowed.TryGetValue(sortField, out var path))
            return ApplyOrder(source, path, IsDesc(sortOrder));
        return defaultOrder(source);
    }

    public static bool IsDesc(string? sortOrder) =>
        sortOrder != null &&
        (sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
         sortOrder.Equals("descending", StringComparison.OrdinalIgnoreCase));

    private static IOrderedQueryable<T> ApplyOrder<T>(IQueryable<T> source, string propertyPath, bool desc)
    {
        var param = Expression.Parameter(typeof(T), "x");
        Expression body = param;
        foreach (var member in propertyPath.Split('.'))
            body = Expression.PropertyOrField(body, member);

        var keySelector = Expression.Lambda(body, param);
        var method = desc ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(
            typeof(Queryable), method,
            new[] { typeof(T), body.Type },
            source.Expression, Expression.Quote(keySelector));

        return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
    }
}
