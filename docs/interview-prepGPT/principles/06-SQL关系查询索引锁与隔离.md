# 06 · SQL 关系查询、索引、锁与隔离

SQL 是声明“我要什么”，优化器决定“怎么得到”。开发者既要准确表达集合语义，也要读懂物理访问代价。本章把 NULL/JOIN/聚合/窗口函数与索引、锁、事务放在同一模型里。

## 1. 关系思维

表是行集合，查询通过筛选、投影、连接、分组产生新关系。没有 ORDER BY，结果没有承诺顺序；今天看起来稳定只是当前计划的副作用。

## 2. NULL 与三值逻辑

比较 NULL 结果 UNKNOWN：

```sql
WHERE OwnerCd <> 'C001'
```

不会返回 OwnerCd NULL 的行，因为 WHERE 只保留 TRUE。

需要：

```sql
WHERE OwnerCd <> 'C001' OR OwnerCd IS NULL;
```

`NOT IN` 子查询含 NULL 时尤其危险，常改 `NOT EXISTS` 并明确相关条件。

## 3. JOIN

INNER 只保匹配；LEFT 保留左行，右侧无匹配为 NULL。

```sql
FROM Product p
LEFT JOIN Stock s ON s.ProductCd = p.ProductCd
WHERE s.WarehouseCd = 'W1'
```

WHERE 排除 NULL，效果接近 INNER。若要保留无库存产品，把仓库条件放 ON。

## 4. 多对多膨胀

订单 join 明细再 join 付款，一对多×一对多会重复金额。先分别聚合到订单粒度再 join，或明确目标粒度。

每条 SQL 先写：“结果一行代表什么”。粒度不清，SUM 很容易重复。

## 5. GROUP BY

SELECT 中非聚合列必须属于分组键。条件聚合：

```sql
SUM(CASE WHEN Status='PASSED' THEN Qty ELSE 0 END)
```

WHERE 在分组前过滤行；HAVING 在分组后过滤组。

## 6. 窗口函数

窗口函数保留明细行：

```sql
ROW_NUMBER() OVER(
  PARTITION BY WarehouseCd, ProductCd
  ORDER BY TxnAt DESC, Id DESC
) AS rn
```

`ROW_NUMBER` 唯一序号；`RANK` 并列留空位；`DENSE_RANK` 并列不留空位。每组最新用 rn=1，并提供唯一 tiebreaker。

累计：

```sql
SUM(Qty) OVER(
 PARTITION BY ProductCd
 ORDER BY TxnAt, Id
 ROWS UNBOUNDED PRECEDING)
```

明确 ROWS/RANGE，重复排序值时语义不同。

## 7. CTE

CTE 提高复杂查询分段可读性，不自动物化/缓存。递归 CTE 适合 BOM/组织树，但要防环、深度和重复路径。

大型中间结果反复使用时，临时表可能更好：有统计、可索引，但增加 tempdb 和写入。

## 8. 数据类型

- 金额/数量 decimal，统一 precision/scale。
- 时间优先 datetime2；跨时区存 UTC + 业务时区。
- varchar/nvarchar 依据字符集，参数类型一致避免隐式转换。
- 主键 GUID/顺序 GUID/int 各影响页分裂、分布式生成和可猜测性。

不要用字符串存日期、数量和 JSON 可查询字段只为“灵活”。

## 9. 规范化与反规范化

规范化减少更新异常；反规范化用重复换读取速度/历史快照。库存保留 AvailableQty 是物化不变量，必须保证所有写路径同步。

历史单据应保存当时名称/价格快照，不能永远 join 当前主数据，否则过去报表会变化。

## 10. 索引 B-tree 模型

索引按 key 有序。复合索引最左前缀决定可高效定位的条件。叶级包含 key、聚集键和 INCLUDE。

索引收益：少读页、减少排序/lookup。代价：空间、写放大、锁/日志、统计维护。

## 11. 聚集与非聚集

聚集索引叶级是数据行，表只能一种聚集顺序；非聚集叶级指向行。主键不必然聚集，取决于设计。

随机 GUID 聚集键可能页分裂；但不能只因这一点改所有主键，考虑分布式、迁移和外键成本。

## 12. 覆盖与 Key Lookup

非聚集索引找到键后缺返回列，会 lookup 聚集行。少量 lookup 很快，大量则随机读取昂贵。INCLUDE 可覆盖，但索引变宽。

