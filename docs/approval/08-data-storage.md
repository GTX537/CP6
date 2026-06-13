# 08 · 数据存储模型：JSON 列 vs EAV vs 动态建表

> **支撑章。** 02 章一句"`DataJson` 用 JSON 列存"带过了，本章把这个抉择讲透：动态表单的字段是用户配出来的、编译期不知道有哪些列，那提交的数据到底怎么存？业界三条路——JSON 列、EAV、动态建表——各有什么代价，CP6 为什么选 JSON 列，什么时候该换。
>
> 上游：[02 表单引擎](./02-form-runtime.md)（`FormData.DataJson`）。对照：[05 集成](./05-integration.md)（ERP 业务单据为什么反而用固定真表）。

---

## 一、题眼：字段编译期未知，怎么建表？

普通业务表（如订单）字段是固定的，建表时就定好列。但**动态表单的字段是运行时用户配的**——今天"请假单"5 个字段，明天加 1 个，后天新建个"报销单"10 个字段。你没法在编译期为它们建固定表。三条出路：

| 方案 | 一句话 | 灵活 | 查询 | 复杂度 |
|---|---|---|---|---|
| **JSON 列** | 整张表单数据塞一个 JSON 字段 | 高 | 中 | 低 |
| **EAV** | 每个字段拆成一行 key-value | 高 | 差 | 中 |
| **动态建表** | 每张表单建一张真实表 | 中 | 好 | 高 |

---

## 二、方案一：JSON 列（CP6 的选择）

```sql
-- 一张表搞定所有动态表单的数据
CREATE TABLE Wf_FormData (
    Id        UNIQUEIDENTIFIER PRIMARY KEY,
    FormKey   NVARCHAR(50),
    BizId     UNIQUEIDENTIFIER,
    DataJson  NVARCHAR(MAX)      -- {"leaveType":"sick","days":3,"reason":"..."}
);
```

**优点**：建表零成本（一张表通吃所有表单）、加字段零 DDL（JSON 里多个 key 而已）、读写最直白（序列化/反序列化一个对象）。

**SQL Server 原生 JSON 支持**让它不止"能存"还"能查"：

```sql
-- 按 JSON 内字段查询
SELECT * FROM Wf_FormData
WHERE FormKey='leave' AND JSON_VALUE(DataJson,'$.leaveType') = 'sick';

-- 把 JSON 展开成行列（报表用）
SELECT BizId, JSON_VALUE(DataJson,'$.days') AS Days
FROM Wf_FormData WHERE FormKey='leave';
```

**给 JSON 字段建索引**（解决"查询中等"的短板）——用**计算列 + 索引**：

```sql
ALTER TABLE Wf_FormData
  ADD LeaveType AS JSON_VALUE(DataJson,'$.leaveType');     -- 计算列
CREATE INDEX IX_FormData_LeaveType ON Wf_FormData(LeaveType);  -- 索引它
```

> 这一招很关键：**平时灵活地塞 JSON，高频查询的几个字段拉成计算列建索引**，既要灵活又要性能。CP6 选 JSON 列正是看中这点——SQL Server 把 JSON 查询和索引都补齐了，不用纠结。

**缺点**：跨表单的复杂统计、字段级强约束（外键、唯一）弱；JSON 里类型是弱的（都当字符串，要 `CAST`）。

---

## 三、方案二：EAV（Entity-Attribute-Value）

把每个字段拆成一行：

```
FormDataId | FieldKey   | Value
-----------+------------+--------
 ord-1     | leaveType  | sick
 ord-1     | days       | 3
 ord-1     | reason     | 感冒
```

**优点**：极致灵活、字段元数据化、单字段可加约束。

**致命缺点**：查"病假且天数>3 的单"要把多行**自连接**拼回一行——

```sql
SELECT a.FormDataId FROM EAV a JOIN EAV b ON a.FormDataId=b.FormDataId
WHERE a.FieldKey='leaveType' AND a.Value='sick'
  AND b.FieldKey='days' AND CAST(b.Value AS INT) > 3;
```

字段越多、连接越多，**查询写起来痛、跑起来慢**。一张"表单"散成 N 行，读一条记录要聚合 N 行。EAV 是"灵活性的高利贷"——存的时候爽，查的时候连本带利还。

> 在有原生 JSON 的数据库里，**EAV 基本被 JSON 列取代了**：JSON 列保住了灵活性，又不用自连接地狱。EAV 现在主要存在于不支持 JSON 的老库，或字段需要独立元数据/权限的极端场景。

---

## 四、方案三：动态建表

每张表单建一张真实表（`Form_Leave`、`Form_Expense`…），字段是真列。

**优点**：查询性能最好（就是普通 SQL）、类型/约束/索引齐全。

