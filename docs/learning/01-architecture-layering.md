# 01 · 分层架构与依赖方向

## 📍 学习目标

读完这一章，你能答出：

1. CP6 为什么拆成 `Entity / Core / WebApi / Tests` 四个项目？
2. 依赖方向是怎么走的？反过来会怎么样？
3. 这种分层是 DDD 吗？是洋葱架构吗？还是只是"传统三层"？
4. 解决方案文件为什么是 `.slnx` 而不是 `.sln`？
5. 前端 `cp6.web` 为什么不进解决方案？

---

## 🔎 真实代码切片

### CP6.slnx — 解决方案就 4 个项目

```xml
<!-- D:\CP6\CP6.slnx -->
<Solution>
  <Project Path="CP6.Entity/CP6.Entity.csproj" />
  <Project Path="CP6.Core/CP6.Core.csproj" />
  <Project Path="CP6.WebApi/CP6.WebApi.csproj" />
  <Project Path="CP6.Tests/CP6.Tests.csproj" />
</Solution>
```

> `.slnx` 是 .NET 8/9/10 引入的新一代 solution 格式，XML 写法、可读性远好于老的 `.sln`（那种像汇编一样的 GUID 堆）。Visual Studio / Rider / dotnet CLI 都已经支持。

### 依赖方向（严格单向，不允许回环）

```
CP6.WebApi  →  CP6.Core  →  CP6.Entity
                              ↑
CP6.Tests   ──────────────────┘
            └→  CP6.WebApi（集成测试）
            └→  CP6.Core（单元测试）

cp6.web ── HTTP / WebSocket ──→ CP6.WebApi
```

### 各层职责（取自 `docs/PROJECT_STRUCTURE.md` §2.2 并精炼）

| 项目 | 角色 | 一句话定义 | 它**不**做什么 |
|---|---|---|---|
| **CP6.Entity** | 数据形状 | 只有 POCO、DTO、枚举、`[Key]` 之类的数据注解 | 不连数据库、不写业务、不引 `Microsoft.EntityFrameworkCore` |
| **CP6.Core** | 业务逻辑 | `CP6Context` + 所有 `*Service` + `BridgeHook` | 不知道 HTTP、不知道 Controller、不返回 `IActionResult` |
| **CP6.WebApi** | HTTP 边界 | Controller + 中间件 + DI 装配 + SignalR Hub | 不写业务逻辑（薄壳，转发给 Service） |
| **CP6.Tests** | 验证 | xUnit + Moq + InMemory DB | 不进生产 |

---

## 💡 资深视角

### 这是"洋葱架构"还是"传统三层"？

严格说，CP6 是**带依赖反转的传统三层 + 部分洋葱思想**：

- ❌ 不是完整洋葱：Domain 层（业务规则）和 Application 层（编排）没拆开，都揉在 `CP6.Core/Services/`。
- ❌ 不是完整 DDD：没有显式的 Aggregate Root、Value Object、Domain Event。但 `IStockMovementService` 守住 `T_Stock` 的不变式，相当于 Aggregate 的写门面（见第 5 章）。
- ✅ 但有依赖反转：Bridge Hook 是接口而不是直接 using —— ERP 不知道 MES 是谁，只知道 `IMesBridgeHook`（见第 6 章）。

**面试可以这样回答**：

> CP6 采用分层架构 + 依赖反转。Entity 层只放数据形状不依赖任何业务库；Core 层承担业务逻辑且不知道 HTTP；WebApi 层是薄壳，把 HTTP 请求转发给 Core 的 Service。跨模块联动通过 `I*BridgeHook` 接口反转依赖，避免 ERP 直接 using MES 命名空间。这不是教科书式 DDD，但保留了 DDD 最有价值的部分——领域不变式的守护（如 `IStockMovementService` 是 `T_Stock` 的唯一写入入口）。

### 为什么 Entity 不能引用 EF Core？

如果 `CP6.Entity` 依赖 `Microsoft.EntityFrameworkCore`：

1. **污染**：任何引用 Entity 的工程（比如 CLI 工具、批处理脚本）都被迫拖一个 EF Core。
2. **耦合**：换 ORM（比如哪天想试 Linq2DB）就要改所有 Entity 的 attribute。
3. **测试变慢**：哪怕只测一个 DTO 序列化，也要把 EF 启动起来。

CP6 的做法是 Entity 只用 `System.ComponentModel.DataAnnotations`（`[Key]` `[MaxLength]` `[Timestamp]`），这些来自 BCL 而不是 EF Core。EF Core 通过约定（或在 `CP6Context.OnModelCreating` 里 Fluent API）来理解这些 attribute。

### 前端 `cp6.web` 为什么不进 `.slnx`？

因为它是独立 Node 工程，跟 .NET 编译链没关系。两边唯一耦合点是：

1. HTTP 契约（API 形状）— 现在是通过 OpenAPI 文档 + 前端手写 axios 客户端
2. SignalR Hub URL — 前端硬编码 `/hubs/notify`

**可改进点**：用 NSwag 或 OpenAPI Generator 把后端 OpenAPI 转成前端 TypeScript 客户端，能省掉手写 + 类型对齐。CP6 当前没做，所以 `cp6.web/src/api/*.ts` 是手维护的。

