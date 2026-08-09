# 05 · EF Core 状态、事务、租户与审计

EF Core 不只是“把 LINQ 变 SQL”。它同时维护模型、实体身份、原始值、当前值、关系、事务和并发。大多数难排问题来自开发者以为自己在修改一个普通对象，却忘了 DbContext 正在维护一张状态图。

## 1. DbContext 是 Unit of Work

一次典型请求 scope：

```text
创建 DbContext
→ 查询/跟踪实体
→ 业务代码修改对象
→ DetectChanges
→ 生成 INSERT/UPDATE/DELETE
→ SaveChanges transaction
→ 更新状态/并发值
→ dispose
```

Context 应短生命周期。长期 Context 会让 ChangeTracker 增长、缓存旧数据、增加并发和内存问题。

## 2. 实体状态

| 状态 | 含义 | SaveChanges |
|---|---|---|
| Detached | Context 不认识 | 无动作 |
| Unchanged | 跟踪且未改 | 无 UPDATE |
| Added | 新行 | INSERT |
| Modified | 有字段变化 | UPDATE |
| Deleted | 删除 | DELETE 或业务转换 |

`Entry(entity).State = Modified` 常把所有可写属性标为修改，可能覆盖并发用户刚改的字段。通用 Repository 的“传 DTO 映射实体后全量 Update”尤其危险。

## 3. Identity resolution

同一 tracking query/context 中，同主键通常返回同一对象实例。这保证关系 fix-up 一致，也意味着第二次查询不一定得到全新对象。

`AsNoTracking` 不做普通跟踪；`AsNoTrackingWithIdentityResolution` 在单次结果内去重身份但不留在 Context tracker。

## 4. Tracking 与 NoTracking

读后修改：tracking。

列表/报表只读：`AsNoTracking`。

不要全局默认 NoTracking 后在写服务里忘记附加；也不要所有查询都 tracking 造成内存/CPU 浪费。按用例选择。

## 5. Change detection

EF 保存前比较原始/当前值，或通过通知追踪修改。审计也常依赖 `PropertyEntry.IsModified`。

批量循环中频繁自动 DetectChanges 可能有成本，但关闭后必须正确恢复和手动调用。先 profile，不要提前关闭框架保护。

## 6. Attach、Update 与 disconnected DTO

Web 请求是断开的：客户端发回 DTO，不是原 Context 跟踪实体。

安全常见模式：

```csharp
var entity = await db.Products.SingleAsync(x => x.Id == dto.Id, ct);
entity.Name = dto.Name;
entity.RowVersion = dto.RowVersion; // 按并发策略处理 original value
await db.SaveChangesAsync(ct);
```

优点：全局过滤/权限下重新加载，白名单修改字段。

直接 `db.Update(mappedEntity)` 的风险：mass assignment、全字段覆盖、租户注入、审计噪声、并发 token 处理错误。

## 7. SaveChanges 的事务

单次 SaveChanges 的数据库命令通常自动事务。多个 SaveChanges/外部步骤需要显式事务或重构为一次保存。

事务不能包含远程 HTTP/SignalR 以获得真正原子性；长事务会占锁。数据库提交与消息用 Outbox 连接。

## 8. 乐观并发

`[Timestamp] RowVersion` 让 UPDATE/DELETE WHERE 包含原版本。0 行受影响时抛并发异常。

处理策略：

- Store wins：刷新服务器版本，丢本地修改。
- Client wins：基于新版本重新应用，需重新校验。
- Merge：逐字段合并，UX 最复杂。
- 返回 409：让用户选择。

库存扣减不能简单 client wins；必须重新判断可用量。

## 9. 唯一约束与并发令牌不同

RowVersion 保护“同一已存在行被并发修改”；唯一约束保护“两个事务并发创建相同业务键”。两者共同需要。

把“先查不存在再插入”当唯一性保证会竞态。

## 10. 导航加载

- Eager：Include/投影一起取。
- Explicit：明确 Load。
- Lazy：访问时自动查，方便但容易 N+1/序列化意外。

CP6 类后台系统更适合显式投影和明确查询。Controller 返回实体导航很容易泄漏数据与生成超大 JSON。

## 11. Split query

多个 collection Include 单 SQL会笛卡尔爆炸；split query 发多条 SQL。取舍：减少重复行 vs 增加往返/一致性窗口。实际看关系数量和结果规模。

