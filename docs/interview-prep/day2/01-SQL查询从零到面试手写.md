# Day 2 · 第 1 章：SQL 查询从零到面试手写（T-SQL / SQL Server 方言）

> 本章目标：把你从"没写过 SQL"带到"面试白板上能手写 15 道题"。所有例题都锚定
> **CP6 真实生产库**（.NET 8 多租户制造业 ERP/MES/WMS，数据库 = SQL Server）。
> 你在 Day 1 已经学完 C# / EF Core，本章开始进入数据库层——**EF Core 最终生成的就是这些 SQL**，
> 面试官问"你写过复杂 SQL 吗"，考的就是本章。
>
> 数据库方言：**T-SQL（SQL Server 专用方言）**。凡是 SQL Server 特有的地方（`TOP`、`OFFSET-FETCH`、
> `ISNULL`、`GETDATE()`、`[方括号]` 转义、`NEWID()`、`FOR JSON`、`MERGE` 等）我都会点名，
> 免得你到了 MySQL / Oracle 面试张冠李戴。

---

## 0. 本章会反复用到的 4 张真实表（先建立肌肉记忆）

在写任何查询前，先认识"演员"。下面 4 张表贯穿全章，请务必记住它们的**真实表名**和核心列。
这些结构是从 CP6 的 C# 实体类（`CP6.Entity/DomainModels/...`）一比一翻译过来的，不是我编的。

### 0.1 `T_Stock` —— 在库实况表（全章第一主角）

C# 实体 `Stock`（`CP6.Entity/DomainModels/Wms/Stock.cs`）上有 `[Table("T_Stock")]`，
所以**数据库里的真实表名是 `T_Stock`**（注意不是 `Stocks`——C# 实体叫 `Stock`，但落到库里被显式改名了）。

它继承链是 `Stock : BaseBizEntity : BaseTenantEntity : BaseEntity`，
所以除了自己的业务列，还自动带上了一堆"公共列"。翻译成 `CREATE TABLE`：

```sql
CREATE TABLE dbo.T_Stock (
    -- ↓↓↓ 来自 BaseEntity（每张表都有）
    Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,  -- 主键，GUID
    Creator       NVARCHAR(100)    NULL,                  -- 创建人
    CreateDate    DATETIME2        NOT NULL,              -- 创建时间
    Modifier      NVARCHAR(100)    NULL,                  -- 修改人
    ModifyDate    DATETIME2        NULL,                  -- 修改时间
    -- ↓↓↓ 来自 BaseTenantEntity（多租户隔离）
    TenantId      UNIQUEIDENTIFIER NOT NULL,              -- 租户 ID（行级隔离硬墙）
    -- ↓↓↓ 来自 BaseBizEntity（业务表通用）
    IsDeleted     BIT              NOT NULL DEFAULT 0,    -- 逻辑删除：0=有效 1=已删
    RowVersion    ROWVERSION,                             -- 乐观锁版本号（并发控制）
    -- ↓↓↓ Stock 自己的业务列
    WarehouseCd   NVARCHAR(10)     NOT NULL,              -- 仓库编码（业务唯一键 1/4）
    LocationCd    NVARCHAR(30)     NOT NULL,              -- 库位编码（业务唯一键 2/4）
    ProductCd     NVARCHAR(20)     NOT NULL,              -- 产品编码（业务唯一键 3/4）
    LotNo         NVARCHAR(30)     NOT NULL,              -- 批次号（业务唯一键 4/4，无批次时=''）
    PhysicalQty   DECIMAL(21,8)    NOT NULL DEFAULT 0,    -- 物理在库数
    AllocatedQty  DECIMAL(21,8)    NOT NULL DEFAULT 0,    -- 已引当（预留）数
    AvailableQty  DECIMAL(21,8)    NOT NULL DEFAULT 0,    -- 可用在库 = Physical - Allocated
    UnitCd        NVARCHAR(10)     NULL,                  -- 单位编码
    ReceiveDate   DATETIME2        NULL,                  -- 入库日
    ExpiryDate    DATETIME2        NULL,                  -- 保质期（FEFO 引当用）
    UnitPrice     DECIMAL(18,4)    NULL,                  -- 单价（库存评估用）
    RecallFlag    BIT              NOT NULL DEFAULT 0,    -- 召回标记：1=禁止出库
    OwnerType     NVARCHAR(10)     NOT NULL DEFAULT 'SELF',-- 所有者区分：SELF/CUSTOMER（VMI）
    OwnerCd       NVARCHAR(20)     NULL,                  -- 所有者编码（VMI 客户编码）
    PaperRollNo   NVARCHAR(20)     NULL,                  -- 原纸卷号（造纸行业专用）
    QcStatus      NVARCHAR(10)     NOT NULL DEFAULT 'PENDING' -- 质检状态 PENDING/PASSED/FAILED/HOLD
);
```

**业务唯一键**（不是主键，主键是 `Id`）：`(WarehouseCd, LocationCd, ProductCd, LotNo)`——
即"哪个仓、哪个库位、哪个产品、哪个批次"唯一确定一行在库。记住这 4 个字段，后面 `GROUP BY` 反复用。

### 0.2 `T_StockTransaction` —— 库存流水（不可变日志，第二主角）

C# 实体 `StockTransaction`（`Wms/StockTransaction.cs`）带 `[Table("T_StockTransaction")]`。
这是一张**只增不改**（INSERT only，禁止 UPDATE/DELETE）的日志表——所有库存变动都往里记一行。
它是"所有汇总报表的唯一真实源"。

```sql
CREATE TABLE dbo.T_StockTransaction (
    -- 公共列同上（Id/Creator/CreateDate/Modifier/ModifyDate/TenantId/IsDeleted/RowVersion）
    Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    CreateDate    DATETIME2        NOT NULL,
    TenantId      UNIQUEIDENTIFIER NOT NULL,
    IsDeleted     BIT              NOT NULL DEFAULT 0,
    -- ↓↓↓ 业务列
    TxnNo         NVARCHAR(25)     NOT NULL,   -- 流水号（业务唯一键，形如 TXN20260714-00001）
    TxnType       NVARCHAR(10)     NOT NULL,   -- 种别：IN/OUT/MOVE/ADJ/RSV/UNRSV
    TxnDateTime   DATETIME2        NOT NULL,   -- 发生时刻
    WarehouseCd   NVARCHAR(10)     NOT NULL,
    LocationCd    NVARCHAR(30)     NOT NULL,
    ProductCd     NVARCHAR(20)     NOT NULL,
    LotNo         NVARCHAR(30)     NOT NULL,
    Qty           DECIMAL(21,8)    NOT NULL,   -- 数量（IN/RSV 正；OUT/UNRSV 正；ADJ 带符号差分）
    UnitCd        NVARCHAR(10)     NULL,
    UnitPrice     DECIMAL(18,4)    NULL,
    RelatedNo     NVARCHAR(25)     NULL,       -- 关联单据号（入库预定 / 出库指示 / 盘点号）
    RelatedType   NVARCHAR(20)     NULL,       -- 关联单据种别 INBOUND/OUTBOUND/STOCKTAKE
    OperatorCd    NVARCHAR(20)     NULL,       -- 作业者编码
    Remark        NVARCHAR(500)    NULL
    -- （还有 ReceiptInspectionNo / KitOrderNo / RmaNo / PaperRollNo 等扩展列，本章用不到）
);
```

`TxnType` 的取值（面试常问"你怎么区分出入库"）：
- `IN`   = 入库（正数）
- `OUT`  = 出库（正数，靠 TxnType 区分方向，不靠负号）
- `MOVE` = 移库
- `ADJ`  = 盘点调整（差分带符号）
- `RSV`  = 引当/预留
- `UNRSV`= 取消引当

### 0.3 `Wf_FlowTask` —— 工作流待办任务（工作流/OA 场景）

C# 实体 `Wf_FlowTask`（`Wf/Wf_FlowTask.cs`），带 `[Table("Wf_FlowTask")]`，真实表名就叫 `Wf_FlowTask`。
它继承 `BaseTenantEntity`（有租户，但**没有** IsDeleted/RowVersion，因为它不是 BaseBizEntity）。

```sql
CREATE TABLE dbo.Wf_FlowTask (
    Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    CreateDate    DATETIME2        NOT NULL,
    TenantId      UNIQUEIDENTIFIER NOT NULL,
    InstanceId    UNIQUEIDENTIFIER NOT NULL,   -- 所属流程实例
    NodeId        NVARCHAR(100)    NOT NULL,    -- 所在节点
    AssigneeId    UNIQUEIDENTIFIER NOT NULL,    -- 处理人（→ Sys_User.Id）
    Status        INT              NOT NULL,    -- 0=待办 1=同意 2=驳回 3=作废 4=挂起
    Countersign   NVARCHAR(20)     NULL,        -- 会签规则 all/any/veto
    Comment       NVARCHAR(1000)   NULL,        -- 处理意见
    DueAt         DATETIME2        NULL,        -- 到期时间（超时扫描依据）
    IsRead        BIT              NOT NULL,    -- 未读标记
    ReadAt        DATETIME2        NULL
);
```

关键：`Status = 0` 就是"待办"，非 0 就是"已办"。做"待办统计"用这个字段。

### 0.4 `Sys_Langs` —— 多语言词条表（系统表，讲 upsert 用）

C# 实体 `Sys_Lang`（`Sys/Sys_Lang.cs`）。注意这张表**主键是 `INT` 自增**（不是 GUID），
因为它是纯系统字典表，直接继承 `BaseEntity` 的语义都没用上，自己定义了 `Id`。
真实表名 `Sys_Langs`（复数——EF Core 默认按 `DbSet` 属性名复数化，这张没被 `[Table]` 改名）。

```sql
CREATE TABLE dbo.Sys_Langs (
    Id        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,  -- 自增整型主键
    TenantId  INT               NULL,     -- null=全局默认；非null=该租户覆盖
    LangKey   NVARCHAR(200)     NOT NULL, -- 词条 key，如 'login.title'
    Status    NVARCHAR(20)      NOT NULL DEFAULT 'reviewed', -- draft/reviewed
    UpdatedBy NVARCHAR(100)     NULL,
    UpdatedAt DATETIME2         NULL,
    ZhCN      NVARCHAR(500)     NULL,     -- 简体中文
    ZhTW      NVARCHAR(500)     NULL,     -- 繁体中文
    En        NVARCHAR(500)     NULL,     -- 英语
    Ja        NVARCHAR(500)     NULL,     -- 日语
    Ko        NVARCHAR(500)     NULL      -- 韩语
);
```

> **面试提示：真实表名不规律，别硬背规律**。CP6 里 `T_Stock`（前缀 T_）、`Wf_FlowTask`（前缀 Wf_）、
> `Sys_Langs`（前缀 Sys_ + 复数）、`Sys_RoleAction`（前缀 Sys_ 但单数）——命名并不统一。
> 生产项目就是这样，你能从 C# 的 `[Table("...")]` 特性、EF 迁移文件、或 seed SQL 里核实真实表名，
> 就是"懂工程"的表现。凭空猜 `Stocks` 会连不上表。

---

## 1. 关系型数据库心智模型（新手必须先建立的世界观）

### 1.1 表 / 行 / 列：一张 Excel 的严肃版

**类比**：一张表（table）就是一张 Excel sheet。
- **列（column / 字段 field）** = Excel 的表头，每列有固定的**数据类型**（整数、字符串、日期…），
  这是 SQL 和 Excel 最大的区别——Excel 一列里能同时塞数字和文字，SQL 不行，列 `PhysicalQty DECIMAL(21,8)`
  就永远只能放小数。
- **行（row / 记录 record / 元组 tuple）** = Excel 的一行数据，代表一个"实体实例"。
  `T_Stock` 的一行 = "A 仓 / L01 库位 / 产品 P001 / 批次 LOT202607 现有 500 个"。
- **表** = 行的集合。SQL 的核心思想：**你操作的是"集合"，不是"一行一行地循环"**（这点后面 1.4 展开）。

### 1.2 主键（Primary Key）：每行的身份证

**主键**唯一标识一行，不能重复、不能为 NULL。CP6 所有业务表的主键都是 `Id`（GUID）。

```sql
-- T_Stock 的主键是 Id
Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
```

**GUID 主键 vs 自增整型主键**（面试高频对比）：

| 维度 | GUID (`UNIQUEIDENTIFIER`) | 自增 (`INT IDENTITY`) |
|------|---------------------------|------------------------|
| CP6 用在 | 业务表（T_Stock 等） | 系统字典表（Sys_Langs） |
| 优点 | 客户端可先生成、不暴露业务量、分库分表不撞 | 短、有序、索引友好、可读 |
| 缺点 | 16 字节大、随机插入导致索引页分裂 | 暴露业务量、多库合并会撞、需回库取值 |

> **补充**：SQL Server 里如果非要 GUID 又要有序，可用 `NEWSEQUENTIALID()` 代替 `NEWID()`，
> 生成大致递增的 GUID，缓解索引碎片。CP6 的 seed SQL 里用的是 `NEWID()`（见 §9 的 seed 精读）。

### 1.3 外键（Foreign Key）：行与行之间的"引用"

**外键**是一列，它的值必须是**另一张表主键**里存在的值（或 NULL），表达"从属/引用"关系。

CP6 里 `Wf_FlowTask.AssigneeId` 逻辑上引用 `Sys_User.Id`（处理人是哪个用户），
`Wf_FlowTask.InstanceId` 引用 `Wf_FlowInstance.Id`（属于哪个流程实例）。

