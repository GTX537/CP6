# 华测检测「系统工程师」面试备战 · 完整对话记录

> **面试时间**：2026-05-28 上午（线上面试）
> **岗位**：系统工程师（.NET + AI 全栈）· 12-18K · 13薪
> **公司**：华测检测（CTI）· 深圳宝安
> **整理日期**：2026-05-27

---

# 系统工程师岗位面试技能分析

根据 JD，我帮你梳理出**核心技能矩阵**和**面试准备重点**。

## 一、技能优先级分层

### 🔴 P0 - 必须熟练（面试必考，答不出直接挂）

**1. C# / .NET 核心**
- C# 语法：泛型、委托、Lambda、LINQ、async/await、反射
- .NET Core / .NET 6+ 特性：依赖注入（DI）、中间件、配置系统、托管服务
- 面向对象三大特性 + SOLID 原则
- 垃圾回收（GC）机制、值类型/引用类型

**2. ASP.NET Core Web API**
- 路由机制、控制器、过滤器（Filter）、中间件管道
- JWT 鉴权、Swagger 配置
- 模型绑定、模型验证
- 跨域（CORS）、异常全局处理

**3. ORM（EF Core / SqlSugar）**
- EF Core：DbContext、Code First / DB First、迁移、导航属性、延迟加载 vs 立即加载
- SqlSugar：常用 CRUD、Lambda 查询、事务、读写分离（国内中小公司常用，重点准备）
- 性能：N+1 问题、AsNoTracking、批量操作

**4. SQL（MySQL / SQL Server）**
- 索引原理（B+ 树、聚簇/非聚簇索引、最左前缀）
- 事务隔离级别、锁机制
- 联表查询、子查询、窗口函数
- 慢 SQL 优化、执行计划分析（EXPLAIN）

### 🟡 P1 - 应该掌握（加分项，体现深度）

**5. 前端（Vue + TS）**
- Vue 2 vs Vue 3 区别（Composition API、响应式原理 Proxy vs defineProperty）
- 组件通信：props/emit、provide/inject、Pinia/Vuex
- 生命周期、路由（Vue Router）
- TypeScript 基础类型、接口、泛型
- jQuery 基本用法（老系统维护可能用到）

**6. AI 集成（重点亮点，公司明显在转型）**
- 大模型 API 调用：OpenAI / DeepSeek / 文心 / 通义千问 SDK 用法
- Prompt Engineering：Few-shot、CoT、角色设定
- RAG 原理：向量数据库（如 Milvus/Qdrant）、Embedding、检索增强流程
- Agent / Workflow：Function Calling、工具调用、多步推理
- Dify / Coze 平台：能讲清楚怎么搭一个智能体

**7. AI 辅助开发工具实战经验**
- Cursor / Claude Code / 通义灵码的实际使用场景
- **准备一个故事**：你如何用 AI 工具把某个任务效率提升了 X 倍

### 🟢 P2 - 了解即可（兜底问题）

- HTML5 / CSS3、响应式布局（媒体查询、Flex、Grid）
- 移动端适配（rem、vw/vh、viewport）
- Git 基本操作
- LIMS / 环境检测领域知识

## 二、面试高频问题预测

| 类别 | 可能问题 |
|---|---|
| **C#/.NET** | async/await 原理？IEnumerable vs IQueryable？依赖注入三种生命周期？ |
| **数据库** | 一个慢 SQL 你怎么排查？联合索引失效的场景？ |
| **架构** | 高并发场景下接口怎么优化？缓存怎么用？ |
| **前端** | Vue 响应式原理？v-if vs v-show？|
| **AI** | RAG 的完整流程讲一下？你做过哪个 Agent？Token 成本怎么控制？ |
| **行为题** | 用 AI 工具改造开发流程的实际案例？|
| **业务** | 了解 LIMS 系统吗？|

## 三、明天面试前的冲刺建议

1. **复盘自己最熟的 .NET 项目**：架构图、技术选型理由、踩过的坑、性能优化点 —— 这是 80% 的面试时间
2. **准备一个 AI 落地案例**：哪怕是个人项目也行，体现你"会用 AI + 懂业务"
3. **背一遍 SQL 优化套路**：索引、执行计划、分页优化
4. **Vue 2/3 差异**快速过一遍
5. **公司背景**：华测检测（CTI）是检验检测龙头
6. **薪资心理价位**：12–18K

## 四、可能的"坑"提示

- JD 写 **"3年以上C#/.NET经验"** + **"环境检测/LIMS 经验优先"** —— 如果没行业经验，要**强调可迁移能力 + 学习速度**
- **"AI 工具深度融合开发流程"** 是这家公司的核心诉求 —— **务必准备 2-3 个 AI 提效的具体例子**
- 12-18K 在深圳宝安属于中等水平，岗位职责很全栈（后端+前端+AI），说明团队不大，要表现出**独立负责能力**

---

# 🔴 P0-1：C# / .NET 核心

按面试出现频率排序的 **6 个子模块**：

| # | 子模块 | 出现频率 | 难度 |
|---|---|---|---|
| 1 | async/await 异步编程 | ⭐⭐⭐⭐⭐ | 中 |
| 2 | 值类型 vs 引用类型 & GC | ⭐⭐⭐⭐⭐ | 中 |
| 3 | 委托、事件、Lambda | ⭐⭐⭐⭐ | 中 |
| 4 | LINQ & IEnumerable vs IQueryable | ⭐⭐⭐⭐⭐ | 中高 |
| 5 | 泛型、反射、特性（Attribute） | ⭐⭐⭐ | 中 |
| 6 | 依赖注入（DI）三种生命周期 | ⭐⭐⭐⭐⭐ | 中 |

## 模块 1：async / await 异步编程

### 【原理】

**核心一句话**：`async/await` 是**语法糖**，编译器会把方法编译成一个**状态机**，遇到 `await` 时把当前线程**释放回线程池**，等异步操作完成后再**恢复执行**。

**关键点 4 条**：

1. **async 本身不创建线程**。它只是告诉编译器："这个方法里面有 await，请生成状态机。"
2. **await 不是等待**，它是"**挂起**当前方法，把线程让出去"。
3. **Task** 代表一个"未来会完成的操作"，类似 JS 的 Promise。
4. **同步上下文（SynchronizationContext）**：在 UI 程序里，await 后默认回到 UI 线程；在 ASP.NET Core 里**没有同步上下文**，所以不需要 `ConfigureAwait(false)`。

### 【面试怎么问】

> Q1：async/await 的原理是什么？
> Q2：async 方法会开新线程吗？
> Q3：await 一个 Task 时，线程在做什么？
> Q4：Task 和 Thread 的区别？
> Q5：为什么不能用 `async void`？
> Q6：什么时候用 `Task.Run`？

### 【标准答案话术】

**Q1 标准答案**：
> "async/await 本质是编译器的语法糖。编译器看到 async 关键字，会把方法体编译成一个**状态机**。每次遇到 await，状态机会检查被等待的 Task 是否完成：如果没完成，就**注册一个回调**然后返回，把当前线程释放回线程池；等 Task 完成后，状态机恢复执行后续代码。所以 **async/await 的核心价值是『不阻塞线程』**，特别适合 I/O 密集型操作。"

**Q2 标准答案**：
> "**不会**。async 只是标记，告诉编译器生成状态机。是否使用新线程，取决于被 await 的 Task 内部实现。比如 `await httpClient.GetAsync()` 是纯 I/O，全程不占用线程；而 `await Task.Run(...)` 才会从线程池借一个线程来执行 CPU 任务。"

**Q5 标准答案（高频陷阱题）**：
> "`async void` 有三个问题：① **异常无法被 catch 捕获**，会直接抛到同步上下文导致进程崩溃；② **无法被 await**，调用者拿不到完成信号；③ **不利于单元测试**。**唯一允许的场景是事件处理器**，比如 WinForm 的 button_Click。其他情况一律用 `async Task`。"

**Q6 标准答案**：
> "`Task.Run` 用于把 **CPU 密集型** 操作丢到线程池后台执行，避免阻塞当前线程（特别是 UI 线程）。**I/O 密集型操作不要用 Task.Run 包装**，因为 I/O 本身就不占线程，包一层反而浪费一个线程。"

### 【加分项】

> "在 ASP.NET Core 里，因为没有 SynchronizationContext，所以**不需要写 `ConfigureAwait(false)`**。但如果是写类库代码，可能被 .NET Framework 调用，那就还是建议加上，避免死锁。"

> "我之前踩过一个坑：在一个同步方法里调用 async 方法时用了 `.Result` 或 `.Wait()`，导致死锁。原因是 await 后想回到原线程，但原线程被 `.Result` 阻塞了。**正确的做法是『一路 async 到底』**。"

## 模块 2：值类型 vs 引用类型 & GC

### 【原理】

**值类型（Value Type）**：
- 存储在**栈（Stack）**上（除非作为类的字段，那就跟着对象在堆上）
- 赋值时**拷贝整个值**
- 包括：`int`, `double`, `bool`, `char`, `struct`, `enum`, `decimal`
- **不能为 null**（除非用 `int?` 即 `Nullable<int>`）

**引用类型（Reference Type）**：
- **对象本身在堆（Heap）**，**引用（指针）在栈**上
- 赋值时**拷贝引用**，两个变量指向同一个对象
- 包括：`class`, `string`, `array`, `interface`, `delegate`
- **可以为 null**

**特殊：`string` 是引用类型，但具有"值类型行为"**（不可变 immutable）。

### 【GC 垃圾回收原理】

1. .NET 把堆分成 **3 代**（Gen 0、Gen 1、Gen 2），新对象进 Gen 0，活过一次 GC 升到 Gen 1，再活过升到 Gen 2。
2. GC 触发条件：Gen 0 满了 → 触发 Gen 0 回收；Gen 1 满了 → Gen 0+1 一起回收；以此类推。**分代设计的核心思想是"大部分对象都是朝生夕死"**。
3. **大对象（≥85KB）直接进 LOH（Large Object Heap）**，LOH 默认不压缩。

### 【面试怎么问】

> Q1：值类型和引用类型的区别？
> Q2：string 是值类型还是引用类型？为什么？
> Q3：GC 是怎么工作的？为什么要分代？
> Q4：什么是装箱（Boxing）和拆箱（Unboxing）？
> Q5：struct 和 class 怎么选？
> Q6：IDisposable 和 GC 是什么关系？

### 【标准答案话术】

**Q1 标准答案**：
> "**存储位置不同**：值类型存在栈上（或作为字段时跟随对象在堆上），引用类型对象在堆、引用在栈。**赋值语义不同**：值类型赋值是拷贝整个数据，引用类型赋值是拷贝引用，两个变量指向同一对象。**默认值不同**：值类型默认值是 0/false，引用类型是 null。**性能特征**：值类型无 GC 压力但拷贝有开销，引用类型有 GC 但传递只拷贝指针。"

**Q2 标准答案（高频）**：
> "string 是**引用类型**，但它是**不可变（immutable）**的，所以表现出值类型的特性。每次修改 string 实际上是**创建了新的 string 对象**。.NET 还做了**字符串驻留（String Interning）**优化：相同字面量的字符串会共享同一个堆对象。"

**Q3 标准答案**：
> "GC 采用**分代回收**：堆分 Gen 0、1、2 三代。新对象进 Gen 0，每次回收后存活的对象升一代。**分代的依据是『弱分代假设』——大部分对象都是短命的**，所以频繁回收 Gen 0、少回收 Gen 2，能极大降低 GC 开销。回收过程分三步：**标记（Mark）→ 压缩（Compact）→ 整理引用**。Gen 2 触发的是 Full GC，最耗时，要尽量避免。"

**Q4 标准答案**：
> "**装箱**是值类型转成引用类型的过程，会在堆上分配内存并复制值；**拆箱**是反过来。比如 `int i = 1; object o = i;` 这就是装箱。装箱有**性能开销**（堆分配 + GC 压力），所以要避免。**典型坑点**：用非泛型集合 `ArrayList` 存值类型会频繁装箱，应该用泛型 `List<int>`。"

**Q5 标准答案**：
> "**用 struct 的场景**：① 数据量小（≤16 字节）；② 不可变（immutable）；③ 频繁创建短生命周期对象，想避免 GC 压力。典型例子：`DateTime`、`Point`、`Guid`。**其他都用 class**。"

**Q6 标准答案**：
> "GC 只能回收**托管资源**（.NET 堆上的对象内存）。但像**文件句柄、数据库连接、socket** 这些**非托管资源**，GC 不知道怎么释放。所以需要实现 `IDisposable` 接口，提供 `Dispose()` 方法手动释放。配合 **`using` 语句**自动调用 Dispose。完整的做法是『**Dispose 模式**』：实现 IDisposable + 析构函数（finalizer）兜底。"

## 模块 3：委托、事件、Lambda

### 【原理】

**委托（Delegate）**：可以理解为"**类型安全的函数指针**"，能把方法当作参数传递。

**三个内置委托**（必背）：
- `Action` / `Action<T>` / `Action<T1,T2>...`：**无返回值**
- `Func<TResult>` / `Func<T, TResult>` / `Func<T1, T2, TResult>...`：**有返回值，最后一个泛型参数是返回类型**
- `Predicate<T>`：返回 bool，常用于筛选

**事件（Event）**：本质是**特殊的委托字段**，只允许 `+=` 和 `-=`，不允许外部直接调用或赋值。

**Lambda 表达式**：是**匿名委托**的简写。

### 【标准答案话术】

**Q1 委托 vs 事件**：
> "**事件本质上是委托的封装**。委托可以被任何代码调用、赋值（=）；事件**只允许订阅（+=）和取消（-=）**，**只能在声明它的类内部触发**。这种限制是为了**防止外部代码乱调用或覆盖**，符合『观察者模式』的封装原则。"

**Q2 Action vs Func**：
> "**Action 没有返回值**，`Action<int>` 表示接受一个 int、不返回的方法。**Func 有返回值**，**最后一个泛型参数是返回类型**，比如 `Func<int, string>` 表示接受 int、返回 string。"

**Q4 闭包（高频陷阱）**：
> "**闭包**是 Lambda 捕获外部变量的现象。比如 `int x = 10; Func<int> f = () => x + 1;` 这里 Lambda 捕获了 x。**坑点**：在 for 循环里捕获循环变量，老版本 C# 会捕获**变量本身（引用）**而不是值，导致所有 Lambda 都用同一个最终值。C# 5.0 之后 foreach 修复了这个问题，但 for 循环依然如此。"

## 模块 4：LINQ & IEnumerable vs IQueryable（EF 必考前置）

### 【原理】

**LINQ（Language Integrated Query）**：把 SQL 风格的查询语法**嵌入 C#**。

**核心区别 IEnumerable vs IQueryable**：

| 维度 | IEnumerable\<T\> | IQueryable\<T\> |
|---|---|---|
| 命名空间 | System.Collections.Generic | System.Linq |
| 执行位置 | **内存中**（客户端） | **数据库**（服务端） |
| 参数类型 | `Func<T, bool>` | `Expression<Func<T, bool>>` |
| 适用场景 | List、Array 等内存集合 | EF Core、数据库查询 |
| 性能 | 取全表回内存再过滤 ❌ | 翻译成 SQL 在 DB 过滤 ✅ |

**延迟执行（Deferred Execution）**：LINQ 查询**不会立即执行**，直到你 `ToList()`、`ToArray()`、`Count()`、`foreach` 时才真正执行。

