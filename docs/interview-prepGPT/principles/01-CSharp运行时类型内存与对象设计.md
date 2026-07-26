# 01 · C# 运行时、类型、内存与对象设计

本章不按语法手册顺序列关键字。我们从一个库存对象被创建、传参、放入集合、比较、修改、回收的完整生命周期理解 C#。目标是看到代码就能预测数据在哪里、复制了什么、谁能观察到修改，以及类型设计怎样保护业务不变量。

## 1. 从源码到运行时发生了什么

C# 编译器通常把源码编译为包含 IL 和元数据的程序集。运行时加载类型，JIT 在方法实际需要执行时把 IL 编译为当前平台机器码。

```text
.cs source
→ Roslyn compile
→ assembly (.dll: IL + metadata)
→ CLR load/type verification
→ JIT selected methods
→ native instructions
```

这解释了几个现象：

- 程序能在不同 CPU/OS 的对应 .NET runtime 上运行。
- 第一次调用某些方法有 JIT 暖机成本。
- 反射能读取程序集元数据。
- 泛型在运行时仍保留类型信息，不等同于 Java 传统类型擦除模型。

面试不要把 IL 说成“解释执行脚本”。现代 .NET 还可能使用 ReadyToRun/AOT，具体部署模式会改变编译时点，但托管类型和运行时服务仍是核心。

## 2. 值类型与引用类型真正的区别

核心不是“值类型在栈、引用类型在堆”。准确区别：

- 值类型变量直接包含该类型的值。
- 引用类型变量包含对对象的引用，变量与对象是两件事。

位置取决于上下文：值类型作为 class 字段时随对象位于托管堆；被闭包捕获的局部也可能进入堆上 display class；JIT 还可做寄存器分配和逃逸优化。

### 2.1 值复制

```csharp
var a = 10;
var b = a;
b++;
```

`b` 得到独立的 10，所以修改 b 不影响 a。

struct 同理，整个值语义被复制：

```csharp
var p1 = new Quantity(10m, "KG");
var p2 = p1;
```

若 struct 很大且频繁传递，复制成本可能明显；可使用 `in`、`ref readonly` 或改为引用类型，但先测量。

### 2.2 引用复制

```csharp
var s1 = new StockDto { AvailableQty = 10m };
var s2 = s1;
s2.AvailableQty = 5m;
```

复制的是引用，s1/s2 指向同一对象，所以都观察到 5。

这不代表“引用类型按引用传递”。默认参数传递仍是按值，只是这个值恰好是引用。

## 3. 参数传递：方法拿到变量的什么

```csharp
static void Mutate(StockDto x) => x.AvailableQty--;

static void Reassign(StockDto x) =>
    x = new StockDto { AvailableQty = 999m };
```

调用：

```csharp
var stock = new StockDto { AvailableQty = 10m };
Mutate(stock);   // 同一对象被改成 9
Reassign(stock); // 调用者仍指向原对象 9
```

`Reassign` 只修改参数变量自己的引用副本。要让方法替换调用者变量，需要 `ref`：

```csharp
static void Reassign(ref StockDto x) =>
    x = new StockDto { AvailableQty = 999m };
```

### 3.1 `ref`、`out`、`in`

| 关键字 | 调用前必须赋值 | 方法可读 | 方法必须/可以写 | 典型用途 |
|---|---:|---:|---:|---|
| `ref` | 是 | 是 | 可以 | 双向更新变量 |
| `out` | 否 | 赋值后 | 必须赋值 | TryParse 多返回值 |
| `in` | 是 | 是 | 只读视图 | 避免大 struct 复制 |

不要用 `ref` 代替清晰返回值。它增加别名和副作用，调用者更难推理。

## 4. 栈、堆和对象布局只需要掌握到可用程度

每次方法调用通常有栈帧，包含返回地址、局部/参数等实现所需数据；引用对象通常在托管堆，由 GC 管理。对象还包含运行时类型/同步相关头部，字段按布局和对齐存放。

开发者需要记住的不是固定字节图，而是：

- 大量短命对象会增加分配与 GC 压力。
- 持有引用会延长对象存活，不是“出了方法就释放”。
- static、事件订阅、缓存和后台任务常造成长生命周期引用。
- 非托管资源不能等 GC 最后才清理，需要 Dispose。

## 5. GC 三代的工作模型

大多数对象短命，所以 GC 使用分代：Gen 0、Gen 1、Gen 2。新对象通常进入 Gen 0；存活后晋升。大对象进入 LOH，回收/压缩策略成本不同。

### 5.1 什么是 GC root

