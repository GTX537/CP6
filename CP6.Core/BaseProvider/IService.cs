using CP6.Entity;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型服务接口
/// </summary>
public interface IService<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<object> GetPageListAsync(int page, int pageSize, string? keyword);
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<int> DeleteAsync(Guid[] ids);
}
