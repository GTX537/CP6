# 10 · 多租户与商业化

> **收口章。** 前九章把审批/OA 平台在**单租户**下做完整了。本章讲最后一步：怎么把它变成**多租户可售产品**——多个企业（租户）共用一套部署，数据和配置互相隔离。这也是 CP6 从"自用系统"走向"SaaS 产品"的关键一跃。
>
> 上游：全书。横切：[PUB 数据权限](../pub/README.md)（租户**内**的部门隔离，与本章租户**间**隔离叠加）。背景：[01 组织模型](./01-org-model.md)阶段0 刻意没加 `TenantId`——本章统一补。

---

## 一、题眼：多租户 = 数据隔离 + 配置隔离

> **多租户 = 同一套部署，多个企业各用各的，彼此看不到对方的数据，也各配各的表单/流程。前者是数据隔离，后者是配置隔离。schema 驱动的 OA 天生适合多租户——每个租户存自己的 form/flow JSON 即可。**

审批平台做多租户有个天然优势：表单和流程本来就是数据（`SchemaJson`），让它们带上 `TenantId`，每个租户配自己的表单流程互不干扰——**配置隔离几乎免费**。难点在数据隔离：怎么保证 A 公司的审批单 B 公司绝对查不到。

---

## 二、现状与三种隔离方案

CP6 现在**全库无 `TenantId`**（`grep` 为 0），前面阶段0 也刻意没加——多租户作为独立章节统一上，避免污染每一步。隔离三条路：

| 方案 | 隔离强度 | 成本 | 适用 |
|---|---|---|---|
| **独立数据库**（每租户一个库） | 最强（物理隔离） | 高（库数爆炸、运维重） | 强合规/大客户 |
| **独立 Schema**（一库多 schema） | 中 | 中 | 中等数量租户 |
| **共享表 + `TenantId`**（行级隔离） | 逻辑隔离 | 低（一套表） | **SaaS 默认，CP6 选这个** |

**CP6 选共享表 + 行级隔离**：所有表加 `TenantId` 列，查询自动按当前租户过滤。一套表、一次部署、按行隔离——SaaS 最常见、最经济。代价是隔离靠代码保证（不是物理墙），所以**过滤绝不能漏**（见第四节）。

---

## 三、给实体加 `TenantId`

行级隔离的第一步：每张要隔离的表加 `TenantId`。最干净的做法是抽一个带租户的基类：

```csharp
// CP6.Entity/BaseTenantEntity.cs（新增，介于 BaseEntity 与业务实体之间）
public abstract class BaseTenantEntity : BaseEntity
{
    public Guid TenantId { get; set; }     // ★所属租户
}

// 需隔离的实体改继承它（表单/流程/组织/权限…）
public class FlowInstance : BaseTenantEntity { /* … */ }
public class Sys_Dept    : BaseTenantEntity { /* … */ }
```

> 注意区分：**业务/配置数据要隔离**（FlowInstance、FormDef、Sys_Dept、用户、角色…）；**纯系统字典/语言包**这类全租户共享的可以不带 `TenantId`。哪些隔离、哪些共享，是一次明确的盘点。

---

## 四、全局查询过滤：一处加，全表生效（防漏的关键）

行级隔离最大的风险是**某个查询忘了加 `WHERE TenantId=?`，数据就串了**。靠人肉在每个查询加过滤必漏。EF Core 的**全局查询过滤器**一处声明、所有查询自动注入：

```csharp
// CP6Context.OnModelCreating —— 对所有租户实体统一加过滤
protected override void OnModelCreating(ModelBuilder mb)
{
    mb.Entity<FlowInstance>().HasQueryFilter(e => e.TenantId == _tenant.CurrentTenantId);
    mb.Entity<Sys_Dept>().HasQueryFilter(e => e.TenantId == _tenant.CurrentTenantId);
    // …每个租户实体一行；或用反射对所有 BaseTenantEntity 自动批量注册
}
```

声明之后，`_db.FlowInstances.Where(...)` 会自动变成 `WHERE TenantId=@current AND ...`——**开发者写查询时不用、也不会忘记加租户条件**。写入时也要自动盖 `TenantId`（在 `SaveChanges` 拦截，给新增的租户实体盖上当前租户）。

> 这一招把"防漏"从"每个人每次都记得"变成"框架强制"。多租户安全的命门就在这一处统一过滤——**宁可在框架层焊死，不靠自觉**。

---

## 五、租户上下文：当前请求属于哪个租户

`HasQueryFilter` 里的 `_tenant.CurrentTenantId` 从哪来？从**登录态**。用户登录后，JWT 里带 `TenantId`，每个请求解析出来放进一个请求级的"租户上下文"：

```csharp
// 中间件：从 JWT 解析 TenantId → 存入 scoped 的 ITenantContext
public class TenantMiddleware
{
    public async Task Invoke(HttpContext ctx, ITenantContext tenant)
    {
        var tid = ctx.User.FindFirst("tenant_id")?.Value;
        if (tid != null) tenant.CurrentTenantId = Guid.Parse(tid);
        await _next(ctx);
    }
}
```

> 一个用户属于哪个租户，在他登录时就定了。整条请求链路（查询过滤、写入盖章）都用这个上下文——**租户身份跟着请求走，不靠参数层层传**。

---

## 六、两层隔离叠加：TenantId × DataScope

CP6 有**两层正交的数据隔离**，别混淆：

| 层 | 隔离什么 | 靠什么 | 章节 |
|---|---|---|---|
| **租户间** | A 公司 vs B 公司 | `TenantId` 全局过滤 | 本章 |
| **租户内** | 同公司 销售部 vs 采购部 | [PUB DataScope](../pub/README.md) + `Sys_Dept` | PUB |

