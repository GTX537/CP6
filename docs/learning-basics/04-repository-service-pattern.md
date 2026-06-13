# 04 · 仓储 + Service 模板

## 🌱 你将学到

- "仓储模式"（Repository Pattern）背后的想法
- 看到 `IRepository<T>` 和 `RepositoryBase<T>` 你不再蒙圈
- 理解 CP6 为什么混着用"仓储 + 直接 DbContext"
- 知道 Service 层该做什么、不该做什么

---

## 🍳 生活类比：仓库的进出货窗口

想象一个大仓库。仓库里堆着各种东西（数据）。如果允许所有人随便进仓库自己拿，会乱：

- 有人拿错
- 有人忘记登记
- 有人漏了打灯

更好的做法：在仓库门口开一个**取货窗口**。所有人都通过这个窗口：

- 登记进出
- 检查权限
- 标准流程

仓储就是这种"取货窗口"。`IRepository<T>` 是窗口的接口（"我提供哪些服务"），`RepositoryBase<T>` 是窗口的具体实现。

---

## 🔎 看 CP6 代码

### IRepository<T> 接口

`D:\CP6\CP6.Core\BaseProvider\IRepository.cs`：

```csharp
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

这个接口说："任何 entity 都可以做这 5 件事"——查单条、分页查、加、改、删。

`where T : BaseEntity` 是泛型约束："T 必须继承 BaseEntity"。意思是只有"有 Id、Creator、CreateDate"的 entity 才能用这个仓储。

### RepositoryBase<T> 实现

`D:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs`：

```csharp
public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _context;
    protected readonly DbSet<T> _dbSet;

    public RepositoryBase(CP6Context context)
    {
        _context = context;
        _dbSet = context.Set<T>();   // 通用获取对应表的 DbSet
    }

    public async Task<T?> FindAsync(Guid id) => await _dbSet.FindAsync(id);

    public async Task<T> AddAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }
    // ... 其他方法
}
```

`context.Set<T>()` 是泛型魔法：传 `Order` 进来就拿到 `T_Order` 的 DbSet，传 `Stock` 进来就拿到 `T_Stock` 的 DbSet。一份代码服务所有 entity。

### Program.cs 里的一行注册

```csharp
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
```

这一行注册了**所有** `IRepository<X>` 的实现。无论是 `IRepository<Order>`、`IRepository<Stock>` 还是 `IRepository<任何 entity>`，DI 都知道用 `RepositoryBase<T>`。

这叫"开放泛型注册"，省下你手动写几十行注册的力气。

### Service 怎么用

```csharp
public class BusinessPartnerService(IRepository<BusinessPartner> repo, CP6Context ctx)
{
    private readonly IRepository<BusinessPartner> _repo = repo;
    private readonly CP6Context _context = ctx;

    // 简单查询：用仓储
    public Task<BusinessPartner?> GetAsync(Guid id) => _repo.FindAsync(id);

