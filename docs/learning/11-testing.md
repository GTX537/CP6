# 11 · 测试金字塔：xUnit + Moq + InMemory

## 📍 学习目标

1. 测试金字塔（单元 / 集成 / E2E）各自的成本和价值
2. CP6 的 192 → 282 个测试是怎么组织的？
3. EF Core InMemory provider 的能力边界（事务、并发、SQL 不支持）
4. 什么时候用 Moq，什么时候不 Mock 直接用真实依赖？
5. AAA 模式（Arrange-Act-Assert）的真实代码示例
6. CI 跑测试的最佳实践

---

## 🔎 真实代码切片

### 测试项目结构

```
CP6.Tests/
├── TestHelper.cs              # 共享工厂：InMemory DbContext / Mock 工厂
├── CacheServiceTests.cs       # 缓存的单元测试
├── OperLogFilterTests.cs      # Filter 的行为测试
├── BridgeHookTests.cs         # Bridge Hook 测试
├── WmsTests/
│   ├── StockMovementServiceTests.cs
│   ├── OutboundServiceTests.cs
│   ├── InboundServiceTests.cs
│   └── StockTakeServiceTests.cs
├── MesTests/
│   ├── WorkOrderServiceTests.cs
│   └── ProductionResultServiceTests.cs
├── ErpTests/
│   └── OrderServiceTests.cs
└── ClosedLoopTests/
    └── WmsErpClosedLoopTests.cs   # 跨模块端到端
```

### `TestHelper.cs` — 共享工厂

```csharp
public static class TestHelper
{
    public static CP6Context CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())   // 每个测试独立 DB
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))  // 关键
            .Options;
        return new CP6Context(options);
    }

    public static CacheService CreateCache()
    {
        var memCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        return new CacheService(memCache);
    }

    public static Mock<IConfiguration> MockConfig(Dictionary<string, string?> values)
    {
        var mock = new Mock<IConfiguration>();
        var section = new Mock<IConfigurationSection>();
        foreach (var kv in values)
            section.Setup(s => s[kv.Key]).Returns(kv.Value);
        mock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(section.Object);
        return mock;
    }
}
```

### AAA 模式经典示例

```csharp
[Fact]
public async Task ApplyAsync_OutboundExceedsAvailable_ShouldThrow()
{
    // ===== Arrange =====
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
    seq.Setup(s => s.NextAsync(It.IsAny<string>()))
       .ReturnsAsync("TXN-001");

    var svc = new StockMovementService(ctx, seq.Object);

    // ===== Act + Assert =====
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        svc.ApplyAsync(WmsTxnType.Outbound, "W01", "A02", "P-001", "L1",
                       qty: -15, referenceNo: "OUT-001", user: "tester"));
    Assert.Contains("库存不足", ex.Message);

    // 验证副作用：T_Stock 没被改
    var stock = await ctx.Stocks.SingleAsync();
    Assert.Equal(10, stock.PhysicalQty);
    Assert.Equal(0, stock.AllocatedQty);
    Assert.Empty(ctx.StockTransactions);   // 也没写 transaction
}
```

### Bridge Hook 测试（典型集成测试）

```csharp
[Fact]
public async Task MesBridgeHook_OnOrderCreated_ShouldExpandWorkOrder()
{
    // Arrange
    using var ctx = TestHelper.CreateInMemoryContext();
    ctx.Orders.Add(new Order
    {
        Id = Guid.NewGuid(),
        WebOrderNo = "ORD-001",
        // ...
    });
    await ctx.SaveChangesAsync();

    var seq = Mock.Of<IMesSequenceService>(s => s.NextAsync("WO") == Task.FromResult("WO-001"));
    var wmsHook = Mock.Of<IWmsBridgeHook>();  // 不关心，给个 NoOp Mock

    var workOrderSvc = new WorkOrderService(ctx, seq, wmsHook, Mock.Of<ILogger<WorkOrderService>>());
    var deadLetter = Mock.Of<IDeadLetterNotifier>();
    var hook = new MesBridgeHook(ctx, workOrderSvc, deadLetter);

    // Act
    await hook.OnOrderCreatedAsync("ORD-001", "tester");

    // Assert
    var wo = await ctx.WorkOrders.SingleAsync(w => w.WebOrderNo == "ORD-001");
    Assert.Equal("WO-001", wo.WorkOrderNo);

    var evt = await ctx.IntegrationEvents.SingleAsync();
    Assert.Equal(IntegrationEventStatus.Success, evt.Status);
    Assert.Equal("WO-001", evt.TargetNo);
}
```