### 【标准答案话术】

**Q1 标准答案**（这题答好直接加分）：
> "**核心区别是查询执行的位置**。IEnumerable 是**在内存中**执行查询，会把全表数据拉到客户端再做过滤；IQueryable 是**在数据源**（比如数据库）执行，LINQ 表达式会被翻译成 SQL 在数据库里运行。
>
> **底层原因**是参数类型不同：IEnumerable 的 Where 接受 `Func<T,bool>`（**已编译的委托**），IQueryable 的 Where 接受 `Expression<Func<T,bool>>`（**表达式树**）。表达式树是『可解析的代码』，EF Core 可以遍历这棵树把它翻译成 SQL。
>
> **实际影响**：在 EF Core 里写 `db.Users.Where(u => u.Age > 18).ToList()`，会生成 `WHERE Age > 18` 的 SQL；但如果写成 `db.Users.ToList().Where(u => u.Age > 18)`，就是先把全表拉回内存，再在 C# 里过滤，**性能差几个数量级**。"

**Q2 延迟执行**：
> "LINQ 查询定义时**不立即执行**，只有在**枚举结果**时才真正运行（比如 foreach、ToList、Count、First）。这叫『延迟执行』。**好处**是可以链式组合查询条件，最后才执行一次；**坑点**是多次枚举同一个 IEnumerable 会**执行多次查询**，这时应该先 ToList 缓存结果。"

### 【加分项】

> "我项目里遇到过这种问题：分页查询写成了 `db.Users.ToList().Skip(10).Take(10)`，导致每次都把全表加载。改成 `db.Users.Skip(10).Take(10).ToList()` 后，SQL 直接生成 `OFFSET ... FETCH NEXT`，性能从几秒降到几十毫秒。"

## 模块 5：泛型、反射、特性

### 【原理】

**泛型（Generic）**：参数化类型，让代码可复用且**类型安全**。`List<T>` 比 `ArrayList` 强：① 编译期类型检查 ② 无装箱拆箱。

**反射（Reflection）**：运行时**动态获取类型信息**、调用方法、读写属性。代价是**性能差**（比直接调用慢 100~1000 倍）。

**特性（Attribute）**：给类、方法、字段等**打标签**，配合反射读取。常见：`[Obsolete]`、`[Required]`、`[HttpGet]`、`[Authorize]`。

### 【标准答案话术】

**Q1 泛型好处**：
> "**三个好处**：① **类型安全**，编译期检查；② **避免装箱拆箱**（值类型不再被当 object 处理）；③ **代码复用**，一份代码处理任意类型。和 Java 的泛型最大区别是 **C# 泛型在运行时保留类型信息**（不是类型擦除），可以通过 `typeof(T)` 直接拿到。"

**Q2 反射应用**：
> "**应用场景**：① 框架层面的依赖注入（DI 容器扫描特性）；② ORM 映射（EF Core 用反射读 Attribute）；③ 序列化/反序列化；④ 插件系统（动态加载 dll）。**性能优化**：① 缓存 MethodInfo / PropertyInfo；② 用 `Expression Tree` 或 `Emit` 生成委托，把反射调用转成直接调用，性能可达原生 90%。"

## 模块 6：依赖注入（DI）三种生命周期（ASP.NET Core 必考）

### 【原理】

ASP.NET Core 内置 DI 容器，注册服务时要选**生命周期**：

| 生命周期 | 注册方法 | 说明 | 典型场景 |
|---|---|---|---|
| **Singleton** | `AddSingleton` | **全局唯一**，应用启动到关闭只有一个实例 | 配置类、日志、缓存 |
| **Scoped** | `AddScoped` | **每个请求一个实例**（HTTP request 范围内复用） | DbContext、UnitOfWork |
| **Transient** | `AddTransient` | **每次注入都新建** | 轻量、无状态的服务 |

### 【面试必考点：陷阱题】

> **Q：Singleton 里能注入 Scoped 服务吗？**
>
> **答**：**不能直接注入**，会导致 Scoped 服务的生命周期被拉长到 Singleton（"captive dependency"问题）。**解决办法**：① 注入 `IServiceProvider`，每次手动创建 scope；② 改用 `IServiceScopeFactory`。

### 【标准答案话术】

> "ASP.NET Core 内置 DI 容器，注册时三种生命周期：**Singleton 全局单例，Scoped 一次请求一个实例，Transient 每次注入都新建**。
>
> 实际项目中：**DbContext 必须用 Scoped**，因为它不是线程安全的，每个请求独立；**配置和缓存用 Singleton**；**轻量无状态服务用 Transient**。
>
> 有个**经典陷阱**：在 Singleton 服务里注入 Scoped 服务（比如 DbContext），编译不报错但运行时会出问题——Scoped 实例的生命周期被强行拉长到 Singleton，多个请求复用同一个 DbContext 会引发线程问题。**ASP.NET Core 默认开启 Scope 校验**，开发环境会直接报错。**解决办法是注入 `IServiceScopeFactory`，每次手动创建 scope**。"

---

# 🔴 P0-2：ASP.NET Core Web API

按面试出现频率排序的 **7 个子模块**：

| # | 子模块 | 频率 | 难度 |
|---|---|---|---|
| 1 | 启动流程 & Program.cs 结构 | ⭐⭐⭐⭐ | 低 |
| 2 | 中间件（Middleware）管道 | ⭐⭐⭐⭐⭐ | 中 |
| 3 | 路由与控制器（Routing） | ⭐⭐⭐⭐ | 中 |
| 4 | 过滤器（Filter）五大类型 | ⭐⭐⭐⭐⭐ | 中 |
| 5 | 模型绑定 & 模型验证 | ⭐⭐⭐ | 低 |
| 6 | JWT 鉴权（认证 vs 授权） | ⭐⭐⭐⭐⭐ | 中 |
| 7 | 全局异常处理 & 日志 & CORS | ⭐⭐⭐⭐ | 低 |

## 模块 1：启动流程 & Program.cs 结构

**.NET 6+ 极简风格**：

```csharp
var builder = WebApplication.CreateBuilder(args);

// ===== 第一阶段：服务注册（DI 容器配置）=====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// ===== 第二阶段：中间件管道配置（顺序极其重要）=====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();              // CORS 必须在 Auth 之前
app.UseAuthentication();    // 认证：你是谁
app.UseAuthorization();     // 授权：你能干啥
app.MapControllers();

app.Run();
```

**关键认知**：Program.cs 分两段：**注册服务（Services）** 和 **配置管道（Pipeline）**。`builder.Build()` 是分水岭。

**面试话术**：
> ".NET 6 之前是 `Program.cs` + `Startup.cs` 两个文件，Startup 里有 `ConfigureServices` 和 `Configure` 两个方法。.NET 6 引入了**最小托管模型（Minimal Hosting）**，合并到一个 Program.cs 里。本质没变，**仍然是两阶段：先注册服务到 DI 容器，再配置中间件管道**。"

## 模块 2：中间件（Middleware）管道

**核心一句话**：中间件是处理 HTTP 请求/响应的**管道组件**，按注册顺序形成一个**洋葱模型**。

**洋葱模型示意**：

```
Request  →  M1  →  M2  →  M3  →  [Endpoint]
                                       ↓
Response ←  M1  ←  M2  ←  M3  ←  [Endpoint]
```

**自定义中间件三种写法**：

```csharp
// 写法1：内联（简单场景）
app.Use(async (context, next) => {
    // 请求前
    await next();
    // 响应后
});

// 写法2：类（推荐，可测试）
public class MyMiddleware {
    private readonly RequestDelegate _next;
    public MyMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext ctx) {
        // 前置逻辑
        await _next(ctx);
        // 后置逻辑
    }
}
app.UseMiddleware<MyMiddleware>();

// 写法3：终结型（不调用 next，比如静态文件）
app.Run(async ctx => await ctx.Response.WriteAsync("End"));
```

**面试必考点：中间件顺序**：

```
UseExceptionHandler       ← 全局异常，必须最外层
UseHttpsRedirection
UseStaticFiles
UseRouting                ← 路由匹配
UseCors                   ← 跨域，必须在 Auth 之前
UseAuthentication         ← 认证
UseAuthorization          ← 授权（必须在 Authentication 之后）
UseEndpoints / MapXxx     ← 端点执行
```

**Q1 中间件顺序为什么重要**：
> "中间件按注册顺序形成『洋葱模型』管道。**顺序错了会出严重 bug**。比如 `UseAuthorization` 必须在 `UseAuthentication` 之后——因为得先认证（识别身份），才能授权（判断权限）。再比如 `UseExceptionHandler` 要在管道最外层，才能捕获后续所有中间件抛出的异常。`UseCors` 必须在认证之前，否则跨域预检请求 OPTIONS 会被认证拦截，返回 401。"

**Q3 Use vs Run vs Map**：
> "**Use**：链式中间件，处理后调用 next 传给下一个。**Run**：终结型中间件，不传递，直接产生响应。**Map**：分支管道，根据请求路径切到不同的中间件分支。"

**Q4 自定义中间件**：
> "我写过一个**全局请求日志中间件**：在 Invoke 里记录请求路径、参数、耗时、响应状态码，再通过 ILogger 输出，方便排查线上问题。还写过 **统一响应包装中间件**：把所有 API 返回值包成 `{code, msg, data}` 标准结构。"

## 模块 3：路由与控制器

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet]                  // GET api/users
    public IActionResult GetAll() => Ok();
    
    [HttpGet("{id:int}")]      // GET api/users/123
    public IActionResult GetById(int id) => Ok();
    
    [HttpPost]                 // POST api/users
    public IActionResult Create([FromBody] UserDto dto) => Ok();
    
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] UserDto dto) => Ok();
    
    [HttpDelete("{id}")]
    public IActionResult Delete(int id) => NoContent();
}
```

**参数绑定来源**：

| 特性 | 来源 | 用途 |
|---|---|---|
| `[FromBody]` | 请求体（JSON） | POST/PUT 的复杂对象 |
| `[FromQuery]` | URL 查询字符串 | GET 的过滤参数 |
| `[FromRoute]` | URL 路由参数 | `/users/{id}` |
| `[FromHeader]` | HTTP 头 | Token、自定义头 |
| `[FromForm]` | form-data | 文件上传 |
| `[FromServices]` | DI 容器 | 方法级注入 |

**Q1 [ApiController] 作用**：
> "`[ApiController]` 会给控制器开启几个**默认行为**：① **自动模型验证**——绑定失败直接返回 400 BadRequest，不需要手动 `if (!ModelState.IsValid)`；② **推断参数来源**——复杂类型默认 `[FromBody]`，简单类型默认 `[FromQuery]`；③ **错误响应符合 ProblemDetails 规范（RFC 7807）**。"

**Q3 RESTful 设计**：
> "RESTful 核心是**用 HTTP 动词表达操作，用 URL 表达资源**：GET 查、POST 增、PUT 全量改、PATCH 部分改、DELETE 删。URL 用名词复数，不要写动词。状态码语义要正确：200 成功、201 创建成功、204 无内容、400 参数错、401 未认证、403 无权限、404 找不到、500 服务端错。"

## 模块 4：过滤器（Filter）五大类型

**5 种过滤器，执行顺序**：

```
Authorization Filter   ← 鉴权（最早）
        ↓
Resource Filter        ← 资源缓存
        ↓
Action Filter (Before) ← Action 执行前
        ↓
   [Action 方法执行]
        ↓
Action Filter (After)  ← Action 执行后
        ↓
Result Filter          ← 结果返回前
        ↓
Exception Filter       ← 整个流程任意位置异常
```

**Q1 中间件 vs 过滤器**：
> "**层级不同**：中间件是 ASP.NET Core 框架层面的，作用于整个 HTTP 管道；过滤器是 MVC 框架层面的，只在路由到 Controller/Action 后才生效。**能访问的上下文不同**：中间件只有 HttpContext，过滤器能拿到 Controller、Action 方法信息、ActionArguments、ModelState 等。**典型使用边界**：跨切面通用功能（日志、CORS、限流）放中间件；业务相关（权限校验、参数预处理、统一响应包装）放过滤器。"

**全局异常处理**：

```csharp
// 方式 1：自定义 ExceptionFilter
public class GlobalExceptionFilter : IExceptionFilter {
    public void OnException(ExceptionContext ctx) {
        ctx.Result = new ObjectResult(new {
            code = 500,
            msg = ctx.Exception.Message
        }) { StatusCode = 500 };
        ctx.ExceptionHandled = true;
    }
}

// 方式 2（更推荐）：UseExceptionHandler 中间件
app.UseExceptionHandler("/error");
```

> "推荐用 **UseExceptionHandler 中间件**，因为它能捕获**整个管道**的异常（包括中间件本身的），而 ExceptionFilter 只能捕获 Action 内部的异常。"

## 模块 5：模型绑定 & 模型验证

```csharp
public class CreateUserDto {
    [Required(ErrorMessage = "用户名必填")]
    [StringLength(20, MinimumLength = 2)]
    public string Name { get; set; }
    
    [EmailAddress]
    public string Email { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
    
    [RegularExpression(@"^1[3-9]\d{9}$")]
    public string Phone { get; set; }
    
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; }
}
```

> "**简单校验用数据注解**：`[Required]`、`[StringLength]`、`[Range]`、`[RegularExpression]` 等，配合 `[ApiController]` 自动返回 400。**复杂业务校验用 FluentValidation 库**，把校验逻辑写在独立的 Validator 类里。**统一错误响应**：通过 `ConfigureApiBehaviorOptions` 自定义 `InvalidModelStateResponseFactory`。"

## 模块 6：JWT 鉴权

**认证 vs 授权**：

| | 认证 | 授权 |
|---|---|---|
| 中文 | 你是谁 | 你能干啥 |
| 英文 | Authentication（AuthN） | Authorization（AuthZ） |
| 中间件 | UseAuthentication | UseAuthorization |
| 实现 | JWT / Cookie / OAuth | 角色、策略、Claim |

**JWT 结构**（三段，用 `.` 分隔）：

```
xxxxx.yyyyy.zzzzz
 ↓     ↓     ↓
Header.Payload.Signature
```

- **Header**：算法（HS256）和类型
- **Payload**：载荷，存 userId、role、过期时间 exp 等（**不要存敏感信息**，是 Base64 编码不是加密）
- **Signature**：用密钥对前两段签名，**防篡改**

**JWT 工作流程**：

```
1. 用户登录 → 服务端验证账号密码 → 生成 JWT 返回
2. 客户端把 JWT 存起来（localStorage / Cookie）
3. 后续请求 Header 带 Authorization: Bearer <token>
4. 服务端用密钥验证签名 → 解析 Payload → 识别用户身份
```

**代码骨架**：

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => {
        opt.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]))
        };
    });

app.UseAuthentication();
app.UseAuthorization();

[Authorize]                            // 必须登录
[Authorize(Roles = "Admin")]           // 必须 Admin 角色
[Authorize(Policy = "MustBe18")]       // 自定义策略
[AllowAnonymous]                       // 允许匿名
```

**Q2 JWT vs Session**：
> "**Session 是服务端有状态**——用户信息存在服务端内存或 Redis，客户端只存 SessionId。**JWT 是无状态**——所有信息都在 Token 里，服务端只验签名，**易于横向扩展和分布式部署**。
>
> **JWT 优势**：① 无状态，集群无需共享存储；② 跨域友好；③ 移动端友好。
>
> **JWT 劣势**：① **签发后无法主动失效**；② Payload 较大，每次请求都带；③ **不能存敏感数据**。"

