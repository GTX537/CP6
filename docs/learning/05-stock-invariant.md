# 05 · 领域不变式：库存这一道铁律

## 📍 学习目标

1. 什么是"领域不变式"（Domain Invariant）？为什么把它放在代码里比 DB 约束更灵活？
2. `T_Stock` 严禁直接 UPDATE 是什么意思？谁来守这条规则？
3. `IStockMovementService.ApplyAsync` 为什么是库存唯一写入入口？
4. `T_StockTransaction` 不变事件流（append-only log）和 Event Sourcing 是什么关系？
5. 高并发下两个工人同时给同一货位扣库存，怎么保证不超扣？

---

## 🔎 真实代码切片

### `T_Stock` 的关键字段

```csharp
// CP6.Entity/DomainModels/Wms/Stock.cs (示意)
public class Stock : BaseBizEntity
{
    // 业务唯一键 (WarehouseCd, LocationCd, ProductCd, LotNo)
    public string WarehouseCd { get; set; }
    public string LocationCd { get; set; }
    public string ProductCd { get; set; }
    public string LotNo { get; set; }

    // 三个数量字段
    public decimal PhysicalQty { get; set; }    // 物理在库
    public decimal AllocatedQty { get; set; }   // 已引当
    public decimal AvailableQty { get; set; }   // = Physical - Allocated（物化在 DB）

    // 特殊标志
    public DateTime? ExpiryDate { get; set; }   // FEFO 引当用
    public bool RecallFlag { get; set; }        // 召回禁出
    public string QcStatus { get; set; }        // PENDING/PASSED/FAILED/HOLD
    public string? OwnerType { get; set; }      // VMI 客户库存
    public string? OwnerCd { get; set; }
}
```

### 库存变动**唯一入口** — `IStockMovementService.ApplyAsync`

```csharp
// CP6.Core/Services/Wms/IStockMovementService.cs
public interface IStockMovementService
{
    Task<StockTransaction> ApplyAsync(
        WmsTxnType type,         // 入库/出库/移动/引当/解引当/调整
        string warehouseCd,
        string locationCd,
        string productCd,
        string lotNo,
        decimal qty,
        string referenceNo,       // 关联单号（OutboundNo / InboundNo / StockTakeNo）
        string user);

    Task<(StockTransaction From, StockTransaction To)> MoveAsync(
        string fromWarehouseCd, string fromLocationCd,
        string toWarehouseCd,   string toLocationCd,
        string productCd, string lotNo, decimal qty,
        string referenceNo, string user);
}
```

### `T_StockTransaction` 的不变事件流

```csharp
public class StockTransaction : BaseBizEntity
{
    public string TxnNo { get; set; }              // 业务编号，IWmsSequenceService 采番
    public WmsTxnType TxnType { get; set; }        // 入库/出库/移动/...
    public string WarehouseCd { get; set; }
    public string LocationCd { get; set; }
    public string ProductCd { get; set; }
    public string LotNo { get; set; }
    public decimal QtyDelta { get; set; }          // 变化量（正=入，负=出）
    public decimal QtyAfter { get; set; }          // 变化后的 PhysicalQty 快照
    public string ReferenceNo { get; set; }        // 关联单号
    public DateTime TxnDate { get; set; }
    // 注意：这张表只追加不更新，对应 entity 没有 UpdateAsync 方法
}
```

---

## 💡 资深视角

### 什么是领域不变式（Domain Invariant）

**不变式**是不管何时何地、何种交互序列，业务都必须满足的恒等条件。CP6 的库存不变式：

| # | 不变式 | 谁负责守 |
|---|---|---|
| I1 | `T_Stock.AvailableQty = PhysicalQty - AllocatedQty` | `StockMovementService` |
| I2 | 每条 `T_Stock` 行的 `AvailableQty ≥ 0`（不能超扣） | `StockMovementService.ApplyAsync` 内部校验 |
| I3 | 每次 `T_Stock` 变动必产生一行 `T_StockTransaction`（不变日志） | `StockMovementService.ApplyAsync` 同一事务内一起写 |
| I4 | `T_Stock` 业务唯一键不可重复 | EF Core `OnModelCreating` 的 `HasIndex().IsUnique()` |
| I5 | `RecallFlag = true` 或 `QcStatus = FAILED` 的 Stock 不可被出库引当 | `OutboundService.AllocateAsync` 候选过滤 |