### Filter 测试

```csharp
[Fact]
public async Task OperLogFilter_WhenTransportDown_ShouldFallbackToDb()
{
    // Arrange
    using var ctx = TestHelper.CreateInMemoryContext();
    var transport = new Mock<IOperLogTransport>();
    transport.SetupGet(t => t.IsConnected).Returns(false);  // Kafka 不可用
    var config = TestHelper.MockConfig(new() { ["IncludeGet"] = "false" });

    var filter = new OperLogFilter(ctx, transport.Object, config.Object);
    var actionContext = CreateActionContext(method: "POST", path: "/api/order/create");
    var executed = false;
    ActionExecutionDelegate next = () =>
    {
        executed = true;
        return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
        {
            Result = new OkObjectResult(new { code = 200 })
        });
    };

    // Act
    await filter.OnActionExecutionAsync((ActionExecutingContext)actionContext, next);

    // Assert
    Assert.True(executed);
    var log = await ctx.Sys_OperLogs.SingleAsync();
    Assert.Equal("POST", log.HttpMethod);
    Assert.Equal("/api/order/create", log.RequestUrl);
    transport.Verify(t => t.PublishAsync(It.IsAny<Sys_OperLog>()), Times.Never);  // 没投递
}
```

---

## 💡 资深视角

### 测试金字塔的成本曲线

```
        E2E (浏览器跑全流程)
       /         \           ← 慢、脆、贵；少量但关键路径
      /           \
     Integration  Tests       ← 中速；Service + DbContext 真实交互
    /                \
   /                  \
  Unit Tests Lots      ← 快、稳；纯函数 + Mock
```

数量比例理想 = 70% Unit / 20% Integration / 10% E2E。CP6 当前以 Integration 为主（StockMovementServiceTests 用真 InMemory DbContext 算 Integration）。

**为什么不要倒过来（大量 E2E）**：

- E2E 跑一次 30s+，1000 个就要 8 小时
- 任何 UI / API 改动都可能让 E2E fail，维护成本高
- 失败时定位难（不知道哪一层挂的）

### InMemory provider 的边界

```csharp
.UseInMemoryDatabase(dbName)
```

**能做的**：

- 基本 CRUD
- LINQ 查询（绝大部分）
- 关联（Include / 反向 navigation）
- ChangeTracker 行为

**不能做的**：

- **事务回滚**（看起来能 BeginTransaction，但 Commit / Rollback 实际无效）
- **唯一索引校验**（部分场景）
- **行版本（RowVersion）**
- **DB 触发器、计算列、CHECK 约束**
- **原始 SQL（FromSqlRaw）**
- **存储过程**

**所以 CP6 加了**：

```csharp
.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
```

否则 Service 里 `BeginTransactionAsync` 会抛 warning（升级为 exception）。

**严肃测试建议**：用 SQL Server LocalDb 或容器化 SQL Server。CP6 选 InMemory 是为了 CI 速度（每用例 DB 独立）。如果业务里有大量 RowVersion / 触发器逻辑，需要补一组 Integration test 跑真 SQL Server。

### 什么时候 Mock vs 真实

| 依赖 | Mock 还是真实？ | 原因 |
|---|---|---|
| `CP6Context` | 真实（InMemory） | 模拟 DbContext 太麻烦，InMemory 够用 |
| `IRepository<T>` | 不用 Mock，直接走真实 | 它本质是 DbContext 的薄包装 |
| `IDeadLetterNotifier` | Mock | 不想真发 SignalR / 写日志 |
| `IHubContext<T>` | Mock | 同上 |
| `ILogger<T>` | `Mock.Of<ILogger<T>>()` 或 `NullLogger<T>.Instance` | 测试不关心日志 |
| `IConfiguration` | Mock（用 TestHelper） | 准备测试配置 |
| 外部 HTTP 服务 | Mock (`HttpMessageHandler`) | 不能依赖外网 |
| `IWmsSequenceService` | 看场景 | 测库存逻辑时 Mock；测采番本身时用真实 |

