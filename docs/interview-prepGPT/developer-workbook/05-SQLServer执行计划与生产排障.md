# 05 · SQL Server 执行计划与生产排障

数据库优化最差的学习方式是背“建索引、避免 SELECT *、不要在列上用函数”。开发者接到的真实问题通常是：“库存页昨天还快，今天为什么 12 秒？”你必须先定位时间花在哪里，再用等待、执行计划、IO 和数据分布解释，最后证明改动没有把写入、锁和别的查询弄坏。

本章以库存分页为主线，但方法可用于订单、报工、追溯和审计查询。

## 1. 先定义慢，不要凭感觉

把模糊投诉改成可测问题：

```text
环境：生产同规格只读副本 / 隔离开发库
租户：数据量最大的租户
查询：warehouse=W1, product=P123, page=1, size=50
频率：连续 20 次，冷/热缓存分开
指标：API p50/p95、COUNT 与 page SQL 耗时、logical reads、返回行数
基线：昨天版本 / 无改动分支 / 相同数据快照
```

如果数据、参数和缓存状态不同，“优化前后”数字没有可比性。

## 2. 从请求分段到数据库

先证明数据库是主因：

```text
浏览器 Queue/Stalled
+ 网络与反向代理
+ ASP.NET 中间件
+ COUNT SQL
+ 分页 SQL
+ JSON 序列化
+ Vue 渲染
```

常见误判：

- 浏览器只允许有限并发，请求在客户端排队，却怪 SQL。
- 401 refresh 后重放，多了一次完整往返。
- COUNT 很慢，page query 很快，只优化列表 SELECT 没效果。
- API 100ms，el-table 渲染 3 秒，却一直调索引。

只有时间线显示 SQL 占主要部分，才进入本章后续步骤。

## 3. 先看等待，再看执行计划

正在慢的查询，先判断它在“工作”还是“等待”：

| 现象 | 可能方向 | 下一证据 |
|---|---|---|
| CPU 高、reads 高 | 扫描、估算错误、复杂计算 | actual plan、STATISTICS IO |
| 被会话阻塞 | 长事务、锁范围 | blocking chain、open transaction |
| PAGEIOLATCH | 从磁盘读页 | buffer/cache、索引、存储延迟 |
| WRITELOG | 日志写入瓶颈 | 事务大小、日志盘、频繁提交 |
| RESOURCE_SEMAPHORE | memory grant 等待 | sort/hash、估算、并发 |
| SOS_SCHEDULER_YIELD | CPU 压力 | 高 CPU 查询、并行度 |

等待类型是方向，不是结论。比如 PAGEIOLATCH 可能因为缺索引读太多，也可能是存储真的慢。

## 4. 实际执行计划读什么

不要只寻找黄色警告图标。按数据流从右向左读：

1. 每个算子 Actual Rows 与 Estimated Rows 差多少。
2. Seek/Scan 读了多少行，最终返回多少行。
3. Predicate 与 Seek Predicate 分别是什么。
4. 是否出现 Key Lookup，执行次数多少。
5. Sort/Hash 是否 spill 到 tempdb。
6. Memory grant 是不足还是远高于实际需要。
7. 并行计划是否产生昂贵 exchange。
8. 哪个算子的 elapsed/CPU/reads 最大。

“用了 Index Seek”不代表快。如果 seek 找到 50 万行再过滤 49.99 万行，一样昂贵。

## 5. 用 logical reads 比耗时更稳定

在隔离环境执行：

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
```

耗时受缓存、机器负载影响；logical reads 表示读取了多少 8KB 数据页，通常更能反映访问路径。

记录格式：

```text
Query: stock page W1/P123
Rows returned: 50
Logical reads: 18,240 → 62
CPU: 180ms → 4ms
Elapsed warm: 240ms → 8ms
Write overhead: INSERT/UPDATE plan 增加一个索引维护
```

别只记录“快了 30 倍”。要留下参数和计划证据。

## 6. 库存分页查询的索引推导

假设查询条件和排序：

```sql
WHERE TenantId = @tenantId
  AND WarehouseCd = @warehouse
  AND ProductCd LIKE @productPattern
  AND IsDeleted = 0