**缺点**致命：
- **运行时 DDL**：用户加个字段就 `ALTER TABLE`——生产库跑 DDL 风险高、可能锁表。
- **表爆炸**：100 张表单 = 100 张表，加上改版的历史版本，表数失控。
- **迁移噩梦**：表结构跟着用户配置漂移，备份/迁移/权限全乱。

> 动态建表把"灵活性"换成了"运维地狱"。只有在**表单数量少且稳定、查询性能要求极高**（如几张核心业务大表）时才考虑——而那种表本就该当固定业务表设计，不该走"动态表单"这条路。

---

## 五、关键区分：动态表单 vs ERP 业务单据，别混存

这是最容易踩的坑：**不是所有"表单"都该用 JSON 列存**。

| | OA 动态表单（请假/报销） | ERP 业务单据（PR/PO/付款） |
|---|---|---|
| 字段 | 用户配的、会变 | 固定的、编译期已知 |
| 存储 | **JSON 列**（本章） | **固定真表**（普通实体，有外键/索引/约束） |
| 谁管 | OA 全托管 | 各业务模块自己 |

[05 章](./05-integration.md)讲过：ERP 单据有自己的表，OA 只挂审批、不存它们的数据。**别把 PO 塞进 JSON 列**——PO 字段固定、要和供应商/物料做外键、要进财务报表，它就该是结构化真表。JSON 列只用于"字段不定"的 OA 原生表单。**用对地方，JSON 列是利器；用错地方（塞结构化业务数据），就是把强类型扔了。**

---

## 六、何时升级（演进路径）

从 JSON 列起步，按需升级，不要一步到位：

1. **起步**：所有动态表单 → 一张 `Wf_FormData` 的 JSON 列。够 80% 场景。
2. **某字段查询变高频** → 给它加**计算列 + 索引**（第二节那招）。仍是 JSON 列，只是热字段加速。
3. **某张表单变成核心高频报表对象** → 考虑为它建专用真表/视图，从 JSON 投影过去。这时它其实已"毕业"成业务实体了。

> 演进的信号是**查询痛**，不是"提前担心性能"。先 JSON 列跑起来，等真有某个字段被反复重查再加计算列——YAGNI 同样适用于存储设计。

---

## 七、资深视角

**为什么 JSON 列是当代默认答案？** 因为主流库（SQL Server、Postgres jsonb、MySQL）都把 JSON 的存、查、索引补齐了。十年前没原生 JSON，大家被迫用 EAV；现在 JSON 列拿走了 EAV 的灵活性、还回了它欠的查询性能，EAV 自然退场。

**版本化数据怎么存？** [02 章](./02-form-runtime.md)的 `FormDef.Version`——`FormData` 记它用哪版 schema 渲染。JSON 列天然适配：旧单的 JSON 缺新字段、多老字段都无所谓，按它那版 schema 渲染即可，不像真表改了列旧数据要迁移。**JSON 列对"定义会演进"的场景额外友好**。

**类型弱怎么办？** JSON 里数字/日期都可能当字符串读。约定：写入时按 schema 字段类型规整、读出时按类型 `CAST`，把"类型纪律"放在应用层（序列化器）而非数据库。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| JSON 列查询/索引 | **SQL Server JSON / Postgres jsonb + GIN** | `JSON_VALUE`、计算列索引、jsonb 操作符 |
| EAV 的代价 | **Magento EAV（反面教材）** | 为什么"万物 EAV"让查询和维护痛苦 |
| 低代码存储选型 | **JeecgBoot online 表 / 钉钉宜搭** | 动态表单落地时的存储抉择 |

> 宜搭/简道云这类成熟低代码，底层动态表单数据基本都是 JSON 文档 + 热字段索引——和本章结论一致。

---

## 九、自检

- [ ] 动态表单字段编译期未知，三种存法分别是什么？各自最大代价？
- [ ] JSON 列查询"中等"的短板怎么补？（计算列 + 索引）
- [ ] 为什么 JSON 列基本取代了 EAV？
- [ ] 动态建表的三个致命缺点？
- [ ] PO 这种 ERP 单据为什么不该用 JSON 列存？JSON 列只该用在什么数据上？
- [ ] 从 JSON 列升级的信号是什么？（查询痛，不是提前优化）

全部能答 → 你能为任何"字段不定"的数据选对存储。下一步 [09 自研设计器](./09-designers.md)——把前面所有"手写 JSON schema"升级为"拖拽生成 JSON"，且生成的 JSON 与手写同构。

---

*实现：`Wf_FormData.DataJson` JSON 列 + 高频字段计算列索引。配套教学见 [docs/oa/08](../oa/08-data-storage.md)。*
