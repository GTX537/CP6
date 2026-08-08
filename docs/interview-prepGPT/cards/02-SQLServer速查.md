# SQL Server 速查

## 逻辑顺序

`FROM/JOIN → ON → WHERE → GROUP → HAVING → SELECT → DISTINCT → ORDER → 分页`

## NULL

- NULL 比较 = UNKNOWN；WHERE 只留 TRUE。
- `<>` 不含 NULL。
- `NOT IN` 子查询含 NULL 可能空；用 NOT EXISTS 或非空约束。
- COUNT(*) 计行，COUNT(col) 忽略 NULL。

## JOIN

- 先定义基数；重复键放大结果。
- LEFT JOIN 右表条件放 WHERE 常去掉空匹配。
- EXISTS 做存在判断，避免 JOIN + DISTINCT。

## 聚合/窗口

- WHERE 明细前；HAVING 分组后。
- GROUP BY 折叠；window 保留明细。
- ROW_NUMBER 不并列；RANK 并列有空洞；DENSE_RANK 无空洞。
- 每组最新必须稳定 tiebreaker。
- running sum 显式 `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`。
- CTE 不保证物化；递归 BOM 防环/深度/版本/精度。

## 索引

- 聚集叶层数据；非聚集叶层键/include/定位。
- 复合索引来自等值、范围、排序、覆盖和工作负载。
- Seek 不一定好；Scan 不一定坏。
- Lookup 看次数；INCLUDE 有写成本。
- SARG：不包列，日期用半开范围，`%x%` 难普通 seek。
- 看 actual vs estimated、IO、spill、隐式转换、行数膨胀。

## 事务/并发

- ACID 不是“开启事务就安全”。
- RU/RC/RR/Serializable + RCSI/Snapshot。
- NOLOCK 可脏读/重复/漏，不是免费速度。
- 死锁：graph、统一顺序、短事务、索引、有界重试。
- 防超卖：条件 UPDATE / RowVersion / 悲观锁 / 分区串行。
- Outbox 原子记录待发事件，不消灭重复。

## 生产排障

`范围 → 指标/等待 → 实际计划/IO → 阻塞/资源 → 最小修复 → 验证 → 复盘`

- 备份成功 ≠ 恢复成功；定期演练 RPO/RTO。
- 磁盘监控看剩余时间，不只百分比。
- 不把 shrink/restart/加索引当通用修复。
- Dapper 手工租户、软删、超时、取消、审计。

## 五道必须会写

1. LEFT JOIN 保留左表。
2. NOT EXISTS。
3. 每组最新 ROW_NUMBER。
4. 条件聚合。
5. 条件 UPDATE 防超卖。

