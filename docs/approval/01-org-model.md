# 01 · 组织模型：部门树 / 上级 / 审批人解析

> **阶段 0 · 从这里入门。** 这是审批引擎的地基，也是 [PUB 数据权限](../pub/README.md) 的共同前置。本章浇出三样东西：`Sys_Dept` 部门树、给 `Sys_User` 补的"部门/上级"关系、以及把"直属上级/部门负责人/角色/指定人"算成具体人的 `ApproverResolver`。本章结束时，"谁来批"在运行时算得出来，缺位也有兜底。
>
> 上游：[总纲](./README.md) 的题眼（审批不侵入业务、依赖单向）。下游：[03 流程引擎](./03-flow-runtime.md) 建 `FlowTask` 时调用本章的解析器。配套可执行计划：[阶段0 实施计划](../superpowers/plans/2026-06-10-approval-stage0-org-model.md)。

---

## 一、为什么组织是地基，比流程引擎还靠前

流程引擎能跑通的前提是"**算得出审批人**"。审批流里写的是抽象规则——"找发起人的直属上级""走部门负责人""交给财务角色"——这些规则要翻译成"具体哪几个 userId"，靠的全是组织关系。

CP6 现状最大的缺口正在这里：`Sys_User` 只有 `UserName/Password/NickName/RoleId/Enable` 五个字段，**连部门、上级都没有**。所以 `leader`/`dept` 类审批人一个都算不出来，你只能在流程里硬编码具体的人——那不叫审批流，叫写死的转发。

> **结论**：阶段1 的流程引擎即使能跑，也只能跑"指定人"这一种最弱的审批；要做"像样的 OA"，组织必须在流程引擎之前补上。这就是它叫**阶段 0** 的原因。

---

## 二、审批人的几种配法（assignee 解析 type）

流程节点 schema 里写的 `"approver": { "type": "directManager", "levels": 1 }`，type 有这么几种主流配法，**组织模型要为每一种提供解析能力**：

| 解析策略（枚举） | 含义 | 解析依据 |
|---|---|---|
| `DirectManager` | 发起人的直属上级（可逐级 N 级） | 顺 `Sys_User.ManagerId` 上溯 |
| `DeptLeader` | 部门负责人/部门长 | 查 `Sys_Dept.LeaderId` |
| `Role` | 某角色/岗位（如财务经理） | 复用 `Sys_User.RoleId` 查角色下的人 |
| `SpecifiedUser` | 指定某人 / 表单字段选的人 | 直接给 userId |

> 总纲第一问敲定的 group会签/并签/串签/多层，是**流程结构**（多个节点、节点内多人），不在本章；本章只管"单条规则 → 一组审批人"。会签的"一组人"也是靠 `Role`/自定义解析出来的。

`ApproverResolver` 就是一个**按 type 分派的解析器**，把抽象规则翻译成具体 userId 列表，或在算不出时给出"缺位原因"。

---

## 三、数据模型：`Sys_Dept` 部门树 + `Sys_User` 三字段

```csharp
// CP6.Entity/DomainModels/Sys/Sys_Dept.cs —— 部门树
[Table("Sys_Dept")]
public class Sys_Dept : BaseEntity
{
    public string  Code     { get; set; } = "";   // 部门编码（物化路径用，如 HQ/SALES）
    public string  Name     { get; set; } = "";
    public Guid?   ParentId { get; set; }          // 上级部门，根为 null
    public string  Path     { get; set; } = "";   // ★物化路径，如 /HQ/SALES/
    public Guid?   LeaderId { get; set; }          // 部门负责人 → Sys_User.Id
    public int     Sort     { get; set; }
    public bool    Enable   { get; set; } = true;
}

// CP6.Entity/DomainModels/Sys/Sys_User.cs —— 现有表补三字段
public Guid?   DeptId    { get; set; }   // 所属部门 → Sys_Dept.Id
public Guid?   ManagerId { get; set; }   // ★直属上级 → Sys_User.Id
public string? Email     { get; set; }   // 通知用
```

### 3.1 为什么直接给 `Sys_User` 加字段，而不另起一张"员工组织"桥表？

低代码 OA 教材（[docs/oa/04](../oa/04-org-engine.md)）里设想的是 `OrgUnit + StaffOrg` 两张表，用桥表表达"一人多部门"。**我们阶段0 故意简化成"部门字段直接挂 `Sys_User`"**，原因：