**Q3 JWT 怎么主动失效**：
> "JWT 本身**无状态**，签发后无法直接撤销。常见方案：① **短有效期 + Refresh Token 机制**——Access Token 设短（15 分钟），盗了影响也小；② **服务端维护黑名单**——把要失效的 Token 存 Redis，每次请求检查；③ **用 jti（JWT ID）+ 用户登录版本号**——每次改密码或强制下线时把版本号 +1，校验时对比。生产里通常组合用。"

**Q4 Refresh Token**：
> "登录时返回**两个 Token**：**Access Token**（短期，15 分钟，调接口用）和 **Refresh Token**（长期，7 天，存数据库或 Redis）。Access Token 过期后，客户端用 Refresh Token 换新的 Access Token，**Refresh Token 用一次就轮换**。"

## 模块 7：全局异常 & 日志 & CORS

**ILogger 六级别**：
`Trace < Debug < Information < Warning < Error < Critical`

**CORS 跨域**：

```csharp
builder.Services.AddCors(opt => {
    opt.AddPolicy("AllowAll", policy => 
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

app.UseCors("AllowAll");
app.UseAuthentication();
```

> "**CORS 是浏览器的同源策略限制**——协议、域名、端口任一不同就算跨域，浏览器会拦截 AJAX 请求。**注意是浏览器拦截，不是服务端拒绝**。
>
> **预检请求（Preflight）**：浏览器在发送『非简单请求』（比如 PUT、DELETE、带自定义 Header 的）之前，会先发一个 **OPTIONS 请求** 询问服务端是否允许。
>
> **安全提醒**：生产环境不要用 `AllowAnyOrigin()`，要明确白名单。"

## 万能组合题：HTTP 请求完整流程

> "① 请求先进入 **Kestrel 服务器**；
> ② 然后按顺序经过**中间件管道**：UseExceptionHandler（兜底异常）→ UseHttpsRedirection → UseStaticFiles → UseRouting（**匹配路由端点**）→ UseCors → UseAuthentication（**验签 JWT**）→ UseAuthorization（**检查权限**）；
> ③ 到达**端点中间件**（MapControllers）后进入 MVC 框架；
> ④ MVC 内部依次执行 **AuthorizationFilter → ResourceFilter → ModelBinding（模型绑定）+ Validation（模型验证）→ ActionFilter（前置）→ Action 方法 → ActionFilter（后置）→ ResultFilter → 返回结果**；
> ⑤ 期间任何异常被 **ExceptionFilter** 或最外层 **UseExceptionHandler** 捕获；
> ⑥ 响应**反向**走出洋葱模型，最终回到客户端。"

---

# 🔴 P0-3：ORM（EF Core + SqlSugar）

## 模块 1：ORM 基本概念 + EF Core 核心

**ORM 核心 3 个概念**：

| 概念 | 作用 |
|---|---|
| **DbContext** | 代表一次数据库会话，**包含 DbSet 集合** + 变更追踪 + 保存 |
| **DbSet\<T\>** | 代表一张表，可以做 CRUD 和 LINQ 查询 |
| **Entity 实体类** | 对应一行数据，普通 POCO 类 |

**最小示例**：

```csharp
public class User {
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Order> Orders { get; set; }
}

public class AppDbContext : DbContext {
    public DbSet<User> Users { get; set; }
    public DbSet<Order> Orders { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder opt) {
        opt.UseSqlServer("...");
    }
}

using var db = new AppDbContext();
var user = await db.Users.FirstOrDefaultAsync(u => u.Id == 1);
user.Name = "新名字";
await db.SaveChangesAsync();
```

**Q1 ORM 优缺点**：
> "**优点**：① 提高开发效率，不用手写 SQL；② 类型安全；③ 数据库无关；④ LINQ 让查询表达力强。**缺点**：① 性能有损耗；② 复杂查询写起来反而绕；③ 隐藏了 SQL 细节，新手容易写出 N+1、全表查询等坑。**实践原则**：**简单 CRUD 用 ORM，复杂报表查询用原生 SQL**。"

**Q2 DbContext 为什么是 Scoped**：
> "**DbContext 不是线程安全的**，多个线程同时操作同一个实例会抛异常或数据错乱。所以在 ASP.NET Core 里**必须注册为 Scoped**——每个 HTTP 请求一个独立实例。"

## 模块 2：Code First vs DB First & 迁移

**迁移命令**：

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet ef database update PreviousMigrationName
dotnet ef migrations remove
```

**Fluent API vs 数据注解**：

```csharp
// 数据注解
public class User {
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; }
}

// Fluent API
protected override void OnModelCreating(ModelBuilder mb) {
    mb.Entity<User>(e => {
        e.HasKey(u => u.Id);
        e.Property(u => u.Name).IsRequired().HasMaxLength(50);
        e.HasMany(u => u.Orders).WithOne(o => o.User).HasForeignKey(o => o.UserId);
        e.HasIndex(u => u.Email).IsUnique();
    });
}
```

> "**新项目用 Code First**，因为代码就是 schema 的源头。**老系统对接用 DB First**。**生产部署注意**：① 迁移文件要纳入版本控制；② **不要直接 `database update`**，生成 SQL 脚本让 DBA 审核；③ 永远要做**向后兼容的迁移**。"

## 模块 3：导航属性 / 延迟 vs 立即加载

**三种加载策略**：

| 策略 | 关键字 | 何时执行 | 说明 |
|---|---|---|---|
| **Eager Loading**（立即加载） | `Include` / `ThenInclude` | 主查询时一起 JOIN | **推荐**，可控 |
| **Explicit Loading**（显式加载） | `Entry().Collection().Load()` | 手动触发 | 灵活但繁琐 |
| **Lazy Loading**（延迟加载） | 用代理或 virtual | **访问导航属性时**自动发 SQL | **容易触发 N+1** |

**Eager Loading 示例**：

```csharp
var users = await db.Users
    .Include(u => u.Orders)
        .ThenInclude(o => o.OrderItems)
    .Where(u => u.Age > 18)
    .ToListAsync();
```

**Q2 N+1 问题**（高频）：
> "**N+1 问题**：查询 N 条主记录后，访问每条的导航属性时**各发一次 SQL**，总共发 **N+1 次**查询。
>
> **场景例子**：查 100 个用户，再循环访问每个用户的 Orders，会发 1 + 100 = 101 次 SQL。
>
> **解决方案**：① 用 **`.Include()`** 显式加载关联数据；② **关闭 Lazy Loading**；③ 必要时**投影成 DTO**；④ 复杂场景用 **`AsSplitQuery`**。"

## 模块 4：性能优化 & 常见陷阱

```csharp
// ❌ 全表加载到内存再过滤
var users = db.Users.ToList().Where(u => u.Age > 18);
// ✅ 在数据库过滤
var users = await db.Users.Where(u => u.Age > 18).ToListAsync();

// ❌ 默认跟踪
var users = await db.Users.ToListAsync();
// ✅ 只读查询加 AsNoTracking
var users = await db.Users.AsNoTracking().ToListAsync();

// ❌ 循环里 SaveChanges
foreach (var u in users) {
    db.Users.Add(u);
    await db.SaveChangesAsync();
}
// ✅ 批量
db.Users.AddRange(users);
await db.SaveChangesAsync();

// ❌ 先 ToList 再分页
var page = db.Users.ToList().Skip(20).Take(10);
// ✅ DB 层分页
var page = await db.Users.OrderBy(u => u.Id).Skip(20).Take(10).ToListAsync();
```

## 模块 5：AsNoTracking

```csharp
var users = await db.Users.AsNoTracking().ToListAsync();
opt.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
```

> "EF Core 默认对查出来的实体做**变更追踪**——内部维护一个 ChangeTracker 记录每个实体的状态。**对纯读场景**（比如列表展示、报表），这是不必要的开销。**用 `AsNoTracking()` 跳过跟踪**，性能能提升 10%-30%。"

## 模块 6：事务、并发、批量操作

```csharp
// 跨 SaveChanges 的事务
using var tx = await db.Database.BeginTransactionAsync();
try {
    db.Users.Add(u1);
    await db.SaveChangesAsync();
    db.Orders.Add(o1);
    await db.SaveChangesAsync();
    await tx.CommitAsync();
} catch {
    await tx.RollbackAsync();
    throw;
}

// 乐观并发
public class Product {
    public int Id { get; set; }
    public string Name { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; }
}

// EF Core 7+ 批量
await db.Users.Where(u => u.Age < 18).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "minor"));
await db.Users.Where(u => u.Deleted).ExecuteDeleteAsync();
```

## 模块 7：SqlSugar 实战要点

| 维度 | EF Core | SqlSugar |
|---|---|---|
| 风格 | 强类型 LINQ，函数式 | **更接近 SQL 写法**，链式 |
| 上手难度 | 中 | **低** |
| 性能 | 中 | **高** |
| 灵活性 | 偏抽象 | **灵活，支持原生 SQL 混写** |
| 生态 | 微软官方 | 国产，文档中文 |
| 适用 | 大型企业、迁移频繁 | **中小项目、国内业务系统** |

**核心 API**：

```csharp
var db = new SqlSugarClient(new ConnectionConfig {
    ConnectionString = "...",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
});

// 查询
var list = db.Queryable<User>().Where(u => u.Age > 18).ToList();
var single = db.Queryable<User>().First(u => u.Id == 1);
var page = db.Queryable<User>().OrderBy(u => u.Id).ToPageList(1, 10, ref totalCount);

// 联表
var result = db.Queryable<User, Order>((u, o) => new JoinQueryInfos(
    JoinType.Left, u.Id == o.UserId
)).Select((u, o) => new { u.Name, o.Amount }).ToList();

// 增删改
db.Insertable(user).ExecuteCommand();
db.Updateable(user).ExecuteCommand();
db.Deleteable<User>().Where(u => u.Id == 1).ExecuteCommand();

// 事务
db.Ado.UseTran(() => {
    db.Insertable(u).ExecuteCommand();
    db.Insertable(o).ExecuteCommand();
});

// 原生 SQL
var data = db.Ado.SqlQuery<User>("SELECT * FROM Users WHERE Id = @id", new { id = 1 });
```

**Q1 EF Core vs SqlSugar**：
> "**EF Core 是微软官方 ORM，强类型 LINQ，跨数据库能力强，适合大型项目**；**SqlSugar 是国产轻量 ORM，API 更接近 SQL，上手快、性能好，国内中小项目用得很多**。我**选型原则**：项目复杂度高、团队大用 EF Core；快速开发、中小型业务系统、对 SQL 控制要求强的场景用 SqlSugar。两者**心智模型相通**。"

## 数据库性能优化万能模板

> "我从**查询、写入、连接、架构**四层做过优化：
>
> ① **查询层**：① 列表页加 `AsNoTracking`；② 关联查询用 `Include` 显式 JOIN；③ 大列表用**投影 DTO**；④ 分页一定在 DB 层；⑤ 复杂报表直接写**原生 SQL**。
>
> ② **写入层**：① 用 `AddRange` + 一次 `SaveChanges` 批量提交；② 大批量用 **BulkExtensions** 库或 `ExecuteUpdateAsync`；③ 事务粒度尽量小。
>
> ③ **连接层**：连接池配置合理；连接 using 释放。
>
> ④ **架构层**：① 加 Redis 缓存热点数据；② 读写分离；③ 分表分库。"

---

# 🔴 P0-4：SQL（MySQL / SQL Server）

## 模块 1：索引原理（B+ 树）

**B+ 树特点**：

1. **多叉平衡树**，一个节点能存几百到几千个 key
2. **非叶子节点只存索引键**，不存数据
3. **所有数据都在叶子节点**，叶子节点之间用**双向链表**连接
4. **树高一般 3-4 层**

**聚簇 vs 非聚簇**：

| 维度 | 聚簇索引 | 非聚簇索引 |
|---|---|---|
| 数据存储 | **叶子节点直接存整行数据** | **叶子节点存主键值** |
| 数量 | **一张表只能有一个** | 可以有多个 |
| InnoDB | **主键就是聚簇索引** | 普通索引 |
| 查找过程 | 一次树查找直接拿到数据 | 先查到主键，**再回表** |

**Q1 为什么用 B+ 树**：
> "① **B+ 树更矮**——非叶子节点不存数据，能存更多 key，磁盘 I/O 少；② **叶子节点用链表连接**——范围查询和排序极快；③ **所有查询路径长度一致**。红黑树/二叉树树太高，每访问一个节点就是一次磁盘 I/O。"

**Q2 聚簇 vs 非聚簇**（高频必考）：
> "**聚簇索引的叶子节点直接存整行数据**，所以一张表只能有一个聚簇索引。InnoDB 里**主键就是聚簇索引**。**非聚簇索引（二级索引）的叶子节点只存主键值**，通过非聚簇索引查询时，先找到主键，再回到聚簇索引查整行数据，这个过程叫『**回表**』。"

**Q3 回表怎么避免**：
> "用『**覆盖索引（Covering Index）**』——索引里包含了 SELECT 需要的所有字段，**不需要回表**。EXPLAIN 结果里看到 **`Using index`** 就是覆盖索引生效了。"

**Q4 为什么自增主键**：
> "**两个好处**：① **避免页分裂**——自增主键永远在最后追加；② **占用空间小**——主键值在每个非聚簇索引里都存一份。**反例**：UUID 做主键会频繁页分裂；长字符串做主键所有二级索引都膨胀。"

## 模块 2：索引失效场景

**联合索引最左前缀原则**（联合索引 `(name, age, city)`）：

| SQL | 用到索引吗 | 原因 |
|---|---|---|
| `WHERE name = 'x'` | ✅ 用 name | 最左匹配 |
| `WHERE name = 'x' AND age = 18` | ✅ 用 name+age | 连续匹配 |
| `WHERE name = 'x' AND city = 'sz'` | ⚠️ 只用 name | age 缺失 |
| `WHERE age = 18` | ❌ 不用 | 跳过 name |
| `WHERE city = 'sz' AND age = 18 AND name = 'x'` | ✅ 全用 | 优化器会调整顺序 |

**10 个失效场景**：

| # | 场景 | 修正 |
|---|---|---|
| 1 | 函数/计算（YEAR(date)）| 改为范围查询 |
| 2 | 隐式类型转换 | 类型一致 |
| 3 | 前导 % 模糊查询 | 前缀匹配或全文索引 |
| 4 | OR 连接非索引列 | 加索引或 UNION |
| 5 | != / NOT IN / NOT LIKE | 改范围 |
| 6 | IS NULL | 业务避免 NULL |
| 7 | 跳过最左列 | 调整 |
| 8 | 范围查询右侧失效 | 范围列放最后 |
| 9 | ORDER BY 不符合最左前缀 | 建合适索引 |
| 10 | 优化器认为全表更快 | 正常 |

## 模块 3：事务 ACID + 隔离级别

**ACID**：

| 字母 | 全称 | 实现 |
|---|---|---|
| **A** | Atomicity 原子性 | undo log |
| **C** | Consistency 一致性 | 业务约束 |
| **I** | Isolation 隔离性 | 锁和 MVCC |
| **D** | Durability 持久性 | redo log |

**四种隔离级别**：

| 级别 | 中文 | 脏读 | 不可重复读 | 幻读 |
|---|---|---|---|---|
| **Read Uncommitted** | 读未提交 | ❌有 | ❌有 | ❌有 |
| **Read Committed** | 读已提交 | ✅无 | ❌有 | ❌有 |
| **Repeatable Read** | 可重复读 | ✅无 | ✅无 | ⚠️ MySQL 通过 MVCC + 间隙锁解决 |
| **Serializable** | 串行化 | ✅无 | ✅无 | ✅无 |

**Q3 三种并发问题举例**：
> "**脏读**：事务 A 改了一行但没提交，事务 B 读到了 A 的修改，然后 A 回滚了，B 读到的就是『脏数据』。
>
> **不可重复读**：事务 B 读了一行数据，事务 A 改了这行并提交，B 在同一个事务里再读同一行，**数据变了**。
>
> **幻读**：事务 B 用 `WHERE age > 18` 查到 10 行，事务 A 插入了一行 age=20 并提交，B 再查同样条件**变成 11 行**。
>
> **不可重复读关注的是『行被改了』，幻读关注的是『行变多/变少了』**。"

**Q4 MySQL RR 怎么解决幻读**：
> "MySQL 的 RR 级别用**两种机制**解决幻读：① **快照读（普通 SELECT）用 MVCC**——读的是事务开始时的快照版本；② **当前读（SELECT FOR UPDATE / UPDATE / DELETE）用间隙锁（Gap Lock）**——锁住行之间的『间隙』。"

## 模块 4：锁

**按粒度**：表锁 / 行锁 / 页锁
**按模式**：共享锁 S / 排他锁 X / 意向锁 IS/IX

**InnoDB 行锁三种实现**：

| 锁 | 锁什么 | 例子 |
|---|---|---|
| **Record Lock** | 锁单个索引记录 | `WHERE id = 5` |
| **Gap Lock** | 锁索引记录之间的间隙 | `WHERE id BETWEEN 1 AND 10` |
| **Next-Key Lock** | Record + Gap | RR 级别默认 |

**Q2 死锁**：
> "死锁是两个事务**互相等待对方持有的锁**。**MySQL 会自动检测并 kill 一个事务回滚**。
>
> **避免方法**：① **保证多事务以相同顺序加锁**；② **缩短事务**；③ **降低隔离级别**；④ **给热点表加合适索引**——没有索引会升级成表锁；⑤ **业务层做重试**。"

**Q3 行锁陷阱**：
> "InnoDB 的**行锁是加在索引上的，不是数据行上**。如果 WHERE 条件没用到索引，**行锁会升级为表锁**。"

## 模块 5：EXPLAIN

**关键列**：

| 列 | 关注点 |
|---|---|
| **type** | 性能：`system > const > eq_ref > ref > range > index > ALL` |
| **key** | 实际用的索引，NULL 就是没用索引 |
| **rows** | 预计扫描行数，越小越好 |
| **Extra** | Using index（覆盖索引）/ Using filesort（警觉）/ Using temporary（警觉）|

**完整话术**：
> "我重点看 **type、key、rows、Extra** 这四列：
> - **type**：最低要达到 `range`，看到 `ALL` 必须优化；
> - **key**：NULL 就是没走索引；
> - **rows**：越小越好；
> - **Extra**：`Using index` 是覆盖索引最好；`Using filesort` 说明排序没用索引；`Using temporary` 说明用了临时表。"

## 模块 6：慢 SQL 优化三步法

**标准 SOP**：

**第 1 步：定位慢 SQL** —— 慢查询日志 / pt-query-digest / 云控制台
**第 2 步：EXPLAIN 分析** —— type/key/rows/Extra
**第 3 步：从 6 个层面优化** —— 索引 / SQL改写 / 表结构 / 缓存 / 架构 / 业务

**经典优化例子**：

```sql
-- LIMIT 深分页
-- ❌ 慢
SELECT * FROM orders ORDER BY id LIMIT 1000000, 10;

