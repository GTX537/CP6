using System.Linq.Expressions;
using CP6.Core.EFDbContext;
using CP6.Entity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型仓储实现 - 所有实体通用的增删改查
/// 新建业务只需继承它，不用重复写 CRUD 代码
/// </summary>
public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _context;
    protected readonly DbSet<T> _dbSet;

    public RepositoryBase(CP6Context context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> FindAsync(Guid id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter,
        int page,
        int pageSize,
        string orderBy = "CreateDate desc")
    {
        IQueryable<T> query = _dbSet;

        // 如果有筛选条件，就加上
        if (filter != null)
        {
            query = query.Where(filter);
        }

        // 获取总数
        var total = await query.CountAsync();

        // 按创建时间倒序 + 分页
        var data = await query
            .OrderByDescending(x => x.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (data, total);
    }

    public async Task<T> AddAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T> UpdateAsync(T entity)
    {
        entity.ModifyDate = DateTime.Now;
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<int> DeleteAsync(params Guid[] ids)
    {
        var entities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync();
        _dbSet.RemoveRange(entities);
        return await _context.SaveChangesAsync();
    }
}