```
Sys_User                      Wf_FlowTask
┌──────────┬────────┐        ┌──────────┬────────────┬────────┐
│ Id (PK)  │ Name   │        │ Id (PK)  │ AssigneeId │ Status │
├──────────┼────────┤        ├──────────┼────────────┼────────┤
│ U-001    │ 田中   │◄───────│ T-100    │ U-001      │ 0      │
│ U-002    │ 铃木   │◄──┐    │ T-101    │ U-001      │ 1      │
└──────────┴────────┘   └────│ T-102    │ U-002      │ 0      │
                             └──────────┴────────────┴────────┘
              Assignee 这一列的值，都能在 Sys_User.Id 里找到 → 这就是外键约束
```

> **注意**：CP6 很多"外键"是**逻辑外键**（应用层保证），数据库里不一定建了真正的 `FOREIGN KEY` 约束。
> 多租户 + 高并发系统常故意不建物理外键（减少锁、方便分库、软删除时不被约束挡住）。
> 面试被问"为什么不建外键"，答"性能/软删/分片灵活性 vs 数据完整性 的权衡"就是满分。

### 1.4 SQL 是"声明式"语言（这点决定你怎么思考）

**命令式（C# / 你 Day 1 学的）**：你告诉计算机"怎么一步步做"。
```csharp
var result = new List<Stock>();
foreach (var s in allStocks)          // 你亲手写循环
    if (s.WarehouseCd == "WH-A")
        result.Add(s);
```

**声明式（SQL）**：你只描述"我要什么结果"，**怎么取由数据库的查询优化器决定**。
```sql
SELECT * FROM T_Stock WHERE WarehouseCd = 'WH-A';   -- 没有循环！只描述"要什么"
```

这个思维转变是新手最大的坎：
- **别再想"循环遍历"**，要想"我要的这批行满足什么条件"。
- 你写的顺序（`SELECT ... FROM ... WHERE ...`）**不是执行顺序**（见 §2.6），数据库会重排、会走索引、会并行，
  只要结果对就行。
- 正因为声明式，**同一个结果可以有多种写法**（子查询 / JOIN / 窗口函数都能"每组取最新"），
  面试就爱考"这三种写法的差异"（见 §7.4）。

### 1.5 NULL 语义与三值逻辑（新手 90% 会踩的坑，务必吃透）

**NULL 不是 0，不是空字符串 `''`，而是"未知 / 缺失"**。CP6 里 `T_Stock.UnitPrice` 可空——
`NULL` 表示"这批库存还没定价"，而 `0` 表示"定价就是 0 元"。二者业务含义天差地别。

**三值逻辑**：普通编程里布尔只有 `true`/`false`，SQL 里有第三个值 `UNKNOWN`。
任何和 `NULL` 的比较，结果都是 `UNKNOWN`（既不是真也不是假）。

```sql
-- 全部返回 UNKNOWN（既非 TRUE 也非 FALSE），所以这些行都不会被选中：
WHERE UnitPrice = NULL      -- ✗ 错！永远不成立
WHERE UnitPrice <> NULL     -- ✗ 错！永远不成立
WHERE UnitPrice > 100       -- 当 UnitPrice 为 NULL 时，也是 UNKNOWN → 这行被排除
```

**正确判断 NULL 必须用 `IS NULL` / `IS NOT NULL`**：
```sql
SELECT * FROM T_Stock WHERE UnitPrice IS NULL;      -- ✓ 找"还没定价"的库存
SELECT * FROM T_Stock WHERE UnitPrice IS NOT NULL;  -- ✓ 找"已定价"的库存
```

**坑 1：`NOT IN (子查询)` 遇到 NULL 会全军覆没**（面试最爱考）：
```sql
-- 如果子查询结果里有一个 NULL，整个 NOT IN 会返回空集！
SELECT * FROM T_Stock
WHERE ProductCd NOT IN (SELECT ProductCd FROM T_StockTransaction);  -- 若右侧含 NULL → 结果为空
```
原因：`NOT IN (a, b, NULL)` 展开成 `x<>a AND x<>b AND x<>NULL`，最后一项永远 `UNKNOWN`，
`真 AND 真 AND UNKNOWN = UNKNOWN`，于是没有任何行通过。**对策：改用 `NOT EXISTS`（见 §5.3）**。

**坑 2：聚合函数忽略 NULL**（除了 `COUNT(*)`）：
```sql
-- 假设 10 行库存里有 3 行 UnitPrice 为 NULL
SELECT AVG(UnitPrice) FROM T_Stock;   -- 只对 7 行非 NULL 求平均，分母是 7 不是 10！
SELECT COUNT(UnitPrice) FROM T_Stock; -- = 7（不数 NULL）
SELECT COUNT(*) FROM T_Stock;         -- = 10（数所有行）
```

**坑 3：`NULL` 拼字符串会变 `NULL`**：
```sql
SELECT 'PN-' + ProductCd FROM T_Stock;  -- 若 ProductCd 为 NULL，整个结果 NULL
-- 对策：用 CONCAT（自动把 NULL 当空串）或 ISNULL（见 §10）
SELECT CONCAT('PN-', ProductCd) FROM T_Stock;  -- NULL 被当 ''
```

**坑 4：`UNIQUE` 约束 与 `GROUP BY` 对待 NULL 不一致**——`GROUP BY` 会把多个 NULL 归到同一组，
但 `= ` 比较里 NULL 互不相等。这也是为什么 §9 的 `import-langs.sql` 里，匹配全局词条要写
`(t.TenantId = s.TenantId OR (t.TenantId IS NULL AND s.TenantId IS NULL))`——
因为 `NULL = NULL` 是 `UNKNOWN`，必须手动补一条 `IS NULL AND IS NULL` 才能把两个全局行配上。

> **面试一句话总结**：NULL 是"未知"，参与比较得 UNKNOWN，判空只能用 IS NULL，
> NOT IN 遇 NULL 会翻车，聚合函数忽略 NULL。**这五点几乎每场 SQL 面试都会摸一遍。**

---

## 2. SELECT 完整篇：单表查询的全部武器

### 2.1 最小骨架：SELECT ... FROM

```sql
SELECT WarehouseCd, ProductCd, PhysicalQty    -- 要哪几列（投影 projection）
FROM   T_Stock;                                -- 从哪张表
```
- `SELECT *` = 要所有列。**生产代码禁用 `SELECT *`**（列变动会破坏调用方、多传网络、走不了覆盖索引）。
  面试写题干可以用 `*`，但你主动说"生产里我会列出具体列"是加分项。

**结果示意**：
```
WarehouseCd | ProductCd | PhysicalQty
------------+-----------+------------
WH-A        | P001      | 500.00000000
WH-A        | P002      | 120.00000000
WH-B        | P001      | 0.00000000
```

### 2.2 WHERE：行过滤（选择 selection）

```sql
SELECT ProductCd, PhysicalQty, AvailableQty
FROM   T_Stock
WHERE  WarehouseCd = 'WH-A'         -- 只要 A 仓
  AND  AvailableQty > 0             -- 且可用库存 > 0
  AND  IsDeleted = 0;              -- 且没被逻辑删除（业务表几乎每条查询都要带这个！）
```

> **CP6 铁律**：业务表（继承 `BaseBizEntity` 的都有 `IsDeleted`）查询默认要带 `IsDeleted = 0`。
> EF Core 里这是"全局查询过滤器"自动加的；你手写 SQL 时必须**自己记得加**，否则会把软删的数据也查出来。
> 面试手写题里主动加 `AND IsDeleted = 0` 是"懂这套系统"的信号。

### 2.3 比较运算符 与 逻辑运算符

| 类别 | 运算符 | 例子 |
|------|--------|------|
| 比较 | `=` `<>`（或 `!=`）`>` `<` `>=` `<=` | `Qty >= 100` |
| 逻辑 | `AND` `OR` `NOT` | `TxnType = 'IN' OR TxnType = 'OUT'` |
| 范围 | `BETWEEN a AND b`（含两端） | `TxnDateTime BETWEEN '2026-07-01' AND '2026-07-31'` |
| 集合 | `IN (…)` / `NOT IN (…)` | `TxnType IN ('IN','OUT','MOVE')` |
| 空判断 | `IS NULL` / `IS NOT NULL` | `UnitPrice IS NULL` |
| 模糊 | `LIKE` / `NOT LIKE` | `ProductCd LIKE 'P00%'` |

**AND / OR 优先级坑**：`AND` 优先级高于 `OR`。下面两句结果不同：
```sql
-- 想表达："A 仓里，IN 或 OUT 的流水"，但下面写错了：
WHERE WarehouseCd = 'WH-A' AND TxnType = 'IN' OR TxnType = 'OUT'
-- 实际被解析成：(WarehouseCd='WH-A' AND TxnType='IN') OR (TxnType='OUT')
--   → 把"所有仓的 OUT"也捞进来了！
-- 正确：用括号
WHERE WarehouseCd = 'WH-A' AND (TxnType = 'IN' OR TxnType = 'OUT')
```

### 2.4 LIKE 通配符

- `%` = 任意个（含 0 个）字符
- `_` = 恰好 1 个字符
- `[abc]` / `[a-z]` = 方括号内任一字符（SQL Server 特有，MySQL 没有）
- `[^abc]` = 不在方括号内的字符

```sql
SELECT * FROM T_Stock WHERE ProductCd LIKE 'P%';       -- P 开头
SELECT * FROM T_Stock WHERE ProductCd LIKE '%01';      -- 01 结尾
SELECT * FROM T_Stock WHERE ProductCd LIKE 'P__1';     -- P + 恰好2字符 + 1，如 P001/PAB1
SELECT * FROM T_Stock WHERE WarehouseCd LIKE 'WH-[AB]';-- WH-A 或 WH-B
```

**坑：前导 `%` 用不了索引**。`LIKE 'P%'` 能走索引（相当于范围扫描），
但 `LIKE '%01'` 前面是通配符，只能全表扫描，慢。面试问"为什么这个查询慢"，前导 `%` 是经典答案之一。

**坑：要匹配字面量 `%` 或 `_` 怎么办？** 用 `ESCAPE`：
```sql
-- 找 LangKey 里真的含下划线 '_' 的词条（否则 _ 会被当通配符）
SELECT * FROM Sys_Langs WHERE LangKey LIKE '%!_%' ESCAPE '!';
```

### 2.5 DISTINCT / ORDER BY / TOP

**DISTINCT**：去重（对"选出来的整行"去重，不是单列）。
```sql
SELECT DISTINCT ProductCd FROM T_Stock;          -- 有哪些不同的产品编码
SELECT DISTINCT WarehouseCd, ProductCd FROM T_Stock;  -- 对 (仓,品) 组合去重
```

**ORDER BY**：排序。`ASC`（升，默认）/ `DESC`（降）。可多列、可按序号、可按表达式。
```sql
SELECT ProductCd, PhysicalQty
FROM   T_Stock
ORDER BY PhysicalQty DESC, ProductCd ASC;   -- 先按数量降序，数量相同再按品号升序
```

**TOP**（SQL Server 特有；MySQL 是 `LIMIT`，Oracle 是 `ROWNUM`/`FETCH`）：取前 N 行。
```sql
SELECT TOP 10 ProductCd, PhysicalQty
FROM   T_Stock
ORDER BY PhysicalQty DESC;          -- 库存最多的 10 个（TOP 几乎总要配 ORDER BY，否则"前10"没意义）

SELECT TOP 5 PERCENT * FROM T_Stock ORDER BY PhysicalQty DESC;  -- 前 5%
SELECT TOP 10 WITH TIES ... ORDER BY PhysicalQty DESC;          -- 含并列（第10名并列的都要）
```

> **TOP vs OFFSET-FETCH**：`TOP` 只能"取前 N"，做不了"跳过 N 再取 M"（分页）。分页要用 `OFFSET-FETCH`，见 §11。

### 2.6 执行顺序（面试最爱考的"为什么"）

你**书写**的顺序 ≠ 数据库**执行**的顺序。逻辑执行顺序是：

```
   书写顺序                        逻辑执行顺序（记住这张图！）
┌─────────────┐              ┌──────────────────────────────┐
│ SELECT      │  ①           │ 1. FROM      选表 + JOIN 组装源 │
│ FROM        │  ②  ←──┐     │ 2. WHERE     行过滤（不能用别名）│
│ WHERE       │  ③     │     │ 3. GROUP BY  分组             │
│ GROUP BY    │  ④     │执行 │ 4. HAVING    组过滤           │
│ HAVING      │  ⑤     │顺序 │ 5. SELECT    投影 + 算别名     │
│ ORDER BY    │  ⑥     │与书 │ 6. DISTINCT  去重             │
└─────────────┘        │写不 │ 7. ORDER BY  排序（可用别名）  │
                       │同！ │ 8. TOP/OFFSET 截断            │
                       └─────└──────────────────────────────┘
```

**为什么 WHERE 里不能用 SELECT 定义的别名？**
因为 `WHERE`（第 2 步）在 `SELECT`（第 5 步）**之前**执行，此刻别名还不存在。

```sql
-- ✗ 报错：WHERE 里用不了 SELECT 的别名 avail
SELECT ProductCd, PhysicalQty - AllocatedQty AS avail
FROM   T_Stock
WHERE  avail > 0;                       -- 错！WHERE 执行时 avail 还没算出来

-- ✓ 改法1：WHERE 里重写表达式
SELECT ProductCd, PhysicalQty - AllocatedQty AS avail
FROM   T_Stock
WHERE  PhysicalQty - AllocatedQty > 0;

-- ✓ 改法2：ORDER BY 里可以用别名（第7步，在 SELECT 之后）
SELECT ProductCd, PhysicalQty - AllocatedQty AS avail
FROM   T_Stock
ORDER BY avail DESC;                    -- 对！ORDER BY 在 SELECT 之后
```

**同理：`WHERE` 里不能用聚合函数**（如 `WHERE COUNT(*) > 5`），因为聚合发生在 `GROUP BY`（第3步），
`WHERE`（第2步）更早。要过滤"聚合后的组"必须用 `HAVING`（第4步）。这就是 §4.4 的 `WHERE` vs `HAVING`。