-- ✅ 游标
SELECT * FROM orders WHERE id > 1000000 ORDER BY id LIMIT 10;

-- ✅ 子查询定位主键
SELECT * FROM orders WHERE id >= (
    SELECT id FROM orders ORDER BY id LIMIT 1000000, 1
) LIMIT 10;
```

**万能话术**：
> "我的慢 SQL 排查 **三步法**：
>
> **第一步：定位**——开慢查询日志（long_query_time 设 1 秒）。
>
> **第二步：分析**——`EXPLAIN` 看四个关键列：type、key、rows、Extra。
>
> **第三步：优化**——按这个优先级试：① **加索引**；② **改写 SQL**（去掉 SELECT *、拆大查询、深分页改游标）；③ **改表结构**；④ **加缓存**；⑤ **架构调整**。
>
> **同时关注**：索引不能滥加，会拖慢写性能、增加存储；优化前后**对比 EXPLAIN**，留下数据。"

## 模块 7：联表、子查询、窗口函数

**JOIN 三种**：INNER / LEFT / RIGHT / (FULL — MySQL 不支持)

**窗口函数**（MySQL 8+）：

```sql
SELECT name, score,
    RANK() OVER (ORDER BY score DESC) AS rnk,
    ROW_NUMBER() OVER (PARTITION BY class ORDER BY score DESC) AS rn
FROM students;
```

**每个部门工资第二高**：

```sql
SELECT * FROM (
    SELECT *, DENSE_RANK() OVER (PARTITION BY dept ORDER BY salary DESC) AS rk
    FROM employees
) t WHERE rk = 2;
```

---

# 初试场景战术调整

初试和终面打法完全不同：

| 阶段 | 时长 | 谁主导 | 考察重点 |
|---|---|---|---|
| 1. 自我介绍 | 3-5 min | 你 | 表达能力、节奏、亮点提炼 |
| 2. 项目深挖 | 10-15 min | 技术 leader | **项目真实性**、技术选型、解决问题能力 |
| 3. 基础技术问答 | 10-15 min | 技术 leader | **广度 > 深度** |
| 4. 反问环节 | 3-5 min | 你 | 思考深度、对岗位的兴趣 |
| 5. HR 收尾 | 5-10 min | HR | 薪资、到岗时间、稳定性、上家离职原因 |

**初试核心目标：筛掉明显不合格的人。**

## 初试胜负手（重要性排序）

| # | 关键点 | 占比 |
|---|---|---|
| 1 | **自我介绍** + **项目讲解** | 50% |
| 2 | **AI 工具实战故事** | 20% |
| 3 | 基础技术问答 | 15% |
| 4 | 软素质 | 10% |
| 5 | 反问环节 | 5% |

**初试最容易翻车的**：
- 自我介绍乱、没重点、超时
- 项目讲不清，技术选型说不出理由
- 薪资期望和岗位不匹配
- 离职原因说成抱怨前公司
- AI 工具一问三不知

---

# 简历分析与 CP6 项目定位

## 简历 × JD 匹配度

| JD 要求 | 你的匹配 | 状态 |
|---|---|---|
| 3年以上 C#/.NET | 4 年（2022.9 至今）| ✅ 满足 |
| 本科+计算机相关 | 西安翻译学院 软件工程 本科 | ✅ 满足 |
| .NET Core / .NET 6+ | **.NET 8** 实战 | ✅ 超配 |
| ASP.NET Core Web API | CP6 项目主框架 | ✅ 强匹配 |
| EF Core + SqlSugar | **EF Core + Dapper 双 ORM** | ✅ 强匹配（Dapper 比 SqlSugar 还轻量）|
| MySQL/SQL Server | SQL Server + MySQL + Oracle | ✅ 超配 |
| Vue 2/3 + TS | **Vue3 + TS + Element Plus + Pinia** | ✅ 强匹配 |
| jQuery | 没写 | ⚠️ 弱 |
| 环境检测/LIMS | 无，但有 **ERP/MES 工业系统经验** | ⚠️ 可迁移 |
| **AI 平台 API 调用** | **玩过 Dify/Coze + 搭过 Agent/RAG** | ✅ **核心反差点** |
| **Cursor/Claude Code** | 日常使用 | ✅ 强匹配 |

**关键观察**：
1. 技术匹配度很高，.NET 经验扎实
2. 项目质量好：CP6 是技术栈最全面的项目
3. **AI 经验是杀手锏**——Dify/Coze + Agent/RAG 实战
4. 4年工作经验对应 12-18K 合理，可以争取 15K 以上
5. 2022.9-2023.12 干 1 年 3 个月就跳，需要准备说辞

---

# 板块 1：自我介绍话术

## 战术目标

| 战术目标 | 怎么做 |
|---|---|
| ① **快速建立专业形象** | 结构化、节奏稳、不拖泥带水 |
| ② **主动埋"钩子"** | 让面试官顺着你想讲的点继续问 |
| ③ **匹配 JD 关键词** | 让他听完就觉得"这人能用" |

## 黄金 4 段式结构

```
1. 基本盘（10 秒）   → 我是谁 + 几年经验 + 主要方向
2. 技术栈（30 秒）   → 一句话提炼，重点突出 JD 命中的
3. 项目亮点（90 秒）→ 重点讲 CP6，主动抛 3 个钩子
4. 求职动机（20 秒） → 为什么是这家公司 + 收尾
```

## 1 分钟极简版（电话面）

> "面试官您好，我叫高步宝，2022 年从西安翻译学院软件工程专业毕业，到现在有 **4 年 C#/.NET 全栈开发经验**。
>
> 主要技术栈是 **.NET 8 + ASP.NET Core Web API + Vue 3 + TypeScript**，数据库用过 SQL Server、MySQL、Oracle，比较熟悉 EF Core 和 Dapper 双 ORM 的搭配使用。中间件用过 Redis、RabbitMQ、SignalR，部署上也接触过 Docker 和 K8S。
>
> 我**最近一个比较完整的项目是给一家日企做的 ERP/MES 系统重构**，我独立负责生产管理模块从 0 到 1 的开发，还做过 **5 国语言动态国际化** 和 **基于 RBAC 的权限体系封装**。其中**接口性能优化** 是我做过最有成就感的一块。
>
> 另外这一年我**主动学了 AI 应用方向**，自己用 Dify 和 Coze 搭过智能体和 RAG，所以看到贵司 JD 里『AI 与开发流程深度融合』这条特别感兴趣，我希望能继续往『.NET + AI』这个方向深耕，所以来面这个机会。"

## 3 分钟完整版（带钩子标注）

**【1. 基本盘 10s】**
> "面试官您好，我叫高步宝，27 岁，2022 年从西安翻译学院软件工程专业本科毕业，到现在差不多 4 年 C#/.NET 开发经验，目前在苏州的纬致芯创科技。"

> 🪝 钩子：年龄 + 学历 + 4 年经验，主动盖章 JD 硬性门槛

**【2. 技术栈 30s】**
> "技术栈上，后端我比较熟 ASP.NET Core Web API，从 .NET Framework、.NET Core 一直到现在的 .NET 8 都做过完整项目；ORM 主要用 EF Core 配合 Dapper——复杂业务用 EF，高频查询和报表用 Dapper，性能更可控；前端用 Vue 3 + TypeScript + Element Plus + Pinia 这套；中间件用过 Redis 做缓存、RabbitMQ 做异步消息、SignalR 做实时推送；部署上接触过 Docker 和 K8S。"

> 🪝 钩子 1：`EF + Dapper 双 ORM` 主动抛出
> 🪝 钩子 2：技术栈和 JD 高度重合

**【3. 项目亮点 90s】**
> "我**最有代表性的项目是过去一年做的『クラウンパッケージ ERP/MES 系统重构』**，是给一家日本的包装制造企业做的核心业务系统升级，**我独立负责生产管理这块核心模块**，从需求分析、数据库设计、后端接口到前端页面全流程负责。
>
> 这个项目里我做过**几件比较有挑战的事**：
>
> 第一，**性能优化**——某些核心查询接口在数据量上去之后从 3 秒打到几百毫秒，我从**索引、SQL 改写、缓存、ORM 切换**几个维度做了系统的优化。
>
> 第二，**做了一套通用的 RBAC 权限体系**——用户、角色、菜单、按钮级权限统一管理，封装成基础模块给其他业务复用。
>
> 第三，**做了 5 语言（中英日韩繁）动态国际化**，翻译数据从 API 动态加载。
>
> **整个系统用 Docker Compose 做容器化部署，结合 K8S 多副本运行**，过程里也踩过 SignalR 在集群下连接不稳定这种坑。"

> 🪝 钩子 1："独立负责核心模块"
> 🪝 钩子 2："性能优化" 主动抛
> 🪝 钩子 3："SignalR 集群坑" 主动埋雷

**【4. 求职动机 30s】—— 杀手锏**
> "除了主业之外，**最近这一年我也在主动学 AI 应用方向**——用过 OpenAI、DeepSeek、通义千问几家的大模型 API，也基于 **Dify 和 Coze 搭过几个智能体和 RAG 的小项目**，所以我看到贵司 JD 里写到『AI 平台 API 调用、Agent 与工作流构建』，还有『Cursor、Claude Code 等 AI 辅助开发工具的深度融合』，**和我现在的兴趣方向高度匹配**。
>
> 我希望能在一家**愿意把 AI 真正落地到业务系统**的团队里继续深耕『.NET + AI』这条路，所以特别想了解一下这个机会。以上是我的基本情况。"

> 🪝 致命杀手锏：精准命中 JD 的 AI 要求

## 5 个开场雷区

| # | 雷区 | 你该怎么做 |
|---|---|---|
| 1 | "我没什么准备…" | 哪怕紧张，**直接开始**，不要自我贬低 |
| 2 | 说话快、连珠炮 | **故意慢半拍**，每段之间停 0.5 秒 |
| 3 | 报菜名（堆技术词） | 每个技术**带场景** |
| 4 | 把简历从头到尾念一遍 | **倒序讲**：当前 → 过去，重点讲 CP6 |
| 5 | 求职动机说"听说贵公司不错" | 必须**和 JD 关键词对齐** |

## 自我介绍后必备追问应对

| 追问 | 你的应对方向 |
|---|---|
| "你 CP6 项目具体负责什么？" | → 用 STAR 法详细展开生产管理模块 |
| "EF Core 和 Dapper 怎么分工的？" | → 复杂业务用 EF，**报表/大数据量查询/动态 SQL** 用 Dapper |
| "性能优化具体怎么做的？" | → 索引、SQL改写、缓存、ORM切换 4 个维度 |
| "你的 Dify/Coze 项目做了什么？" | → 你具体玩过什么场景就说什么，别编 |
| "为什么从第一家公司离开？" | → 离职原因话术 |

---

# 板块 2：CP6 项目深度讲解（ERP+MES+WMS）

## CP6 项目定位

> "**CP6 是给一家日本包装制造企业做的一体化业务管理系统**，**覆盖 ERP（订单+销售+采购）、MES（生产执行）、WMS（仓储管理）三大业务域**，**前端 Vue3 + TS，后端 .NET 8 + Web API，数据库 SQL Server**，**已上线运行**。"

## 业务全景图

```
        【ERP 域】                  【MES 域】              【WMS 域】
