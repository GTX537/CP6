# 05 · 库存这一道铁律

## 🌱 你将学到

- "不变式"是什么意思——不是高级数学，是业务规则
- 看懂 `IStockMovementService` 为什么是库存的"唯一入口"
- 理解为什么 `T_Stock` 旁边还有个 `T_StockTransaction` 表
- 知道并发改库存怎么不超扣

---

## 🍳 生活类比：银行账户

想象一个银行账户。

**情景 A：每个柜员都能直接改余额**
柜员甲：把张三的余额从 1000 改成 1500（这是存 500 元）。
柜员乙：把张三的余额从 1000 改成 800（这是取 200 元）。

俩柜员同时操作 → 谁后改赢谁，张三可能从 1000 变成 1500 或 800（实际应该是 1300）。而且没人知道这两笔交易发生了。

**情景 B：通过统一窗口 + 永久记账**
所有改动必须通过一个特殊的"账户变动窗口"。每次变动：

1. 检查（取款不能超过余额）
2. 改余额
3. 同时记一笔"流水"（永远保留）

CP6 的 `T_Stock` 像账户余额，`T_StockTransaction` 像流水账。`IStockMovementService` 是那个统一窗口。

---

## 🔎 看 CP6 代码

### T_Stock 的字段

```csharp
public class Stock : BaseBizEntity
{
    public string WarehouseCd { get; set; }    // 仓库代码
    public string LocationCd { get; set; }     // 货位代码
    public string ProductCd { get; set; }      // 产品代码
    public string LotNo { get; set; }          // 批次号

    public decimal PhysicalQty { get; set; }   // 物理在库
    public decimal AllocatedQty { get; set; }  // 已被引当（占住但还没出库）
    public decimal AvailableQty { get; set; }  // 可用 = Physical - Allocated

    public string QcStatus { get; set; }       // QC 状态
    public bool RecallFlag { get; set; }       // 召回标记
    public DateTime? ExpiryDate { get; set; }  // 过期日
}
```

业务唯一键是 `(WarehouseCd, LocationCd, ProductCd, LotNo)` 这 4 个组合——同一个仓库的同一个货位的同一个产品的同一个批次，只有一行记录。

### IStockMovementService —— 唯一入口

```csharp
public interface IStockMovementService
{
    Task<StockTransaction> ApplyAsync(
        WmsTxnType type,         // 入库 / 出库 / 引当 / 移动 / ...
        string warehouseCd,
        string locationCd,
        string productCd,
        string lotNo,
        decimal qty,             // 变化量（正=入，负=出）
        string referenceNo,      // 关联单号
        string user);
}
```

所有改库存的操作都要叫这个。比如入库：

```csharp
// 入库 100 个 P-001
await _stockMovement.ApplyAsync(
    WmsTxnType.Inbound, "W01", "W01-A02", "P-001", "L20260601",
    qty: +100, referenceNo: "RCV-0001", user: "alice");
```

或者出库：

```csharp
// 出库 3 个
await _stockMovement.ApplyAsync(
    WmsTxnType.Outbound, "W01", "W01-A02", "P-001", "L20260601",
    qty: -3, referenceNo: "OUT-0001", user: "bob");
```

### 内部实现（伪代码）

```csharp
public async Task<StockTransaction> ApplyAsync(...)
{
    // 1. 查 Stock
    var stock = await _context.Stocks.SingleAsync(s =>
        s.WarehouseCd == warehouseCd
        && s.LocationCd == locationCd
        && s.ProductCd == productCd
        && s.LotNo == lotNo);

    // 2. 改字段
    stock.PhysicalQty += qty;
    stock.AvailableQty = stock.PhysicalQty - stock.AllocatedQty;

    // 3. 校验不变式
    if (stock.AvailableQty < 0)
        throw new InvalidOperationException("库存不足");

    // 4. 同时写 StockTransaction
    var txn = new StockTransaction
    {
        TxnNo = await _sequence.NextAsync("TXN"),
        TxnType = type,
        WarehouseCd = warehouseCd,
        // ... 各字段
        QtyDelta = qty,
        QtyAfter = stock.PhysicalQty,
        ReferenceNo = referenceNo
    };
    _context.StockTransactions.Add(txn);

    // 5. 一次性写 DB（EF Core 自动事务）
    await _context.SaveChangesAsync();

    return txn;
}
```

