using CP6.Entity;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型服务基类 - 封装通用业务逻辑
/// 具体业务可以重写方法来加自定义逻辑
/// </summary>
public class ServiceBase<T> : IService<T> where T : BaseEntity
{
    protected readonly IRepository<T> _repository;

    public ServiceBase(IRepository<T> repository)
    {
        _repository = repository;
    }

    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await _repository.FindAsync(id);
    }

    public virtual async Task<object> GetPageListAsync(int page, int pageSize, string? keyword)
    {
        // 默认不带筛选，子类可以重写来加搜索逻辑
        var (data, total) = await _repository.GetPageListAsync(null, page, pageSize);
        return new { rows = data, total };
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        entity.CreateDate = DateTime.Now;
        return await _repository.AddAsync(entity);
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        return await _repository.UpdateAsync(entity);
    }

    public virtual async Task<int> DeleteAsync(Guid[] ids)
    {
        return await _repository.DeleteAsync(ids);
    }
}
