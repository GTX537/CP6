# 02 · 聚合、CTE 与窗口函数

## 1. GROUP BY 的粒度

分组前先写一句“结果一行代表什么”。

```sql
SELECT
    WarehouseCd,
    ProductCd,
    SUM(PhysicalQty) AS PhysicalQty,
    SUM(AllocatedQty) AS AllocatedQty,
    SUM(AvailableQty) AS AvailableQty
FROM Stock
GROUP BY WarehouseCd, ProductCd;
```

结果一行代表“一个仓库中的一个产品”。SELECT 中非聚合列必须属于分组键。

## 2. WHERE 与 HAVING

- WHERE 在分组前过滤明细。
- HAVING 在聚合后过滤组。

```sql
SELECT WarehouseCd, SUM(AvailableQty) AS Qty
FROM Stock
WHERE IsDeleted = 0
GROUP BY WarehouseCd
HAVING SUM(AvailableQty) < 0;
```

能在 WHERE 过滤的条件不要都堆到 HAVING，因为先减少明细通常更有效，也更符合语义。

## 3. 聚合中的 NULL

- `COUNT(*)` 计行。
- `COUNT(column)` 忽略 NULL。
- `SUM/AVG/MIN/MAX` 通常忽略 NULL。
- 空集 SUM 可能返回 NULL。

用 `COALESCE(SUM(...), 0)` 前先确认“无数据”业务上是否真等于 0；财务中“未知”和“0”可能不同。

## 4. 条件聚合

```sql
SELECT
    WarehouseCd,
    SUM(CASE WHEN QcStatus = 'PASSED' THEN AvailableQty ELSE 0 END) AS PassedQty,
    SUM(CASE WHEN QcStatus IN ('FAILED','HOLD') THEN AvailableQty ELSE 0 END) AS BlockedQty
FROM Stock
GROUP BY WarehouseCd;
```

这是固定列透视的常用写法。动态状态列才考虑动态 PIVOT，且必须白名单防注入。

## 5. CTE

CTE 是一条语句内的具名查询表达式，主要提升可读性和支持递归，不自动物化或缓存。

```sql
WITH ActiveStock AS (
    SELECT *
    FROM Stock
    WHERE IsDeleted = 0 AND PhysicalQty <> 0
)
SELECT WarehouseCd, SUM(PhysicalQty)
FROM ActiveStock
GROUP BY WarehouseCd;
```

若同一复杂结果要重复使用、需要索引或跨语句，可考虑临时表，而不是假设 CTE 只算一次。

## 6. 递归 CTE 展开 BOM

假设 `Bom(ParentProductCd, ComponentCd, QtyPer)`：

```sql
WITH BomTree AS (
    SELECT
        ParentProductCd,
        ComponentCd,
        CAST(QtyPer AS decimal(38,10)) AS TotalQty,
        1 AS Lvl,
        CAST('>' + ParentProductCd + '>' + ComponentCd + '>' AS varchar(max)) AS Path
    FROM Bom
    WHERE ParentProductCd = @root

    UNION ALL

    SELECT
        b.ParentProductCd,
        b.ComponentCd,
        CAST(t.TotalQty * b.QtyPer AS decimal(38,10)),
        t.Lvl + 1,
        t.Path + b.ComponentCd + '>'
    FROM Bom b
    JOIN BomTree t ON b.ParentProductCd = t.ComponentCd
    WHERE t.Path NOT LIKE '%>' + b.ComponentCd + '>%'
)
SELECT * FROM BomTree
OPTION (MAXRECURSION 100);
```

面试要主动提：

- 防环。
- 最大深度。
- 数量乘法精度/溢出。
- 同一组件经不同路径汇总。
- 生效日期和 BOM 版本。

## 7. 窗口函数不折叠行

GROUP BY 把多行变少；窗口函数保留明细并计算“同组视角”的值。

基本结构：

```sql
function(...) OVER (
    PARTITION BY ...
    ORDER BY ...
    ROWS BETWEEN ...
)
```

## 8. 排名函数

| 函数 | 并列 | 序号空洞 | 用途 |
|---|---|---|---|
| ROW_NUMBER | 不并列，强制唯一序号 | 无 | 每组取一条、分页 |
| RANK | 并列同名次 | 有 | 比赛名次 |
| DENSE_RANK | 并列同名次 | 无 | 稠密等级 |
| NTILE(n) | 分桶 | 不适用 | 分位分组 |