> **面试标准答话**：SQL 逻辑执行顺序是 FROM→WHERE→GROUP BY→HAVING→SELECT→ORDER BY。
> 别名在 SELECT 阶段才诞生，所以 WHERE / GROUP BY / HAVING 用不了别名，只有 ORDER BY 能用。

---

## 3. JOIN 彻底讲透（连接是 SQL 的灵魂，也是面试重灾区）

现实里数据分散在多张表。"库存表 + 流水表"、"待办表 + 用户表"要拼起来看，就靠 JOIN。

### 3.1 五种 JOIN 的语义（ASCII 文氏图）

设左表 L、右表 R，按某个条件匹配：

```
INNER JOIN（内连接，只要两边都匹配的）
   L        R
 ┌───┐   ┌───┐
 │   │███│   │        结果 = 中间交集 ███
 └───┘   └───┘        L 有、R 没有的 → 丢弃；R 有、L 没有的 → 丢弃

LEFT JOIN（左外连接，左表全保留）
   L        R
 ┌───┐   ┌───┐
 │███│███│   │        结果 = 左表全部 + 匹配上的右表值
 └───┘   └───┘        左表有但右表没匹配的行 → 右表列填 NULL

RIGHT JOIN（右外连接，右表全保留）
   L        R
 ┌───┐   ┌───┐
 │   │███│███│        结果 = 右表全部 + 匹配上的左表值（左表列可能 NULL）
 └───┘   └───┘        实务中少用，通常把表交换后写成 LEFT JOIN 更直观

FULL JOIN（全外连接，两边都全保留）
   L        R
 ┌───┐   ┌───┐
 │███│███│███│        结果 = 左全 + 右全，任一边没匹配的另一边填 NULL
 └───┘   └───┘

CROSS JOIN（笛卡尔积，不写 ON，每行两两组合）
 L(m行) × R(n行) = m×n 行     用途：生成"所有组合"，如 §9 seed 里"每租户 × 每动作"
```

### 3.2 INNER JOIN：最常用

**场景**：把库存流水和"它当前所在库存行"拼起来，看每笔入库流水对应的当前在库单价。
```sql
SELECT t.TxnNo, t.ProductCd, t.Qty, s.PhysicalQty, s.UnitPrice
FROM   T_StockTransaction t
INNER JOIN T_Stock s
    ON  s.WarehouseCd = t.WarehouseCd    -- 按业务唯一键的 4 个字段连接
    AND s.LocationCd  = t.LocationCd
    AND s.ProductCd   = t.ProductCd
    AND s.LotNo       = t.LotNo
WHERE  t.TxnType = 'IN';
```
- `t`、`s` 是**表别名**，让 SQL 简短、消除"两表同名列"（`ProductCd` 两边都有）的歧义。
- 多字段连接：CP6 的业务唯一键就是 4 个字段，所以 `ON` 里 `AND` 连了 4 个等式。**这是制造业库存系统的典型形态**。

### 3.3 LEFT JOIN：保留左表全部（"找没有的"）

**场景**：列出所有产品的库存，即使某产品从没有过流水。或反过来——"哪些库存行从来没动过（没流水）"。
```sql
-- 每个在库行，左连它的流水；没流水的行，流水列为 NULL
SELECT s.ProductCd, s.PhysicalQty, t.TxnNo, t.TxnType
FROM   T_Stock s
LEFT JOIN T_StockTransaction t
    ON  t.WarehouseCd = s.WarehouseCd
    AND t.LocationCd  = s.LocationCd
    AND t.ProductCd   = s.ProductCd
    AND t.LotNo       = s.LotNo;
```

**经典用法：LEFT JOIN + IS NULL = "找左表里右表没有的"**（"反差集"）：
```sql
-- 找"有库存记录，但从来没有过任何流水"的在库行（数据异常排查）
SELECT s.WarehouseCd, s.ProductCd, s.LotNo
FROM   T_Stock s
LEFT JOIN T_StockTransaction t
    ON t.WarehouseCd=s.WarehouseCd AND t.LocationCd=s.LocationCd
   AND t.ProductCd=s.ProductCd     AND t.LotNo=s.LotNo
WHERE  t.Id IS NULL;    -- 右表没匹配上 → t 的所有列为 NULL → 这就是"没流水的库存行"
```

### 3.4 ON vs WHERE 在 LEFT JOIN 里的天坑（面试必考！）

这是全章最容易错、面试官最爱设的陷阱。看两个"长得几乎一样"的查询：

```sql
-- 查询 A：过滤条件写在 ON 里
SELECT s.ProductCd, t.TxnNo, t.TxnType
FROM   T_Stock s
LEFT JOIN T_StockTransaction t
    ON  t.ProductCd = s.ProductCd
    AND t.TxnType   = 'OUT';          -- ← 条件在 ON

-- 查询 B：过滤条件写在 WHERE 里
SELECT s.ProductCd, t.TxnNo, t.TxnType
FROM   T_Stock s
LEFT JOIN T_StockTransaction t
    ON  t.ProductCd = s.ProductCd
WHERE  t.TxnType = 'OUT';             -- ← 条件在 WHERE
```

**结果完全不同：**
- **查询 A（ON 里）**：`T_Stock` 的**每一行都保留**（LEFT JOIN 的承诺）。
  某产品若没有 OUT 流水，那一行仍在，只是 `t.TxnNo`、`t.TxnType` 为 `NULL`。
  → "所有产品 + 它们的出库流水（没有就留空）"。
- **查询 B（WHERE 里）**：JOIN 先做完（左表全留、无 OUT 的右列为 NULL），
  然后 `WHERE t.TxnType = 'OUT'` 再过滤——而 `NULL = 'OUT'` 是 UNKNOWN，
  那些"没有 OUT 流水的左行"**被 WHERE 干掉了**！LEFT JOIN 被悄悄降级成了 INNER JOIN。
  → 实际只剩"有 OUT 流水的产品"。

```
ON 里加条件  → 影响"怎么匹配"，不影响"左表保留哪些行" → LEFT 语义保住
WHERE 里加条件 → 在 JOIN 之后过滤全部结果行 → 把右表为 NULL 的左行也筛掉 → LEFT 退化成 INNER
```

**规则**：
- 对**右表**的过滤条件，想保住 LEFT 语义 → 放 `ON`。
- 对**左表**的过滤条件 → 放 `WHERE`（放 ON 无意义甚至误导）。
- 想要"退化成 INNER 的效果"？那直接写 INNER JOIN，别用"LEFT + WHERE 右表"这种迷惑写法。

> **面试满分回答**：LEFT JOIN 里，右表条件放 ON 保留左表所有行；放 WHERE 会因 NULL 比较把未匹配的左行滤掉，
> 使 LEFT 退化为 INNER。所以"过滤右表用 ON，过滤左表用 WHERE"。

### 3.5 自连接（Self Join）：一张表连自己

**场景**：`T_StockTransaction` 里 `MOVE`（移库）会成对出现——MOVE-OUT 行的 `CounterLocationCd`
指向移入的库位。把"移出"和"移入"配成一行看。或者组织架构（员工表的 `ManagerId` 指向同表的员工）。

```sql
-- 员工表自连接找"谁的上司是谁"（用组织架构表举例，概念一样）
SELECT e.Name AS 员工, m.Name AS 上司
FROM   Sys_User e
LEFT JOIN Sys_User m ON m.Id = e.ManagerId;  -- 同一张表用两个别名 e / m
```
自连接的关键：**同一张表起两个不同别名**，当成两张表用。

### 3.6 多表连接

JOIN 可以串联多张：`A JOIN B JOIN C JOIN D`，从左往右依次拼。
```sql
-- 待办任务 + 处理人 + 流程实例 + 表单定义（工作流场景，4 表连）
SELECT u.Name AS 处理人, t.NodeId, t.Status, i.FlowKey, f.FormName
FROM   Wf_FlowTask t
INNER JOIN Sys_User        u ON u.Id = t.AssigneeId
INNER JOIN Wf_FlowInstance i ON i.Id = t.InstanceId
LEFT  JOIN Wf_FormDef      f ON f.Id = i.FormId
WHERE  t.Status = 0;      -- 只看待办
```

### 3.7 JOIN 产生"重复行"的原因与对策（新手常被吓到）

**现象**：连接后行数暴增，或本以为一行却变多行。

**原因**：JOIN 是"按条件配对"，若**右表对一个左行匹配了多行**，左行就会被复制多份。
例：一个在库行有 5 笔流水，`T_Stock LEFT JOIN T_StockTransaction` 后，那个库存行会出现 5 次
（每次配一笔流水）。这不是 bug，是 JOIN 的定义（一对多 → 结果一对多）。

**对策**：
1. **先聚合再连接**（最常用）：把"多"的一方先 `GROUP BY` 汇成一行，再连。
   ```sql
   -- 想要"每个在库行 + 它的流水总笔数"，而不是被流水撑成多行
   SELECT s.ProductCd, s.LotNo, s.PhysicalQty, ISNULL(x.Cnt, 0) AS 流水笔数
   FROM   T_Stock s
   LEFT JOIN (
       SELECT WarehouseCd, LocationCd, ProductCd, LotNo, COUNT(*) AS Cnt
       FROM   T_StockTransaction
       GROUP BY WarehouseCd, LocationCd, ProductCd, LotNo
   ) x ON x.WarehouseCd=s.WarehouseCd AND x.LocationCd=s.LocationCd
       AND x.ProductCd=s.ProductCd    AND x.LotNo=s.LotNo;
   ```
2. **用 `EXISTS` 代替 JOIN**（只想知道"有没有"，不想要右表的值）——不会产生重复（见 §5.3）。
3. **`SELECT DISTINCT`**——能去重但**治标不治本**，且可能掩盖逻辑错误，慎用。

---

## 4. 聚合与分组（做报表的核心）

### 4.1 五个聚合函数

| 函数 | 作用 | 忽略 NULL？ |
|------|------|-------------|
| `COUNT(*)` | 数行数 | 否（数所有行） |
| `COUNT(列)` | 数该列非 NULL 的行 | 是 |
| `COUNT(DISTINCT 列)` | 数该列不同的非 NULL 值个数 | 是 |
| `SUM(列)` | 求和 | 是 |
| `AVG(列)` | 平均 | 是（分母只算非 NULL） |
| `MIN/MAX(列)` | 最小/最大 | 是 |

```sql
-- 全库存概览（不分组 = 整表当一组）
SELECT COUNT(*)                    AS 库存行数,
       SUM(PhysicalQty)            AS 物理总量,
       AVG(UnitPrice)             AS 平均单价,   -- 注意：只对已定价的行平均
       MAX(PhysicalQty)           AS 单行最大量,
       COUNT(DISTINCT ProductCd)  AS 品种数
FROM   T_Stock
WHERE  IsDeleted = 0;
```

### 4.2 `COUNT(*)` vs `COUNT(列)` vs `COUNT(DISTINCT 列)`（面试高频）

```sql
-- 假设 T_Stock 有 10 行，其中 3 行 UnitPrice 为 NULL，ProductCd 只有 P001/P002 两种值
SELECT COUNT(*)                   FROM T_Stock;  -- 10（所有行）
SELECT COUNT(UnitPrice)           FROM T_Stock;  -- 7（非 NULL 的）
SELECT COUNT(DISTINCT ProductCd)  FROM T_Stock;  -- 2（不同产品编码个数）
SELECT COUNT(1)                   FROM T_Stock;  -- 10（和 COUNT(*) 一样，无性能差异）
```
> **面试坑**：`COUNT(*)` 和 `COUNT(1)` 在 SQL Server 里**性能完全一样**（都数行数），
> "COUNT(1) 更快"是过时的以讹传讹。真正有区别的是 `COUNT(列)`——它要判 NULL，且语义是"非空行数"。

### 4.3 GROUP BY 规则：分组统计

**核心规则**：`SELECT` 里出现的列，**要么在 `GROUP BY` 里，要么被聚合函数包着**，不能"裸奔"。

```sql
-- ✗ 报错：TxnType 没在 GROUP BY 里，也没被聚合
SELECT WarehouseCd, TxnType, SUM(Qty)
FROM   T_StockTransaction
GROUP BY WarehouseCd;                  -- 错！TxnType 裸奔

-- ✓ 正确：按 (仓, 种别) 分组，统计每组数量总和与笔数
SELECT WarehouseCd, TxnType,
       SUM(Qty)  AS 数量合计,
       COUNT(*)  AS 笔数
FROM   T_StockTransaction
WHERE  IsDeleted = 0
GROUP BY WarehouseCd, TxnType          -- 分组键：出现在 SELECT 里的非聚合列，全在这
ORDER BY WarehouseCd, TxnType;
```
**为什么这条规则？** 因为分组后"一组变一行"，`WarehouseCd`、`TxnType` 每组内是唯一的（能代表这一行），
但 `Qty` 一组内有很多值，数据库不知道该显示哪个，所以必须用聚合函数明确"要这组的和/平均/最大…"。

**结果示意**：
```
WarehouseCd | TxnType | 数量合计   | 笔数
------------+---------+-----------+-----
WH-A        | IN      | 12000.00  | 45
WH-A        | OUT     | 8300.00   | 38
WH-B        | IN      | 5000.00   | 20
```

### 4.4 HAVING vs WHERE（面试必考对比）

- **WHERE**：分组**前**过滤**行**（第2步，早于 GROUP BY）。用不了聚合函数。
- **HAVING**：分组**后**过滤**组**（第4步，晚于 GROUP BY）。可以用聚合函数。