## 12. 全局查询过滤器

软删除与多租户：

```csharp
HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted)
```

优点是默认安全；风险是条件隐式、管理员查询需 `IgnoreQueryFilters`、导航 required 关系可能改变结果。所有绕过必须集中受控。

## 13. 写入租户盖章

新增实体 TenantId 空时从当前 tenant context 填充，减少漏写。普通 DTO 不应暴露 TenantId。

要决定显式非空且不同租户时：拒绝还是允许平台场景。普通 service 应 fail-closed；跨租户操作走独立权限接口。

## 14. 多租户唯一索引

```text
UNIQUE(Code) → UNIQUE(TenantId, Code)
```

查询隔离与数据库唯一语义要一致。迁移时还要处理外键是否引用原唯一键，以及存量 TenantId 回填。

## 15. 字段审计

`IAuditable` opt-in，保存前捕获 diff，保存后获取新增主键并写审计。跳过主键、租户、元字段和敏感字段。

审计问题：

- 大文本/PII 是否记录。
- 失败是否回滚业务。
- 谁能读审计。
- 保留多久、如何分区归档。
- 批量更新绕过。

审计不是简单 JSON before/after；它是合规数据模型。

## 16. ExecuteUpdate/ExecuteDelete

直接数据库执行，不加载/跟踪实体，性能好。但绕过：

- ChangeTracker 字段审计。
- 内存领域方法。
- 自动更新时间/租户盖章（取决于表达式）。

使用时显式补规则和审计，且执行后 Context 中已跟踪实体可能过期。

## 17. Dapper 与 EF 混用事务

若要同一事务，Dapper 使用 EF 当前 DbConnection 和 DbTransaction；否则两个调用看似在一个 service，实际各自提交。

还要防 Context 追踪的旧值与 Dapper 已更新数据库不一致，必要时 reload/clear。

## 18. 原生 SQL

值参数化；表名/列名不能普通参数化，动态标识符必须白名单。多租户 WHERE 与软删除要手写。

FromSql 组合能力取决于 SQL 可组合形状；存储过程结果通常不能继续任意数据库端 LINQ。

## 19. 模型与迁移

迁移是从模型差异生成的版本化数据库变更，不等于生产安全。

上线检查：

- 是否锁大表。
- 加 NOT NULL 是否有默认/回填。
- 建索引是否在线/耗时。
- 先部署兼容代码还是先迁移。
- 回滚代码能否读新 schema。
- migration history 是否一致。

对大表使用 expand/contract：先加可空新列，双写/回填，再切读，最后收紧。

## 20. 性能观察

开启 command 日志（脱敏）、slow query、TagWith、OpenTelemetry。看 SQL 数量、往返、行列宽度和跟踪数量。

避免：循环 SaveChanges、N+1、实体全列、无界 ToList、同 Context 长期增长。

## 21. Repository 的边界

泛型仓储适合稳定 CRUD，但如果 `orderBy` 参数被忽略、Update 全字段、Queryable 泄漏后任意组合，抽象可能比直接 DbContext 更危险。

复杂读模型用 query service；关键写用领域 service；Repository 只承诺它真正实现的行为，并有契约测试。

## 22. 测试

- InMemory：业务分支，不代表事务/SQL。
- SQLite：关系约束，仍不等 SQL Server。
- SQL Server：rowversion、collation、索引、锁和真实迁移。

测试完成后用新 Context 查最终状态，避免 tracker 假象。

## 23. 必做实验

1. Tracking 实体修改后保存；NoTracking 同样修改观察。
2. `Update` detached entity 观察哪些列被标 Modified。
3. 两 Context rowversion 冲突。
4. 两事务并发创建同业务键触发唯一约束。
5. ExecuteUpdate 后查询审计，并 reload 跟踪实体。
6. Dapper 与 EF 同/不同事务故意失败，比较回滚。

## 24. 闭卷问题

1. DbContext 为什么不能当长期缓存？
2. `Update` disconnected entity 为什么危险？
3. RowVersion 与唯一约束各防什么？
4. AsNoTracking 的边界？
5. Split query 牺牲什么？
6. 全局过滤器有哪些绕过？
7. 两阶段审计解决什么？
8. ExecuteUpdate 绕过哪些机制？
9. EF 与 Dapper 如何共享事务？
10. 为什么迁移生成成功不等于生产可执行？

