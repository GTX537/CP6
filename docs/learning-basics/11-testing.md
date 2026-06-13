# 11 · 测试

## 🌱 你将学到

- "单元测试 / 集成测试 / E2E 测试"分别在测什么
- 看懂一段 xUnit 测试代码
- 理解 Mock 是什么、什么时候用
- 知道 EF Core InMemory 数据库的能力边界

---

## 🍳 生活类比：装修验房

你买了新房，验房：

- **检查单个开关**（每个开关都按一下）= 单元测试
- **检查几个开关串起来**（卧室开关控制走廊灯）= 集成测试
- **走一遍日常使用**（早晨起床到出门关门）= E2E 测试

理想的验房比例：90% 单个开关 + 8% 串联 + 2% 走一遍。
反过来（90% 走一遍）就太慢、出问题不知道是哪一步坏的。

测试金字塔的本质是这个比例。

---

## 🔎 看 CP6 代码

### CP6.Tests 项目结构

```
CP6.Tests/
├── TestHelper.cs              # 共享工厂
├── CacheServiceTests.cs       # 缓存的单元测试
├── OperLogFilterTests.cs      # Filter 的行为测试
├── WmsTests/                  # WMS 各 Service
│   ├── StockMovementServiceTests.cs
│   ├── OutboundServiceTests.cs
│   └── ...
├── MesTests/
├── ErpTests/
└── ClosedLoopTests/           # 跨模块端到端
    └── WmsErpClosedLoopTests.cs
```

### TestHelper.cs

```csharp
public static class TestHelper
{
    public static CP6Context CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())   // 每用例独立 DB
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }
}
```

每个测试用例叫一次 `CreateInMemoryContext()`，得到一个全新的"假数据库"（在内存里），互不干扰。

### 一个典型测试（AAA 模式）

```csharp
[Fact]
public async Task ApplyAsync_OutboundExceedsAvailable_ShouldThrow()
{
    // ===== Arrange（准备）=====
    using var ctx = TestHelper.CreateInMemoryContext();
    ctx.Stocks.Add(new Stock
    {
        Id = Guid.NewGuid(),
        WarehouseCd = "W01", LocationCd = "A02",
        ProductCd = "P-001", LotNo = "L1",
        PhysicalQty = 10, AllocatedQty = 0, AvailableQty = 10
    });
    await ctx.SaveChangesAsync();

    var seq = new Mock<IWmsSequenceService>();
    seq.Setup(s => s.NextAsync(It.IsAny<string>())).ReturnsAsync("TXN-001");

    var svc = new StockMovementService(ctx, seq.Object);

    // ===== Act + Assert（动作 + 断言）=====
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        svc.ApplyAsync(WmsTxnType.Outbound, "W01", "A02", "P-001", "L1",
                       qty: -15, referenceNo: "OUT-001", user: "tester"));
    Assert.Contains("库存不足", ex.Message);

    // 验证副作用：T_Stock 没被改
    var stock = await ctx.Stocks.SingleAsync();
    Assert.Equal(10, stock.PhysicalQty);
    Assert.Empty(ctx.StockTransactions);
}
```

AAA = Arrange / Act / Assert（准备 / 动作 / 断言）。这是测试的标准模板。

### 用 Moq 创建假依赖

```csharp
var seq = new Mock<IWmsSequenceService>();
seq.Setup(s => s.NextAsync(It.IsAny<string>())).ReturnsAsync("TXN-001");

// seq.Object 是个假对象，实现了 IWmsSequenceService 接口
// 它的 NextAsync 不管传啥参数都返回 "TXN-001"
var svc = new StockMovementService(ctx, seq.Object);
```

Mock 让你不需要真的有 `WmsSequenceService` 实现就能测 `StockMovementService`。

---

## 🤔 为什么这样

### Q1: 为什么不用真数据库测

用真数据库的问题：

- 慢（每个测试启动一个 SQL Server，几秒）
- 不隔离（测试间数据互相干扰）
- CI 配复杂（CI 服务器要装 SQL Server）

InMemory provider 是 EF Core 提供的"假数据库"：

- 快（内存里，没有 IO）
- 隔离（每用例独立 dbName）
- 简单（不装任何东西）

但 InMemory 有局限（见下文 Q4）。

### Q2: 什么时候 Mock，什么时候不 Mock

| 依赖 | Mock 还是真实 | 原因 |
|---|---|---|
| CP6Context | 真实（InMemory）| 模拟 DbContext 太麻烦 |
| IRepository | 不 Mock，让它走真实 | 本质是 DbContext 包装 |
| IDeadLetterNotifier | Mock | 不想真发 SignalR |
| IHubContext | Mock | 同上 |
| ILogger | `NullLogger.Instance` | 测试不关心日志 |
| IWmsSequenceService | 看场景 | 测库存逻辑时 Mock；测采番本身时用真实 |

**核心原则**：Mock 基础设施依赖（IO、随机、时间），真实跑领域逻辑。

### Q3: Mock 过度的反模式