```sql
-- 找"入库总量超过 10000"的仓库，且只统计 2026 年 7 月的流水
SELECT WarehouseCd, SUM(Qty) AS 入库总量
FROM   T_StockTransaction
WHERE  TxnType = 'IN'                                   -- ← WHERE：分组前先只留 IN 的行
  AND  TxnDateTime >= '2026-07-01'
  AND  TxnDateTime <  '2026-08-01'
GROUP BY WarehouseCd
HAVING SUM(Qty) > 10000                                 -- ← HAVING：分组后按聚合值筛组
ORDER BY 入库总量 DESC;
```
**记忆口诀**：WHERE 筛行在前、HAVING 筛组在后；能用 WHERE 就别用 HAVING（WHERE 先过滤能减少参与聚合的数据，更快）。

### 4.5 GROUPING SETS / ROLLUP / CUBE（认识即可，日报小计常用）

普通 `GROUP BY` 一次只出一个粒度。若既要"按仓+种别明细"又要"按仓小计"又要"总计"，用 `ROLLUP`：

```sql
-- ROLLUP 自动生成 层级小计 + 总计
SELECT WarehouseCd, TxnType, SUM(Qty) AS 合计
FROM   T_StockTransaction
WHERE  IsDeleted = 0
GROUP BY ROLLUP (WarehouseCd, TxnType);
-- 结果里会多出几行：
--   WH-A | IN   | ...   （明细）
--   WH-A | OUT  | ...   （明细）
--   WH-A | NULL | ...   ← WH-A 的小计（TxnType 为 NULL 表示"这一列被汇总掉了"）
--   NULL | NULL | ...   ← 全部总计
```
- `ROLLUP(a,b)` = 出 `(a,b)`、`(a)`、`()` 三个层级。
- `CUBE(a,b)` = 出所有组合 `(a,b)`、`(a)`、`(b)`、`()`。
- `GROUPING SETS((a,b),(a),())` = 你手动指定要哪些层级，最灵活。
- 用 `GROUPING(列)` 函数可判断某行的 NULL 是"真 NULL"还是"被汇总掉的小计标记"。

> 面试通常只要求"知道 ROLLUP 能出小计/总计"，能说出上面这段就够了。

---

## 5. 子查询全型

**子查询** = 嵌在另一条查询里的 SELECT。按"返回什么"和"用在哪"分几类。

### 5.1 标量子查询（返回单个值）

返回**一行一列**，可用在任何"需要一个值"的地方。
```sql
-- 每个产品的库存量，同时显示"全库平均库存"做对比（标量子查询给出那个单一平均值）
SELECT ProductCd, PhysicalQty,
       (SELECT AVG(PhysicalQty) FROM T_Stock WHERE IsDeleted=0) AS 全库平均
FROM   T_Stock
WHERE  IsDeleted = 0;
```
**坑**：标量子查询若返回多行会报错（"子查询返回的值多于一个"）。确保它只出一个值。

### 5.2 IN 子查询（返回一列多值）

```sql
-- 找"在 7 月有过出库流水"的所有产品的当前库存
SELECT * FROM T_Stock
WHERE  ProductCd IN (
    SELECT ProductCd FROM T_StockTransaction
    WHERE TxnType='OUT' AND TxnDateTime >= '2026-07-01'
);
```

### 5.3 EXISTS vs IN（性能与 NULL 语义，面试爱考）

`EXISTS` 判断"子查询有没有至少一行"，返回真/假。通常写成**相关子查询**（引用外层的列）：
```sql
-- 用 EXISTS 改写上面的"有出库流水的产品"
SELECT * FROM T_Stock s
WHERE  EXISTS (
    SELECT 1 FROM T_StockTransaction t
    WHERE t.ProductCd = s.ProductCd      -- 相关：引用了外层 s.ProductCd
      AND t.TxnType   = 'OUT'
);
```

**IN vs EXISTS 三点差异：**

1. **NULL 安全性（最重要）**：`NOT IN` 遇到子查询里的 NULL 会返回空集（§1.5 坑1），
   `NOT EXISTS` **不受 NULL 影响**。所以"找差集"一律用 `NOT EXISTS`：
   ```sql
   -- ✓ 安全：找"从来没有过任何流水的库存产品"
   SELECT * FROM T_Stock s
   WHERE NOT EXISTS (
       SELECT 1 FROM T_StockTransaction t WHERE t.ProductCd = s.ProductCd
   );
   -- ✗ 危险：若 T_StockTransaction.ProductCd 有 NULL 值，下面会返回空集
   -- SELECT * FROM T_Stock WHERE ProductCd NOT IN (SELECT ProductCd FROM T_StockTransaction);
   ```
2. **性能**：现代 SQL Server 优化器通常把 `IN`/`EXISTS` 优化成同样的"半连接（semi join）"，
   性能往往相当。经验法则：**子查询结果集大 → EXISTS 常更优**（找到一行就短路返回），
   **子查询结果集小且无 NULL → IN 可读性更好**。别死记"EXISTS 一定快"。
3. **可读性**：`IN` 直观（"在这批值里"）；`EXISTS` 表达"存在满足条件的关联行"，做复杂关联更清晰。

### 5.4 相关子查询（Correlated Subquery）

子查询里引用了外层的列，于是**外层每一行都要重新算一次子查询**（逻辑上如此）。
上面的 `EXISTS` 就是相关子查询。再看一个标量相关子查询：
```sql
-- 每个在库行，配上"它自己这个产品+批次的最近一次流水时间"
SELECT s.ProductCd, s.LotNo, s.PhysicalQty,
       (SELECT MAX(t.TxnDateTime)
        FROM   T_StockTransaction t
        WHERE  t.ProductCd = s.ProductCd     -- 相关：引用外层 s
          AND  t.LotNo     = s.LotNo) AS 最近流水时刻
FROM   T_Stock s;
```
**坑**：相关子查询逐行执行，数据量大时慢。很多场景可用 `JOIN` 或**窗口函数**改写得更快（见 §7.4）。

### 5.5 派生表（Derived Table，FROM 里的子查询）

把一个子查询当成"临时表"放在 `FROM` 里（§3.7 已经用过）。必须给它起别名。
```sql
SELECT d.WarehouseCd, d.品种数
FROM (
    SELECT WarehouseCd, COUNT(DISTINCT ProductCd) AS 品种数
    FROM   T_Stock WHERE IsDeleted = 0
    GROUP BY WarehouseCd
) AS d                          -- ← 派生表必须有别名 d
WHERE d.品种数 > 5;
```
派生表在语法上和 CTE（§6）很像，CTE 通常更易读、可复用、可递归。

---

## 6. CTE（公用表表达式，`WITH`）

### 6.1 用途：把复杂查询拆成"可读的步骤"

`CTE` 用 `WITH 名字 AS (子查询)` 定义一个"命名的临时结果集"，只在紧跟其后的那条语句里有效。
它像"给子查询起个名字，先声明后使用"，比层层嵌套的派生表可读性好得多。

```sql
-- 用 CTE 重构 §5.5：先算每仓品种数，再筛选、再排序——分步骤，一目了然
WITH WarehouseVariety AS (
    SELECT WarehouseCd, COUNT(DISTINCT ProductCd) AS 品种数
    FROM   T_Stock
    WHERE  IsDeleted = 0
    GROUP BY WarehouseCd
)
SELECT WarehouseCd, 品种数
FROM   WarehouseVariety
WHERE  品种数 > 5
ORDER BY 品种数 DESC;
```

多个 CTE 用逗号隔开，后面的能引用前面的：
```sql
WITH InSum AS (
    SELECT ProductCd, SUM(Qty) AS 入库量
    FROM T_StockTransaction WHERE TxnType='IN' GROUP BY ProductCd
),
OutSum AS (
    SELECT ProductCd, SUM(Qty) AS 出库量
    FROM T_StockTransaction WHERE TxnType='OUT' GROUP BY ProductCd
)
SELECT i.ProductCd, i.入库量, ISNULL(o.出库量,0) AS 出库量,
       i.入库量 - ISNULL(o.出库量,0) AS 净入库
FROM   InSum i
LEFT JOIN OutSum o ON o.ProductCd = i.ProductCd;
```

> **CTE vs 派生表 vs 临时表**：CTE 不落地（每次引用都可能重算，不是"物化"），作用域只有下一条语句；
> 临时表（`#temp`）真落地、可加索引、跨语句复用。大数据量多次引用时临时表可能更快。面试知道这个区别即可。

### 6.2 递归 CTE：展开层级结构（制造业经典——BOM 物料清单 / 组织架构）

递归 CTE 分两部分，用 `UNION ALL` 连接：
- **锚成员（anchor）**：起点（递归的第 0 层）。
- **递归成员（recursive）**：引用 CTE 自己，一层层往下展开，直到没有新行。

**场景 A：组织架构展开**（谁是某经理的所有下属，含各层级深度）
```sql
WITH OrgTree AS (
    -- 锚：从顶层经理开始（ManagerId 为 NULL 的是最高层）
    SELECT Id, Name, ManagerId, 0 AS Depth
    FROM   Sys_User
    WHERE  ManagerId IS NULL
    UNION ALL
    -- 递归：找"上司在上一层结果里"的人，深度 +1
    SELECT u.Id, u.Name, u.ManagerId, t.Depth + 1
    FROM   Sys_User u
    INNER JOIN OrgTree t ON u.ManagerId = t.Id   -- 引用 CTE 自己 = 递归
)
SELECT REPLICATE('  ', Depth) + Name AS 组织层级, Depth
FROM   OrgTree
ORDER BY Depth;
```

**场景 B：BOM 物料清单展开**（制造业最经典！一个成品由子件组成，子件又由更小的子件组成）
假设有物料结构表 `Mes_BomItem(ParentProductCd, ChildProductCd, QtyPer)`：
```sql
WITH BomExplosion AS (
    -- 锚：从成品 'FG-001' 的直接子件开始
    SELECT ParentProductCd, ChildProductCd, QtyPer,
           CAST(QtyPer AS DECIMAL(21,8)) AS 累计用量,  -- 从顶层到当前的用量连乘
           1 AS Level
    FROM   Mes_BomItem
    WHERE  ParentProductCd = 'FG-001'
    UNION ALL
    -- 递归：把每个子件再展开成它的子件，用量沿路连乘
    SELECT b.ParentProductCd, b.ChildProductCd, b.QtyPer,
           CAST(e.累计用量 * b.QtyPer AS DECIMAL(21,8)),
           e.Level + 1
    FROM   Mes_BomItem b
    INNER JOIN BomExplosion e ON b.ParentProductCd = e.ChildProductCd
)
SELECT REPLICATE('    ', Level-1) + ChildProductCd AS 物料, QtyPer AS 单位用量,
       累计用量, Level
FROM   BomExplosion
ORDER BY Level;
```

**递归 CTE 的两个必知点：**
1. **防死循环**：数据里若有环（A→B→A），递归会无限。SQL Server 默认递归深度上限 100，
   可用 `OPTION (MAXRECURSION 1000)` 调整，或 `OPTION (MAXRECURSION 0)` 解除（危险，确认无环才用）。
2. **递归成员里不能用聚合、TOP、DISTINCT、外连接**（限制较多），一般只做"逐层展开"。

> **面试爱问**："如何用 SQL 展开 BOM / 查一个人的所有下属？"——答"递归 CTE，锚成员定起点，
> 递归成员 JOIN 自己逐层展开，注意 MAXRECURSION 防环"，就是标准答案。

---

## 7. 窗口函数专题（面试白板手写最高频，务必练熟）

### 7.1 窗口函数是什么：不折叠行的"分组计算"

`GROUP BY` 会把"多行折叠成一行"。**窗口函数在保留每一行明细的同时，附加一个"跨行计算"的值**——
既看到明细，又看到统计，这是它的杀手锏。

语法核心：`函数() OVER (PARTITION BY ... ORDER BY ...)`
- `OVER (...)` 定义"窗口"（在哪一批行上算）。
- `PARTITION BY` = 按某列分区（类似 GROUP BY，但不折叠行）。
- `ORDER BY` = 窗口内排序（排名类、累计类需要）。

```sql
-- 每个在库行，附上"它所在仓库的物理总量"和"它占本仓的比例"——明细与汇总同屏
SELECT WarehouseCd, ProductCd, PhysicalQty,
       SUM(PhysicalQty) OVER (PARTITION BY WarehouseCd)            AS 本仓总量,
       PhysicalQty * 100.0
         / SUM(PhysicalQty) OVER (PARTITION BY WarehouseCd)        AS 占本仓百分比
FROM   T_Stock
WHERE  IsDeleted = 0;
```
**结果示意**（注意：每行明细都在，右边多了汇总列）：
```
WarehouseCd | ProductCd | PhysicalQty | 本仓总量 | 占本仓百分比
------------+-----------+-------------+---------+-------------
WH-A        | P001      | 500         | 620     | 80.6
WH-A        | P002      | 120         | 620     | 19.4
WH-B        | P001      | 300         | 300     | 100.0
```

### 7.2 排名函数：ROW_NUMBER / RANK / DENSE_RANK（三者区别必考）

```sql
SELECT ProductCd, WarehouseCd, PhysicalQty,
       ROW_NUMBER() OVER (ORDER BY PhysicalQty DESC) AS rn,
       RANK()       OVER (ORDER BY PhysicalQty DESC) AS rk,
       DENSE_RANK() OVER (ORDER BY PhysicalQty DESC) AS drk
FROM   T_Stock;
```
假设有并列（两行 PhysicalQty 都是 500）：
```
PhysicalQty | ROW_NUMBER | RANK | DENSE_RANK
------------+------------+------+-----------
600         | 1          | 1    | 1
500         | 2          | 2    | 2      ← 并列
500         | 3          | 2    | 2      ← 并列（RANK/DENSE_RANK 给同名次）
300         | 4          | 4    | 3
                              ↑            ↑
                    RANK 跳号(没有3)   DENSE_RANK 不跳号(接着3)
```
**三者区别（背下来）：**
- `ROW_NUMBER()`：**永远连续唯一** 1,2,3,4——即使值相同也强行分先后（用于分页、"每组取一条"）。
- `RANK()`：并列同名次，**之后跳号**（1,2,2,4）——像体育比赛并列第二后直接第四。
- `DENSE_RANK()`：并列同名次，**之后不跳号**（1,2,2,3）——名次紧凑。

