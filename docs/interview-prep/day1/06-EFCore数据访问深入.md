# 第 6 章 EF Core 数据访问深入

> 面向"制造业生产管理系统开发工程师"面试（C# + SQL + Vue，5 年经验强度）。
> 全部示例取自 `C:\CP6` 真实生产项目：一个 .NET 8 多租户制造业 ERP/MES/WMS 系统，数据库 SQL Server。
> 学习方式：**概念 → CP6 真实代码（标注路径）→ 逐行解析 → 生成的 SQL → 坑与真实事故 → 面试问答**。
>
> 本章是全套教程里最长、最硬核的一章。EF Core 是 .NET 后端面试的绝对重灾区，面试官几乎必问"全局查询过滤器""变更追踪""N+1""迁移策略""事务与并发"。CP6 项目恰好把这些点全部做出了**生产级、有事故教训**的实现，是极好的复习标本。读完本章你不仅会用 EF Core，还能讲出"为什么这么设计"和"踩过什么坑"——这正是 5 年经验候选人和 2 年经验候选人的分水岭。

---

## 6.0 本章导览：你将学到什么

| 小节 | 主题 | CP6 标本 |
|------|------|----------|
| 6.1 | ORM 概念 + EF Core vs Dapper vs ADO.NET | `MesDashboardDapperService.cs` |
| 6.2 | DbContext 深讲（生命周期/线程安全） | `Program.cs` DI 注册 |
| 6.3 | 模型映射（约定/注解/Fluent API） | `BaseEntity.cs` / `CP6Context.OnModelCreating` |
| 6.4 | 多租户全局查询过滤器专题 ★本章皇冠 | `CP6Context.cs` 反射注册 + `StampTenant` + `RoleController` 漏洞 |
| 6.5 | 变更追踪（快照/五态/AsNoTracking） | `RepositoryBase.cs` |
| 6.6 | 查询执行（LINQ→SQL/参数化/分页） | `RepositoryBase.GetPageListAsync` |
| 6.7 | 关联与加载（Include/N+1） | WMS `ErpBridgeHook` |
| 6.8 | 迁移完整篇 | `Migrations\` 目录演进史 + 3 个精读 |
| 6.9 | 事务与并发（显式事务/RowVersion/悲观锁） | `StockMovementService.cs` |
| 6.10 | 批量操作与性能（ExecuteUpdate 审计盲区★） | `OrderService.cs` |
| 6.11 | 原生 SQL + 存储过程 | `AddMesStoredProcedures` 迁移 |
| 6.12 | 审计管道专题（IAuditable + SaveChanges 拦截） | `IAuditable.cs` + `CP6Context` 审计核心 |
| — | 章末：20 道面试题 + 自测清单 + 3 个动手练习 |

---

## 6.1 ORM 概念：对象关系阻抗失配，与三种数据访问方式对比

### 6.1.1 什么是 ORM，为什么需要它

**ORM = Object-Relational Mapping（对象-关系映射）。**

我们的 C# 代码活在"对象世界"：类、继承、集合、引用、封装。数据库活在"关系世界"：表、行、列、外键、集合运算（SQL）。这两个世界的模型不一致，术语叫**对象关系阻抗失配（Object-Relational Impedance Mismatch）**。具体失配点：

| 对象世界 | 关系世界 | 失配表现 |
|----------|----------|----------|
| 继承（`Space_Site : BaseTenantEntity`） | 没有继承概念 | 基类字段怎么落表？TPH/TPT/TPC 三种策略 |
| 对象引用（`order.Details`） | 外键 + JOIN | 一个导航属性 = 一次 JOIN 还是一次额外查询？ |
| 集合（`List<OrderDetail>`） | 结果集（多行） | 加载时机（贪婪/延迟）问题 |
| 对象身份（同一对象 == 同一引用） | 主键相等 | 同一行查两次，是一个对象还是两个？ |
| `null` | `NULL`（三值逻辑） | `x == null` 翻译成 `IS NULL` 而非 `= NULL` |
| 私有字段/封装 | 全是公开列 | 映射时要能穿透封装 |

ORM 就是**自动在这两个世界之间搬运数据**的中间层。你写 C# 的 LINQ，它翻译成 SQL；数据库返回行，它组装成对象。**EF Core（Entity Framework Core）就是微软官方的 ORM。**

> 面试话术：如果面试官问"什么是 ORM"，不要只说"对象映射到表"。要点出"阻抗失配"这个术语，并举一个具体失配点（比如继承或对象身份），立刻显出深度。

### 6.1.2 三种数据访问方式对比

.NET 生态里访问 SQL Server 有三档抽象：

| 维度 | 裸 ADO.NET | Dapper（Micro-ORM） | EF Core（Full ORM） |
|------|-----------|---------------------|---------------------|
| 抽象层级 | 最低，手写 `SqlCommand`/`SqlDataReader` | 薄，手写 SQL + 自动映射结果到对象 | 高，LINQ 自动生成 SQL |
| 你写 SQL 吗 | 全手写 | 手写 | 一般不写（LINQ 生成） |
| 结果映射 | 手动 `reader.GetString(0)` | 自动（反射/IL 生成） | 自动 |
| 变更追踪 | 无 | 无 | 有（脏检查、自动 UPDATE） |
| 迁移/建表 | 无 | 无 | 有（Migrations） |
| 关系加载 | 手动 | 手动（multi-map） | 自动（Include） |
| 性能 | 最快（几乎无开销） | 很快（接近裸 ADO） | 稍慢（追踪/翻译开销），但可用 `AsNoTracking`/投影逼近 |
| 开发效率 | 最低 | 中 | 最高 |
| 适用 | 极端性能场景、批量 | 复杂只读报表、性能敏感查询 | CRUD 业务主线、领域模型 |

**关键观点（面试高频）：EF Core 和 Dapper 不是二选一，成熟项目往往并用。**

### 6.1.3 CP6 真实代码：EF 为主，仪表盘复杂报表用 Dapper

CP6 正是"两者并用"的教科书案例。绝大多数业务（下单、库存移动、审批流）走 EF Core；而 **MES 生产仪表盘的重型聚合查询**，团队刻意下沉到 Dapper + 存储过程。

**标本路径：`C:\CP6\CP6.Core\Services\Mes\MesDashboardDapperService.cs`**

```csharp
using System.Data;
using CP6.Entity.DTOs.Mes;
using Dapper;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MES ダッシュボード Dapper + 存儲過程 (SP) 版実装
/// </summary>
/// <remarks>
/// JD「SQL Server 性能調優・存儲過程」要件のサンプル：
/// - 既存 EF Core 版（MesDashboardService）と並列実装
/// - 大量集計クエリは SP に寄せて DB 側でインデックス + プラン最適化
/// - Dapper でストロングタイプ マッピング + 単一往復
/// </remarks>
public class MesDashboardDapperService
{
    private readonly IDbConnection _conn;

    public MesDashboardDapperService(IDbConnection conn) => _conn = conn;

    /// <summary>本日サマリ — SP 経由</summary>
    public async Task<MesDashboardSummaryDto> GetSummaryAsync()
    {
        var row = await _conn.QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(
            "usp_GetMesDashboardSummary",
            commandType: CommandType.StoredProcedure);
        return row ?? new MesDashboardSummaryDto();
    }

