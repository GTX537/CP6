# Day 2 · 第 3 章　SQL Server 生产实战——存储过程、运维、排障与 EF 性能落地

> **本章定位**：这一章是你"5 年经验感"的主战场。
>
> 面试官问「你会写 SQL」——初级工程师答"会 SELECT / JOIN / GROUP BY"；
> 面试官问「生产库磁盘满了你怎么办」「某个页面查询超时你怎么查」「你讲一次线上故障」——
> 只有真正在生产里被烧过的人才答得出。**这章教的就是后者。**
>
> JD 原文要求「定位并修复生产系统中发现的故障，且能推进改善措施」——注意这句话有两层：
> **定位并修复**（止血 + 治标）和**推进改善措施**（治本 + 复盘）。面试官会用开放题反复戳你这两层。
> 本章所有标本都锚定 `C:\CP6` 真实生产项目（.NET 8 制造业 ERP/MES/WMS + SQL Server 2022 + Docker），
> 包括真实的存储过程迁移、真实的 Dapper 报表服务、真实的备份脚本、真实的容量监控脚本，
> 以及一次**真实发生过的、磁盘满导致全栈停机的生产事故**——这是你面试时"讲一次故障"的金牌素材。
>
> **本章的阅读方式**：每一节固定结构 = 「概念 → CP6 真实标本（标路径）→ 解析 → 生产事故/坑 → 面试问答」。
> 概念部分把你当新手教；标本部分你要能对着真实代码/脚本讲出所以然；事故/坑是别人踩过的血；
> 面试问答是把知识"翻译"成面试语言。

---

## 目录

