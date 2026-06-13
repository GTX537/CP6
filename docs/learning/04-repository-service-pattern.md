# 04 · 通用仓储 + Service 模板

## 📍 学习目标

1. Generic Repository 是不是"反模式"？CP6 这么写为什么没翻车？
2. `RepositoryBase<T>` 和 `ServiceBase<T>` 各自承担什么？
3. 子类要 override 的边界在哪里？
4. 什么时候不走 `IRepository`、直接写 LINQ？

---

## 🔎 真实代码切片

### `IRepository<T>` 只暴露 5 个方法

```csharp
// CP6.Core/BaseProvider/IRepository.cs
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> FindAsync(Guid id);
    Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter,
        int page,
        int pageSize,
        string orderBy = "CreateDate desc");
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<int> DeleteAsync(params Guid[] ids);
}
```

### `RepositoryBase<T>` 的实现

```csharp
// CP6.Core/BaseProvider/RepositoryBase.cs
public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _context;
    protected readonly DbSet<T> _dbSet;

    public RepositoryBase(CP6Context context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> FindAsync(Guid id) => await _dbSet.FindAsync(id);

    public async Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter, int page, int pageSize, string orderBy = "CreateDate desc")
    {
        IQueryable<T> query = _dbSet;
        if (filter != null) query = query.Where(filter);
        var total = await query.CountAsync();
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
```

### Program.cs 的开放泛型注册

```csharp
// 一行注册所有 entity 的 IRepository<>
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
```

### Service 怎么用：以业务 Service 为例

```csharp
public class BusinessPartnerService : IBusinessPartnerService
{
    private readonly IRepository<BusinessPartner> _repo;
    private readonly CP6Context _context;

    public BusinessPartnerService(IRepository<BusinessPartner> repo, CP6Context ctx)
    {
        _repo = repo;
        _context = ctx;
    }

    // 简单 CRUD —— 直接走 _repo
    public Task<BusinessPartner?> GetAsync(Guid id) => _repo.FindAsync(id);

    // 复杂查询 —— 跳过 _repo，直接 LINQ
    public async Task<List<BusinessPartner>> SearchByKeyword(string keyword)
    {
        return await _context.BusinessPartners
            .Where(p => p.PartnerCd.Contains(keyword) || p.PartnerName.Contains(keyword))
            .OrderBy(p => p.PartnerCd)
            .Take(50)
            .AsNoTracking()
            .ToListAsync();
    }
}
```

---

## 💡 资深视角

### "Generic Repository 是反模式"对吗？

这是个老争论。**反对方**（Greg Young, Ayende）的核心论点：

1. EF Core 的 `DbContext + DbSet<T>` 本身就是 Repository + UnitOfWork。再封一层是脱裤子放屁。
2. 通用仓储泄漏 `IQueryable<T>` 后，Service 层可以写出任意复杂查询，等于没封装。
3. 跨实体的业务逻辑（如订单+库存联动）放在哪？通用仓储装不下。

**支持方**的反驳：

1. 团队水平参差，给个统一入口能堵掉 90% 的低级查询。
2. 切换 ORM 时有缓冲层（虽然实际换 ORM 概率极低）。
3. 单测时 mock 仓储比 mock DbContext 干净。

**CP6 的取舍**：

- ✅ 提供 `IRepository<T>` 但**只 5 个方法**，不暴露 `IQueryable<T>`。
- ✅ 简单 CRUD 走仓储，复杂查询直接写 LINQ on `_context.DbSet`。
- ✅ Service 持有 `IRepository<T>` 和 `CP6Context` 两者，按需选用。

这是个**实用主义混合方案**：保留通用仓储的"统一审计 + 默认排序"好处，但不强迫所有查询走它。

### 子类 override 的边界

CP6 的 `ServiceBase<T>` 用法是：

```csharp
// 假设有这个抽象（CP6 实际是按域单写，没强制 ServiceBase）
public class ProductService : IProductService
{
    private readonly IRepository<Product> _repo;
    // 简单 CRUD 转发给 _repo
    public Task<Product?> GetAsync(Guid id) => _repo.FindAsync(id);

    // 复杂逻辑（如校验 + 联动）放在 Service 里
    public async Task<Product> CreateAsync(ProductCreateDto dto)
    {
        // 1. 校验
        if (await _context.Products.AnyAsync(p => p.ProductCd == dto.ProductCd))
            throw new InvalidOperationException("製品コード重複");

        // 2. 业务规则
        var product = new Product { /* ... */ };

        // 3. 触发跨模块
        await _wmsBridge.OnProductCreatedAsync(product);

        return await _repo.AddAsync(product);
    }
}
```