**为什么把规则放在 Service 而不只放在 DB**：

- ✅ 可以业务化报错（"零件 P-001 在 W01-A02 库存不足，当前 5，需求 10"）
- ✅ 可以做组合校验（要查 RecallFlag + ExpiryDate + QcStatus 三者）
- ✅ 可以触发副作用（写 transaction、发 SignalR、记 oplog）
- ❌ 只靠 DB CHECK 约束的话只能报"违反约束"，对用户无意义

但**关键的兜底必须靠 DB**：业务唯一键 + 外键 + non-null。CP6 在 `OnModelCreating` 里都加了。

### 唯一写入入口（Single Entry Point）的威力

如果允许任何 Service 直接 `_context.Stocks.Find(...).PhysicalQty += 10; SaveChanges()`：

- 谁都可能改库存 → 没有审计
- 没有事务日志 → 查不到"啥时候、谁、为啥变成这数"
- 没有校验 → 可能写出负数
- 没法在变动时触发 SignalR / Bridge Hook

CP6 强制所有库存变动经 `IStockMovementService`：

```csharp
// 入库
await _stockMovement.ApplyAsync(WmsTxnType.Inbound, "W01", "W01-A02", "P-001", "L20260601", +10, "RCV-0001", user);

// 出库
await _stockMovement.ApplyAsync(WmsTxnType.Outbound, "W01", "W01-A02", "P-001", "L20260601", -3, "OUT-0001", user);

// 引当（占住但未实物出库）
await _stockMovement.ApplyAsync(WmsTxnType.Allocate, "W01", "W01-A02", "P-001", "L20260601", +5 /*引当量增加*/, "PICK-0001", user);

// 移动
await _stockMovement.MoveAsync("W01", "A02", "W01", "B01", "P-001", "L20260601", 2, "MOVE-0001", user);
```

每次都在**同一个事务**里：

1. UPDATE `T_Stock` 的对应行（PhysicalQty + AllocatedQty + AvailableQty）
2. INSERT 一行 `T_StockTransaction`
3. 校验 AvailableQty ≥ 0，否则 throw

### 不变事件流 ≈ Event Sourcing 雏形

`T_StockTransaction` 是 **append-only 事件日志**：

| 时间 | TxnType | Qty | After |
|---|---|---|---|
| 06-01 09:00 | Inbound | +100 | 100 |
| 06-01 10:30 | Outbound | -10 | 90 |
| 06-01 11:00 | Allocate | +5（引当）| 90 |
| 06-01 14:00 | Outbound | -5 | 85（实物出，同时引当 -5）|

随时可以从这张表**重放**出任何一刻的 `T_Stock` 状态：

```sql
SELECT 
    WarehouseCd, LocationCd, ProductCd, LotNo,
    SUM(CASE WHEN TxnType IN ('Inbound','Outbound','Adjust') THEN QtyDelta ELSE 0 END) AS PhysicalQty,
    SUM(CASE WHEN TxnType IN ('Allocate','Deallocate') THEN QtyDelta ELSE 0 END) AS AllocatedQty
FROM T_StockTransaction
WHERE TxnDate <= @asOfTime
GROUP BY WarehouseCd, LocationCd, ProductCd, LotNo;
```

这就是 **Event Sourcing 的雏形**：

- `T_Stock` 是**物化视图（projection）**
- `T_StockTransaction` 是**事件流（event log）**
- 两者通过同一事务保持一致

真正的 ES 还会有 snapshot、re-projection 等，CP6 简化了。但已经能实现：

- **追溯**：任意时刻任意货位的库存状态
- **审计**：每次变动谁、为啥、关联什么单
- **回放**：如果某次 UPDATE 出 bug，可以从事件流重算 T_Stock

### 高并发下的超扣防护

两个工人同时给 (W01, A02, P-001, L20260601) 扣 6，剩余只有 10 怎么办？

**方案 A：乐观锁** — `T_Stock.RowVersion`

```csharp
public async Task<StockTransaction> ApplyAsync(...)
{
    var stock = await _context.Stocks.SingleAsync(predicate);  // 带 RowVersion
    stock.PhysicalQty += qty;
    if (stock.AvailableQty < 0) throw new InsufficientStockException();
    await _context.SaveChangesAsync();  // EF 自动加 RowVersion WHERE
}
```

第二个工人 SaveChanges 时 RowVersion 已变 → 抛 `DbUpdateConcurrencyException` → API 返回 409 → 前端提示重试。