ORDER BY WarehouseCd, LocationCd, ProductCd, LotNo, Id
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
```

索引不是把 WHERE 中所有列照抄进去。推导顺序：

1. 所有查询必带 `TenantId`，通常应在前缀。
2. 等值且选择性有价值的仓库紧随其后。
3. 排序列尽量接在等值前缀后，减少额外 Sort。
4. `ProductCd LIKE '%x%'` 无法作为普通 seek 的有效范围。
5. 返回但不筛选/排序的窄列可 INCLUDE，避免 lookup。
6. `IsDeleted=0` 可考虑 filtered index，但要核对参数化与查询形状。

候选而不是最终答案：

```sql
CREATE INDEX IX_Stock_Tenant_Warehouse_Order
ON dbo.T_Stock
(
    TenantId,
    WarehouseCd,
    LocationCd,
    ProductCd,
    LotNo,
    Id
)
INCLUDE
(
    PhysicalQty,
    AllocatedQty,
    AvailableQty,
    UnitCd,
    ExpiryDate,
    OwnerType,
    RecallFlag,
    QcStatus
)
WHERE IsDeleted = 0;
```

这个索引可能很宽。宽索引会增加磁盘、缓存占用和每次库存写入成本。真实方案要根据最常用筛选、列投影和现有索引合并，不能从文档复制进生产。

## 7. COUNT 与分页 SELECT 是两个工作负载

列表返回 50 行不代表数据库只处理 50 行。COUNT 可能要统计全部匹配行。

优化选择：

- 使用同一过滤前缀的窄索引服务 COUNT。
- UI 不需要精确总数时返回 `hasNext`，多取一行。
- 深页避免 offset，改 keyset pagination。
- 总数可延迟加载或缓存，但要定义新鲜度。
- 报表需要一致快照时，考虑 snapshot isolation 或专门读模型。

不能为了快偷偷把精确 total 改成估算，除非产品接受并明确展示。

## 8. SARGability 的准确理解

SARGable 指谓词能让优化器有效使用索引搜索范围。

不利写法：

```sql
WHERE CAST(TxnAt AS date) = @day;
```

更好的半开区间：

```sql
WHERE TxnAt >= @day
  AND TxnAt < DATEADD(day, 1, @day);
```

但“列上有函数一定不走索引”不是绝对规律，优化器可能改写，计算列索引也可能支持。最终仍看实际计划。

### 8.1 字符串包含搜索

```sql
ProductCd LIKE '%123%'
```

普通索引难以从任意中间位置 seek。选择：

- 业务改成精确或前缀搜索。
- 全文索引，适合词语搜索，不一定适合编码片段。
- 额外规范化搜索列/倒排表。
- 数据量小则接受 scan，并限制租户/仓库范围。

先问用户为什么需要包含，别直接改交互语义。

## 9. 估算错误与统计信息

Actual Rows 与 Estimated Rows 差几个数量级时，优化器可能选错 join、内存和并行度。原因：

- 统计信息过旧。
- 列之间强相关，但单列统计看不到。
- 参数分布高度倾斜。
- table variable/复杂表达式估算有限。
- 隐式类型转换。

处理不是机械 `UPDATE STATISTICS`：

1. 确认统计更新时间和修改比例。
2. 看直方图是否覆盖关键值。
3. 考虑复合统计或索引。
4. 检查参数类型与列类型一致。
5. 再评估参数敏感计划。

## 10. 参数敏感与“有时快有时慢”

同一存储过程/参数化 SQL：仓库 W-small 只有 10 行，W-big 有 1000 万行。编译时参数影响缓存计划，后续另一种分布复用时可能很差。

证据：

- 相同 query hash，不同参数耗时差异大。
- 缓存计划的估算接近某一类参数。
- 重新编译暂时变快。

选项：

- SQL Server 参数敏感计划优化能力（视版本/兼容级别）。
- `OPTION (RECOMPILE)`，用 CPU 换每次适配。
- `OPTIMIZE FOR` 或拆分查询形状。
- 重新设计索引/数据分区，使不同参数都可接受。

不要把清 plan cache 当修复。它只是改变下一次谁来“抽签”。

## 11. OFFSET 深分页为什么越来越慢

第 10,000 页仍需跳过前面大量行：

```sql
OFFSET 499950 ROWS FETCH NEXT 50 ROWS ONLY
```

数据库通常仍要定位/扫描并丢弃很多记录。Keyset pagination 用最后一条排序键继续：

```sql
WHERE (WarehouseCd > @w)
   OR (WarehouseCd = @w AND LocationCd > @loc)
   -- 继续展开完整稳定排序键