- CP6 当前是**一人一主部门**的场景，没有矩阵式多部门归属的需求。一张桥表是为"一人多部门"准备的，YAGNI。
- `Sys_User` 已是登录主体，部门/上级直接挂上去，查询少一次 join，解析器更直白。
- 真要支持一人多部门，再加 `Sys_UserDept` 桥表即可，不影响现有字段——演进开放。

> 这与 PUB 的多角色不同：角色用了中间表 `Sys_UserRole`（一人多角色是真实需求），部门则单挂字段（一人一主部门够用）。**按真实基数决定要不要中间表**，不是一刀切。

### 3.2 为什么存 `Path`（物化路径）？为什么用 `Code` 拼而不是 `Id`？

部门是树。要查"销售部及其所有下级部门"，纯靠 `ParentId` 递归很慢。存 `Path = "/HQ/SALES/"`，一句 `WHERE Path LIKE '/HQ/SALES/%'` 就把整棵子树捞出来——**用空间换查询效率**，树形结构的经典手法。代价是部门移动要批量更新子孙 `Path`，但部门调整频率极低，划算。

路径用**部门编码 `Code`** 拼（`/HQ/SALES/`）而不是 Guid `Id`，有两个实在好处：
1. **可读**：`/HQ/SALES/` 一眼看懂层级，`/a3f.../9b2.../` 不行。
2. **建树时不卡 Id 生成时机**：`BaseEntity.Id` 标了 `DatabaseGeneratedOption.Identity`，SQL Server 下由 DB 生成、客户端赋值会被忽略；若用 Id 拼 Path 就得先 SaveChanges 拿到 Id 再回填，多一趟。用 `Code` 则建单时即可拼好。

---

## 四、审批人解析器 `ApproverResolver`（四策略 + 缺位兜底）

解析器返回的不是"一定有人"，而是**"要么有审批人，要么给出缺位原因"**，供上层挂起人工指派——这是它最重要的设计：

```csharp
// CP6.Core/Services/Wf/IApproverResolver.cs
public class ApproverResolveResult
{
    public List<Guid> ApproverIds { get; set; } = new();
    public bool   Resolved => ApproverIds.Count > 0;
    public string? UnresolvedReason { get; set; }   // 缺位原因：挂起 + 管理员指派
}
```

> **为什么不抛异常？** "算不出审批人"是**业务正常分支**（新员工没设上级、部门没配负责人），不是程序错误。抛异常会让整条流程崩在半路、还得 catch；返回 `Unresolved` 则让流程引擎从容地把单子挂到"待管理员指派"——可控、可观测。总纲的异常原则"审批人解析为空→挂起+管理员指派"就落在这。

### 4.1 直属上级：逐级上溯，链短于 N 取链顶

```csharp
private async Task<ApproverResolveResult> ResolveDirectManagerAsync(ApproverRule rule, ApproverResolveContext ctx)
{
    var levels = rule.Levels < 1 ? 1 : rule.Levels;
    var current = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
    if (current == null) return Unresolved("发起人不存在");

    Sys_User? manager = null;
    for (int i = 0; i < levels; i++)
    {
        if (current.ManagerId == null) break;                 // 链断了
        var next = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == current.ManagerId && u.Enable);
        if (next == null) break;
        manager = next; current = next;
    }
    return manager == null
        ? Unresolved("发起人无直属上级，需人工指派")             // 一级都没有 → 缺位
        : Resolved(manager.Id);                               // 想 N 级但链短 → 取可达链顶
}
```

**两个边界的取舍**：发起人**一级上级都没有** → 缺位挂起（不能凭空造人）；**想要 3 级但链只有 2 级** → 取链顶那个人（不缺位）。前者是"真的没人"，后者是"已经到顶了"，区别对待才符合直觉。

### 4.2 部门负责人：沿部门树向上找第一个有效负责人

```csharp
var dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == deptId && d.Enable);
while (dept != null)
{
    if (dept.LeaderId != null)
    {
        var leader = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == dept.LeaderId && u.Enable);
        if (leader != null) return Resolved(leader.Id);
    }
    if (dept.ParentId == null) break;
    dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == dept.ParentId && d.Enable);
}
return Unresolved("沿部门树未找到有效的部门负责人，需人工指派");
```

**兜底逻辑**：子部门（如"销售一组"）常没单独配负责人，那就**沿父链向上**找——找到"销售部"的负责人就用它。一路到根都没有才缺位。这让"小组级单据自动归大部门长审"成立，不用每个末级部门都配人。

### 4.3 角色 / 指定人

