# 02 · OOP、泛型与集合

## 1. 面向对象不是“继承四件套”

面向对象的实用目标是把状态、行为和不变量放到有清晰责任的边界中。封装、抽象、多态、继承是手段，不是评分口号。

### 封装

封装不是把字段全部改成 `private`。真正的封装是让无效状态无法轻易产生。

库存若允许任意代码直接修改 `PhysicalQty`，就可能忘记同步 `AvailableQty` 和流水。CP6 用 `IStockMovementService` 作为库存变动入口，体现了“集中不变量”。不过实体属性仍有公开 setter，所以它依赖服务边界和代码纪律，不是强封装的最终形态。

### 抽象类与接口

| 比较 | 抽象类 | 接口 |
|---|---|---|
| 表达 | “是一种”并共享实现/状态 | “能做什么”的契约 |
| 多继承 | 类只能继承一个基类 | 可实现多个接口 |
| 字段/构造 | 可以 | 不保存实例字段 |
| 演进 | 基类修改影响继承层次 | 默认接口实现可缓解，但要慎用 |
| CP6 | `BaseEntity` | `IStockMovementService`、`IAuditable` |

选择问题：如果只需要能力契约和可替换实现，优先接口；如果确实存在稳定公共状态和模板实现，再考虑抽象基类。

### 多态

调用方依赖接口，运行时注入不同实现。例如桥接服务可选择真实实现或 NoOp 实现。多态的价值是让调用方不需要用大段 `if` 判断具体类型。

但“面向接口”不等于每个类都机械创建接口。没有替换点、没有测试边界、没有多个实现时，接口可能只是额外跳转。

## 2. SOLID 用项目语言回答

| 原则 | 人话 | CP6 可讨论点 |
|---|---|---|
| SRP | 一个模块只有一类变化原因 | Controller 处理 HTTP，Service 处理库存规则 |
| OCP | 扩展行为尽量不改稳定核心 | 工作流节点处理器集合、桥接接口 |
| LSP | 子类型替换基类型后契约仍成立 | NoOp 实现不能破坏调用方预期 |
| ISP | 不强迫调用方依赖不用的方法 | 小型业务接口比“万能 Service”清晰 |
| DIP | 高层依赖抽象，不直接绑具体基础设施 | `IStockFinBridge`、`IWmsNotifier` |

面试不要只背英文。给一个违反例子，再给修复方式。

## 3. 泛型解决什么

泛型让算法或容器在保持类型安全的同时复用，并避免部分值类型装箱。

CP6：

```csharp
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> FindAsync(Guid id);
    Task<T> AddAsync(T entity);
}
```

`where T : BaseEntity` 给实现两个编译期保证：

1. `T` 至少拥有 `Id`、`CreateDate` 等成员。
2. 调用者不能把任意类型传给仓储。

开放泛型注册：

```csharp
services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
```

容器在解析 `IRepository<Stock>` 时构造 `RepositoryBase<Stock>`。这不是提前注册了每个闭合类型，而是注册了构造规则。

## 4. 泛型约束速查

| 约束 | 含义 | 典型用途 |
|---|---|---|
| `where T : class` | 引用类型 | ORM/服务契约 |
| `where T : struct` | 非可空值类型 | 数值/值对象算法 |
| `where T : BaseEntity` | 指定基类 | 通用实体操作 |
| `where T : IFoo` | 实现接口 | 调用约定能力 |
| `where T : new()` | 有公共无参构造 | 工厂创建；通常放最后 |
| `where T : notnull` | 不能是可空类型 | Dictionary key 等 |
| `where T : unmanaged` | 不含托管引用 | interop/底层内存 |

约束不是越多越好。约束会减少适用面，应该只表达算法真正需要的能力。

## 5. 协变与逆变

记忆方向：

- 只“产出” T，可以协变 `out T`。
- 只“消费” T，可以逆变 `in T`。

```csharp
IEnumerable<string> strings = new List<string>();
IEnumerable<object> objects = strings; // 协变

Action<object> printObject = Console.WriteLine;
Action<string> printString = printObject; // 逆变
```

