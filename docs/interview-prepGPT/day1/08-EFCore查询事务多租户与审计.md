# 08 · EF Core：查询、事务、多租户与审计

## 1. DbContext 的职责

DbContext 同时是：

- 数据库会话/工作单元入口。
- 模型元数据持有者。
- 查询提供者入口。
- 实体状态跟踪器。
- `SaveChanges` 的变更提交边界。

它不是线程安全对象，通常每个 Web 请求一个 scoped 实例。

## 2. 实体状态

| 状态 | 含义 | SaveChanges 典型动作 |
|---|---|---|
| Detached | 未被上下文跟踪 | 无 |
| Unchanged | 已跟踪，无变化 | 无 |
| Added | 新实体 | INSERT |
| Modified | 有修改 | UPDATE |
| Deleted | 删除 | DELETE 或软删逻辑 |

EF 默认使用快照检测：加载时保存原值，DetectChanges 时比较当前值。大量跟踪实体会带来内存和比较成本。

## 3. 跟踪与 `AsNoTracking`

跟踪适合“加载 → 修改 → 保存”。只读查询使用 `AsNoTracking` 可减少状态管理成本。

但 NoTracking 不等于永远更快：

- 同一结果中重复实体时，身份解析可能有价值。
- 后续修改需要 Attach 或重新查询。
- 小结果差异可能不重要。

CP6 `StockController.Search` 是只读分页，使用 NoTracking 合理。

## 4. `FindAsync` 的特殊语义

`DbSet.FindAsync(id)` 先检查 ChangeTracker，已跟踪则可能不查数据库；普通 `FirstOrDefaultAsync` 通常生成查询。二者在陈旧数据、软删除过滤和跟踪状态上可能表现不同，仓储抽象不应掩盖这种语义。

## 5. 映射

优先级通常是：约定 → DataAnnotations → Fluent API。复杂索引、关系、过滤索引和提供者特定配置更适合 Fluent API。

CP6 `Stock`：

- `[Table("T_Stock")]` 映射表名。
- `[Required, MaxLength]` 约束字符串。
- `[Column(TypeName = "decimal(21,8)")]` 固定数量精度。

数据库约束才是最终防线。C# `[Required]` 与数据库 NOT NULL 都有价值，但覆盖的入口不同。

## 6. 查询加载策略

| 策略 | 优点 | 风险 |
|---|---|---|
| Eager `Include` | 一次表达对象图 | JOIN 膨胀、过度加载 |
| Explicit | 控制何时加载 | 容易循环成 N+1 |
| Lazy | 代码方便 | 隐式查询、序列化风暴 |
| DTO projection | 精确列、通常性能最好 | 需要显式映射 |

管理系统列表优先投影 DTO。编辑页需要聚合对象时再权衡 Include 或拆分查询。

## 7. SaveChanges 的事务