```csharp
// ❌ 反例
var mock = new Mock<IOrderService>();
mock.Setup(s => s.CreateAsync(It.IsAny<OrderCreateDto>(), It.IsAny<string>()))
    .ReturnsAsync(new Order { Id = ..., WebOrderNo = "ORD-001" });

// 然后测 mock.Object.CreateAsync(...)
// → 等于"我说返回啥就返回啥"，测了个寂寞
```

测 OrderService 应该用**真实的** OrderService，Mock 它的依赖（IBridgeHook、IDocNumber）。

### Q4: InMemory 不能做什么

EF Core InMemory provider 不支持：

- **事务回滚**（`BeginTransactionAsync` 不报错但 Commit/Rollback 实际无效）
- **唯一索引校验**（部分场景）
- **RowVersion 乐观锁**
- **DB 触发器、计算列、CHECK 约束**
- **原始 SQL（FromSqlRaw）**

所以 CP6 的 TestHelper 加了 `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`，吞掉"事务无效"的警告。

严肃测试上面这些场景要用**真 SQL Server LocalDb** 或**容器化 SQL Server**。CP6 当前没做。

---

## ⚠️ 容易搞错的地方

### 1. 测试间共享 dbName

```csharp
// ❌ 反例
var ctx = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
    .UseInMemoryDatabase("test")     // 所有测试都叫 "test" → 共享数据
    .Options);
```

CP6 用 `Guid.NewGuid().ToString()` 让每个测试 dbName 独立。

### 2. 异步死锁

```csharp
// ❌ 反例
[Fact]
public void TestSomething()
{
    var result = MyAsync().Result;   // 同步等 async → 死锁风险
}

// ✅ async Task
[Fact]
public async Task TestSomething()
{
    var result = await MyAsync();
}
```

### 3. 没 await SaveChangesAsync

```csharp
ctx.Stocks.Add(stock);
// 忘了 await ctx.SaveChangesAsync();
// → 下面查的时候没数据
```

### 4. 测试覆盖率 95% 但生产仍出 bug

可能原因：

- 测试只看"代码执行过"，不看"行为正确"
- 断言太弱（只验"不报错"没验返回值）
- 缺集成测试（模块拼起来才出问题）
- 缺边界 case（时间、并发、异常）

覆盖率是必要条件不是充分条件。

### 5. 用 Mock 然后测 Mock

第三点 Q3 说过。Mock 应该用在依赖上，不是测试对象本身。

---

## ✋ 动手试试

### 任务 1：跑现有测试

命令行：

```bash
cd D:\CP6
dotnet test
```

看输出几个测试通过、几个失败。CP6 文档说有 282 个测试，全绿。

### 任务 2：故意改一个 Service 让测试失败

打开 `D:\CP6\CP6.Core\Services\Wms\StockMovementService.cs`，找校验"库存不足"的那一段，把它改成不抛异常：

```csharp
// 注释掉
// if (stock.AvailableQty < 0)
//     throw new InvalidOperationException("库存不足");
```

再跑 `dotnet test`，应该有测试失败。

看失败的测试名，理解"这个测试就是用来防止你刚才那种破坏的"。

**实验完恢复代码**。

### 任务 3：照葫芦写一个新测试

打开 `CP6.Tests/WmsTests/` 找一个简单测试，模仿写一个新的。

比如测"入库 +50 后库存变 50"：

```csharp
[Fact]
public async Task ApplyAsync_Inbound_ShouldIncreasePhysicalQty()
{
    using var ctx = TestHelper.CreateInMemoryContext();
    ctx.Stocks.Add(new Stock
    {
        Id = Guid.NewGuid(),
        WarehouseCd = "W01", LocationCd = "A02",
        ProductCd = "P-001", LotNo = "L1",
        PhysicalQty = 0, AllocatedQty = 0, AvailableQty = 0
    });
    await ctx.SaveChangesAsync();

    var seq = new Mock<IWmsSequenceService>();
    seq.Setup(s => s.NextAsync(It.IsAny<string>())).ReturnsAsync("TXN-001");

    var svc = new StockMovementService(ctx, seq.Object);

    await svc.ApplyAsync(WmsTxnType.Inbound, "W01", "A02", "P-001", "L1",
                        qty: 50, referenceNo: "RCV-001", user: "tester");

    var stock = await ctx.Stocks.SingleAsync();
    Assert.Equal(50, stock.PhysicalQty);
    Assert.Equal(50, stock.AvailableQty);

    var txn = await ctx.StockTransactions.SingleAsync();
    Assert.Equal(50, txn.QtyDelta);
}
```

跑 `dotnet test`，确认通过。

### 任务 4：用 Mock 测一个有依赖的 Service

挑一个 Service（比如 `OrderService`），看它的构造函数注入了什么。把每个依赖：

- 是 DbContext → 用 TestHelper.CreateInMemoryContext
- 是 IBridgeHook → 用 Moq
- 是 ILogger → NullLogger.Instance

然后写一个测试覆盖它的"正常路径"。这是最常见的测试套路。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/11-testing.md`](../learning/11-testing.md)——讲时间 mock、覆盖率假象
- xUnit 官方：[文档](https://xunit.net/)
- Moq：[Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)
- 关键词搜索："AAA pattern 测试"、"EF Core InMemory 测试"
