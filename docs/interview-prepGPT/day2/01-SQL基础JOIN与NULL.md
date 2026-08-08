# 01 · SQL 基础、JOIN 与 NULL

## 1. SQL 是声明式语言

你描述“要什么结果”，优化器决定具体访问路径。书写顺序和逻辑处理顺序不同。

### 逻辑处理顺序

```text
FROM / JOIN
→ ON
→ WHERE
→ GROUP BY
→ HAVING
→ SELECT
→ DISTINCT
→ ORDER BY
→ OFFSET / FETCH
```

这解释了为什么同层 SELECT 别名通常不能在 WHERE 里使用，也解释了 LEFT JOIN 的条件位置问题。

## 2. 示例表

后续用简化模型：

```sql
CREATE TABLE Warehouse (
    WarehouseCd varchar(10) NOT NULL PRIMARY KEY,
    WarehouseName nvarchar(100) NOT NULL
);

CREATE TABLE Stock (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId uniqueidentifier NOT NULL,
    WarehouseCd varchar(10) NOT NULL,
    LocationCd varchar(30) NOT NULL,
    ProductCd varchar(20) NOT NULL,
    LotNo varchar(30) NOT NULL,
    PhysicalQty decimal(21,8) NOT NULL,
    AllocatedQty decimal(21,8) NOT NULL,
    AvailableQty decimal(21,8) NOT NULL,
    ExpiryDate datetime2 NULL
);
```

教材表名是教学简化。真实表和列请以 EF 映射/迁移为准。

## 3. SELECT 不只是取列

```sql
SELECT
    s.WarehouseCd,
    s.ProductCd,
    s.PhysicalQty - s.AllocatedQty AS CalculatedAvailable
FROM dbo.T_Stock AS s
WHERE s.PhysicalQty <> 0
ORDER BY s.WarehouseCd, s.ProductCd;
```

要点：

- 明确列，不用生产接口里的 `SELECT *`。
- 别名表达业务含义。
- 无 ORDER BY 时，结果顺序无保证。
- 排序键不唯一时，分页仍可能漂移，应补稳定 tiebreaker。

## 4. WHERE 与可搜索性

常用谓词：`=`, `<>`, `>`, `BETWEEN`, `IN`, `LIKE`, `IS NULL`, `EXISTS`。

`BETWEEN` 两端都包含。时间区间更安全的半开写法：

```sql
WHERE TxnDateTime >= @from
  AND TxnDateTime <  @toExclusive
```

避免 `CAST(TxnDateTime AS date) = @day` 包住索引列；改为一天起止范围，通常更利于索引 seek。

## 5. NULL 是未知，不是空字符串或零

SQL 使用三值逻辑：TRUE、FALSE、UNKNOWN。WHERE 只保留 TRUE。

```sql
WHERE OwnerCd <> 'C001'
```

不会保留 `OwnerCd IS NULL` 的行，因为 `NULL <> 'C001'` 是 UNKNOWN。

若业务要“不是 C001，包括未知”：

```sql
WHERE OwnerCd <> 'C001' OR OwnerCd IS NULL
```

## 6. `NOT IN` 与 NULL

```sql
WHERE ProductCd NOT IN (SELECT ProductCd FROM BlockedProduct)
```

若子查询含 NULL，比较可能整体变 UNKNOWN，结果意外为空。更稳妥：

```sql
WHERE NOT EXISTS (
    SELECT 1
    FROM BlockedProduct b
    WHERE b.ProductCd = s.ProductCd
)
```

或者先证明子查询列 NOT NULL。面试必须说出数据约束，而不是机械规定“永远不用 NOT IN”。

## 7. INNER JOIN

```sql
SELECT w.WarehouseName, s.ProductCd, s.AvailableQty
FROM Stock s
JOIN Warehouse w
  ON w.WarehouseCd = s.WarehouseCd;
```

内连接只保留双方匹配。若连接键一侧不唯一，结果行数会放大。JOIN 不是“补列”，它是关系组合；先确认基数。

## 8. LEFT JOIN 的 ON 与 WHERE

目标：列出所有仓库，以及它们的正库存；没有库存的仓库也保留。

正确：