ORDER BY ...
FETCH NEXT 50 ROWS ONLY;
```

优点：深翻页成本稳定、并发插入下更自然。

代价：不能随意跳第 N 页，前端要保存 cursor，复合比较复杂。后台管理页面若用户主要顺序浏览，很适合；若必须任意页码，offset 仍有价值。

## 12. 阻塞：快 SQL 也可能等很久

一个执行只需 10ms 的 SELECT，被未提交事务阻塞 20 秒，优化索引未必是首要解。

排查：

1. 找被阻塞 session 和 blocking session。
2. 看 blocker 正在做什么、事务何时开始。
3. 找是否“打开事务后等待用户输入/外部 HTTP”。
4. 看锁对象、模式和范围。
5. 先安全处理业务，再修事务边界。

不要看到 blocker 就立即 kill。它可能正在执行关键批量结账，回滚比等待更久。

## 13. 库存写入的锁与死锁

库位移动若在一个事务更新源行和目标行，并发存在：

```text
Txn A: lock Location 1 → wait Location 2
Txn B: lock Location 2 → wait Location 1
```

形成环路，SQL Server 选择一个 deadlock victim。

预防：

- 所有移动按稳定键排序后获取/更新两行。
- 事务尽量短，不在事务内做 SignalR/HTTP。
- 合适索引减少锁住无关行。
- 对 deadlock victim 做有限重试，重新执行业务校验。

死锁不是简单“并发太高”，而是资源获取形成环。

## 14. 隔离级别按异常选择

| 隔离 | 主要行为 | 代价/边界 |
|---|---|---|
| Read Committed | 防脏读，常见默认 | 两次读可不同，读写可能阻塞 |
| RCSI | 读已提交使用行版本 | tempdb/版本存储，写写仍竞争 |
| Snapshot | 事务级一致快照 | 更新冲突、版本存储 |
| Repeatable Read | 已读行保持锁 | 阻塞更多，仍可能幻读 |
| Serializable | 范围锁，最强传统隔离 | 并发最低、死锁/等待增加 |

库存扣减通常更依赖原子 UPDATE、rowversion、唯一约束和事务，而不是把全系统直接升到 Serializable。

## 15. Dapper 查询的生产检查

Dapper 适合精确 SQL 和报表，但要手动负责：

- 参数化。
- TenantId。
- 超时和取消。
- 事务传递。
- DTO 类型/decimal 精度。
- 多结果集释放。
- SQL 可观测性。

错误示例：

```csharp
var sql = $"SELECT * FROM T_Stock WHERE ProductCd = '{code}'";
```

不仅注入，还漏租户。正确方向：

```csharp
const string sql = """
SELECT WarehouseCd, LocationCd, ProductCd, LotNo, AvailableQty
FROM dbo.T_Stock
WHERE TenantId = @TenantId
  AND ProductCd = @ProductCd
  AND IsDeleted = 0;
""";
```

参数类型要与列一致。把 nvarchar 参数比 varchar 列可能引入隐式转换，影响索引。

## 16. 安全实验脚本原则

执行计划实验只在专用数据库：

- 开始先 `SELECT DB_NAME()`。
- 创建对象前用 `OBJECT_ID` 守卫，已存在就中止。
- 合成数据有确定数量和分布。
- 记录索引前后计划与 IO。
- 不在生产用 `FREEPROCCACHE`、`DROPCLEANBUFFERS`。
- 清理前再次确认数据库名和对象名。

## 17. 必做实验 A：索引前后

在 `labs/02-How-to练SQL与执行计划.md` 的专用表上完成：

1. 生成至少 5 万行，包含大/小仓库倾斜。
2. 运行仓库+产品查询，保存 actual plan 与 IO。
3. 建候选复合索引。
4. 重跑相同参数、相同缓存条件。
5. 再测 INSERT/UPDATE 速度和索引大小。
6. 写出是否保留索引的结论，不以“SELECT 变快”单独决定。

## 18. 必做实验 B：参数倾斜

让 W1 占 90% 数据，W2 占 0.1%。使用参数化查询分别先编译 W1/W2，观察计划和耗时。若本地版本支持 Query Store，比较不同 plan。提出至少两种处理方案及代价。

## 19. 必做实验 C：阻塞与死锁

使用两个 SSMS 窗口，在专用练习表：

1. Session A 开事务更新 row 1，不提交。
2. Session B 更新同一行，观察阻塞。
3. 查看 blocking chain。
4. 回滚 A，确认 B 继续。
5. 再用 A 1→2、B 2→1 制造死锁。
6. 读取 deadlock graph，指出 victim、资源和获取顺序。

## 20. 面试回答模板

> 我排查慢 SQL 不会先加索引。先把请求分段，确认时间主要在数据库；再看当前会话是在 CPU/IO 工作还是被锁、内存、日志等待。SQL 本身用 actual plan 对比估算与真实行数，结合 STATISTICS IO 看读页，检查 seek predicate、残余过滤、lookup、sort spill 和参数分布。库存分页还要分开看 COUNT 与 page query，复合索引从 TenantId、等值条件和稳定排序推导，同时测写入代价。若只是偶发慢，我会重点检查阻塞、统计和参数敏感，而不是清缓存碰运气。

## 21. 闭卷验收

1. 写出慢请求的分段计时图。
2. 解释 Actual/Estimated Rows 差异为什么会改变计划。
3. 为库存分页推导一个候选索引并说明为什么可能拒绝它。
4. 比较 offset 和 keyset pagination。
5. 画出两库位移动的死锁环。
6. 说明参数嗅探/敏感不是“SQL Server 缓存有 bug”。
7. 用 logical reads 和写入开销证明一次索引改动。
8. 给出 Dapper 的租户、参数、事务和取消检查表。
