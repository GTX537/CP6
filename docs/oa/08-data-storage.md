# 08 · 数据存储模型：JSON 列 vs EAV vs 动态建表

> 第01章给了结论（OA 用 JSON 列），本章给透彻的理由和工程细节。这是低代码平台**最重要、最难改**的架构决策——存错了，后期想换代价巨大。

## 📍 学习目标

1. 动态表单数据为什么不能像普通业务表那样建表？
2. JSON 列 / EAV / 动态建表三种方案的本质权衡是什么？
3. JSON 列存了之后，"按字段查询/报表"怎么办？
4. SQL Server 的 JSON 能力（`JSON_VALUE`/`OPENJSON`/计算列索引）怎么用？
5. 什么场景才真的需要 EAV 或动态建表？

---

## 🔎 问题的根源：字段是运行时才知道的

普通业务表（如订单）字段在开发期就定死，建一张 `T_Order` 即可。但低代码表单的字段是**用户在设计器里拖出来的、运行时才存在**，而且**每张表单字段都不同、随时会改**。你不可能为用户拖的每张表单都预先写一个 C# 实体 + 建表 + 迁移。这就是动态数据存储要解决的核心矛盾。

---

## 🔎 三种方案的本质权衡

| 方案 | 怎么存 | 加字段成本 | 查询/报表 | 适用 |
|---|---|---|---|---|
| **动态建表** | 每张表单 → 一张真实表，加字段=ALTER TABLE | 高（DDL+迁移） | ✅ 原生 SQL，最强 | 字段稳定、需复杂报表的核心业务表单 |
| **EAV** | 一张表存 (实例,字段,值) 多行 | 零 | ❌ 一条记录拼多行，多条件查询噩梦 | 字段极度动态、几乎不做横向报表 |
| **JSON 列** ✅ | 整张表单 = 一个 JSON 字段 | 零 | ⚠️ 中等（靠 JSON 函数） | **OA 审批单**（一张张独立单据） |

### 为什么 OA 选 JSON 列？

OA 审批单的特征完美契合 JSON 列：

1. **以"单"为单位读写**——一次操作一整张单子，不需要"查所有单子的某个字段求和"这种重报表。
2. **字段千变万化**——请假单、报销单、采购单字段完全不同，JSON 列零成本容纳。
3. **加字段不改库**——表单改版加字段，JSON 自动容纳，不用迁移。

```csharp
// 一张表存所有表单的数据，DataJson 是个 JSON 字段
[Table("T_Oa_FormData")]
public class FormData : BaseEntity
{
    public string FormKey  { get; set; } = ""; // 哪张表单
    public string BizId    { get; set; } = ""; // 单据号
    public int    FormVer  { get; set; }        // 用哪版 schema 提交的（呼应第01章版本化）
    public string DataJson { get; set; } = "{}";// {"leaveType":"sick","days":3,...}
}
```

---

## 🔎 JSON 列怎么查询：SQL Server 原生能力

"JSON 列没法查"是误解。SQL Server 2016+ 提供完整 JSON 函数：

```sql
-- 查所有病假单（提取 JSON 里的字段做条件）
SELECT * FROM T_Oa_FormData
WHERE FormKey = 'leave_apply'
  AND JSON_VALUE(DataJson, '$.leaveType') = 'sick';

-- 报表：各请假类型天数汇总（把 JSON 当列用）
SELECT JSON_VALUE(DataJson,'$.leaveType') AS type,
       SUM(CAST(JSON_VALUE(DataJson,'$.days') AS DECIMAL)) AS total
FROM T_Oa_FormData WHERE FormKey='leave_apply'
GROUP BY JSON_VALUE(DataJson,'$.leaveType');
```

**高频查询字段想加索引**？用**计算列 + 索引**把 JSON 里的字段"提"成可索引的列：