**模板**：
- **委托给仓储**：纯增删改查
- **不委托**：需要预先校验、需要触发副作用（Bridge Hook、SignalR 推送）、需要跨多 entity

### 为什么 CP6 没有"通用 Service 基类"

仔细看 CP6 的 Service 类，并没有 `ServiceBase<T>`。原因：

- 业务 Service 的方法签名差异巨大（`OrderService.CancelAsync` vs `WarehouseService.CreateAsync`）
- 通用 ServiceBase 只能提供与仓储重复的 CRUD，价值低
- 强加基类会让每个 Service 多一层无意义继承

**反模式**：

```csharp
// 不要这样
public abstract class ServiceBase<T> where T : BaseBizEntity
{
    public virtual Task<T?> GetAsync(Guid id) => _repo.FindAsync(id);
    public virtual Task<T> CreateAsync(T entity) => _repo.AddAsync(entity);
    // ... 子类几乎都要 override，等于没用
}
```

CP6 的处理是：每个业务域写自己的 `I*Service` + `*Service`，简单业务转发给 `IRepository<T>`，复杂业务自由发挥。

### 开放泛型注册的威力

```csharp
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
```

这一行替代了几十行手动注册：

```csharp
// ❌ 如果不用开放泛型
builder.Services.AddScoped<IRepository<Order>, RepositoryBase<Order>>();
builder.Services.AddScoped<IRepository<OrderDetail>, RepositoryBase<OrderDetail>>();
builder.Services.AddScoped<IRepository<Product>, RepositoryBase<Product>>();
// ... 几十个 entity 就要几十行
```

DI 容器在第一次请求 `IRepository<Order>` 时，自动闭合泛型构造 `RepositoryBase<Order>` 并注入 `CP6Context`。

### 怎么扩展（场景：加缓存）

```csharp
public class CachedRepository<T> : IRepository<T> where T : BaseEntity
{
    private readonly IRepository<T> _inner;   // 包装原仓储
    private readonly CacheService _cache;

    public async Task<T?> FindAsync(Guid id)
    {
        return await _cache.GetOrSetAsync(
            key: $"{typeof(T).Name}:{id}",
            factory: () => _inner.FindAsync(id),
            expiration: TimeSpan.FromMinutes(5));
    }

    // 写入时让缓存失效
    public async Task<T> UpdateAsync(T entity)
    {
        var result = await _inner.UpdateAsync(entity);
        await _cache.RemoveAsync($"{typeof(T).Name}:{entity.Id}");
        return result;
    }
    // ...
}

// 注册时用 Decorator 模式
builder.Services.AddScoped<IRepository<Product>>(sp =>
    new CachedRepository<Product>(
        new RepositoryBase<Product>(sp.GetRequiredService<CP6Context>()),
        sp.GetRequiredService<CacheService>()));
```

这是 **Decorator Pattern**。CP6 当前没用，但当你说"想给某几个高频读 entity 加缓存"时，可以这样改而不动 Service 代码。

---

## ⚠️ 踩坑记录

### 坑 1：`UpdateAsync` 的 ChangeTracker 陷阱

```csharp
public async Task<T> UpdateAsync(T entity)
{
    entity.ModifyDate = DateTime.Now;
    _context.Entry(entity).State = EntityState.Modified;   // 标记整个 entity 为 dirty
    await _context.SaveChangesAsync();
    return entity;
}
```

**问题**：`State = Modified` 会把 entity 的**所有列**都 update，包括没改的。如果两个请求并发：

```
T0  A 查到 order = { Price=100, Status="NEW" }
T1  B 查到 order = { Price=100, Status="NEW" }
T2  A 改 Status="CONFIRMED", UpdateAsync → DB: Status=CONFIRMED, Price=100
T3  B 改 Price=200, UpdateAsync → DB: Price=200, Status="NEW"   ← 把 A 的 Status 改写回来了
```

**修复**：
- 加乐观锁 `[Timestamp]`（CP6 的 BaseBizEntity 有了）
- 或改用 `_context.Entry(entity).Property(e => e.Status).IsModified = true` 精细控制

### 坑 2：`DeleteAsync` 不是软删除

CP6 的 `RepositoryBase.DeleteAsync` 是物理删除：

```csharp
public async Task<int> DeleteAsync(params Guid[] ids)
{
    var entities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync();
    _dbSet.RemoveRange(entities);
    return await _context.SaveChangesAsync();
}
```

