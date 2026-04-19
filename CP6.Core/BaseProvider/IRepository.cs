using System.Linq.Expressions;
using CP6.Entity;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型仓储接口 - 定义所有实体通用的数据库操作
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>
    /// 根据 Id 查询单条
    /// </summary>
    Task<T?> FindAsync(Guid id);

    /// <summary>
    /// 分页查询
    /// </summary>
    Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter,
        int page,
        int pageSize,
        string orderBy = "CreateDate desc");

    /// <summary>
    /// 新增
    /// </summary>
    Task<T> AddAsync(T entity);

    /// <summary>
    /// 修改
    /// </summary>
    Task<T> UpdateAsync(T entity);

    /// <summary>
    /// 删除（支持批量）
    /// </summary>
    Task<int> DeleteAsync(params Guid[] ids);
}