```sql
SELECT w.WarehouseCd, s.ProductCd, s.PhysicalQty
FROM Warehouse w
LEFT JOIN Stock s
  ON s.WarehouseCd = w.WarehouseCd
 AND s.PhysicalQty > 0;
```

若写：

```sql
FROM Warehouse w
LEFT JOIN Stock s ON s.WarehouseCd = w.WarehouseCd
WHERE s.PhysicalQty > 0;
```

无匹配仓库的 s 列是 NULL，WHERE 过滤掉，效果退化成 inner join。

不是说右表条件永远放 ON。先定义你要过滤“匹配候选”还是“最终结果”。

## 9. SELF JOIN

自连接适合层级、前后记录或重复关系：

```sql
SELECT child.LocationCd, parent.LocationCd AS ParentLocation
FROM Location child
LEFT JOIN Location parent ON parent.Id = child.ParentId;
```

层级不定深时考虑递归 CTE，而不是手写固定五层 join。

## 10. CROSS JOIN 与 APPLY

Cross join 生成笛卡尔积，行数为 m×n，必须有明确目的。`CROSS APPLY`/`OUTER APPLY` 可以对左表每行执行相关表表达式，常用于每组 Top 1：

```sql
SELECT p.ProductCd, latest.TxnDateTime, latest.Qty
FROM Product p
OUTER APPLY (
    SELECT TOP (1) t.TxnDateTime, t.Qty
    FROM StockTransaction t
    WHERE t.ProductCd = p.ProductCd
    ORDER BY t.TxnDateTime DESC, t.Id DESC
) latest;
```

## 11. EXISTS

```sql
SELECT s.*
FROM Stock s
WHERE EXISTS (
    SELECT 1
    FROM StockTransaction t
    WHERE t.ProductCd = s.ProductCd
      AND t.TxnDateTime >= @since
);
```

EXISTS 表达“至少有一条”，避免 JOIN 为了存在判断而放大结果后再 DISTINCT。

## 12. UNION 与 UNION ALL

- `UNION ALL` 直接合并，保留重复。
- `UNION` 还要去重，通常需要排序或哈希。

默认使用符合业务语义的版本；若来源天然不重叠，`UNION ALL` 更直接。

## 13. 数据修改

### UPDATE 前先 SELECT

```sql
BEGIN TRAN;

SELECT * FROM Stock WHERE WarehouseCd = @warehouse;

UPDATE Stock
SET AvailableQty = PhysicalQty - AllocatedQty
WHERE WarehouseCd = @warehouse;

SELECT @@ROWCOUNT AS Affected;

ROLLBACK; -- 演练确认后再决定提交
```

生产操作还要：备份、审批、影响行上限、锁评估、审计和回滚计划。

### DELETE 与软删除

软删除保留行并加 `IsDeleted`，方便恢复/审计，但所有唯一约束、查询和容量治理都变复杂。不能只说“软删更安全”。

## 14. 参数化与注入

错误：

```csharp
var sql = "SELECT * FROM Stock WHERE ProductCd = '" + productCd + "'";
```

正确：

```csharp
connection.QueryAsync<StockRow>(
    "SELECT ... WHERE TenantId=@tenantId AND ProductCd=@productCd",
    new { tenantId, productCd });
```

参数化同时解决注入和计划复用的大部分问题。动态列名/排序方向不能用普通参数，需要白名单映射。

## 15. 分页

```sql
ORDER BY WarehouseCd, LocationCd, ProductCd, LotNo, Id
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
```

要求稳定、唯一排序。深分页会扫描/跳过大量行，可用 keyset：记录上一页最后一个复合键，下一页使用 `>` 条件继续。

## 高频陷阱

1. NULL = NULL 为 TRUE。
2. LEFT JOIN 后右表条件放哪里都一样。
3. JOIN 后加 DISTINCT 是通用去重方案。
4. 没有 ORDER BY 也按主键返回。
5. `NOT IN` 与 `NOT EXISTS` 在含 NULL 时完全等价。
6. 参数化可以参数化表名和列名。

## 闭卷题

1. 写所有仓库及正库存，保留空仓。
2. 查从未发生库存交易的产品。
3. 查 OwnerCd 不是 C001 或未知的库存。
4. 写稳定分页。
5. 解释 JOIN 结果行数为什么放大。