┌────────────────┐         ┌────────────────┐      ┌────────────────┐
│  客户管理       │         │  生产排程      │      │  原料入库      │
│  销售订单       │ ──订单→ │  工单管理      │ ─领料→│  库位管理      │
│  采购订单       │         │  工序流转      │      │  成品入库      │
│  报价/合同      │         │  车间报工      │ ─出库→│  发货出库      │
│  财务对账       │         │  设备状态      │      │  盘点          │
└────────────────┘         └────────────────┘      └────────────────┘
        ↑                          ↑                       ↑
        └───────── 共享：RBAC 权限 / i18n 国际化 / 基础数据 ──────────┘
```

**核心数据流**：
```
销售下单（ERP）→ 拆分生产工单（MES）→ 车间领料（WMS出库）
       → 生产执行+报工（MES）→ 成品入库（WMS）→ 发货出库（WMS）
```

## 三大模块详解

### 模块 1：ERP（企业资源计划）

| 功能 | 业务说明 |
|---|---|
| **客户/供应商管理** | 客户档案、信用额度、供应商资质 |
| **销售管理** | 询价、报价、订单、合同、回款 |
| **采购管理** | 采购申请、采购订单、到货验收 |
| **基础数据** | 物料、BOM、计量单位、币种 |

**关键技术点**：多级审批流、多币种 + 汇率、PDF/Excel 导出、5 国语言 i18n

### 模块 2：MES（制造执行系统）⭐ 你的主战场

| 功能 | 业务说明 |
|---|---|
| **生产排程** | 销售订单 → 拆分生产工单 |
| **工单管理** | 工单创建、下发、暂停、关闭 |
| **工序流转** | 工单走多道工序（裁切、印刷、装订、包装）|
| **车间报工** | 工人扫码报工，记录完成数量、不良品数 |
| **设备/产能** | 设备状态采集、产能监控 |
| **质检** | 工序间质检、最终质检 |

**关键技术点**：状态机、SignalR 实时推送、复杂查询性能、配置化流程

### 模块 3：WMS（仓储管理系统）

| 功能 | 业务说明 |
|---|---|
| **入库管理** | 采购入库、成品入库、退货入库 |
| **出库管理** | 销售出库、生产领料、退货出库 |
| **库位管理** | 仓库 → 库区 → 货架 → 库位 四级 |
| **库存盘点** | 周期盘点、动态盘点、盈亏处理 |
| **库存预警** | 安全库存、效期预警 |
| **批次/序列号** | 批次追溯、FIFO/LIFO |

**关键技术点**：强一致性（乐观锁/悲观锁）、高并发（RabbitMQ）、条码扫码、库存快照

## 角色边界划分

| 模块 | 真实角色 | 演示话术 |
|---|---|---|
| **MES - 生产管理** | ✅ **独立负责，主战场** | "**我从 0 到 1 设计开发的**" |
| **MES - 其他子模块** | ⚠️ 部分参与 | "**我参与了 XX，主负责是同事 X**" |
| **ERP - 销售管理** | ⚠️ 部分参与 | "**我做了 XX 部分**" |
| **ERP - 其他** | ❌ 同事负责 | "**这块同事负责，我大致了解流程**" |
| **WMS** | ❓ 看实际参与度 | **没参与就老实说"了解流程，没动过代码"** |
| **公共模块（RBAC/i18n）** | ✅ 简历写了"封装" | "**这块我封装的**" |

> ⚠️ **铁律**：**演示时切换到"不是你做的"模块**，**主动说："这块是我同事 X 负责的，我熟悉业务但代码细节没深入"**——别冒充自己全包了。

## STAR 法则深度展开

### S - 背景（20 秒）
> "生产管理模块的核心场景是：**业务下了订单之后，要拆成生产工单**、排产、车间领料、生产报工、入库等一系列流程。
>
> 老系统这块**有几个痛点**：① 工单查询慢，数据多了之后列表页打开要 3 秒以上；② 生产数据没实时反馈，车间报工后管理端要刷新才能看到；③ 流程节点都是硬编码，业务调整一次就要改代码上线。"

### T - 任务（10 秒）
> "我的目标是：**重新设计这块模块**——既要解决老系统的性能和实时性问题，又要把流程做得**可配置**。"

### A - 行动（60 秒）—— 最重要
> "我大概**从 4 个方面动手**：
>
> **第一，数据层**——重新设计了工单、工序、物料这几张核心表的结构，针对高频查询字段加了**联合索引**。复杂报表和列表查询用 **Dapper 直接写 SQL** 控制性能，普通 CRUD 用 **EF Core** 提升开发效率。
>
> **第二，接口层**——后端用 ASP.NET Core Web API，用**全局异常中间件**统一返回结构，用**JWT 做认证**，参数校验靠 ApiController 内置的模型验证，**复杂业务校验用 FluentValidation**。
>
> **第三，缓存和异步**——基础数据（工序、车间、物料分类）放 **Redis** 缓存，过期时间+主动失效；耗时的批量操作（比如订单转工单、批量排产）丢到 **RabbitMQ** 异步处理。
>
> **第四，实时推送**——车间报工、设备状态变化这些事件用 **SignalR 推到前端**，管理端不用刷新就能看到最新数据。"

### R - 结果（20 秒）
> "**最后的效果**：① 工单列表的查询响应从 3 秒优化到 **500 毫秒以内**；② 实时数据延迟从『要手动刷新』变成 **秒级推送**；③ 流程节点配置化之后，**业务调整不用发版**就能改流转规则。系统上线后稳定跑了几个月，目前还在迭代。"

## 5 个技术选型答辩

### Q1：为什么用 EF Core + Dapper 双 ORM？

> "这是**有意识的分工**：
>
> - **EF Core 适合复杂业务、强类型查询、事务**——比如订单创建涉及多张表的写入和关联，EF 的导航属性和 ChangeTracker 帮我们省了很多代码。
> - **但 EF 生成的 SQL 在复杂报表场景不够可控**——比如多表联查 + 分组 + 子查询，EF 生成的 SQL 经常带冗余 JOIN。这种地方我用 **Dapper 直接写原生 SQL**，性能可控。
>
> **分工原则**：写 CRUD 和事务用 EF，读复杂报表用 Dapper。一开始我也担心维护两套有成本，**但实际用下来心智模型是一致的**。"

### Q2：Redis 缓存怎么用？数据一致性怎么保证？

> "缓存我主要用在 **3 类数据**：
>
> ① **基础数据**（工序、车间、物料分类等）——更新频率极低，**Cache Aside 模式**：读时先查 Redis，没有就查 DB 再回填；更新时**先更 DB 再删 Redis**。
>
> ② **用户登录态 / Token 黑名单**——JWT 是无状态的，注销时把 Token 的 jti 放 Redis 黑名单。
>
> ③ **接口限流计数**——基于 Redis 的 INCR 做简单限流。
>
> **一致性问题**主要靠『**Cache Aside + 短过期时间兜底**』。强一致场景（比如库存）不上缓存，直接读 DB。"

### Q3：RabbitMQ 用来做什么？消息丢失怎么办？

> "RabbitMQ 主要做**异步解耦**：① 订单批量转工单这种耗时操作；② 邮件/钉钉通知；③ 跨模块事件。
>
> **消息可靠性 3 个层面保障**：
> ① **生产端**：开启 publisher confirm，发送失败回调重试；
> ② **Broker 端**：队列和消息都设 durable 持久化；
> ③ **消费端**：手动 ack，消费成功才确认；处理失败丢死信队列；**消费逻辑做幂等**。"

### Q4：SignalR 部署在 K8S 多副本下不是有问题吗？

> "**是的，这正是我踩过的一个坑**——SignalR 默认连接是有状态的，多副本下客户端可能连到 A 副本，但消息从 B 副本推出去就到不了客户端。
>
> **解决办法**用 **Redis Backplane**——所有副本通过 Redis 转发消息。配置上就是在 Program.cs 里 `AddSignalR().AddStackExchangeRedis(...)` 加一行。**但要注意 Redis 连接数和带宽消耗**。"

### Q5：5 语言国际化你怎么做的？

> "前端用 **vue-i18n**，但**翻译数据不写在代码里**，而是**从后端 API 动态加载**——这样运营改文案不用前端发版。
>
> 后端有张 `i18n_translations` 表，字段是 `key, lang, value`，前端启动时按当前语言拉一次，缓存到 Pinia。
>
> **难点是兜底**：① 翻译缺失时回退到默认语言；② 富文本/带变量的翻译要支持占位符；③ 缓存版本号控制。"

## 性能优化故事（3 秒→500ms）

### 完整话术（"侦探故事"）

> "**给您讲一个具体的优化案例**：
>
> **【发现问题】** 上线之后业务反馈，**生产工单列表页**打开很慢，一开始几百条数据没事，**数据量涨到几万条之后查询稳定 3 秒以上**。
>
> **【定位原因】** 我先用 **SQL Server Profiler 抓出实际执行的 SQL**，再用 **EXPLAIN（执行计划）** 分析，发现 3 个问题：
> ① 这个查询要**关联工单、订单、客户、车间** 4 张表，关联字段没建索引，**type 是 ALL，扫描了几十万行**；
> ② SELECT 写的是 `SELECT *`，**带回了一堆前端用不到的字段**，且导致**回表**；
> ③ 分页用的是 `LIMIT skip, take`，**深分页时性能急剧下降**。
>
> **【解决方案】** 我从 **4 个维度** 优化：
>
> 第一，**索引**：给关联字段（订单号、客户ID、车间ID）加了**联合索引**，并且 SELECT 的字段都包含在索引里，达到**覆盖索引**效果，避免回表。
>
> 第二，**SQL 改写**：把 `SELECT *` 改成只查前端实际用到的字段；把多层 JOIN 拆解，用**子查询先过滤主表**再 JOIN，减少中间结果集。
>
> 第三，**ORM 切换**：这个查询原来用 EF Core 写的，生成的 SQL 有冗余 JOIN，我**改用 Dapper 手写 SQL**，完全可控。
>
> 第四，**缓存**：列表里有些维度数据（车间名、客户名）变化频率低，从 Redis 取，不再实时关联 DB。
>
> **【结果】** **优化后查询从 3 秒压到 500 毫秒以内**，**性能提升 80% 以上**。
>
> **【沉淀】** 这次之后我**养成几个习惯**：① 任何上线接口先压测；② 列表查询永远不写 `SELECT *`；③ 写复杂查询前先在 DB 里跑 EXPLAIN；④ 高频查询和报表优先考虑 Dapper。"

## 基本面追问应对

| 追问 | 应对话术 |
|---|---|
| **项目多少人？** | "整体团队 X 人，前端 X 人，后端 X 人（**含我**），加测试和产品。" |
| **数据量多大？** | "**核心表**比如工单表大概**几万到几十万**量级，订单表**百万级**，**日活用户几百**。" |
| **上线了吗？多少人用？** | "**已上线**，目前**日活 X 人**，还在持续迭代。" |
| **团队用什么协作工具？** | "**代码 Git/GitLab，需求用 Jira / 禅道，文档 Confluence / 飞书**。" |
| **代码评审有没有？** | "**有，每个 PR 至少一个 Reviewer**，重要改动 leader 也会看。" |
| **怎么发版？** | "用 **CI/CD pipeline**，GitLab Runner → Docker 构建 → 推镜像 → K8S 滚动更新。" |

> ⚠️ **铁律**：**不知道的数据宁可保守说"大致是 X 量级"，不要拍脑袋编**。

## CP6 讲解 5 大雷区

| # | 雷区 | 后果 |
|---|---|---|
| 1 | **吹自己是"主负责人"**，但说不出架构决策 | 一追问技术选型就崩 |
| 2 | **技术词堆砌**（"我们用了 DDD、CQRS、微服务……"），但讲不出落地 | 信号灯立刻闪红 |
| 3 | **数字虚高**（"日活几万、QPS 几千"），但产品体量对不上 | 面试官见过太多了 |
| 4 | **甩锅团队**（"那个是 XX 同事做的，我不太清楚"） | 显得不主动、视野窄 |
| 5 | **不知道就硬编** | 不如老实说"这块我没深入了解，但我理解大致是 XX" |

**救命话术**：
> "这块**当时主要是 XX 同事负责**，**我大致了解的思路是 XX**，**但具体实现细节没完全跟下来**。如果让我做的话，我会**考虑 XX 方向**。"

## 演示风险应对（5 大翻车点）

| # | 风险 | 应对 |
|---|---|---|
| 1 | **环境登录不上 / 网络抽风** | **提前 10 分钟登录测试**；备一份**截图 PPT** 兜底 |
| 2 | **演示中出现 bug** | "这是个已知问题，我们排期在修"——**绝不慌、绝不甩锅** |
| 3 | **被问到不是你做的模块细节** | "**这块是 X 同事负责的，我大致了解 XX**" |
| 4 | **数据敏感 / 不能展示真实数据** | **演示前确认**；实在敏感就只演示界面流程不展开数据 |
| 5 | **被打断追问跑题** | "我先把这个流程演示完，咱们再细聊这块好吗？" |

## 演示前 10 分钟 Checklist

```
[ ] 系统能登录（账号密码本地存一份）
[ ] 关掉所有无关浏览器 tab、微信、QQ
[ ] 桌面整理干净
[ ] 浏览器书签栏隐藏（Ctrl+Shift+B）
[ ] VPN/网络稳定
[ ] 演示数据预先看一遍，确认没有敏感信息
[ ] 屏幕字体调大（Ctrl + +）
[ ] 准备好截图 PPT 兜底
[ ] 自己先把流程走一遍（5 分钟）
[ ] 关掉系统通知（Win+A → 专注模式）
```

---

# 板块 3：MES 工单状态机 + 并发/幂等设计

## 业务场景

> "一个**生产工单**的生命周期是这样的：销售订单进来后，**拆分成工单**，工单经过**排产 → 下发车间 → 车间领料 → 多道工序流转 → 质检 → 完工 → 入库**。
>
> 整个过程**车间工人不停地报工**，**多人同时报工同一个工单的不同工序很正常**；并且**报工接口可能被重复触发**——比如工人扫码后网络抽风，重试了一次。
>
> 所以**核心要解决两个工程问题**：① **状态怎么流转才不乱**；② **并发和重复请求怎么不出数据问题**。"

## 工单 7 个状态

```
待排产 (PendingScheduling)
    ↓
已排产 (Scheduled)
    ↓
已下发 (Released)
    ↓
生产中 (InProduction)  ← 工序流转都发生在这里
    ↓
待质检 (PendingQC)
    ↓
已完工 (Completed)
    ↓
已入库 (Stocked)

异常分支：已暂停 (Paused) / 已取消 (Cancelled)
```

## 状态机三种实现方案

| 方案 | 评价 |
|---|---|
| ❌ if-else 散落 | 难维护，状态规则散落 |
| ⚠️ switch 集中 | 扩展性差 |
| ✅ **状态转移表 + 规则引擎** | 集中配置，扩展性强 |

## 状态转移表 + 规则引擎实现

```csharp
// 状态转移表
private static readonly Dictionary<(WorkOrderStatus, WorkOrderAction), WorkOrderStatus> 
    Transitions = new()
{
    { (PendingScheduling, Schedule),  Scheduled },
    { (Scheduled,         Release),   Released },
    { (Released,          StartWork), InProduction },
    { (InProduction,      ReportDone), PendingQC },
    { (PendingQC,         PassQC),    Completed },
    { (Completed,         Stock),     Stocked },
    // 异常分支
    { (Scheduled,         Cancel),    Cancelled },
    { (Released,          Pause),     Paused },
    { (Paused,            Resume),    Released },
};