## 9. 每组取最新一条

```sql
WITH Ranked AS (
    SELECT
        t.*,
        ROW_NUMBER() OVER (
            PARTITION BY TenantId, WarehouseCd, LocationCd, ProductCd, LotNo
            ORDER BY TxnDateTime DESC, Id DESC
        ) AS rn
    FROM StockTransaction t
)
SELECT *
FROM Ranked
WHERE rn = 1;
```

为什么要 `Id DESC`：时间可能相同。若 Id 是随机 GUID，它不代表业务先后，只能提供确定性；更好的业务顺序可能是单调流水号或 rowversion。先定义“最新”的真实含义。

## 10. 每组 Top N

把上题 `rn <= 3` 即每个组合键前三条。若要包含并列第三名，用 RANK/DENSE_RANK 并定义并列规则。

## 11. 运行余额

```sql
SELECT
    ProductCd,
    TxnDateTime,
    Qty,
    SUM(Qty) OVER (
        PARTITION BY ProductCd
        ORDER BY TxnDateTime, Id
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningQty
FROM StockTransaction;
```

显式写 `ROWS`，避免默认 frame 在重复排序值时带来意外同组聚合语义。

## 12. LAG / LEAD

```sql
SELECT
    MachineCd,
    StatusAt,
    Status,
    LAG(Status) OVER (PARTITION BY MachineCd ORDER BY StatusAt, Id) AS PrevStatus
FROM MachineStatusHistory;
```

可用于状态变化检测、相邻时间差、价格变动。边界是第一行没有前值，返回 NULL。

## 13. 去重保留最新

先在事务/备份下预览：

```sql
WITH D AS (
    SELECT Id,
           ROW_NUMBER() OVER (
               PARTITION BY TenantId, RoleId, MenuId
               ORDER BY CreateDate DESC, Id DESC
           ) AS rn
    FROM Sys_RoleMenu
)
SELECT * FROM D WHERE rn > 1;
```

确认后再 DELETE。删除重复只是清理历史，最终必须加唯一约束防复发。

## 14. 月度透视

```sql
SELECT
    ProductCd,
    SUM(CASE WHEN MONTH(TxnDateTime)=1 THEN Qty ELSE 0 END) AS M01,
    SUM(CASE WHEN MONTH(TxnDateTime)=2 THEN Qty ELSE 0 END) AS M02
FROM StockTransaction
WHERE TxnDateTime >= @yearStart
  AND TxnDateTime <  @nextYearStart
GROUP BY ProductCd;
```

WHERE 使用范围保证可搜索性；SELECT 中 MONTH 只用于已筛选结果的分类。

## 15. 关系除法：全部满足

“找齐套的工单：每个所需物料都有足够库存”通常用 NOT EXISTS 反例：

```sql
SELECT wo.WorkOrderNo
FROM WorkOrder wo
WHERE NOT EXISTS (
    SELECT 1
    FROM WorkOrderMaterial m
    WHERE m.WorkOrderNo = wo.WorkOrderNo
      AND NOT EXISTS (
          SELECT 1
          FROM Stock s
          WHERE s.ProductCd = m.MaterialCd
          GROUP BY s.ProductCd
          HAVING SUM(s.AvailableQty) >= m.RequiredQty
      )
);
```

生产实现还要考虑仓库、批次、QC、预留和单位换算。

## 16. 性能思考

窗口函数常需要按 partition/order 排序。合适索引可能减少排序：

```text
(TenantId, ProductCd, TxnDateTime DESC, Id DESC)
INCLUDE (Qty, TxnType, WarehouseCd, LocationCd)
```

但宽索引增加写成本，必须依据真实查询和执行计划。

## 高频陷阱

1. CTE 会自动缓存结果。
2. GROUP BY 和窗口函数都会减少行数。
3. ROW_NUMBER 遇相同时间也有稳定结果。
4. 默认窗口 frame 永远等同 ROWS。
5. 去重脚本执行后不需要唯一索引。
6. 递归 CTE 只要 MAXRECURSION 就能防业务环。

## 闭卷题

1. 每个产品最新交易，处理并列时间。
2. 每仓库库存金额和冻结金额条件聚合。
3. 每个产品交易量 Top 3 日期。
4. 展开 BOM 并汇总相同底层材料。
5. 计算库存流水运行余额并找首次变负时点。