**方案 B：悲观锁** — `SELECT ... WITH (UPDLOCK, ROWLOCK)`

```csharp
var stock = await _context.Stocks
    .FromSqlInterpolated($@"
        SELECT * FROM T_Stock WITH (UPDLOCK, ROWLOCK)
        WHERE WarehouseCd = {whCd} AND LocationCd = {locCd} 
          AND ProductCd = {prodCd} AND LotNo = {lotNo}")
    .SingleAsync();
```

第二个工人会**阻塞**到第一个事务提交，然后看到最新值再决定。

**CP6 用方案 A**（乐观锁），原因：

- 出货超扣概率本身不高（拣货员通常不并发同一货位）
- 乐观锁无阻塞，吞吐高
- 冲突时让前端重试一次几乎不影响用户体验

**对比方案 B**：适合高频且必并发场景（如秒杀），但 CP6 的业务不是这种。

### 为什么 `T_Stock` 业务唯一键有 4 列

```
(WarehouseCd, LocationCd, ProductCd, LotNo)
```

实务里 1 个零件可能在 N 个货位（散在不同库位）、每个货位可能有 M 个批次（先入先出要分批）。所以：

- 不能用 (WarehouseCd, ProductCd) 当唯一键 —— 货位丢失了
- 不能用 (WarehouseCd, LocationCd, ProductCd) —— 批次丢失了

CP6 用这 4 列 + EF Core `HasIndex().IsUnique()` 兜底。

### `OutboundService.AllocateAsync` 的候选过滤

引当时不能用所有库存，必须排除：

```csharp
var candidates = await _context.Stocks
    .Where(s => s.ProductCd == productCd)
    .Where(s => !s.RecallFlag)                      // 召回禁出
    .Where(s => s.QcStatus != "FAILED")             // QC 不合格禁出
    .Where(s => s.QcStatus != "HOLD")               // QC 待复检禁出
    .Where(s => s.ExpiryDate == null || s.ExpiryDate > DateTime.Now)  // 过期禁出
    .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)  // FEFO 优先
    .ThenBy(s => s.CreateDate)                        // 同期则 FIFO
    .AsNoTracking()
    .ToListAsync();
```

这就是 **FEFO（First-Expired-First-Out）** + 候选过滤。背后是不变式 I5。

---

## ⚠️ 踩坑记录

### 坑 1：忘记把 `T_StockTransaction` 写在同一事务里

```csharp
// ❌ 反例
public async Task<StockTransaction> ApplyAsync(...)
{
    stock.PhysicalQty += qty;
    await _context.SaveChangesAsync();   // 先存 Stock

    var txn = new StockTransaction { ... };
    _context.StockTransactions.Add(txn);
    await _context.SaveChangesAsync();   // 再存 Transaction
    // 如果两次中间挂了 → T_Stock 改了但 T_StockTransaction 没记 → 审计断链
}
```

**修复**：用一次 SaveChangesAsync 写两个 entity（EF 自动事务），或显式 `BeginTransactionAsync`：

```csharp
using var tx = await _context.Database.BeginTransactionAsync();
stock.PhysicalQty += qty;
_context.StockTransactions.Add(txn);
await _context.SaveChangesAsync();
await tx.CommitAsync();
```

### 坑 2：`AvailableQty` 物化字段不一致

`AvailableQty = PhysicalQty - AllocatedQty` 这个公式如果不在 Service 里强制维护，就会出现"DB 里 AvailableQty=10，但 PhysicalQty=20、AllocatedQty=15"这种数学错误。

CP6 的处理：所有 ApplyAsync 在结尾算一遍 AvailableQty 写回，并加 `Debug.Assert(stock.AvailableQty == stock.PhysicalQty - stock.AllocatedQty)`。生产环境再加 DB 触发器或 SCHEMA 约束兜底。

### 坑 3：批量入库时一行行 ApplyAsync 性能差

```csharp
// ❌ 1000 行一次循环 SaveChanges，1000 次 SQL roundtrip
foreach (var detail in receipt.Details)
    await _stockMovement.ApplyAsync(...);
```

**修复**：在 Service 里提供批量版本 `ApplyBatchAsync`，一次 SaveChanges 同时写 1000 个 T_Stock UPDATE + 1000 个 T_StockTransaction INSERT。EF Core 8+ 支持 `ExecuteUpdate` 和 batch insert，能进一步加速。