**核心原则**：Mock **基础设施依赖**（IO、随机性、时间），真实**领域逻辑**。

### 时间相关测试

```csharp
// ❌ 反例
[Fact]
public async Task ShouldExpireAfter6Hours()
{
    var token = JwtHelper.Generate(...);
    Thread.Sleep(TimeSpan.FromHours(6));   // 不可能
}

// ✅ 正确做法：抽时间提供器
public interface IDateTimeProvider { DateTime UtcNow { get; } }
public class FixedDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

// 测试里
var time = new FixedDateTimeProvider { UtcNow = new DateTime(2026,06,09,10,0,0) };
var jwt = new JwtHelper(time);
var token = jwt.Generate(...);

time.UtcNow = time.UtcNow.AddHours(7);   // 推进时间
Assert.False(jwt.Validate(token));
```

CP6 当前用 `DateTime.Now`（散在各 Service），不易测试。**改进点**：抽个 `IDateTimeProvider`。

### Mock 用法陷阱

```csharp
// ❌ 过度 mock
var mock = new Mock<IOrderService>();
mock.Setup(s => s.CreateAsync(It.IsAny<OrderCreateDto>(), It.IsAny<string>()))
    .ReturnsAsync(new Order { Id = ..., WebOrderNo = "ORD-001" });
// 测试就等于"我说返回啥就返回啥"，没测真实业务

// ✅ 用真实 OrderService + InMemory DbContext + Mock 它的外部依赖
var svc = new OrderService(ctx, mockBridgeHook.Object, ...);
var result = await svc.CreateAsync(dto, "tester");
Assert.Equal("ORD-001", result.WebOrderNo);   // 验真实采番逻辑
```

**经验**：在 Service 测试里 Mock Service 自己 = 自欺欺人。Mock 应该用在 Service 的依赖上。

### CI 跑测试

```yaml
# .github/workflows/ci.yml
- name: Test
  run: dotnet test --logger "trx" --collect:"XPlat Code Coverage"

- name: Upload coverage
  uses: codecov/codecov-action@v3
```

**良好 CI 实践**：

- 每个 PR 跑全部测试
- 失败必须修才能 merge
- 跟踪覆盖率不能跌（如 80% 阈值）
- 慢测试 (>1s) 标记为 `[Trait("Category","Slow")]`，可单独跳过

CP6 当前没看到 `.github/workflows`，应该是本地 `dotnet test` 跑。生产建议加 CI。

### 测试组织（FluentAssertions 风格 vs xUnit 原生）

```csharp
// xUnit 原生
Assert.Equal(10, stock.PhysicalQty);
Assert.Contains("库存不足", ex.Message);

// FluentAssertions（更可读）
stock.PhysicalQty.Should().Be(10);
ex.Message.Should().Contain("库存不足");
```

CP6 用 xUnit 原生。FluentAssertions 是个加分项但不必须。

---

## ⚠️ 踩坑记录

### 坑 1：InMemory 数据库被复用

```csharp
// ❌ 反例
var ctx1 = new CP6Context(opts);
ctx1.Add(entity);
await ctx1.SaveChangesAsync();

var ctx2 = new CP6Context(opts);   // 同一 dbName → 共享数据
var found = await ctx2.Entities.CountAsync();   // = 1
```

InMemory DB 是按 dbName 命名的全局字典。**测试间隔离**靠每个测试用唯一 dbName（`Guid.NewGuid().ToString()`）。CP6 的 TestHelper 默认这样做。

### 坑 2：BeginTransaction 警告 → 异常

```csharp
// EF Core 10 默认会把 InMemory 不支持的操作升级为 throw
public async Task Move() {
    using var tx = await _ctx.Database.BeginTransactionAsync();  // 抛异常
}
```

修复见 `TestHelper.CreateInMemoryContext`：`ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`。但请记住：**这只是吞警告，事务实际没用**。

### 坑 3：异步死锁

```csharp
// ❌ 反例
[Fact]
public void TestSomething()
{
    var result = MyAsyncMethod().Result;   // 同步等待 async
}

// ✅ 用 async Task
[Fact]
public async Task TestSomething()
{
    var result = await MyAsyncMethod();
}
```

xUnit 早期版本同步死锁概率高，新版本 `[Fact]` 配 `async Task` 是标准。

### 坑 4：测试间状态污染（静态字段）