---

## 🤔 为什么这样

### Q1: 什么叫"不变式"？

"不变式"（invariant）= 不管发生什么事都必须满足的条件。CP6 的库存不变式：

1. `AvailableQty = PhysicalQty - AllocatedQty`（数学等式）
2. `AvailableQty ≥ 0`（不能超扣）
3. 每次 Stock 变动必有 StockTransaction（审计）
4. 召回 / QC 不合格的库存不能出库

谁来保证不变式？**`IStockMovementService` 是唯一守门人**。所有库存变动经它，它检查不变式。

如果允许任何 Service 直接 `_context.Stocks.Update(stock)`，就没人守了。

### Q2: 为什么还要 T_StockTransaction？

T_Stock 只告诉你"现在多少"。T_StockTransaction 告诉你"什么时候、谁、为什么变化"。

举例：经理问"为什么 P-001 少了 50 个？"

- 只有 T_Stock：你只能说"现在是 X，之前不知道"
- 有 T_StockTransaction：你可以查 SQL：

```sql
SELECT TxnDate, TxnType, QtyDelta, ReferenceNo, Creator
FROM T_StockTransaction
WHERE ProductCd = 'P-001' AND TxnDate >= '2026-06-01'
ORDER BY TxnDate;
```

完整流水。这就是审计。

**好处**：

- 任何时刻可以回溯
- 出 bug 时可以重算
- 客户对账容易

### Q3: 并发怎么办？两个工人同时给同一货位扣库存

工人 A 和 B 同时想从某货位扣 6 个，剩余 10 个。如果都成功 → 剩 -2（超扣）。

**CP6 的防护：乐观锁 + Service 内部校验**

`Stock` 继承 `BaseBizEntity`，有 `RowVersion` 字段（第 03 章讲过）。

```
T0  A 查 stock，得到 { Qty=10, RowVersion=V1 }
T1  B 查 stock，得到 { Qty=10, RowVersion=V1 }
T2  A 改 Qty -= 6 → Qty=4，SaveChanges
     EF 发: UPDATE Stock SET Qty=4, RowVersion=V2 WHERE RowVersion=V1
     更新成功（1 行受影响）
T3  B 改 Qty -= 6 → Qty=4，SaveChanges
     EF 发: UPDATE Stock SET Qty=4, RowVersion=V2 WHERE RowVersion=V1
     更新失败（0 行受影响，因为 RowVersion 已是 V2）
     → 抛 DbUpdateConcurrencyException
```

API 层捕获这个异常，返回 409 Conflict，前端提示"数据已变，请刷新重试"。重试时 B 看到正确余额 4，可能选择只扣 4 个或者改其他货位。

### Q4: 那为什么不用 SQL 的 SELECT FOR UPDATE 强加锁

可以。两种方案：

| 方案 | 行为 | 适合 |
|---|---|---|
| 乐观锁 | 冲突时其中一个失败，让前端重试 | 冲突少（CP6 业务） |
| 悲观锁 | 后到的等着 | 冲突多（秒杀、抢座） |

CP6 业务里两个拣货员同时扣同一货位概率低，乐观锁性能好。

---

## ⚠️ 容易搞错的地方

### 1. 绕过 IStockMovementService 直接改 Stock

```csharp
// ❌ 反例
var stock = await _context.Stocks.SingleAsync(...);
stock.PhysicalQty -= 5;
await _context.SaveChangesAsync();
// ↑ 没写 T_StockTransaction，审计断了
// ↑ 没校验 AvailableQty 是否变成负数
```

