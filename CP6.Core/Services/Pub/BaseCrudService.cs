using CP6.Core.Services.Sys;
using CP6.Entity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>
/// 通用 CRUD 泛型基座 —— PUB 章08 §2。把 PUB 四粒度/采番/部门归属固化进基类：
/// 查询自动注入数据权限、创建自动采番 + 部门归属、更新自动拒写只读/隐藏字段。
/// 子类只需给出 <see cref="ResourceKey"/>（及可选 <see cref="SeqBizKey"/>/<see cref="CodeField"/>）。
/// 依赖 DbContext 基类以便测试（生产子类传 CP6Context）。
/// </summary>
public abstract class BaseCrudService<T> where T : BaseEntity, IDataScoped
{
    protected readonly DbContext Db;
    protected readonly IDataScopeFilter Scope;
    protected readonly IFieldPermService FieldPerm;
    protected readonly ICurrentPermissionContext Current;
    protected readonly ISeqService Seq;

    protected BaseCrudService(DbContext db, IDataScopeFilter scope, IFieldPermService fieldPerm,
        ICurrentPermissionContext current, ISeqService seq)
    {
        Db = db;
        Scope = scope;
        FieldPerm = fieldPerm;
        Current = current;
        Seq = seq;
    }

    /// <summary>数据/字段权限资源键（如 order）。</summary>
    protected abstract string ResourceKey { get; }

    /// <summary>采番业务键；null = 不自动采番。</summary>
    protected virtual string? SeqBizKey => null;

    /// <summary>采番号写入的属性名；与 SeqBizKey 同时非空才采番。</summary>
    protected virtual string? CodeField => null;

    /// <summary>分页查询（自动注入当前用户数据范围）。</summary>
    public async Task<(List<T> rows, int total)> QueryAsync(int page, int pageSize)
    {
        var ctx = await Current.GetAsync();
        var q = Scope.Apply(Db.Set<T>().AsQueryable(), ResourceKey, ctx);
        var total = await q.CountAsync();
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (rows, total);
    }

    /// <summary>创建（自动采番 + 部门归属 + 创建人）。</summary>
    public async Task<T> CreateAsync(T entity)
    {
        var ctx = await Current.GetAsync();

        if (!string.IsNullOrEmpty(SeqBizKey) && !string.IsNullOrEmpty(CodeField))
        {
            var no = await Seq.NextAsync(SeqBizKey);
            typeof(T).GetProperty(CodeField)?.SetValue(entity, no);
        }

        var deptProp = typeof(T).GetProperty(nameof(IDataScoped.DeptId));
        if (deptProp != null && deptProp.CanWrite && deptProp.GetValue(entity) == null)
            deptProp.SetValue(entity, ctx.DeptId);   // 部门归属默认本部门

        entity.Creator ??= ctx.UserName;
        entity.CreateDate = DateTime.Now;
        Db.Set<T>().Add(entity);
        await Db.SaveChangesAsync();
        return entity;
    }

    /// <summary>更新（自动拒写只读/隐藏字段，保留审计/创建信息）。</summary>
    public async Task<T?> UpdateAsync(T incoming)
    {
        var ctx = await Current.GetAsync();
        var original = await Db.Set<T>().FindAsync(incoming.Id);
        if (original == null) return null;

        FieldPerm.StripReadOnly(incoming, original, ResourceKey, ctx);   // 受限字段用原值覆盖 incoming

        var creator = original.Creator;
        var created = original.CreateDate;
        Db.Entry(original).CurrentValues.SetValues(incoming);
        original.Creator = creator;            // 保留创建信息
        original.CreateDate = created;
        original.Modifier = ctx.UserName;
        original.ModifyDate = DateTime.Now;
        await Db.SaveChangesAsync();
        return original;
    }

    /// <summary>批量删除。</summary>
    public async Task<int> DeleteAsync(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        var rows = await Db.Set<T>().Where(x => set.Contains(x.Id)).ToListAsync();
        Db.Set<T>().RemoveRange(rows);
        return await Db.SaveChangesAsync();
    }
}