1. [存储过程与视图](#1-存储过程与视图)
2. [触发器：语法、慎用理由、CP6 的 SaveChanges 替代方案](#2-触发器)
3. [动态 SQL 与注入防护](#3-动态-sql-与注入防护)
4. [Dapper 实战：为什么 CP6 报表不用 EF](#4-dapper-实战)
5. [备份与恢复（生产必修）](#5-备份与恢复)
6. [容量与增长治理 + 磁盘满全栈停机事故还原](#6-容量与增长治理)
7. [慢查询定位方法论](#7-慢查询定位方法论)
8. [EF Core 性能落地清单（DB 视角复盘）](#8-ef-core-性能落地清单)
9. [数据库部署与版本化](#9-数据库部署与版本化)
10. [Docker 里的 SQL Server](#10-docker-里的-sql-server)
11. [面试排障题模拟（三道开放题标准框架）](#11-面试排障题模拟)
12. [章末：面试题 15 问 + 自测清单 + 动手练习](#12-章末)

---

<a name="1-存储过程与视图"></a>
## 1. 存储过程与视图

### 1.1 概念：存储过程是什么

**存储过程（Stored Procedure，简称 SP）** 是一段**预先编译、存在数据库里、有名字、可以带参数**的 SQL 程序。
你可以把它想成"数据库里的一个函数"：调用方（应用程序）传参数进去，它在数据库引擎内部跑一堆 SQL，返回结果集或输出值。

对比一下你熟悉的应用层代码：

| 维度 | 应用层 SQL（EF/Dapper 拼的 SQL） | 存储过程 |
|---|---|---|
| 代码住在哪 | C# 项目里，随应用一起部署 | 数据库里（`sys.procedures`），随库走 |
| 编译时机 | 每次运行时由 EF 生成、DB 首次见到时编译计划 | 创建时就在库里，计划缓存复用 |
| 网络往返 | 复杂逻辑可能多次往返 | 一次调用，逻辑全在 DB 侧跑完 |
| 版本管理 | Git 里（和代码一起 review） | 要靠迁移脚本纳入版本控制（否则容易"库里改了没人知道"）|
| 谁能改 | 改代码要走 PR + 部署 | DBA 可以直接在库里改（是优点也是坑）|

### 1.2 CREATE PROCEDURE 语法骨架

```sql
CREATE PROCEDURE dbo.usp_DoSomething      -- dbo 是 schema；usp_ 是"user stored procedure"命名习惯
    @Param1 int = 30,                     -- 带默认值的输入参数
    @Param2 nvarchar(50),                 -- 无默认值 = 必填
    @OutTotal int OUTPUT                  -- 输出参数（可选）
AS
BEGIN
    SET NOCOUNT ON;                       -- 关掉 "N rows affected" 消息，减少网络噪声（几乎必写）

    -- 变量声明
    DECLARE @today date = CAST(GETDATE() AS date);

    -- 逻辑 …
    SELECT @OutTotal = COUNT(*) FROM SomeTable WHERE X = @Param1;

    -- 返回结果集（调用方拿到的是这个 SELECT）
    SELECT Col1, Col2 FROM SomeTable WHERE Y = @Param2;
END
```

三种"返回"要分清：
- **结果集**（Result Set）：最后一个（或多个）`SELECT` 语句返回的表，是最常用的返回方式。
- **输出参数**（`OUTPUT`）：单个标量值，通过参数带回去。
- **返回码**（`RETURN n`）：只能返回 int，习惯用于表示状态码（0=成功），**不要用来传数据**。

### 1.3 何时用存储过程 vs 应用层代码（两大阵营论据）

这是面试高频争议题，要能讲出**两边的道理**，再给出"现代 ORM 时代"的定位。

**"用存储过程"阵营的论据：**
1. **性能**：复杂聚合/多表 JOIN 在 DB 侧一次跑完，避免把大量中间数据搬到应用层。计划被缓存复用。
2. **减少网络往返**：一次 `EXEC` 干完，而不是应用层来回捞数据再算。
3. **封装 + 安全**：只授予 `EXECUTE` 权限，不直接暴露底表。SQL 注入面小（参数强类型）。
4. **DBA 可运维**：不重新部署应用就能改查询逻辑、加 hint、调优。

**"用应用层代码"阵营的论据：**
1. **可测试/可 debug**：C# 代码有单元测试、断点、类型系统；SP 的调试体验差得多。
2. **版本控制自然**：逻辑和业务代码在一个 Git 仓库里 review、diff、回滚。
3. **可移植**：换数据库（SQL Server → PostgreSQL）时，SP 是 T-SQL 方言，要重写；ORM 抽象掉了方言。
4. **业务逻辑不该散落在两个地方**：一半在 C#、一半在 SP，维护时要两处找，容易漏。
5. **团队技能**：多数后端团队 C# 强、T-SQL 弱，业务逻辑塞 SP 里会变成"没人敢碰的黑盒"。

**现代 ORM 时代的定位（面试标准答案）：**

> "默认用 ORM（EF Core）写业务 CRUD 和大部分查询——**因为可测试、可版本控制、可移植**。
> 但对**重聚合报表 / 仪表盘 / 复杂统计**这类 EF 生成 SQL 不理想、或者需要 DB 侧计划优化的场景，
> 我会**下沉到存储过程 + Dapper 调用**。判断标准是：这段逻辑是不是'数据密集、往返敏感、且相对稳定'。
> 稳定很关键——SP 改动成本高，频繁变的业务逻辑不适合放 SP。"

CP6 项目就是**完全按这个原则**做的：99% 的业务走 EF Core，唯独 **MES 仪表盘**（重聚合、频繁刷新、逻辑稳定）
专门下沉到了存储过程 + Dapper。下面就精读这个真实标本。

### 1.4 CP6 真实标本：MES 仪表盘存储过程

> **标本路径**：`C:\CP6\CP6.Core\Migrations\20260518115050_AddMesStoredProcedures.cs`

这是一个 **EF Core 迁移文件**。注意关键点：**存储过程本身也是通过 EF 迁移来版本化管理的**——
这就解决了前面说的"SP 版本控制难"的问题。SP 的 SQL 用 `migrationBuilder.Sql(@"...")` 内联在 `Up()` 里，
`Down()` 里对应 `DROP`。这样 SP 的每一次变更都进 Git、进迁移链、随部署自动应用。**这是一个非常成熟的做法，面试可以主动讲。**

#### 标本第一段：先加索引（性能调优的前置）

```csharp
migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_ProductionResult_CreateDate_IsDeleted')
    CREATE INDEX IX_T_ProductionResult_CreateDate_IsDeleted
        ON [T_ProductionResult] ([CreateDate], [IsDeleted]) INCLUDE ([GoodQty], [DefectQty], [MachineCd]);
");
```

**逐句解析**：
- `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ...)`：**幂等守卫**。迁移可能被重跑（不同环境、重建库），
  先查系统视图 `sys.indexes` 里有没有这个索引，没有才建。这是生产迁移的铁律——**一切 DDL 都要幂等**。
- `CREATE INDEX ... ON [T_ProductionResult] ([CreateDate], [IsDeleted])`：在生产实绩表上建复合索引。
  为什么是 `CreateDate` 打头？因为仪表盘查询几乎全是"查今天/近 N 天的实绩"，按日期范围过滤，日期列做索引**键列**最有效。
- `INCLUDE ([GoodQty], [DefectQty], [MachineCd])`：**覆盖索引（Covering Index）** 的精髓。
  把查询要 SELECT 的列（良品数、不良数、机台）"塞进"索引的叶子层。这样引擎光扫索引就能拿到全部需要的数据，
  **不用回表**（不用再根据主键去聚集索引里捞行）。这是仪表盘类"读多、聚合、列固定"查询的经典优化。

> **面试点**：能讲清"键列 vs INCLUDE 列的区别"是加分项。键列用于**定位和排序**（WHERE/ORDER BY/JOIN 条件），
> INCLUDE 列只是**顺带存下来避免回表**（SELECT 列表里但不参与过滤）。把 SELECT 列全塞键列会让索引又大又慢、维护成本高。

#### 标本第二段：`usp_GetMesDashboardSummary`（今日 KPI 一次往返）

```sql
CREATE PROCEDURE dbo.usp_GetMesDashboardSummary
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @today date = CAST(GETDATE() AS date);
    DECLARE @tomorrow date = DATEADD(day, 1, @today);

    DECLARE @inProgress int, @completed int, @good decimal(21,8), @defect decimal(21,8), @delayed int;

    SELECT @inProgress = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0 AND [Status] = 3;                          -- Status=3 = 进行中

    SELECT @completed = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0
      AND [Status] IN (4, 6)                                        -- 4/6 = 完成态
      AND ActualEndDate >= @today AND ActualEndDate < @tomorrow;    -- 今天完成的

    SELECT
        @good   = ISNULL(SUM(GoodQty), 0),
        @defect = ISNULL(SUM(DefectQty), 0)
    FROM T_ProductionResult
    WHERE IsDeleted = 0
      AND CreateDate >= @today AND CreateDate < @tomorrow;

    SELECT @delayed = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0
      AND PlanEndDate < @today
      AND [Status] NOT IN (4, 6, 9);                                -- 计划结束日已过，但还没完成/取消

    SELECT
        InProgressCount = @inProgress,
        CompletedCount  = @completed,
        TotalGoodQty    = @good,
        TotalDefectQty  = @defect,
        DefectRate      = CASE WHEN (@good + @defect) > 0
                               THEN CAST(@defect / (@good + @defect) * 100 AS decimal(8,2))
                               ELSE 0 END,
        DelayedCount    = @delayed;
END
```

**为什么这个场景要用存储过程？** 逐条对照前面的判断标准：
- **数据密集**：一个仪表盘要 4 个不同的聚合（进行中/完成/良品不良/延误）。用 EF 就是 4 次 `CountAsync/SumAsync`，
  4 次网络往返、4 个独立查询计划。SP 里一次 `EXEC` 全算完，**一次往返**返回一行结果。
- **逻辑稳定**：仪表盘的 KPI 定义（哪个 Status 算进行中）很少变。稳定 = 适合 SP。
- **计划优化**：配合前面那个覆盖索引，`WHERE CreateDate >= @today AND CreateDate < @tomorrow` 走索引 seek，飞快。

**几个 T-SQL 生产细节，面试能讲出来就显功底：**
- **日期范围为什么写 `>= @today AND < @tomorrow` 而不是 `= @today` 或 `BETWEEN`？**
  因为 `CreateDate` 是 `datetime`（带时分秒），`= @today` 只能匹配到零点整那一瞬。
  用**半开区间** `[今天 00:00, 明天 00:00)` 才能覆盖今天全天，且这种写法**能用上索引**（sargable，
  即"Search ARGument ABLE"）。反面教材是 `WHERE CAST(CreateDate AS date) = @today`——对列做了函数，
  索引失效、全表扫。**"不要在 WHERE 的列上套函数"是慢查询的头号杀手**，后面第 7 节还会讲。
- `ISNULL(SUM(GoodQty), 0)`：`SUM` 对空集返回 `NULL`，用 `ISNULL` 兜底成 0，避免上层拿到 null。
- `DefectRate` 里的 `CASE WHEN (@good+@defect) > 0`：**防除零**。分母可能是 0（今天还没生产），必须挡住。
- `SELECT InProgressCount = @inProgress`：这个 `别名 = 变量` 写法给结果列命名，方便 Dapper 按列名映射到 DTO 属性。

#### 标本第三段：`usp_GetMesDailyTrend @Days`（参数化 + 递归 CTE 补全日期）

```sql
CREATE PROCEDURE dbo.usp_GetMesDailyTrend
    @Days int = 30                        -- ★ 参数化：默认近 30 天，调用方可传 7/90
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @to date = DATEADD(day, 1, CAST(GETDATE() AS date));
    DECLARE @from date = DATEADD(day, -@Days + 1, CAST(GETDATE() AS date));

    ;WITH Dates AS (                       -- 递归 CTE：生成连续日期序列
        SELECT @from AS d
        UNION ALL
        SELECT DATEADD(day, 1, d) FROM Dates WHERE d < DATEADD(day, -1, @to)
    ),
    Agg AS (                               -- 按日聚合实绩
        SELECT CAST(CreateDate AS date) AS d, SUM(GoodQty) AS GoodQty, SUM(DefectQty) AS DefectQty
        FROM T_ProductionResult
        WHERE IsDeleted = 0 AND CreateDate >= @from AND CreateDate < @to
        GROUP BY CAST(CreateDate AS date)
    )
    SELECT
        CONVERT(varchar(10), D.d, 23) AS [Date],   -- 格式 23 = yyyy-MM-dd
        ISNULL(A.GoodQty, 0)   AS GoodQty,
        ISNULL(A.DefectQty, 0) AS DefectQty
    FROM Dates D
    LEFT JOIN Agg A ON D.d = A.d           -- ★ 左连日期骨架 → 没生产的日子也出 0，不断档
    ORDER BY D.d
    OPTION (MAXRECURSION 366);             -- 递归上限保护（默认 100，这里放宽到一年）
END
```

**这个 SP 的精妙点（面试可讲）：**
- **`@Days` 参数化**：前端切换"7 天 / 30 天 / 90 天"只是传不同参数，同一个编译好的计划复用。
- **递归 CTE 造日期骨架**：折线图最怕"某天没生产 → 那天在结果里直接消失 → 折线断档/错位"。
  这里先用递归 CTE 生成**连续的日期序列**，再 `LEFT JOIN` 聚合结果，没数据的日子补 0。
  这是"报表要连续、DB 侧补全比前端补全干净"的典型手法。
- **`OPTION (MAXRECURSION 366)`**：递归 CTE 默认最多递归 100 层，超过报错。这里查询可能要 90+ 天甚至一年，
  显式放宽到 366（一年上限，同时防死递归）。**这是防御性编程——既满足业务，又设了天花板。**

#### CP6 真实标本：EF/应用层怎么调用这些 SP

> **标本路径**：`C:\CP6\CP6.Core\Services\Mes\MesDashboardDapperService.cs`

```csharp
public class MesDashboardDapperService
{
    private readonly IDbConnection _conn;
    public MesDashboardDapperService(IDbConnection conn) => _conn = conn;

    /// <summary>本日サマリ — SP 経由</summary>
    public async Task<MesDashboardSummaryDto> GetSummaryAsync()
    {
        var row = await _conn.QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(
            "usp_GetMesDashboardSummary",
            commandType: CommandType.StoredProcedure);      // ★ 声明这是 SP，不是裸 SQL
        return row ?? new MesDashboardSummaryDto();
    }

    /// <summary>日別推移 — SP 経由（既定 30 日）</summary>
    public async Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30)
    {
        var rows = await _conn.QueryAsync<DailyTrendDto>(
            "usp_GetMesDailyTrend",
            new { Days = days },                              // ★ 匿名对象 → 参数 @Days（自动参数化，防注入）
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }
}
```

**逐点解析**：
- 用的是 **Dapper**（`QueryFirstOrDefaultAsync<T>` / `QueryAsync<T>`），不是 EF。为什么？下面第 4 节专讲。
- `commandType: CommandType.StoredProcedure`：告诉 Dapper "第一个参数是 SP 名字，帮我 `EXEC` 它"，
  而不是把它当一段裸 SQL 执行。
- `new { Days = days }`：Dapper 把匿名对象的属性名映射成 `@Days` 参数，**强类型、自动参数化**——这就是防 SQL 注入的正道。
- `QueryFirstOrDefaultAsync<MesDashboardSummaryDto>`：Dapper 按**列名 → DTO 属性名**自动映射（所以前面 SP 里
  `InProgressCount = @inProgress` 的列别名要和 DTO 属性对上）。

> **EF 也能调 SP**，语法是 `context.Database.SqlQuery<T>($"EXEC usp_Xxx {param}")` 或
> `FromSqlInterpolated`。但 CP6 这里选了 Dapper——因为 SP 返回的是**投影 DTO**（不是实体），
> Dapper 的轻量映射比 EF 更贴合这个场景。**"SP 的返回是投影就用 Dapper，是实体就可以用 EF"是一个实用判据。**

### 1.5 视图与索引视图

**视图（View）** = 一条存起来的 `SELECT`，给它起个名字，之后可以像表一样 `SELECT * FROM v_Xxx`。

```sql
CREATE VIEW dbo.v_TodayProduction AS
SELECT MachineCd, SUM(GoodQty) AS GoodQty, SUM(DefectQty) AS DefectQty
FROM T_ProductionResult
WHERE IsDeleted = 0 AND CAST(CreateDate AS date) = CAST(GETDATE() AS date)
GROUP BY MachineCd;
```

**视图的价值**：
- **封装复杂 JOIN/口径**：把"良品率怎么算""哪些状态算完成"这类口径固化在视图里，多个查询复用，口径统一。
- **权限隔离**：只授视图权限，隐藏底表结构和敏感列。
- **简化上层**：报表工具/BI 直接查视图，不用懂底层 20 张表怎么 JOIN。

**普通视图不存数据**——它只是查询的"别名"，每次查视图 = 展开成底层 SQL 再执行，**没有性能加成**（甚至嵌套视图会更慢）。

**索引视图（Indexed View / 物化视图）** 才存数据：给视图建**唯一聚集索引**后，视图结果被**物化**（真实存盘），
底表变更时引擎自动维护视图数据。适合"读极多、聚合固定、写相对少"的场景（如维度汇总表）。
代价是：**写放大**（底表每次写都要维护视图）+ 一堆严格限制（`SCHEMABINDING`、不能用 `OUTER JOIN`、聚合要带 `COUNT_BIG(*)` 等）。

> **CP6 的选择**：CP6 没用索引视图，而是走了"**SP + 覆盖索引**"路线（前面那个迁移）。
> 这在制造业 MES 里是更常见的选择——实绩表写入频繁（每次报工都插），索引视图的写放大不划算；
> 而 SP + 覆盖索引把优化集中在"读"路径，写路径只多维护一个窄索引，性价比更高。

### 1.6 生产坑：存储过程的"隐形黑盒"风险

**坑 1：SP 在库里被人手改，Git 里看不到。**
> 有人为了救火直接在生产库 `ALTER PROCEDURE` 加了个 hint，没同步回代码。下次部署迁移一跑，SP 被 `DROP + CREATE`
> 覆盖回旧版，救火的改动无声消失，问题复发。**CP6 用迁移管理 SP 就是为了根治这个**——SP 的唯一真相源是迁移文件，
> 库里手改会被下次迁移抹掉，反过来逼你"改必须走代码"。

**坑 2：参数嗅探（Parameter Sniffing）导致 SP 时快时慢。**
> SP 第一次执行时，SQL Server 根据**当时传入的参数值**生成并缓存执行计划。如果第一次传的参数很"特殊"
> （比如 `@Days=1` 只匹配几行），生成的计划对"典型值"（`@Days=90` 匹配几万行）可能是灾难性的。
> 于是 SP "看谁先跑"，时快时慢。第 8 节会讲缓解手段。

**坑 3：`SET NOCOUNT ON` 忘了写。**
> 每个语句都返回 "N rows affected" 消息，高频调用时这些消息本身就是网络和客户端解析开销。CP6 的每个 SP 都写了。

### 1.7 面试问答（第 1 节）

**Q：什么时候你会用存储过程而不是 EF？**
> A：默认全用 EF，因为可测试、可版本控制。但遇到**数据密集、往返敏感、逻辑稳定**的场景会下沉到 SP——
> 我们项目里 MES 仪表盘就是典型：一个页面要 4 个聚合，EF 是 4 次往返，SP 一次 EXEC 全算完，
> 配合覆盖索引走 index seek。而且我们把 SP 用 EF 迁移管理，SP 的每次变更都进 Git、随部署自动应用，
> 解决了"SP 版本控制难"的老问题。

**Q：覆盖索引是什么？为什么能加速？**
> A：把查询 SELECT 需要的列通过 `INCLUDE` 放进索引叶子层，这样引擎扫索引就能拿到所有列，不用回表
> （回聚集索引按主键捞行）。适合"过滤条件固定、SELECT 列固定"的报表查询。键列负责定位排序，
> INCLUDE 列只是顺带存下来避免回表。

**Q：视图能提升性能吗？**
> A：普通视图不能——它只是查询的别名，每次展开成底层 SQL。只有**索引视图**（物化视图）才存数据、有读加速，
> 但代价是写放大和一堆限制，适合读极多写极少的汇总场景。我们项目权衡后没用索引视图，走的是 SP + 覆盖索引。

---

<a name="2-触发器"></a>
## 2. 触发器：语法、慎用理由、CP6 的 SaveChanges 替代方案

### 2.1 概念：触发器是什么

**触发器（Trigger）** 是"挂在表上、由 DML 操作（INSERT/UPDATE/DELETE）自动触发"的一段 SQL。
你不显式调用它，它在你对表做增删改时**自动、隐式**地跑。

```sql
CREATE TRIGGER trg_ProductionResult_Audit
ON T_ProductionResult
AFTER INSERT, UPDATE, DELETE          -- AFTER = DML 完成后触发；也有 INSTEAD OF（替代原操作）
AS
BEGIN
    SET NOCOUNT ON;
    -- inserted / deleted 是两张"魔法表"：
    --   INSERT → inserted 有新行，deleted 空
    --   DELETE → deleted 有旧行，inserted 空
    --   UPDATE → 两张都有（旧行在 deleted，新行在 inserted）
    INSERT INTO AuditLog (TableName, Op, RowKey, ChangedAt)
    SELECT 'T_ProductionResult', 'I', Id, GETDATE() FROM inserted
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.Id = inserted.Id);
    -- … UPDATE / DELETE 分支
END
```

**典型用途**：
- 审计留痕（谁在什么时候改了什么）——**最经典的触发器用途**。
- 维护派生数据（库存汇总、冗余计数）。
- 强制复杂业务规则（跨表约束）。
- `INSTEAD OF` 触发器让"视图可更新"。

### 2.2 为什么现代系统慎用触发器

触发器最大的问题是**隐式**——它不在你调用的代码里，却在你每次写表时偷偷执行。这带来一连串维护噩梦：

1. **逻辑藏在你看不见的地方**：新人 `UPDATE` 一张表，结果连锁触发了 3 个触发器、改了 5 张表，
   他完全不知道为什么。调试时 C# 代码里根本找不到这段逻辑。
2. **性能黑洞**：触发器在事务内同步执行。一个触发器里做了重活（复杂 JOIN、写另一张大表），
   会让每一次简单的 UPDATE 都变慢，且极难定位——因为慢的地方"不在你的查询里"。
3. **递归/嵌套触发器**：触发器 A 改表触发触发器 B，B 又改表触发 A……容易失控。
4. **批量操作的陷阱**：新手常把触发器逻辑写成"假设 inserted 只有一行"，但一条 `UPDATE ... WHERE` 可能影响一万行，
   触发器只处理了第一行——**静默的数据错误**。
5. **难测试、难版本控制**：和 SP 一样的问题，还叠加"隐式"更难发现。

> **业界共识**：触发器不是禁用，而是"能用别的手段就别用触发器"。审计、派生数据这些，现代做法更倾向于
> **在应用层显式拦截**（如 ORM 的保存拦截器）或**事件驱动**（写完发消息，异步处理）。

### 2.3 CP6 的架构选择：用 SaveChanges 拦截替代审计触发器

> **标本路径**：`C:\CP6\CP6.Core\EFDbContext\CP6Context.cs`（字段审计核心，约 2187–2345 行）

CP6 要做**字段级审计**（记录"谁把哪条记录的哪个字段从什么值改成了什么值"）——这正是触发器的经典用途。
但 CP6 **没有用触发器**，而是**在 EF Core 的 `SaveChanges` 里拦截**。这是一个非常值得在面试讲的架构对比。

**做法（真实代码）**：重写 `DbContext.SaveChanges` / `SaveChangesAsync`，在保存前后做两阶段捕获：

```csharp
public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
{
    StampTenant();                                        // 多租户盖章（新增行补当前租户）
    var pending = CaptureFieldAuditBeforeSave();          // 阶段一：保存前捕获 IAuditable 实体的 diff
    if (pending.Count == 0)
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);   // 无审计目标 → 零开销原路径

    var useTx = Database.IsRelational() && Database.CurrentTransaction == null;
    var tx = useTx ? await Database.BeginTransactionAsync(ct) : null;
    try
    {
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);  // ① 业务行落库（Added 主键此刻落定）
        WriteAuditRows(pending);                                                   // ② 生成审计行
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, ct);          // ③ 审计行落库（同一事务）
        if (tx != null) await tx.CommitAsync(ct);                                  // ④ 一起提交，原子
        return result;                                                            // 返回业务影响行数（审计行不计入）
    }
    catch { if (tx != null) await tx.RollbackAsync(ct); throw; }
}
```

**捕获阶段（`CaptureFieldAuditBeforeSave`）** 遍历 `ChangeTracker.Entries<IAuditable>()`——
只有**实现了 `IAuditable` 标记接口**的实体才被审计（opt-in 白名单，不是全表拦截）：

```csharp
foreach (var e in ChangeTracker.Entries<IAuditable>())   // 访问 Entries 触发 DetectChanges
{
    if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
    var changes = BuildChanges(e);                         // 逐字段 before/after diff
    if (e.State == EntityState.Modified && changes.Count == 0) continue;   // 空改不记（零噪声）
    // …记下 实体名 / 操作码 / diff / 主键 / 租户
}
```

**diff 构造（`BuildChanges`）** 里有一套"三重密钥防护"，防止把敏感字段写进审计日志：

```csharp
if (pkNames.Contains(name)) continue;                                   // 跳过主键
if (name == "TenantId" || _metaSkip.Contains(name)) continue;           // 跳过租户 + who/when 元字段
if (p.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() != null) continue;  // 显式 [AuditIgnore]
if (IsSecretField(name)) continue;                                      // 名字命中 password/secret/hash/salt… 兜底拒名单
```

### 2.4 触发器 vs SaveChanges 拦截：架构对比表

| 维度 | 数据库触发器 | CP6 的 SaveChanges 拦截 |
|---|---|---|
| 逻辑位置 | 藏在库里，看不见 | 在 C# DbContext 里，**显式可见、可 debug** |
| 版本控制 | 要靠迁移单独管 | 和业务代码同仓、同 PR、同 review |
| 测试 | 难（要连库） | **可单元测试**（`IsSecretField`/`Stringify` 都是 internal 纯函数直测）|
| 用户上下文 | DB 里拿不到"谁在操作"（只有 DB 登录 sa）| **能拿到应用层的 UserId/UserName**（`_user?.UserId`）|
| 批量正确性 | 新手易写错（假设单行）| ChangeTracker 天然按实体逐个处理，无此坑 |
| 敏感字段过滤 | 要手写 | 三重防护（拒名单 + `[AuditIgnore]` + 元字段跳过）|
| 绕过风险 | **绕不过**（直接 SQL 也触发）| **能绕过**（`ExecuteUpdate`/裸 SQL/直连库不经 DbContext 就无审计）|
| 性能 | 每次写都同步执行 | 无审计目标时**零开销**（提前返回原路径）|

**关键权衡（面试必讲）**：SaveChanges 拦截的**唯一软肋**是"绕过风险"——如果有代码走
`ExecuteUpdateAsync`（EF 的批量更新，不经 ChangeTracker）或裸 SQL，就不会被审计。
CP6 的 MEMORY 里确实记着这条为**已知风险票**（"ERP ExecuteUpdateAsync 审计盲区"）。

> **面试怎么答这个权衡**："我们选 SaveChanges 拦截而非触发器，是用'一点点绕过风险'换'可见性、可测试、拿得到用户上下文'。
> 对绕过风险，治理办法是：约定所有写路径走 DbContext，把 `ExecuteUpdate` 列为需要 review 的模式，
> 审计敏感的表额外加数据库层保护。如果监管要求'一行都不能漏审计'，那才回到触发器——因为触发器绕不过。
> **这是一个典型的'没有银弹、只有取舍'的架构决策**，能把取舍讲清楚比背标准答案更能体现经验。"

### 2.5 面试问答（第 2 节）

**Q：为什么现代系统不推荐触发器？**
> A：核心问题是"隐式"——逻辑藏在库里，不在调用代码里，新人改一张表连锁触发一堆看不见的逻辑，
> 调试时 C# 里根本找不到。还有性能黑洞（事务内同步跑重活）、批量操作陷阱（新手假设 inserted 单行）、
> 难测试难版本控制。能用应用层拦截或事件驱动替代就别用触发器。

**Q：那审计留痕不用触发器怎么做？**
> A：我们项目用 EF Core 的 SaveChanges 拦截。重写 DbContext.SaveChangesAsync，保存前遍历 ChangeTracker
> 里实现了 IAuditable 的实体，逐字段做 before/after diff，业务行和审计行放同一事务原子提交。
> 好处是逻辑显式可见可测试、能拿到应用层的操作用户、敏感字段能过滤。代价是走裸 SQL/ExecuteUpdate 会绕过——
> 这是我们已知并在治理的风险。触发器的唯一不可替代优势就是"绕不过"，所以强合规场景才回到触发器。

---

<a name="3-动态-sql-与注入防护"></a>
## 3. 动态 SQL 与注入防护

### 3.1 SQL 注入攻击原理（用登录框经典例子讲透）

**先看一段有漏洞的代码**（新手最容易写出来的，字符串拼接）：

```csharp
// ❌ 危险！绝对不要这样写
string sql = "SELECT * FROM Users WHERE Username = '" + username + "' AND Password = '" + password + "'";
```

正常用户输入 `alice` / `123456`，拼出来：

```sql
SELECT * FROM Users WHERE Username = 'alice' AND Password = '123456'
```

现在攻击者在**用户名框**里输入：`' OR 1=1 --`，密码随便填。拼出来变成：

```sql
SELECT * FROM Users WHERE Username = '' OR 1=1 --' AND Password = 'whatever'
```

**逐字拆解这个攻击**：
- 攻击者输入开头的 `'` **闭合了** `Username = '` 的那个引号。
- `OR 1=1` 是恒真条件——整个 WHERE 变成"用户名为空 **或** 1=1"，1=1 永远成立，**匹配全表所有用户**。
- `--` 是 SQL 行注释，把后面的 `' AND Password = '...'` **全部注释掉**——密码校验直接消失。
- 结果：查询返回所有用户，应用拿到第一行（通常是管理员），**攻击者无密码登录成功**。

**更狠的变种：**
- `'; DROP TABLE Users; --`：闭合引号后用 `;` 起一条新语句，**删表**（如果连接权限够）。
- `' UNION SELECT CardNo, Cvv, 1, 1 FROM CreditCards --`：用 `UNION` **偷别的表的数据**，让敏感数据出现在登录返回里。
- `' OR 1=1; WAITFOR DELAY '0:0:5' --`：**盲注**——用响应时间判断条件真假，即使页面不回显数据也能一位一位撞出密码。

> **注入的本质**：**数据被当成了代码执行**。用户输入的 `' OR 1=1 --` 本应是"用户名"（数据），
> 却因为字符串拼接混进了 SQL 语句结构里，被引擎当成"逻辑"（代码）执行了。
> **一切防注入手段的核心，都是把"数据"和"代码"彻底分开。**

### 3.2 正确姿势：参数化查询

参数化的原理是：**SQL 语句结构和参数值分两条通道发给数据库**。语句里写占位符 `@username`，
值单独传。数据库先编译语句结构（此时 `@username` 只是个"洞"），再把值**作为纯数据**填进去——
值永远不会被解析成 SQL 语法。攻击者输入 `' OR 1=1 --` 只会被当成一个**字面的用户名字符串**去匹配，
匹配不到就匹配不到，绝不会改变语句逻辑。

**① EF Core（LINQ）自动参数化**——你几乎不用操心：

```csharp
var user = await db.Users
    .FirstOrDefaultAsync(u => u.Username == username && u.Password == hash);
// EF 生成：WHERE Username = @__username_0 AND Password = @__hash_1
// username 永远是参数，注入不了
```

**② EF Core 裸 SQL——必须用 `FromSqlInterpolated`（内插会被转成参数），不要用 `FromSqlRaw` 拼字符串：**

```csharp
// ✅ 安全：内插字符串被 EF 转成参数
var users = db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Username = {username}");

// ❌ 危险：FromSqlRaw + 字符串拼接 = 回到注入漏洞
var users = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Username = '" + username + "'");
```

**③ Dapper——匿名对象传参**（就是 CP6 的做法，见第 1、4 节）：

```csharp
var user = await conn.QueryFirstOrDefaultAsync<User>(
    "SELECT * FROM Users WHERE Username = @Username",
    new { Username = username });          // @Username 是参数，安全
```

**④ 存储过程内的动态 SQL——用 `sp_executesql` 参数化，不要 `EXEC(@sql拼串)`：**

有时 SP 里确实需要动态拼 SQL（比如动态排序列、动态表名）。此时：

```sql
-- ✅ 安全：sp_executesql 支持参数
DECLARE @sql nvarchar(max) = N'SELECT * FROM T_WorkOrder WHERE Status = @status';
EXEC sp_executesql @sql, N'@status int', @status = @inputStatus;

-- ❌ 危险：EXEC 拼串 = SP 里的注入漏洞
DECLARE @sql nvarchar(max) = 'SELECT * FROM T_WorkOrder WHERE Status = ' + @inputStatus;
EXEC(@sql);
```

> **注意**：`sp_executesql` 只能参数化**值**，不能参数化**标识符**（表名、列名）。
> 如果要动态拼表名/列名（比如动态排序），值参数化挡不住——必须用**白名单校验**：
> `IF @sortCol NOT IN ('CreateDate','Status') SET @sortCol = 'CreateDate';`，
> 然后用 `QUOTENAME(@sortCol)` 加方括号转义。**"标识符要白名单，值要参数化"是两条不同的防线。**

### 3.3 CP6 的现状：全线参数化，无拼接

CP6 里所有数据访问都走 EF Core（LINQ 自动参数化）或 Dapper（匿名对象参数）——
你在前面看到的 `MesDashboardDapperService` 的 `new { Days = days }` 就是标准参数化写法。
存储过程用的是**强类型参数**（`@Days int`），Dapper 通过 `CommandType.StoredProcedure` 调用，值全程作为参数传递。
**整条链路上没有一处字符串拼接 SQL**——这就是"防注入靠架构约束，而不是靠每个人自觉"的正确形态。

### 3.4 纵深防御（不止参数化）

参数化是**第一道也是最重要的防线**，但生产系统要纵深防御：
- **最小权限**：应用连库的账号不该有 `DROP`/`ALTER` 权限（CP6 目前用 `sa` 连库，是简化，见 runbook §4 提到
  "日后改最小权限专用登录"的规划——这本身就是一个可讲的改进点）。
- **输入校验**：长度、格式、白名单——不能替代参数化，但能减小攻击面。
- **报错不外泄**：生产环境不要把 SQL 异常原文返回给前端（泄露表结构、给盲注提供信息）。
- **WAF / 速率限制**：挡住自动化注入扫描。

### 3.5 面试问答（第 3 节）

**Q：讲一下 SQL 注入的原理和防护。**
> A：本质是"数据被当成代码执行"。经典例子：登录框字符串拼接 SQL，攻击者在用户名输入 `' OR 1=1 --`，
> 前面的引号闭合了用户名引号，`OR 1=1` 恒真匹配全表，`--` 注释掉密码校验，无密码登录成功。
> 防护核心是把数据和代码分开——用**参数化查询**：语句结构和值分两条通道，值永远作为纯数据不会被解析成语法。
> EF 的 LINQ 自动参数化，裸 SQL 用 FromSqlInterpolated，Dapper 用匿名对象传参，SP 内动态 SQL 用 sp_executesql。
> 我们项目全线 EF + Dapper 参数化，没有一处拼接。再加最小权限、输入校验、报错不外泄做纵深防御。

**Q：`sp_executesql` 和 `EXEC(@sql)` 有什么区别？**
> A：`EXEC(@sql)` 直接执行拼好的字符串，等于把注入漏洞搬进了 SP。`sp_executesql` 支持参数占位符，
> 值作为参数传，安全，而且执行计划能复用。但要注意 `sp_executesql` 只能参数化值，不能参数化表名/列名——
> 动态标识符要用白名单 + QUOTENAME。

---

<a name="4-dapper-实战"></a>
## 4. Dapper 实战：为什么 CP6 报表不用 EF

### 4.1 概念：Dapper 是什么

**Dapper** 是一个**微型 ORM**（micro-ORM），由 Stack Overflow 团队开发。它做的事情很少但很精：
- 你给它一段 SQL（或 SP 名）+ 参数，它执行、把结果集**按列名映射到你的 C# 类型**，返回。
- 它是 `IDbConnection` 的一组扩展方法（`Query<T>`、`QueryAsync<T>`、`Execute`、`QueryFirstOrDefault<T>`…）。
- 它**不做**：变更追踪、导航属性、迁移、LINQ 翻译、缓存——这些 EF 干的重活它一概不管。

一句话："**Dapper = 手写 SQL + 自动对象映射**"。你完全掌控 SQL，只把"填 DataReader 到对象"这件苦力活交给它。

### 4.2 CP6 真实标本：MesDashboardDapperService

> **标本路径**：`C:\CP6\CP6.Core\Services\Mes\MesDashboardDapperService.cs`（完整文件已在第 1.4 节展示）

回顾类头的注释（这是 CP6 作者自己写下的"为什么"，直接引用最有说服力）：

```
/// <remarks>
/// JD「SQL Server 性能調優・存儲過程」要件のサンプル：
/// - 既存 EF Core 版（MesDashboardService）と並列実装
/// - 大量集計クエリは SP に寄せて DB 側でインデックス + プラン最適化
/// - Dapper でストロングタイプ マッピング + 単一往復
/// </remarks>
```

翻译要点：
1. **和 EF 版并列存在**——CP6 里有两套仪表盘服务：`MesDashboardService`（EF 版）和 `MesDashboardDapperService`（Dapper+SP 版）。
   这不是"推倒 EF"，而是"针对重聚合场景多提供一条高速路"。控制器里两条路都暴露了
   （`/summary` 走 EF，`/summary/sp` 走 Dapper+SP，见 `MesDashboardController.cs`）。
2. **大量聚合下沉 SP**——让 DB 侧用索引和计划优化。
3. **Dapper 做强类型映射 + 单一往返**。

**控制器里的 A/B 对照（真实代码，`MesDashboardController.cs`）**：

```csharp
// ═══ Dapper + SP 版（性能調優サンプル — JD 要件）
//     従来 EF 版より 30-60% 高速（測定値はデータ量による）
[HttpGet("summary/sp")]
public async Task<IActionResult> SummarySp()
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var s = await _dapper.GetSummaryAsync();
    sw.Stop();
    return Ok(new { code = 0, message = "OK", data = s, elapsedMs = sw.ElapsedMilliseconds });
    //                                                    ^^^^^^^^ 连耗时都返回了，方便对比
}
```

> **这段代码本身就是面试素材**——它把"EF 版 vs Dapper+SP 版"做成了可测量的 A/B 端点，
> 甚至返回 `elapsedMs`。你面试讲"我怎么验证性能优化有效"时，这就是活证据："我保留两条路径、
> 用同样的输入测耗时对比，而不是凭感觉说 Dapper 快。"

### 4.3 为什么这个场景 Dapper 优于 EF

| 原因 | 展开 |
|---|---|
| **返回的是投影 DTO，不是实体** | 仪表盘返回 `MesDashboardSummaryDto`（几个聚合数字），不是 `T_WorkOrder` 实体。EF 的变更追踪、身份映射对投影是纯浪费。 |
| **SQL 完全掌控** | 重聚合、递归 CTE、`OPTION (MAXRECURSION)` 这些，EF 的 LINQ 翻译要么生成不出、要么生成得很难看。手写 SQL 直接、可控、可优化。 |
| **单一往返** | SP 一次 EXEC 返回结果，Dapper 一次映射。EF 版可能是多个 `CountAsync`/`SumAsync`。 |
| **无追踪开销** | Dapper 不建 ChangeTracker、不做快照。纯读场景 EF 也能 `.AsNoTracking()`，但 Dapper 天生就没这负担。 |
| **映射轻量** | Dapper 的 IL emit 映射极快，接近手写 DataReader。 |

### 4.4 EF vs Dapper 选型表（面试可直接背）

| 维度 | EF Core | Dapper |
|---|---|---|
| 定位 | 全功能 ORM | micro-ORM（SQL + 映射）|
| 变更追踪 | ✅ 有（增删改自动 diff）| ❌ 无（要自己写 INSERT/UPDATE）|
| 写操作（CRUD）| ✅ 强项，`SaveChanges` 一把梭 | 要手写 SQL，繁琐 |
| 复杂查询/报表 | LINQ 翻译有时低效/翻不出 | ✅ 强项，SQL 完全掌控 |
| 迁移/建表 | ✅ 内建 | ❌ 无 |
| 学习曲线 | 陡（LINQ 翻译、追踪、加载策略）| 平（会 SQL 就会用）|
| 性能（纯读）| 好（`AsNoTracking` 后）| **更好**（更轻）|
| 防注入 | ✅ 自动参数化 | ✅ 匿名对象参数化（要自己记得用参数）|
| 存储过程调用 | 可以，但对投影不如 Dapper 顺手 | ✅ 顺手（`CommandType.StoredProcedure`）|
| 多态/继承映射 | ✅ | ❌ 手动 |

**选型口诀（面试标准答案）**：
> "**写操作和领域模型用 EF**（变更追踪、迁移、导航属性省太多事）；**重查询、报表、仪表盘、存储过程调用用 Dapper**
> （SQL 掌控、映射轻、往返少）。两者不是二选一，同一个项目里可以并存——我们项目 99% 走 EF，
> MES 仪表盘这类重聚合专门用 Dapper + SP，控制器里两条路并列，甚至做了带耗时的 A/B 端点验证提速有效。"

### 4.5 生产坑：Dapper 的自由是双刃剑

- **坑 1：Dapper 不管参数化，你忘了就中招。** EF 强制参数化，Dapper 只是"你用匿名对象它就参数化"——
  如果你手贱写 `$"... WHERE X = {input}"` 字符串内插传给 Dapper，就是注入漏洞。**自由的代价是自律。**
- **坑 2：列名对不上，映射静默失败。** SP/SQL 的结果列名和 DTO 属性名不一致时，Dapper 映射不上那一列
  （默认给默认值，不报错）。所以前面 SP 里要写 `InProgressCount = @inProgress` 让列名对齐 DTO。
- **坑 3：连接生命周期。** CP6 在 `Program.cs` 里 `AddScoped<IDbConnection>(_ => new SqlConnection(...))`，
  每个请求一个连接。Dapper 会自动开关连接，但要小心别在长生命周期里囤连接。

### 4.6 面试问答（第 4 节）

**Q：你们项目为什么报表用 Dapper 不用 EF？**
> A：报表返回的是投影 DTO 不是实体，EF 的变更追踪和身份映射是纯浪费；重聚合、递归 CTE 这些 EF 的 LINQ
> 翻译要么翻不出要么很难看，手写 SQL 直接可控；而且能配合存储过程一次往返。我们 MES 仪表盘就是 Dapper + SP，
> 和 EF 版并列，控制器里做了带耗时的 A/B 端点，实测比 EF 版快 30-60%（随数据量）。写操作我们还是全用 EF——
> 变更追踪和迁移省太多事。

**Q：Dapper 怎么防注入？**
> A：用参数——传匿名对象 `new { Days = days }`，Dapper 把它转成 `@Days` 参数。千万别用字符串内插拼 SQL 再传给它，
> 那就把 EF 帮你挡掉的注入又放回来了。Dapper 的自由度高，防注入要靠自律。

---

<a name="5-备份与恢复"></a>
## 5. 备份与恢复（生产必修）

> 这一节是"5 年经验"和"应届生"的分水岭。会写 SQL 谁都行，**懂得怎么保命**才是运维。

### 5.1 概念：三种备份

| 备份类型 | 备份内容 | 恢复时怎么用 | 频率典型值 |
|---|---|---|---|
| **完整备份（FULL）** | 整个数据库的完整快照 | 恢复的**基石**，单独就能还原到备份那一刻 | 每天/每周 |
| **差异备份（DIFFERENTIAL）** | 自**上次完整备份**以来变化的部分 | 要先还原完整备份，再叠加差异 | 每天/每几小时 |
| **事务日志备份（LOG）** | 自上次日志备份以来的所有事务记录 | 完整+差异之后，逐个日志"重放"到某个精确时间点 | 每 5–15 分钟 |

**恢复链**：`完整备份 → (最近一次差异备份) → 之后的所有日志备份`，串起来才能恢复到"最后一秒"。
少一环，就只能恢复到少的那一环之前。

### 5.2 恢复模式：FULL vs SIMPLE

这是**最重要、也最容易被面试戳穿**的知识点。数据库的**恢复模式（Recovery Model）** 决定了"日志怎么处理、能不能做时间点恢复"。

| | SIMPLE（简单） | FULL（完整） |
|---|---|---|
| 事务日志 | checkpoint 后**自动截断**（空间自动回收）| **一直保留**，直到你做日志备份才截断 |
| 能做日志备份吗 | ❌ 不能 | ✅ 能 |
| 能恢复到任意时间点吗 | ❌ 只能恢复到最近的完整/差异备份 | ✅ 能（配合日志备份，恢复到任意一秒）|
| 日志会不会暴涨 | 不会（自动截断）| **会！如果不定期做日志备份，日志无限增长撑爆磁盘** |
| 适用场景 | 数据可容忍丢一天、或能重导（数仓、测试、CP6 当前）| 生产核心库、丢一分钟都不行 |

> **⚠️ 面试最爱的坑题**："FULL 模式下日志文件涨到 200GB 停不下来，为什么？"
> 答案：**FULL 模式的日志只有在做了日志备份后才截断**。如果你设了 FULL 却从不备份日志，
> 日志会永久累积、无限增长，直到撑爆磁盘。要么定期做日志备份，要么（如果不需要时间点恢复）改回 SIMPLE。
> 这个坑第 6 节还会作为"日志暴涨"的头号原因详讲。

### 5.3 RTO 与 RPO（面试必须张口就来）

这两个缩写是"聊备份"的通用语言，答不出等于没做过运维。

- **RPO（Recovery Point Objective，恢复点目标）**：**能容忍丢多少数据**（时间维度）。
  = "灾难发生时，最多丢失最近多长时间的数据？" 由**备份频率**决定。
  例：每 4 小时备份一次 → 最坏 RPO ≈ 4 小时（灾难发生在下次备份前一刻，这 4 小时的数据全丢）。
- **RTO（Recovery Time Objective，恢复时间目标）**：**能容忍多久恢复不了服务**（时间维度）。
  = "从故障发生到系统重新可用，最多允许多长时间？" 由**恢复流程的速度**决定（备份多大、还原多快、有没有演练过）。

> **一句话记忆**：**RPO 管"丢多少"（往前看，数据损失），RTO 管"停多久"（往后看，停机时长）**。
> 缩短 RPO → 更频繁备份 / 用日志备份 / 上高可用。缩短 RTO → 更快的恢复流程 / 热备 / 演练过的 runbook。

### 5.4 CP6 真实标本：db-backup.ps1 逐步精读

> **标本路径**：`C:\CP6\scripts\db-backup.ps1`
> **调度**：Windows 计划任务 `CP6-DB-Backup`，**每 4 小时一次**。
> **备份落地**：`C:\CP6Backups\<库名>\<库名>_时间戳.bak`，**本地保留 14 天滚动**。

先看这个脚本的**背景注释**（作者自己写的，直接暴露了 CP6 的真实处境）：

```
# 背景：SQL Server 跑在 WSL docker 容器 cp6-db 内，数据落在 WSL 虚拟盘上——
#       WSL/宿主机曾多次卡死近乎重装，库数据是本机唯一副本。
```

**这句话信息量极大**：CP6 的数据库跑在 Windows → WSL2 → Docker 容器里，数据在 WSL 虚拟盘上，
而这台机器曾经**卡死到近乎重装**。所以这个备份脚本不是"最佳实践演示"，而是**血的教训催生的保命措施**。

**逐段解析脚本**：

**① 配置头**
```powershell
$ErrorActionPreference = 'Stop'         # 任何错误立即停（不静默继续）
$BackupRoot   = 'C:\CP6Backups'
$RetentionDays = 14                     # 保留 14 天
$Container    = 'cp6-db'
$LocalJson    = 'C:\CP6\CP6.WebApi\appsettings.Local.json'   # sa 密码从这里读
```
> **亮点**：脚本本身**不含任何密钥**（注释明说"可安全入库"）。sa 密码运行时从 gitignored 的
> `appsettings.Local.json` 解析。**"脚本入 Git，密钥不入 Git"是安全底线**，面试可主动提。

**② 运行时解析凭据**
```powershell
$cs = (Get-Content $LocalJson -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection
$pw = ($cs -split ';' | Where-Object { $_ -like 'Password=*' }) -replace '^Password=', ''
if ([string]::IsNullOrEmpty($pw)) { throw "无法从 appsettings.Local.json 解析 sa 密码" }
```
从连接字符串里切出 `Password=xxx` 部分。解析失败直接抛错——**fail fast，不带着空密码往下跑**。

**③ 容器健康守卫（这段是"生产经验"的浓缩）**
```powershell
$state = (wsl docker inspect --format '{{.State.Status}}' $Container 2>$null)
if ($state -ne 'running') { Log "SKIP: 容器非 running，本轮跳过"; exit 0 }
$health = (wsl docker inspect --format '{{.State.Health.Status}}' $Container 2>$null)
if ($health -and $health -ne 'healthy') { Log "SKIP: 容器未 healthy，本轮跳过"; exit 0 }
```
> **为什么要这段？** 因为 WSL 会"弹跳"（第 6 节详讲）——容器可能刚被拉起还没就绪。
> 如果这时硬跑 `BACKUP`，会失败并让计划任务卡住。**守卫的哲学是"没就绪就干净跳过（exit 0），
> 等下一轮，绝不 hang 住计划任务"**。这种"宁可跳过一轮，也不卡死调度"的防御性设计，是运维老手的标志。

**④ 自动发现业务库**
```powershell
$dbListRaw = wsl docker exec $Container $SqlcmdInCtr -S localhost -U sa -P "$pw" -C -h -1 -W `
    -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'CP6DB%' ORDER BY name;"
$dbs = @($dbListRaw | Where-Object { $_ -and $_.Trim() -match '^CP6DB' } | ForEach-Object { $_.Trim() })
if ($dbs.Count -eq 0) { throw "未发现 CP6DB* 业务库" }
```
不硬编码库名，而是查 `sys.databases` 找所有 `CP6DB` 前缀的库（CP6 是多租户/多库）。**新增租户库自动纳入备份，不用改脚本。**

**⑤ 库级就绪守卫（一个非常深的坑）**
```powershell
# 库级就绪守卫：容器 healthy ≠ 库 ONLINE（启动恢复期 BACKUP 会 Msg 904/3023）
$ready = $false
for ($i = 0; $i -lt 12; $i++) {
    $notOnline = wsl docker exec ... -Q "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'CP6DB%' AND state_desc <> 'ONLINE';"
    if (($notOnline | Select-Object -First 1).Trim() -eq '0') { $ready = $true; break }
    Start-Sleep -Seconds 10
}
if (-not $ready) { Log "SKIP: 业务库超过 120s 未全部 ONLINE（启动恢复期），本轮跳过"; exit 0 }
```
> **这段是精华中的精华**：作者踩过一个坑——**"容器 healthy 不等于数据库 ONLINE"**。
> SQL Server 容器起来了（healthcheck 过），但数据库还在做**启动恢复**（redo/undo 未提交事务），
> 此时状态不是 `ONLINE`，硬跑 `BACKUP` 会报 `Msg 904`（数据库未打开）/ `Msg 3023`（备份/文件操作冲突）。
> 所以要**再查一层** `state_desc = 'ONLINE'`，最多等 120 秒，还不行就跳过本轮。
> **"容器健康 ≠ 应用就绪 ≠ 数据库就绪"是分布式系统里一层层的就绪性问题，能讲出这个层次感很加分。**

**⑥ 备份主循环（带压缩、校验、重试）**
```powershell
foreach ($db in $dbs) {
    $bakOk = $false
    for ($try = 1; $try -le 2; $try++) {   # 一次重试（启动尾声偶发 Msg 3023）
        wsl docker exec $Container $SqlcmdInCtr ... -b `
            -Q "BACKUP DATABASE [$db] TO DISK='$ctrPath' WITH INIT, COMPRESSION, CHECKSUM;"
        if ($LASTEXITCODE -eq 0) { $bakOk = $true; break }
        Start-Sleep -Seconds 15
    }
    if (-not $bakOk) { Log "ERROR: $db BACKUP 失败（两次尝试）"; continue }
    # ... docker cp 拷出到 Windows 文件系统 ...
}
```
`BACKUP DATABASE` 的三个关键 WITH 选项：
- **`INIT`**：覆盖（而非追加）到目标文件——保证每份 .bak 是干净的单一备份。
- **`COMPRESSION`**：备份压缩，省磁盘和拷贝时间（CP6 数据是本机唯一副本，磁盘紧，压缩很关键）。
- **`CHECKSUM`**：写备份时计算页校验和——**能在备份阶段就发现数据页损坏**，而不是等还原时才发现"备份本身是坏的"。
  这是"备份可用性"的第一道保险。

**⑦ 拷出到 Windows + 清理容器内临时文件**
```powershell
# docker cp 目标须用 WSL 视角路径（C:\ 会被误解析为容器名）
$destWsl = (wsl wslpath -a ($destWin -replace '\\', '/')).Trim()
wsl docker cp "${Container}:$ctrPath" "$destWsl"
wsl docker exec $Container rm -f $ctrPath | Out-Null      # 删容器内临时 bak，不占容器卷
```
> **一个真实的坑注释**："docker cp 目标须用 WSL 视角路径（`C:\` 会被误解析为容器名）"——
> Windows 路径 `C:\...` 里的冒号会被 `docker cp` 当成 `容器名:路径` 的分隔符。要用 `wslpath` 转成 `/mnt/c/...`。
> **这种"跨 Windows/WSL/容器三层文件系统"的细节坑，是真做过才知道的。**

**⑧ 滚动保留 + 汇总**
```powershell
$cutoff = (Get-Date).AddDays(-$RetentionDays)
$purged = Get-ChildItem -Path $BackupRoot -Recurse -Filter '*.bak' | Where-Object { $_.LastWriteTime -lt $cutoff }
if ($purged) { $purged | Remove-Item -Force; Log "清理 $($purged.Count) 份旧备份" }
Log "完成：$ok/$($dbs.Count) 库备份成功"
if ($ok -lt $dbs.Count) { exit 1 }        # 有库失败 → 非零退出码（计划任务能感知失败）
```
删 14 天前的旧备份，防备份目录无限增长。最后按成功数决定退出码——**让计划任务能感知失败**。

### 5.5 这套备份方案的 RPO/RTO 评估 + 可改进点

**当前状态评估**：

| 指标 | CP6 现状 | 评价 |
|---|---|---|
| RPO | ≈ 4 小时（每 4h 完整备份，无日志备份）| 对制造业数据偏松——4 小时的报工/库存变动可能丢失 |
| RTO | 取决于 .bak 大小 + 还原速度（runbook 有还原步骤）| 有 runbook，但**没证据表明演练过** |
| 恢复模式 | 未显式设，SQL Server 默认 model 库继承（很可能 FULL 或 SIMPLE 视 model 而定）| 应显式确认并统一 |
| 备份校验 | ✅ `CHECKSUM`（写时校验）| 好，但**没有还原验证** |
| 异地副本 | ❌ 只在同一台机器的 `C:\CP6Backups` | **最大风险**——机器整机挂了/被勒索，备份和库同归于尽 |

**可改进点（这些正是面试"推进改善措施"要说的）**：

1. **异地副本（最优先）**：现在备份和数据库在**同一台物理机**。如果磁盘坏、机器被盗、勒索软件加密整机，
   备份跟着一起没。改进：把 `C:\CP6Backups` 同步到另一台机器 / NAS / 对象存储（S3/Azure Blob）。
   **"备份的黄金法则 3-2-1：3 份副本、2 种介质、1 份异地。"**
2. **缩短 RPO——加事务日志备份**：如果制造业数据不能忍受丢 4 小时，改恢复模式为 FULL + 每 15 分钟日志备份，
   RPO 降到 15 分钟。代价是要管理日志备份链和日志文件增长。
3. **恢复演练（治 RTO）**：备份最怕"从没试过还原"。定期（如每季度）拿一份 .bak 在隔离环境还原一次，
   验证：能不能还原成功、数据完整不完整、花了多久（这就是实测 RTO）。**"没演练过的备份 = 薛定谔的备份"**。
4. **备份成功告警**：现在失败只写日志（`backup.log`）+ 非零退出码。应加主动告警（邮件/IM），
   否则"备份连续失败一周没人知道"，等到要还原时才发现没有可用备份。
5. **差异备份补位**：完整备份每 4h 一次，中间可加差异备份把 RPO 进一步压小，而差异备份比完整备份小、快。

### 5.6 面试问答（第 5 节）

**Q：讲讲你们的备份策略。**
> A：SQL Server 跑在 Docker 容器里，我们用 PowerShell 脚本 + Windows 计划任务每 4 小时做一次完整备份，
> 容器内 `BACKUP DATABASE WITH INIT, COMPRESSION, CHECKSUM`，再 docker cp 拷到 Windows 文件系统，本地保留 14 天滚动。
> 脚本有多层就绪守卫——容器 running、healthy、以及数据库真正 ONLINE（因为容器 healthy 不等于库 ONLINE，
> 启动恢复期硬备份会报 Msg 904/3023），没就绪就干净跳过本轮不卡死调度。密钥不入脚本，运行时从 gitignored 配置读。

**Q：这套方案有什么可以改进的？**
> A：最大的问题是**没有异地副本**——备份和库在同一台机器，整机故障就同归于尽，应该按 3-2-1 法则同步到异地。
> 其次 RPO 是 4 小时，如果业务不能忍，可以上 FULL 恢复模式 + 15 分钟日志备份把 RPO 压到 15 分钟。
> 还有**恢复演练**——我们有 runbook 但没定期演练，没演练过的备份等于薛定谔的备份，要定期拿 .bak 试还原、实测 RTO。
> 最后加个备份失败告警，别等要还原时才发现连续失败。

**Q：FULL 和 SIMPLE 恢复模式区别？RTO/RPO 是什么？**
> A：SIMPLE 下日志 checkpoint 后自动截断、不能做日志备份、只能恢复到最近的完整/差异备份；FULL 下日志保留到你做日志备份
> 才截断、能做时间点恢复。FULL 的坑是不做日志备份日志会无限涨撑爆磁盘。RPO 是能容忍丢多少数据（备份频率决定），
> RTO 是能容忍停多久（恢复速度决定）——RPO 管"丢多少"、RTO 管"停多久"。

---

<a name="6-容量与增长治理"></a>
## 6. 容量与增长治理 + 磁盘满全栈停机事故还原

### 6.1 概念：数据/日志文件的增长机制

一个 SQL Server 数据库物理上至少两个文件：
- **数据文件 `.mdf`**：存表、索引、实际数据。
- **日志文件 `.ldf`**：存事务日志（每个改动的 redo/undo 记录，保证 ACID）。

两个文件都会**自动增长（autogrowth）**：写满了就按设定的增量（比如每次 +64MB 或 +10%）向操作系统申请更多空间。
增长**只涨不缩**——SQL Server 不会自动把空间还给操作系统（除非你手动 shrink）。

### 6.2 日志暴涨的常见原因（面试高频）

日志文件 `.ldf` 涨到失控是生产事故常客。头号原因：

1. **FULL 恢复模式 + 从不做日志备份**（第 5.2 节的坑）：FULL 下日志只有做了日志备份才截断，
   不备份 → 日志永久累积 → 撑爆磁盘。**这是 #1 原因。**
2. **单个巨型事务**：一次 `DELETE` / `UPDATE` 几百万行放在一个事务里，整个事务的日志要一直保留到 COMMIT/ROLLBACK，
   日志瞬间暴涨。改进：分批（每次删 5000 行 + 提交）。
3. **索引重建**：`REBUILD` 大索引会产生大量日志。
4. **长时间未提交的事务**：一个事务开着不 COMMIT，它之前的日志都不能截断（哪怕别的事务早提交了）。
   `DBCC OPENTRAN` 能查最老的活动事务。
5. **复制/CDC/AlwaysOn 未同步**：日志要等这些消费掉才能截断，卡住就涨。

### 6.3 shrink 的争议

`DBCC SHRINKFILE` 能把文件缩小、把空间还给操作系统。但 DBA 圈子里**shrink 数据文件是"能不做就不做"**：
- **shrink 会引起严重的索引碎片**（它把页往文件头挪，物理顺序全乱），碎片又拖慢查询，你可能还要 REBUILD 索引，
  而 REBUILD 又让文件涨回去——**恶性循环**。
- 数据文件"用掉的空间"通常会再被用到，缩了又涨是白折腾 + 碎片代价。

**什么时候 shrink 可接受**：一次性事件后的日志文件（比如一次巨型归档删除后日志涨到 200GB，之后不会再有），
或者确实永久性释放了大量空间（删了一个大历史表且不会再长回来）。**"日志文件一次性 shrink 可以，数据文件常规 shrink 是反模式"** 是安全的面试立场。

### 6.4 CP6 真实标本：审计日志容量监控

> **标本路径**：`C:\CP6\scripts\audit-log-monitor.ps1`
> **调度**：计划任务 `CP6-AuditLog-Monitor`，**每 4 小时**，日志写 `C:\CP6Backups\audit-log-monitor.log`。

回顾第 2 节——CP6 的字段审计（`Sys_FieldAuditLogs` 表）是"每次改 IAuditable 实体就插一行"。
问题来了：**审计表只增不减，会不会涨到爆？** 这个脚本就是给这张表装的"容量哨兵"：

```powershell
$rowWarn = 1000000   # 100万行告警
$mbWarn  = 500       # 500MB 告警

$q = "SET NOCOUNT ON;
      SELECT p.rows, CAST(SUM(a.total_pages)*8/1024.0 AS DECIMAL(12,1)) AS mb
      FROM sys.tables t
      JOIN sys.partitions p ON t.object_id=p.object_id AND p.index_id IN (0,1)
      JOIN sys.allocation_units a ON p.partition_id=a.container_id
      WHERE t.name='Sys_FieldAuditLogs'
      GROUP BY p.rows;"
$out = wsl -e docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd ... -d CP6DB -h -1 -Q "$q" ...
$rows = [long]$parts[0]; $mb = [decimal]$parts[1]
$level = "OK"
if ($rows -gt $rowWarn -or $mb -gt $mbWarn) { $level = "WARN" }
```

**这段 SQL 逐句解析（面试可直接讲这个"查表占用空间"的技巧）**：
- `sys.tables t JOIN sys.partitions p`：从系统目录视图查表的分区。
- `p.index_id IN (0,1)`：`index_id=0` 是堆（无聚集索引），`index_id=1` 是聚集索引——两者是"表数据本身"
  （不算非聚集索引），取其一即代表行数据。
- `SUM(a.total_pages) * 8 / 1024.0`：`sys.allocation_units.total_pages` 是分配的**页数**，
  每页 **8KB**，`× 8` 得 KB，`/ 1024` 得 **MB**。**"SQL Server 一页 = 8KB"是必须记住的常识。**
- `p.rows`：分区行数（近似值，非精确 count 但足够监控）。

**这个脚本的设计哲学（注释里写死了）**：
```
# 阈值超限只告警不删数据——retention/归档策略本体待用户裁决
```
> **只告警、不自动删**——这是**极其正确**的运维克制。自动删审计日志？万一阈值设错、
> 万一那些日志正好要合规审查用？**"监控可以自动，销毁必须人工确认"**。触发 WARN 时脚本额外写一行提示
> "需执行归档/purge 裁决"，把决定权留给人。面试讲这个能体现"对生产数据的敬畏"。

**基线数据（MEMORY 记录）**：这张审计表当前基线约 **835 行 / 0.6MB**，阈值设 100 万行 / 500MB——
即离告警还很远，是**预防性监控**（趁早装哨兵，不是等爆了才装）。

### 6.5 ★ 金牌事故素材：磁盘满导致全栈停机（完整 STAR 还原）

> 这是**真实发生过**的生产事故（CP6 MEMORY 有完整复盘记录）。面试问"讲一次你处理的生产故障"，
> 这个故事结构完整、有技术深度、有治标治本、有复盘改进——是**教科书级的 STAR 答案**。

#### 事故因果链（一图看懂）

```
   Windows Update 缓存悄悄涨到 2.8GB
              │
              ▼
   C 盘可用空间 → 0（磁盘满）
              │
              ▼
   WSL2 要分配 swap 交换文件 → 磁盘没空间 → swap 分配失败
              │
              ▼
   WSL2 I/O error（虚拟盘写不进去）
              │
              ▼
   dockerd（Docker 守护进程）崩溃
              │
              ▼
   cp6-db 容器挂掉 → 数据库不可用
              │
              ▼
   cp6-api 连不上库 → 整个后端 500 → 全栈停机
```

**这条链最"阴险"的地方**：根因（Windows Update 缓存）和表现（数据库全栈挂）**隔了四层**——
磁盘 → WSL → Docker → 数据库。第一反应去查数据库、查容器，全是"下游症状"，根本查不到"是 C 盘被 WU 缓存吃满了"。
**"表象在数据库，根因在操作系统磁盘"——这种跨层故障是最考验排障功力的。**

#### 用 STAR 讲这个故事（面试逐字可用）

**S（Situation 情境）**：
> "我们的生产栈是 .NET 8 后端 + SQL Server，数据库跑在 Windows Server 上的 WSL2 + Docker 容器里。
> 某天整个系统突然全线 500，前端打不开，后端所有接口报错——**全栈停机**。"

**T（Task 任务）**：
> "我要在最短时间内**先恢复服务**（止血），再**定位根因**（不能只重启了事，否则还会复发），
> 最后**推进改进**避免再犯。"

**A（Action 行动）**——分止血、定位、根因三步：
> "**第一步止血/定位**：接口报数据库连不上，我先去看容器——`docker ps` 发现 dockerd 本身都崩了。
> 再看宿主机，发现 **C 盘可用空间是 0**。顺藤摸瓜：WSL2 需要动态分配 swap 交换文件，磁盘满了分配不到，
> 导致 WSL 虚拟盘 I/O error，dockerd 跟着崩，数据库容器随之挂掉。
>
> **第二步找是谁吃了磁盘**：C 盘 2.8GB 被 **Windows Update 缓存**占了。清掉 WU 缓存，
> C 盘腾出空间，重启 WSL / dockerd，容器和数据库恢复，服务回来了——这是**治标**。
>
> **第三步治本**：光清缓存不够，磁盘还会再满、WSL 还会再崩。所以我做了两件事根治：
> 一是加了一个**看门狗计划任务**（`wsl-keepalive.ps1`）—— 因为 WSL2 的生命周期跟客户端连接走，
> 最后一个 wsl.exe 退出后约 1 分钟整机就 poweroff，容器全没。这个脚本常驻一个隐藏 wsl 会话
> `sleep infinity` 钉住 VM，登录自启、幂等（已存活就跳过），防止 WSL 静默关机。
> 二是把'磁盘扩容'作为**真正的治本建议**提出来——WU 缓存只是导火索，根子是磁盘余量太紧。"

**R（Result 结果）**：
> "服务在最短时间恢复；看门狗上线后 WSL 静默关机问题消失；并且我把整个因果链和处置写进了复盘文档
> （避雷指南），团队后来遇到栈弹跳能照着诊断捷径快速定位，不用再从头摸一遍。"

#### 看门狗标本（真实代码）

> **标本路径**：`C:\CP6\scripts\wsl-keepalive.ps1`

```powershell
# 背景：WSL2 生命周期跟客户端连接走——最后一个 wsl.exe 退出后约 1 分钟整机 poweroff，
#       dockerd/容器/systemd 单元全部消失。必须常驻一个隐藏 wsl 会话钉住 VM。
$existing = Get-CimInstance Win32_Process -Filter "Name = 'wsl.exe'" |
    Where-Object { $_.CommandLine -match 'sleep\s+infinity' }
if ($existing) {
    Write-Output "keep-alive 已存活（PID $($existing.ProcessId -join ', ')），跳过"    # 幂等
    exit 0
}
Start-Process -WindowStyle Hidden wsl.exe -ArgumentList '-d','Ubuntu','-u','root','--','sleep','infinity'
Write-Output "keep-alive 已拉起"
```
**逐点讲**：
- `Get-CimInstance Win32_Process ... -match 'sleep\s+infinity'`：先查有没有已经在跑的 keep-alive 进程。
- **幂等**：已存活就 `exit 0` 跳过——脚本可以被反复触发（登录自启 + 手动），不会拉起一堆重复进程。
- `Start-Process -WindowStyle Hidden wsl.exe ... sleep infinity`：拉起一个**隐藏窗口**的 wsl 会话，
  里面跑 `sleep infinity` 永不退出，把 WSL2 VM"钉"住不让它自动关机。
- 调度是 **ONLOGON 触发**（登录即启）——因为这台是常驻登录的服务器。

> **这个 keep-alive 和第 5 节备份脚本里的"容器健康守卫"是配套的**：keep-alive 尽量让 WSL 不挂，
> 但万一还是弹跳了，备份脚本的守卫会"跳过本轮"而不是失败。**多层防御，每层都假设上一层可能失守。**

### 6.6 面试问答（第 6 节）

**Q：数据库日志文件突然涨到几百 GB，怎么回事？**
> A：最常见是 FULL 恢复模式却从不做日志备份——FULL 下日志只有做了日志备份才截断，不备份就无限累积。
> 其它可能：一个巨型事务（几百万行的 DELETE 放一个事务）、长时间未提交的事务卡住日志截断、大索引 REBUILD。
> 排查：`DBCC SQLPERF(LOGSPACE)` 看日志使用率，`DBCC OPENTRAN` 查最老活动事务，
> `sys.databases.log_reuse_wait_desc` 看日志为什么不能重用。治理：该备份就备份、大事务分批、
> 不需要时间点恢复就改 SIMPLE。日志文件一次性 shrink 可以，但别常规 shrink 数据文件——会造成严重碎片。

**Q：讲一次你处理过的生产故障。**（用 6.5 的 STAR，浓缩版）
> A：我们数据库跑在 Windows 的 WSL2 + Docker 里，某天全栈 500。查下来 dockerd 崩了，再查是 C 盘满了——
> WSL 分配 swap 失败导致虚拟盘 I/O error、dockerd 崩、数据库容器挂。根因是 Windows Update 缓存悄悄吃了 2.8GB
> 把 C 盘撑满。**这个故障最难的是根因和表象隔了四层**——磁盘→WSL→Docker→数据库。
> 止血是清 WU 缓存、重启 WSL 恢复服务；治本是加了个看门狗计划任务钉住 WSL 不让它静默关机，
> 并提磁盘扩容建议。最后写了复盘文档让团队以后能照着快速诊断。教训是：跨层依赖的系统，
> 排障不能只盯着报错那一层，要顺着依赖链往下游根因追。

**Q：审计日志这种只增不减的表怎么治理容量？**
> A：先装监控哨兵——我们有个脚本每 4 小时查 `sys.partitions`/`sys.allocation_units` 算这张表的行数和 MB
> （页数 × 8KB / 1024），超阈值（100 万行或 500MB）告警。关键原则是**只告警不自动删**——
> 归档/purge 策略必须人工裁决，因为审计日志可能有合规价值，自动删风险太大。长期治理可以做分区、
> 定期归档到冷存储、或按 retention 滚动删。

---

<a name="7-慢查询定位方法论"></a>
## 7. 慢查询定位方法论

> 面试问"某个页面查询很慢，你怎么查"——这一节给你一套**可复述的方法论**（现象→定位→分析→修复→验证），
> 外加一个基于 CP6 库存查询的完整排障剧本。

### 7.1 方法论五步

```
① 现象   —— 用户/监控报"XX 页面慢/超时"。先量化：多慢？偶发还是持续？何时开始？
   │
② 定位   —— 找到"到底是哪条 SQL 慢"：
   │        · DMV：sys.dm_exec_query_stats 找 top 耗时 SQL
   │        · Query Store：历史计划 + 回归对比
   │        · EF 日志：看 EF 实际生成的 SQL（很多"慢"是 EF 生成了烂 SQL）
   │
③ 分析   —— 拿到那条 SQL，看执行计划：
   │        · 有没有 Table Scan / Index Scan（该 Seek 却 Scan = 缺索引或 SQL 不 sargable）
   │        · 有没有 Key Lookup（缺覆盖列）、Hash/Sort 溢出 tempdb、估算行数 vs 实际行数偏差大（统计信息过期/参数嗅探）
   │
④ 修复   —— 对症下药：
   │        · 缺索引 → 建索引（含覆盖列）
   │        · SQL 不 sargable（WHERE 套函数）→ 改写
   │        · 一次拉太多 → 分页 / 只取需要的列
   │        · N+1 → Include / 投影 / 批量
   │        · 参数嗅探 → OPTION(RECOMPILE) / OPTIMIZE FOR
   │
⑤ 验证   —— 改完再测：执行计划变了吗？耗时降了吗？逻辑读（logical reads）降了吗？
            用数据说话，别凭感觉。别忘了看有没有副作用（这个索引拖慢了写入吗？）
```

### 7.2 定位工具详解

**① DMV：找 top 耗时 SQL**
```sql
-- 按总耗时排 top 20 慢查询（服务器级，缓存里的）
SELECT TOP 20
    qs.total_worker_time / 1000 AS total_cpu_ms,
    qs.total_elapsed_time / 1000 AS total_elapsed_ms,
    qs.execution_count,
    (qs.total_elapsed_time / qs.execution_count) / 1000 AS avg_ms,
    qs.total_logical_reads,
    SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text)
          ELSE qs.statement_end_offset END - qs.statement_start_offset)/2)+1) AS query_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
ORDER BY qs.total_elapsed_time DESC;
```
- `sys.dm_exec_query_stats`：每条缓存的查询计划的聚合统计（执行次数、CPU、耗时、逻辑读）。
- **`total_logical_reads`（逻辑读）是关键指标**——它是"读了多少个 8KB 页"，比时间更稳定
  （时间受当时负载影响，逻辑读只受查询和数据量影响）。**优化的目标常常就是"把逻辑读降下来"。**
- 排序维度选择：`total_elapsed_time` 找"总耗时大户"，`avg_ms` 找"单次最慢"，`execution_count × avg` 找"高频温水煮青蛙"。

**② Query Store（SQL Server 2016+）**：库级开关，把查询计划和运行时统计**持久化**（DMV 是内存里的，重启就没）。
最大价值是**计划回归对比**——"这条查询上周 50ms，今天 5s，是不是换了个烂计划？"Query Store 能看到历史计划、
甚至**强制回退到旧的好计划**（Force Plan）。面试知道"Query Store 能查计划回归、能 force plan"就够了。

**③ EF 日志：看 EF 到底生成了什么 SQL**（第 8 节详讲怎么开）。很多"慢查询"根因是 **EF 生成了糟糕的 SQL**
（N+1、笛卡尔爆炸、拉了全表列）。不看 EF 生成的实际 SQL，你在 DB 侧看到的只是"结果"，不知道"为什么 EF 这么写"。

### 7.3 执行计划怎么看（新手视角）

执行计划是"SQL Server 打算怎么执行这条查询"的图。SSMS 里 `Ctrl+M`（含实际计划）跑一下就出图。看几个关键信号：

| 看到什么 | 意味着 | 怎么办 |
|---|---|---|
| **Table Scan** | 全表扫（没有可用索引，或表是堆）| 建合适索引 |
| **Index Scan**（该 Seek 却 Scan）| 有索引但没走 seek——常因 WHERE 不 sargable（列套了函数）| 改写 SQL 让它 sargable |
| **Key Lookup / RID Lookup** | 走了非聚集索引但要回表捞额外列 | 把那些列 `INCLUDE` 进索引做覆盖 |
| **Hash Match / Sort** 且 spill 到 tempdb | 内存不够，排序/哈希溢出到磁盘 | 加内存 grant / 减少数据量 / 加支持排序的索引 |
| **估算行数 vs 实际行数差很多** | 统计信息过期 或 参数嗅探 | `UPDATE STATISTICS` / `OPTION(RECOMPILE)` |
| **粗箭头**（数据流量大） | 某一步处理了大量行 | 看能不能更早过滤（谓词下推）|

**sargable（可用索引的谓词）三条铁律**：
1. **不要在 WHERE 的列上套函数**：`WHERE CAST(CreateDate AS date) = @d` ❌ →
   `WHERE CreateDate >= @d AND CreateDate < @d+1` ✅（回顾第 1.4 节 CP6 SP 的写法）。
2. **不要在列上算术**：`WHERE Price * 1.1 > 100` ❌ → `WHERE Price > 100/1.1` ✅。
3. **避免前导通配 LIKE**：`WHERE Name LIKE '%abc'` ❌（用不上索引）→ `LIKE 'abc%'` ✅。

### 7.4 完整排障剧本（基于 CP6 库存查询场景，逼真虚构）

> 场景设定贴合 CP6 的 WMS 库存查询。这是一个**从报警到验证的完整剧本**，面试可以当"我怎么排障"的模板叙述。

**① 现象**
> 运维反馈：WMS 库存查询页（按仓库 + 物料筛选，带分页）**从 200ms 涨到 8 秒**，最近两天开始，持续性的。
> 量化：不是偶发，是每次都慢；时间点上和"上周导入了一批历史库存流水（表从 50 万行涨到 800 万行）"吻合。

**② 定位**
> 跑 `sys.dm_exec_query_stats` 的 top 耗时查询，第一名就是库存查询那条 SQL，`avg_ms≈8000`、
> `total_logical_reads` 高得离谱（几十万页 = 在扫大表）。同时开 EF 日志，看到 EF 生成的 SQL 是：
> ```sql
> SELECT ... FROM T_Stock s
> JOIN T_StockFlow f ON f.StockId = s.Id         -- 关联了 800 万行的流水表
> WHERE s.WarehouseCd = @w AND s.MaterialCd = @m
> ORDER BY s.CreateDate DESC
> OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
> ```

**③ 分析**
> 看执行计划：`T_StockFlow` 上是 **Table Scan**（800 万行全扫），因为 `StockFlow.StockId` 上没索引；
> 而且 `T_Stock` 的 `WarehouseCd + MaterialCd` 组合也没有合适复合索引，走了 Index Scan + Key Lookup。
> 估算行数和实际行数还差得多——统计信息在数据暴涨后没更新。三个问题叠加：**缺索引 + 缺覆盖 + 统计过期**。

**④ 修复**（分步，每步验证增量效果）
> 1. `UPDATE STATISTICS T_StockFlow; UPDATE STATISTICS T_Stock;`——先更新统计，让优化器估准。
> 2. 给流水表建索引：`CREATE INDEX IX_StockFlow_StockId ON T_StockFlow(StockId) INCLUDE (Qty, FlowDate);`——
>    消灭 800 万行 Table Scan。
> 3. 给库存表建覆盖索引：`CREATE INDEX IX_Stock_Wh_Mat ON T_Stock(WarehouseCd, MaterialCd, CreateDate DESC) INCLUDE (...)`——
>    过滤 + 排序 + 覆盖一把到位（`CreateDate DESC` 匹配 ORDER BY，避免额外 Sort）。
> 4. 检查是不是真的需要 JOIN 流水表——如果页面只展示库存不展示流水明细，那这个 JOIN 本身就是多余的
>    （很可能是 EF 的导航属性被无意 Include 了），去掉 JOIN 直接治本。**"最快的查询是不查"。**

**⑤ 验证**
> 重跑：执行计划变成 Index Seek，`logical_reads` 从几十万降到几百，`avg_ms` 从 8000 回到 150。
> 再确认副作用：新建的两个索引让 `T_StockFlow` 的写入（每次库存变动都插流水）多维护一个索引——
> 测了下插入耗时增加可忽略（窄索引），可接受。**用逻辑读 + 执行计划 + 耗时三个证据确认修复，而不是"感觉快了"。**

### 7.5 面试问答（第 7 节）

**Q：某个页面查询超时，你的排查步骤？**
> A：五步。① 现象量化——多慢、偶发还是持续、何时开始（常和某次数据增长/发版吻合）。
> ② 定位是哪条 SQL——用 `sys.dm_exec_query_stats` 找 top 耗时、看 `total_logical_reads`，
> 同时开 EF 日志看 EF 实际生成的 SQL（很多慢是 EF 生成了 N+1 或笛卡尔）。
> ③ 分析执行计划——找 Table Scan / Key Lookup / 估算与实际行数偏差 / tempdb spill。
> ④ 修复——缺索引就建（含覆盖列）、WHERE 套函数就改成 sargable、拉太多就分页/投影、N+1 就 Include 或批量。
> ⑤ 验证——比执行计划、比逻辑读、比耗时，用数据确认，还要看有没有拖慢写入的副作用。

**Q：`sys.dm_exec_query_stats` 里你最看哪个指标？**
> A：`total_logical_reads`（逻辑读页数）。因为它比时间稳定——时间受当时服务器负载影响，逻辑读只受查询本身和数据量影响。
> 优化的目标常常就是把逻辑读降下来。配合 `avg_ms` 找单次最慢、`execution_count` 找高频温水煮青蛙。

---

<a name="8-ef-core-性能落地清单"></a>
## 8. EF Core 性能落地清单（DB 视角复盘）

> 承接 Day 1 第 6 章的 EF 性能，这里换个视角——**从数据库侧看 EF 的性能问题长什么样**。

### 8.1 EF 日志怎么开（附 CP6 真实教训）

要看 EF 到底生成了什么 SQL，就得开 EF 的日志。两种方式：

**方式一：appsettings.json 配日志级别**
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.EntityFrameworkCore.Database.Command": "Information"   // ← 这行让 EF 打印每条 SQL
  }
}
```
`Microsoft.EntityFrameworkCore.Database.Command` 设成 `Information`，EF 会把每条执行的 SQL + 参数 + 耗时打进日志。

**方式二：`LogTo` / `EnableSensitiveDataLogging`（开发期）**
```csharp
optionsBuilder
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging();   // 连参数值都打（⚠️ 仅开发！会打印敏感数据）
```

#### ★ CP6 真实教训：EF 日志级别太低刷爆磁盘

> **标本**：`C:\CP6\CP6.WebApi\appsettings.json` 的 Logging 配置。

看 CP6 生产的实际配置——它**故意保守**：
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```
注意：**没有把 EF 的 `Database.Command` 设成 Information**（生产默认它继承不到那么细）。这是**血的教训**：

> CP6 的 **QA 环境曾经因为 EF 日志级别开太低（太详细），把每条 SQL 都打日志，磁盘被日志刷爆**
> （MEMORY 记录："QA 后端 EF 日志须降 Warning"）。这和第 6 节的磁盘满事故是同一类根因——**日志本身能撑爆磁盘**。

**教训要点（面试可讲）**：
- **EF 的 SQL 日志（`Database.Command=Information`）只在排障时临时开，查完立刻关**。生产常开 = 每个请求几十条 SQL 全打，
  I/O 暴涨 + 磁盘吃紧。
- 生产的稳态级别应该是 **Warning**（甚至更高）——只记异常，不记正常 SQL。
- **`EnableSensitiveDataLogging` 绝不上生产**——它会把参数值（可能含密码、个人信息）打进日志，是合规事故。
- 日志要配 **滚动 + 大小上限 + 保留策略**，别让它无限长（同备份目录的道理）。

> **这是一个绝佳的"我从教训里学到什么"面试素材**："我们 QA 环境曾因为 EF SQL 日志开太详细把磁盘刷爆，
> 之后我们定了规矩：SQL 级日志只在排障时临时开，生产稳态是 Warning，日志必须配滚动和大小上限。"

### 8.2 参数嗅探问题入门

回顾第 1.6 节：SQL Server 给参数化查询/SP **缓存执行计划**，计划是**按第一次执行时的参数值**生成的。
如果第一次的参数值不典型，缓存的计划对后续典型值可能很差——这就是**参数嗅探（Parameter Sniffing）**。

**在 DB 侧的表现**：同一条 SQL/SP，**时快时慢**，且"慢"往往在某次重启/重编译后突然出现（因为重新嗅探了个坏值）。

**缓解手段**：
- `OPTION (RECOMPILE)`：每次执行都重新编译计划（拿当前参数生成最优计划）。代价是编译开销，适合执行不频繁但参数分布悬殊的查询。
- `OPTIMIZE FOR (@p = 值)` / `OPTIMIZE FOR UNKNOWN`：让优化器按指定值（或"平均分布"）生成计划，不受第一次嗅探影响。
- 拆分查询、局部变量（局部变量不被嗅探，按平均基数估算）。

> **面试深度**：知道"参数嗅探导致 SP 时快时慢，缓解用 OPTION(RECOMPILE) 或 OPTIMIZE FOR"就已经超过大多数候选人了。

### 8.3 N+1 在 SQL 侧的表现

**N+1 问题**：查一个列表（1 条查询），然后循环里对每条访问导航属性又各触发一条查询（N 条）。
应用侧不明显，**DB 侧一看就露馅**：EF 日志里出现**一堆形状完全相同、只有参数不同的小查询**：

```sql
-- 1 条主查询
SELECT * FROM T_WorkOrder WHERE ...;
-- 然后 N 条这种（每个工单一条，只有 @id 变）：
SELECT * FROM T_WorkOrderProcess WHERE WorkOrderId = @id_1;
SELECT * FROM T_WorkOrderProcess WHERE WorkOrderId = @id_2;
SELECT * FROM T_WorkOrderProcess WHERE WorkOrderId = @id_3;
...  -- 几百上千条，DB 往返打满
```

**识别信号**：EF 日志/DMV 里，某条"形状一样、`execution_count` 极高、单条很快但累计很慢"的查询——**温水煮青蛙**。

**修复**：
- `.Include(w => w.Processes)`：让 EF 用 JOIN 一次拉回（注意多个 Include 可能笛卡尔爆炸，用 `AsSplitQuery` 拆）。
- **投影**：`.Select(w => new Dto { ..., ProcessCount = w.Processes.Count() })`——只取需要的，EF 生成一条聚合 SQL。
- 手动批量：一次 `WHERE WorkOrderId IN (@ids)` 捞回全部再在内存分组。

### 8.4 批量插入的选择

一次插几千几万行，`AddRange + SaveChanges` 未必最优：

| 方式 | 机制 | 适用规模 |
|---|---|---|
| `AddRange` + `SaveChanges` | EF 批量化 INSERT（EF Core 会合并成多值 INSERT，但仍走 ChangeTracker）| 几十~几千行 |
| `ExecuteSqlRaw` 多值 INSERT | 手写 SQL，绕过追踪 | 几千行 |
| **SqlBulkCopy**（`BULK INSERT`）| ADO.NET 的批量复制 API，最快，走 TDS 批量协议 | 几万~几百万行 |
| 第三方 EFCore.BulkExtensions | 封装 SqlBulkCopy，EF 风格 API | 大批量 |

> **判据**：**几千行内 AddRange 够了**（EF Core 会自动批处理）；**上万行考虑 SqlBulkCopy**——它绕过 ChangeTracker
> 和逐行 INSERT，用批量协议，能快一两个数量级。CP6 的高频写（报工、库存流水）如果单次批量很大，就是 SqlBulkCopy 的场景。
> 注意 SqlBulkCopy 绕过 DbContext，**也就绕过了第 2 节的审计拦截**——批量导入的审计要另想办法。

### 8.5 面试问答（第 8 节）

**Q：怎么看 EF 生成的 SQL？有什么注意事项？**
> A：把 `Microsoft.EntityFrameworkCore.Database.Command` 日志级别设 Information，EF 就打印每条 SQL + 参数 + 耗时；
> 开发期还能用 `LogTo` + `EnableSensitiveDataLogging`。但注意事项很重要——我们 QA 环境曾因为 EF SQL 日志开太详细
> 把磁盘刷爆。所以 SQL 级日志只在排障时临时开，生产稳态是 Warning；`EnableSensitiveDataLogging` 绝不上生产
> （会打印参数值，可能含敏感数据）；日志必须配滚动和大小上限。

**Q：N+1 在数据库侧长什么样？怎么修？**
> A：EF 日志或 DMV 里会出现一堆**形状完全相同、只有参数不同的小查询**，execution_count 极高，单条快但累计慢，
> 是典型的温水煮青蛙。修复用 `.Include`（一次 JOIN 拉回，注意笛卡尔用 AsSplitQuery）、投影只取需要的、
> 或手动 `WHERE IN (@ids)` 批量捞。

**Q：一次插 10 万行怎么做？**
> A：不用 AddRange——那还是走 ChangeTracker，慢。用 SqlBulkCopy（或 EFCore.BulkExtensions 封装），
> 它走 TDS 批量协议，绕过逐行 INSERT 和追踪，能快一两个数量级。代价是绕过 DbContext 也绕过了我们的审计拦截，
> 批量导入的审计要单独处理。几千行以内的话 AddRange 就够了，EF Core 会自动批处理。

---

<a name="9-数据库部署与版本化"></a>
## 9. 数据库部署与版本化

### 9.1 EF 迁移在 CI/CD 里的位置

**EF Migration** 把"数据库结构变更"变成**代码里的、有顺序的、可版本控制的**迁移文件（就是第 1.4 节那个 SP 迁移的形态）。
每个迁移有 `Up()`（前进）和 `Down()`（回滚），迁移历史记在库里的 `__EFMigrationsHistory` 表。

**迁移在 CI/CD 的三种应用时机**：

| 方式 | 怎么做 | 优缺点 |
|---|---|---|
| **应用启动时自动迁移** | 代码里 `db.Database.Migrate()` | 简单，CP6 就是这个（`runbook.md`：cp6-api 启动时 `Migrate()` 自动建表 + 跑幂等种子）。缺点：多实例并发启动可能撞、迁移失败卡启动 |
| **部署流水线里显式跑** | `dotnet ef database update` 或生成 SQL 脚本手动执行 | 可控、可 review 生成的 SQL、可在部署门禁里卡。生产更推荐 |
| **生成幂等 SQL 脚本给 DBA** | `dotnet ef migrations script --idempotent` | DBA 主导的严格环境，脚本能反复安全跑 |

### 9.2 CP6 部署纪律：每波恰一迁移

> CP6 的 MEMORY 反复强调一条铁律：**「每波恰一迁移」**（每个开发波次只产生恰好一个 EF 迁移）。

这条纪律的价值：
- **迁移链清晰**：一个功能波 = 一个迁移，`__EFMigrationsHistory` 和代码 PR 一一对应，回滚粒度明确。
- **避免迁移碎片**：不会出现"一个功能拆成 5 个零碎迁移，还有互相依赖"的混乱。
- **线上应用可预期**：`Migrate()` 每次只补一个迁移的差，风险可控。

CP6 的实际线上应用方式（runbook + MEMORY）：**cp6-api 容器启动时 `db.Database.Migrate()` 自动补差**——
库已最新则 no-op，落后则应用缺的迁移。MEMORY 里每个波次都记着"恰一迁移 XxxYyy 线上已应用"，就是这条纪律的执行痕迹。

> **⚠️ 迁移的坑（面试可讲）**：`Migrate()` 自动迁移在**多实例**下有并发风险（两个实例同时启动同时跑迁移）。
> CP6 目前单实例可接受；多实例要加迁移锁或改成"部署流水线单独跑一次迁移，实例只连不迁"。

### 9.3 种子数据的幂等模式：NOT EXISTS 防重插

> **标本路径**：`C:\CP6\docs\seeds\mes-permission-seed.sql`

种子数据（seed）= 系统运行必需的基础数据（权限点、菜单、默认角色、字典）。种子的**铁律是幂等**——
同一个种子脚本跑一次和跑一百次，结果必须一样（不能每跑一次多插一份）。CP6 的标准做法是 **`NOT EXISTS` 守卫**：

```sql
/* 冪等性: 各 INSERT 前に NOT EXISTS チェック（TenantId+MenuId+ActionCode 単位）→ 重複実行安全。 */

INSERT INTO Sys_MenuAction (Id, MenuId, ActionCode, ActionName, Sort, CreateDate, TenantId)
SELECT NEWID(), a.MenuId, a.ActionCode, a.ActionName, a.Sort, SYSDATETIME(), t.Id
FROM @Actions a
CROSS JOIN (SELECT Id FROM Sys_Tenants) t                       -- ★ 逐租户展开（多租户）
WHERE NOT EXISTS (                                              -- ★ 幂等守卫：已存在就不插
    SELECT 1 FROM Sys_MenuAction ma
    WHERE ma.TenantId = t.Id AND ma.MenuId = a.MenuId AND ma.ActionCode = a.ActionCode
);
```

**逐点精读**：
- **`WHERE NOT EXISTS (...)`**：插入前检查"这个 (租户, 菜单, 动作) 组合是否已存在"，存在就跳过。
  这样脚本可以**反复执行**——第一次插全部，之后每次都是"已存在→跳过"，零重复。这是种子幂等的**标准形态**。
- **`CROSS JOIN (SELECT Id FROM Sys_Tenants) t`**：CP6 是多租户系统，权限要给**每个租户**都种一份。
  `CROSS JOIN` 把 25 个动作定义 × N 个租户展开，一次给所有租户种。**新增租户后重跑，NOT EXISTS 保证只给新租户补种，老租户跳过。**
- **`NEWID()` 主键**：每行生成新 GUID 主键。注意幂等判断**不是靠主键**（主键每次 NEWID 都不同），
  而是靠**业务唯一键**（TenantId+MenuId+ActionCode）。**"幂等要判业务键，不是判主键"是个易错点。**
- **事务包裹**：整个脚本 `BEGIN TRANSACTION ... COMMIT`，配 `SET XACT_ABORT ON` + `TRY/CATCH ROLLBACK`——
  要么全成功，要么全回滚，不留半拉子状态。

**这个种子还有一个"真相源"设计（CP6 特色，面试可讲）**：文件头注释写着
```
★正本は C#：CP6.WebApi/Seed/MesPermissionSeed.cs（起動時逐租户冪等種子）。
  本 SQL は同一集合の文書留档・手動投入用であり、C# と 1:1 一致。乖離時は C# を正とする。
```
> **种子的真相源是 C# 代码**（应用启动时逐租户幂等跑），SQL 文件是"同一集合的文档留档 + 手动投入用"，
> 两者 1:1 一致，**冲突时以 C# 为准**。这是"避免种子逻辑双份漂移"的治理——面试讲这个能体现对"单一真相源"的意识。

### 9.4 多环境（开发/QA/生产）数据库管理

CP6 用 **`appsettings.{Environment}.json` 分层覆盖**管理多环境（见第 10 节）：
- **开发（Development）**：本地连 `localhost`，可开详细日志、宽松 CORS、禁 CSRF。
- **QA**：接近生产但独立库；**日志级别是踩过坑的重点**（第 8.1 节：QA 曾被 EF 日志刷爆，须降 Warning）。
- **生产（Docker）**：`appsettings.Docker.json` + 环境变量注入密钥，收紧 CORS/CSRF/Cookie Secure。

runbook §4 有一份"生产开关核对清单"：`Security:Csrf:Enabled=true`、CORS 收紧到真实域、Cookie `Secure`、HTTPS——
**"dev 的默认值上生产前逐项核"**，防止把开发的宽松配置带上生产。

### 9.5 面试问答（第 9 节）

**Q：数据库结构变更怎么管理和上线？**
> A：用 EF Migration——结构变更变成有顺序、能版本控制、有 Up/Down 的迁移文件，历史记在 `__EFMigrationsHistory`。
> 我们纪律是"每波恰一迁移"，一个功能波对应一个迁移，回滚粒度清晰。上线方式是 cp6-api 容器启动时 `Migrate()` 自动补差，
> 库最新则 no-op。要注意多实例并发迁移风险，严格环境可以改成流水线单独跑迁移、或生成幂等 SQL 脚本给 DBA。
> 连存储过程我们都用迁移管理（migrationBuilder.Sql），SP 变更也进 Git、随部署应用。

**Q：种子数据怎么保证反复执行不出错？**
> A：幂等——每个 INSERT 前 `WHERE NOT EXISTS` 检查业务唯一键（不是主键，主键每次 NEWID 都不同）存不存在，
> 存在就跳过。多租户用 `CROSS JOIN Sys_Tenants` 逐租户展开，新增租户重跑只补新租户、老的跳过。
> 整体事务包裹 + XACT_ABORT + TRY/CATCH ROLLBACK，全成功或全回滚。我们种子真相源是 C# 启动种子，
> SQL 是文档留档，1:1 一致、冲突以 C# 为准，避免双份漂移。

---

<a name="10-docker-里的-sql-server"></a>
## 10. Docker 里的 SQL Server

> **标本路径**：`C:\CP6\docker-compose.yml`、`C:\CP6\CP6.WebApi\appsettings.Docker.json`

### 10.1 CP6 真实拓扑

CP6 整个栈用 `docker-compose` 编排，SQL Server 是其中一个容器：

```yaml
services:
  cp6-db:
    image: mcr.microsoft.com/mssql/server:2022-latest      # 官方 SQL Server 2022 镜像
    container_name: cp6-db
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD in .env}"   # 从 .env 注入
      MSSQL_PID: "Developer"                                # Developer 版（免费，功能=企业版，仅限非生产）
    ports:
      - "1433:1433"                                         # 暴露给宿主，本地 dotnet run 可连
    volumes:
      - cp6-db-data:/var/opt/mssql                          # ★ 命名卷持久化数据
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' -b"]
      interval: 10s
      retries: 20
      start_period: 40s                                    # 给 SQL Server 40s 启动缓冲
    restart: unless-stopped
```

整个栈：`cp6-db`（SQL Server）+ `cp6-redis` + `cp6-mq`（RabbitMQ）+ `cp6-kafka` + `cp6-api`（.NET 后端）
+ `cp6-web`（前端）+ `cp6-cloudflared`（内网穿透）。而这**一整套又跑在 Windows 的 WSL2 里**——
这就是第 6 节磁盘满事故里"Windows → WSL2 → Docker → 容器"四层结构的由来。

### 10.2 连接字符串管理：环境变量注入

**核心原则：镜像/配置文件里放占位符，真实密钥运行时从环境变量注入。**

`appsettings.Docker.json`（进 Git，**不含真密码**）：
```json
"ConnectionStrings": {
  "_comment": "实际密码通过 docker-compose env var ConnectionStrings__DefaultConnection 覆写",
  "DefaultConnection": "Server=cp6-db;Database=CP6DB;User Id=sa;Password=__OVERRIDE_VIA_ENV__;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

`docker-compose.yml` 里注入真值（`${MSSQL_SA_PASSWORD}` 来自 `.env`，`.env` 在 `.gitignore`）：
```yaml
cp6-api:
  environment:
    ConnectionStrings__DefaultConnection: "Server=cp6-db;Database=CP6DB;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=True"
```

**几个关键点（面试可讲）**：
- **`Server=cp6-db`**：容器间用**服务名**当主机名（Docker 内置 DNS 解析到容器 IP），不是 `localhost`。
  这是 Docker 网络的基础——`localhost` 在容器里指容器自己。（对比 `appsettings.json` 本地开发是 `Server=localhost`。）
- **`ConnectionStrings__DefaultConnection`**：双下划线 `__` 是 .NET 配置的**分层分隔符**，
  环境变量 `ConnectionStrings__DefaultConnection` 覆盖 JSON 里的 `ConnectionStrings:DefaultConnection`。
  **这是 .NET 配置注入的标准姿势，密钥永不进 Git。**
- **`TrustServerCertificate=True`**：容器内 SQL Server 用自签证书，跳过证书链校验（内网可接受，公网要用真证书）。
- **`MultipleActiveResultSets=True`（MARS）**：允许一个连接上同时有多个活动结果集（EF 某些场景需要）。

### 10.3 数据卷持久化（生死攸关）

```yaml
volumes:
  cp6-db-data:            # 命名卷，SQL Server 数据落这里
```
- 容器是**无状态**的——`docker rm` 容器数据不丢，因为数据在**命名卷** `cp6-db-data` 里（挂到容器的 `/var/opt/mssql`）。
- **⚠️ 致命红线（runbook 原文）**：`docker compose down -v` 的 **`-v` 会删命名卷 → 整库连词条一起抹掉**。
  同机重部署只能 `docker compose down`（**不带 -v**）。**"down 带不带 -v 是删不删库的分界线"——这是面试可讲的血泪操作纪律。**
- i18n 发布快照单独挂了个卷 `cp6-api-i18n:/app/wwwroot/i18n`——因为那是运行期写的资产，不挂卷容器重建就回滚。

### 10.4 内存限制

SQL Server 在容器里默认会**尽量吃内存**（它认为整个可见内存都是它的）。生产要限制：
- **compose 层**：给 `cp6-db` 加 `deploy.resources.limits.memory` 或 `mem_limit`，防止它把宿主内存吃光饿死别的容器。
- **SQL Server 层**：`sp_configure 'max server memory'` 设上限，给 OS 和其他进程留余量。
- **WSL 层**：`.wslconfig` 里限制 WSL2 整体内存/swap（CP6 MEMORY 提过 `.wslconfig` 的 swap 设为 4GB）。

> CP6 的 compose 当前没显式设 `cp6-db` 的内存 limit——**这是一个可讲的改进点**：
> "在内存紧张的单机多容器环境，应该给数据库容器和 SQL Server 都设内存上限，防止它吃光宿主内存导致别的容器 OOM。"

### 10.5 面试问答（第 10 节）

**Q：你们数据库怎么部署的？连接字符串里的密码怎么管？**
> A：SQL Server 跑在 docker-compose 编排的容器里（官方 mssql 2022 镜像），数据放命名卷持久化。
> 密码管理原则是配置文件放占位符、真值运行时从环境变量注入——`appsettings.Docker.json` 进 Git 但密码是
> `__OVERRIDE_VIA_ENV__`，compose 里用 `${MSSQL_SA_PASSWORD}`（来自 gitignored 的 .env）注入，
> 通过 .NET 的 `ConnectionStrings__DefaultConnection` 双下划线分层覆盖。密钥永不进 Git。
> 容器间用服务名 `cp6-db` 当主机名（Docker 内置 DNS），不是 localhost。

**Q：Docker 里数据库数据怎么不丢？有什么要特别小心的？**
> A：数据在命名卷里（挂到容器 `/var/opt/mssql`），容器删了重建数据还在。最要命的是 `docker compose down -v`——
> `-v` 会删命名卷，整库连数据带多语言词条一起没。所以同机重部署只能用不带 `-v` 的 `down`。
> 我们把这条写进了 runbook 当致命红线。另外备份也不能只靠这个卷（它和库在同一台机器），要按 3-2-1 异地。

---

<a name="11-面试排障题模拟"></a>
## 11. 面试排障题模拟（三道开放题标准框架）

> 开放题考的不是"标准答案"，而是**你有没有一套结构化的处置思维**。所有生产故障题都套同一个骨架：
>
> **① 先止血（恢复服务）→ ② 定位（找到是什么）→ ③ 根因（为什么发生）→ ④ 治本（防复发）→ ⑤ 复盘（沉淀）**
>
> 面试时**先说这个框架，再往里填细节**——展现的是方法论，不是运气。切记：**先止血再找根因**
> （用户在等，不能为了查根因让系统一直挂），但**不能只止血不治本**（否则明天复发）。

### 11.1 「系统突然变慢，你怎么查？」

**① 止血（先判断严不严重、要不要立即降级）**
> 先确认影响面：全站慢还是某功能慢？所有用户还是部分？如果全站快挂了，先考虑临时降级
> （限流、关掉非核心功能、加实例）争取排查时间。

**② 定位（分层排查，从上往下）**
> - **应用层**：CPU/内存/线程池是否打满？有没有异常刷屏？最近有没有发版（时间点最能说明问题）？
> - **数据库层**：`sys.dm_exec_requests` 看有没有长时间运行/阻塞的查询；`sys.dm_exec_query_stats` 找 top 耗时 SQL；
>   看有没有**阻塞链**（`blocking_session_id`）——一个长事务锁住表，全体排队。
> - **资源层**：磁盘满没有（回想第 6 节！）、内存够不够、网络。
> - **依赖层**：Redis/MQ/外部 API 是不是挂了拖慢了整条链。

**③ 根因**
> 常见几类：某次发版引入了 N+1 或缺索引的慢查询 / 数据量增长到临界（第 7 节剧本）/ 一个大事务锁表
> / 参数嗅探换了坏计划 / 磁盘/内存资源见底 / 缓存失效击穿。**用第 7 节的方法论坐实到具体那条 SQL 或那个资源。**

**④ 治本**
> 对症：加索引/改写 SQL/加分页/修 N+1/拆大事务/加缓存/扩资源。**改完要验证**（执行计划 + 逻辑读 + 耗时）。

**⑤ 复盘**
> 写故障报告：时间线、根因、处置、改进项。加监控告警让下次能提前发现（慢查询阈值告警、资源水位告警）。

### 11.2 「某个页面查询超时，怎么办？」

> 这题比"系统变慢"更聚焦——**已经缩小到一个页面/一条查询**了，直接进第 7 节的五步方法论。

**① 止血**：如果这个页面是核心且超时严重，先加个查询超时/降级（返回缓存的旧数据 or 友好提示），别让它拖垮线程池。

**② 定位**：开 EF 日志看这个页面**实际发了哪些 SQL**（往往不止一条，可能 N+1）；把最慢那条拎出来。

**③ 分析**：执行计划找 Table Scan / Key Lookup / 估算偏差；看 `logical_reads`。

**④ 修复**（按第 7.4 剧本）：缺索引→建覆盖索引；WHERE 套函数→改 sargable；一次拉太多→分页 + 只取需要列；
N+1→Include/投影/批量；统计过期→UPDATE STATISTICS。

**⑤ 验证**：执行计划从 Scan 变 Seek，逻辑读和耗时下降，确认没拖慢写入。

> **加分点**：主动提"最快的查询是不查"——先问这个页面**是不是真需要查这么多**？
> 能不能加缓存（这种数据几秒钟的旧值可接受吗）？能不能异步/预计算（仪表盘就是走了 SP 预聚合，见第 1、4 节）？

### 11.3 「数据库磁盘满了，怎么应急？」

> 这题直接呼应第 6 节的真实事故——**如果你把那个 STAR 故事讲出来，这题就是满分**。

**① 止血（争取空间，让库先能写）**
> - **快速腾空间**：清临时文件、旧日志、旧备份（**但别删还没验证的最新备份！**）、Windows Update 缓存（第 6 节根因！）。
> - **如果是日志文件 `.ldf` 撑满**：先 `BACKUP LOG`（FULL 模式）截断日志，或临时改 SIMPLE，再按需 shrink 那一次。
> - **如果是数据文件**：加一块盘 / 给数据文件加一个新文件到别的盘 / 清理可归档的历史数据。
> - **最坏情况**：数据库已经因为磁盘满进了**只读/suspect** 状态，腾出空间后可能要手动恢复。

**② 定位（谁吃了磁盘）**
> `xp_fixeddrives` 看各盘剩余；OS 层用工具找大文件（第 6 节就是 WU 缓存 2.8GB）；
> 库内 `sys.database_files` 看数据/日志文件各多大、用了多少。

**③ 根因**
> 是**日志暴涨**（FULL 不备份日志 / 大事务，第 6.2 节）？还是**数据自然增长**到临界？
> 还是**别的东西**吃了盘（WU 缓存、日志文件、临时文件——第 6 节的四层链）？定位到"到底是谁、为什么"。

**④ 治本**
> - 日志暴涨 → 定期日志备份 or 改 SIMPLE；大事务分批。
> - 数据增长 → 归档/分区/清理策略（呼应第 6.4 节审计表监控：装哨兵、定阈值、人工裁决归档）。
> - 资源根子 → **扩容磁盘**（第 6 节结论：清缓存是治标，扩容才是治本）+ 加**磁盘水位告警**（别再等满了才知道）。
> - 依赖链 → 像 CP6 那样加**看门狗**防 WSL 静默关机（第 6.5 节 keep-alive）。

**⑤ 复盘**
> 写因果链文档（第 6.5 节那张图）；把"磁盘剩余"纳入监控告警；定期审视增长趋势（第 6.4 节的容量监控脚本就是这个思路）。

> **面试完整作答示范**：
> "磁盘满我会先止血——快速找可清理的空间（旧备份、临时文件、系统更新缓存），如果是日志撑满就先备份日志截断
> 或临时改 SIMPLE。然后定位是谁吃的盘、库里数据还是日志文件、还是操作系统层别的东西。
> 我们真出过一次这类事故：数据库跑在 WSL+Docker 里，C 盘被 Windows Update 缓存吃满 2.8GB，
> 导致 WSL 分不到 swap、虚拟盘 I/O error、dockerd 崩、数据库全栈挂——根因和表象隔了四层。
> 止血是清缓存重启，治本是加了看门狗防 WSL 静默关机 + 提磁盘扩容 + 加磁盘水位告警，最后写了复盘文档。
> 核心教训是：磁盘余量要提前监控告警，别等满了才救火；而且跨层依赖的系统排障要顺着依赖链追到最下游根因。"

---

<a name="12-章末"></a>
## 12. 章末：面试题 15 问 + 自测清单 + 动手练习

### 12.1 面试题 15 问（详细答案，侧重生产场景开放题）

**Q1. 什么时候用存储过程，什么时候用应用层代码？**
> 默认用 ORM（EF）写业务，因为可测试、可版本控制、可移植。数据密集 + 往返敏感 + 逻辑稳定的场景
> （报表、仪表盘、复杂聚合）下沉到 SP。我们项目 MES 仪表盘就是 SP + Dapper，一个页面 4 个聚合从 4 次往返变 1 次 EXEC，
> 配覆盖索引走 index seek。而且用 EF 迁移管理 SP，解决版本控制难题。稳定很关键——频繁变的逻辑不适合 SP（改动成本高）。

**Q2. 覆盖索引和普通索引区别？键列和 INCLUDE 列怎么选？**
> 覆盖索引把查询 SELECT 的列通过 INCLUDE 放进索引叶子层，扫索引就能拿全部列不用回表（Key Lookup）。
> 键列用于定位、排序、JOIN 条件（WHERE/ORDER BY），INCLUDE 列只是顺带存避免回表（SELECT 列表但不参与过滤）。
> 别把 SELECT 列全塞键列——索引会又大又慢、维护成本高。

**Q3. 为什么现代系统慎用触发器？审计不用触发器怎么做？**
> 触发器是隐式的——逻辑藏在库里不在调用代码里，改一张表连锁触发看不见的逻辑，调试时 C# 里找不到，
> 还有事务内同步跑重活的性能黑洞、批量操作陷阱（新手假设 inserted 单行）、难测难版控。
> 我们审计用 EF 的 SaveChanges 拦截：重写 SaveChangesAsync，保存前遍历 ChangeTracker 里 IAuditable 实体逐字段 diff，
> 业务行 + 审计行同事务原子提交。好处是显式可测、拿得到应用层用户、敏感字段能过滤；代价是走裸 SQL/ExecuteUpdate 会绕过，
> 触发器的唯一不可替代优势是绕不过，强合规才回到触发器。

**Q4. 完整讲一遍 SQL 注入原理和防护。**
> 本质是数据被当代码执行。登录框例子：拼接 SQL 时用户名输入 `' OR 1=1 --`，前引号闭合用户名引号，
> OR 1=1 恒真匹配全表，`--` 注释掉密码校验，无密码登录。变种能 DROP TABLE、UNION 偷数据、时间盲注。
> 防护核心是数据代码分离——参数化查询：语句结构和值分两条通道，值永远作为纯数据不被解析成语法。
> EF 的 LINQ 自动参数化、裸 SQL 用 FromSqlInterpolated、Dapper 用匿名对象、SP 内动态 SQL 用 sp_executesql
> （只能参数化值，表名列名要白名单 + QUOTENAME）。再加最小权限、输入校验、报错不外泄纵深防御。

**Q5. EF 和 Dapper 怎么选？能共存吗？**
> 能共存，我们项目就是。写操作和领域模型用 EF（变更追踪、迁移、导航属性省事），重查询/报表/仪表盘/SP 调用用 Dapper
> （SQL 掌控、映射轻、往返少、无追踪开销）。判据：返回投影 DTO 用 Dapper，返回实体用 EF。
> 我们 99% 走 EF，MES 仪表盘用 Dapper + SP，控制器里两条路并列还做了带耗时的 A/B 端点验证提速有效。

**Q6. FULL 和 SIMPLE 恢复模式的区别？FULL 下日志暴涨怎么回事？**
> SIMPLE 下日志 checkpoint 后自动截断、不能做日志备份、只能恢复到最近完整/差异备份；
> FULL 下日志保留到你做日志备份才截断、能做时间点恢复。FULL 下日志暴涨的头号原因就是设了 FULL 却从不做日志备份——
> 日志无限累积撑爆磁盘。治理：定期日志备份，或不需要时间点恢复就改 SIMPLE。

**Q7. RTO 和 RPO 是什么？怎么优化？**
> RPO 是能容忍丢多少数据（备份频率决定），RTO 是能容忍停多久（恢复速度决定）——RPO 管丢多少、RTO 管停多久。
> 缩短 RPO：更频繁备份 / 日志备份 / 高可用。缩短 RTO：更快恢复流程 / 热备 / 演练过的 runbook。
> 我们现状 RPO 约 4 小时（每 4h 完整备份），改进方向是加日志备份压到 15 分钟。

**Q8. 讲讲你们的备份方案，有什么可改进？**
> PowerShell + 计划任务每 4h 完整备份，容器内 `BACKUP DATABASE WITH INIT, COMPRESSION, CHECKSUM`，
> docker cp 拷到 Windows，保留 14 天滚动，多层就绪守卫（容器 running/healthy/库 ONLINE，因为容器 healthy 不等于库 ONLINE），
> 密钥不入脚本。可改进：最大问题是没异地副本（备份和库同机，整机故障同归于尽，要 3-2-1）、
> RPO 4 小时偏松（加日志备份）、没恢复演练（没演练过的备份等于薛定谔的备份，定期试还原测 RTO）、加备份失败告警。

**Q9. 数据库日志文件涨到几百 GB，怎么排查处理？**
> 最常见 FULL 模式不做日志备份。排查：`DBCC SQLPERF(LOGSPACE)` 看使用率，`DBCC OPENTRAN` 查最老活动事务，
> `sys.databases.log_reuse_wait_desc` 看日志为什么不能重用（可能是备份、长事务、复制卡住）。
> 处理：该备份就 BACKUP LOG、大事务分批、不需要时间点恢复改 SIMPLE。日志文件一次性 shrink 可以，
> 但别常规 shrink 数据文件——会造成严重索引碎片，恶性循环。

**Q10. 讲一次你处理过的生产故障。（金牌题）**
> 用第 6.5 的 STAR：数据库跑在 Windows 的 WSL2+Docker 里，某天全栈 500。查下来 dockerd 崩了、
> C 盘满了、根因是 Windows Update 缓存吃了 2.8GB——WSL 分不到 swap 导致虚拟盘 I/O error、dockerd 崩、数据库容器挂。
> 最难的是根因和表象隔了四层（磁盘→WSL→Docker→数据库）。止血清缓存重启，治本加看门狗钉住 WSL 防静默关机 + 提扩容 + 加告警，
> 写复盘文档。教训：跨层依赖排障要顺依赖链追下游根因，别只盯报错那层；资源余量要提前监控告警。

**Q11. 某页面查询超时，你的完整排查步骤？**
> 五步：现象量化（多慢、偶发/持续、何时起，常和数据增长或发版吻合）→ 定位（DMV 找 top 耗时 SQL + 看 logical_reads，
> 开 EF 日志看实际生成的 SQL）→ 分析执行计划（Table Scan / Key Lookup / 估算偏差 / tempdb spill）→
> 修复（建覆盖索引 / 改 sargable / 分页投影 / 修 N+1 / 更新统计）→ 验证（执行计划变 Seek、逻辑读和耗时降、无写入副作用）。
> 加分：先问是不是真要查这么多，能不能缓存/预计算。

**Q12. 慢查询定位你最看哪些指标？sargable 是什么？**
> 最看 `total_logical_reads`——逻辑读比时间稳定，只受查询和数据量影响，优化目标常是把逻辑读降下来。
> sargable 指谓词能用上索引：不在 WHERE 的列上套函数（`CAST(date列)=x` 要改成范围 `>= x AND < x+1`）、
> 不在列上做算术、避免前导通配 `LIKE '%x'`。不 sargable 会让索引失效变全表扫，是慢查询头号杀手。

**Q13. N+1 在数据库侧长什么样？批量插入 10 万行怎么做？**
> N+1 在 EF 日志/DMV 里是一堆形状相同只有参数不同的小查询、execution_count 极高、单条快累计慢，温水煮青蛙。
> 修：Include（一次 JOIN，笛卡尔用 AsSplitQuery）、投影只取需要、`WHERE IN(@ids)` 批量。
> 插 10 万行用 SqlBulkCopy（走 TDS 批量协议，绕过逐行 INSERT 和追踪，快一两个数量级），
> 几千行内 AddRange 就够（EF Core 自动批处理）。注意 SqlBulkCopy 绕过 DbContext 也绕过审计拦截。

**Q14. EF 迁移怎么上线？种子数据怎么保证幂等？**
> EF Migration 把结构变更变成有序、版本控制、有 Up/Down 的迁移，历史记 `__EFMigrationsHistory`。
> 我们纪律"每波恰一迁移"，cp6-api 启动时 `Migrate()` 自动补差。多实例要注意并发迁移风险。连 SP 都用迁移管理。
> 种子幂等靠每个 INSERT 前 `WHERE NOT EXISTS` 检查业务唯一键（不是主键，主键每次 NEWID 都变）、
> 多租户 CROSS JOIN 逐租户展开、事务包裹。真相源是 C# 启动种子，SQL 是文档留档 1:1 一致。

**Q15. Docker 里跑数据库，密钥和数据怎么管？最大的坑是什么？**
> 密钥：配置文件放占位符、真值运行时环境变量注入（`ConnectionStrings__DefaultConnection` 双下划线分层覆盖，
> compose 里 `${MSSQL_SA_PASSWORD}` 来自 gitignored 的 .env），密钥永不进 Git。容器间用服务名当主机名不是 localhost。
> 数据：命名卷持久化，容器删了数据还在。最大的坑是 `docker compose down -v`——`-v` 删命名卷、整库连词条一起没，
> 同机重部署只能用不带 -v 的 down。而且备份不能只靠这个卷（和库同机），要异地。

### 12.2 自测清单

打钩自查，答不上的回去重读对应节：

- [ ] 能写出 `CREATE PROCEDURE` 骨架，说清结果集/OUTPUT/RETURN 三种返回的区别（§1.2）
- [ ] 能讲存储过程 vs 应用层代码的两派论据 + 现代 ORM 时代定位（§1.3）
- [ ] 能看着 CP6 的 MES SP 讲清"为什么用 SP""覆盖索引怎么加速""为什么日期范围写半开区间"（§1.4）
- [ ] 能解释键列 vs INCLUDE 列、普通视图 vs 索引视图（§1.5）
- [ ] 能讲触发器为什么慎用 + SaveChanges 拦截替代方案的取舍（§2）
- [ ] 能完整复述 SQL 注入原理（`' OR 1=1 --` 逐字拆解）+ 四种参数化写法（§3）
- [ ] 能解释 `sp_executesql` vs `EXEC(@sql)`，以及"标识符白名单、值参数化"（§3.2）
- [ ] 能讲清 EF vs Dapper 选型 + 为什么 CP6 报表用 Dapper（§4）
- [ ] 能背 FULL vs SIMPLE 恢复模式、RTO/RPO 定义（§5）
- [ ] 能逐步讲 CP6 备份脚本的就绪守卫（容器 running/healthy/库 ONLINE 三层）+ INIT/COMPRESSION/CHECKSUM（§5.4）
- [ ] 能说出这套备份的 5 个改进点（异地/日志备份/演练/告警/差异）（§5.5）
- [ ] 能讲日志暴涨的 5 个原因 + shrink 的争议（§6.2、§6.3）
- [ ] 能完整讲磁盘满全栈停机事故（四层因果链 + STAR + 看门狗）（§6.5）
- [ ] 能复述慢查询五步方法论 + 执行计划关键信号 + sargable 三铁律（§7）
- [ ] 能讲 EF 日志怎么开 + QA 磁盘被日志刷爆的教训 + 参数嗅探（§8）
- [ ] 能讲 N+1 在 DB 侧的表现 + 批量插入选型（§8.3、§8.4）
- [ ] 能讲"每波恰一迁移"+ NOT EXISTS 幂等种子 + 种子真相源在 C#（§9）
- [ ] 能讲 Docker 连接串环境变量注入 + `down -v` 删库红线（§10）
- [ ] 能用"止血→定位→根因→治本→复盘"框架答三道开放排障题（§11）

### 12.3 动手练习 3 个

> 动手做过才是自己的。以下练习在 CP6 或任意 SQL Server 上都能做（没有 CP6 环境就自建一张测试表模拟）。

**练习 1：写一个 SP 并从 Dapper 调用（对标 §1、§4）**
> 目标：仿照 CP6 的 `usp_GetMesDashboardSummary`，写一个"某表按日期范围聚合"的存储过程 + 从 C# 用 Dapper 调用。
> 1. 建一张测试表（如订单表，含 `CreateDate`、`Amount`、`Status`、`IsDeleted`）。
> 2. 写 SP `usp_GetOrderSummary @days int = 30`：返回近 N 天的订单数、总额、平均额，注意用半开区间日期范围（sargable）。
> 3. 加一个覆盖索引在 `(CreateDate, IsDeleted) INCLUDE (Amount)`，对比加索引前后的执行计划（应从 Scan 变 Seek）。
> 4. 在 C# 里用 Dapper 的 `QueryFirstOrDefaultAsync<T>(..., new { days = 7 }, commandType: StoredProcedure)` 调用它。
> 5. **验证点**：SP 的结果列名要和 DTO 属性名对齐（否则映射不上，静默为默认值）。

**练习 2：亲手制造并修复一个慢查询（对标 §7）**
> 目标：把第 7.4 的排障剧本亲手走一遍。
> 1. 建一张大表（用 `INSERT ... SELECT` + 递归 CTE 或 `GENERATE_SERIES`/`sys.all_objects` 交叉造 100 万行）。
> 2. 写一个"按某列过滤 + 排序 + 分页"的查询，故意**不建索引**，跑一下看执行计划（Table Scan）+ 逻辑读。
> 3. 再写一个**故意不 sargable** 的版本（WHERE 里对列套函数，如 `WHERE YEAR(CreateDate)=2026`），对比它更慢。
> 4. 建合适的覆盖索引，把 sargable 版本重跑，对比执行计划（Seek）、逻辑读（暴降）、耗时。
> 5. **验证点**：用 `SET STATISTICS IO ON; SET STATISTICS TIME ON;` 量化逻辑读和 CPU 时间，用数据说话。

**练习 3：写一个幂等种子脚本 + 模拟一次备份还原（对标 §5、§9）**
> 目标：体会"幂等"和"还原演练"。
> 1. 仿照 CP6 的 `mes-permission-seed.sql`，写一个用 `WHERE NOT EXISTS`（判业务唯一键，不是主键）的种子脚本，
>    插入几条字典/配置数据。**连跑三次**，确认第二、三次零重复插入（用 `@@ROWCOUNT` 或前后 count 对比证明）。
> 2. 用 `BACKUP DATABASE ... WITH COMPRESSION, CHECKSUM` 备份你的测试库。
> 3. 故意"搞破坏"（删几行 / DROP 一张表），然后 `RESTORE DATABASE ... WITH REPLACE` 从备份还原。
> 4. **验证点**：还原后数据回来了吗？记录还原花了多久（这就是你实测的 RTO）。
>    这一步就是第 5.5 说的"恢复演练"——很多人从没做过，做一次你就比大多数候选人更懂备份。

---

## 结语

这一章的核心不是让你背会多少 SQL 语法，而是让你**具备"生产环境思维"**：
- **懂取舍**：SP vs EF、触发器 vs 拦截、EF vs Dapper——没有银弹，只有场景下的权衡，能把权衡讲清楚就是经验。
- **懂运维**：备份要异地、要演练、要告警；日志会撑爆磁盘；容量要提前监控；恢复模式选错日志会暴涨。
- **懂排障**：一切故障套"止血→定位→根因→治本→复盘"，慢查询套"现象→定位→分析→修复→验证"，用逻辑读和执行计划说话。
- **懂敬畏**：`down -v` 会删库，自动删审计日志是禁忌，`EnableSensitiveDataLogging` 不上生产，密钥不进 Git。

面试时，当被问到开放的生产题，**先亮出你的方法论框架，再用 CP6 的真实标本和那次磁盘满事故填充细节**——
这种"有框架 + 有实战"的回答，就是 JD 里"定位并修复生产系统故障、推进改善措施"最想听到的答案。

> **把第 6.5 的磁盘满事故练到能脱口而出**——它是你这三天冲刺里性价比最高的一块素材。