CP6 团队约定：任何库存变动都过 `IStockMovementService`。这是文化也是 code review 时的红线。

### 2. T_StockTransaction 和 T_Stock 分开 SaveChanges

```csharp
// ❌ 反例
stock.PhysicalQty -= 5;
await _context.SaveChangesAsync();   // 先存 Stock

var txn = new StockTransaction { ... };
_context.StockTransactions.Add(txn);
await _context.SaveChangesAsync();   // 再存 Transaction
// ↑ 两次中间挂了 → 库存改了但没流水 → 审计断链
```

**修复**：一次 SaveChanges 写两个 entity，EF Core 自动包事务，要么都成功要么都失败。

### 3. 物化字段 AvailableQty 算错

```csharp
stock.PhysicalQty += 10;
// ❌ 忘了更新 AvailableQty
await _context.SaveChangesAsync();
// → AvailableQty 和 PhysicalQty / AllocatedQty 不一致
```

CP6 在 `ApplyAsync` 结尾会重算 AvailableQty，并 `Debug.Assert` 校验等式。新人写新代码时容易漏。

### 4. 移动跨仓库忘了用事务

```csharp
// 从 W01-A 移动 5 个到 W01-B
await ApplyAsync(WmsTxnType.MoveOut, "W01", "A", ..., qty: -5, ...);   // 1
await ApplyAsync(WmsTxnType.MoveIn,  "W01", "B", ..., qty: +5, ...);   // 2
// ↑ 1 和 2 之间挂了 → 凭空消失 5 个
```

CP6 的 `MoveAsync` 方法把两步包在一个 `BeginTransactionAsync` 里，原子性。你自己写跨仓库逻辑时也要这样。

---

## ✋ 动手试试

### 任务 1：找到所有改 T_Stock 的代码

在 VS Code / Rider 里搜：

```
_context.Stocks.Update
_context.Stocks.Add
```

理想情况下：只在 `StockMovementService` 内部看到这种代码。其他 Service 里如果有 = 红色警报，要改。

实际看一遍 CP6 是不是真的守住了"唯一入口"。

### 任务 2：写一个测试看不变式被破时报错

打开 `D:\CP6\CP6.Tests\WmsTests\`，找现成的库存测试。然后照样写一个：

```csharp
[Fact]
public async Task ApplyAsync_OutboundExceedsAvailable_ShouldThrow()
{
    using var ctx = TestHelper.CreateInMemoryContext();
    ctx.Stocks.Add(new Stock
    {
        Id = Guid.NewGuid(),
        WarehouseCd = "W01", LocationCd = "A02",
        ProductCd = "P-001", LotNo = "L1",
        PhysicalQty = 10, AllocatedQty = 0, AvailableQty = 10
    });
    await ctx.SaveChangesAsync();

    var svc = /* 实例化 StockMovementService */;

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        svc.ApplyAsync(WmsTxnType.Outbound, "W01", "A02", "P-001", "L1",
                       qty: -15, referenceNo: "OUT-001", user: "test"));
}
```

跑通这个测试，让你亲眼看到"超扣会被挡住"。第 11 章详细讲测试。

### 任务 3：画一张图

画两张表（T_Stock 和 T_StockTransaction）+ 一个 IStockMovementService 中间人。在外面画几个不同的 Service（OrderService、InboundService、OutboundService），用箭头表示它们都通过 IStockMovementService 改库存。

这张图在你脑子里固定下来，就理解"领域不变式 + 唯一入口"了。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/05-stock-invariant.md`](../learning/05-stock-invariant.md)——讲 Event Sourcing 和并发深入
- 关键词搜索："Domain Invariant"、"Aggregate Root DDD"
- 项目内：`CP6.Core/Services/Wms/StockMovementService.cs`、`docs/MSBBWM_Requirements.txt` 库存章节