### 7.3 LAG / LEAD（取前一行 / 后一行）与 SUM() OVER 累计

**LAG/LEAD**：在有序窗口里，取"当前行的前 N 行 / 后 N 行"的某个值。做**环比、差值、趋势**。
```sql
-- 某产品每笔流水，对比"上一笔流水的数量"，算出环比差
SELECT ProductCd, TxnDateTime, Qty,
       LAG(Qty)  OVER (PARTITION BY ProductCd ORDER BY TxnDateTime) AS 上一笔,
       Qty - LAG(Qty) OVER (PARTITION BY ProductCd ORDER BY TxnDateTime) AS 环比差,
       LEAD(TxnDateTime) OVER (PARTITION BY ProductCd ORDER BY TxnDateTime) AS 下一笔时间
FROM   T_StockTransaction
WHERE  TxnType = 'IN';
```
`LAG(列, 偏移, 默认值)`——偏移默认 1，默认值默认 NULL（第一行没有"上一笔"时给默认值）。

**SUM() OVER (ORDER BY ...)：累计（running total）**——移动汇总，做"库存累计变化 / 累计销量曲线"。
```sql
-- 某产品按时间的库存累计流水（把每笔 IN 记正、OUT 记负后累加，得到理论在库曲线）
SELECT ProductCd, TxnDateTime, TxnType, Qty,
       SUM(CASE WHEN TxnType='IN' THEN Qty
                WHEN TxnType='OUT' THEN -Qty ELSE 0 END)
           OVER (PARTITION BY ProductCd ORDER BY TxnDateTime
                 ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS 累计在库
FROM   T_StockTransaction
WHERE  ProductCd = 'P001'
ORDER BY TxnDateTime;
```
- `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` = 窗口"框架"：从分区第一行到当前行。
  这是累计的标准写法。**注意**：`OVER (ORDER BY ...)` 若不写框架，默认是 `RANGE ... CURRENT ROW`，
  遇到 ORDER BY 有并列值时会把并列行一起算入，可能不是你要的——**做精确累计务必显式写 `ROWS`**。

### 7.4 "每组取最新一条"三种写法对比（面试超高频，务必都会）

**需求**：`T_StockTransaction` 里，取**每个产品最新一笔流水**（按 TxnDateTime 最大）。

**写法 1：窗口函数 ROW_NUMBER（首选，最清晰高效）**
```sql
WITH Ranked AS (
    SELECT *,
           ROW_NUMBER() OVER (PARTITION BY ProductCd ORDER BY TxnDateTime DESC) AS rn
    FROM   T_StockTransaction
)
SELECT ProductCd, TxnNo, TxnDateTime, Qty
FROM   Ranked
WHERE  rn = 1;          -- 每个产品分区里，时间最新的那条是 rn=1
```
优点：一次扫描、意图清晰、并列可用 `ROW_NUMBER`（强制唯一）或 `RANK`（要并列都取）灵活控制。

**写法 2：相关子查询**
```sql
SELECT t.ProductCd, t.TxnNo, t.TxnDateTime, t.Qty
FROM   T_StockTransaction t
WHERE  t.TxnDateTime = (
    SELECT MAX(t2.TxnDateTime) FROM T_StockTransaction t2
    WHERE t2.ProductCd = t.ProductCd
);
```
缺点：逐行执行子查询，数据量大时慢；**若同一产品同一时刻有两笔（并列最大），会都返回**（可能非预期）。

**写法 3：JOIN 派生表（先聚合出每组最大值，再连回原表）**
```sql
SELECT t.ProductCd, t.TxnNo, t.TxnDateTime, t.Qty
FROM   T_StockTransaction t
INNER JOIN (
    SELECT ProductCd, MAX(TxnDateTime) AS MaxDt
    FROM   T_StockTransaction
    GROUP BY ProductCd
) m ON m.ProductCd = t.ProductCd AND m.MaxDt = t.TxnDateTime;
```
缺点：同样在"同一时刻并列"时会返回多行；两次访问表。

**三者对比小结（面试可直接背）：**
| 写法 | 可读性 | 性能 | 并列处理 | 推荐度 |
|------|--------|------|----------|--------|
| ROW_NUMBER 窗口 | 高 | 好（一次扫描） | 可控（RN 唯一 / RANK 并列） | ★★★★★ |
| 相关子查询 | 中 | 差（逐行） | 并列全返回 | ★★ |
| JOIN 派生表 | 中 | 中 | 并列全返回 | ★★★ |

> **面试标准答话**："每组取最新我首选窗口函数 `ROW_NUMBER() OVER(PARTITION BY 分组 ORDER BY 时间 DESC)` 再取 `rn=1`，
> 因为一次扫描、意图清晰、还能通过 ROW_NUMBER/RANK 精确控制并列。子查询和 JOIN 派生表也能做，但性能或并列语义不如它。"

---

## 8. CASE 表达式与行转列（透视报表）

### 8.1 CASE WHEN：SQL 里的 if-else

两种形式：
```sql
-- 简单 CASE（等值匹配）
SELECT TxnNo,
       CASE TxnType
           WHEN 'IN'  THEN '入库'
           WHEN 'OUT' THEN '出库'
           WHEN 'ADJ' THEN '盘点调整'
           ELSE '其他'
       END AS 种别名称
FROM   T_StockTransaction;

-- 搜索 CASE（任意条件，更强大）
SELECT ProductCd, PhysicalQty,
       CASE WHEN PhysicalQty = 0            THEN '缺货'
            WHEN PhysicalQty < 100          THEN '低库存'
            WHEN PhysicalQty < 1000         THEN '正常'
            ELSE '高库存'
       END AS 库存等级
FROM   T_Stock;
```

### 8.2 条件聚合：SUM(CASE WHEN ...) 实现行转列（透视 pivot）

这是**做日报/月报最核心的技巧**。把"按种别的多行"转成"一行多列"。

**需求**：每个仓库一行，分列显示 IN / OUT / ADJ 的合计数量（制造业出入库日报的标准形态）。
```sql
SELECT WarehouseCd,
       SUM(CASE WHEN TxnType='IN'  THEN Qty ELSE 0 END) AS 入库量,
       SUM(CASE WHEN TxnType='OUT' THEN Qty ELSE 0 END) AS 出库量,
       SUM(CASE WHEN TxnType='ADJ' THEN Qty ELSE 0 END) AS 调整量,
       COUNT(*)                                          AS 总笔数
FROM   T_StockTransaction
WHERE  IsDeleted = 0
  AND  TxnDateTime >= '2026-07-01' AND TxnDateTime < '2026-08-01'
GROUP BY WarehouseCd;
```
**结果示意**（原本每仓每种别一行 → 变成每仓一行、种别成列）：
```
WarehouseCd | 入库量  | 出库量  | 调整量 | 总笔数
------------+--------+--------+-------+------
WH-A        | 12000  | 8300   | -50   | 128
WH-B        | 5000   | 4200   | 0     | 76
```
原理：`CASE` 让"不匹配的种别贡献 0"，`SUM` 只把匹配的加起来 → 等于"按种别分别求和"，
但结果落在**同一行的不同列**。这就是"行转列"。

**按月透视**（列 = 月份）：
```sql
SELECT ProductCd,
       SUM(CASE WHEN MONTH(TxnDateTime)=1 THEN Qty ELSE 0 END) AS 一月,
       SUM(CASE WHEN MONTH(TxnDateTime)=2 THEN Qty ELSE 0 END) AS 二月,
       SUM(CASE WHEN MONTH(TxnDateTime)=3 THEN Qty ELSE 0 END) AS 三月
       -- ... 十二月
FROM   T_StockTransaction
WHERE  TxnType='OUT' AND YEAR(TxnDateTime)=2026
GROUP BY ProductCd;
```

> **PIVOT 关键字**：SQL Server 有专门的 `PIVOT` 语法，但它列名必须写死、可读性差、动态列麻烦。
> **实务里大家更爱 `SUM(CASE WHEN)`**——灵活、跨数据库通用、易懂。面试写 `SUM(CASE WHEN)` 更稳。

### 8.3 CASE 的两个易错点

1. **CASE 返回单一类型**：各 `THEN` 分支的返回值类型要能统一，否则隐式转换可能报错或截断。
2. **NULL 匹配**：`CASE TxnType WHEN NULL THEN ...` **永远不匹配**（又是 NULL = 的坑），
   要判 NULL 得用搜索 CASE：`CASE WHEN TxnType IS NULL THEN ...`。

---

## 9. 数据修改（INSERT / UPDATE / DELETE / MERGE）+ 真实 seed 精读

### 9.1 INSERT

```sql
-- 单行
INSERT INTO Sys_Langs (LangKey, ZhCN, En, Ja, Status)
VALUES ('login.title', '登录', 'Login', 'ログイン', 'reviewed');

-- 多行（一条语句插多行，比多条 INSERT 快）
INSERT INTO Sys_Langs (LangKey, ZhCN, En) VALUES
 ('btn.save',   '保存', 'Save'),
 ('btn.cancel', '取消', 'Cancel');

-- INSERT ... SELECT（从查询结果插入，数据迁移常用）
INSERT INTO T_StockArchive (Id, ProductCd, PhysicalQty)
SELECT Id, ProductCd, PhysicalQty FROM T_Stock WHERE IsDeleted = 1;
```
**坑**：`INSERT` 一定要显式列出列名（`INSERT INTO t (a,b,c)`）。省略列名依赖列顺序，
表结构一变就错位——生产事故常见来源。

### 9.2 UPDATE（含 UPDATE ... FROM JOIN 写法）

```sql
-- 基础 UPDATE：一定要带 WHERE，否则全表更新！
UPDATE T_Stock
SET    AvailableQty = PhysicalQty - AllocatedQty,
       ModifyDate   = SYSDATETIME(),
       Modifier     = 'system'
WHERE  WarehouseCd = 'WH-A' AND ProductCd = 'P001';
```

**UPDATE ... FROM JOIN（SQL Server 特有，超实用）**：根据另一张表的值批量更新。
```sql
-- 用"每个 (仓/库位/品/批) 的最新流水单价"回填 T_Stock 的 UnitPrice
UPDATE s
SET    s.UnitPrice = latest.UnitPrice,
       s.ModifyDate = SYSDATETIME()
FROM   T_Stock s
INNER JOIN (
    SELECT WarehouseCd, LocationCd, ProductCd, LotNo, UnitPrice,
           ROW_NUMBER() OVER (PARTITION BY WarehouseCd,LocationCd,ProductCd,LotNo
                              ORDER BY TxnDateTime DESC) AS rn
    FROM   T_StockTransaction
    WHERE  UnitPrice IS NOT NULL
) latest
   ON latest.WarehouseCd=s.WarehouseCd AND latest.LocationCd=s.LocationCd
  AND latest.ProductCd=s.ProductCd     AND latest.LotNo=s.LotNo
  AND latest.rn = 1;                    -- 只用最新一笔
```
**注意**：`SET s.列 = ...` 里 `s` 是 `FROM` 里的别名。这是 T-SQL 独有语法（MySQL/PostgreSQL 写法不同）。

> **CP6 铁律**：`T_Stock` 注释明确写"直接 UPDATE 禁止，写入口只有 `StockMovementService`"。
> 生产里库存不允许裸 SQL 改（要走 Service 记流水、保证账实一致）。上面 UPDATE 只是教学演示。
> 面试时你能说出"库存这类关键数据我不会裸 UPDATE，要走统一 Service 写流水"是加分项。

### 9.3 DELETE vs TRUNCATE

```sql
DELETE FROM Sys_Langs WHERE Status = 'draft';   -- 删符合条件的行，逐行记日志，可回滚，触发触发器
TRUNCATE TABLE T_TempImport;                    -- 清空整表，不逐行、极快、不可加 WHERE
```
| 维度 | DELETE | TRUNCATE |
|------|--------|----------|
| 能否带 WHERE | 能 | 不能（只能清空整表） |
| 速度 | 慢（逐行、记日志） | 极快（只记页释放） |
| 触发器 | 触发 | 不触发 |
| 自增种子 | 保留 | 重置为初始值 |
| 事务回滚 | 可 | 可（在显式事务里也能回滚，但受最小日志限制） |
| 外键 | 可（除非被引用） | 表被外键引用时不允许 |

> **CP6 现实**：业务表用**逻辑删除**（`UPDATE ... SET IsDeleted = 1`），几乎不用物理 `DELETE`。
> `TRUNCATE` 只在清临时表/测试数据时用。面试问"你怎么删数据"，答"生产用软删 IsDeleted，
> 保留审计与可恢复性；物理删只在临时表"最稳。

### 9.4 MERGE（upsert：有则更新、无则插入）—— 精读真实 `import-langs.sql`

`MERGE` 一条语句同时处理"匹配就更新、不匹配就插入"，是**幂等 upsert** 的利器。
下面是 CP6 真实脚本 `deploy/import-langs.sql` 的核心（把仓库里的多语言 JSON 灌回 `Sys_Langs`）：