```csharp
// 角色：该角色下所有启用用户（复用现有 Sys_Role / Sys_User.RoleId）
var ids = await _db.Sys_Users.Where(u => u.RoleId == rule.RoleId && u.Enable)
                             .Select(u => u.Id).ToListAsync();
return ids.Count > 0 ? Resolved(ids.ToArray()) : Unresolved($"角色 {rule.RoleId} 下无启用用户");

// 指定人：流程固定指定 或 发起人/表单字段选的人
return rule.UserId == null ? Unresolved("未指定审批人") : Resolved(rule.UserId.Value);
```

> **停用用户必须排除**（`u.Enable`）。否则离职的人还会被解析成审批人，单子卡死在死人手里——这是审批系统最常见的线上事故之一。

---

## 五、与现有 RBAC 的关系：角色 ≠ 组织，正交

为什么不直接用 CP6 的 `Sys_Role` 当组织？因为**角色（能干什么）和组织（在哪个部门、归谁管）是两个正交维度**：

- **角色**解决权限——"采购经理能审批 PO"。
- **组织**解决汇报关系与审批路由——"隶属华东采购部、上级是华东大区总"。

一个人可以是"采购经理(角色) + 华东采购部(组织)"。混用会让"按角色审批"和"按部门审批"纠缠不清。所以：**`Role` 类型审批人复用 `Sys_Role`，部门关系独立用 `Sys_Dept` 建模**，两条线各管各的。

---

## 六、与 PUB 数据权限的协同：同一棵 `Sys_Dept`

这棵部门树不是审批专用。[PUB 的数据行权限 DataScope](../pub/README.md) 的"本部门及下级"也靠它——`WHERE 记录部门.Path LIKE 用户部门.Path + '%'`，和审批的"部门负责人沿树兜底"用的是同一个 `Path`。

> **一次建、两处用**：`Sys_Dept` 是 OA 审批路由与 PUB 数据权限的**共同前置**。所以阶段0 的组织模型独立先落，OA 流程引擎和 PUB 权限引擎都直接复用，不重复建表。这也是总纲把它单列为阶段0、不塞进任一引擎内部的原因。

---

## 七、资深视角

**直属上级：存字段 vs 靠部门推断？** 两种流派。存 `ManagerId` 精确但要维护；靠"部门负责人"推断省维护但表达不了"同部门多层级汇报"。生产常两者结合——本章正是：`DirectManager` 读显式 `ManagerId`，`DeptLeader` 沿部门树推断，流程按需选其一。

**Path 的代价你认不认？** 部门移动要刷子孙 Path。如果你的组织半年不动一次，认；如果天天重组（少见），考虑闭包表（closure table）。对纸箱厂这种稳定组织，物化路径是最优解。

**多组织/多公司怎么办？** 现在单组织。将来要多法人，给 `Sys_Dept` 加 `CompanyId` 根、或让 `Path` 带公司前缀即可，解析器加一层公司过滤——结构开放，不返工。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 部门树 + 数据权限 | **RuoYi / VOL.Core** | `dept` 物化/祖级表、`dataScope` 按部门过滤 |
| 直属上级链 | **钉钉/企业微信 组织 API** | `manager` 字段、逐级上报 |
| 审批人解析抽象 | **Flowable IdentityService / SAP 代理** | 把"角色/部门/上级"抽象成统一解析 |

> RuoYi 的"数据权限：全部/本部门/本部门及以下/仅本人/自定义"就是 PUB 的 DataScope，依赖的也是这棵部门树——核心模型全世界一致。

---

## 九、阶段0 自检

- [ ] `DirectManager` 想要 3 级但链只有 2 级，返回谁？发起人没有任何上级呢？（链顶 / 缺位挂起）
- [ ] `DeptLeader` 在末级部门没配负责人时怎么兜底？（沿 `Path` 父链向上找）
- [ ] 为什么解析器返回 `Unresolved` 而不是抛异常？
- [ ] 停用用户为什么必须从角色/上级解析里排除？
- [ ] `Sys_Dept` 为什么由 OA 与 PUB 共用、且单列为阶段0？
- [ ] `Path` 为什么用 `Code` 拼而不是 `Id`？

全部能答 → 审批人"找直属上级/部门负责人/角色/指定人"算得出来、缺位有兜底，阶段0 闭合，流程引擎（[03 章](./03-flow-runtime.md)）可以放心调用它建 `FlowTask`。

---

*配套可执行计划见 [阶段0 实施计划](../superpowers/plans/2026-06-10-approval-stage0-org-model.md)（7 任务 TDD：实体→字段→DeptService→ApproverResolver→Controller/DI→迁移→前端）。实现落 `CP6.Entity/DomainModels/Sys`、`CP6.Core/Services/{Sys,Wf}`、`cp6.web/src/views/sys`。*