### 坑 4：`MoveAsync` 跨仓库时事务边界

```csharp
public async Task MoveAsync(...)
{
    using var tx = await _context.Database.BeginTransactionAsync();
    await ApplyAsync(WmsTxnType.MoveOut, fromWh, fromLoc, prod, lot, -qty, refNo, user);
    await ApplyAsync(WmsTxnType.MoveIn,  toWh,   toLoc,   prod, lot, +qty, refNo, user);
    await tx.CommitAsync();
}
```

两步必须在同一事务，否则可能"扣了 from 没加 to" → 库存凭空消失。CP6 实际实现就是这样。

---

## 🧪 自检题

1. **不变式校验**：DB 里发现一条 `T_Stock` 的 `AvailableQty=-3`，怎么排查？  
   <details><summary>答案</summary>(1) 查 <code>T_StockTransaction</code> 这条 Stock 的全部记录，重放看哪一行算错；(2) 大概率是有人绕过 <code>StockMovementService</code> 直接 UPDATE T_Stock；(3) 短期：写脚本修复这条；(4) 长期：加 DB CHECK 约束 <code>AvailableQty &gt;= 0</code>，再加触发器在 T_Stock UPDATE 时强制对应 T_StockTransaction 存在。</details>

2. **方案对比**：让你重新设计 T_Stock，你会保留 PhysicalQty / AllocatedQty / AvailableQty 三个字段，还是只存 PhysicalQty 和 AllocatedQty，AvailableQty 用计算列？  
   <details><summary>答案</summary>两种都合理。物化列（CP6 的做法）查询快、Allocate 时不用算；计算列（SQL Server <code>AS (PhysicalQty - AllocatedQty) PERSISTED</code>）省一次写、强一致。CP6 选物化是因为还要校验"AvailableQty 不为负"在 Service 层做，计算列只能 DB 约束做。两者可以并存：DB 用 PERSISTED 计算列做兜底约束，Service 不再写 AvailableQty。</details>

3. **并发题**：两个用户同时给同一货位发 +10 入库，乐观锁会让其中一个失败吗？  
   <details><summary>答案</summary>会。RowVersion 是按"读出来的版本"匹配的，第二个 SaveChanges 时 RowVersion 已变。但这里"+10"是可交换操作（commutative），冲突没必要让用户重试。<b>更合理的做法</b>：用 SQL <code>UPDATE T_Stock SET PhysicalQty = PhysicalQty + @qty WHERE ...</code> 直接增量更新，配合行锁，不走乐观锁。CP6 当前简化了，没区分"覆盖更新 vs 增量更新"，这是个可改进点。</details>

4. **设计题**：让你给 CP6 加一个"库存预约（reserve）"功能（订单确认就预占库存，发货才真扣），怎么扩展 StockMovementService？  
   <details><summary>答案</summary>新增两个 WmsTxnType：<code>Reserve</code> 和 <code>Cancel Reserve</code>。Reserve 增加 AllocatedQty 不变 PhysicalQty；Cancel Reserve 反向。引当（Allocate）从已 Reserve 的池子里调一份过来。这样能在 ERP 受注确认时立刻占住库存，但发货前还能整体取消。CP6 当前的 Allocate 就是简化版的 Reserve。</details>

5. **质疑题**：DBA 说"这套 T_StockTransaction 一年要存 5000 万行，建议物化只留 90 天，老数据归档"。你怎么权衡？  
   <details><summary>答案</summary>分区表（partition by month）+ 自动归档是行业标准。90 天的热区放高性能表 + index；老数据按月归档到 <code>T_StockTransaction_Archive</code> 或 cold storage（如 Azure Cool Blob）。重放/审计时如果要查超过 90 天的，UI 接受稍慢响应。注意：归档前必须做完所有 <b>projection 重算</b>（T_Stock 已经更新过，不依赖完整 transaction log）。CP6 当前未做分区，规模上去后必须做。</details>

---

## 🔗 延伸阅读

- [Domain-Driven Design (Eric Evans)](https://www.domainlanguage.com/ddd/) — 不变式、聚合根的概念源头
- [Event Sourcing (Martin Fowler)](https://martinfowler.com/eaaDev/EventSourcing.html)
- [Optimistic vs Pessimistic Concurrency in EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- 项目内：`docs/MSBBWM_Requirements.txt` §在库管理章 / `CP6.Core/Services/Wms/StockMovementService.cs`