```sql
MERGE dbo.Sys_Langs AS t                       -- 目标表（target）
USING (
    SELECT LangKey, TenantId, Status, ZhCN, ZhTW, En, Ja, Ko
    FROM OPENJSON(@json) WITH (                 -- 源：把一段 JSON 解析成表（源 source）
        LangKey  nvarchar(200) '$.LangKey',
        TenantId int           '$.TenantId',
        Status   nvarchar(20)  '$.Status',
        ZhCN     nvarchar(500) '$.ZhCN',
        ZhTW     nvarchar(500) '$.ZhTW',
        En       nvarchar(500) '$.En',
        Ja       nvarchar(500) '$.Ja',
        Ko       nvarchar(500) '$.Ko'
    )
) AS s
ON  t.LangKey = s.LangKey                       -- 匹配键：自然键 (LangKey, TenantId)
AND (t.TenantId = s.TenantId
     OR (t.TenantId IS NULL AND s.TenantId IS NULL))  -- ★ NULL 全局键要手动配（§1.5 坑4！）
WHEN MATCHED THEN UPDATE SET                    -- 已存在 → 更新翻译
    t.Status = ISNULL(s.Status, t.Status),
    t.ZhCN = s.ZhCN, t.ZhTW = s.ZhTW, t.En = s.En, t.Ja = s.Ja, t.Ko = s.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT          -- 不存在 → 插入新词条
    (TenantId, LangKey, Status, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (s.TenantId, s.LangKey, ISNULL(s.Status, 'reviewed'),
            s.ZhCN, s.ZhTW, s.En, s.Ja, s.Ko);
```

**逐点讲解（这段浓缩了本章一半知识点）：**
- `MERGE target USING source ON 匹配条件`：三段式。`WHEN MATCHED` / `WHEN NOT MATCHED BY TARGET` 分支。
- `OPENJSON(...) WITH (...)`：SQL Server 把 JSON 字符串**当表读**的函数，配 `export-langs.sql` 的
  `FOR JSON PATH`（把表导成 JSON）成对使用——这就是 CP6 的"词条版本化 + 灾备"方案。
- **那行 NULL 处理是精髓**：因为 `NULL = NULL` 是 UNKNOWN，全局词条（`TenantId IS NULL`）两边都是 NULL
  时用 `=` 根本配不上，必须补 `OR (t.TenantId IS NULL AND s.TenantId IS NULL)`，否则每次导入都把全局词条
  当"新行"重复插入——正是 §1.5 坑 4 的真实生产体现。
- `ISNULL(s.Status, 'reviewed')`：源里没给 Status 就用默认值（§10 会讲 ISNULL）。
- **幂等**：这脚本反复跑结果一样（已存在的更新、没有的插入，不会重复）——这是部署脚本的黄金标准。

**MERGE 的坑（面试可提，显专业）**：SQL Server 早期版本 `MERGE` 有若干并发/触发器 bug，
社区对它评价两极。很多团队宁可写"`UPDATE` + `INSERT ... WHERE NOT EXISTS`"两步替代（见 §9.5）。
知道"MERGE 好用但有争议"就很专业了。

### 9.5 幂等种子脚本模式：`INSERT ... WHERE NOT EXISTS`（精读 `erp-permission-seed.sql`）

另一种更朴素、更稳的幂等 upsert：**插入前先 `NOT EXISTS` 检查**。CP6 权限种子全用这套。
下面是真实的 `docs/seeds/erp-permission-seed.sql` 核心：

```sql
-- 给每个租户 × 每个动作，登记一条权限点（已存在则跳过 → 可反复安全执行）
INSERT INTO Sys_MenuAction (Id, MenuId, ActionCode, ActionName, Sort, CreateDate, TenantId)
SELECT NEWID(), a.MenuId, a.ActionCode, a.ActionName, a.Sort, SYSDATETIME(), t.Id
FROM   @Actions a                                 -- 表变量：30 个动作定义
CROSS JOIN (SELECT Id FROM Sys_Tenants) t         -- ★ CROSS JOIN = 每租户 × 每动作（笛卡尔积）
WHERE  NOT EXISTS (                               -- ★ 幂等闸门：已有就不插
    SELECT 1 FROM Sys_MenuAction ma
    WHERE ma.TenantId = t.Id
      AND ma.MenuId   = a.MenuId
      AND ma.ActionCode = a.ActionCode
);
```

**讲解：**
- `NEWID()`：生成 GUID 主键（对应 `Id UNIQUEIDENTIFIER`）。`SYSDATETIME()`：当前时间。
- `CROSS JOIN (SELECT Id FROM Sys_Tenants) t`：**§3.1 的笛卡尔积在生产的真实用途**——
  "把 30 个动作，为每个租户各插一份"。若有 4 个租户，就插 4×30=120 行。这是多租户 seed 的标准手法。
- `WHERE NOT EXISTS (...)`：**幂等的核心**。插之前检查"这个 (租户, 菜单, 动作) 是否已存在"，
  存在就不插。于是脚本**跑 100 遍结果都一样**，绝不重复——部署/灾备脚本必备特性。
- 整个脚本还包在 `BEGIN TRY ... BEGIN TRANSACTION ... COMMIT ... BEGIN CATCH ... ROLLBACK ... THROW`
  里（事务 + 错误处理，见 §12 补充），保证"要么全成功、要么全回滚"。

**`MERGE` vs `INSERT...WHERE NOT EXISTS` 选型**：
- 只需"没有才插、不改已有" → `INSERT ... WHERE NOT EXISTS`（`erp-permission-seed` 用它，因为权限点插了就不动）。
- 需"有则更新、无则插" → `MERGE` 或"`UPDATE` 后 `INSERT ... WHERE NOT EXISTS`"（`import-langs` 用 MERGE，因为词条要更新翻译）。

### 9.6 OUTPUT 子句（拿到被改动的行）

`OUTPUT` 让 INSERT/UPDATE/DELETE **返回受影响的行**（`inserted.` / `deleted.` 伪表），
做审计日志、拿新生成的 Id、"删除并归档"一步到位。
```sql
-- 删除草稿词条，同时把被删的行输出到归档表（一条语句完成"删+存档"）
DELETE FROM Sys_Langs
OUTPUT deleted.LangKey, deleted.ZhCN, deleted.En, SYSDATETIME()
  INTO Sys_LangArchive (LangKey, ZhCN, En, ArchivedAt)
WHERE  Status = 'draft';

-- UPDATE 时同时看到改前改后值
UPDATE T_Stock SET AllocatedQty = AllocatedQty + 10
OUTPUT deleted.AllocatedQty AS 改前, inserted.AllocatedQty AS 改后
WHERE  WarehouseCd='WH-A' AND ProductCd='P001' AND LotNo='';
```
- `inserted` = 改后/新插的值；`deleted` = 改前/被删的值（UPDATE 两者都有）。

> **对应 EF Core**：EF 保存后能拿到数据库生成的主键/计算列，底层正是靠 `OUTPUT`。Day 1 学的
> `SaveChanges()` 之后 `entity.Id` 有值，就是 EF 用 `OUTPUT inserted.Id` 取回来的。

---

## 10. T-SQL 常用函数速查（面试手写救命表）

### 10.1 日期函数

```sql
SELECT GETDATE();                                  -- 当前日期时间（本地）；SYSDATETIME() 精度更高
SELECT GETUTCDATE();                               -- 当前 UTC
SELECT DATEADD(DAY,  7, GETDATE());                -- 加 7 天（YEAR/MONTH/DAY/HOUR/MINUTE...）
SELECT DATEADD(MONTH, -1, TxnDateTime);            -- 减 1 个月
SELECT DATEDIFF(DAY, TxnDateTime, GETDATE());      -- 两日期相差多少天（后减前）
SELECT DATEDIFF(HOUR, CreateDate, ModifyDate);     -- 相差小时
SELECT YEAR(TxnDateTime), MONTH(TxnDateTime), DAY(TxnDateTime);  -- 取年/月/日
SELECT DATEPART(WEEKDAY, GETDATE());               -- 星期几
SELECT EOMONTH(GETDATE());                         -- 本月最后一天（月末，SQL Server 2012+）
SELECT CAST(GETDATE() AS DATE);                    -- 砍掉时间部分，只留日期
```

**FORMAT vs CONVERT（格式化日期为字符串）：**
```sql
SELECT FORMAT(GETDATE(), 'yyyy-MM-dd');            -- '2026-07-15'（FORMAT 灵活但慢，大数据量慎用）
SELECT FORMAT(GETDATE(), 'yyyy年MM月dd日');         -- 中文格式
SELECT CONVERT(varchar(10), GETDATE(), 23);        -- '2026-07-15'（23 = ISO 日期，CONVERT 快）
SELECT CONVERT(varchar(19), GETDATE(), 120);       -- '2026-07-15 14:30:00'（120 = ODBC 规范）
```
> **性能提示**：`FORMAT` 基于 .NET CLR，比 `CONVERT` 慢一个数量级。批量/高频场景用 `CONVERT` 配样式码。

**按日期范围查询的正确姿势（面试常错）：**
```sql
-- ✗ 别对列包函数，会走不了索引（"非 SARGable"）
WHERE YEAR(TxnDateTime)=2026 AND MONTH(TxnDateTime)=7
-- ✓ 用半开区间 [月初, 下月初)，能走索引；也天然规避"月末含不含时间"的坑
WHERE TxnDateTime >= '2026-07-01' AND TxnDateTime < '2026-08-01'
```

### 10.2 字符串函数

```sql
SELECT LEN('ABC');                        -- 3（字符数；注意 LEN 不含尾部空格，DATALENGTH 含）
SELECT SUBSTRING('TXN20260715-001', 4, 8);-- '20260715'（从第4位取8个字符，1-based）
SELECT CHARINDEX('-', 'TXN-001');         -- 4（'-' 首次出现的位置；找不到返回 0）
SELECT LEFT('ABCDEF', 3);                 -- 'ABC'
SELECT RIGHT('ABCDEF', 2);                -- 'EF'
SELECT UPPER('abc'), LOWER('ABC');        -- 'ABC' / 'abc'
SELECT LTRIM(RTRIM('  x  '));             -- 'x'（去两端空格；SQL2017+ 可用 TRIM）
SELECT REPLACE('a-b-c', '-', '/');        -- 'a/b/c'
SELECT REPLICATE('ab', 3);               -- 'ababab'
SELECT CONCAT('PN-', ProductCd, '-', LotNo);  -- 拼接，自动把 NULL 当空串（比 + 安全）
```

**STRING_AGG（把多行拼成一个字符串，SQL Server 2017+，超实用）：**
```sql
-- 每个仓库，把它有的产品编码拼成一行逗号分隔（"WH-A: P001,P002,P003"）
SELECT WarehouseCd,
       STRING_AGG(ProductCd, ',') WITHIN GROUP (ORDER BY ProductCd) AS 产品清单
FROM   T_Stock
WHERE  IsDeleted = 0
GROUP BY WarehouseCd;
```
> `STRING_AGG` 是聚合函数，配 `GROUP BY` 用，`WITHIN GROUP (ORDER BY ...)` 控制拼接顺序。
> 旧版本 SQL Server 没有它，得用 `FOR XML PATH` 的黑魔法——面试提一句"新版用 STRING_AGG，
> 老版用 FOR XML PATH"很加分。

### 10.3 NULL 处理三剑客：ISNULL / COALESCE / NULLIF

```sql
-- ISNULL(a, b)：a 为 NULL 就取 b（SQL Server 特有，只接受 2 个参数）
SELECT ISNULL(UnitPrice, 0) FROM T_Stock;              -- 未定价的当 0

-- COALESCE(a, b, c, ...)：返回第一个非 NULL 的（标准 SQL，多参数，更灵活）
SELECT COALESCE(UnitPrice, ListPrice, 0) FROM T_Stock; -- 依次回退：单价→标价→0

-- NULLIF(a, b)：a=b 就返回 NULL，否则返回 a —— 防除零经典用法
SELECT SUM(OutQty) / NULLIF(SUM(InQty), 0) FROM ...;   -- InQty 合计为 0 时，除数变 NULL 而非报错
```
**ISNULL vs COALESCE 区别（面试会问）：**
- `ISNULL` 只吃 2 个参数；`COALESCE` 吃任意多个。
- `COALESCE` 是 ANSI 标准（跨库通用）；`ISNULL` 是 SQL Server 专有。
- 返回类型判定不同：`ISNULL` 取第一个参数的类型；`COALESCE` 按优先级规则取"最高优先级类型"，
  有时会有意外的隐式转换。生产里对类型敏感时更推荐显式 `CAST` + `COALESCE`。

### 10.4 类型转换：CAST / CONVERT / TRY_CAST / TRY_CONVERT

```sql
SELECT CAST('123' AS INT);                  -- 123（标准 SQL，跨库通用）
SELECT CAST(PhysicalQty AS INT);            -- 小数转整（截断，不四舍五入）
SELECT CONVERT(INT, '123');                 -- 同上（SQL Server 专有，能带样式码，见日期格式化）
SELECT TRY_CAST('abc' AS INT);              -- 转不了返回 NULL 而不是报错（防脏数据崩）
SELECT TRY_CONVERT(DATE, '2026-13-99');     -- 非法日期返回 NULL
```
> **面试价值**：处理外部导入的脏数据（如 Excel 上传）时，用 `TRY_CAST`/`TRY_CONVERT` 让转不了的返回 NULL，
> 配 `WHERE TRY_CAST(...) IS NULL` 就能一把捞出所有"格式错误的行"。这是数据清洗的实战技巧。

---

## 11. 分页：OFFSET-FETCH（对应 EF 的 Skip / Take）

`TOP` 只能取前 N，做不了"第 3 页"。分页用 `OFFSET ... FETCH`（SQL Server 2012+）：

```sql
-- 库存列表，按产品编码排序，取第 3 页（每页 20 条）
SELECT ProductCd, WarehouseCd, PhysicalQty
FROM   T_Stock
WHERE  IsDeleted = 0
ORDER BY ProductCd                       -- ★ OFFSET-FETCH 必须有 ORDER BY（否则"第几页"没意义）
OFFSET  40 ROWS                          -- 跳过前 40 条（第1、2页）= (页码-1) × 每页
FETCH NEXT 20 ROWS ONLY;                 -- 取 20 条
```
- 第 N 页：`OFFSET (N-1) * 每页 ROWS FETCH NEXT 每页 ROWS ONLY`。
- **必须配 `ORDER BY`**，否则 SQL Server 直接报错（分页要有稳定顺序）。