两者叠加：先按 `TenantId` 锁定"这家公司"，再按 `DataScope` 锁定"这家公司里我能看的部门"。**租户隔离是硬墙（跨租户绝对不可见），数据权限是软墙（同租户内按部门/角色收放）。** 一硬一软，分工清楚。

---

## 七、配置隔离与模板市场（商业化）

schema 带上 `TenantId` 后，配置隔离自然成立——每个租户有自己的 `FormDef`/`FlowDef`。在此之上做商业化：

- **预置模板**：平台维护一批通用表单/流程模板（请假、报销、采购审批）。新租户开通时**复制**一份到自己名下（带上自己的 `TenantId`），再自行改——而不是从零配。
- **模板市场**：把优质模板做成可一键安装的市场，是 SaaS 的增值点。
- **按租户计费**：用户数、流程实例数、存储量等按 `TenantId` 计量，是 SaaS 的收费基础。

> 这就是为什么"schema 驱动"对商业化是降维优势：表单/流程是数据，**复制、隔离、计量都是数据操作**，不用为每个客户改代码、发版本。

---

## 八、从单租户迁移上来

CP6 是先单租户做完、再上多租户，迁移路径要平滑：

1. 加 `TenantId` 列（可空起步）。
2. 建一个"默认租户"，把存量数据全部回填成默认租户 `TenantId`。
3. `TenantId` 改非空 + 建索引（`TenantId` 几乎进每个查询，必须索引）。
4. 开启全局查询过滤 + 写入盖章。
5. 单租户期就是"只有一个默认租户"的多租户——平滑过渡，存量数据无损。

> 因为前面阶段0~4 没到处乱加 `TenantId`，这次集中迁移反而干净：一个基类、一处过滤、一次回填。**该延后的延后，到点集中做**——这正是总纲把多租户放最后的原因。

---

## 九、资深视角

**为什么不一开始就加 `TenantId`？** 因为它会污染每一张表、每一个查询、每一个测试。在没确定商业化形态前过早上多租户，是给所有代码背一个税。CP6 的选择是**先把功能做对（单租户），多租户作为一次集中改造**——前面所有章节因此更轻。

**行级隔离安全吗？** 安全的前提是"过滤绝不漏"。`HasQueryFilter` 焊死了读，`SaveChanges` 拦截焊死了写，再加上"跨租户操作"的显式审计——逻辑隔离能做到生产级。真有客户要物理隔离（强合规），再为大客户单独上独立库，混合模式。

**`TenantId` 用 Guid 还是租户编码？** 用 Guid（不可猜、不可枚举），别用自增/短码——否则有人改个 id 就可能探测其它租户。安全的 id 本身就是隔离的一部分。

---

## 十、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 行级多租户 | **EF Core Global Query Filters / Finbuckle.MultiTenant** | 自动过滤、租户解析中间件 |
| 隔离方案选型 | **SaaS 多租户成熟度模型（微软）** | 独立库/独立 schema/共享表的权衡 |
| 配置即产品 | **钉钉宜搭 / Salesforce 模板市场** | schema 模板复制、按租户计量计费 |

> Finbuckle.MultiTenant 把"租户解析 + 查询过滤 + 数据隔离"封成一套——和本章思路一致，自研时可直接对照它的实现。

---

## 十一、全书收尾自检

- [ ] 多租户的两件事是什么？（数据隔离 + 配置隔离）为什么 schema 驱动对配置隔离几乎免费？
- [ ] 三种隔离方案怎么选？CP6 为什么选共享表 + 行级？
- [ ] 行级隔离防漏的命门在哪？（全局查询过滤一处焊死，不靠自觉）
- [ ] `TenantId`（租户间）和 DataScope（租户内）怎么叠加？一硬一软指什么？
- [ ] 为什么把多租户放全书最后、而不是一开始就加 `TenantId`？
- [ ] 从单租户迁移上来的步骤？

全部能答 → **审批/OA 平台整本闭合**：从组织地基、表单流程引擎、复杂审批、与采购/财务集成、自研设计器，到多租户商业化——CP6 有了自己的审批中台与低代码 OA 平台，并具备 SaaS 化的底座。

---

## 全书地图（11 文件）

| 文件 | 主题 | 阶段 |
|---|---|---|
| [README](./README.md) | 总纲：心智模型 + 模块边界 + 数据模型 + 路线 | — |
| [01](./01-org-model.md) | 组织模型（部门树/上级/审批人解析） | 0 |
| [02](./02-form-runtime.md) | 表单引擎运行时 | 1 |
| [03](./03-flow-runtime.md) | 流程引擎运行时（状态机/会签） | 1 |
| [04](./04-form-flow-binding.md) | 表单×流程绑定（字段权限/待办中心） | 1 |
| [05](./05-integration.md) | 集成：同步回调接采购/财务 ★MVP | 2 |
| [06](./06-rule-engine.md) | 规则引擎（联动/计算） | 3 |
| [07](./07-advanced-flow.md) | 高级流程（退回/加签/超时/委派） | 3 |
| [08](./08-data-storage.md) | 数据存储（JSON 列 vs EAV） | — |
| [09](./09-designers.md) | 自研设计器（表单+流程） | 4 |
| [10](./10-multi-tenant.md) | 多租户与商业化（本章） | 4 |

---

*实现：`BaseTenantEntity` + `CP6Context` 全局查询过滤 + `TenantMiddleware`。配套教学见 [docs/oa/10](../oa/10-multi-tenant.md)。*