    /// <summary>日別推移 — SP 経由（既定 30 日）</summary>
    public async Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30)
    {
        var rows = await _conn.QueryAsync<DailyTrendDto>(
            "usp_GetMesDailyTrend",
            new { Days = days },
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    /// <summary>工程別進捗 — SP 経由</summary>
    public async Task<List<ProcessProgressDto>> GetProcessProgressAsync()
    {
        var rows = await _conn.QueryAsync<ProcessProgressDto>(
            "usp_GetMesProcessProgress",
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }
}
```

**逐行解析：**

- `private readonly IDbConnection _conn;` — Dapper 不需要 DbContext，它是 `IDbConnection`（`System.Data`）的一组扩展方法。CP6 在 `Program.cs` 里把 `IDbConnection` 注册成 Scoped（每请求一个连接，下一节讲）。
- `QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(...)` — Dapper 的核心：执行 SQL/存储过程，把结果集的列**按名字自动映射**到 DTO 的属性上。`<T>` 是强类型返回。
- `commandType: CommandType.StoredProcedure` — 告诉 Dapper 这个字符串不是 SQL 文本，而是**存储过程名**。
- `new { Days = days }` — 匿名对象作为参数。Dapper 会把它转成 `@Days` 的**参数化**命令（防注入，见 6.6.3）。
- `rows.AsList()` — Dapper 返回 `IEnumerable<T>`，`AsList()` 是 Dapper 提供的零拷贝转 `List`（若底层已是 List 就直接返回，不复制）。

**为什么这三个查询要用 Dapper 而不是 EF？** 看注释里的设计意图：**大量集计クエリ（重型聚合查询）**。仪表盘要算"今日在制/完工/良品率/延迟工单数"，涉及多表 `SUM`/`COUNT`/`CASE WHEN`/递归 CTE 生成日期序列。这类查询：
1. 用 LINQ 写会很别扭，甚至有些（递归 CTE）EF 根本翻译不出来；
2. 放到**存储过程**里，DBA 能单独调优执行计划、加覆盖索引；
3. 是**只读**的，不需要变更追踪，EF 的追踪开销纯属浪费。

配套的存储过程见 6.11 精读（`usp_GetMesDashboardSummary` 等，是真实 SQL Server T-SQL）。

**对比：同样的业务，EF Core 版长什么样？** 一个普通分页查询（`RepositoryBase`，6.5 精读）：

```csharp
var data = await query
    .OrderByDescending(x => x.CreateDate)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

没有一行 SQL，全是 LINQ，EF 自动翻译。这就是两种风格的直观差异。

### 6.1.4 坑与真实事故

- **坑 1：以为 EF 慢就全上 Dapper。** 错。EF 的"慢"绝大多数来自**变更追踪 + 意外的 N+1**，用 `AsNoTracking()` + 投影就能逼近 Dapper。CP6 只在**重型只读聚合**这一处下沉 Dapper，业务主线仍全 EF——因为业务主线需要追踪、审计、租户过滤这些 EF 才有的能力（下面各节会看到）。
- **坑 2：Dapper 没有租户过滤。** 注意 `MesDashboardDapperService` 手写的 SP 里必须自己 `WHERE IsDeleted = 0`，也**没有** EF 那套自动 `WHERE TenantId = @x`。如果这些 SP 要按租户隔离，得手动传租户参数——这是"下沉裸 SQL"的代价：你丢掉了 ORM 的安全网。CP6 的仪表盘 SP 目前是单租户口径部署，这是已知的、需要注意的边界。

### 6.1.5 面试问答

**Q：EF Core 和 Dapper 怎么选？**
A：不是二选一，是分层并用。业务写入主线（含领域模型、审计、多租户过滤、事务）用 EF Core，因为它有变更追踪、迁移、全局过滤器这些安全网；复杂只读报表/重型聚合/性能热点用 Dapper + 存储过程，避免追踪开销、让 DBA 能调优执行计划。CP6 就是这么分的：ERP/WMS 业务全 EF，MES 仪表盘聚合用 `MesDashboardDapperService` 走存储过程。

**Q：什么是对象关系阻抗失配？**
A：对象世界（继承、引用、集合、对象身份、封装）和关系世界（表、外键、结果集、主键、列）的模型不一致，导致映射时有一堆问题：继承怎么落表、导航属性触发几次查询、同一行查两次是不是同一个对象、`null` 对 `NULL` 的三值逻辑等。ORM 就是自动搬运这两个世界数据的中间层。

---

## 6.2 DbContext 深讲：生命周期、连接管理、线程安全

### 6.2.1 DbContext 是什么

`DbContext` 是 EF Core 的**工作单元（Unit of Work）+ 仓储（Repository）+ 身份映射（Identity Map）**的组合体。它：
- 持有一批 `DbSet<T>`（每个对应一张表/一个实体的查询入口）；
- 内部有一个 **ChangeTracker（变更追踪器）**，记录你从它查出来的每个对象的原始快照；
- 管理**数据库连接**（默认按需打开、查完即关）；
- `SaveChanges()` 时把追踪到的所有变更**一次性**、**在一个事务里**刷到数据库。

CP6 的 DbContext 是 `CP6Context`，路径 `C:\CP6\CP6.Core\EFDbContext\CP6Context.cs`（2356 行，本章主标本，后面反复引用）。它的 `DbSet` 声明片段：

```csharp
public class CP6Context : DbContext, IDataProtectionKeyContext
{
    /// <summary>用户表</summary>
    public DbSet<Sys_User> Sys_Users { get; set; }
    /// <summary>角色表</summary>
    public DbSet<Sys_Role> Sys_Roles { get; set; }
    /// <summary>受注ヘッダー</summary>
    public DbSet<Order> Orders { get; set; }
    /// <summary>受注明細</summary>
    public DbSet<OrderDetail> OrderDetails { get; set; }
    /// <summary>在庫実況（WM020 + 全 WMS 中核）</summary>
    public DbSet<Stock> Stocks { get; set; }
    // ...（这个 DbContext 声明了约 200 个 DbSet，覆盖 ERP/MES/WMS/OA/Fin/Space 全模块）
}
```

**`DbSet<T>` = 一个可查询的表入口。** `_db.Stocks` 就是 `IQueryable<Stock>`，你在它上面 `.Where(...).ToList()`，EF 生成对应 SQL。`DbContext` 顶部还有一句注释道破天机：**"每新增一个实体，就在这里加一个 DbSet"**——这是 CP6 团队的开发纪律。

### 6.2.2 DbContext 的生命周期：为什么是 Scoped

**标本路径：`C:\CP6\CP6.WebApi\Program.cs`**

```csharp
// 3. 注册数据库上下文
builder.Services.AddDbContext<CP6Context>(options =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3.1 注册 Dapper 用的 IDbConnection（每次请求新建连接）
builder.Services.AddScoped<IDbConnection>(_ =>
    // ...
```

**`AddDbContext<T>` 默认把 DbContext 注册为 Scoped（作用域）生命周期。** 在 ASP.NET Core 里，一个"作用域"= 一次 HTTP 请求。所以：

> **一次 HTTP 请求 = 一个 CP6Context 实例。** 请求开始时创建，请求结束时 `Dispose`。同一请求内所有服务（Controller、各种 Service、`RepositoryBase`）注入的是**同一个** `CP6Context`。

**为什么必须是 Scoped，不是 Singleton 也不是 Transient？**

- **不能 Singleton（全应用一个）**：DbContext **非线程安全**（下一小节详解），多个并发请求共用一个实例会互相踩坏 ChangeTracker 和连接状态，直接崩。而且 ChangeTracker 会无限累积追踪的实体，内存泄漏。
- **不能 Transient（每次注入都新建）**：同一请求内 Controller 和 Service 会拿到**不同**的 DbContext，那么 Service A 追踪并修改的实体，Service B 的 DbContext 完全不知道，`SaveChanges` 各存各的、无法在一个事务里原子提交，事务边界彻底破碎。
- **Scoped 恰好**：一个请求一个上下文，请求内共享同一变更追踪器和事务边界，请求间互相隔离。这就是"工作单元"模式在 Web 里的落地。

CP6 的 `RepositoryBase<T>` 通过构造函数注入 `CP6Context`，拿到的就是这个请求级实例：

```csharp
// C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs
public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _context;
    protected readonly DbSet<T> _dbSet;

    public RepositoryBase(CP6Context context)   // ← DI 注入请求级 Scoped 实例
    {
        _context = context;
        _dbSet = context.Set<T>();              // Set<T>() 是 DbSet<T> 的泛型形式
    }
}
```

注意 `context.Set<T>()`：当你写泛型仓储、拿不到具体的 `_context.Orders` 属性时，用 `Set<T>()` 按类型动态取 `DbSet`。

### 6.2.3 连接管理：DbContext 不是"一直握着连接"

新手常有误解："Scoped 一个请求一个 DbContext，那连接是不是整个请求都占着？" **不是。**

EF Core 默认**惰性、短暂**地管理连接：
- 你执行一次查询（`ToListAsync`）或 `SaveChanges` 时，EF 才**打开**连接；
- 命令执行完立刻**关闭**（归还给连接池）；
- 连接来自 ADO.NET **连接池**，"关闭"其实是还池，不是真断 TCP。

所以一个请求内即使有 5 次查询，也不是占用连接 5 次的全程，而是 5 次"借-用-还"。**例外**：显式事务（`BeginTransaction`）期间连接会一直保持打开，直到 `Commit`/`Rollback`——因为事务是连接级的（见 6.9）。

### 6.2.4 DbContext 非线程安全：原理与翻车场景

**这是面试必考点。** 官方明文：**一个 DbContext 实例不能被多个线程并发使用。**

**原理**：DbContext 内部的 ChangeTracker、`DbConnection`、命令执行状态都是**可变的、无锁的**。EF 为了性能刻意不加锁。两个线程同时用一个 DbContext：
- 一个线程在写 ChangeTracker 的实体状态，另一个在读/枚举 → 集合被并发修改 → 抛 `InvalidOperationException`；
- 更糟：两个线程同时在同一 `DbConnection` 上执行命令 → 抛 **"A second operation was started on this context instance before a previous operation completed"**（这是最著名的错误信息，面试官爱问）。

**最常见翻车场景：`await` 忘了。**

```csharp
// ❌ 灾难代码：并发用同一个 DbContext
var task1 = _db.Orders.ToListAsync();      // 没 await
var task2 = _db.Stocks.ToListAsync();      // 没 await，与上一个并发跑
await Task.WhenAll(task1, task2);          // 💥 "A second operation was started..."
```

上面两个查询在同一个 `_db`（同一 DbContext）上**并发**执行，必炸。

```csharp
// ✅ 正确：顺序 await，同一 DbContext 一次只跑一个操作
var orders = await _db.Orders.ToListAsync();
var stocks = await _db.Stocks.ToListAsync();
```

**另一个翻车场景：把 Scoped DbContext 捕获进后台任务/单例。** 比如在请求里 `Task.Run(() => _db.Xxx...)`，请求结束 DbContext 被 Dispose，后台任务还在用 → `ObjectDisposedException`。CP6 里所有后台服务（如 `OperLogCleanupService`）都是**自己开 scope、取一个新的 DbContext**，绝不捕获请求级的。

**为什么 CP6 能避免这些坑？** 因为它的写法是"一个请求内顺序 `await`、同一 Scoped 上下文"。真需要并行查多个，就得**为每个并行分支开独立的 DbContext**（用 `IDbContextFactory<T>`），而不是共享。

### 6.2.5 面试问答

**Q：DbContext 应该注册成什么生命周期？为什么？**
A：Scoped，一次 HTTP 请求一个实例。因为 DbContext 非线程安全（不能 Singleton，并发会崩且内存泄漏），又要在请求内共享同一变更追踪器和事务边界（不能 Transient，否则各 Service 拿到不同上下文、无法原子提交）。`AddDbContext` 默认就是 Scoped。

**Q："A second operation was started on this context instance..." 这个错误是什么原因？**
A：同一个 DbContext 实例被并发使用了——通常是异步方法漏了 `await`，导致两个数据库操作在同一连接上并发跑；或者把 Scoped 的 DbContext 捕获进了并行任务。DbContext 非线程安全，一次只能有一个进行中的操作。解决：顺序 `await`；真要并行就为每个分支用 `IDbContextFactory` 造独立上下文。

**Q：一个 Scoped DbContext 是不是整个请求都占着一个数据库连接？**
A：不是。EF 默认惰性管理连接：执行查询/SaveChanges 时才从连接池借出、命令完成即归还，一个请求内多次"借-用-还"。只有显式事务期间连接才全程保持打开。

---

## 6.3 模型映射：约定优先、数据注解 vs Fluent API

### 6.3.1 三种配置方式，优先级从低到高

EF Core 决定"C# 类怎么映射到表"有三个层次，**后者覆盖前者**：

1. **约定（Convention）** — 零配置默认规则。属性名 `Id` 或 `<类名>Id` → 主键；`string` → `nvarchar(max)`；`Guid`/`int` → 对应列；`public` 属性 → 列；名为 `XxxId` + 有 `Xxx` 导航属性 → 外键。
2. **数据注解（Data Annotations）** — 在实体属性上贴特性（`[Key]`、`[MaxLength]`、`[Required]`、`[Timestamp]`、`[Column]`）。就近、直观，但表达力有限。
3. **Fluent API** — 在 `DbContext.OnModelCreating` 里用 `modelBuilder.Entity<T>()...` 链式配置。表达力最强（复合键、复合唯一索引、过滤索引、关系级联、查询过滤器），能配注解配不了的东西。

CP6 **三种都用**：基类字段用约定 + 少量注解，索引/关系/租户过滤全用 Fluent API。

### 6.3.2 约定 + 注解：BaseEntity

**标本路径：`C:\CP6\CP6.Entity\BaseEntity.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity;

/// <summary>所有实体的公共基类，包含每张表都需要的公共字段</summary>
public abstract class BaseEntity
{
    /// <summary>主键，使用 Guid 自动生成</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>创建人</summary>
    [MaxLength(100)]
    public string? Creator { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;

    /// <summary>修改人</summary>
    [MaxLength(100)]
    public string? Modifier { get; set; }

    /// <summary>修改时间</summary>
    public DateTime? ModifyDate { get; set; }
}
```

**逐行解析：**
- `[Key]` — 数据注解，声明 `Id` 是主键。其实按约定（属性名叫 `Id`）EF 也会认它做主键，这里显式贴是为可读性。
- `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` — 值由数据库生成。对 `Guid` 主键，SQL Server 侧会用 `NEWSEQUENTIALID()` 生成顺序 GUID（比纯随机 GUID 对聚簇索引更友好，减少页分裂）。
- `[MaxLength(100)]` — `Creator string` 会映射成 `nvarchar(100)` 而不是约定的 `nvarchar(max)`。**这很重要**：`nvarchar(max)` 不能进索引、存储也低效，凡是有长度上限的字符串都该标 `MaxLength`。
- `string?`（可空引用类型）→ 列 `NULL`；`Guid`（非空值类型）→ 列 `NOT NULL`；`DateTime?` → `NOT NULL` 的相反，可空。**EF Core 用 C# 的可空性推断列的可空性**，这是 .NET 8 下的约定。
- `CreateDate = DateTime.Now`（属性初始化器）— 这是 **C# 层默认值**，不是数据库 `DEFAULT`。新对象在内存里就带值。

**继承体系（约定的 TPH）**：CP6 的实体继承链是
`BaseEntity`（Id + 审计四字段）→ `BaseTenantEntity`（+ TenantId）→ 各业务实体。
因为 `BaseEntity` 是 `abstract`、不单独建表，EF 用**约定**把子类字段全部拍平到各自的表里（不是 TPH 那种基类建表——这里基类根本不映射）。每个具体实体一张表。

### 6.3.3 Fluent API：OnModelCreating 里的索引与关系

CP6 把复杂配置全放在 `CP6Context.OnModelCreating`。看几个有代表性的：

```csharp
// C:\CP6\CP6.Core\EFDbContext\CP6Context.cs · OnModelCreating

// ① 单列唯一索引 + 自定义索引名
modelBuilder.Entity<Sys_Tenant>()
    .HasIndex(x => x.TenantCode).IsUnique().HasDatabaseName("UX_Sys_Tenant_Code");

// ② 复合唯一索引（防止同一角色重复授予同一菜单）
modelBuilder.Entity<Sys_UserRole>(e =>
{
    e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();   // 防重复授予同一角色
    e.HasIndex(x => x.UserId);                               // 按用户取全部角色
});

// ③ 过滤唯一索引（Filtered Unique Index）：NULL 不计入唯一约束
modelBuilder.Entity<Sys_Menu>()
    .HasIndex(x => x.MenuKey).IsUnique()
    .HasFilter("[MenuKey] IS NOT NULL");

// ④ 复合主键（自定义，非约定 Id）
modelBuilder.Entity<Sys_Role>(e =>
{
    e.HasKey(x => new { x.TenantId, x.RoleId });   // 每租户独立角色集
    e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
});

// ⑤ 一对多关系 + 级联删除
modelBuilder.Entity<JournalEntry>(e =>
{
    e.HasMany(x => x.Lines)
        .WithOne()
        .HasForeignKey(l => l.EntryId)
        .OnDelete(DeleteBehavior.Cascade);   // 删凭证头 → 分录行随删
});
```

**逐行解析：**
- `HasIndex(x => x.TenantCode).IsUnique()` — 建唯一索引。`.HasDatabaseName("UX_...")` 显式命名（否则 EF 生成 `IX_表_列` 的默认名；显式命名便于迁移里稳定引用、DBA 排查）。
- `HasIndex(x => new { x.UserId, x.RoleId })` — 匿名对象 = **复合索引**，列顺序即 `new{}` 里的顺序（`(UserId, RoleId)`），最左前缀 `UserId` 也能单独用。
- **`HasFilter("[MenuKey] IS NOT NULL")`（过滤索引）** — SQL Server 特性。业务含义：`MenuKey` 允许为 `NULL`（不是每个菜单都有稳定业务键），但**非空的必须唯一**。过滤索引让唯一约束只作用于非空行。这是 SQL Server 里"可选唯一列"的标准做法，面试能讲出来是加分项。
- `HasKey(x => new {...})` — 复合主键。CP6 的 `Sys_Role` 用 `(TenantId, RoleId)` 做主键，让每个租户有独立的、可以重号的角色集（A 租户 RoleId=1 和 B 租户 RoleId=1 是两个不同角色）。
- `OnDelete(DeleteBehavior.Cascade)` — 删父行时子行由数据库级 `ON DELETE CASCADE` 一并删。CP6 只在**真正的所有权聚合**（凭证头-分录行、记账规则头-规则行）上用级联；业务上大多数"删除"是软删（`IsDeleted=true`），不物理删。

### 6.3.4 值转换器与特殊列类型

CP6 里也有值/列类型的特殊配置：

```csharp
// 大文本列：审计 diff 的 JSON，显式 nvarchar(max)
modelBuilder.Entity<Sys_FieldAuditLog>(e =>
{
    e.Property(x => x.Changes).HasColumnType("nvarchar(max)");   // 大文本
});

// JSON 列（几何路径）也用 nvarchar(max)
modelBuilder.Entity<CP6.Entity.DomainModels.Wms.WmsBin>(e =>
{
    e.Property(x => x.Id).ValueGeneratedNever();   // 主键由发布方给定，禁自动生成
    e.Property(x => x.PathJson).HasColumnType("nvarchar(max)");
    e.Property(x => x.AttrsJson).HasColumnType("nvarchar(max)");
});
```

- `HasColumnType("nvarchar(max)")` — 覆盖约定/`MaxLength`，强制大文本列。审计 JSON、几何路径 JSON 这类不定长内容用它。
- `ValueGeneratedNever()` — **禁止**数据库生成主键值。`WmsBin.Id` 是 Space 模块发布过来的 `LocationId`，必须原样保留、不能让数据库覆盖。这是"外部给定主键"的关键配置。

CP6 里 `[Timestamp]` 特性（RowVersion 乐观并发令牌，见 6.9）也是一种特殊的注解式映射，会映射成 SQL Server 的 `rowversion` 列。

### 6.3.5 注解 vs Fluent API：怎么选

| 场景 | 用什么 | 原因 |
|------|--------|------|
| 主键、长度、必填、可空 | 数据注解 | 就近、直观，实体自解释 |
| `[Timestamp]` RowVersion | 数据注解 | 一个特性就够 |
| 复合主键、复合/过滤/唯一索引 | Fluent API | 注解表达不了 |
| 关系、级联、外键行为 | Fluent API | 注解表达力弱 |
| 全局查询过滤器 | **只能** Fluent API | 注解根本没有 |
| 值转换器、列类型 | Fluent API | 集中管理 |

**CP6 的实践哲学**：实体保持"贫血"、只带最基本的注解（`[Key]`/`[MaxLength]`/`[Timestamp]`）；一切"关系性、索引性、跨实体"的配置全部集中到 `OnModelCreating`。好处是模型规则有**单一集中处**，评审迁移时一眼看全。

### 6.3.6 面试问答

**Q：数据注解和 Fluent API 有什么区别，怎么选？**
A：数据注解贴在实体属性上（`[Key]`/`[MaxLength]`/`[Timestamp]`），就近、直观但表达力有限；Fluent API 在 `OnModelCreating` 里链式配置，能做复合主键、复合/过滤唯一索引、关系级联、查询过滤器、值转换器等注解做不了的事，优先级也更高（覆盖注解）。简单的用注解，复杂的和跨实体的用 Fluent。全局查询过滤器只能用 Fluent API。

**Q：什么是过滤唯一索引（filtered index），CP6 哪里用了？**
A：SQL Server 支持给唯一索引加 `WHERE` 条件，只对满足条件的行强制唯一。CP6 的 `Sys_Menu.MenuKey` 是 `HasIndex(...).IsUnique().HasFilter("[MenuKey] IS NOT NULL")`——允许 MenuKey 为空，但非空的必须唯一。这是实现"可选唯一列"的标准手法。

---

## 6.4 多租户全局查询过滤器专题 ★本章皇冠

> 这是 CP6 最亮的架构点，也是面试里最能拉开差距的话题。SaaS 多租户、行级安全、软删除，全靠这套机制。务必吃透。

### 6.4.1 问题：多租户行级隔离怎么做

CP6 是 **SaaS 多租户**系统：多个客户（租户，Tenant）的数据存在**同一套表**里，靠每行的 `TenantId` 列区分。核心安全要求：

> **A 租户的用户，永远只能查到、只能写入 A 租户的数据。** 一行都不能串。

最幼稚的做法：每个查询手动加 `.Where(x => x.TenantId == currentTenant)`。问题是**总有一天有人忘写**——一个漏写的查询就是一个跨租户数据泄露。这叫"防漏命门"：安全不能靠人自觉。

EF Core 的答案是**全局查询过滤器（Global Query Filter）**：给实体注册一次过滤条件，之后**所有**针对该实体的查询自动带上，想漏都漏不了。

### 6.4.2 载体：BaseTenantEntity

**标本路径：`C:\CP6\CP6.Entity\BaseTenantEntity.cs`**

```csharp
namespace CP6.Entity;

/// <summary>
/// 多租户实体基类（OA 章10 §3）。介于 BaseEntity 与业务实体之间：需按租户行级隔离的
/// 实体改继承本类即获得 TenantId，并自动纳入 CP6Context 的全局查询过滤 + 写入盖章。
/// 纯字典/语言包/菜单结构等系统级共享表保持继承 BaseEntity（不带 TenantId）。
/// </summary>
public abstract class BaseTenantEntity : BaseEntity
{
    /// <summary>租户 Id（行级隔离硬墙；写入时由 SaveChanges 自动盖当前租户，查询时全局过滤）。</summary>
    public Guid TenantId { get; set; }
}
```

**设计要点**：CP6 用**继承**当"标记"。一个实体**是否要租户隔离**，只看它继承 `BaseTenantEntity` 还是 `BaseEntity`：
- 继承 `BaseTenantEntity`（如 `Order`、`Stock`、`Space_Site`）→ 自动纳入租户过滤 + 写入盖章；
- 继承 `BaseEntity`（如 `Sys_Menu`、`Sys_Lang` 语言包、`DataProtectionKey`）→ 系统级共享表，不隔离。

这个"继承即声明意图"的设计，让下面的反射批量注册成为可能。

### 6.4.3 皇冠代码：反射批量注册全局查询过滤器

**标本路径：`C:\CP6\CP6.Core\EFDbContext\CP6Context.cs` · OnModelCreating 尾部**

```csharp
// ═══════════════════════════════════════════════════════════
//  章10 多租户：对所有 BaseTenantEntity 反射批量注册全局查询过滤（防漏命门，OA4-D1/D3）
//  WHERE TenantId == CurrentTenantId —— 闭包到本上下文实例，EF 每次查询重读当前租户。
// ═══════════════════════════════════════════════════════════
foreach (var et in modelBuilder.Model.GetEntityTypes()
             .Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null))
{
    var p = Expression.Parameter(et.ClrType, "e");
    var body = Expression.Equal(
        Expression.Property(p, nameof(BaseTenantEntity.TenantId)),
        Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
    modelBuilder.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
}
```

**逐行拆解（这段是本章最需要理解的 8 行）：**

- `modelBuilder.Model.GetEntityTypes()` — 拿到模型里**所有**已注册的实体类型（约 200 个）。
- `.Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)` — 只挑**继承 `BaseTenantEntity`** 的实体。`t.BaseType is null` 是关键细节：EF 模型里的实体可能有继承关系（TPH），只对**根实体**注册一次，避免对派生实体重复注册。
- 然后手工构造一棵**表达式树（Expression Tree）**，等价于 lambda `e => e.TenantId == this.CurrentTenantId`：
  - `Expression.Parameter(et.ClrType, "e")` — lambda 的参数 `e`（类型是当前实体）。
  - `Expression.Property(p, "TenantId")` — `e.TenantId`。
  - `Expression.Property(Expression.Constant(this), "CurrentTenantId")` — **`this.CurrentTenantId`**，注意 `this` 是**当前 DbContext 实例**被闭包捕获。
  - `Expression.Equal(...)` — 把两者用 `==` 连起来。
  - `Expression.Lambda(body, p)` — 组装成完整 lambda。
- `modelBuilder.Entity(et.ClrType).HasQueryFilter(...)` — 给这个实体注册全局查询过滤器。

**为什么要手工建表达式树，不能直接写 `HasQueryFilter(e => e.TenantId == CurrentTenantId)`？** 因为这里是**泛型运行时循环**，`et.ClrType` 是 `Type`（运行时才知道），编译期写不出 `HasQueryFilter<具体类型>`。只能用 `Expression` API 在运行时按类型动态造 lambda。这正是"反射遍历模型 + 表达式树"的经典用法，**面试讲出来非常加分**。

**为什么过滤器要闭包 `this.CurrentTenantId` 而不是一个固定值？** 看 `CurrentTenantId` 的定义：

```csharp
private readonly ITenantContext? _tenant;

/// <summary>当前租户 Id（全局查询过滤 + 写入盖章用）。无注入则默认租户。</summary>
public Guid CurrentTenantId => _tenant?.CurrentTenantId ?? TenantContext.DefaultTenant;
```

`CurrentTenantId` 是个**属性**，每次求值都从注入的 `ITenantContext` 读**当前请求**的租户。因为过滤器闭包的是 `this`（DbContext 实例）并在查询翻译时**读属性**，所以每次查询都拿到**实时**的当前租户。EF 会为不同的 `CurrentTenantId` 值缓存不同的查询计划变体（它把过滤器里的上下文属性当参数处理），既正确又不破坏查询缓存。

> **注意**：如果这里写成捕获一个局部变量的固定值，那所有请求都会用 DbContext **首次构建模型**时的租户——灾难。CP6 用属性 + `this` 闭包避开了这个坑。

### 6.4.4 生成的 SQL：过滤器自动注入

有了过滤器，一个普通查询：

```csharp
var stocks = await _db.Stocks.Where(s => s.ProductCd == "P001").ToListAsync();
```

EF 生成的 SQL **自动**带上租户条件（假设当前租户是 `@__ef_filter__CurrentTenantId_0`）：

```sql
SELECT [s].[Id], [s].[TenantId], [s].[ProductCd], [s].[PhysicalQty], ...
FROM [T_Stock] AS [s]
WHERE [s].[TenantId] = @__ef_filter__CurrentTenantId_0   -- ← 过滤器自动注入！
  AND [s].[ProductCd] = @__p_0
```

你的 LINQ 里**根本没写** `TenantId` 那一句，EF 帮你加了。哪怕全项目 200 张租户表、几千个查询，一个都漏不了。这就是"防漏命门"的威力。

**软删除同理**：CP6 的软删除（`IsDeleted`）在部分实体上也是靠类似机制/手写 `!x.IsDeleted` 组合。租户过滤是全局注册的最强形态。

### 6.4.5 逃生舱：IgnoreQueryFilters 及其危险

有极少数场景**必须**跨租户查，EF 提供逃生舱 `IgnoreQueryFilters()`——临时关掉全局过滤器。

**标本路径：`C:\CP6\CP6.Core\Services\Sys\RefreshTokenService.cs`**

```csharp
/// 安全设计：④TokenHash 单列全局唯一，
/// refresh 时无租户上下文按 TokenHash + IgnoreQueryFilters 跨租户精确命中，再由令牌回设租户上下文。
public async Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua)
{
    var hash = HashOf(rawToken);
    // 无租户上下文：按 TokenHash 跨租户查（全局唯一索引；IgnoreQueryFilters 白名单——令牌本身即凭证）
    var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == hash);
    if (row == null || row.ExpiresAt <= DateTime.Now)
        throw new InvalidOperationException("E-SEC-007");   // 令牌无效或已过期
    // ...
    // 由令牌的 TenantId 回设上下文，后续查询/盖章按其租户正确作用域
    _tenant.CurrentTenantId = row.TenantId;
    var user = await _db.Sys_Users.IgnoreQueryFilters().FirstAsync(u => u.Id == row.UserId);
    // ...
}
```

**为什么这里必须用逃生舱？** 刷新令牌（refresh token）到达时，用户**还没建立租户上下文**（cookie 里只有一个不可逆的令牌哈希，不带 TenantId）。系统需要先按 `TokenHash`（全局唯一索引）**跨租户**找到这行令牌，才能知道它属于哪个租户——所以此刻必须 `IgnoreQueryFilters()`。找到后立即 `_tenant.CurrentTenantId = row.TenantId` **回设上下文**，后续所有查询/写入又回到正常的租户作用域。

**为什么它危险？** `IgnoreQueryFilters()` 一开，这个查询就能看到**全库所有租户**的数据。用错地方 = 跨租户泄露。CP6 的纪律是：
- 只在**极少数、有充分理由**的地方用（refresh token、平台管理员跨租户审计）；
- 用了必须**立刻回设租户上下文**或**自己手动加精确条件**（这里靠全局唯一的 `TokenHash` 精确命中单行）；
- 每处都在注释里写明"白名单"理由，评审时重点看。

配套的 `Sys_RefreshToken.TokenHash` 在 `OnModelCreating` 里被**特意保持单列全局唯一**（没有像其他表那样升级成 `(TenantId, TokenHash)`），就是为了支撑这个跨租户按哈希命中的场景。CP6 的反射批量索引升级循环里专门给它开了跳过条件（6.4.7 会看到）。

### 6.4.6 写入侧盖章：SaveChanges 自动填 TenantId

**查询侧过滤只是安全故事的一半。写入侧还得保证新行落上正确的 TenantId。** 靠人手填？又会漏、又能被恶意篡改。CP6 在 `SaveChanges` 里**自动盖章（stamp）**。

**标本路径：`C:\CP6\CP6.Core\EFDbContext\CP6Context.cs` · StampTenant**

```csharp
/// <summary>写入盖章（章10 §4）：新增的 BaseTenantEntity 未显式设租户 → 盖当前租户。
/// Sys_OperLog（int Id 非 BaseTenantEntity）同样盖章：覆盖 DeadLetterNotifier 等未显式设租户的写入路径。</summary>
private void StampTenant()
{
    foreach (var e in ChangeTracker.Entries<BaseTenantEntity>())
        if (e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
            e.Entity.TenantId = CurrentTenantId;

    foreach (var e in ChangeTracker.Entries<Sys_OperLog>())
        if (e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
            e.Entity.TenantId = CurrentTenantId;

    // P0-T3：Sys_Role（int RoleId 复合主键，非 BaseTenantEntity，未进反射批量）新增行盖当前租户。
    foreach (var e in ChangeTracker.Entries<Sys_Role>())
        if (e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
            e.Entity.TenantId = CurrentTenantId;

    // P0-T3 补口：Sys_RoleMenu（int Id 非 BaseTenantEntity）同样盖章。
    foreach (var e in ChangeTracker.Entries<Sys_RoleMenu>())
        if (e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
            e.Entity.TenantId = CurrentTenantId;
}
```

**逐行解析：**
- `ChangeTracker.Entries<BaseTenantEntity>()` — 遍历所有被追踪的、属于 `BaseTenantEntity` 的实体。
- `e.State == EntityState.Added` — 只处理**新增**的（更新行不动它的 TenantId，避免搬家）。
- `e.Entity.TenantId == Guid.Empty` — **只在没填时才盖**。如果调用方已经显式设了 TenantId（且非空），尊重它（但注意下面 6.4.8 的漏洞——正因为"非空就尊重"，恶意请求体能绕过）。
- `e.Entity.TenantId = CurrentTenantId` — 盖上当前请求的租户。

后面三个 `foreach` 是对**不继承 `BaseTenantEntity`** 的特殊表（`Sys_OperLog`、`Sys_Role` 复合主键、`Sys_RoleMenu`）手工补盖章——因为它们没进反射批量，但业务上也需要租户隔离。

`StampTenant()` 在 `SaveChanges`/`SaveChangesAsync` 的**最开头**被调用（见 6.12 完整的 SaveChanges 重写）：

```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    StampTenant();   // ← 保存前先盖章
    // ...
}
```

### 6.4.7 反射批量把唯一索引升级为 (TenantId, ...)

还有第三层设防。多租户下，"业务单号唯一"必须是"**每租户**唯一"——否则 B 租户没法用和 A 租户相同的单号。CP6 又用一段反射把所有租户表上的单列唯一索引**自动升级**成 `(TenantId, 原列)` 复合唯一：

```csharp
// 章10 §8：把所有 BaseTenantEntity 上"全局唯一"索引升级为 (TenantId, ...) 复合唯一。
foreach (var et in modelBuilder.Model.GetEntityTypes()
             .Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null))
{
    var tenantProp = et.FindProperty(nameof(BaseTenantEntity.TenantId));
    if (tenantProp is null) continue;

    // 被 FK 作为主键引用的唯一索引排除（SQL Server 不允许 DROP 被 FK 依赖的索引）
    var fkPrincipalKeyProps = et.GetReferencingForeignKeys()
        .Select(fk => fk.PrincipalKey.Properties).ToList();

    foreach (var idx in et.GetIndexes().Where(i => i.IsUnique).ToList())
    {
        if (idx.Properties.Contains(tenantProp)) continue;   // 已带 TenantId 前缀，跳过
        if (fkPrincipalKeyProps.Any(kp => kp.SequenceEqual(idx.Properties))) continue;   // FK 主键依赖，跳过

        // Sys_RefreshToken.TokenHash 保持单列全局唯一（refresh 无租户上下文，跨租户按哈希命中）
        if (et.ClrType == typeof(Sys_RefreshToken)
            && idx.Properties.Count == 1
            && idx.Properties[0].Name == nameof(Sys_RefreshToken.TokenHash))
            continue;

        var dbName = idx.GetDatabaseName();
        var filter = idx.GetFilter();
        var newProps = new List<IMutableProperty> { tenantProp };
        newProps.AddRange(idx.Properties);

        et.RemoveIndex(idx.Properties);              // 删旧的单列唯一索引
        var newIdx = et.AddIndex(newProps);          // 建 (TenantId, ...) 复合唯一
        newIdx.IsUnique = true;
        if (dbName != null) newIdx.SetDatabaseName(dbName);
        if (filter != null) newIdx.SetFilter(filter);   // 保留原过滤条件
    }
}
```

**要点**：
- 同样是"防漏命门"哲学——不靠每个实体手写 `(TenantId, Code)`，而是遍历模型自动升级。
- **三处跳过**很讲究：已含 TenantId 的（`Sys_Lang`）跳过；被外键当主键引用的（改了要连带改子表 FK）跳过；`Sys_RefreshToken.TokenHash`（必须全局唯一以支持跨租户命中）跳过。
- 保留原 `DatabaseName` 和 `Filter`（过滤唯一索引如 AP 发票去重的 `[SupplierInvoiceNo] IS NOT NULL` 必须留着）。
- **默认租户期等价无损**：单租户部署时全表 TenantId 相同，`(TenantId, Code)` 唯一 ⇔ `(Code)` 唯一。

### 6.4.8 真实漏洞与修复：角色新增接口信任请求体 TenantId

**这是本章最有价值的"事故案例"，面试讲出来极有杀伤力。**

**漏洞**：`StampTenant` 的逻辑是"TenantId 为空才盖章"。这意味着——如果**请求体（HTTP body）里带了一个非空的、别的租户的 TenantId**，`StampTenant` 会认为"调用方已经设了"，**放行**，于是这行数据落到了别的租户！这是典型的**跨租户写注入**。

角色新增接口 `POST /role` 接收 `[FromBody] Sys_Role entity`，如果直接 `Add(entity)`，攻击者构造 `{"tenantId": "<B租户的GUID>", "roleName": "hacker"}`，就能往 B 租户塞一个角色。

**修复标本：`C:\CP6\CP6.WebApi\Controllers\Sys\RoleController.cs`**

```csharp
[HttpPost]
[RequirePermission("role", "add")]
public async Task<IActionResult> Add([FromBody] Sys_Role entity)
{
    // P0 终审 #1：TenantId 是不可信输入——StampTenant 仅在 TenantId==Guid.Empty 时盖章，
    // body 携带的他租非空 Guid 会被放行 → 跨租户写注入。控制器边界强制盖当前租户。
    entity.TenantId = _context.CurrentTenantId;   // ← 修复：强制覆写为当前租户，无视 body
    entity.CreateDate = DateTime.Now;
    _context.Sys_Roles.Add(entity);
    await _context.SaveChangesAsync();
    return Ok(entity);
}
```

**修复的本质**：在**控制器边界**（信任边界）把 `entity.TenantId` **无条件覆写**成当前租户，无视请求体里带的任何值。这样即使攻击者传了别的租户 GUID，也会被踩掉。

**这个案例的深刻教训（务必背下来当面试谈资）：**
1. **纵深防御（Defense in Depth）**：查询侧全局过滤器 + 写入侧盖章，两侧都设防才是完整的租户隔离。
2. **但"盖章只在空值时生效"留了个缝**——它信任了外部输入的非空 TenantId。安全设计里，**任何来自请求体的字段都是不可信的**，能被用户控制的值绝不能用来做授权/归属判定。
3. **修复不在盖章逻辑里改，而在信任边界（控制器）强制覆写**——因为盖章逻辑要兼顾"服务内部合法地显式设租户"的场景（如 refresh token 回设、平台管理员代建），不能一刀切禁止非空。真正该拦截的是"外部请求体"这个不可信来源。

CP6 把这个修复登记为 "P0 终审 #1"，是一次正式的安全评审发现，配套还有另外三个跨租户隔离修复（`Sys_RoleMenu` 唯一索引、启动网复活菜单、Down 回滚撞重复键）。

### 6.4.9 面试问答

**Q：EF Core 的全局查询过滤器是什么？怎么用它做多租户？**
A：`HasQueryFilter` 给实体注册一个过滤 lambda，之后所有针对该实体的查询自动带上这个条件，想漏都漏不了。CP6 用它做多租户行级隔离：所有需要隔离的实体继承 `BaseTenantEntity`（带 TenantId），然后在 `OnModelCreating` 里用反射遍历模型，给每个这样的实体注册 `e => e.TenantId == CurrentTenantId`。过滤器闭包 DbContext 的 `CurrentTenantId` 属性，每次查询实时读当前请求的租户，EF 生成的 SQL 自动加 `WHERE TenantId = @x`。

**Q：为什么要用反射 + 表达式树注册，不逐个手写？**
A：一是"防漏命门"——200 张租户表逐个手写迟早漏一个，漏一个就是跨租户泄露；二是运行时循环里实体类型是 `Type`，编译期写不出 `HasQueryFilter<具体类型>`，只能用 `Expression.Parameter/Property/Equal/Lambda` 手工造表达式树按类型动态注册。

**Q：IgnoreQueryFilters 什么时候用？有什么风险？**
A：极少数必须跨租户的场景，比如刷新令牌到达时还没有租户上下文，得先按全局唯一的 TokenHash 跨租户找到令牌行才能知道它属于哪个租户。风险是它关掉全局过滤器后能看到全库所有租户数据，用错就泄露。纪律：只在白名单场景用，用了立刻回设租户上下文或用全局唯一键精确命中单行，每处注释写明理由供评审。

**Q：讲一个你知道的多租户安全漏洞。**
A：CP6 的角色新增接口曾信任请求体里的 TenantId。写入盖章逻辑是"TenantId 为空才盖当前租户"，攻击者在 body 里塞一个别的租户的非空 GUID 就能绕过盖章、把数据写到别的租户。修复是在控制器边界无条件把 `entity.TenantId = CurrentTenantId` 覆写，无视 body。教训是查询和写入两侧都要设防，且任何来自请求体的字段都是不可信输入，不能用于归属判定。

---

## 6.5 变更追踪：快照、EntityState 五态、AsNoTracking

### 6.5.1 快照追踪（Snapshot Change Tracking）原理

当你用（默认追踪的）查询从 DbContext 取出实体，EF **给每个实体拍一张"原始值快照"**存进 ChangeTracker。之后你改对象的属性，EF 不会立刻知道；直到 `SaveChanges`（或访问 `ChangeTracker.Entries()`）时，EF 执行 **DetectChanges**：拿当前值和快照逐属性比对，算出**哪些实体、哪些列变了**，据此生成最小化的 `UPDATE`（只更新变的列）。

这就是"脏检查（dirty checking）"。好处：你只管改对象，不用手写 UPDATE 语句。代价：拍快照 + 比对有内存和 CPU 开销，实体越多越贵。

### 6.5.2 EntityState 五态

每个被追踪的实体在 ChangeTracker 里有一个状态：

| State | 含义 | SaveChanges 时 |
|-------|------|----------------|
| `Added` | 新增，尚未入库 | 生成 `INSERT` |
| `Modified` | 已存在，有属性被改 | 生成 `UPDATE`（只改变的列） |
| `Deleted` | 标记删除 | 生成 `DELETE` |
| `Unchanged` | 查出来后没动过 | 什么都不做 |
| `Detached` | 未被追踪（不在 ChangeTracker） | 忽略 |

状态转移：`Add()` → Added；查询默认 → Unchanged；改属性 + DetectChanges → Modified；`Remove()` → Deleted；`AsNoTracking()` 查出或 `Entry.State = Detached` → Detached。

CP6 的 `RepositoryBase` 展示了显式设状态：

```csharp
// C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs
public async Task<T> UpdateAsync(T entity)
{
    entity.ModifyDate = DateTime.Now;
    _context.Entry(entity).State = EntityState.Modified;   // ← 手动把游离实体标为 Modified
    await _context.SaveChangesAsync();
    return entity;
}
```

`_context.Entry(entity).State = EntityState.Modified` — 把一个**游离（Detached）**的实体（比如从 HTTP body 反序列化来的、没经过查询的）**附加**并整体标为 Modified，`SaveChanges` 会生成 UPDATE **所有列**。

### 6.5.3 Attach vs Update 的区别

- **`Attach(entity)`** — 把游离实体附加为 **Unchanged**（假设它和库里一致）。之后你改哪个属性，哪个变 Modified，SaveChanges 只更新那几列。
- **`Update(entity)`** — 把游离实体附加为 **Modified（全部列）**。SaveChanges 更新**所有列**，不管你改没改。
- **`Entry(entity).State = Modified`**（CP6 的 `UpdateAsync` 用法）— 等价于 `Update`，全列 UPDATE。

**坑**：`Update`/`State=Modified` 全列 UPDATE 有两个问题——(1) 会把你没打算改的列也覆盖（如果 entity 某些字段是默认值，会把库里的真值冲掉）；(2) 对字段级审计不友好（下面 6.12 讲，CP6 的审计要精确 diff，全列 UPDATE 会让每次都"看起来所有列都变了")。

**CP6 的演进**：早期 `RepositoryBase.UpdateAsync` 用 `State = Modified`（全列）。但引入字段级审计后，关键控制器改成**"先查后改"**——先从库里查出实体（追踪态），再拷贝可编辑列，这样 DetectChanges 能算出**精确**的 diff。看 `RoleController.Update`：

```csharp
// C:\CP6\CP6.WebApi\Controllers\Sys\RoleController.cs
[HttpPut]
[RequirePermission("role", "edit")]
public async Task<IActionResult> Update([FromBody] Sys_Role entity)
{
    // #4 字段审计 T4：先查后改（替 attach-as-Modified），令 Modified diff 准确。
    // P0-T3：复合主键 (TenantId,RoleId) 后不能用单参 FindAsync；按 RoleId 查（全局过滤自动限定当前租户）。
    var existing = await _context.Sys_Roles.FirstOrDefaultAsync(r => r.RoleId == entity.RoleId);
    // ...拷贝可编辑列到 existing...
}
```

`existing` 是追踪态，只改变化的列 → DetectChanges 得到精确 diff → 审计准确。这是"先查后改（load-then-modify）" vs "attach-as-modified" 的现实取舍。

### 6.5.4 AsNoTracking：原理与何时必用

`AsNoTracking()` 告诉 EF：**这次查询出来的实体不要追踪**（不拍快照、不进 ChangeTracker，状态 Detached）。

**原理层面它省了什么**：
1. 不拍原始值快照 → 省内存、省 CPU；
2. 不进 Identity Map（身份映射）→ 不做同一实体去重（下一小节）；
3. SaveChanges 完全忽略它们。

**何时必用（面试高频）**：**只读查询**——列表展示、报表、导出、任何"查出来只是给前端看、不会回写"的场景。CP6 在 WMS 大量只读查询里用它：

```csharp
// C:\CP6\CP6.Core\Services\Wms\ErpBridgeHook.cs
var ob = await Db.OutboundOrders.AsNoTracking()
    .FirstOrDefaultAsync(...);

// C:\CP6\CP6.Core\Services\Wms\IotService.cs
var sensors = await _db.IotSensors.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync();
```

一次跨模块桥接读、一次 IoT 传感器列表读——都是只读，`AsNoTracking()` 直接省掉追踪开销。**在高频只读接口上，AsNoTracking 常能带来可观的吞吐提升**（少拍快照、少 GC 压力）。

**反过来，什么时候不能用**：你查出来要改并 `SaveChanges` 回写时——那必须追踪，否则 EF 不知道你改了什么。CP6 的 `StockMovementService`（6.9）查库存行是要改的，就**没有** `AsNoTracking`。

### 6.5.5 追踪查询的身份解析（Identity Resolution）

**同一个 DbContext、同一次追踪查询里，同一行（同主键）只会有一个 C# 对象实例。** 这叫身份解析 / Identity Map。

```csharp
var a = await _db.Orders.FirstAsync(o => o.Id == id);
var b = await _db.Orders.FirstAsync(o => o.Id == id);   // 同一 DbContext、同一 id
// a 和 b 是同一个对象引用！ReferenceEquals(a, b) == true
```

第二次查询 EF 发现该主键已在 ChangeTracker 里，**直接返回已有实例**（不覆盖你可能已做的修改）。好处：一致性（不会出现同一行两个不同步的副本）。

**但 `AsNoTracking()` 关掉身份解析**：不追踪就没有 Identity Map，两次 `AsNoTracking` 查同一行会得到**两个不同对象**。若一次带 `Include` 的 `AsNoTracking` 查询里同一主实体重复出现（如一对多 JOIN 展开），默认会生成**多个副本**。EF Core 提供 `AsNoTrackingWithIdentityResolution()` 折中：不追踪但仍去重。

### 6.5.6 坑与真实事故

- **坑：`AsNoTracking` 后又想 SaveChanges 改。** 查出来是 Detached 的，改了 `SaveChanges` 毫无反应（EF 根本没追踪）。必须要么去掉 `AsNoTracking`，要么 `Attach`/`Update` 手动附加。
- **坑：全列 UPDATE 冲掉真值。** 用 `Update(dto映射来的entity)` 而 DTO 少映射了几个字段（它们是默认值），SaveChanges 把库里的真值覆盖成默认值。CP6 用"先查后改"规避，且能得到精确审计 diff。
- **坑：大批量追踪导致 DetectChanges 变慢。** 一个上下文追踪几万个实体时，每次 `SaveChanges` 的 DetectChanges 是 O(实体数 × 属性数)，会明显变慢。批量只读用 `AsNoTracking`，批量写用 `ExecuteUpdate`（6.10）。

### 6.5.7 面试问答

**Q：EF Core 怎么知道你改了实体、生成 UPDATE 的？**
A：快照追踪。追踪查询取出实体时，ChangeTracker 给每个实体拍原始值快照。SaveChanges（或访问 Entries）时执行 DetectChanges，逐属性比对当前值和快照，算出哪些实体哪些列变了，生成只更新变化列的最小 UPDATE。这就是脏检查。

**Q：AsNoTracking 什么时候用，为什么能提升性能？**
A：只读查询用（列表、报表、导出、不回写的数据）。它省掉拍快照、进 ChangeTracker、身份解析这些开销，减少内存和 GC 压力。CP6 的 WMS 只读查询（IoT 列表、跨模块桥接读）都用了。注意查出来要改并 SaveChanges 的场景不能用，否则 EF 不追踪、改了不生效。

**Q：Attach 和 Update 的区别？**
A：Attach 把游离实体附加为 Unchanged，之后改哪列哪列变 Modified，只更新改的列；Update 直接附加为 Modified（全部列），SaveChanges 更新所有列。全列 UPDATE 有覆盖未改列真值、审计 diff 不精确的问题，CP6 的更新接口改用"先查后改"来得到精确 diff。

**Q：同一个 DbContext 查同一行两次，是一个对象还是两个？**
A：追踪查询下是同一个对象——身份解析/Identity Map 保证同主键只有一个实例，第二次查直接返回 ChangeTracker 里已有的。但 AsNoTracking 关掉身份解析，会得到两个不同对象。

---

## 6.6 查询执行：LINQ → 表达式树 → SQL

### 6.6.1 翻译管道：你的 LINQ 是怎么变成 SQL 的

```
你写的 LINQ (IQueryable)
      │  编译器把 lambda 编成 Expression<Func<...>>（表达式树，不是委托）
      ▼
表达式树（Expression Tree）
      │  EF Core 的查询编译器遍历表达式树
      ▼
查询模型 → 关系模型 → SQL 生成
      ▼
参数化 SQL 命令 → ADO.NET → SQL Server
      ▼
DataReader 返回行 → 物化（materialize）成实体/DTO
```

关键：`IQueryable<T>` 上的 `.Where(...)` 接收的是 **`Expression<Func<T,bool>>`（表达式树）**，不是 `Func<T,bool>`（委托）。表达式树是"代码的数据结构表示"，EF 能**遍历分析**它、翻译成 SQL。而 `IEnumerable<T>`（LINQ to Objects）上的 `.Where` 接收委托，是在内存里逐个跑——这就是 `IQueryable` 和 `IEnumerable` 的本质区别，也是最经典的面试题之一。

CP6 的 `RepositoryBase` 签名就体现了这点：

```csharp
// C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs
public async Task<(List<T> Data, int Total)> GetPageListAsync(
    Expression<Func<T, bool>>? filter,   // ← 表达式树，能翻译成 SQL WHERE
    int page, int pageSize,
    string orderBy = "CreateDate desc")
{
    IQueryable<T> query = _dbSet;
    if (filter != null)
        query = query.Where(filter);     // 拼到 IQueryable，尚未执行

    var total = await query.CountAsync();     // ← 第一次执行：SELECT COUNT(*)

    var data = await query
        .OrderByDescending(x => x.CreateDate)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();                        // ← 第二次执行：SELECT ... OFFSET/FETCH

    return (data, total);
}
```

**要点**：`filter` 是 `Expression<Func<T,bool>>`，所以调用方传的 lambda 会被翻译进 SQL 的 `WHERE`，在**数据库端**过滤，而不是查全表回内存过滤。

### 6.6.2 延迟执行（Deferred Execution）

注意上面：`query = query.Where(filter)`、`.OrderBy`、`.Skip`、`.Take` 这些**都不执行**，只是往 `IQueryable` 上**拼表达式树**。直到遇到**终结操作**才真正发 SQL：
- `CountAsync()` / `ToListAsync()` / `FirstOrDefaultAsync()` / `AnyAsync()` 等——这些才触发数据库往返。

所以 `GetPageListAsync` 发了**两条** SQL：一条 `COUNT`，一条 `OFFSET/FETCH` 取当页。这是分页的标准两查（总数 + 数据）。

### 6.6.3 参数化 = 防 SQL 注入

EF 生成的 SQL **总是参数化**的。比如：

```csharp
var list = await _db.Orders.Where(o => o.WebOrderNo == userInput).ToListAsync();
```

生成：

```sql
SELECT ... FROM [T_Order] AS [o]
WHERE [o].[TenantId] = @__ef_filter_0   -- 全局过滤器
  AND [o].[WebOrderNo] = @__userInput_1  -- ← 参数化，不是字符串拼接！
```

`userInput` 作为**参数** `@__userInput_1` 传给 SQL Server，**永远不会**被当成 SQL 代码执行。即使 `userInput = "'; DROP TABLE T_Order;--"`，也只是被当成一个普通字符串值去匹配，注入不了。**用 LINQ/参数化天然免疫 SQL 注入**，这是 EF 相对手拼 SQL 的一大安全优势。（原生 SQL 也有安全写法，见 6.11。）

### 6.6.4 常见 LINQ → SQL 翻译对照

| LINQ | SQL | 说明 |
|------|-----|------|
| `.Where(x => x.A == b)` | `WHERE A = @b` | 参数化 |
| `.Where(x => list.Contains(x.A))` | `WHERE A IN (@a0,@a1,...)` | 集合 Contains → IN |
| `.Where(x => x.Name.Contains("abc"))` | `WHERE Name LIKE N'%abc%'` | 字符串 Contains → LIKE |
| `.Where(x => x.Name.StartsWith("ab"))` | `WHERE Name LIKE N'ab%'` | 前缀能用索引 |
| `.OrderByDescending(x=>x.D).Skip(n).Take(m)` | `ORDER BY D DESC OFFSET n ROWS FETCH NEXT m ROWS ONLY` | 分页 |
| `.Count()` / `.Any()` | `SELECT COUNT(*)` / `EXISTS` | 聚合 |
| `.Select(x => new {...})` | `SELECT 只选的列` | 投影，只查需要的列 |

**`Contains` 的两副面孔**——这是面试陷阱：
- **集合 `.Contains(x.Prop)`**（`ids.Contains(x.Id)`）→ SQL `IN`。CP6 的 `RepositoryBase.DeleteAsync` 就是：

```csharp
public async Task<int> DeleteAsync(params Guid[] ids)
{
    var entities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync();
    // → SELECT ... WHERE Id IN (@id0, @id1, ...)
    _dbSet.RemoveRange(entities);
    return await _context.SaveChangesAsync();
}
```

- **字符串 `x.Prop.Contains("abc")`** → SQL `LIKE '%abc%'`。注意 `LIKE '%...%'`（前后通配）**用不上索引**，全表扫描，大表上是性能陷阱。

### 6.6.5 查看生成的 SQL（面试常问"你怎么调试 EF 的 SQL"）

三种方法：
1. **`ToQueryString()`**（EF Core 5+）——对一个 `IQueryable` 调用，**不执行**就返回将要生成的 SQL 字符串：
   ```csharp
   var sql = _db.Orders.Where(o => o.WebOrderNo == "X").ToQueryString();
   // 打印出完整 SQL，调试神器
   ```
2. **日志**——在 `AddDbContext` 里 `.LogTo(Console.WriteLine, LogLevel.Information)` 或配置 `EnableSensitiveDataLogging()`（开发环境才开，会打印参数值）。CP6 的运维经验里专门提到"QA 后端 EF 日志须降到 Warning"——因为 Information 级会把每条 SQL 都打出来，量大、刷爆磁盘（CP6 曾因磁盘满导致后端崩溃）。
3. **SQL Profiler / 扩展事件**——数据库侧抓实际执行的语句。

### 6.6.6 坑与真实事故

- **坑：客户端求值（client evaluation）。** 早期 EF Core 若某段 LINQ 翻译不了，会**默默拉全表到内存**再算——灾难。EF Core 3.0+ 改为**翻译不了就抛异常**（除了最后的 `Select` 投影），逼你写能翻译的查询。面试可提这个演进。
- **坑：`Contains` 大列表。** `ids.Contains(x.Id)` 当 `ids` 有几千个时，生成的 `IN (@0,...,@N)` 会超参数上限（SQL Server 2100 个参数）或计划缓存爆炸。大列表要用临时表/TVP/分批。
- **坑：字符串 `Contains` 全表扫。** 模糊搜索 `LIKE '%kw%'` 用不上索引，大表慢。需要时上全文索引。
- **事故：EF 日志刷爆磁盘。** CP6 运维记录：QA 环境 EF 日志开在 Information，加上磁盘本就吃紧，SQL 日志把盘写满 → swap I/O error → 后端崩。教训：生产/QA EF 日志级别至少 Warning。

### 6.6.7 面试问答

**Q：IQueryable 和 IEnumerable 的区别？**
A：`IQueryable<T>` 的 LINQ 方法接收表达式树（`Expression<Func<>>`），EF 能分析翻译成 SQL，在数据库端执行、只返回结果；`IEnumerable<T>` 接收委托，在内存里逐个跑。所以对 DbSet（IQueryable）写 `.Where` 是数据库过滤，若不小心先 `.ToList()` 变成 IEnumerable 再 `.Where` 就是把全表拉进内存再过滤，性能天差地别。

**Q：EF Core 怎么防 SQL 注入？**
A：LINQ 生成的 SQL 总是参数化的，用户输入作为 `@参数` 传给数据库，永远不会被当 SQL 代码执行，天然免疫注入。原生 SQL 用 `FromSqlInterpolated`（插值也会转参数）而不是 `FromSqlRaw` 字符串拼接。

**Q：怎么看 EF 生成的 SQL？**
A：`ToQueryString()` 不执行就返回 SQL 字符串；`LogTo` 日志输出（开发可加 `EnableSensitiveDataLogging` 看参数值）；数据库侧用 Profiler。注意生产别把 EF 日志开在 Information 级，量大——CP6 就踩过 EF 日志刷爆磁盘导致后端崩的坑。

**Q：`ids.Contains(x.Id)` 和 `x.Name.Contains("abc")` 生成的 SQL 一样吗？**
A：不一样。集合的 `Contains` 翻译成 `IN (...)`；字符串的 `Contains` 翻译成 `LIKE '%abc%'`。后者前后通配用不上索引，大表是性能陷阱。

---

## 6.7 关联与加载：导航属性与三种加载策略

### 6.7.1 导航属性

导航属性（Navigation Property）是实体上指向关联实体的引用：
- **引用导航**（一对一/多对一）：`OrderDetail.Order`（一个明细属于一个订单头）；
- **集合导航**（一对多）：`JournalEntry.Lines`（一个凭证头有多条分录行）。

CP6 在 `OnModelCreating` 里配置关系（如凭证头-分录行）：

```csharp
modelBuilder.Entity<JournalEntry>(e =>
{
    e.HasMany(x => x.Lines)          // JournalEntry 有多条 Lines
        .WithOne()                   // 每条 Line 属于一个 Entry
        .HasForeignKey(l => l.EntryId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

### 6.7.2 三种加载策略

| 策略 | 触发方式 | 何时发 SQL | CP6 是否用 |
|------|----------|-----------|-----------|
| **贪婪加载 Eager** | `.Include(x => x.Nav)` | 主查询时一起 JOIN 加载 | ✅ 主力 |
| **显式加载 Explicit** | `.Entry(e).Collection(...).LoadAsync()` | 你手动调用时 | 偶尔 |
| **延迟加载 Lazy** | 访问导航属性时自动查 | 第一次访问 `.Nav` 时 | ❌ 刻意不用 |

**Eager（贪婪加载 / Include）**——最常用，明确、可控：

```csharp
var order = await _db.Orders
    .Include(o => o.Details)          // 一并加载明细
    .FirstOrDefaultAsync(o => o.Id == id);
// 生成一条带 JOIN 的 SQL，一次往返把订单头 + 明细都取回
```

**Explicit（显式加载）**——先查主体，之后按需手动加载某个导航：

```csharp
var order = await _db.Orders.FirstAsync(o => o.Id == id);
await _db.Entry(order).Collection(o => o.Details).LoadAsync();   // 手动、按需
```

**Lazy（延迟加载）**——访问 `.Details` 时 EF 偷偷发一条查询。**方便但危险**，是 N+1 的头号来源。

### 6.7.3 为什么 CP6 不用 Lazy Loading

**这是面试好话题。** CP6 刻意**不启用**延迟加载（不装 `Microsoft.EntityFrameworkCore.Proxies`、不把导航属性设 `virtual`）。原因：

1. **隐藏的 N+1**：延迟加载让"访问一个属性"变成"偷偷发一条 SQL"。一个 `foreach (order in orders) { use(order.Details); }` 循环，如果 `Details` 是延迟加载，就是 **1 + N 条** SQL（1 条查订单 + 每个订单各 1 条查明细）。代码看起来人畜无害，性能却是灾难，而且**在代码里完全看不出来**。
2. **DbContext 生命周期陷阱**：延迟加载要求访问导航时 DbContext 还活着。若实体已经离开请求作用域（DbContext 已 Dispose），访问导航属性抛 `ObjectDisposedException`。
3. **序列化爆炸**：把带延迟加载导航的实体直接 JSON 序列化返回，序列化器遍历属性会触发一连串查询，甚至无限递归。
4. **异步不友好**：延迟加载是**同步**发 SQL（属性 getter 不能 await），在异步管道里是隐藏的同步阻塞。

**CP6 的替代方案**：显式 `Include`（要什么明说）+ 投影（只查需要的列）+ 必要时"两查 + 手动组装"（下面 N+1 篇讲）。**"要什么明说"是 CP6 数据访问的核心纪律**——所有加载都显式可见，性能可预测。

### 6.7.4 N+1 问题完整篇

**现象**：查 N 条主记录，然后对每条各查一次关联，总共 **1 + N** 条 SQL。

**制造 N+1 的三种典型写法**：
```csharp
// ① 延迟加载 + 循环（最隐蔽）
var orders = await _db.Orders.ToListAsync();          // 1 条
foreach (var o in orders)
    Console.WriteLine(o.Details.Count);               // 每次访问 .Details 各 1 条 → N 条

// ② 循环里显式查（很常见）
var orders = await _db.Orders.ToListAsync();          // 1 条
foreach (var o in orders)
{
    var details = await _db.OrderDetails               // ← 循环里查库！N 条
        .Where(d => d.WebOrderNo == o.WebOrderNo).ToListAsync();
}
```

**检测**：开 EF 日志看是不是同一形状的 SQL 重复了 N 次；用 MiniProfiler；code review 时看到"循环体里有 await 数据库调用"就要警觉。

**方案 A：Include（贪婪加载，一次 JOIN）**
```csharp
var orders = await _db.Orders
    .Include(o => o.Details)
    .ToListAsync();
// 1 条带 JOIN 的 SQL 搞定
```

**方案 B：两查 + ToLookup 内存组装**（无导航属性、或跨聚合、或想避免 JOIN 笛卡尔膨胀时）
```csharp
var orders = await _db.Orders.Where(...).ToListAsync();          // 第 1 条
var orderNos = orders.Select(o => o.WebOrderNo).ToList();
var details = await _db.OrderDetails
    .Where(d => orderNos.Contains(d.WebOrderNo)).ToListAsync();  // 第 2 条：IN (...)
var lookup = details.ToLookup(d => d.WebOrderNo);                // 内存分组
foreach (var o in orders)
    o.DetailList = lookup[o.WebOrderNo].ToList();                // 内存组装，零额外查询
```
**总共 2 条 SQL**（不管多少订单），比 Include 的单条 JOIN 略多一次往返，但避免了下面的笛卡尔膨胀。CP6 在需要聚合多个子集合、或跨模块拼装时常用这种"批量取 + 内存 lookup 组装"。

**方案 C：AsSplitQuery 避免笛卡尔爆炸**

当一个主实体 Include **多个**集合导航（比如订单同时 `Include(Details).Include(Processes).Include(Materials)`），单条 JOIN 会产生**笛卡尔积膨胀**：一个订单有 10 明细 × 8 工程 × 5 材料 = 400 行重复数据回传，主实体字段被重复 400 次，网络和内存都爆。

```csharp
var orders = await _db.Orders
    .Include(o => o.Details)
    .Include(o => o.Processes)
    .Include(o => o.Materials)
    .AsSplitQuery()          // ← 拆成多条查询，每个集合一条，各自 IN 主键
    .ToListAsync();
```

`AsSplitQuery()` 把一条大 JOIN 拆成**多条单集合查询**（主表 1 条 + 每个集合各 1 条），EF 在内存里拼回来。代价是多几次往返 + 各查询间可能有一致性窗口（非同一快照，除非在事务里）；收益是消除笛卡尔膨胀。**规则**：Include 单个集合用默认 single query；Include 多个集合考虑 `AsSplitQuery`。

### 6.7.5 坑与真实事故

- **坑：Include 多集合的笛卡尔爆炸。** 三个集合 Include，行数是三者笛卡尔积，几百上千倍膨胀。用 `AsSplitQuery` 或拆两查。
- **坑：Include 之后 `.Select` 投影冲突。** 若你已经投影只选需要的字段，就别再 Include（投影里带上关联字段即可，EF 会自动 JOIN 需要的）。Include 是"整只加载导航实体"，投影是"只取列"，别混用。
- **坑：把 Lazy 当默认。** 有些团队装了 Proxies 图省事，结果 N+1 遍地。CP6 的选择——不装、全显式——是更工程化的。

### 6.7.6 面试问答

**Q：什么是 N+1 问题？怎么解决？**
A：查 N 条主记录后对每条各查一次关联，总共 1+N 条 SQL。常见于延迟加载 + 循环，或循环体里显式查库。解决：Include 贪婪加载一次 JOIN 搞定；或"两查 + ToLookup"批量取关联再内存组装（2 条 SQL）；Include 多个集合时用 AsSplitQuery 避免笛卡尔膨胀。

**Q：为什么很多团队（包括 CP6）不用延迟加载？**
A：延迟加载把"访问属性"变成"偷偷发 SQL"，是最隐蔽的 N+1 来源，代码里完全看不出来；还有 DbContext 已 Dispose 时访问导航抛异常、序列化触发连环查询、同步阻塞异步管道等问题。CP6 全用显式 Include + 投影，"要什么明说"，性能可预测。

**Q：Include 三个集合导航会有什么问题？怎么办？**
A：单条 JOIN 会笛卡尔膨胀——行数是三个集合大小的乘积，主实体字段重复几百倍回传，内存和网络爆炸。用 `AsSplitQuery()` 拆成多条单集合查询在内存拼回，或改成分次两查 + ToLookup 组装。

---

## 6.8 迁移完整篇

### 6.8.1 迁移是什么，migrations add 的原理

**迁移（Migration）= 用代码描述的数据库 schema 变更，可版本化、可回滚、可评审。**

工作机制：
1. 你改实体/`OnModelCreating`（比如给 `Stock` 加一列 `QcStatus`）；
2. 跑 `dotnet ef migrations add Phase7AddStockQcStatus`；
3. EF **对比**当前模型和**上一个迁移的模型快照**（`Migrations/CP6Context.ModelSnapshot.cs` + 各迁移的 `.Designer.cs`），算出差异；
4. 生成一个迁移类，含 `Up()`（应用变更）和 `Down()`（回滚变更），以及更新后的快照。

这就是"模型快照 diff"：迁移不是你手写的，是 EF 用"目标模型 - 当前快照"算出来的**增量**。

### 6.8.2 CP6 迁移演进史（真实文件名，看架构生长）

**标本目录：`C:\CP6\CP6.Core\Migrations\`**，约 112 个迁移文件（每个配一个 `.Designer.cs`）。摘录时间线，能直接读出这个系统是怎么一模块一模块长出来的：

```
20260408123616_Init                          ← 系统起点：用户/角色/菜单基础
20260409141540_AddSysLang                    ← 多语言
20260412143011_AddDictTables                 ← 字典
20260413120133_AddOperLog                    ← 操作日志
20260418152603_AddEstimateCalcMSBBPA010      ← ERP 見積計算書
20260420115704_AddQuotationMSBBPA030         ← 御見積書
20260426043055_AddProductMasterMSBBPA050     ← 製品マスタ
20260502225006_AddBpAndFscPA110              ← 取引先 / FSC
20260506103852_AddSheetPriceAndPlateMold     ← シート単価・木型
20260515124952_AddMesModule                  ← MES 制造执行 上线
20260518113635_AddMachineAndOee              ← 设备 / OEE
20260518115050_AddMesStoredProcedures        ← MES 存储过程（6.11 精读）
20260522132938_AddWmsCore                    ← WMS 仓储 上线
20260523060454_AddWmsInbound                 ← 入库
20260523062608_AddWmsOutbound                ← 出库
20260523064741_AddWmsStockTake               ← 棚卸
20260524115345_AddWmsKitting                 ← キッティング
20260524164522_AddWmsLogistics               ← 物流
20260525132938_AddWmsPaperIndustry2          ← 纸器业特化
20260527111136_AddWmsConnectivity            ← 连携/IoT
20260529141447_AddWmsMobile                  ← 移动作业
20260531153048_RemoveArticleAndDashboardRevamp
20260603151821_Phase6OrderCancelAndIntegrationEvent  ← 订单取消 + 集成事件
20260605142325_Phase7AddStockQcStatus        ← 库存 QC 状态（6.8.4 精读）
20260606034052_Phase9AddMaterialShortage     ← 缺料
20260606040754_Phase10aAddCreditNoteAndReturnedQty
20260609133018_Gap42AddOutboundRouting       ← 多仓引当
20260609173439_Gap43AddFxRate                ← 多通貨
20260613170522_PubB0OrgModel                 ← 组织模型（部门树）
20260613173639_PubB1Multirole                ← 多角色 RBAC
20260613175034_PubB2FunctionPerm             ← 功能权限
20260613180525_PubB3DataScope                ← 数据权限
20260613181519_PubB4FieldPerm                ← 字段权限
20260613201058_OaStage1FormEngine            ← OA 表单引擎
20260614043719_OaStage1FlowEngine            ← OA 流程引擎
20260614065230_I18nP1_SysLangUniqueKey       ← 多语言唯一键
20260614154841_FinGlAccountCostCenter        ← 财务 总账/成本中心
20260614155853_FinJournalKernel              ← 财务 记账凭证内核
... （之后还有 Space 空间底座、WFS 工作流深化、多租户等约 60 个迁移）
20260708100345_SysRoleMenuTenantize          ← 角色菜单租户化
20260710172302_SysRoleMenuUniqueIndex        ← 唯一索引（6.8.5 精读：Down 先删重复副本）
```

**观察**：迁移名 = `时间戳_语义名`。语义名带模块前缀（`AddWms*`/`Fin*`/`Oa*`/`Pub*`）和阶段号（`Phase7`/`Gap42`/`B3`）。**看迁移列表就能读出整个产品的开发史**——先 ERP，再 MES，再 WMS，再权限中台（Pub），再 OA/工作流，再财务，最后多租户加固。这是"每功能波恰一迁移"纪律的直接产物。

### 6.8.3 CP6 迁移纪律（面试可讲的工程实践）

CP6 对迁移有明确纪律（来自项目记忆/规范）：
1. **每功能波恰一个迁移**——一个功能特性对应一个迁移，不零碎、不攒一大堆。上面时间线里一个 `AddWmsInbound` = 入库这一波的全部 schema 变更。
2. **迁移进 git，走评审**——迁移是代码，必须提交、被 review。schema 变更和业务代码一起评审，防止"某人本地偷偷改了库"。
3. **`Down()` 必须可回滚**——每个迁移都要能安全撤销（下面的真实案例展示了这有多讲究）。
4. **生产应用策略**——CP6 生产**不用**"启动时自动迁移"（`Database.Migrate()` on startup），而是**受控地生成幂等脚本 / 手动 `database update`**。原因见 6.8.6。

### 6.8.4 精读迁移结构：Phase7AddStockQcStatus（加列 + 索引）

**标本路径：`C:\CP6\CP6.Core\Migrations\20260605142325_Phase7AddStockQcStatus.cs`**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AddStockQcStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QcStatus",
                table: "T_Stock",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "PENDING");     // ← 给已有行一个默认值，否则 NOT NULL 加列会失败

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_QcStatus",
                table: "T_Stock",
                column: "QcStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_Stock_QcStatus",
                table: "T_Stock");            // ← Down 顺序和 Up 相反：先删索引

            migrationBuilder.DropColumn(
                name: "QcStatus",
                table: "T_Stock");           // ← 再删列
        }
    }
}
```

**逐行解析：**
- `partial class Phase7AddStockQcStatus : Migration` — 每个迁移是一个 `Migration` 子类。
- **`Up()`** — 应用变更：给 `T_Stock` 加一列 `QcStatus`（`nvarchar(10)`，非空，**默认值 `PENDING`**），再建一个普通索引。
- `defaultValue: "PENDING"` — **关键**：往已有数据的表加**非空**列，必须给默认值，否则已有行的新列没值、违反 NOT NULL、迁移失败。这是加列的常识陷阱。
- **`Down()`** — 回滚变更，**顺序与 Up 相反**：先 `DropIndex` 再 `DropColumn`（因为列上有索引，得先删索引才能删列，有依赖顺序）。
- 对应生成的 SQL 大致是 `ALTER TABLE [T_Stock] ADD [QcStatus] nvarchar(10) NOT NULL DEFAULT N'PENDING';` + `CREATE INDEX [IX_T_Stock_QcStatus] ON [T_Stock] ([QcStatus]);`

这是**最典型、最干净**的迁移结构：Up/Down 对称，一个加、一个删。

### 6.8.5 真实案例：唯一索引迁移的 Down 先删重复副本

**这是 CP6 迁移纪律里最精彩的一个案例——把它当"迁移要考虑数据现状"的教科书。**

**标本路径：`C:\CP6\CP6.Core\Migrations\20260710172302_SysRoleMenuUniqueIndex.cs`**

```csharp
public partial class SysRoleMenuUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Sys_RoleMenu_Tenant_Role",
            table: "Sys_RoleMenus");

        // P0 终审 #3：升唯一索引前先清存量重复 (TenantId,RoleId,MenuId) 行（原仅非唯一索引，
        // 兜底网/重跑种子可能已插重复）——每组保留最小 Id，否则 CreateIndex(unique) 会失败。
        migrationBuilder.Sql(@"
DELETE rm FROM dbo.Sys_RoleMenus rm
WHERE rm.Id > (
    SELECT MIN(x.Id) FROM dbo.Sys_RoleMenus x
    WHERE x.TenantId = rm.TenantId AND x.RoleId = rm.RoleId AND x.MenuId = rm.MenuId
);");

        migrationBuilder.CreateIndex(
            name: "IX_Sys_RoleMenu_Tenant_Role",
            table: "Sys_RoleMenus",
            columns: new[] { "TenantId", "RoleId", "MenuId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Sys_RoleMenu_Tenant_Role",
            table: "Sys_RoleMenus");

        migrationBuilder.CreateIndex(
            name: "IX_Sys_RoleMenu_Tenant_Role",
            table: "Sys_RoleMenus",
            columns: new[] { "TenantId", "RoleId" });   // 退回非唯一索引
    }
}
```

**为什么这个迁移不平凡：**
- 目标是把 `Sys_RoleMenu` 上原本的**非唯一**索引 `(TenantId, RoleId)` 升级成**唯一**索引 `(TenantId, RoleId, MenuId)`。
- **但生产库里可能已经有重复行**（历史上兜底逻辑/重跑种子插过重复的角色-菜单映射）。如果直接 `CreateIndex(unique)`，SQL Server 遇到重复值会**报错、迁移失败**。
- 所以 `Up()` 里**先跑一段清理 SQL**：用 `DELETE ... WHERE Id > (SELECT MIN(Id) ...)` **每组重复保留最小 Id、删掉其余副本**，把数据清成"无重复"，然后才能安全建唯一索引。
- 这就是 **"迁移不能只改 schema，还得处理存量数据现状"** 的活教材。同类的还有 CP6 记忆里提到的另一个案例：`Sys_RoleMenu` 唯一索引的 `Down()` 曾因"删非 A1 副本"才能安全回滚（回滚时也要考虑数据冲突）。

**教训（面试金句）**：**加约束前先让数据满足约束。** 生产迁移最容易翻车的不是 schema 语法，而是"新约束和存量脏数据冲突"。成熟的迁移会在 `Up()` 里带数据清洗/回填 SQL。

### 6.8.6 生产应用策略：自动迁移的利弊

`database update` / `Database.Migrate()` 会把待应用的迁移**顺序执行**到目标库。生产上有两条路线：

| 策略 | 做法 | 利 | 弊 |
|------|------|----|----|
| **启动时自动迁移** | 应用启动时 `db.Database.Migrate()` | 省事、自动 | 多实例并发启动会竞态；无法在事务/审阅下执行；失败时应用起不来；权限过大（应用账号能改 schema） |
| **幂等脚本 / 受控执行** | `dotnet ef migrations script --idempotent` 生成脚本，DBA/CI 受控执行 | 可审阅、可回滚、可在维护窗口执行、幂等（重复执行安全） | 需要发布流程配合 |

**CP6 选受控执行**（结合它的 Docker 部署纪律）：生成迁移 → 评审 → 受控 `database update`/幂等脚本应用到线上库，**不在应用启动时自动迁移**。`--idempotent` 生成的脚本每条变更都包了 `IF NOT EXISTS(SELECT ... FROM __EFMigrationsHistory WHERE MigrationId = '...')`，重复跑也不会重复应用，适合 CI/CD 和多实例。

CP6 部署经验里还有一条相关避雷：**部署时要删 `appsettings.Local/Development.json`**——否则本地连接串会遮蔽 Docker 环境变量，应用连不上生产库（这和迁移应用到哪个库直接相关）。

### 6.8.7 面试问答

**Q：EF Core 迁移是怎么生成的？**
A：`migrations add` 时 EF 对比当前模型（实体 + OnModelCreating）和上一个迁移保存的模型快照，算出差异，生成含 Up（应用）/Down（回滚）的迁移类并更新快照。迁移不是手写的，是"目标模型 - 快照"的增量。

**Q：生产环境怎么应用迁移，要不要启动时自动迁移？**
A：我倾向受控执行——用 `migrations script --idempotent` 生成幂等脚本，评审后在维护窗口/CI 受控应用，不在应用启动时自动迁移。自动迁移有多实例并发竞态、失败即起不来、应用账号权限过大、无法审阅等问题。CP6 就是生成迁移→git 评审→受控 update 的流程。

**Q：给一张有数据的表加非空列要注意什么？**
A：必须给默认值，否则已有行新列无值违反 NOT NULL，迁移失败。CP6 的 Phase7AddStockQcStatus 就是加 `QcStatus nvarchar(10) NOT NULL DEFAULT 'PENDING'`。

**Q：讲一个你处理过的棘手迁移。**
A：CP6 把 Sys_RoleMenu 的非唯一索引升级成唯一索引。生产库里历史上有重复的角色-菜单映射行，直接建唯一索引会因重复值报错。所以迁移的 Up 里先跑一段 `DELETE WHERE Id > (SELECT MIN(Id) ... GROUP BY 三列)` 每组保留最小 Id 删掉重复副本，把数据清成满足约束再建唯一索引。教训是加约束前先让存量数据满足约束，生产迁移要带数据清洗。

---

## 6.9 事务与并发

### 6.9.1 SaveChanges 的隐式事务

**每次 `SaveChanges()` 本身就是一个事务。** 无论它内部要发多少条 INSERT/UPDATE/DELETE，EF 都把它们包在**一个事务**里：要么全成功提交，要么全回滚。所以你 `Add` 了订单头 + 3 条明细后一次 `SaveChanges`，这 4 条 INSERT 是原子的。

这意味着**很多场景根本不需要你显式开事务**——只要能在一次 `SaveChanges` 里完成的写入，天然原子。

### 6.9.2 什么时候需要显式事务

当一个业务操作**跨多次 `SaveChanges`**、或**混合了 EF 写入 + 原生 SQL + 外部调用**、且要求整体原子时，需要显式事务 `BeginTransaction`。

**经典场景：扣库存 + 写流水 + 更新单据必须原子。** 库存移动就是这种——改库存行、插一条不可变流水、可能还回写单据，任何一步失败都不能留下"库存扣了但没流水"的脏状态。

**标本路径：`C:\CP6\CP6.Core\Services\Wms\StockMovementService.cs`**

```csharp
// 仕様書 §13.3 ApplyAsync 実装。
// 1) BeginTransaction → 2) 対象 Stock SELECT → 3) AllowNegative チェック
// → 4) Stock 更新（RowVersion で楽観ロック） → 5) StockTransaction INSERT → 6) Commit。
// 並行安全：DbUpdateConcurrencyException が出たら呼び出し側でリトライ。

// SQL Server のみトランザクションを張る（InMemory は SupportsTransactions=false）
IDbContextTransaction? tx = null;
if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
{
    tx = await _db.Database.BeginTransactionAsync(ct);
}

try
{
    // 在庫行を取得 or 新規（業務 UK で一意）
    var stock = await _db.Stocks
        .FirstOrDefaultAsync(s =>
            s.WarehouseCd == req.WarehouseCd &&
            s.LocationCd == req.LocationCd &&
            s.ProductCd == req.ProductCd &&
            s.LotNo == req.LotNo &&
            !s.IsDeleted, ct);

    if (stock == null) { /* 新規 Stock を Add */ }

    ApplyDelta(stock, req);   // 在庫数の増減

    // マイナス在庫チェック（超卖防止）
    if (!allowNegative)
    {
        if (stock.PhysicalQty < 0)
            throw new InsufficientStockException(...);   // ← 抛异常 → 下面 catch → 回滚
        if (stock.AvailableQty < 0)
            throw new InsufficientStockException(...);
    }

    // トランザクション履歴を INSERT（不可変ログ）
    var txn = new StockTransaction { /* ... */ };
    _db.StockTransactions.Add(txn);

    await _db.SaveChangesAsync(ct);          // ← Stock 更新 + Txn 插入，一起写
    if (tx != null) await tx.CommitAsync(ct); // ← 提交事务
    // ... commit 后才点火 Fin 桥接、SignalR 通知（best-effort）
}
catch { /* tx?.Rollback */ throw; }
```

**逐行解析：**
- `_db.Database.BeginTransactionAsync(ct)` — 开显式事务，返回 `IDbContextTransaction`。之后的所有 `SaveChanges` 都在这个事务里，直到 `Commit`/`Rollback`。
- **`ProviderName != "...InMemory"` 判断** — 这是 CP6 的实战细节：InMemory 测试提供程序**不支持事务**（`SupportsTransactions=false`），所以测试环境不开事务、生产（SQL Server）才开。这样同一份业务代码既能跑真库又能跑内存测试。
- 库存不足时 `throw InsufficientStockException` — 异常冒泡到 `catch`，触发 `Rollback`，**库存改动和流水插入一起撤销**，绝不留"扣了库存没记流水"的脏状态。这就是原子性的价值。
- `Commit` **之后**才做 Fin 桥接过账、SignalR 通知，且用 `try{}catch{}` 吞掉——因为它们是 best-effort 的副作用，失败不能回滚已成功的库存移动。这是"核心事务原子 + 副作用最终一致"的分层设计。

### 6.9.3 乐观并发（Optimistic Concurrency）：RowVersion

**问题**：两个用户同时读到库存 100，各自扣 10。若无并发控制，两个 `UPDATE Stock SET Qty=90` 都成功，实际扣了 20 却只减到 90——**丢失更新（lost update）**，超卖。

**乐观并发**假设冲突罕见，不加锁，而是**在更新时检查"我读到之后有没有人改过"**。EF Core 用**并发令牌（concurrency token）**实现，最常见是 `RowVersion`（SQL Server 的 `rowversion`/`timestamp` 列，每次行被更新时数据库自动 +1）。

CP6 在需要并发保护的实体上用 `[Timestamp]`：

```csharp
// C:\CP6\CP6.Entity\DomainModels\Wf\Wf_FlowInstance.cs
[Timestamp]
public byte[]? RowVersion { get; set; }

// C:\CP6\CP6.Entity\DomainModels\Wf\Wf_Connector.cs
[Timestamp] public byte[]? RowVersion { get; set; }

// C:\CP6\CP6.Entity\DomainModels\Wf\Wf_ServiceJob.cs
[Timestamp] public byte[]? RowVersion { get; set; }
```

**机制**：`[Timestamp]` 让 EF 把该列当并发令牌。当你 `SaveChanges` 更新一行时，EF 生成的 UPDATE 会**在 WHERE 里带上读取时的 RowVersion 值**：

```sql
UPDATE [Wf_FlowInstance]
SET [Status] = @status, ...
WHERE [Id] = @id AND [RowVersion] = @original_rowversion;  -- ← 关键
```

- 如果没人改过，`RowVersion` 还是原值，`WHERE` 命中，更新 1 行，成功；
- 如果**别人先改了**，那行的 `RowVersion` 已经变了，`WHERE` **匹配 0 行**，EF 检测到"影响行数=0"，抛 **`DbUpdateConcurrencyException`**。

**处理 `DbUpdateConcurrencyException`**：
```csharp
try
{
    await _db.SaveChangesAsync();
}
catch (DbUpdateConcurrencyException ex)
{
    // 有人在你之前改了这行。策略：
    // ①重试（reload 最新值 + 重新应用你的变更）— CP6 的 StockMovement 就是"呼出側でリトライ"
    // ②让用户看到最新值、重新决定（database wins / client wins）
    // ③放弃并报错
}
```

CP6 的 `StockMovementService` 注释明说 **"DbUpdateConcurrencyException が出たら呼び出し側でリトライ"**（并发异常由调用方重试）——扣库存冲突了就重读重扣，直到成功或库存不足。工作流引擎的子流程并发测试（`SubFlowConcurrencyTests`）也靠 `Wf_FlowInstance.RowVersion` 保证"迟到的复核方不会双重恢复"：两个并发恢复请求，第二个撞 RowVersion → 抛并发异常 → 状态闸零动作。

**测试细节（体现深度）**：CP6 的测试基座是 SQLite，而 SQLite 没有 SQL Server 的 `rowversion` 自动递增。CP6 的 `WfTestDb` 手动建了个 `AFTER UPDATE` 触发器 `SET RowVersion = randomblob(8)` 来模拟令牌递增，让乐观锁在测试里真正生效。这说明团队**认真测了并发路径**，不是写了 `[Timestamp]` 就当没事。

### 6.9.4 悲观锁（Pessimistic Lock）：UPDLOCK，什么时候需要

**乐观并发**适合冲突**罕见**的场景（大多数业务）。但当冲突**频繁**、且重试代价高时，可能需要**悲观锁**——读的时候就用数据库锁把行锁住，别人得等。

SQL Server 用锁提示 `WITH (UPDLOCK, HOLDLOCK)`：

```sql
SELECT * FROM T_Stock WITH (UPDLOCK, HOLDLOCK)
WHERE WarehouseCd=@w AND ProductCd=@p AND LotNo=@l;
-- UPDLOCK：读时加更新锁，其他事务不能同时拿更新锁 → 串行化扣减
-- HOLDLOCK：锁保持到事务结束
```

EF Core 本身没有"加悲观锁"的 LINQ API，需要 `FromSqlRaw`/原生 SQL 加锁提示，在一个事务里"锁读 → 改 → 提交"。

**库存扣减防超卖：乐观还是悲观？**（这是很好的面试讨论题）
- **乐观（RowVersion）+ 重试**：适合冲突不密集的常规库存。CP6 的 `StockMovementService` 选这条——大多数库位不会有很多人同时抢扣，冲突时重试即可。实现简单，不长期占锁，吞吐好。
- **悲观（UPDLOCK）**：适合**极热点**库存（比如秒杀某个 SKU，成百上千请求抢同一行），乐观锁会导致大量重试风暴，此时悲观锁串行化反而更稳。
- **数据库层面的原子扣减**：更优雅的做法是让扣减在**一条 SQL 内条件更新**——`UPDATE Stock SET Qty=Qty-@n WHERE ... AND Qty>=@n`，靠数据库行锁 + 条件保证不超卖，`@@ROWCOUNT=0` 即库存不足。这避免了"先读后写"的窗口。

CP6 当前用乐观 + 应用层负库存检查（`if (stock.PhysicalQty < 0) throw`），并在事务里保证原子。这是符合其业务并发强度的选择；注释里也留了"未来可加 RowVersion 到 Stock 或改条件更新"的演进空间。

### 6.9.5 执行策略与重试（连接弹性）

云数据库/网络抖动会导致瞬时故障。EF Core 的**执行策略（Execution Strategy）** `EnableRetryOnFailure()` 能自动重试瞬时错误。**但有个陷阱**：开了自动重试后，**手动 `BeginTransaction` 要用 `execStrategy.ExecuteAsync(...)` 包起来**——否则 EF 抛 "The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions"，因为重试整个事务需要能重放，EF 要你把事务体交给它管理。面试能提到这个坑是加分项。

### 6.9.6 坑与真实事故

- **坑：以为 SaveChanges 要手动开事务。** 单次 SaveChanges 已经是事务，别画蛇添足。只有跨多次 SaveChanges / 混原生 SQL 才需要显式事务。
- **坑：InMemory 不支持事务。** 测试用 InMemory 时 `BeginTransaction` 是 no-op 甚至抛异常。CP6 用 `ProviderName` 判断，生产开测试不开。更好的做法是并发/事务测试改用 SQLite（CP6 的工作流测试就是）。
- **坑：RowVersion 冲突不处理直接崩。** `DbUpdateConcurrencyException` 必须显式 catch 并决定重试/覆盖/报错，否则用户看到 500。
- **坑：重试策略 + 手动事务不兼容。** 见 6.9.5。

### 6.9.7 面试问答

**Q：SaveChanges 需要手动开事务吗？**
A：不需要。每次 SaveChanges 内部就是一个事务，里面所有 INSERT/UPDATE/DELETE 要么全提交要么全回滚。只有当一个业务要跨多次 SaveChanges、或混合 EF 写入 + 原生 SQL + 需要整体原子时，才用 BeginTransaction 显式事务。CP6 的库存移动就是显式事务：改库存 + 插流水在一个事务里，库存不足抛异常整体回滚。

**Q：什么是乐观并发，EF Core 怎么实现？**
A：假设冲突罕见、不加锁，更新时检查"读取之后有没有人改过"。EF 用并发令牌实现，常见是 `[Timestamp] byte[] RowVersion`（SQL Server rowversion 列自动递增）。SaveChanges 更新时 WHERE 带上读取时的 RowVersion，若被别人改过则匹配 0 行、抛 DbUpdateConcurrencyException。CP6 在工作流实例、连接器等实体上用它，并发恢复冲突时第二个请求撞 RowVersion 被拦。

**Q：库存扣减防超卖，乐观锁还是悲观锁？**
A：看并发强度。常规库存用乐观锁（RowVersion）+ 冲突重试，简单、不长占锁、吞吐好，CP6 就这么做；极热点 SKU（秒杀）用悲观锁 UPDLOCK 串行化，避免乐观锁重试风暴；最优雅是一条 SQL 条件更新 `UPDATE SET Qty=Qty-n WHERE Qty>=n` 靠数据库行锁原子扣减、ROWCOUNT=0 即不足，消除先读后写窗口。

**Q：DbUpdateConcurrencyException 怎么处理？**
A：捕获后三种策略：重试（reload 最新值重新应用变更）、client/database wins（用某一方的值覆盖）、放弃报错让用户重新决定。CP6 的库存移动是调用方重试，工作流是撞令牌后状态闸零动作（幂等放弃）。

---

## 6.10 批量操作与性能

### 6.10.1 AddRange / RemoveRange

批量插入/删除用 `AddRange`/`RemoveRange`，一次 `SaveChanges` 全部写入（仍在一个事务）：

```csharp
// C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs
public async Task<int> DeleteAsync(params Guid[] ids)
{
    var entities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync();
    _dbSet.RemoveRange(entities);   // 批量标 Deleted
    return await _context.SaveChangesAsync();
}
```

注意这里是**"先查出来再 RemoveRange"**——EF 需要实体在 ChangeTracker 里才能删。这会把 N 行都加载进内存并追踪，删 N 行发 N 条 DELETE。行数大时低效——这正是 `ExecuteDelete` 要解决的。

### 6.10.2 ExecuteUpdate / ExecuteDelete（EF Core 7+）：批量直改

EF Core 7 引入 `ExecuteUpdate`/`ExecuteDelete`：**不加载实体、不经过 SaveChanges**，直接生成一条 `UPDATE ... WHERE` / `DELETE ... WHERE` 打到数据库。

**标本路径：`C:\CP6\CP6.Core\Services\Erp\OrderService.cs`（订单软删的级联）**

```csharp
// 订单头软删后，级联把明细/工程/工程备注/材料全部软删
await _db.OrderDetails.Where(x => x.WebOrderNo == webOrderNo)
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.IsDeleted, true)
        .SetProperty(x => x.Modifier, userName)
        .SetProperty(x => x.ModifyDate, now));
await _db.OrderProcesses.Where(x => x.WebOrderNo == webOrderNo)
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.IsDeleted, true)
        .SetProperty(x => x.Modifier, userName)
        .SetProperty(x => x.ModifyDate, now));
// ...OrderProcessNotes, OrderMaterials 同理
```

生成的 SQL（一条，不加载任何行）：

```sql
UPDATE [d] SET [d].[IsDeleted] = 1, [d].[Modifier] = @userName, [d].[ModifyDate] = @now
FROM [T_OrderDetail] AS [d]
WHERE [d].[WebOrderNo] = @webOrderNo;
```

**对比 `RemoveRange` 方式**：`ExecuteUpdate` 不把几十条明细查进内存、不追踪、一条 SQL 搞定——**批量更新的性能之选**。

### 6.10.3 ★真实发现：ExecuteUpdate 绕过字段级审计造成审计盲区

**这是 CP6 最重要的性能/正确性权衡案例，务必讲透——面试里这种"我发现了一个隐蔽的正确性问题"的故事极有说服力。**

回顾 CP6 的审计管道（6.12 详讲）：字段级审计是在 **`SaveChanges` 重写**里做的——遍历 ChangeTracker 里的 `IAuditable` 实体、算 before/after diff、写审计行。

**问题来了**：`ExecuteUpdate`/`ExecuteDelete` **完全绕过 SaveChanges 管道**——它不加载实体、不进 ChangeTracker、不触发 `SaveChanges` 重写。因此：

> **凡是用 `ExecuteUpdate` 改的数据，字段级审计一行都不会记。这就是"审计盲区（audit blind spot）"。**

CP6 项目在评审中明确发现并记录了这个问题。典型例子是 `OrderService` 里的**単価訂正一括伝播**（批量修改订单明细的 `SetUnitPrice`）：

```csharp
// C:\CP6\CP6.Core\Services\Erp\OrderService.cs：单价一括更新
foreach (var kv in headerSetUnitPriceMap)
{
    await _db.OrderDetails
        .Where(x => x.WebOrderNo == kv.Key && !x.IsDeleted)
        .ExecuteUpdateAsync(s => s
            .SetProperty(x => x.SetUnitPrice, kv.Value)   // ← 改了单价，但审计管道看不见！
            .SetProperty(x => x.ModifyDate, now)
            .SetProperty(x => x.Modifier, userName));
}
```

这段批量改了订单明细的单价——一个**财务敏感字段**——但因为走 `ExecuteUpdate`，**字段审计表里没有任何记录**。谁在什么时候把单价从多少改成多少，审计追溯不到。CP6 把它登记为一条 🔴 高优先跟踪票（"ERP ExecuteUpdateAsync 审计盲区"），连同同类的级联软删一起，作为已知技术债。

**这个发现的意义（面试金句）：**
1. **性能优化和横切关注点（审计/追踪）常有隐性冲突。** `ExecuteUpdate` 是正确的性能选择，但它绕过的正是审计所依赖的管道。**"绕过 ORM 管道"意味着同时绕过挂在管道上的一切**——审计、软删过滤、租户盖章、领域事件。
2. **横切逻辑挂在 SaveChanges 上，就必须假设"所有写入都走 SaveChanges"。** 一旦引入 `ExecuteUpdate` 这种旁路，这个假设就破了，得**要么补审计、要么明确豁免并记账**。
3. **正确的处理不是"禁用 ExecuteUpdate"**（那会牺牲必要的性能），而是：对**审计敏感**的字段（单价、金额、权限）走 SaveChanges 路径确保留痕；对**非敏感的大批量**（如级联软删标记）用 ExecuteUpdate 换性能，并在架构文档里**显式记录这些审计盲区**。CP6 正是这么做的——发现、评估、记账、按敏感度分流。

**面试话术**：如果被问"你在 EF 项目里发现过什么坑"，讲这个。它展示了你既懂性能优化（ExecuteUpdate）、又懂横切架构（审计管道）、还有"发现隐性冲突并做出工程权衡"的成熟度——这正是 5 年经验该有的判断力。

### 6.10.4 性能清单（面试常让你列）

EF Core 性能优化的标准清单，CP6 各处都有体现：

1. **只读查询用 `AsNoTracking()`** — 省追踪开销（6.5，WMS 大量使用）。
2. **投影只查需要的列** — `.Select(x => new { x.A, x.B })` 而不是查整个实体。少读列 = 少 IO、少内存、少映射。
3. **避免循环里查库（N+1）** — 用 Include / 批量取 + ToLookup（6.7）。
4. **批量写用 ExecuteUpdate/ExecuteDelete** — 不加载实体（6.10.2，注意审计盲区）。
5. **合理的分页** — `Skip/Take` + `OrderBy`（6.6），别一次拉全表。
6. **合理的索引** — 高频查询条件列建索引（CP6 的 `OnModelCreating` 里几百个 `HasIndex`），SQL Server 存储过程里也主动加覆盖索引（6.11）。
7. **减少往返** — 能一条 SQL 别拆多条；确需批量往返用 batching（EF 默认会把多个 SaveChanges 命令批处理）。
8. **`ToQueryString` 审 SQL** — 定位慢查询、确认索引命中（6.6.5）。
9. **重型聚合下沉 Dapper/存储过程** — 让 DBA 调优执行计划（6.1，MES 仪表盘）。
10. **别 Include 一堆集合导致笛卡尔膨胀** — 用 AsSplitQuery（6.7.4）。

### 6.10.5 面试问答

**Q：ExecuteUpdate/ExecuteDelete 相比传统方式好在哪，有什么副作用？**
A：好在不加载实体、不进 ChangeTracker、一条 UPDATE/DELETE WHERE 直接打到数据库，批量改删性能远好于"先查出来 RemoveRange/改属性再 SaveChanges"。副作用是它绕过整个 SaveChanges 管道——挂在管道上的字段级审计、软删过滤、租户盖章、领域事件全部不触发。CP6 就发现用 ExecuteUpdate 批量改订单明细单价造成了审计盲区，把它登记为技术债，按字段敏感度分流：敏感字段走 SaveChanges 留痕，大批量非敏感操作才用 ExecuteUpdate。

**Q：EF Core 性能优化你会做哪些？**
A：只读 AsNoTracking；投影只查需要的列；消除 N+1（Include 或批量取 + ToLookup）；批量写用 ExecuteUpdate；合理分页和索引；Include 多集合用 AsSplitQuery 避免笛卡尔膨胀；用 ToQueryString 审 SQL 定位慢查询；重型聚合下沉存储过程让 DBA 调优。核心思想是少读、少追踪、少往返、走对的索引。

---

## 6.11 原生 SQL 与存储过程

### 6.11.1 什么时候用原生 SQL

LINQ 翻译不了、或手写 SQL 更直接/更快时。EF Core 提供几种方式，**安全性差别很大**：

| API | 用途 | 安全性 |
|-----|------|--------|
| `FromSqlInterpolated($"...{x}...")` | 查询返回实体，插值**自动参数化** | ✅ 安全 |
| `FromSqlRaw("... {0}", x)` | 查询返回实体，`{0}` 占位参数化 | ⚠️ 拼字符串则危险 |
| `SqlQuery<T>($"...")` | 查询返回**标量/非实体**（EF Core 7+） | ✅ 安全 |
| `ExecuteSqlInterpolated($"...")` | 执行 INSERT/UPDATE/DELETE/DDL | ✅ 安全 |
| `migrationBuilder.Sql("...")` | 迁移里执行任意 SQL | 迁移专用 |

**黄金规则：永远用插值版（`FromSqlInterpolated`/`ExecuteSqlInterpolated`）或参数占位，绝不字符串拼接用户输入。** 插值版看着像 `$"WHERE Name = {userInput}"` 字符串插值，但 EF **把 `{userInput}` 转成 `@p0` 参数**，不是拼进 SQL 文本——所以安全。而 `FromSqlRaw($"WHERE Name = {userInput}")`（先用 C# 插值拼成字符串再传 Raw）就**会**注入，这是最阴险的陷阱。

### 6.11.2 CP6 里的原生 SQL：主要在迁移和存储过程

CP6 的 EF 业务代码几乎不用 `FromSql`（业务查询全走 LINQ + 全局过滤器，安全且享受租户隔离）。原生 SQL 集中在两处：
1. **迁移里的 `migrationBuilder.Sql(...)`** — 建存储过程、清洗数据（6.8.5 的去重、6.11.3 的 SP）。
2. **Dapper + 存储过程** — MES 仪表盘（6.1.3）。

这其实是个**好的架构信号**：把原生 SQL 收敛到"迁移（schema/SP 定义）"和"专门的 Dapper 报表服务"两个明确的地方，业务主线保持纯 LINQ，享受参数化 + 租户过滤 + 审计的全套安全网。

### 6.11.3 精读：MES 存储过程迁移

**标本路径：`C:\CP6\CP6.Core\Migrations\20260518115050_AddMesStoredProcedures.cs`**

CP6 用**迁移**来管理存储过程——SP 也是 schema 的一部分，随迁移进 git、可版本化、可回滚。这是管理 SP 的好实践（避免"SP 在库里但没进代码库"的失控）。

```csharp
public partial class AddMesStoredProcedures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ① 先加重型聚合查询需要的覆盖索引
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_ProductionResult_CreateDate_IsDeleted')
    CREATE INDEX IX_T_ProductionResult_CreateDate_IsDeleted
        ON [T_ProductionResult] ([CreateDate], [IsDeleted]) INCLUDE ([GoodQty], [DefectQty], [MachineCd]);
");
        // ② 幂等地创建存储过程（先 DROP IF EXISTS 再 CREATE）
        migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.usp_GetMesDashboardSummary', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetMesDashboardSummary;
");
        migrationBuilder.Sql(@"
CREATE PROCEDURE dbo.usp_GetMesDashboardSummary
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @today date = CAST(GETDATE() AS date);
    DECLARE @tomorrow date = DATEADD(day, 1, @today);
    DECLARE @inProgress int, @completed int, @good decimal(21,8), @defect decimal(21,8), @delayed int;

    SELECT @inProgress = COUNT(*) FROM T_WorkOrder WHERE IsDeleted = 0 AND [Status] = 3;
    SELECT @completed = COUNT(*) FROM T_WorkOrder
        WHERE IsDeleted = 0 AND [Status] IN (4, 6)
          AND ActualEndDate >= @today AND ActualEndDate < @tomorrow;
    SELECT @good = ISNULL(SUM(GoodQty), 0), @defect = ISNULL(SUM(DefectQty), 0)
        FROM T_ProductionResult WHERE IsDeleted = 0
          AND CreateDate >= @today AND CreateDate < @tomorrow;
    SELECT @delayed = COUNT(*) FROM T_WorkOrder
        WHERE IsDeleted = 0 AND PlanEndDate < @today AND [Status] NOT IN (4, 6, 9);

    SELECT
        InProgressCount = @inProgress,
        CompletedCount  = @completed,
        TotalGoodQty    = @good,
        TotalDefectQty  = @defect,
        DefectRate      = CASE WHEN (@good + @defect) > 0
                               THEN CAST(@defect / (@good + @defect) * 100 AS decimal(8,2))
                               ELSE 0 END,
        DelayedCount    = @delayed;
END
");
        // ... usp_GetMesDailyTrend（带 @Days 参数 + 递归 CTE 生成日期序列）
        // ... usp_GetMesProcessProgress
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF OBJECT_ID('dbo.usp_GetMesDashboardSummary', 'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetMesDashboardSummary;");
        // ... 其余 SP 和索引也 DROP（Down 干净回滚）
    }
}
```

**逐行/要点解析：**
- **`IF NOT EXISTS ... CREATE INDEX`** / **`IF OBJECT_ID(...) IS NOT NULL DROP`** — 幂等写法。迁移里的原生 SQL 要能重复执行不报错（配合幂等脚本策略，6.8.6）。
- **`SET NOCOUNT ON`** — 抑制"N rows affected"消息，减少网络往返、避免干扰 Dapper 结果集解析。SP 标准开头。
- **`DATEADD`/`CAST(GETDATE() AS date)`** — 纯 T-SQL 日期运算，比在 C# 里算再传参更贴近数据。
- **`INCLUDE ([GoodQty], [DefectQty], [MachineCd])`** — 覆盖索引（covering index）：把 SP 要 SUM 的列放进索引的 include，让聚合查询**只读索引就够**、不用回表。这是"存储过程 + 索引调优"配合的典型，正是 JD 里"SQL Server 性能调优"的实证。
- **递归 CTE 生成日期序列**（在 `usp_GetMesDailyTrend` 里，`;WITH Dates AS (... UNION ALL ...) OPTION (MAXRECURSION 366)`）——这是 **LINQ 根本翻译不出来**的东西，只能靠原生 SP。这就是"为什么这几个查询下沉 Dapper + SP"的技术原因（呼应 6.1.3）。
- 这个 SP 由 6.1.3 的 `MesDashboardDapperService.GetSummaryAsync()` 通过 `CommandType.StoredProcedure` 调用——**迁移定义 SP、Dapper 调用 SP**，闭环。

### 6.11.4 坑与真实事故

- **坑：`FromSqlRaw` + 字符串插值 = 注入。** `FromSqlRaw($"...{userInput}...")` 是把用户输入拼进 SQL 文本，可注入。要么用 `FromSqlInterpolated`（EF 转参数），要么 `FromSqlRaw("... {0}", userInput)`（占位转参数）。
- **坑：原生 SQL 绕过全局过滤器。** `FromSql` 查出来的行**不带**租户/软删过滤（EF 只对 LINQ 组合的部分注入过滤器；`FromSql` 的 SQL 主体是你写的）。多租户下用 `FromSql` 必须自己在 SQL 里带 `WHERE TenantId=@t`——和 Dapper 一样的代价（6.1.4）。
- **坑：SP 变更没进迁移。** 有人直接在生产库改 SP，代码库不知道 → 下次部署被旧定义覆盖或不一致。CP6 把 SP 放迁移里正是防这个。

### 6.11.5 面试问答

**Q：EF Core 里怎么安全地执行原生 SQL？**
A：用插值版 `FromSqlInterpolated`/`ExecuteSqlInterpolated`，或带参数占位的 `FromSqlRaw("... {0}", arg)`——EF 会把插值/占位转成参数化命令，防注入。绝不能先用 C# 字符串插值把用户输入拼成字符串再传 FromSqlRaw，那会注入。另外原生 SQL 绕过全局查询过滤器，多租户下要自己在 SQL 里加租户条件。

**Q：CP6 的存储过程怎么管理？**
A：放在 EF 迁移里，用 `migrationBuilder.Sql` 幂等地 DROP+CREATE，随迁移进 git、可版本化、可回滚。SP 里配合覆盖索引（INCLUDE 列）做性能调优，用递归 CTE 等 LINQ 翻译不出的能力，由 Dapper 服务通过 CommandType.StoredProcedure 调用。这样 SP 定义不会游离在代码库之外。

---

## 6.12 审计管道专题：IAuditable + SaveChanges 拦截做字段级 diff

> CP6 把"谁在什么时候把哪个字段从什么值改成了什么值"做成了自动的、声明式的字段级审计。这是 SaveChanges 重写的高级应用，也是本章审计相关内容的收束。

### 6.12.1 需求：字段级审计

制造业 ERP 对**可追溯性**要求极高：订单金额、库存调整、权限授予等改动，必须留下**字段级** before/after 痕迹（合规、追责、对账）。要求：
- **opt-in**：只有明确标记的实体才审计（不是所有表都记，否则噪声爆炸）；
- **精确 diff**：只记真正变化的字段，记下旧值→新值；
- **敏感字段脱敏**：密码、密钥、哈希绝不能进审计日志；
- **原子**：审计行和业务行必须同事务落库，不能业务成功审计丢失。

### 6.12.2 标记接口 IAuditable

**标本路径：`C:\CP6\CP6.Entity\IAuditable.cs`**

```csharp
namespace CP6.Entity;

/// <summary>
/// 字段级审计 opt-in 空标记接口（#4 字段审计 T1）。
/// 实体实现本接口即被 CP6Context.SaveChanges 写入管道纳入字段级 before/after 变更捕获。
/// 不实现则完全不参与审计（默认不审计，按需开启）。本身不映射任何列。
/// </summary>
public interface IAuditable
{
}
```

**空标记接口（marker interface）** — 没有任何方法，纯粹当"标签"。一个实体 `class GlAccount : BaseTenantEntity, IAuditable` 就是声明"我要被审计"。SaveChanges 靠 `ChangeTracker.Entries<IAuditable>()` 一把捞出所有要审计的实体。这和 6.4 用 `BaseTenantEntity` 继承当租户标记是同一种设计哲学——**用类型系统声明横切意图**。

配套 `[AuditIgnore]` 特性（`C:\CP6\CP6.Entity\AuditIgnoreAttribute.cs`）标在敏感属性上，让该字段不进 diff。

### 6.12.3 审计捕获核心

**标本路径：`C:\CP6\CP6.Core\EFDbContext\CP6Context.cs` · 审计管道**

先看敏感字段的三重防护和 diff 构造：

```csharp
/// <summary>行级元字段（who/when 留痕已由专列承载，diff 跳过避免噪声）。</summary>
private static readonly string[] _metaSkip = { "Creator", "CreateDate", "Modifier", "ModifyDate" };

/// <summary>密钥拒名单（兜底）：即使字段漏标 [AuditIgnore]，名称命中亦不入 diff。</summary>
internal static bool IsSecretField(string name)
{
    var n = name.ToLowerInvariant();
    return n == "password" || n.EndsWith("secret") || n.EndsWith("hash")
        || n == "tokenhash" || n == "salt" || n == "clientsecretprotected" || n == "twofactorsecret";
}

/// <summary>值化：null 透传；超 1000 字符截断；恒用 InvariantCulture。</summary>
internal static string? Stringify(object? v)
{
    if (v == null) return null;
    var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    return s.Length > 1000 ? s[..1000] : s;
}

/// <summary>构造单实体的标量字段 before/after diff（跳过主键/TenantId/元字段/[AuditIgnore]/拒名单）。</summary>
private List<FieldChange> BuildChanges(EntityEntry e)
{
    var pkNames = e.Metadata.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet() ?? new();
    var list = new List<FieldChange>();
    foreach (var p in e.Properties)
    {
        var name = p.Metadata.Name;
        if (pkNames.Contains(name)) continue;                            // 主键
        if (name == "TenantId" || _metaSkip.Contains(name)) continue;    // 租户 + 元字段
        if (p.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() != null) continue;  // [AuditIgnore]
        if (IsSecretField(name)) continue;                               // 拒名单兜底
        switch (e.State)
        {
            case EntityState.Added: list.Add(new(name, null, Stringify(p.CurrentValue))); break;
            case EntityState.Deleted: list.Add(new(name, Stringify(p.OriginalValue), null)); break;
            case EntityState.Modified:
                if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))   // ← 只记真变的
                    list.Add(new(name, Stringify(p.OriginalValue), Stringify(p.CurrentValue)));
                break;
        }
    }
    return list;
}
```

**逐行解析：**
- **`_metaSkip`** — 审计 who/when 已经有专列（`Modifier`/`ModifyDate`）记录，diff 里再记就是噪声，跳过。
- **三重密钥防护**：(1) `[AuditIgnore]` 特性显式标记；(2) `IsSecretField` 名称拒名单**兜底**（即使漏标特性，名字含 `password`/`secret`/`hash` 等也拦下）；(3) 跳过主键/TenantId/元字段。**密钥绝不入审计日志**是硬底线，用两层（特性 + 名称）冗余防护。
- **`p.OriginalValue` vs `p.CurrentValue`** — 这正是快照追踪（6.5.1）的果实：`OriginalValue` 是查出来时的快照值，`CurrentValue` 是当前值。审计直接读 ChangeTracker 算好的 before/after。
- **`if (p.IsModified && !Equals(OriginalValue, CurrentValue))`** — 只记**真正变化**的字段。这就是为什么 6.5.3 强调"先查后改"——若用全列 UPDATE（`State=Modified`），所有列 `IsModified=true`，审计会记一堆没真变的字段。
- **`Stringify` 用 `InvariantCulture`** — 值转字符串时用不变区域性，保证小数点、日期格式与服务器区域无关（审计日志要跨环境一致可读）。
- 注意 **RowVersion 天然不进审计**——它是 `[Timestamp]` 并发令牌，不是业务字段，且每次更新必变、记它纯噪声。CP6 的设计里 RowVersion 字段被排除（它不实现 IAuditable 的实体不审计，且工作流运行时实体如 `Wf_FlowInstance` 刻意不贴 IAuditable，有专门的负测试坐实"零审计行"）。

### 6.12.4 两阶段原子落库：SaveChanges 重写

审计的难点：**新增（Added）实体的主键在保存前还没生成**（Guid 由数据库生成/或 SaveChanges 时才定），审计行要记录"这行的主键"，就得等业务行落库后。CP6 用**两阶段 + 同事务**解决：

```csharp
/// <summary>阶段一：保存前遍历 IAuditable 变更，捕获 diff + 存前键 + 租户。</summary>
private List<PendingAudit> CaptureFieldAuditBeforeSave()
{
    var list = new List<PendingAudit>();
    foreach (var e in ChangeTracker.Entries<IAuditable>())   // 访问 Entries 触发 DetectChanges
    {
        if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
        var changes = BuildChanges(e);
        if (e.State == EntityState.Modified && changes.Count == 0) continue;   // 空改不记（零噪声）
        var tenant = e.Entity is BaseTenantEntity bt ? bt.TenantId : CurrentTenantId;
        list.Add(new PendingAudit(e, e.Metadata.ClrType.Name, MapOp(e.State), changes, ExtractKey(e), tenant));
    }
    return list;
}

public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    StampTenant();                                    // ① 先盖租户（6.4.6）
    var pending = CaptureFieldAuditBeforeSave();      // ② 捕获 diff（业务行还没存，Added 键未定）
    if (pending.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);   // 无审计 → 零开销原路径

    var useTx = Database.IsRelational() && Database.CurrentTransaction == null;   // InMemory 不开；已有环境事务则参与
    var tx = useTx ? Database.BeginTransaction() : null;
    try
    {
        var result = base.SaveChanges(acceptAllChangesOnSuccess);   // ③ 业务变更落库（Added 键此刻落定）
        WriteAuditRows(pending);                                    // ④ 用落定后的真键写审计行
        base.SaveChanges(acceptAllChangesOnSuccess: true);          // ⑤ 审计行落库（调 BASE 不重入）
        tx?.Commit();                                               // ⑥ 同事务提交：业务+审计原子
        return result;                                              // 返业务影响行数（审计行不计入）
    }
    catch { tx?.Rollback(); throw; }
    finally { tx?.Dispose(); }
}
```

（`SaveChangesAsync` 是完全对称的 async 版本。）

**逐行解析（这是 SaveChanges 重写的精华）：**
- **`StampTenant()` 在最前** — 先盖租户，这样审计捕获时业务实体的 TenantId 已确定。
- **`if (pending.Count == 0) return base.SaveChanges(...)`** — **零开销快路径**：没有 IAuditable 变更时，走原生 SaveChanges，不开事务、不额外处理。绝大多数纯系统写入（无审计目标）不受任何性能影响。
- **`useTx = Database.IsRelational() && CurrentTransaction == null`** — 只在关系型库（非 InMemory）且**当前没有外层事务**时才自己开事务。如果外层已经开了事务（比如 `StockMovementService` 的显式事务），就**参与**它、不另开——审计行自动进外层事务。这个"环境事务感知"很关键，避免嵌套事务冲突。
- **阶段一（②）捕获时用存前键**，Modified/Deleted 用 `OriginalValue` 的键（Deleted 保存后会 Detached）；
- **阶段二（④）** `WriteAuditRows` 里对 Added 用 `ExtractKey(pa.Entry)` **重新提取**——此刻业务行已落库（③之后），Guid 主键已落定，取到真值。
- **`base.SaveChanges`（⑤）而非 `this.SaveChanges`** — 写审计行时调 `base`，**不重入**本重写（否则无限递归/重复盖章）；且审计行本身不是 `IAuditable`，不会被再次捕获。
- **`tx.Commit()`（⑥）** — 业务行（③）和审计行（⑤）在**同一个事务**里，一起提交。业务成功审计必成功，业务回滚审计一起回滚。**原子性**是审计可信的前提。

```csharp
private void WriteAuditRows(List<PendingAudit> pending)
{
    foreach (var pa in pending)
    {
        var key = pa.Operation == 1 ? ExtractKey(pa.Entry) : pa.KeyBeforeSave;   // Added 取存后真值
        Sys_FieldAuditLogs.Add(new Sys_FieldAuditLog
        {
            EntityName = pa.EntityName,
            EntityKey = key,
            Operation = pa.Operation,
            Changes = System.Text.Json.JsonSerializer.Serialize(pa.Changes),   // diff 序列化成 JSON 存
            UserId = _user?.UserId,                                            // 谁改的（注入的当前用户）
            UserName = _user?.UserName,
            ChangedAt = DateTime.Now,
            TenantId = pa.TenantId
        });
    }
}
```

- **`_user`** 是构造函数注入的 `ICurrentUserAccessor`（6.2 的构造函数第三参数），从当前请求的 claims 读"谁在操作"。
- diff 存成 JSON（`Changes` 列是 `nvarchar(max)`，6.3.4 配置的）。审计一行 = 一次操作，含实体名、主键、操作类型、字段变更 JSON、操作人、时间、租户。

### 6.12.5 完整安全故事串联

到这里，CP6 的写入管道在 `SaveChanges` 里串起了三条横切逻辑，**顺序和原子性都经过设计**：

```
SaveChanges 被调用
  → StampTenant()          盖租户（写入侧租户设防，6.4.6）
  → CaptureFieldAuditBeforeSave()   捕获 IAuditable diff（存前）
  → [开/参与事务]
      → base.SaveChanges   业务行落库（Added 键落定）
      → WriteAuditRows     写审计行（用落定后真键）
      → base.SaveChanges   审计行落库
  → Commit                 业务 + 审计 同事务原子提交
```

而查询侧有全局过滤器（6.4.3）自动隔租户。**写入盖章 + 查询过滤 + 字段审计**，全部挂在 DbContext 这一个中枢上，声明式（继承 `BaseTenantEntity` / 实现 `IAuditable`）、防漏、原子。这就是 6.10.3 审计盲区案例的背景——一旦 `ExecuteUpdate` 绕过这个中枢，这三条逻辑全部失效，所以它才成为需要显式记账的技术债。

### 6.12.6 坑与真实事故

- **坑：全列 UPDATE 污染审计。** `State=Modified` 全列标脏 → 审计记一堆没真变的字段。CP6 用"先查后改"（6.5.3）+ `IsModified && !Equals` 双保险。
- **坑：审计行被 Dispose/漏事务。** 若审计写在业务事务外，业务提交了审计失败 = 留痕丢失。CP6 用同事务 + 环境事务感知保证原子。
- **坑：密钥进日志。** 漏标 `[AuditIgnore]` 就泄密。CP6 加 `IsSecretField` 名称拒名单兜底，两层防护。
- **坑：ExecuteUpdate 审计盲区**（6.10.3）——审计管道最大的边界。

### 6.12.7 面试问答

**Q：怎么用 EF Core 实现字段级审计？**
A：重写 DbContext 的 SaveChanges/SaveChangesAsync。用空标记接口 IAuditable 让实体 opt-in，`ChangeTracker.Entries<IAuditable>()` 捞出变更实体，读每个属性的 OriginalValue/CurrentValue 算 before/after diff（只记真变的、跳过主键/租户/元字段/敏感字段），序列化成 JSON 连同操作人/时间/主键写审计行。因为 Added 实体主键保存前未定，用两阶段：先捕获 diff，base.SaveChanges 让业务行落库主键落定，再写审计行，最后同一事务提交保证业务和审计原子。CP6 就是这么做的。

**Q：审计里怎么防止密码这类敏感字段被记录？**
A：三重防护。一是 `[AuditIgnore]` 特性显式标在敏感属性上；二是名称拒名单兜底（字段名含 password/secret/hash/salt 等即使漏标也拦下）；三是跳过主键、TenantId、审计元字段。密钥不入日志是硬底线，用特性 + 名称双层冗余。

**Q：为什么审计要在事务里，且写审计行要调 base.SaveChanges？**
A：事务保证业务行和审计行原子——业务成功审计必成功，业务回滚审计一起回滚，否则留痕会丢或对不上。调 base.SaveChanges 而不是 this 是为了不重入被重写的 SaveChanges（否则无限递归、重复盖章），且审计行本身不是 IAuditable 不会被再次捕获。CP6 还做了环境事务感知：外层已有事务就参与而不另开。

---

## 本章面试题 20 问（详细答案）

**1. 什么是 ORM？什么是对象关系阻抗失配？**
ORM 是自动在对象世界和关系世界间搬运数据的中间层，你写 LINQ 它翻译成 SQL、数据库返回行它组装成对象。阻抗失配指两个世界的模型不一致：继承 vs 无继承、对象引用 vs 外键、集合 vs 结果集、对象身份 vs 主键、null vs NULL 三值逻辑、封装 vs 全公开列——映射时都要处理这些差异。

**2. EF Core、Dapper、裸 ADO.NET 怎么选？**
业务写入主线用 EF Core（有变更追踪、迁移、全局过滤器、审计等安全网）；复杂只读报表/性能热点用 Dapper + 存储过程（无追踪开销、DBA 可调优执行计划）；极端场景才裸 ADO。不是二选一，CP6 就是 ERP/WMS 全 EF、MES 仪表盘聚合用 Dapper 走存储过程并用。

**3. DbContext 为什么注册成 Scoped？**
一次 HTTP 请求一个实例。不能 Singleton（非线程安全、并发崩、ChangeTracker 内存泄漏），不能 Transient（同请求各 Service 拿到不同上下文、无法共享变更追踪和事务边界原子提交）。Scoped 恰好：请求内共享、请求间隔离，落地工作单元模式。

**4. "A second operation was started on this context" 是什么原因？**
同一 DbContext 被并发使用——通常异步漏 await 导致两个操作在同一连接上并发，或 Scoped 上下文被捕获进并行任务。DbContext 非线程安全一次只能一个进行中操作。解决：顺序 await，真要并行用 IDbContextFactory 造独立上下文。

**5. 约定、数据注解、Fluent API 三者关系？**
优先级从低到高、后者覆盖前者。约定是零配置默认规则；注解贴属性上就近直观但表达力有限；Fluent API 在 OnModelCreating 里能做复合主键、复合/过滤唯一索引、关系、查询过滤器等注解做不了的。全局查询过滤器只能 Fluent。CP6 实体只带基本注解，跨实体配置全集中 OnModelCreating。

**6. 全局查询过滤器是什么？CP6 怎么用它做多租户？**
HasQueryFilter 给实体注册过滤 lambda，之后所有查询自动带上、想漏都漏不了。CP6 让隔离实体继承 BaseTenantEntity，在 OnModelCreating 用反射遍历模型给每个注册 `e => e.TenantId == CurrentTenantId`，闭包 DbContext 的 CurrentTenantId 属性实时读当前租户，生成 SQL 自动加 WHERE TenantId=@x。

**7. 为什么要反射 + 表达式树注册过滤器？**
防漏命门——200 张表逐个手写迟早漏、漏一个就跨租户泄露；且运行时循环里实体是 Type、编译期写不出泛型 HasQueryFilter，只能用 Expression API 按类型动态造 lambda。

**8. IgnoreQueryFilters 的用途和风险？**
临时关全局过滤器做跨租户查。CP6 用在刷新令牌——到达时无租户上下文，需按全局唯一 TokenHash 跨租户找到令牌行才知道属于哪个租户。风险是能看到全库所有租户数据，用错就泄露；纪律是只在白名单场景、用了立刻回设租户上下文或用全局唯一键精确命中。

**9. 讲一个多租户安全漏洞。**
CP6 角色新增接口曾信任请求体的 TenantId。写入盖章是"TenantId 为空才盖"，攻击者 body 塞别的租户非空 GUID 就绕过盖章写到别租户。修复在控制器边界无条件 `entity.TenantId = CurrentTenantId` 覆写。教训：查询写入两侧都设防，请求体字段都是不可信输入不能用于归属判定。

**10. 快照追踪原理？EntityState 五态？**
追踪查询取出实体时拍原始值快照进 ChangeTracker，SaveChanges 时 DetectChanges 逐属性比对当前值和快照算出变更，生成最小 UPDATE。五态：Added（INSERT）、Modified（UPDATE 变化列）、Deleted（DELETE）、Unchanged（不动）、Detached（不追踪）。

**11. AsNoTracking 什么时候用？**
只读查询——列表、报表、导出、不回写的数据。省拍快照、进 ChangeTracker、身份解析的开销，减少内存和 GC。查出来要改并 SaveChanges 的不能用。CP6 的 WMS 只读查询大量使用。

**12. Attach 和 Update 区别？CP6 为什么改用先查后改？**
Attach 附加为 Unchanged 只更新后续改的列；Update 附加为 Modified 全列更新。全列 UPDATE 会覆盖未改列真值、审计 diff 不精确。CP6 引入字段审计后改用先查后改：查出追踪态实体只改变化列，DetectChanges 得到精确 diff。

**13. IQueryable 和 IEnumerable 区别？**
IQueryable 的方法接收表达式树，EF 分析翻译成 SQL 数据库端执行；IEnumerable 接收委托内存里逐个跑。对 DbSet 写 Where 是数据库过滤，若先 ToList 变 IEnumerable 再 Where 就是拉全表进内存再过滤。

**14. EF Core 怎么防 SQL 注入？**
LINQ 生成的 SQL 总是参数化，用户输入作为 @参数 传给数据库永不当代码执行。原生 SQL 用 FromSqlInterpolated（插值转参数）或 FromSqlRaw 占位，绝不字符串拼接用户输入。

**15. N+1 是什么？三种解法？**
查 N 条主记录后每条各查一次关联，共 1+N 条 SQL。解法：Include 一次 JOIN；两查 + ToLookup 内存组装（2 条 SQL）；Include 多集合用 AsSplitQuery 避免笛卡尔膨胀。根因常是延迟加载 + 循环或循环里查库。

**16. 为什么不用延迟加载？**
它把访问属性变成偷偷发 SQL，是最隐蔽的 N+1 来源代码里看不出来；还有 DbContext 已 Dispose 访问导航抛异常、序列化触发连环查询、同步阻塞异步管道。CP6 全用显式 Include + 投影，要什么明说、性能可预测。

**17. 迁移是怎么生成的？生产怎么应用？**
migrations add 时 EF 对比当前模型和上一迁移快照算差异，生成 Up/Down 并更新快照。生产用 `migrations script --idempotent` 生成幂等脚本受控应用（可审阅、可回滚、多实例安全），不在启动时自动迁移（竞态、失败即起不来、权限过大）。

**18. 讲一个棘手的迁移。**
CP6 把 Sys_RoleMenu 非唯一索引升级成唯一索引。生产库有历史重复行，直接建唯一索引会报错。Up 里先 `DELETE WHERE Id > (SELECT MIN(Id) GROUP BY 三列)` 每组保留最小 Id 删重复副本，清成满足约束再建唯一索引。教训：加约束前先让存量数据满足约束。

**19. 乐观并发 vs 悲观锁，库存扣减怎么选？**
乐观（RowVersion）不加锁、更新时 WHERE 带读取时的令牌、被改过则 0 行抛 DbUpdateConcurrencyException，适合冲突罕见 + 重试，CP6 库存移动就是；悲观（UPDLOCK）读时锁行串行化，适合极热点 SKU 避免重试风暴；最优是一条 SQL 条件更新 `SET Qty=Qty-n WHERE Qty>=n` 靠行锁原子扣减消除先读后写窗口。

**20. ExecuteUpdate 的副作用是什么？CP6 发现了什么？**
它绕过整个 SaveChanges 管道，挂在管道上的字段审计、软删过滤、租户盖章、领域事件全不触发。CP6 发现用 ExecuteUpdate 批量改订单明细单价（财务敏感字段）造成审计盲区——谁何时改了单价追溯不到，登记为技术债。处理是按字段敏感度分流：敏感字段走 SaveChanges 留痕，大批量非敏感操作才用 ExecuteUpdate，并在文档显式记录盲区。

---

## 自测清单

读完本章，合上文档，检查你能否不看答案讲清楚：

- [ ] 说出对象关系阻抗失配的至少 3 个具体失配点。
- [ ] 说清 EF Core / Dapper / ADO.NET 各自适用场景，以及 CP6 为什么两者并用。
- [ ] 解释 DbContext 为什么是 Scoped，Singleton/Transient 各会出什么问题。
- [ ] 复述"A second operation was started..."的成因和解决。
- [ ] 说清约定/注解/Fluent API 的优先级和各自适用。
- [ ] 手画（口述）反射注册全局查询过滤器的 8 行逻辑，说清为什么要表达式树、为什么闭包 CurrentTenantId 属性。
- [ ] 讲清 IgnoreQueryFilters 的合法用途（refresh token）和风险。
- [ ] 复述角色接口跨租户写注入漏洞的成因和修复，并总结教训。
- [ ] 解释快照追踪 + DetectChanges 如何生成最小 UPDATE。
- [ ] 说清 AsNoTracking 省了什么、何时必用、何时不能用。
- [ ] 区分 Attach/Update/State=Modified，说清 CP6 为什么先查后改。
- [ ] 解释 IQueryable vs IEnumerable、延迟执行、参数化防注入。
- [ ] 说出 N+1 的三种解法和各自代价，解释 AsSplitQuery 解决什么。
- [ ] 说清为什么不用延迟加载。
- [ ] 解释迁移生成原理、Up/Down、幂等脚本 vs 启动自动迁移的取舍。
- [ ] 复述唯一索引迁移 Down 先删重复副本的案例。
- [ ] 区分乐观/悲观并发，讨论库存扣减防超卖方案。
- [ ] 说清 RowVersion 机制和 DbUpdateConcurrencyException 处理。
- [ ] 讲透 ExecuteUpdate 审计盲区案例的发现、成因、工程权衡。
- [ ] 完整描述 CP6 SaveChanges 重写里 盖章→捕获→两阶段落库→同事务提交 的流程。
- [ ] 说清字段审计敏感字段三重防护。

---

## 动手练习

### 练习 1（读懂一个真实迁移文件）★必做

打开 `C:\CP6\CP6.Core\Migrations\20260710172302_SysRoleMenuUniqueIndex.cs`，不看本章解析，回答：

1. 这个迁移的目标是什么（从非唯一到唯一？涉及哪几列）？
2. `Up()` 里那段 `DELETE ... WHERE Id > (SELECT MIN(Id) ...)` 在做什么？**如果去掉它会发生什么？**
3. `Down()` 为什么是"删唯一索引 + 建回非唯一 (TenantId, RoleId)"？回滚后数据会不会有问题？
4. 再打开配套的 `.Designer.cs`，找到 `Sys_RoleMenu` 那段，看模型快照是怎么记录这个索引的。

进阶：再挑 `20260605142325_Phase7AddStockQcStatus.cs` 对比，说出"加列 + 索引"型迁移和"数据清洗 + 改约束"型迁移在结构上的差异。

### 练习 2（追踪一个租户查询的完整 SQL）

1. 在 `CP6Context.OnModelCreating` 里找到反射注册全局过滤器的那段（约 2129 行），确认它对 `BaseTenantEntity` 子类生效。
2. 假想一个查询 `_db.Orders.Where(o => o.WebOrderNo == "W001").ToListAsync()`，**手写出你预期 EF 生成的完整 SQL**（含全局过滤器注入的 `WHERE TenantId=@x` 和参数化的 WebOrderNo）。
3. 如果你能跑起项目，在这句前加 `.ToQueryString()` 打印实际 SQL，和你手写的对比。
4. 再把查询改成 `.IgnoreQueryFilters()`，对比 SQL 少了哪一段——理解逃生舱的作用。

### 练习 3（设计一个带审计的实体 + 走通写入管道）

1. 阅读 `IAuditable.cs`、`AuditIgnoreAttribute.cs`，和 `CP6Context` 的 `BuildChanges`/`CaptureFieldAuditBeforeSave`/`WriteAuditRows`。
2. 设想给一个新实体 `Coupon : BaseTenantEntity, IAuditable`，含字段 `Code`、`Amount`、`SecretPin`。回答：
   - 新增一张 Coupon，审计行的 `Changes` JSON 会包含哪些字段？（提示：主键/TenantId/元字段/敏感字段都不进）
   - `SecretPin` 会被审计吗？它靠哪重防护被排除？如果字段名叫 `SecretPin` 但你没标 `[AuditIgnore]`，还会被排除吗？（提示：看 `IsSecretField` 的 `EndsWith("secret")` 匹配大小写——`ToLowerInvariant` 后 `secretpin` 不以 secret 结尾，得靠 `[AuditIgnore]`！这是个值得警惕的边界）
   - 如果用 `_db.Coupons.Where(...).ExecuteUpdateAsync(s => s.SetProperty(x => x.Amount, 0))` 批量清零金额，审计表会有记录吗？为什么？该怎么补救？
3. 画出这次"新增 Coupon"在 SaveChanges 重写里经过的完整调用顺序（StampTenant → Capture → base.SaveChanges → WriteAuditRows → base.SaveChanges → Commit）。

> 练习 3 的第 2.2 问是本章埋的一个真实边界：`IsSecretField` 用 `EndsWith("secret")`，字段名 `SecretPin` 小写后是 `secretpin`，**不以 secret 结尾**，拒名单兜底不住，必须靠 `[AuditIgnore]`。这提醒你：名称拒名单是兜底不是万能，敏感字段该显式标注。

---

*本章完。下一章：第 7 章（按你的教程大纲）。建议把 6.4 多租户专题、6.10.3 ExecuteUpdate 审计盲区、6.12 审计管道这三段作为面试的"深度炸弹"，准备好能白板画出来。*