**对应 EF Core（你 Day 1 学的）：**
```csharp
var page = db.Stocks
    .Where(s => !s.IsDeleted)
    .OrderBy(s => s.ProductCd)
    .Skip(40)          // → OFFSET 40 ROWS
    .Take(20)          // → FETCH NEXT 20 ROWS ONLY
    .ToList();
```
EF 的 `Skip(40).Take(20)` 生成的就是上面这段 `OFFSET 40 FETCH NEXT 20`。**面试问"EF 分页底层 SQL 长啥样"，
就答 OFFSET-FETCH。**

**深分页性能坑（进阶加分）**：`OFFSET 1000000 ROWS` 要先扫过 100 万行再丢掉，越翻越慢。
大数据量分页应改用"键集分页（keyset / seek）"：记住上一页最后一条的排序键，
`WHERE ProductCd > '上页最后值' ORDER BY ProductCd FETCH NEXT 20`。面试提这个直接拔高档次。

**总行数**：分页通常还要一个总数做页码。别用两次查询，可用窗口函数一次拿：
```sql
SELECT ProductCd, PhysicalQty,
       COUNT(*) OVER () AS 总行数            -- 空 OVER() = 整个结果集的总行数
FROM   T_Stock WHERE IsDeleted = 0
ORDER BY ProductCd OFFSET 40 ROWS FETCH NEXT 20 ROWS ONLY;
```

---

## 12. 事务与错误处理（seed 脚本里的那层"壳"，顺带补齐）

前面 `erp-permission-seed.sql` 外面包的那层，是生产脚本的标准骨架：
```sql
SET NOCOUNT ON;         -- 不返回"N 行受影响"的计数消息（减少网络噪音）
SET XACT_ABORT ON;      -- 出错时自动整体回滚事务（强烈建议开）
BEGIN TRY
    BEGIN TRANSACTION;      -- 开启事务：以下操作"要么全成、要么全败"
        -- ... INSERT / UPDATE / MERGE ...
    COMMIT TRANSACTION;    -- 全部成功 → 提交
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;   -- 出错 → 回滚，撤销本事务所有改动
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;                 -- 把错误抛给调用方（sqlcmd 会以非 0 退出码失败）
END CATCH;
```
**ACID 一句话**：事务保证 **A**原子（全成或全败）、**C**一致、**I**隔离、**D**持久。
`BEGIN TRAN...COMMIT/ROLLBACK` 就是原子性的实现。面试问"转账怎么保证不丢钱"——事务。

**四种隔离级别**（认识即可）：读未提交 < 读已提交（SQL Server 默认）< 可重复读 < 串行化。
级别越高越安全、并发越差。默认"读已提交"会有"不可重复读/幻读"，高一致场景才调高。

---

# ★ 面试题库（先做题，答案在后）★

## A. 概念题 15 问（先自己默答，再看后面的详细答案）

1. NULL 是什么？为什么 `WHERE UnitPrice = NULL` 查不到任何数据？怎样才能正确判空？
2. SQL 的逻辑执行顺序是什么？为什么 `WHERE` 里不能用 `SELECT` 定义的列别名，`ORDER BY` 却可以？
3. `INNER JOIN` / `LEFT JOIN` / `RIGHT JOIN` / `FULL JOIN` / `CROSS JOIN` 各自语义？
4. LEFT JOIN 时，一个针对**右表**的过滤条件，写在 `ON` 里和写在 `WHERE` 里有什么区别？
5. `WHERE` 和 `HAVING` 的区别？能用 `WHERE` 的场景为什么不建议用 `HAVING`？
6. `COUNT(*)`、`COUNT(列)`、`COUNT(DISTINCT 列)`、`COUNT(1)` 分别数什么？`COUNT(*)` 和 `COUNT(1)` 谁快？
7. `IN` 和 `EXISTS` 的区别？为什么"找差集"要用 `NOT EXISTS` 而不是 `NOT IN`？
8. `ROW_NUMBER`、`RANK`、`DENSE_RANK` 三者在遇到并列值时有什么不同？
9. "每个产品取最新一笔流水"有哪几种写法？各自优劣？你首选哪个，为什么？
10. `DELETE`、`TRUNCATE`、逻辑删除（软删）三者区别？CP6 生产里用哪种，为什么？
11. `MERGE` 和 `INSERT ... WHERE NOT EXISTS` 都能做幂等 upsert，怎么选？各自适合什么场景？
12. 什么是"幂等"的部署脚本？为什么种子脚本必须幂等？CP6 是怎么实现的？
13. `ISNULL` 和 `COALESCE` 有什么区别？
14. SQL Server 分页怎么写？对应 EF Core 的什么方法？深分页为什么慢，怎么优化？
15. 什么是"非 SARGable"查询？举一个让索引失效的写法，并给出改法。

## B. 手写题 15 道（题目在前，答案在最后统一给出，先自己写！）

题目全部基于本章 4 张真实表：`T_Stock`、`T_StockTransaction`、`Wf_FlowTask`、`Sys_Langs`。

- **手写 1（库存汇总）**：按仓库统计 `T_Stock` 里每个仓库的物理总量、可用总量、库存行数，
  只算未删除（`IsDeleted=0`）的，按物理总量降序。
- **手写 2（出入库流水统计）**：统计 2026 年 7 月，每个仓库的入库总量、出库总量、净变化（入-出），
  用条件聚合一行一仓呈现。
- **手写 3（每产品最新一次入库）**：`T_StockTransaction` 里，取每个产品**最新一笔 `IN` 流水**的
  流水号、时间、数量（用窗口函数）。
- **手写 4（低库存预警）**：找出可用库存（`AvailableQty`）小于 100 但大于 0 的库存行，
  显示仓库、库位、产品、批次、可用量，按可用量升序。
- **手写 5（找没动过的库存）**：找出 `T_Stock` 里"从来没有过任何流水"的库存行（用 `NOT EXISTS`）。
- **手写 6（工作流待办统计）**：统计每个处理人（`AssigneeId`）当前的待办数量（`Status=0`）和已办数量（`Status<>0`），
  按待办数降序。
- **手写 7（按月透视）**：把 `T_StockTransaction` 里 2026 年每个产品的**出库量**，
  按月份透视成"产品 | 1月 | 2月 | 3月 | ..."的报表（至少写到 3 月）。
- **手写 8（占比分析）**：用窗口函数，列出每个库存行及其"占所在仓库物理总量的百分比"。
- **手写 9（排名）**：按物理库存量给所有产品在**各自仓库内**排名（同仓内并列量给同名次且不跳号），
  取每仓前 3 名。
- **手写 10（累计在库曲线）**：对产品 `'P001'`，按时间顺序，把 IN 记正、OUT 记负，
  算出每笔流水后的"累计在库"（running total）。
- **手写 11（连续 N 天有生产/流水）**：找出"连续 3 天及以上每天都有流水记录"的产品
  （提示：按产品+日期去重后，用 `ROW_NUMBER` 的"岛屿(gaps and islands)"技巧）。
- **手写 12（多语言缺失检查）**：找出 `Sys_Langs` 里"有简体中文 `ZhCN` 但缺日语 `Ja` 翻译"的词条 key。
- **手写 13（幂等种子）**：给 `Sys_Langs` 幂等地插入一条词条 `'stock.available'`（简中"可用库存"、
  英"Available"、日"利用可能"），已存在则不插（用 `NOT EXISTS`）。
- **手写 14（分页）**：`T_Stock` 按产品编码排序，取第 2 页（每页 15 条），同时返回总行数。
- **手写 15（批量回填 UPDATE）**：用每个 (仓/库位/品/批) 的**最新一笔流水单价**，
  回填 `T_Stock.UnitPrice`（用 `UPDATE ... FROM` + 窗口函数）。

---

# ★ 答案与详解 ★

## A. 概念题答案

**1. NULL 与判空**
NULL 表示"未知/缺失"，不是 0 也不是空串。任何值与 NULL 用 `=`、`<>`、`>` 等比较，结果都是 `UNKNOWN`
（三值逻辑），既非真也非假，于是该行不被 `WHERE` 选中——所以 `= NULL` 永远查不到。
正确判空只能用 `IS NULL` / `IS NOT NULL`。延伸坑：`NOT IN` 遇 NULL 返回空集、聚合函数忽略 NULL、
NULL 拼字符串变 NULL。

**2. 逻辑执行顺序**
`FROM → WHERE → GROUP BY → HAVING → SELECT → DISTINCT → ORDER BY → TOP/OFFSET`。
别名在 `SELECT` 阶段（第 5 步）才生成。`WHERE`（第 2 步）、`GROUP BY`、`HAVING` 都在它之前，
此刻别名还不存在，故不能用；`ORDER BY`（第 7 步）在 `SELECT` 之后，所以能用别名。同理 `WHERE`
里不能用聚合函数（聚合在 GROUP BY 第 3 步发生），要过滤聚合结果得用 `HAVING`。

**3. 五种 JOIN**
INNER=只保留两边都匹配的交集；LEFT=左表全保留，右表没匹配的填 NULL；RIGHT=右表全保留；
FULL=两边全保留、任一边无匹配填 NULL；CROSS=笛卡尔积（m×n，不写 ON），生成所有组合
（CP6 seed 里"每租户×每动作"就用它）。

**4. LEFT JOIN 里 ON vs WHERE（右表条件）**
放 `ON`：只影响"怎么匹配右表"，不影响左表保留——左表所有行都在，没匹配的右列为 NULL，保住 LEFT 语义。
放 `WHERE`：JOIN 做完后再过滤，`NULL=值` 是 UNKNOWN，会把"右表没匹配的左行"一起筛掉，
使 LEFT **退化成 INNER**。所以过滤右表用 ON、过滤左表用 WHERE。

**5. WHERE vs HAVING**
WHERE 分组前筛行（不能用聚合），HAVING 分组后筛组（可用聚合）。能用 WHERE 就别用 HAVING，
因为 WHERE 先减少参与聚合的行数，更快；HAVING 是对已聚合的结果再筛，代价更高。

**6. COUNT 家族**
`COUNT(*)`=所有行（含 NULL、含全 NULL 行）；`COUNT(列)`=该列非 NULL 的行数；
`COUNT(DISTINCT 列)`=该列不同非 NULL 值的个数；`COUNT(1)` 等价 `COUNT(*)`。
`COUNT(*)` 与 `COUNT(1)` 在 SQL Server 里**性能完全相同**，"COUNT(1) 更快"是误传。

**7. IN vs EXISTS / 为什么用 NOT EXISTS**
EXISTS 判"子查询是否至少有一行"，找到即短路。二者在现代优化器下常被优化成半连接，性能相近；
经验上子查询集大用 EXISTS（短路）、集小无 NULL 用 IN（可读）。关键区别在 NULL：`NOT IN` 若子查询
含 NULL 会整体返回空集（`x<>NULL` 恒 UNKNOWN），`NOT EXISTS` 不受 NULL 影响——所以找差集一律
用 `NOT EXISTS`。

**8. ROW_NUMBER / RANK / DENSE_RANK**
遇并列时：ROW_NUMBER 仍给连续唯一序号（强行分先后）；RANK 给相同名次但之后跳号（1,2,2,4）；
DENSE_RANK 给相同名次且之后不跳号（1,2,2,3）。分页/每组取一条用 ROW_NUMBER，
业务排名要"并列且不跳号"用 DENSE_RANK。

**9. 每组取最新（见 §7.4）**
三种：①窗口 `ROW_NUMBER() OVER(PARTITION BY 品 ORDER BY 时间 DESC)` 取 rn=1；②相关子查询
`WHERE 时间 = (SELECT MAX(时间) ...)`；③JOIN 派生表（先 GROUP BY MAX 再连回）。
首选窗口函数：一次扫描、意图清晰、并列可控（ROW_NUMBER 唯一 / RANK 全取）。子查询逐行慢，
JOIN 派生表两次访问表，且后两者遇"同刻并列"会返回多行。

**10. DELETE / TRUNCATE / 软删**
DELETE 逐行删可带 WHERE、可回滚、触发触发器、保留自增种子；TRUNCATE 清空整表极快、不能带 WHERE、
重置自增、不触发触发器、被外键引用时不可用。软删=`UPDATE SET IsDeleted=1` 不真删。CP6 业务表
一律软删（保留审计与可恢复性、配合 EF 全局过滤器），物理 DELETE/TRUNCATE 只用于临时表/测试数据。

**11. MERGE vs INSERT...WHERE NOT EXISTS**
MERGE 一条语句处理"匹配则更新、不匹配则插入"，适合"要更新已有值"的 upsert（如 import-langs 更新翻译）；
INSERT...WHERE NOT EXISTS 只做"没有才插、有则不动"，适合"插了就不改"的登记（如权限点 seed）。
MERGE 更紧凑但历史上有并发/触发器争议，保守团队用"UPDATE + INSERT WHERE NOT EXISTS"两步替代。

**12. 幂等种子脚本**
幂等=同一脚本执行任意多次，结果与执行一次相同（不重复插、不误覆盖）。种子脚本必须幂等，
因为部署/灾备会反复跑，不能因跑第二遍就插重、报唯一键冲突。CP6 两种实现：①`INSERT...WHERE NOT EXISTS`
（按自然键检查存在性，erp-permission-seed）；②`MERGE ON 自然键`（import-langs）。且都包在事务里
保证原子性。

**13. ISNULL vs COALESCE**
ISNULL 是 SQL Server 专有、只接受 2 参、返回类型取第一参数类型；COALESCE 是 ANSI 标准、接受多参、
返回第一个非 NULL 值、返回类型按类型优先级推导。跨库/多级回退用 COALESCE，简单二选一且在意类型用 ISNULL。