### .slnx vs .sln

```bash
# 转换命令
dotnet sln migrate
```

- `.sln`：1970 年风格的扁平文本，GUID 满天飞，merge conflict 灾难。
- `.slnx`：2024 年起标配 XML，可读、可 diff、可自动生成。

**面试加分项**：如果面试官项目还在用 `.sln`，你能提一句"现在新项目可以考虑迁 `.slnx`"，立刻显得你跟得上 .NET 生态。

---

## ⚠️ 踩坑记录

### 坑 1：Tests 工程引用 WebApi 导致循环风险

CP6.Tests 同时引用 `CP6.WebApi` 和 `CP6.Core`。看似合理（要测 Controller 集成 + 测 Service 单元），但有副作用：

- Tests 编译时会拖整个 WebApi 启动栈（DI、appsettings）
- 一些 Mock 时要绕开 `Program.cs` 的 HostedService（否则 InMemory DB 里跑 retry worker）

CP6 用 `TestHelper.cs` 里的 `CreateInMemoryContext()` 工厂规避，但更干净的做法是把 Service 测和 Controller 测拆成两个 csproj。

### 坑 2：`Sys_Menu` 不继承 `BaseEntity`

`BaseEntity` 主键是 `Guid`，但 `Sys_Menu` 用 `int MenuId`，因为：

- 菜单树 ParentId 自引用，int 更省
- 前端递归渲染 + 序号排序，int 比 Guid 直观

> 这暴露了"统一基类"的边界 —— 不是所有表都能塞进同一个抽象。CP6 的处理是：让 Menu 直接继承 `BaseEntity` 但 hide 掉 `Id` 字段。**面试官常问**："如果有的表不需要乐观锁怎么办？" 你可以说："拆抽象层，业务表继承 `BaseBizEntity`（有 `RowVersion`），系统表继承 `BaseEntity`（无 `RowVersion`）"。这正是 CP6 的做法。

### 坑 3：CP6.Core 引用了 SqlClient

```bash
# CP6.Core.csproj 里有这个
<PackageReference Include="Microsoft.Data.SqlClient" />
```

严格洋葱架构会认为 Core 不该知道"SQL Server"。CP6 把 Dapper 的 `IDbConnection` 在 Core 用，所以引了。代价是：换 PostgreSQL 时要改 Core。

**取舍**：换库的概率很低，换来 Dapper 直查（性能更好）值得。这是个理性妥协的例子。

---

## 🧪 自检题

1. **依赖方向**：如果我在 `CP6.Entity` 里加一行 `using CP6.Core;`，会发生什么？  
   <details><summary>答案</summary>编译失败。`CP6.Entity.csproj` 不引用 `CP6.Core.csproj`。如果你强行加引用，IDE 会报循环依赖，因为 Core 已经依赖 Entity。</details>

2. **重命名**：把 `RepositoryBase<T> where T : BaseEntity` 改成 `where T : BaseBizEntity`，会有什么影响？  
   <details><summary>答案</summary>所有继承 `BaseEntity` 但不继承 `BaseBizEntity` 的 entity（比如 `Sys_Menu`、`Sys_OperLog`）会无法通过 `IRepository<T>` 操作。要拆成 `RepositoryBase<T>` + `BizRepositoryBase<T> : RepositoryBase<T> where T : BaseBizEntity`。</details>

3. **设计**：如果 PM 说"将来可能要拆出独立的报表服务，单独部署"，你会怎么调整分层？  
   <details><summary>答案</summary>把 `CP6.Core/Services/` 里跟报表相关的 Service（OtdReportService、UnshippedOrderService）抽到新工程 `CP6.Reporting`，让它依赖 `CP6.Entity` + `CP6.Core.Abstractions`（一个新加的接口工程）。WebApi 同时引用 `CP6.Core` 和 `CP6.Reporting`。这样未来可以把 Reporting 拆 microservice。</details>

4. **质疑**：有人说"Generic Repository 是反模式，应该用 Specification 模式"，你怎么回应？  
   <details><summary>答案</summary>分场景：CP6 的 `RepositoryBase` 只暴露 `FindAsync/GetPageList/Add/Update/Delete` 五个方法，没有泄漏 `IQueryable<T>`，已经避免了"Generic Repository 反模式"最大的争议（在 Service 层乱写 query）。真正复杂的查询（如 OtdReport 的多表 join）CP6 是直接在 Service 里写 EF Core 或 Dapper 而不经仓储。这是个理性的混合方案。Specification 模式适合查询条件高度组合化的场景（如电商商品筛选），CP6 这种业务流型系统没这个需要。</details>

---

## 🔗 延伸阅读

- [.NET Solution File Format (.slnx)](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file) — 官方迁移指南
- [Onion Architecture (Jeffrey Palermo, 2008)](https://jeffreypalermo.com/2008/07/the-onion-architecture-part-1/) — 洋葱架构原文
- [Clean Architecture (Robert C. Martin)](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html) — 依赖反转的总纲
- 项目内：[`docs/PROJECT_STRUCTURE.md`](../PROJECT_STRUCTURE.md) §1, §2