public async Task<Result> ChangeStatusAsync(long woId, WorkOrderAction action, string operatorId)
{
    var wo = await _repo.GetByIdAsync(woId);
    
    // 1. 校验状态转移合法性
    if (!Transitions.TryGetValue((wo.Status, action), out var nextStatus))
        return Result.Fail($"不允许从 {wo.Status} 通过 {action} 转移");
    
    // 2. 前置业务校验
    await _validators[action].ValidateAsync(wo);
    
    // 3. 执行状态变更(带乐观锁)
    wo.Status = nextStatus;
    wo.Version++;
    await _repo.UpdateAsync(wo);
    
    // 4. 写状态变更历史
    await _historyRepo.AddAsync(new StatusHistory {
        WorkOrderId = woId,
        FromStatus = wo.Status,
        ToStatus = nextStatus,
        Operator = operatorId,
        Time = DateTime.Now
    });
    
    // 5. 发领域事件
    await _eventBus.PublishAsync(new WorkOrderStatusChanged(woId, nextStatus));
    
    return Result.Ok();
}
```

## 状态机的 3 个好处

> "**这套设计带来 3 个好处**：
>
> ① **新增状态/规则只改一张转移表**，业务扩展性强；
> ② **所有状态变更走同一入口**，方便统一加日志、加权限、加事件；
> ③ **状态历史天然记录**，业务追溯和审计都好做。"

## 并发设计

### 真实并发场景

> "**生产环境真实并发场景有 3 类**：
>
> **场景 A**：**车间组长改状态 + 工人报工同时发生**。
> **场景 B**：**多个工人同时报工同一道工序**——两个工人各完成 50 件，分别报工，**累加数量要正确**。
> **场景 C**：**消息消费的并发**——RabbitMQ 多消费者，**同一条消息可能被两个消费者同时消费**。"

### 并发控制方案

**🥇 主方案：乐观锁** —— 状态变更场景
> "**对于工单状态变更，我用的是乐观锁**——SQL Server 的 `RowVersion` 字段。实体加一个 `[Timestamp] byte[] RowVersion` 字段，EF Core 自动在 UPDATE 时带上 `WHERE RowVersion = @oldVersion`，**如果别人改过，更新行数为 0，EF 抛 `DbUpdateConcurrencyException`**。
>
> **为什么用乐观锁不用悲观锁**：① 工单状态变更冲突概率低，乐观锁性能好；② 悲观锁会长时间持有数据库锁；③ 乐观锁不阻塞读。"

**🥈 补充方案：悲观锁** —— 强一致扣减场景
> "**对于库存扣减、领料扣减这种强一致场景，用悲观锁**——`SELECT ... WITH (UPDLOCK, ROWLOCK)`。"

**🥉 补充方案：Redis 分布式锁** —— 跨服务并发
> "**对于跨服务/跨实例的并发**（K8S 多副本下），用 **Redis 分布式锁**（SETNX + 过期时间）。
>
> **关键点**：
> ① **锁的粒度要细**——锁工单 ID，不要锁整张表；
> ② **必须设过期时间**——防止持锁服务挂了导致死锁；
> ③ **释放锁要带 lockToken**——只能释放自己加的锁，**用 Lua 脚本保证原子**；
> ④ **看场景选择 Redisson 或自己写**。"

**🚫 报工累加** —— 数据库原子操作
> "**多人同时报工累加数量**这种场景，**不要 SELECT 然后改完再 UPDATE**，**直接 `UPDATE table SET CompletedQty = CompletedQty + @qty WHERE Id = @id` 让数据库做原子加法**——一行 SQL，无并发问题。"

## 幂等设计

### 为什么要幂等

> "**幂等**就是『**同一个操作执行一次和执行多次，结果一致**』。
>
> **为什么需要**：① **网络重试**——客户端超时后重试；② **消息队列重复消费**——RabbitMQ at-least-once 投递语义；③ **用户重复点击**。
>
> **如果没幂等**：报工接口被重复调用，**同一批数量被加两次**，库存超扣、生产数据虚高。**这是生产事故级别的 bug**。"

### 幂等 4 种实现方式

**方式 1：幂等键（Idempotency Key）** —— 通用、推荐

```csharp
[HttpPost("report")]
public async Task<Result> ReportAsync(
    [FromHeader] string idempotencyKey, 
    [FromBody] ReportDto dto)
{
    // 1. 检查幂等键
    var cachedResult = await _redis.GetAsync($"idempotent:{idempotencyKey}");
    if (cachedResult != null) return cachedResult;
    
    // 2. SETNX 加锁(防并发)
    if (!await _redis.SetNxAsync($"lock:{idempotencyKey}", "1", 30秒)) 
        return Result.Fail("请勿重复提交");
    
    // 3. 执行业务
    var result = await _service.ReportAsync(dto);
    
    // 4. 缓存结果(供后续重试用)
    await _redis.SetAsync($"idempotent:{idempotencyKey}", result, 1小时);
    
    return result;
}
```

**方式 2：业务唯一键** —— 强一致场景
> "**把『工单ID + 工序ID + 报工时间(精确到秒) + 操作人ID』组合成业务唯一键**，建数据库唯一索引。**重复插入直接被数据库拒绝**。"

**方式 3：状态机本身就保证幂等** —— 状态变更场景
> "**工单状态变更天然就有幂等保护**——状态转移表里 `(InProduction, StartWork) → ?` 不存在。"

**方式 4：乐观锁版本号** —— 更新场景
> "**带版本号的 UPDATE**：`UPDATE WHERE Id = @id AND Version = @oldVersion`，重复执行第二次时 Version 已变，影响行数 = 0。"

### 幂等的 5 个原则

> "**我做幂等的 5 条原则**：
>
> ① **查询接口天然幂等**，不用管；
> ② **新增/更新/删除接口必须幂等**；
> ③ **优先用业务唯一键** —— 不依赖外部存储，最可靠；
> ④ **辅以幂等键**——通用兜底；
> ⑤ **写操作都要带幂等保护**。"

## 高频追问

**Q1：你的状态机如果业务方要新增一个状态，怎么改？**
> "**只改 3 个地方**：① **状态枚举里加一个值**；② **状态转移表里加新转移**；③ **如果有特殊前置校验，加一个 Validator**。**业务代码不用改**。"

**Q2：乐观锁失败了你怎么处理？**
> "**两种策略，看场景**：
> ① **核心业务（比如报工）** —— 业务层 **自动重试 2-3 次**（带退避），还失败就返回错误让用户重试。
> ② **管理操作（比如改工单备注）** —— 直接抛错给用户，**提示『数据已被他人修改，请刷新重试』**。
>
> **不能无限重试**，否则雪崩。"

**Q3：分布式锁挂了怎么办？**
> "**3 个防护**：
> ① **锁必须带过期时间**；
> ② **业务执行时间不能超过锁过期时间**；
> ③ **续期机制**——Redisson 的 Watchdog 自动续期；
> ④ **降级**：极端情况 Redis 全挂，**业务层判断**——是否允许跳过分布式锁，还是直接拒绝服务。"

**Q4：RabbitMQ 消费幂等怎么做？**
> "消费端**用消息 ID 做幂等**：
> ① 消息发布时带 messageId（全局唯一）；
> ② 消费端先查 Redis/DB 看 messageId 是否已处理；
> ③ 处理完写入 'messageId 已处理' 标记；
> ④ **整个 '查重 → 处理业务 → 写标记' 要事务保证原子**。"

**Q5：报工并发场景下你做了压测吗？**
> "**老实说，没做过专业压测**，**只在测试环境用 JMeter 做过简单并发测试**——10 个线程并发报工同一工单，**累加数量正确，无丢失**，**响应时间从 20ms 到 200ms 不等**。**生产场景峰值并发是几十左右**，目前没出过并发问题。"

## 记忆口诀

> **状态机三件事**：**转移表**、**变更服务**、**领域事件**
> **并发三件事**：**乐观锁防变更冲突**、**悲观锁保强一致**、**分布式锁防跨副本**
> **幂等三件事**：**幂等键通用兜底**、**业务唯一键最稳**、**状态机和版本号天然保护**

---

# 板块 4：跨模块串联主线剧本

## 主线剧本：一个客户订单的完整生命周期

**核心故事**：客户『**朝日饮料**』下了 **10 万个饮料外包装盒**的订单，**这个订单在 CP6 系统里走完从询价到发货的全流程**。

## 全流程数据流图

```
       ┌─────────────────  ERP 域  ─────────────────┐
客户   │  ① 询价  ② 报价  ③ 合同  ④ 销售订单 ⑤ 审批  │
朝日 ──→                                              
       │                          │                  │
       │                       订单确认               
       └──────────────────────────┼──────────────────┘
                                  ↓ 领域事件:OrderConfirmed
       ┌──────────────────  MES 域  ─────────────────┐
       │  ⑥ 拆分工单  ⑦ 排产  ⑧ 下发车间  ⑨ 工序流转  │
       │                            ↓                │
       │                         ⑩ 领料请求 ───────────┐
       │  ⑪ 报工(SignalR)  ⑫ 质检  ⑬ 完工            │  
       └──────────────────────────┬──────────────────┘  │
                                  │                     │
                  事件:WorkOrderCompleted               │
                                  ↓                     ↓
       ┌──────────────────  WMS 域  ─────────────────┐ ←┘
       │  ⑭ 原料出库(领料)  ⑮ 成品入库  ⑯ 发货出库   │
       │                                              │
       │  共享:库位/批次/盘点                          │
       └──────────────────────────┼──────────────────┘
                                  ↓
                          ⑰ 发货 → 客户
                          
       ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
       横向贯通：① RBAC 权限  ② i18n 国际化
                 ③ 基础数据(客户/物料/BOM)
                 ④ 审批引擎  ⑤ 消息总线(RabbitMQ)
