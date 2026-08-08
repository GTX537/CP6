# 05 · SQL Server、Dapper 与生产排障

## 1. EF、Dapper、存储过程如何选

| 方式 | 强项 | 代价 |
|---|---|---|
| EF Core | 模型、跟踪、迁移、普通 CRUD | 复杂 SQL 可读性/控制下降 |
| Dapper | 精确 SQL、轻量映射、报表 | 手工 SQL、租户/软删/审计需自律 |
| 存储过程 | DB 内复杂批处理、权限边界、复用执行计划 | 版本部署、测试、跨库迁移和业务分散 |

成熟系统可以混用，但要定义边界。CP6 以 EF 为主，复杂报表/特定查询使用 Dapper。

## 2. Dapper 安全模板

```csharp
const string sql = """
SELECT WarehouseCd, ProductCd, SUM(AvailableQty) AS AvailableQty
FROM T_Stock
WHERE TenantId = @TenantId
  AND IsDeleted = 0
  AND (@WarehouseCd IS NULL OR WarehouseCd = @WarehouseCd)
GROUP BY WarehouseCd, ProductCd;
""";

var rows = await connection.QueryAsync<StockSummary>(
    new CommandDefinition(
        sql,
        new { TenantId = tenantId, WarehouseCd = warehouseCd },
        transaction: tx,
        commandTimeout: 30,
        cancellationToken: ct));
```

检查：参数化、TenantId、软删、超时、取消、事务、列与类型映射。

## 3. 动态 SQL

值用参数；表名、列名、排序方向用白名单：

```csharp
var orderSql = sort switch
{
    "qty" => "AvailableQty DESC",
    "product" => "ProductCd ASC",
    _ => "WarehouseCd ASC, ProductCd ASC"
};
```

不要把客户端字符串直接插入 ORDER BY。`QUOTENAME` 也不能替代业务白名单。

## 4. 存储过程的事务和错误

```sql
CREATE OR ALTER PROCEDURE dbo.ApplyStock
    @TenantId uniqueidentifier,
    @StockId uniqueidentifier,
    @Qty decimal(21,8)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;
        -- 条件更新 + 流水
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
```

`XACT_ABORT ON` 让许多运行时错误自动终止事务，但仍需 TRY/CATCH 处理、记录和 rethrow。动态 SQL 内外作用域也要测试。

## 5. 慢查询排查顺序

```text
先确认范围
→ 是单 SQL、数据库整体还是应用等待？
→ 获取 Query Store/实际计划/等待类型
→ 看 IO、CPU、duration、rows
→ 比估算与实际
→ 找阻塞、spill、lookup、排序、参数敏感
→ 最小变更验证
→ 监控回归
```

不要一上来“加索引”或“重启数据库”。

## 6. 常见等待类别

| 类别 | 可能方向 |
|---|---|
| LCK_* | 阻塞、长事务、索引不足扩大锁范围 |
| PAGEIOLATCH_* | 从存储读页，缓存未命中/IO 压力 |
| WRITELOG | 日志刷盘、事务写入量 |
| CXPACKET/CXCONSUMER | 并行计划，不单独等于问题 |
| RESOURCE_SEMAPHORE | 内存授予等待 |
| ASYNC_NETWORK_IO | 客户端消费慢或网络，不只是数据库慢 |

等待是线索，不是直接根因。

## 7. 阻塞排查

找 head blocker、事务开始时间、正在执行 SQL、锁资源。处理优先级：

1. 评估业务影响。
2. 联系/确认长事务来源。
3. 必要时终止 blocker，但先理解回滚成本。
4. 修复事务范围、索引或访问顺序。

杀 session 是止血，不是根治。

## 8. 备份不是恢复能力

完整恢复链可能包括 Full、Differential、Log。RPO 是可接受数据损失，RTO 是可接受恢复时间。

必须定期做恢复演练：

- 备份文件可读。
- 加密密钥/凭证可用。
- 能恢复到新实例。
- 应用能连接并做关键查询。
- 时间满足 RTO。

只看到作业成功不能证明可恢复。

## 9. 日志文件与磁盘

事务日志增长原因可能是：

- Full recovery 下未做 log backup。
- 长事务阻止截断。
- AG/复制延迟。
- 大批量更新。

反复 shrink 不是常规治理，会造成增长抖动和碎片。正确做法是定位不能复用原因、规划初始大小/增长单位、监控空间并处理根因。

## 10. 容量治理

至少监控：

- 数据/日志文件使用率和增长速率。
- 宿主/容器磁盘。
- tempdb 使用。
- 大表和索引增长。
- 备份占用与保留。
- 应用日志/EF SQL 日志。

阈值不只看百分比。1TB 盘剩 10% 与 10GB 盘剩 10% 的可用时间不同，应结合增长速率预测“距耗尽时间”。

## 11. 磁盘满事故的排查框架

症状可能跨层传播：

```text
宿主磁盘满
→ WSL/容器虚拟盘无法扩展
→ SQL 写日志/恢复失败
→ API 连接错误
→ 前端超时/500
```

排查按证据：

1. 用户症状与开始时间。
2. API 错误率/日志。
3. DB 状态和磁盘。
4. 容器/WSL 状态。
5. 最大增长目录和增长来源。

止血：释放安全可回收空间、恢复关键服务。治本：容量告警、保留策略、日志级别、备份迁移、扩容和演练。

## 12. SQL 日志与隐私

开发可记录 SQL 和参数，生产需谨慎：

- 参数可能含 PII/密钥。
- Debug 日志量巨大。
- 日志写入本身能成为瓶颈或填满磁盘。

默认结构化摘要、慢查询采样、脱敏和受控临时提升日志级别。

## 13. Docker 中的 SQL Server

要区分：

- 容器可运行。
- SQL Server 进程就绪。
- 数据库 ONLINE。
- 应用 schema 已迁移。
- 业务健康检查通过。

容器 `healthy` 不一定等于某个数据库已完成恢复。健康检查应覆盖真实依赖层级，但避免每次都执行昂贵业务查询。

## 14. 部署与迁移

生产数据库变更先兼容再切换：

1. Expand：加可空列/新表/新索引。
2. 应用双读/双写或回填。
3. 观察并切换。
4. Contract：后续版本删除旧结构。

一次部署直接 rename/drop 列会让旧应用实例在滚动发布中失败。

## 15. 故障回答模板

> 我先确认影响面和开始时间，优先止血；然后按应用、数据库、宿主三层收集证据，不把相关性当根因。定位后做最小可回滚修复，并用业务请求、指标和日志三方验证。最后补监控、容量阈值、恢复演练或测试，让同类问题更早发现、自动阻断或更快恢复。

## 高频陷阱

1. Dapper 自动应用 EF 全局过滤器。
2. 存储过程天然防 SQL 注入。
3. CPU 高就一定缺索引。
4. Scan 一定是慢查询根因。
5. 有备份文件就等于能恢复。
6. 磁盘满后删日志即可，其他不用做。