实际计划看 lookup 执行次数，不是看到 lookup 图标就消灭。

## 13. 过滤索引

大量软删除表可：

```sql
CREATE INDEX ... WHERE IsDeleted = 0;
```

查询谓词必须让优化器证明符合过滤条件。参数化形状、NULL 与 SET 选项需验证。

## 14. 执行计划

关注真实/估算行数、seek/scan、残余 predicate、join 类型、sort/hash spill、memory grant、并行 exchange、warning 和 reads。

cost 百分比是估算，不是实际耗时。用 actual plan、IO/TIME 和 Query Store。

## 15. Join 算法

- Nested Loops：外侧少行、内侧有索引。
- Hash Join：大无序输入，需内存。
- Merge Join：两侧已按 join key 有序。

算法没有固定好坏；估算错误会让正确算法选择失效。

## 16. 事务 ACID

Atomicity 一起提交；Consistency 不变量从一个合法状态到另一个；Isolation 并发可见性；Durability 提交后持久。

应用事务不能自动覆盖 Kafka/HTTP。分布式一致性用 outbox、幂等、状态机和补偿。

## 17. 并发现象

- Dirty read：读未提交。
- Non-repeatable read：同事务两次读同一行不同。
- Phantom：同条件多出/少行。
- Lost update：并发基于旧值覆盖。

隔离级别只解决一部分；业务冲突还需 version/条件更新。

## 18. 锁

共享锁读、排他锁写、更新锁帮助避免转换死锁；还有行/页/表、意向锁、键范围锁。SQL Server 可能锁升级。

索引缺失会扫描并锁更多资源，既慢又阻塞。

## 19. 死锁

两个事务形成等待环。SQL Server 选 victim 回滚。读取 deadlock graph 找资源和顺序。

修复：统一资源顺序、缩短事务、合适索引、降低不必要锁范围、有限重试。不要只增加 timeout。

## 20. 乐观与悲观

乐观：允许竞争，提交时 version 检测。适合冲突低、交互长。

悲观：提前锁，适合冲突高且临界区短，但阻塞/死锁成本高。

库存可用 rowversion/条件 UPDATE；不是所有库存都必须 Serializable。

## 21. 原子条件更新

```sql
UPDATE T_Stock
SET AvailableQty = AvailableQty - @qty,
    PhysicalQty = PhysicalQty - @qty
WHERE Id = @id
  AND AvailableQty >= @qty;
```

检查影响行数。0 可能是不存在或不足，需要额外区分。还要同步流水、rowversion、租户和审计。

## 22. 阻塞排查

找 blocker、open transaction、SQL、等待资源。不要直接 kill；评估回滚和业务。

事务中等待用户、网络调用、循环处理大量行是常见根因。

## 23. 参数敏感、统计与隐式转换

统计描述分布，估算影响计划。数据倾斜会让一个缓存计划不适合所有参数。参数类型与列不一致可能在列侧转换，破坏 seek。

证据后选择 PSP/recompile/拆形状/索引，不清缓存当修复。

## 24. 存储过程与 Dapper

存储过程适合稳定批处理/复杂 SQL 权限边界，但会把逻辑放数据库。必须版本化、测试、监控。

Dapper 让 SQL 明确，不提供租户、审计、跟踪。参数化、事务、取消和映射都由开发者负责。

## 25. 必做实验

1. NULL 的 `<>`、`NOT IN`、`NOT EXISTS`。
2. LEFT JOIN 条件放 ON/WHERE 比较。
3. 多对多 join 制造重复汇总再修复。
4. ROW_NUMBER 每组最新，去掉 tiebreaker。
5. 建复合索引比较 IO 与写入。
6. 两 session 制造阻塞和死锁。
7. 原子条件扣减两个并发请求。

## 26. 闭卷问题

1. NULL 为什么让 `NOT IN` 陷阱？
2. LEFT JOIN 条件位置怎样改变结果？
3. 一行结果粒度为何先于 SUM？
4. ROW_NUMBER/RANK 区别？
5. 复合索引最左前缀是什么？
6. Seek 为什么仍可能慢？
7. Snapshot 与 Serializable 取舍？
8. 死锁与普通阻塞区别？
9. 原子 UPDATE 怎样避免 lost update？
10. Dapper 为什么参数化仍不等于安全完整？