```sql
ALTER TABLE T_Oa_FormData
  ADD LeaveType AS JSON_VALUE(DataJson, '$.leaveType');  -- 持久化计算列
CREATE INDEX IX_FormData_LeaveType ON T_Oa_FormData(LeaveType);
```

这样既享受 JSON 的灵活，又对热点字段有索引性能——**两全**。EF Core 里 `DataJson` 当普通 string 属性，需要时用 `FromSqlRaw` 或 Dapper 跑上面的 JSON 查询（CP6 正好 EF+Dapper 混用）。

---

## 🔎 混合策略：JSON 为主 + 关键字段冗余成列

生产级最优解往往是**混合**：

- **主体存 JSON**（灵活、零迁移）。
- **少数高频查询/报表字段，冗余一份到真实列**（提交时同步写入，建索引）。如金额、状态、申请人、部门——这些几乎所有报表都要。

```
T_Oa_FormData: BizId | FormKey | DataJson(全量) | Amount(冗余列) | DeptId(冗余列) | Status(冗余列)
```

报表查冗余列（快），看详情读 JSON（全）。这就是简道云/宜搭这类成熟产品的实际做法。

---

## 💡 资深视角

**EAV 为什么是"看起来灵活、实际是坑"？**
EAV 把一条记录拆成 N 行（每字段一行），查"天数>3 且类型=病假"要自连接拼装，3 个条件就是 3 次自连接，报表几乎无法写。它只在"字段无限动态且永不横向分析"的极少数场景才划算。**绝大多数人选 EAV 是被"以后可能要查"吓的，最后死在查询复杂度上。**

**什么时候才真用动态建表？**
当某类表单字段已稳定、数据量大、要做复杂多维报表/和其他业务表 JOIN 时，可以为它**单独**动态建一张物理表（低代码平台叫"物理表模式/强表"）。但这是少数核心表单的优化，不是默认方案。默认 JSON，按需升级。

**为什么这个决策"最难改"？**
存储模型一旦上线、积累了数据，迁移要重写所有读写逻辑 + 数据搬迁。所以宁可一开始想清楚。好在结论很明确：**OA 审批场景 = JSON 列 + 关键字段冗余**，照做即可，别折腾 EAV。

---

## ⚠️ 踩坑记录

1. **被吓去做 EAV**：最常见的过度设计。OA 场景 JSON 列足够。
2. **JSON 裸存不冗余热点字段**：所有报表都全表扫 `JSON_VALUE`，数据量一大就慢。热点字段冗余成计算列/真实列+索引。
3. **不存 schema 版本**：表单改版后老数据的 JSON 结构和新 schema 对不上，读回渲染错位。`FormVer` 必存。
4. **数字/日期当字符串比较**：`JSON_VALUE` 返回字符串，比较前要 `CAST`，否则 `"10" < "9"`。
5. **JSON 里塞大附件**：附件二进制塞进 DataJson 让行巨大。附件存文件/对象存储，JSON 里只放引用。

---

## 🧪 自检题

1. 动态表单数据为什么不能像订单表那样开发期建表？
2. JSON 列 / EAV / 动态建表的本质权衡（加字段成本 vs 查询能力）分别如何？
3. JSON 列怎么按字段查询？怎么给热点字段加索引？
4. 混合策略具体怎么做？为什么报表要查冗余列？
5. 什么场景才真的值得为某张表单动态建物理表？

---

## 🔗 延伸阅读 / 动手清单

**动手清单：**
- [ ] `T_Oa_FormData` 用 JSON 列存 DataJson，带 FormVer
- [ ] 实现按 `JSON_VALUE` 的条件查询（Dapper 或 FromSqlRaw）
- [ ] 给 1~2 个热点字段（如状态、金额、部门）做计算列 + 索引
- [ ] 附件改为存引用，不进 JSON 主体

**下一章** → [09. 与 CP6 集成：IntegrationEvent / BridgeHook / SQL Server JSON](./09-cp6-integration.md)，把 OA 接进 CP6 的现有骨架。