`List<string>` 不能赋给 `List<object>`，否则调用方能向其中加入整数，破坏原列表类型安全。

## 6. 集合选择不是背复杂度表

先问访问模式：

- 要按位置连续遍历：`List<T>`。
- 要按唯一键查找：`Dictionary<TKey,TValue>`。
- 要去重或快速判断存在：`HashSet<T>`。
- 先进先出：`Queue<T>`。
- 后进先出：`Stack<T>`。
- 一个键对应多个值：`Lookup<TKey,TElement>` 或字典套列表。
- 多线程并发更新：考虑并发集合，但先定义原子操作。

### 复杂度与真实代价

| 操作 | List | Dictionary/HashSet | 备注 |
|---|---:|---:|---|
| 按索引读取 | O(1) | 不适用 | List 连续内存，缓存友好 |
| 查找值 | O(n) | 平均 O(1) | 哈希退化与常数成本存在 |
| 尾部添加 | 摊销 O(1) | 平均 O(1) | 扩容会复制/重哈希 |
| 中间插删 | O(n) | 不适用 | LinkedList 还需要先找到节点 |

不要看到 O(1) 就默认更快。小集合线性扫描可能因缓存局部性更好而更快；性能结论要测。

## 7. Dictionary 的正确性契约

哈希查找大致经过：

1. 计算 key 的哈希码。
2. 定位桶。
3. 在冲突项中用相等性比较确认。

因此 key 必须满足：

- 相等对象哈希码相同。
- 作为 key 期间，参与相等/哈希的字段不变。
- 自定义比较规则时使用合适的 `IEqualityComparer<T>`。

仓库码大小写不敏感的字典可以显式使用：

```csharp
var byCode = new Dictionary<string, Stock>(StringComparer.OrdinalIgnoreCase);
```

## 8. 集合常见失败

### `ToDictionary` 遇重复键

会抛异常。先定义重复业务语义：取最新、聚合、报错还是保留列表。不能随手 `GroupBy(...).First()` 隐藏数据质量问题。

### 多次枚举

`IEnumerable<T>` 可能每次重新执行昂贵查询或生成逻辑。若需要稳定快照且数据量可控，明确 `ToList()` 一次；若数据量大，改流式处理。

### ConcurrentDictionary 不等于整段线程安全

```csharp
if (!dict.ContainsKey(key))
    dict[key] = Create();
```

这两个操作组合不是原子的。应使用 `GetOrAdd`，并理解 value factory 可能被并发调用多次，只有一个结果获胜；有外部副作用时仍要小心。

## 9. CP6 泛型仓储的批判性阅读

`RepositoryBase<T>` 展示了泛型复用，但也要看出边界：

- `orderBy` 参数当前没有真正使用，实际固定按 `CreateDate` 倒序。
- `UpdateAsync` 把整个实体标记为 Modified，可能造成过度更新和越权字段覆盖。
- 通用 CRUD 不能表达库存变动、财务过账等领域不变量。
- `FindAsync` 的跟踪/缓存语义和普通查询不同。

一个成熟回答不是“泛型仓储减少重复代码”就结束，而是：

> 它适合简单主数据 CRUD；关键聚合写入仍要经过领域服务。通用抽象若泄漏字符串排序、全量更新等行为，应该收紧接口或为具体场景单独建查询/命令服务。

## 10. CP6 证据

- `CP6.Core/BaseProvider/IRepository.cs`
- `CP6.Core/BaseProvider/RepositoryBase.cs`
- `CP6.Core/BaseProvider/ServiceBase.cs`
- `CP6.Core/Services/Wms/IStockMovementService.cs`

## 闭卷验收

- [ ] 抽象类 vs 接口给出选择规则和 CP6 例子。
- [ ] 解释开放泛型注册。
- [ ] 画出协变、逆变的类型方向。
- [ ] 说明相等性与哈希契约。
- [ ] 找出泛型仓储至少三个边界。