但 `BaseBizEntity.IsDeleted` 字段存在 → 说明业务想用软删除。**矛盾**。

**解决**：业务 Service 里覆盖删除逻辑：

```csharp
public async Task SoftDeleteAsync(Guid id, string user)
{
    var entity = await _repo.FindAsync(id);
    entity.IsDeleted = true;
    entity.Modifier = user;
    await _repo.UpdateAsync(entity);
}
```

并在所有查询里手动 `Where(x => !x.IsDeleted)`，或用 EF Core 的 `HasQueryFilter`（全局过滤）。

### 坑 3：分页 OrderBy 参数被忽略

```csharp
public async Task<...> GetPageListAsync(..., string orderBy = "CreateDate desc")
{
    // ❌ 看实现：orderBy 参数完全没用，写死了 CreateDate
    var data = await query.OrderByDescending(x => x.CreateDate)...
}
```

CP6 的 `RepositoryBase` 这里有个**已知设计缺陷**：暴露了 orderBy string 参数但没实现。如果需要动态排序，要用 `System.Linq.Dynamic.Core` 包：

```csharp
query = query.OrderBy(orderBy);   // "Status asc, CreateDate desc"
```

或者上 [QuerySort.cs](../../CP6.Core/Services/QuerySort.cs) 这种手卷的排序解析器（CP6 实际有这个文件）。

---

## 🧪 自检题

1. **设计判断**：你接手项目，发现 Service 都直接拿 `DbContext` 写，没有 `IRepository`。是否应该重构加上仓储层？  
   <details><summary>答案</summary>看团队和场景。如果团队习惯写 LINQ on DbContext 且测试用 InMemory provider，引入仓储层成本大于收益。如果团队想 mock 数据访问做单元测试（不想拖 EF Core 启动），引入轻量仓储有价值。CP6 的取舍是"既保留 DbContext 直接访问，又提供 IRepository 给简单场景"，是平衡解。</details>

2. **代码味道**：看到 `_repo.GetPageListAsync(o => o.CustomerCd == cd && o.Status >= 5)`，你怎么评价？  
   <details><summary>答案</summary>没问题。这正是通用仓储该处理的场景：简单 where 条件 + 分页。如果变成<code>_repo.GetPageListAsync(o => o.Details.Any(d => d.ProductCd.StartsWith("P-")))</code>这种跨表查询，就该退出仓储直接 LINQ on DbContext。</details>

3. **加缓存**：现在要给 `Product` 加缓存（高频读），怎么改对 Service 层最透明？  
   <details><summary>答案</summary>用 Decorator 模式：写 <code>CachedRepository&lt;T&gt;</code> 包装 <code>RepositoryBase&lt;T&gt;</code>，DI 注册时只针对 <code>IRepository&lt;Product&gt;</code> 这个具体类型用 Decorator，其他 entity 保持原样。Service 完全不知道有缓存。</details>

4. **软删除全局过滤**：怎么让"所有查询自动过滤掉 IsDeleted=true 的"？  
   <details><summary>答案</summary>在 <code>CP6Context.OnModelCreating</code> 用 <code>modelBuilder.Entity&lt;T&gt;().HasQueryFilter(e =&gt; !e.IsDeleted)</code>。需要绕过过滤时用 <code>IgnoreQueryFilters()</code>。CP6 当前没启用全局过滤，软删除靠业务层手动 where —— 这是个改进点。</details>

5. **质疑题**：有人提议"把 `IService<T>` 也做成开放泛型注册，所有 Service 继承 `ServiceBase<T>`"，你怎么劝退？  
   <details><summary>答案</summary>业务 Service 的方法签名差异远大于 Repository。<code>OrderService.CancelAsync(no, reason, force)</code> 和 <code>UserService.ResetPasswordAsync(id)</code> 没法塞进同一个 ServiceBase。强行抽象只会产生几乎所有子类都 override 的"空基类"，污染继承层级。Repository 能通用是因为它面向"数据形状"，Service 面向"业务规则"，规则是个性化的。</details>

---

## 🔗 延伸阅读

- [Repository Pattern is Dead (Ayende)](https://ayende.com/blog/3955/repository-is-the-new-singleton)
- [Why Repositories with EF? (Jimmy Bogard)](https://jimmybogard.com/no-need-for-repositories-in-ef-core/)
- [Decorator Pattern in DI](https://andrewlock.net/adding-decorated-classes-to-the-asp.net-core-di-container-using-scrutor/)
- 项目内：`CP6.Core/BaseProvider/`、`CP6.Core/Services/QuerySort.cs`