```

## 5 段式主线讲解（5 分钟版）

### 第 1 幕：ERP 接单（45 秒）

> "**第一步在 ERP 域**：
>
> ① 业务员收到客户『朝日饮料』的询价——10 万个饮料外包装盒，要求 30 天交付。
>
> ② 业务员在系统里 **新建报价单**，选客户、选产品、填数量价格——系统自动**根据 BOM 算出物料成本**，业务员加毛利率出报价。
>
> ③ 客户确认后，**生成销售合同 + 销售订单**。订单提交后**走多级审批**——业务员 → 销售主管 → 财务确认。
>
> ④ **订单审批通过后**，**ERP 发布一条领域事件 `OrderConfirmed`**（订单已确认），通过 **RabbitMQ 异步通知**下游模块。
>
> **这一步的关键技术点**：① 报价 BOM 计算用了 EF Core 的导航属性多层 Include；② 审批流是可配置的；③ **模块间通过 RabbitMQ + 领域事件解耦**。"

### 第 2 幕：MES 生产执行（90 秒）—— 主战场

> "**接下来到 MES 域，这是我独立负责的核心模块**：
>
> ⑥ **MES 服务监听到 `OrderConfirmed` 事件**，自动**拆单**——10 万个包装盒，按工艺路线拆成多个生产工单（裁切 → 印刷 → 装订 → 包装 4 道工序）。
>
> ⑦ **排产**——计划员根据车间产能、设备占用、交期，把工单排到具体的车间和时间窗口。
>
> ⑧ **工单下发**——排好的工单下发到车间。
>
> ⑨ **领料**——工单下发后，**MES 调 WMS 的领料 API**，**这是一个跨模块的 API 调用，做了幂等保护**。
>
> ⑩ **工序流转**——工人在工位扫码确认开工，**走状态机** ——每个状态转移走统一服务。
>
> ⑪ **报工**——工人完成一批就扫码报工。**报工接口做了幂等 + 乐观锁**：
>   - **数据库累加完成数量**（用 `SET CompletedQty = CompletedQty + @qty` 原子加法）
>   - **SignalR 实时推送**到管理端大屏
>   - **发布 `ReportSubmitted` 事件**
>
> ⑫ **质检**——所有工序完成后进入质检。
>
> ⑬ **工单完工**——发布 `WorkOrderCompleted` 事件。
>
> **这一段的技术亮点**：① 状态机用转移表设计；② 报工的乐观锁+幂等键+原子累加 三层防护；③ SignalR 配 Redis Backplane；④ 性能优化把工单列表从 3 秒压到 500ms。"

### 第 3 幕：WMS 仓储联动（45 秒）

> "**WMS 这块全程在配合 MES 和 ERP**：
>
> ⑭ **领料出库**：MES 触发的领料请求到达 WMS，**根据 FIFO 原则**选批次出库，**库存扣减用悲观锁保证强一致**。
>
> ⑮ **成品入库**：MES 工单完工后，WMS 收到 `WorkOrderCompleted` 事件。
>
> ⑯ **发货出库**：客户要求发货时，**ERP 触发出库单 → WMS 执行出库 → 物流系统对接**。
>
> **WMS 这块代码我没主负责**，但因为**和 MES 接口对接很紧密**，**API 协议、事件契约、出错重试机制**都和负责 WMS 的同事一起设计的。"

### 第 4 幕：横向贯穿能力（30 秒）

> "**整个链路上还有几条横向能力贯穿三大模块**：
>
> ① **RBAC 权限**——用户/角色/菜单/按钮 4 层；
> ② **i18n 国际化**——5 语言动态翻译；
> ③ **审批引擎**——ERP 订单审批、MES 工单暂停审批、WMS 盘亏审批都走同一引擎；
> ④ **消息总线**——RabbitMQ 承担所有跨模块异步通信；
> ⑤ **基础数据中心**——客户、物料、BOM、车间、库位这些数据由专门的『基础数据模块』维护。"

### 第 5 幕：收尾 + 主动引导（20 秒）

> "**整体走下来一个订单从询价到发货大概 7-15 天**，**系统全程支撑数据流转、状态可视化、问题追溯**。
>
> **如果您感兴趣**，**我可以挑里面任何一段展开讲深**——比如**状态机怎么设计、SignalR 集群怎么部署、跨模块事件一致性怎么保证、性能怎么优化**——您想听哪一块？"

## 跨模块的 4 个高难度问题

### Q1：模块之间是怎么通信的？为什么用 RabbitMQ 而不是直接 API 调用？

> "**两种方式都用**，按场景选：
>
> **同步实时场景**（比如 MES 领料调 WMS 出库）——**用 HTTP API**，要等响应才能继续业务。
>
> **异步解耦场景**（比如订单确认通知 MES 拆单、工单完工通知 WMS）——**用 RabbitMQ + 领域事件**。
>
> **为什么用 MQ 而不是全 API**：
> ① **解耦**——ERP 不需要知道有几个下游订阅它的事件；
> ② **削峰**——突发请求 MQ 缓冲；
> ③ **可靠**——MQ 持久化 + 重试；
> ④ **异步**——上游不用等下游处理完。
>
> **但 MQ 也不是银弹**——会引入**最终一致性**问题。"

### Q2：跨模块的数据一致性怎么保证？

> "**我们不追求强一致，追求最终一致**——用 **Saga 思想**做补偿：
>
> ① 每个跨模块操作设计**正向操作 + 补偿操作**；
>
> ② **本地事务 + 事件发布**——MES 在本地事务里改工单状态+写消息表，**消息表的消息由独立的发布器轮询发到 MQ**——保证『状态变更和事件发布原子』（**事务消息**或 **Outbox 模式**）；
>
> ③ **下游消费失败重试**——RabbitMQ 自动重试，多次失败进**死信队列**人工介入；
>
> ④ **业务对账**——重要数据每日定时对账。
>
> **极端场景下确实可能出现短暂数据不一致**，但**业务能容忍秒级到分钟级的不一致**，所以选最终一致方案。"

### Q3：如果 MES 服务挂了，ERP 已经确认了订单，怎么办？

> "**RabbitMQ 帮我们兜底**——ERP 确认订单后把 `OrderConfirmed` 事件发到 MQ，**消息是持久化的，MQ 不挂消息不丢**。
>
> MES 挂的时候消息**堆积在队列里**，MES 恢复后从队列继续消费，**业务自动续上**。
>
> **关键前提**：MES 的消费逻辑必须**幂等**——万一消息已经消费一半 MES 挂了，恢复后可能重投，**幂等保证不会重复拆单**。"

### Q4：3 大模块部署在一起还是分开？

> "我们目前**分开部署但在同一个 K8S 集群里**：
>
> ① **代码层面是分模块**——同一个 .NET solution 下三个项目，**领域模型隔离**；
>
> ② **部署层面分独立服务**——三个独立的 Docker 镜像，K8S 各自 Deployment，**支持独立扩容**；
>
> ③ **数据库目前共享**——同一个 SQL Server 实例，但**逻辑库表按模块分组**。
>
> **为什么这么选**：
> - **完全单体**：耦合太高；
> - **完全微服务**：当前业务规模不需要；
> - **『模块化单体』倾向微服务的过渡形态**——**演进式架构**。"

## 5 个翻车点

| # | 翻车点 | 怎么避 |
|---|---|---|
| 1 | **流程讲跑偏，岔到细节回不来** | 心里有主线："**订单 → 拆单 → 排产 → 报工 → 完工 → 入库 → 发货**" |
| 2 | **把不是你做的部分吹成自己做的** | 每个模块结尾**主动声明分工** |
| 3 | **数字虚高** | 用合理量级——**几万到几十万**，**日活几百到一千** |
| 4 | **被问"具体代码怎么实现"时空泛** | 每个关键点准备 1 句**实现细节** |
| 5 | **讲完没收尾，等面试官随便问** | 主动给选项："您想听哪块深入？" |

---

# 板块 5：AI 杀手锏话术（Dify/Coze/RAG）

## 战术目标

| 战术目标 | 怎么做 |
|---|---|
| ① **证明你不是只会嘴上谈 AI** | 讲具体场景、具体平台操作、具体踩坑 |
| ② **证明你能把 AI 落地到业务系统** | 主动把 AI 能力和检测/LIMS 业务场景挂钩 |
| ③ **不夸大经验** | 老实承认是"个人项目/技术探索"，但有深度有思考 |
| ④ **建立 ".NET + AI" 复合人才标签** | 让面试官记住你这个稀缺人设 |

**最重要的认知**：
> **你的 AI 经验是"个人探索 + 小项目"，不是企业级生产经验。面试官问深了会穿。**
> 所以话术原则是：**承认深度有限，但展示思考力 + 学习速度 + 落地意识**。

## 模块 1：AI 辅助开发（Cursor / Claude Code）

### 标准话术

> "**用得挺多**。我目前主要用 **Cursor 和 Claude Code**，已经是日常工作流的一部分了。
>
> **具体怎么用**，我可以举几个场景：
>
> **第一，写脚手架代码**——比如新建一个 CRUD 接口，从 Controller、Service、Repository、DTO、Validator 这一套，**让 AI 一次性生成大体框架**，我再调整业务细节，**写代码的时间能省一半**。
>
> **第二，调试和排查 bug**——把异常堆栈、相关代码片段贴给 AI，让它**分析可能的原因**。
>
> **第三，写单元测试**——AI 写 mock 数据和 case 覆盖比手写快很多。
>
> **第四，重构和迁移**——比如把一段老的同步代码改成 async/await，把 jQuery 改成 Vue 组件。
>
> **第五，写 SQL 和正则**——这两块 AI 比我自己写又快又准。
>
> 我的体感是：**AI 辅助开发的边界是 "审查者" 而不是 "代码生成器"**——AI 生成的代码我会**逐行 review**，业务关键逻辑一定自己确认。**不能放任 AI 写完直接 commit**。"

### 加分追问

> Q：那你觉得 AI 写的代码可以直接上生产吗？

> A：**不行**。AI 写的代码有几个常见问题：① **业务边界感知差**；② **可能编造 API**；③ **安全/性能隐患**——比如 SQL 注入、N+1 查询、内存泄漏 AI 不一定会主动避免。**所以一定要 Code Review，关键代码自己写**。

## 模块 2：大模型 API 集成

### 标准话术

> "调过几家主流的：**OpenAI 的 GPT 系列、DeepSeek、阿里通义千问**，国内的还试过文心一言和豆包。
>
> **集成方式**主要 2 种：
>
> **第一种是直接调 REST API**——大模型基本都遵循 **OpenAI 兼容协议**。**.NET 这边我用过 `OpenAI-DotNet` 和 `Azure.AI.OpenAI` 这两个 SDK**。
>
> **第二种是 SDK 封装**——通义千问有官方 SDK。
>
> **集成时几个关键点**：
> ① **流式输出**（Stream）——用户体验好，长回答不用等几十秒。.NET 里用 `IAsyncEnumerable` 接收流。
> ② **Token 成本控制**——记录每次调用的 input/output token 数，按业务做配额。
> ③ **重试和降级**——大模型 API 不稳定，要做超时重试 + 失败兜底。
> ④ **Prompt 模板管理**——把 Prompt 抽成模板，存配置或数据库。
> ⑤ **上下文管理**——多轮对话要管理 messages 数组，注意 Token 上限要截断或摘要。"

### Token 成本控制

> "**3 个层面**：
> ① **Prompt 精简**——能 5 句话说清的不写 10 句；
> ② **缓存命中**——相同/相似 query 直接用历史结果；
> ③ **按场景选模型**——简单分类用便宜的小模型；
> ④ **多轮对话用 summary**——超长上下文摘要压缩。"

## 模块 3：Dify / Coze 实战经验 ⭐⭐⭐⭐⭐

### 项目模板 A：研发文档问答助手（Dify + RAG）

> "**我自己搭过一个研发文档问答机器人**，用的是 Dify。
>
> **背景**：我之前所在团队的项目文档很多——需求文档、API 文档、设计文档、历史邮件、群聊记录，散落在 Confluence、飞书文档、本地 Word 里。新人入职问问题、老人翻历史决策都得花很多时间，**就想做一个 AI 助手能问答这些文档**。
>
> **技术方案用 Dify 的 RAG 流程**：
> ① **数据导入**——把 Confluence 和飞书文档导出成 Markdown / PDF，导入 Dify 的知识库；
> ② **切片**——按 Markdown 标题层级 + Token 数切片（每片 500 token），保留章节标题做 metadata；
> ③ **向量化**——用 OpenAI 的 text-embedding-3-small 模型生成 embedding；
> ④ **检索**——用户提问后，先向量召回 top-K 片段，再走 LLM（用了 DeepSeek-V3）生成回答；
> ⑤ **加了 rerank**——召回后用 bge-reranker 做二次排序；
> ⑥ **接入飞书/钉钉机器人**——团队群里直接 @ 它问问题。
>
> **效果**：常见问题（比如『xx 接口的鉴权规则是啥』）能直接给出答案+原文出处。**精度不算高，可能 70% 左右**。
>
> **踩过的坑**：① **切片粒度**——太大答非所问，太小丢上下文；② **Embedding 模型选择**——英文模型对中文表现差，换了多语言模型；③ **混合检索**——纯向量召回有时候不如关键词，加了 BM25 + 向量混合检索效果更好。"

### Dify vs Coze 选型话术

> "**Coze（字节）**：上手快、可视化优秀、**国内生态好**、字节系模型免费额度多。**适合非技术团队、快速搭原型、to C 场景**。
> **Dify**：**开源可自部署**、可控性高、对接 LLM 自由度大。**适合企业自建、对数据隐私要求高、需要深度定制的场景**。
> **从企业角度看，敏感数据 / 私有部署需求 → Dify；快速验证 / 公开数据 → Coze**。
>
> 这家公司是检测行业，**数据涉及客户检测报告、商业机密，私有化部署是刚需**，所以**生产场景下应该优先 Dify 或自研**。"

## 模块 4：RAG 原理 + 实战 ⭐⭐⭐⭐⭐

### 标准话术

> "RAG 是 **Retrieval-Augmented Generation**，**检索增强生成**。
>
> 它解决的核心问题是：**大模型有知识截止日期，且不知道你的私域知识**。RAG 就是在大模型回答之前，**先从你的知识库里检索相关内容，喂给模型作为参考**。
>
> **完整流程分两个阶段：**
>
> **【离线阶段 - 建索引】**
> ① **文档加载**——读取 PDF、Word、Markdown、网页等各种文档；
> ② **切片（Chunking）**——把长文档切成 500-1000 token 的片段；
> ③ **向量化（Embedding）**——用 embedding 模型把每个片段转成向量；
> ④ **存入向量库**——存到向量数据库（Milvus、Qdrant、Chroma、PGVector 等）。
>
> **【在线阶段 - 检索 + 生成】**
> ① **用户提问**——把问题也向量化；
> ② **向量检索**——在向量库里找**和问题向量最相似的 top-K 片段**（一般 K=3~5）；
> ③ **（可选）重排序**——用 reranker 模型再排一次序；
> ④ **拼装 Prompt**——把检索到的片段作为『上下文』+ 用户问题，一起塞给大模型；
> ⑤ **大模型生成回答**——基于上下文回答；
> ⑥ **返回结果**——带上来源出处。
>
> **核心难点 4 个**：
> ① **切片策略**——粒度大小直接影响精度；
> ② **检索精度**——纯向量召回不够，**常用『向量+BM25 关键词』混合检索**；
> ③ **Prompt 工程**——怎么让模型『只基于检索内容回答，不要瞎编』；
> ④ **幻觉**——即使有上下文，模型也可能编造。"

### 高频追问

| 追问 | 回答要点 |
|---|---|
| **什么是 Embedding？** | "把文本转成定长向量（比如 1536 维），**语义相近的向量在空间里距离近**。" |
| **怎么算相似度？** | "**余弦相似度**最常用，范围 -1~1，**越接近 1 越相似**。" |
| **向量库选哪个？** | "**小数据用 Chroma 或 PGVector；大规模用 Milvus 或 Qdrant；超大规模可以 Pinecone**。我自己用过 PGVector 和 Chroma。" |
| **RAG 和微调（Fine-tuning）区别？** | "**RAG 不改模型，外挂知识库**；**微调改模型权重**。一般原则：**知识用 RAG，能力（风格、专业回答模式）用微调**。" |
| **怎么解决幻觉？** | "① Prompt 里强制约束『**只基于参考内容回答，找不到答案就说不知道**』；② **强制引用来源**；③ **事实校验**。" |

## 模块 5：Agent / Workflow

### 标准话术

> "**Agent（智能体）** 是一个能**自主规划 + 调用工具 + 多步推理** 的 AI 系统。
>
> 比如用户问『帮我查一下深圳明天天气，然后订一张去深圳的高铁票』——
> - 普通对话模型只能回答『我没法操作』；
> - Agent 会**规划**：① 调天气 API → ② 调订票 API → ③ 综合返回结果。
>
> **Agent 的核心机制**：
> ① **LLM 大脑**——做意图识别和规划；
> ② **工具（Tools / Function Calling）**——封装好的外部能力；
> ③ **记忆（Memory）**——长短期记忆；
> ④ **执行循环**——观察→思考→行动→观察（ReAct 模式）。
>
> **Workflow（工作流）** 比 Agent 更**确定性**——是**预定义的步骤编排**。
>
> **二者区别**：
> - **Agent 是『自主决策』**，更灵活但可控性差；
> - **Workflow 是『预定路径』**，更可控但灵活性差。
>
> **生产环境我个人倾向 Workflow + 受控 Agent**——核心流程用 Workflow 保证稳定，边角场景让 Agent 处理。**纯 Agent 在生产现阶段还是有风险，可能跑飞**。"

## 模块 6：检测行业 AI 落地 5 方向 ⭐⭐⭐⭐⭐

> "我对贵司的检测业务做过一点功课，**初步有几个 AI 落地的方向**：
>
> **方向一：检测报告智能问答**（RAG 场景）
> 检测公司应该积累了大量历史检测报告、标准规范（GB、ISO、ASTM 等）、客户合同。**用 RAG 搭一个内部知识库 Agent**，工程师或客服查标准、查历史报告、查合同条款就能秒级响应。
>
> **方向二：报告自动撰写助手**（生成 + 模板场景）
> 检测报告结构高度模板化——基础信息 + 检测数据 + 结论。**让 AI 基于检测数据自动生成报告初稿**。
>
> **方向三：客户咨询智能体**（Agent + Workflow）
> 客户问『我这个产品需要做哪些检测项』『预计周期多久』『费用大概多少』，**搭一个 Coze/Dify 智能体接入官网或微信公众号**。
>
> **方向四：异常数据识别**（数据分析场景）
> 检测数据里偶尔有异常值，用 AI 做初筛预警。
>
> **方向五：LIMS + AI 流程自动化**（RPA 类场景）
> LIMS 系统里有大量手动录入、状态流转、通知任务，**用 AI 做意图识别 + Workflow 自动派发**。
>
> 这些都是我的初步想法，**具体可行性还要结合贵司的实际业务场景和数据情况判断**。但我相信『**.NET 全栈 + AI 落地**』这个方向上，我能给团队带来一些不一样的价值。"

## AI 话术 5 大雷区

| # | 雷区 | 怎么避 |
|---|---|---|
| 1 | **吹企业级生产经验** | 老实说"个人探索 + 小项目" |
| 2 | **乱用 AI 术语**（"我搞过 Transformer 微调"）| 只讲应用层 |
| 3 | **看不起 AI 工具** | 强调"AI 是放大器，需要工程能力" |
| 4 | **把 AI 当万能** | 强调"有边界、要兜底、要 Code Review" |
| 5 | **不知道 RAG 的细节** | 切片、向量、检索、Prompt 都背熟 |

---

# 板块 6：HR 灵魂三问

## Q1：为什么离开第一家公司？（2022.9-2023.12，1 年 3 个月就跳）

### 话术模板 A（业务方向因素）
> "在第一家公司主要做传统 .NET 项目，技术栈相对老一些，**业务也比较单一**。**我希望接触更新的技术栈和更复杂的业务场景**，所以选择了纬致芯创——果然在那边接触到 .NET 8、容器化、微服务等更现代的技术栈。这次回头看，**当时的选择对我的成长是有帮助的**。"

### 话术模板 B（家庭/地域因素）
> "**主要是地域原因**——第一家在无锡，**家人和发展规划都在苏州**，所以选择了离家更近、平台更大的公司。**和技术或人际无关**。"

### 话术模板 C（成长瓶颈）
> "**第一家公司团队偏小，独立成长空间到一定程度遇到瓶颈**。我希望加入有更完善技术体系、更多技术挑战的团队。**事实证明这次选择是对的**——纬致芯创给了我接触全栈架构、容器化部署的机会。"

### 绝对不能说

- ❌ "公司加班太多" → 显得不能吃苦
- ❌ "钱给得少" → HR 心里给你画问号
- ❌ "和领导/同事不合" → 致命，直接挂
- ❌ "公司效益不好" → 显得没担当

## Q2：期望薪资多少？

**JD 范围**：12-18K·13薪

| 现状 | 建议报价 | 话术 |
|---|---|---|
| **现薪 ≤ 12K** | "**14-16K**" | 留谈判空间 |
| **现薪 13-15K** | "**16-18K**" | 顶上限拼一拼 |
| **现薪 ≥ 15K** | "**18K 及以上**" | 拒绝降薪 |
| **不想说现薪** | 用区间 | "**期望 15-18K，最终看面试综合评估和您这边的薪资体系**" |

**HR 追问"为什么期望这个数"**：
> "**主要 3 个考虑**：① 现司薪资是 XK，我希望适度提升；② 这个岗位要求 .NET 全栈 + AI 落地，**和我能力匹配度高**，我能直接上手；③ 这个数也在贵司 JD 范围内，**我相信是合理的**。**当然具体可以再沟通**。"

## Q3：职业规划是什么？

> "**短期 1-2 年**：在 .NET + AI 这个复合方向上做深，**真正把 AI 能力落地到业务系统**，沉淀实战经验。
>
> **中期 3-5 年**：成长为既懂工程又懂 AI 应用的**技术骨干 / 架构师**，能独立带小团队做完整的 AI 业务项目。
>
> **长期**：希望在 **'AI + 行业'** 这个交叉领域有自己的积累——AI 不只是工具，**而是改变行业的方式**。所以我很看重这次机会——**贵司是检测行业头部，AI 落地空间大**。"

---

# 板块 7：反问环节 5 问

## 🥇 高分问题（让面试官觉得你有思考）

1. **"贵司在 .NET + AI 这块目前是什么阶段？是已经有落地的产品，还是处于探索 / 规划期？我入职后大概会从哪个具体场景切入？"**
   > 🪝 暗示：我已经在想"入职后做什么"

2. **"团队目前的技术栈和工具链是什么样的？比如 AI 这块用 Dify 还是自研框架？代码评审、CI/CD、监控这些工程化做到什么程度？"**
   > 🪝 展示：你关心工程质量

3. **"我做过 MES/ERP 类业务系统，听贵司用 LIMS 系统，**这块业务复杂度和工程实践有什么差异**？我能从哪些方面快速衔接？"**
   > 🪝 展示：你做过业务对比研究

## 🥈 中分问题（看团队和文化）

4. **"团队的技术氛围怎么样？比如有没有定期技术分享、内部技术博客这种机制？"**
5. **"这个岗位的成长路径是什么？比如我未来 1-2 年能往哪些方向发展？"**

## 🥉 低分但必问（务实信息）

6. **"工作时间和加班情况大致是怎样的？"**（**JD 写五天八小时周末双休，确认一下**）
7. **"面试流程大概是几轮？后续多久能收到反馈？"**

## 不要问的

- ❌ "公司福利怎么样？" → 太功利
- ❌ "可以居家办公吗？" → 不合时宜
- ❌ "你们公司主要做什么？" → 完全没做功课

---

# 板块 8：最终备忘单

## 心态定调

```
你的定位:4年.NET全栈 + Vue3前端 + AI实战派
你的杀手锏:Dify/Coze/RAG 实战 + .NET+AI 复合人才
你的主项目:CP6(ERP+MES+WMS)一体化系统,独立负责MES
你的短板:1段经历1年3个月跳过、AI是个人探索非企业级
你的对手:大多只会.NET不懂AI / 只懂AI不懂工程
你的策略:技术稳基本盘,AI抛差异化,主动埋钩子掌控节奏
```

**3 条铁律**：
1. **节奏稳** —— 慢 0.5 秒说话，别抢话
2. **诚实优先** —— 不会就说不会，分工边界要清晰
3. **数字落地** —— 不要"提升一些"，要"3秒→500ms"

## 高频技术题速答卡

### C# / .NET

| 问题 | 速答 |
|---|---|
| **async/await 原理** | 编译器生成状态机，遇 await 释放线程回线程池，完成后恢复执行。本身不开新线程。 |
| **async void 为什么不能用** | 异常无法捕获崩进程、无法 await、难测试。仅事件处理器例外。 |
| **值类型 vs 引用类型** | 值类型栈+拷贝值，引用类型堆+拷贝引用；string 引用类型但不可变。 |
| **GC 怎么工作** | 分代 Gen0/1/2，弱分代假设。大对象进 LOH。 |
| **DI 三种生命周期** | Singleton 全局/Scoped 每请求/Transient 每次新建。**DbContext 必 Scoped**。 |
| **IEnumerable vs IQueryable** | IEnumerable 内存执行，IQueryable 数据库执行（表达式树翻译 SQL）。 |
| **LINQ 延迟执行** | 定义时不执行，枚举时（ToList/foreach/Count）才执行。 |

### ASP.NET Core

| 问题 | 速答 |
|---|---|
| **中间件顺序** | ExceptionHandler → HttpsRedirect → Routing → **CORS** → **AuthN → AuthZ** → Endpoints |
| **[ApiController] 作用** | 自动模型验证返回400 + 参数来源推断 + ProblemDetails 错误格式 |
| **JWT 三段** | Header(算法)/Payload(claims,只Base64不加密)/Signature(防篡改) |
| **JWT 怎么主动失效** | 短期Token+RefreshToken / 黑名单Redis / 版本号(jti) |
| **CORS 预检** | 浏览器同源策略限制；非简单请求先发OPTIONS询问；**CORS必须在AuthN前** |

### 数据库

| 问题 | 速答 |
|---|---|
| **聚簇 vs 非聚簇** | 聚簇叶子存整行(1张表1个,主键);非聚簇叶子存主键,**回表** |
| **覆盖索引** | 索引包含SELECT所有字段，**不回表**，EXPLAIN显示 `Using index` |
| **最左前缀** | 联合索引(a,b,c) → 必须从a开始连续匹配；优化器会调整WHERE顺序 |
| **索引失效场景** | 函数/类型转换/前导%/OR非索引列/!=/跳过最左/范围后列失效 |
| **四种隔离级别** | RU→RC→**RR(MySQL默认,MVCC+间隙锁解决幻读)**→Serializable |
| **EXPLAIN 看哪几列** | type(避免ALL) / key(NULL=没索引) / rows / Extra(避免filesort/temporary) |

### 前端 Vue

| 问题 | 速答 |
|---|---|
| **Vue2 vs Vue3** | 响应式 defineProperty→Proxy；Options API→Composition API；性能更好；TS支持原生 |
| **v-if vs v-show** | v-if DOM增删；v-show CSS切换；频繁切换用show，条件渲染用if |
| **Pinia vs Vuex** | Pinia 是 Vuex5；去掉 mutations；TS 支持好；模块化更简洁 |

## 5 个必须避免的初试错误

1. **🚫 自我介绍超时 5 分钟** → **必须 2.5-3 分钟讲完**
2. **🚫 不会就硬编** → 用"这块我没深入了解，**但思路上我理解大致是...**"
3. **🚫 吹自己做了所有模块** → **分工边界清晰**，承认同事的工作
4. **🚫 抱怨上家公司** → 离职原因往中性方向说
5. **🚫 反问环节"没什么问的"** → **必问 2-3 个**，否则减分

## 明早出门时间表

```
07:00  起床
07:15  早餐时 过一遍这份备忘单 (10分钟)
07:30  对镜子讲 1 遍自我介绍 (3分钟)
07:40  CP6 主线7步主线 心里默念一遍
07:45  整理仪容/服装
08:00  出门 (按面试时间倒推交通时间)
       地铁/路上听个轻音乐 不要刷手机抖音
       提前 20-30 分钟到达
       在咖啡店/楼下休息 5 分钟
       再扫一遍 钩子清单 + AI 杀手锏话术