单次 `SaveChanges` 对关系数据库通常在事务中执行。跨多次 SaveChanges 或混合外部副作用时，需要显式事务或更高层协调。

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
try
{
    // 修改库存 + 写流水
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
catch
{
    await tx.RollbackAsync(ct);
    throw;
}
```

数据库事务不能覆盖 Kafka、SignalR、外部 HTTP。跨边界一致性通常使用 outbox、幂等消费者、Saga/补偿。

## 8. 乐观并发

乐观并发假设冲突少：读取时带版本，更新时 WHERE 包含版本；影响行数为 0 表示其他事务已修改。

处理流程：

1. 捕获 `DbUpdateConcurrencyException`。
2. 读取数据库当前值。
3. 选择提示用户、自动重试或合并。
4. HTTP 通常返回 409。

自动重试只适合可重新计算、无不可重复外部副作用的操作。库存扣减不能简单无限重试。

## 9. 悲观锁与原子 SQL

库存并发可选：

- RowVersion 乐观锁 + 有界重试。
- 事务内锁提示/高隔离级别。
- 单条条件 UPDATE：`UPDATE ... SET Qty=Qty-@n WHERE Qty>=@n`，根据影响行数判断。
- 按 SKU 分区串行队列。

没有万能方案。看冲突率、吞吐、延迟、数据库类型和业务可接受失败。

## 10. 多租户全局查询过滤

CP6 反射扫描所有 `BaseTenantEntity` 根类型，动态注册：

```text
e.TenantId == CurrentTenantId
```

优点：

- 新实体继承基类即可自动纳入。
- 业务查询少写重复 WHERE。
- 导航查询也受到过滤。

已知绕过和限制：

1. `IgnoreQueryFilters()`。
2. Dapper/FromSql 原生 SQL 的具体形态。
3. 非 BaseTenantEntity 特殊表需单独配置。
4. 当前写盖章只在 Added 且 TenantId 为空时赋值；若传入非空错误租户，不会被自动覆盖。
5. 平台级跨租户操作需要受控上下文和审计。

### 更严格写防线建议

对普通租户请求：

- Added：无条件把 TenantId 设为当前租户，或拒绝不一致值。
- Modified/Deleted：确认原实体 TenantId 等于当前租户。
- DTO 不接收 TenantId。
- Dapper API 强制接收 TenantContext 并参数化租户条件。
- 反射/集成测试扫描未纳入隔离的实体和写端点。

这是建议，不代表当前全部已完成。

## 11. 多租户唯一索引

只加查询过滤还不够。若业务码在每租户内唯一，数据库索引应是：

```text
UNIQUE (TenantId, BusinessCode)
```

否则租户 B 不能复用租户 A 的订单号。CP6Context 有反射逻辑把多租户实体部分唯一索引升级为租户前缀，但对被 FK 依赖的唯一键等场景明确跳过。这表示仍有迁移复杂度和已知限制。

## 12. 审计管道

CP6 字段审计大致分两阶段：

1. 保存前扫描 `IAuditable` 实体，记录 before/after diff 和存前主键。
2. 业务保存后获取新主键，写 `Sys_FieldAuditLog`，在同一关系数据库事务内提交。

安全处理：

- 跳过主键、TenantId 和元字段。
- `[AuditIgnore]`。
- 密钥字段名拒绝名单。
- 值长度截断。
- 使用 InvariantCulture。

### 审计绕过面

- `ExecuteUpdate`/`ExecuteDelete` 直接生成 SQL，不走 ChangeTracker/SaveChanges diff。
- Dapper 或原生 SQL。
- 不实现 `IAuditable` 的实体。
- 数据库外部修改。

因此审计需求高时要规定唯一写通道，或为批量路径显式写审计、使用数据库 CDC/Temporal/触发器等补充机制。

## 13. 两阶段审计的事务细节

当前实现若没有外部事务，会自己开启关系数据库事务；已有事务则参与。先保存业务，再写审计，最后 commit。调用 `base.SaveChanges` 避免审计自身再次进入 override。

需要注意：返回值只返回业务影响行数，审计行不计入。InMemory 提供者不支持同样的事务语义，测试不能把 InMemory 通过当成 SQL Server 原子性证明。

## 14. 迁移

迁移是“模型变化到数据库 DDL 的版本化代码”，不是自动安全部署。

生产流程：

1. 生成迁移并人工审查。
2. 检查锁表、全表更新、默认值、索引创建成本。
3. 在与生产相近的数据量演练。
4. 备份并验证恢复。
5. 使用兼容式 expand/migrate/contract 处理零停机。
6. 上线后验证 schema、应用和关键查询。

`Down` 不一定能无损回滚数据。生产回滚常是应用回退 + 向前修复数据库，而不是盲目执行 Down。

## 15. 批量更新

EF Core `ExecuteUpdateAsync` 高效，因为无需加载和跟踪每行。但它会绕过：

- 实体 setter/领域方法。
- ChangeTracker。
- SaveChanges override。
- 某些审计和租户写保护。

选择批量操作前先列出所有横切副作用，再显式补齐。

## 16. Dapper 与 EF Core

不是二选一：

- EF 适合实体写入、关系、迁移和大部分查询。
- Dapper 适合复杂报表、精确 SQL、存储过程。

Dapper 不自动提供 EF 的查询过滤、跟踪和 SaveChanges 审计。多租户、软删、权限、超时、取消必须显式传入。

## 17. StockMovementService 深读

优点：

- 入口校验。
- 盘点冻结保护。
- 库存与流水单次事务。
- 负库存策略。
- 提交后桥接与通知不破坏主交易。

值得追问：

- 文档提 RowVersion，实体/迁移是否确实配置并由 SQL Server生成。
- 新库存并发插入同业务键如何依赖唯一索引和重试。
- `MoveAsync` 两腿不原子。
- commit 后空 catch 缺少可观测/补偿。
- InMemory 测试无法证明真实锁和事务。

这五点可以形成一段很强的“我如何审查关键写路径”回答。

## 高频陷阱

1. DbContext 是数据库连接，创建后始终占一个连接。
2. AsNoTracking 后完全不能更新。
3. 单次 SaveChanges 能让数据库和消息队列原子提交。
4. 全局过滤器等于绝对租户安全。
5. InMemory 测试通过就证明 SQL Server 行为正确。
6. ExecuteUpdate 只是更快的 SaveChanges。

## 闭卷验收

- [ ] 画实体五态和 SaveChanges。
- [ ] 解释投影、Include、拆分查询的取舍。
- [ ] 给出库存防超卖三种方案。
- [ ] 列出多租户读写五个绕过面。
- [ ] 解释字段审计为什么分两阶段。
- [ ] 对 StockMovementService 做优点/限制各三条的代码审查。

