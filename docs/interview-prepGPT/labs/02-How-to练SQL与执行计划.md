# How to 建立 SQL 手写与执行计划练习

你将使用独立练习表验证 NULL、LEFT JOIN、窗口函数和索引计划。只在本地开发/临时数据库执行。

## 前置

- SQL Server 或可连接的本地 CP6 开发数据库。
- SSMS、Azure Data Studio 或 `sqlcmd`。
- 确认不是生产库。

## 步骤 1：创建隔离练习库/表

推荐创建独立数据库 `CP6_InterviewLab`。若无建库权限，使用明确的临时表 `#StockLab`。

```sql
CREATE TABLE #StockLab (
    Id int IDENTITY PRIMARY KEY,
    WarehouseCd varchar(10) NOT NULL,
    ProductCd varchar(20) NOT NULL,
    OwnerCd varchar(20) NULL,
    PhysicalQty decimal(21,8) NOT NULL,
    AllocatedQty decimal(21,8) NOT NULL,
    TxnAt datetime2 NOT NULL
);

INSERT INTO #StockLab
    (WarehouseCd, ProductCd, OwnerCd, PhysicalQty, AllocatedQty, TxnAt)
VALUES
    ('W1','P1',NULL, 10,2,'2026-07-22T09:00:00'),
    ('W1','P1','C001',8,1,'2026-07-22T09:00:00'),
    ('W1','P2','C002',0,0,'2026-07-22T10:00:00'),
    ('W2','P1','',5,5,'2026-07-22T11:00:00');
```

立即验证：

```sql
SELECT * FROM #StockLab ORDER BY Id;
```

## 步骤 2：NULL 真值实验

依次运行：

```sql
SELECT * FROM #StockLab WHERE OwnerCd <> 'C001';
SELECT * FROM #StockLab WHERE OwnerCd <> 'C001' OR OwnerCd IS NULL;
SELECT * FROM #StockLab WHERE OwnerCd NOT IN ('C001', NULL);
```

写下每次返回的 Id，并解释 UNKNOWN。

## 步骤 3：每组最新

```sql
WITH Ranked AS (
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY WarehouseCd, ProductCd
               ORDER BY TxnAt DESC, Id DESC
           ) AS rn
    FROM #StockLab
)
SELECT * FROM Ranked WHERE rn = 1;
```

去掉 `Id DESC` 重跑多次，说明 SQL 语义为何不保证并列顺序，即使这次输出看似稳定。

## 步骤 4：条件聚合

```sql
SELECT WarehouseCd,
       SUM(PhysicalQty) AS PhysicalQty,
       SUM(AllocatedQty) AS AllocatedQty,
       SUM(PhysicalQty - AllocatedQty) AS AvailableQty,
       SUM(CASE WHEN OwnerCd IS NULL OR OwnerCd = ''
                THEN PhysicalQty ELSE 0 END) AS SelfLikeQty
FROM #StockLab
GROUP BY WarehouseCd;
```

手工计算结果再对照。

## 步骤 5：实际执行计划与 IO

临时表数据太少时计划差异不明显。切换到专用练习库 `CP6_InterviewLab`，先确认当前数据库，再创建不会覆盖既有对象的持久练习表：

```sql
SELECT DB_NAME() AS CurrentDatabase;

IF OBJECT_ID('dbo.StockLab', 'U') IS NOT NULL
    THROW 50001, 'dbo.StockLab 已存在；请换表名或先人工确认它确实是练习表。', 1;

CREATE TABLE dbo.StockLab (
    Id int IDENTITY PRIMARY KEY,
    WarehouseCd varchar(10) NOT NULL,
    ProductCd varchar(20) NOT NULL,
    PhysicalQty decimal(21,8) NOT NULL,
    TxnAt datetime2 NOT NULL
);

WITH N AS (
    SELECT TOP (50000)
           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
INSERT dbo.StockLab (WarehouseCd, ProductCd, PhysicalQty, TxnAt)
SELECT CONCAT('W', (n % 20) + 1),
       CONCAT('P', (n % 2000) + 1),
       CAST((n % 10000) / 10.0 AS decimal(21,8)),
       DATEADD(minute, n, CAST('2026-01-01' AS datetime2))
FROM N;
```

`OBJECT_ID` 守卫的目的，是让复制执行也不会悄悄覆盖同名业务表。插入完成后运行：

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

SELECT Id, ProductCd, PhysicalQty
FROM dbo.StockLab
WHERE WarehouseCd = 'W1' AND ProductCd = 'P123';
```

记录 logical reads 和实际计划，再创建候选索引：

```sql
CREATE INDEX IX_StockLab_Warehouse_Product
ON dbo.StockLab(WarehouseCd, ProductCd)
INCLUDE(PhysicalQty);
```

重跑并比较。实验完在确认目标为练习库后删除索引/数据库。

## 步骤 6：日期可搜索性

比较：

```sql
WHERE CAST(TxnAt AS date) = '2026-07-22'
```

与：

```sql
WHERE TxnAt >= '2026-07-22'
  AND TxnAt <  '2026-07-23'
```

使用实际计划和 IO 解释，而不是只背“函数不能走索引”。

## 验证

- [ ] NULL 三个查询结果已记录。
- [ ] 每组最新有确定 tiebreaker。
- [ ] 条件聚合手算一致。
- [ ] 有加索引前后实际计划/IO 截图或文本。
- [ ] 能说出新索引的写入代价。

## 排错

- 临时表不存在：它只在当前会话存在，重新创建。
- 看不到实际计划：在客户端启用 Include Actual Execution Plan。
- 计划仍扫描：数据太少、统计/选择性不支持，scan 可能正确；不要强制证明预设答案。
- 无权限建索引：使用个人练习数据库或请 DBA 提供只读计划环境，不在生产临时尝试。