```csharp
public class SomeService {
    private static int _counter = 0;   // 全局状态
}
```

测试并行跑（xUnit 默认并行）时 `_counter` 会错乱。CP6 没明显犯这错，但任何 BackgroundService 或单例都要小心。`[Collection("NonParallel")]` 可以强制某组测试串行。

### 坑 5：Mock 接口的默认行为

```csharp
var mock = new Mock<IWmsBridgeHook>();
// 没 Setup 任何方法
await mock.Object.OnOrderCreatedAsync("...", "...");   // 返回 Task.CompletedTask（默认）
```

如果不 Setup，Mock 接口方法返回 default。对 `Task` 返回 `Task.CompletedTask`，对 `T` 返回 `null`。`Mock.Of<T>()` 是 `new Mock<T>().Object` 的简写，适合"我只想要个空实现"。

---

## 🧪 自检题

1. **覆盖率假象**：单元测试覆盖率 95% 但生产仍频繁出 bug，可能是什么原因？  
   <details><summary>答案</summary>(1) 覆盖了行数但断言弱（只看不抛异常没看返回值）；(2) Mock 过度，测的全是 mock 行为；(3) 缺集成测试，模块拼起来才出问题；(4) 缺 E2E，UI 集成路径没覆盖；(5) 缺时间/并发/异常路径测试。覆盖率只是"代码执行过"，不等于"行为正确"。</details>

2. **测试粒度**：测试 `OrderService.CreateAsync` 应该一个测试覆盖所有分支，还是每个分支一个测试？  
   <details><summary>答案</summary>每个分支一个 <code>[Fact]</code>，或用 <code>[Theory]</code> + <code>[InlineData]</code> 参数化。原则：<b>一个测试一个原因 fail</b>。这样失败时直接告诉你"哪条业务规则坏了"，而不是看到一个大测试要排查 N 小时。</details>

3. **场景重现**：用户反馈"并发改同一订单失败"，怎么写测试重现？  
   <details><summary>答案</summary>InMemory provider 不支持乐观锁，要用真 SQL Server LocalDb。
   <pre><code>var ctx1 = CreateContext();
   var ctx2 = CreateContext();
   var order1 = await ctx1.Orders.FindAsync(id);
   var order2 = await ctx2.Orders.FindAsync(id);
   order1.Status = "A"; await ctx1.SaveChangesAsync();
   order2.Status = "B";
   await Assert.ThrowsAsync&lt;DbUpdateConcurrencyException&gt;(() =&gt; ctx2.SaveChangesAsync());</code></pre></details>

4. **重构**：CP6 当前 Service 测试每个文件 50+ 用例，怎么组织让 reviewer 容易看？  
   <details><summary>答案</summary>(1) 按方法分嵌套 class：<code>public class StockMovementServiceTests { public class ApplyAsync_Tests { [Fact] public Task When... { } } }</code>；(2) 测试方法名遵循 <code>MethodName_State_Expected</code>：<code>ApplyAsync_OutboundExceedsAvailable_Throws</code>；(3) 共享 setup 抽 <code>protected</code> 方法或 <code>IClassFixture</code>。</details>

5. **质疑题**：管理层说"测试写得太慢，让 AI 生成不行吗"，你怎么回答？  
   <details><summary>答案</summary>AI 可以快速生成覆盖率高的样板测试，但<b>价值最高的测试是基于业务边界的</b>，AI 不了解你们业务的不变式。CP6 的库存不变式测试、Bridge Hook 幂等测试、并发冲突测试，这些都需要懂业务的人设计。AI 能帮：(1) 跑通现有 AAA 模板的填空；(2) 补简单断言；(3) 生成 mock setup boilerplate。不能替代：(1) 决定测什么；(2) 设计边界条件；(3) 评审测试是否抓得到真实生产 bug。</details>

---

## 🔗 延伸阅读

- [xUnit 文档](https://xunit.net/)
- [Moq Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)
- [Testing in EF Core](https://learn.microsoft.com/en-us/ef/core/testing/)
- [Working Effectively with Legacy Code (Michael Feathers)](https://www.amazon.com/Working-Effectively-Legacy-Michael-Feathers/dp/0131177052) — 给老代码加测试的圣经
- 项目内：`CP6.Tests/` 全部文件，特别是 `TestHelper.cs` 和 `WmsTests/`