    // 复杂查询：绕过仓储，直接用 DbContext
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

注意 Service 同时持有 `_repo` 和 `_context` 两者——简单的走仓储，复杂的直接用 DbContext。

---

## 🤔 为什么这样

### Q1: 不直接用 DbContext 行不行？

行。但 CP6 选择两种共存有几个好处：

**好处 1：简单 CRUD 统一**
新加一个 entity，你不用写"加、改、查、删"那 4 个方法，注入 `IRepository<NewEntity>` 直接用。

**好处 2：未来好换**
如果将来想换 ORM（比如换 Dapper-only），只要改 `RepositoryBase<T>` 的实现，Service 里 `_repo.AddAsync(x)` 不用动。

**好处 3：好测试**
单元测试时可以 Mock `IRepository<T>`，不用启 EF Core。

### Q2: 那为什么不强制所有查询都走仓储？

因为通用仓储装不下复杂查询。比如"按客户、按月、按产品分组销售额"——这种 SQL 写在仓储里没意义，仓储只能写通用的。

CP6 的取舍：通用仓储只 5 个方法，复杂查询直接 LINQ on DbContext。这是**实用主义**：不教条但有效。

### Q3: 听说过"通用仓储是反模式"？

有人说这话。他们的理由：

1. EF Core 的 DbContext + DbSet 本身就是仓储
2. 通用仓储如果暴露了 IQueryable<T>，等于没封装

CP6 的处理是**只 5 个方法且不暴露 IQueryable**，避开了这个反模式的主要问题。不是教条，是务实。

### Q4: Service 应该做什么？

Service 是业务逻辑的家。它应该：

- **校验**：订单号是否重复、库存是否够
- **编排**：先查这个，再改那个，最后写日志
- **触发副作用**：通知别的模块、发 SignalR、记审计

它不应该：

- 处理 HTTP（那是 Controller 的事）
- 关心数据库连接细节（那是 DbContext 的事）

---

## ⚠️ 容易搞错的地方

### 1. 在 Controller 里写业务

```csharp
// ❌ Controller 直接写校验、增删改
public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
{
    if (await _ctx.Orders.AnyAsync(o => o.WebOrderNo == dto.WebOrderNo))
        return BadRequest("重复");
    _ctx.Orders.Add(new Order { /* ... */ });
    await _ctx.SaveChangesAsync();
}

// ✅ Controller 只调 Service
public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
{
    var order = await _orderService.CreateAsync(dto);
    return Ok(order);
}
```

### 2. Repository.UpdateAsync 的并发陷阱

CP6 的 `UpdateAsync`：

```csharp
public async Task<T> UpdateAsync(T entity)
{
    entity.ModifyDate = DateTime.Now;
    _context.Entry(entity).State = EntityState.Modified;
    await _context.SaveChangesAsync();
    return entity;
}
```

`State = Modified` 意思是"这个 entity 的所有列都改了"。如果两个请求同时改同一条记录的不同字段，互相覆盖。

防护是 `BaseBizEntity` 上的 `RowVersion`（乐观锁，第 03 章提过）。但你自己写 Service 时也要意识到。

### 3. DeleteAsync 是物理删除

CP6 的 `RepositoryBase.DeleteAsync` 是真删，但 `BaseBizEntity` 有 `IsDeleted` 字段（软删除标记）。这俩有点矛盾。

实践：业务 Service 里如果想软删，自己写：

```csharp
public async Task SoftDeleteAsync(Guid id, string user)
{
    var entity = await _repo.FindAsync(id);
    entity.IsDeleted = true;
    entity.Modifier = user;
    await _repo.UpdateAsync(entity);
}
```

### 4. 跨 entity 的事务

```csharp
public async Task DoTwoThings(...)
{
    await _orderRepo.AddAsync(order);       // SaveChanges 1
    await _stockRepo.UpdateAsync(stock);    // SaveChanges 2
    // 1 成功 2 失败 → 数据不一致！
}
```

修复：用同一个 DbContext + 一次 SaveChanges，或显式开事务。仓储不擅长跨 entity 事务，这种场景直接用 DbContext：

```csharp
_context.Orders.Add(order);
stock.PhysicalQty -= 1;
await _context.SaveChangesAsync();   // 一次 + EF 自动事务
```

---

## ✋ 动手试试

### 任务 1：照 CP6 套路写一个 Service

假设要加一个"产品分类管理"：

1. 在 `CP6.Entity/DomainModels/` 加 `ProductCategory.cs`：

```csharp
public class ProductCategory : BaseBizEntity
{
    public string CategoryCd { get; set; } = "";
    public string CategoryName { get; set; } = "";
}
```

2. 在 `CP6Context.cs` 加 `public DbSet<ProductCategory> ProductCategories { get; set; }`

3. 在 `CP6.Core/Services/` 加 `IProductCategoryService.cs`：

```csharp
public interface IProductCategoryService
{
    Task<ProductCategory?> GetAsync(Guid id);
    Task<List<ProductCategory>> SearchAsync(string keyword);
    Task<ProductCategory> CreateAsync(ProductCategory entity);
}
```

4. 实现 `ProductCategoryService.cs`，依赖 `IRepository<ProductCategory>`。

5. 在 `Program.cs` 注册：

```csharp
builder.Services.AddScoped<IProductCategoryService, ProductCategoryService>();
```

跑 `dotnet build` 看能不能编译过。

> 不用真的生成数据库迁移（那个第 03 章提了，做完整流程要走 `dotnet ef migrations add`），你看到代码能编译就行。本任务是让你熟悉 CP6 的添加新模块套路。

### 任务 2：在 Service 里"借用" Repository

修改你刚才写的 `ProductCategoryService.GetAsync` 和 `CreateAsync` 直接转发给 `_repo`：

```csharp
public Task<ProductCategory?> GetAsync(Guid id) => _repo.FindAsync(id);

public Task<ProductCategory> CreateAsync(ProductCategory entity) => _repo.AddAsync(entity);
```

而 `SearchAsync` 用 `_context` 直接 LINQ（因为关键字搜索不适合仓储）。

这就是 CP6 的真实模式：**简单转仓储，复杂自己写**。

### 任务 3：读一个真实 Service

打开 `D:\CP6\CP6.Core\Services\BusinessPartnerService.cs`（或者别的），通读一遍。回答：

- 它注入了什么？
- 哪些方法转发给仓储，哪些自己写 LINQ？
- 有没有触发副作用（调用别的 Service / Bridge Hook）？

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/04-repository-service-pattern.md`](../learning/04-repository-service-pattern.md)——讲 Decorator 模式、缓存包装
- 关键词搜索："Repository Pattern C#"、"Unit of Work"
- 项目内：随便挑 3 个 Service 通读，建立"CP6 风格"