**14. 分页**
`ORDER BY ... OFFSET (页-1)*页大小 ROWS FETCH NEXT 页大小 ROWS ONLY`，必须有 ORDER BY。
对应 EF Core 的 `.Skip(n).Take(m)`。深分页慢是因为 OFFSET 要先扫过并丢弃前面所有行；
优化用键集分页（keyset/seek）：记住上页最后的排序键，`WHERE key > 上页末值 ... FETCH NEXT`。

**15. 非 SARGable**
指条件无法利用索引（Search ARGument able 的否定）。典型：对列包函数或运算，如
`WHERE YEAR(TxnDateTime)=2026`、`WHERE UnitPrice*2>100`、前导通配 `LIKE '%x'`、列上隐式类型转换。
改法：把函数移到常量侧，用范围代替，如 `WHERE TxnDateTime>='2026-01-01' AND TxnDateTime<'2027-01-01'`。

## B. 手写题答案

**手写 1（库存汇总）**
```sql
SELECT WarehouseCd,
       SUM(PhysicalQty)  AS 物理总量,
       SUM(AvailableQty) AS 可用总量,
       COUNT(*)          AS 库存行数
FROM   T_Stock
WHERE  IsDeleted = 0
GROUP BY WarehouseCd
ORDER BY 物理总量 DESC;
```

**手写 2（出入库条件聚合）**
```sql
SELECT WarehouseCd,
       SUM(CASE WHEN TxnType='IN'  THEN Qty ELSE 0 END) AS 入库总量,
       SUM(CASE WHEN TxnType='OUT' THEN Qty ELSE 0 END) AS 出库总量,
       SUM(CASE WHEN TxnType='IN'  THEN Qty ELSE 0 END)
         - SUM(CASE WHEN TxnType='OUT' THEN Qty ELSE 0 END) AS 净变化
FROM   T_StockTransaction
WHERE  IsDeleted = 0
  AND  TxnDateTime >= '2026-07-01' AND TxnDateTime < '2026-08-01'
GROUP BY WarehouseCd
ORDER BY WarehouseCd;
```

**手写 3（每产品最新一笔 IN）**
```sql
WITH R AS (
    SELECT ProductCd, TxnNo, TxnDateTime, Qty,
           ROW_NUMBER() OVER (PARTITION BY ProductCd ORDER BY TxnDateTime DESC) AS rn
    FROM   T_StockTransaction
    WHERE  TxnType = 'IN'
)
SELECT ProductCd, TxnNo, TxnDateTime, Qty
FROM   R
WHERE  rn = 1
ORDER BY ProductCd;
```

**手写 4（低库存预警）**
```sql
SELECT WarehouseCd, LocationCd, ProductCd, LotNo, AvailableQty
FROM   T_Stock
WHERE  IsDeleted = 0
  AND  AvailableQty > 0
  AND  AvailableQty < 100
ORDER BY AvailableQty ASC;
```

**手写 5（找没动过的库存，NOT EXISTS）**
```sql
SELECT s.WarehouseCd, s.LocationCd, s.ProductCd, s.LotNo, s.PhysicalQty
FROM   T_Stock s
WHERE  s.IsDeleted = 0
  AND  NOT EXISTS (
        SELECT 1 FROM T_StockTransaction t
        WHERE t.WarehouseCd = s.WarehouseCd
          AND t.LocationCd  = s.LocationCd
          AND t.ProductCd   = s.ProductCd
          AND t.LotNo       = s.LotNo
      );
```

**手写 6（工作流待办统计）**
```sql
SELECT AssigneeId,
       SUM(CASE WHEN Status = 0  THEN 1 ELSE 0 END) AS 待办数,
       SUM(CASE WHEN Status <> 0 THEN 1 ELSE 0 END) AS 已办数,
       COUNT(*)                                     AS 合计
FROM   Wf_FlowTask
GROUP BY AssigneeId
ORDER BY 待办数 DESC;
```

**手写 7（按月透视出库量）**
```sql
SELECT ProductCd,
       SUM(CASE WHEN MONTH(TxnDateTime)=1 THEN Qty ELSE 0 END) AS 一月,
       SUM(CASE WHEN MONTH(TxnDateTime)=2 THEN Qty ELSE 0 END) AS 二月,
       SUM(CASE WHEN MONTH(TxnDateTime)=3 THEN Qty ELSE 0 END) AS 三月
       -- 四月~十二月同理往后加
FROM   T_StockTransaction
WHERE  TxnType = 'OUT'
  AND  TxnDateTime >= '2026-01-01' AND TxnDateTime < '2027-01-01'
GROUP BY ProductCd
ORDER BY ProductCd;
```

**手写 8（占比，窗口函数）**
```sql
SELECT WarehouseCd, ProductCd, LotNo, PhysicalQty,
       SUM(PhysicalQty) OVER (PARTITION BY WarehouseCd) AS 本仓总量,
       CASE WHEN SUM(PhysicalQty) OVER (PARTITION BY WarehouseCd) = 0 THEN 0
            ELSE PhysicalQty * 100.0 / SUM(PhysicalQty) OVER (PARTITION BY WarehouseCd)
       END AS 占本仓百分比
FROM   T_Stock
WHERE  IsDeleted = 0
ORDER BY WarehouseCd, 占本仓百分比 DESC;
```

**手写 9（仓内排名取前3，DENSE_RANK）**
```sql
WITH R AS (
    SELECT WarehouseCd, ProductCd, PhysicalQty,
           DENSE_RANK() OVER (PARTITION BY WarehouseCd ORDER BY PhysicalQty DESC) AS 名次
    FROM   T_Stock
    WHERE  IsDeleted = 0
)
SELECT WarehouseCd, ProductCd, PhysicalQty, 名次
FROM   R
WHERE  名次 <= 3
ORDER BY WarehouseCd, 名次;
```

**手写 10（累计在库，SUM OVER）**
```sql
SELECT ProductCd, TxnDateTime, TxnType, Qty,
       SUM(CASE WHEN TxnType='IN'  THEN Qty
                WHEN TxnType='OUT' THEN -Qty ELSE 0 END)
           OVER (PARTITION BY ProductCd ORDER BY TxnDateTime
                 ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS 累计在库
FROM   T_StockTransaction
WHERE  ProductCd = 'P001'
ORDER BY TxnDateTime;
```

**手写 11（连续 3 天有流水，岛屿技巧）**
思路：先把每个产品有流水的"日期"去重；对每个产品按日期排 `ROW_NUMBER`；
`日期 - rn 天` 得到一个"锚点"，**连续的日期锚点相同**；按 (产品, 锚点) 分组，组内计数 ≥3 就是连续≥3天。
```sql
WITH D AS (   -- 每个产品每天一行（去重到"天"）
    SELECT DISTINCT ProductCd, CAST(TxnDateTime AS DATE) AS Dt
    FROM   T_StockTransaction
),
G AS (        -- 计算连续分组锚点：日期减去行号天数，连续则锚点恒定
    SELECT ProductCd, Dt,
           DATEADD(DAY,
               -ROW_NUMBER() OVER (PARTITION BY ProductCd ORDER BY Dt),
               Dt) AS 岛锚
    FROM   D
)
SELECT ProductCd, MIN(Dt) AS 连续开始, MAX(Dt) AS 连续结束, COUNT(*) AS 连续天数
FROM   G
GROUP BY ProductCd, 岛锚
HAVING COUNT(*) >= 3
ORDER BY ProductCd, 连续开始;
```
> "Gaps and Islands"是面试进阶经典，能写出这段说明你窗口函数真的会用了。

**手写 12（多语言缺失检查）**
```sql
SELECT LangKey, ZhCN
FROM   Sys_Langs
WHERE  ZhCN IS NOT NULL AND ZhCN <> ''      -- 有简中
  AND (Ja IS NULL OR Ja = '');              -- 缺日语
```

**手写 13（幂等插入词条，NOT EXISTS）**
```sql
INSERT INTO Sys_Langs (LangKey, Status, ZhCN, En, Ja)
SELECT 'stock.available', 'reviewed', '可用库存', 'Available', '利用可能'
WHERE NOT EXISTS (
    SELECT 1 FROM Sys_Langs
    WHERE LangKey = 'stock.available'
      AND TenantId IS NULL          -- 全局词条：TenantId 为 NULL（注意 NULL 要用 IS）
);
```

**手写 14（分页 + 总数）**
```sql
SELECT ProductCd, WarehouseCd, PhysicalQty,
       COUNT(*) OVER () AS 总行数
FROM   T_Stock
WHERE  IsDeleted = 0
ORDER BY ProductCd
OFFSET  15 ROWS               -- 第2页 = (2-1)*15
FETCH NEXT 15 ROWS ONLY;
```

**手写 15（批量回填单价，UPDATE...FROM + 窗口）**
```sql
UPDATE s
SET    s.UnitPrice  = latest.UnitPrice,
       s.ModifyDate = SYSDATETIME(),
       s.Modifier   = 'price-backfill'
FROM   T_Stock s
INNER JOIN (
    SELECT WarehouseCd, LocationCd, ProductCd, LotNo, UnitPrice,
           ROW_NUMBER() OVER (
               PARTITION BY WarehouseCd, LocationCd, ProductCd, LotNo
               ORDER BY TxnDateTime DESC) AS rn
    FROM   T_StockTransaction
    WHERE  UnitPrice IS NOT NULL
) latest
   ON latest.WarehouseCd = s.WarehouseCd
  AND latest.LocationCd  = s.LocationCd
  AND latest.ProductCd   = s.ProductCd
  AND latest.LotNo       = s.LotNo
  AND latest.rn = 1
WHERE s.IsDeleted = 0;
```
> 生产提醒：`T_Stock` 实际禁止裸 UPDATE（要走 `StockMovementService`）。此题考的是
> `UPDATE...FROM` + 窗口函数取"每组最新"的写法，面试白板题很常见。

---

# ★ 自测清单（面试前一晚过一遍，能全部口答即达标）★

**基础概念**
- [ ] 能说清 NULL 的三值逻辑，以及 `= NULL` 为什么无效、判空要用 `IS NULL`
- [ ] 能默写 SQL 逻辑执行顺序，并解释"WHERE 用不了别名、ORDER BY 能用"
- [ ] 能解释主键/外键、GUID 主键 vs 自增主键的取舍
- [ ] 知道业务表查询要带 `IsDeleted = 0`（软删）

**JOIN**
- [ ] 能画五种 JOIN 的文氏图并说语义
- [ ] 能讲清 LEFT JOIN 里"右表条件放 ON vs WHERE"的区别（LEFT 退化成 INNER 的坑）
- [ ] 知道 JOIN 一对多会产生重复行，会用"先聚合再连"或 EXISTS 解决
- [ ] 会写自连接、多表连接

**聚合与分组**
- [ ] 能区分 `COUNT(*)`/`COUNT(列)`/`COUNT(DISTINCT)`，知道 `COUNT(*)`=`COUNT(1)` 同速
- [ ] 能说 GROUP BY 规则（SELECT 非聚合列必须在 GROUP BY 里）
- [ ] 能区分 WHERE vs HAVING，知道优先用 WHERE
- [ ] 知道 ROLLUP 能出小计/总计

**子查询 & CTE**
- [ ] 会写标量/IN/EXISTS/相关子查询/派生表
- [ ] 能说清 IN vs EXISTS 与 NOT IN 的 NULL 陷阱
- [ ] 会写 CTE 重构可读性；会写递归 CTE 展开 BOM/组织架构，知道 MAXRECURSION 防环

**窗口函数（重中之重）**
- [ ] 能默写 `函数() OVER (PARTITION BY ... ORDER BY ...)`
- [ ] 能区分 ROW_NUMBER/RANK/DENSE_RANK 的并列行为
- [ ] 会用 LAG/LEAD 做环比、`SUM() OVER` 做累计（会写 `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`）
- [ ] 能手写"每组取最新一条"三种写法并说优劣，首选窗口函数

**行转列 / 数据修改 / 函数 / 分页**
- [ ] 会用 `SUM(CASE WHEN...)` 做透视日报/月报
- [ ] 会写 INSERT（含 INSERT...SELECT）、UPDATE...FROM JOIN、DELETE
- [ ] 能区分 DELETE/TRUNCATE/软删；知道 CP6 用软删
- [ ] 能读懂并解释 MERGE（import-langs）和 INSERT...WHERE NOT EXISTS（权限 seed）两种幂等 upsert
- [ ] 会用 OUTPUT 拿改动行；知道 EF 取回主键靠 OUTPUT
- [ ] 记得住日期/字符串/NULL 函数（DATEADD/DATEDIFF/CONVERT、SUBSTRING/CHARINDEX/STRING_AGG、ISNULL/COALESCE/NULLIF、TRY_CAST）
- [ ] 会写 OFFSET-FETCH 分页，知道对应 EF 的 Skip/Take，知道深分页优化（keyset）

**工程素养（答出即加分）**
- [ ] 知道真实表名要从 `[Table]` 特性/迁移/seed 核实（T_Stock 而非 Stocks）
- [ ] 知道生产禁 `SELECT *`、关键表禁裸 UPDATE（走 Service）
- [ ] 知道"非 SARGable"会让索引失效，能给改法
- [ ] 知道种子脚本要幂等 + 包事务（BEGIN TRY/TRAN/CATCH/ROLLBACK/THROW）

---

> **本章小结**：你现在已经把 SQL 从"表/行/列"一路练到了"窗口函数手写、递归 CTE 展开 BOM、
> MERGE 幂等 upsert"。这些例题全部锚定 CP6 真实的 `T_Stock` / `T_StockTransaction` / `Wf_FlowTask` /
> `Sys_Langs`——面试时你不仅能写出 SQL，还能说"这是我在制造业库存系统里真实处理过的表"，
> 这比背语法强一百倍。下一章我们讲**索引与查询优化**（为什么慢、执行计划怎么看、索引怎么建），
> 把本章写的查询变"快"。