进门前 深呼吸 3 次 / 微笑 / 主动问好
```

## 进门前最后 3 句口诀

```
口诀一: 慢半拍说话,声音稳。
口诀二: 不会就老实说,诚实是加分。
口诀三: 我是来"合作"的,不是来"被审"的。
```

---

# 板块 9：线上面试专项补丁

## 5 个线上独有优势

| # | 优势 | 怎么用 |
|---|---|---|
| 1 | **可以放小抄** | **桌面侧边/电脑下方贴备忘单**——但**不能盯着看**，**只在关键时刻"扫一眼"** |
| 2 | **演示更方便** | 直接**屏幕共享**演示 CP6，比线下手机/平板演示清晰 10 倍 |
| 3 | **紧张感降低** | 你在自己环境里，**心理压力比线下小** |
| 4 | **可以喝水/调节** | 渴了直接喝，**自然就好** |
| 5 | **结束后不用尬聊** | 关掉视频就行 |

## 5 个线上独有风险

| # | 风险 | 防范 |
|---|---|---|
| 1 | **网络卡顿** | 提前**连有线网/换 5G 热点备用**；测试上行带宽 |
| 2 | **摄像头/麦克风没准备好** | 提前 **30 分钟测试**——用腾讯会议自检 |
| 3 | **背景乱 / 光线差** | 选**白墙/书柜**当背景；**光源在脸前不在背后** |
| 4 | **盯着屏幕不看摄像头** | **重点表态时看摄像头**；听讲时可以看屏幕 |
| 5 | **听不清面试官说话** | 用**有线耳机**；听不清直接说"**抱歉刚才有点卡，您能再说一下吗？**" |

## 备忘单贴在屏幕边框

```
┌─────── 钩子 ───────┐
│ • CP6: ERP+MES+WMS │
│ • 我:独立MES        │
│ • 性能:3s→500ms     │
│ • 双ORM:EF + Dapper │
│ • SignalR集群:Redis │
│ • AI: Dify/RAG      │
│ • 落地: 5方向       │
│                     │
│ HR:                 │
│ • 离职:成长方向     │
│ • 薪资:15-18K       │
│ • 反问:AI落地阶段   │
└────────────────────┘
```

## 屏幕共享演示的"主控感"

- ✅ **主动说**："**我直接共享屏幕给您看一下**"
- ✅ 共享前**关掉所有无关窗口**
- ✅ **共享单个窗口**（不要共享整个桌面）
- ✅ 鼠标移动**慢一点**

## 处理"听不清"的话术

```
听不清: "抱歉刚才有点卡顿,您最后那句能再说一下吗?"
听到一半: "您是问 XX 对吗?" (先确认理解)
对方静音了: "您那边好像没声音"
```

## 眼神管理

```
你在说话时           → 看摄像头(让他感觉被注视)
你在听他说话时       → 可以看屏幕(看他的脸)
你在思考时           → 自然往侧上方看(像在想问题)
重点表态/求职动机时   → 一定看摄像头
```

## 面试前 30 分钟 设备 Checklist

```
[ ] 网络:有线优先,Wi-Fi测试上行≥5Mbps
[ ] 电脑充电(别打到一半没电)
[ ] 摄像头:腾讯会议自检,画面清晰
[ ] 麦克风:有线耳机优先,降噪开启
[ ] 屏幕:亮度调高,字体放大(共享时面试官能看清)
[ ] 桌面:整理干净,关掉所有通知
[ ] 背景:白墙/书柜,不要床/衣柜
[ ] 光线:正面光,不逆光
[ ] 着装:上半身整洁(衬衫/Polo 即可)
[ ] 关掉:微信/QQ/邮件/钉钉/系统弹窗
[ ] 备忘单:贴在显示器侧边
[ ] 演示账号:提前登录测试
[ ] 喝水:杯子放在桌上
[ ] 上厕所:面试前 10 分钟去一下
[ ] 通知家人/室友:这段时间别打扰
```

## 5 个线上禁忌

1. ❌ **盯着备忘单不看摄像头**
2. ❌ **背景出现家人/宠物/床/衣物**
3. ❌ **吃东西、嚼口香糖**
4. ❌ **手机响、电脑弹窗**
5. ❌ **网络断了惊慌失措**——**淡定重连**：「**不好意思网络抖动了一下，咱们继续吧**」

---

# 板块 10：模拟面试

## 模拟面试规则

**场景设定**：
- 面试官扮演 **华测检测的技术负责人 + HR**（一面通常是技术 leader 主导）
- **35-40 岁，C#/.NET 老兵，做检测行业 ERP 多年，最近在推 AI 落地**
- 风格：**不犀利但抓细节，听到模糊会追问，听到亮点会鼓励**

## 10 题分布

| # | 类型 | 难度 |
|---|---|---|
| 1 | 自我介绍 | ⭐ |
| 2 | 项目背景 | ⭐⭐ |
| 3 | 技术选型答辩 | ⭐⭐⭐ |
| 4 | 项目深挖（性能） | ⭐⭐⭐ |
| 5 | 技术深挖（并发/幂等） | ⭐⭐⭐⭐ |
| 6 | 系统架构思维 | ⭐⭐⭐⭐ |
| 7 | AI 经验真实性验证 | ⭐⭐⭐ |
| 8 | AI 落地业务思考 | ⭐⭐⭐⭐ |
| 9 | HR 离职原因 | ⭐⭐⭐ |
| 10 | HR 薪资 + 反问 | ⭐⭐⭐ |

## 答题原则

- 想怎么说就怎么说，**别完美主义**
- **不会就说不会**——比硬编更值钱
- 长度自己控制（建议 1-3 分钟）
- **觉得这题难就直接说"过"，换一题**

## Q1：请做个自我介绍（3 分钟左右）

**场景**：你打开腾讯会议，连接成功。面试官的画面出现——一个 30 多岁的男士，背后是公司 logo 墙。他笑着打招呼：

> 面试官：
>
> "你好你好，能看到我吗？听得清楚吗？OK，那咱们开始啊。
>
> 我看了下你的简历，4 年 .NET 经验，做过 ERP 系统重构，挺符合我们这边的要求。
>
> **你先做个自我介绍吧，3 分钟左右就行。**"

**线上小提示**：
- 第一句先打招呼："**面试官您好**"，**看摄像头说**
- 中间有钩子时**可以低头看一眼备忘单**（自然一点）
- 讲到最后求职动机时，**眼睛回到摄像头**——让他感受到"诚意"

---

# 最终自检：10 件事

- [ ] **自我介绍 3 分钟版** 能脱稿讲 1 遍
- [ ] **CP6 7 步主线** 能脱稿讲 1 遍
- [ ] **AI 杀手锏 90 秒话术** 能脱稿讲 1 遍
- [ ] **5 个技术答辩**（双ORM/性能/SignalR/状态机/并发）至少背 3 个
- [ ] **离职原因话术** 选定 1 个版本背熟
- [ ] **薪资报价** 想清楚说哪个区间
- [ ] **职业规划话术** 能讲
- [ ] **反问 3 个问题** 准备好
- [ ] **演示账号 + 网络 + 兜底 PPT** 准备就位
- [ ] **华测检测官网** 看 5 分钟（业务范围/公司背景）

---

> **结语**：本文档为 2026-05-27 晚间面试备战会话整理稿。
> 候选人优势：4 年 .NET 实战 + CP6 全栈项目 + AI 工具实战派（Dify/RAG）。
> 核心战术：技术稳基本盘 + AI 抛差异化 + 主动埋钩子掌控节奏。
>
> **明天加油！** 🚀