运行时从活动线程栈、静态字段、GC handles 等根开始遍历可达对象。不可达对象才可回收。

“内存泄漏”在托管世界常指对象仍可达但业务已不需要，例如 singleton 缓存永不淘汰、事件没有取消订阅、字典 key 无限增长。

### 5.2 Dispose 与 GC 的分工

GC 管托管内存；`IDisposable` 负责文件句柄、数据库连接、socket 等资源的确定性归还：

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
```

`using` 编译为 try/finally 语义，即使异常也释放。不要手动到处调用 Dispose 而忽略异常路径。

## 6. 装箱与拆箱

值类型需要作为 `object` 或某接口使用时，可能装箱为堆对象：

```csharp
int n = 42;
object o = n;       // boxing
int m = (int)o;     // unboxing + value copy
```

非泛型集合过去会频繁装箱和运行时强转。`List<int>` 保持类型安全并避免这种装箱。

接口调用不总必然产生可观察装箱，JIT/泛型约束可能优化；性能结论用 BenchmarkDotNet/Profiler 证实，不用关键字猜。

## 7. 数值类型：为什么数量和金额用 decimal

`double` 使用二进制浮点，许多十进制小数无法精确表示：

```csharp
Console.WriteLine(0.1 + 0.2 == 0.3); // 通常 false
```

`decimal` 用十进制表示方式，适合金额、单价和需要十进制精度的数量。CP6 库存数量使用 `decimal(21,8)`，单价常用 `decimal(18,4)`。

仍要定义：

- 舍入时点。
- 舍入模式，例如 ToEven/AwayFromZero。
- 单位换算精度。
- SQL 与 C# precision/scale 一致。
- 汇总时先舍入明细还是最后舍入。

`decimal` 不是“永不出错”，只是表示基础更符合业务。

## 8. string 不可变与比较规则

字符串修改实际产生新值：

```csharp
var code = "p001";
code = code.ToUpperInvariant();
```

循环拼大字符串使用 `StringBuilder`，但少量插值更清晰。

业务编码比较要明确：

```csharp
string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
```

展示给人的自然语言排序/比较可能需要 culture；机器协议、权限 key、编码通常用 ordinal。数据库 collation 也会影响大小写与重音，C# 和 SQL 规则不一致可能出现“内存认为不同，唯一索引认为相同”。

## 9. Nullable：把缺失放进类型

`decimal?` 表示数值可能不存在，不要用 0 代替“不知道”。`string?` 配合可空引用类型，让编译器做流分析。

```csharp
if (stock.OwnerCd is { Length: > 0 } owner)
{
    Use(owner); // 这里 owner 非空
}
```

空值运算符：

```csharp
x?.Property
x ?? fallback
x ??= Create()
x! // 只压制警告，不做运行时保护
```

`!` 应表示“我有编译器看不到的可靠不变量”。若只是为了消警告，可能把问题推迟成 NullReferenceException。

## 10. 相等性：引用相等、值相等和业务相等

class 默认 `Equals` 通常是引用身份；record 默认按成员做值相等；struct 默认值相等但性能/语义可自定义。

若重写 Equals，必须保持：自反、对称、传递、稳定，并同步重写 GetHashCode。对象放入 Dictionary/HashSet 后，参与哈希的字段不能再变化，否则对象可能“在集合里却找不到”。

实体常按身份判断，值对象按所有组成字段判断。不要让 EF 实体的可变字段全部参与 hash。

## 11. record、class、struct 如何选

| 需求 | 候选 |
|---|---|
| 有身份、生命周期、可变状态 | class |
| 不可变数据载体、值相等 | record class |
| 很小、值语义、高频内联 | readonly record struct / struct |
| EF Core 业务实体 | 通常 class |

DTO 用 record 不是强制。序列化、模型绑定、团队风格和更新方式都影响选择。

## 12. OOP 的目标是保护变化和不变量

继承不是复用代码的默认工具。先问关系是“is-a”还是“has-a”。CP6 的 `Stock : BaseBizEntity` 复用 TenantId、审计字段、软删除和 RowVersion，是基础数据能力的继承；业务策略更适合组合服务。

### 12.1 封装不是把字段改 private 就结束

如果任何服务都能直接改 `PhysicalQty`，库存不变量依然暴露。更强设计让变化通过领域方法：

```csharp
stock.ApplyInbound(qty);
stock.Reserve(qty, allowNegative);
```

但 EF 实体完全领域化也会增加映射和团队学习成本。当前 CP6 把不变量集中在 `StockMovementService`，是应用服务集中式保护。评价时说明现实取舍，不贴“贫血模型一定坏”的标签。

### 12.2 接口的价值

接口把调用者依赖从具体实现转成能力契约，例如 `IStockMovementService`、`IWmsNotifier`。它支持替换和测试，但每个类都机械加接口会制造噪声。

使用接口的理由：

- 有真实替代实现。
- 跨层稳定契约。
- 需要隔离外部副作用。
- 调用者不应依赖实现细节。

## 13. 泛型把算法与类型参数分离

```csharp
public interface IRepository<T> where T : BaseEntity
```

约束使实现可安全访问 BaseEntity 成员，并限制错误类型。

常见约束：`class`、`struct`、`notnull`、`new()`、基类、接口、`unmanaged`。约束应来自算法真实需求，不要为了方便实例化就滥用 `new()`，它只能调用无参构造且限制工厂策略。

### 13.1 协变与逆变的方向

只产出 T 的接口可协变：`IEnumerable<Dog>` 可当 `IEnumerable<Animal>`。

只消费 T 的委托可逆变：能处理 Animal 的处理器当然能处理 Dog。

记忆：producer out，consumer in。若既读又写同一个 T，通常不可变。

## 14. 集合按访问模式选择

| 集合 | 强项 | 常见误用 |
|---|---|---|
| List | 顺序、索引、尾部追加 | 频繁 Contains 做大规模查找 |
| Dictionary | key→value 平均 O(1) | 可变 key、重复 Add |
| HashSet | 去重/成员判断 | 需要稳定顺序 |
| Queue | FIFO 工作队列 | 多线程无同步 |
| Stack | LIFO/撤销 | 当普通列表 |
| ConcurrentDictionary | 并发字典操作 | 复合逻辑误以为自动原子 |

复杂度是平均/摊销模型，还受 hash 质量、缓存局部性和数据量影响。几百条数据时清晰常比理论 O(1) 更重要。

## 15. 委托、Lambda、闭包

委托是类型安全的方法引用。`Func<T,bool>` 让筛选策略作为参数传入；事件限制外部只能订阅/退订，不能直接触发。

闭包捕获的是变量，不一定是当时的值：

```csharp
var actions = new List<Action>();
for (var i = 0; i < 3; i++)
{
    var copy = i;
    actions.Add(() => Console.WriteLine(copy));
}
```

闭包会让捕获变量进入编译器生成对象并延长生命周期。长生命周期事件处理器捕获大对象是常见泄漏源。

## 16. 异常是失败控制流，不是普通分支

只捕获能处理的异常。`catch (Exception) {}` 会隐藏数据不一致和运维信号。

```csharp
catch (DbUpdateConcurrencyException ex)
{
    // 转成稳定 409 / 领域冲突，保留 inner/日志
}
```

`throw;` 保留原堆栈；`throw ex;` 重置抛出位置。包装异常时把原异常放 inner exception。

事务后 best-effort 通知可以捕获异常不让主交易失败，但至少需要日志、指标或 outbox；“业务上不抛给用户”不等于“完全吞掉”。

## 17. 现代语法要服务表达

- 模式匹配：把类型/属性条件放进清晰分支。
- switch expression：适合纯映射，复杂副作用用普通控制流。
- `required`：提醒初始化，但反序列化/ORM 行为需验证。
- `init`：构造后不可普通赋值，适合不可变 DTO。
- collection expressions：减少样板，但团队/目标框架要支持。
- raw string：SQL/JSON 更清晰，仍需参数化。

面试重点不是能列 C# 版本，而是能解释语法怎样减少非法状态或样板。

## 18. 必做实验

1. 运行 `labs/01-How-to搭建CSharp面试实验场.md` 的值/引用实验。
2. 写一个可变 struct 放入 List，取出后修改副本，解释为什么集合内未变。
3. 用可变对象作为 Dictionary key，修改 hash 字段后观察查找。
4. 比较 decimal/double 的 0.1 累加。
5. 创建事件订阅者不退订，用弱引用/内存工具观察生命周期。
6. 演示 `throw` 与 `throw ex` 堆栈差异。

## 19. 闭卷问题

1. 为什么“值类型都在栈”是错的？
2. 引用类型默认参数为什么仍叫按值传递？
3. `ref` 什么时候值得用，什么时候降低可读性？
4. GC 能回收为什么仍需要 Dispose？
5. decimal 解决了什么，又没解决什么？
6. 为什么可变对象不适合做哈希 key？
7. 何时选 record，何时选实体 class？
8. 接口和继承分别保护什么变化？
9. 闭包如何改变变量生命周期？
10. best-effort catch 为什么仍要可观测？

