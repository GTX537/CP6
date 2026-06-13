# 04 · 组织引擎：部门树 / 上下级 / 审批人解析

> 第02章引擎里有个打了桩的方法 `ResolveAssignees`——把"直属上级""HR角色"算成具体的人。它靠的就是组织架构。而 **CP6 现在没有组织架构**，这是 OA 落地的硬前置。本章补齐它。

## 📍 学习目标

1. 审批流里"审批人"有哪几种配法？各自怎么算出具体的人？
2. 部门树怎么存？为什么要存 `Path`？
3. "直属上级"在数据上是怎么找到的？
4. CP6 现有的 `Sys_User` / `M_Staff` 怎么和组织模型对接，而不是推翻重建？
5. 为什么组织是 OA 的"地基"，比流程引擎还靠前？

---

## 🔎 审批人的几种配法（assignee 类型）

第02章流程定义里的 `"assignee": { "type": "leader" }`，type 有这么几种主流配法，**组织引擎要为每一种提供解析能力**：

| type | 含义 | 解析依据 |
|---|---|---|
| `user` | 指定某个人 | 直接是 userId，最简单 |
| `role` | 某个角色（如 HR） | 查角色下所有人 |
| `dept` | 某部门负责人 | 查部门的 leader |
| `leader` | 发起人的直属上级 | 顺着发起人的上级链找一级 |
| `leaderN` | 上 N 级领导 | 上级链找 N 级 |
| `formField` | 表单里选的人 | 读 FormData 里某字段的值 |
| `starter` | 发起人本人（退回常用） | Instance.StarterId |

**`ResolveAssignees` 就是一个按 type 分派的解析器**，把抽象规则翻译成"具体哪几个 userId"。

---

## 🔎 数据模型：部门树 + 员工归属

```csharp
// CP6.Entity/DomainModels/Oa/OrgUnit.cs —— 部门树
[Table("T_Oa_OrgUnit")]
public class OrgUnit : BaseEntity
{
    public string Name      { get; set; } = "";
    public Guid?  ParentId  { get; set; }          // 上级部门，根为 null
    public string Path      { get; set; } = "";    // ★物化路径，如 "/总公司/华东/仓储部/"
    public string? LeaderId { get; set; }          // 部门负责人 userId
    public int    SortOrder { get; set; }
}

// CP6.Entity/DomainModels/Oa/StaffOrg.cs —— 员工归属（桥接现有 Sys_User）
[Table("T_Oa_StaffOrg")]
public class StaffOrg : BaseEntity
{
    public Guid   SysUserId  { get; set; }   // ★关联 CP6 现有 Sys_User，不另起人事
    public Guid   OrgUnitId  { get; set; }   // 所属部门
    public string? PositionCd{ get; set; }   // 职务/职级
    public string? LeaderUserId { get; set; }// ★直属上级（也可不存、靠部门 Leader 推断）
    public bool   IsPrimary  { get; set; }   // 一人多部门时的主部门
}
```

### 为什么存 `Path`（物化路径）？

部门是树。要查"仓储部及其所有下级部门的人"，纯靠 `ParentId` 递归很慢。存一个 `Path = "/总公司/华东/仓储部/"`，一句 `WHERE Path LIKE '/总公司/华东/仓储部/%'` 就把整棵子树捞出来。**用空间换查询效率**，是树形结构的经典手法（缺点：部门移动要批量更新子孙 Path，但部门调整频率极低，划算）。

---

## 🔎 解析器：把规则翻译成人

```csharp
// CP6.Core/Services/Oa/OrgResolver.cs
public async Task<List<string>> ResolveAssignees(AssigneeRule rule, FlowInstance inst)
{
    switch (rule.Type)
    {
        case "user":   return new() { rule.Value };
        case "starter":return new() { inst.StarterId };

        case "role":   // 角色下所有人（复用 CP6 现有 Sys_Role）
            return await _db.SysUsers.Where(u => u.RoleId == int.Parse(rule.Value))
                                     .Select(u => u.Id.ToString()).ToListAsync();

        case "leader": // ★发起人的直属上级
        {
            var so = await _db.StaffOrgs.FirstAsync(x => x.SysUserId.ToString() == inst.StarterId && x.IsPrimary);
            if (so.LeaderUserId != null) return new() { so.LeaderUserId };
            // 兜底：取主部门负责人
            var dept = await _db.OrgUnits.FindAsync(so.OrgUnitId);
            return dept.LeaderId != null ? new() { dept.LeaderId } : new();
        }

        case "dept":   // 指定部门负责人
        {
            var dept = await _db.OrgUnits.FindAsync(Guid.Parse(rule.Value));
            return dept.LeaderId != null ? new() { dept.LeaderId } : new();
        }

        case "formField": // 表单里选的人
        {
            var data = await LoadFormData(inst.BizId);
            return new() { data[rule.Value]?.ToString() ?? "" };
        }
        default: return new();
    }
}
```

**第02章卡住的 `ResolveAssignees` 到这里就通了。** 注意它**复用了 CP6 现有的 `Sys_User`/`Sys_Role`**，组织模型只是给它们补上"部门 + 上级"两层关系，不另起一套人事系统。

---

## 💡 资深视角

**为什么组织是地基、比流程引擎还靠前？**
流程引擎能跑通的前提是"算得出审批人"。没有组织，`leader`/`dept`/`role` 全部失效，你只能硬编码具体 userId——那不叫审批流。所以严格说**阶段1 跑通用硬编码，但要做"像样的 OA"，组织必须在阶段2 之前补上**。这也是 CP6 现状最大的缺口（`Sys_User` 连部门字段都没有）。

**直属上级：存字段 vs 靠部门推断？**
两种流派。存 `LeaderUserId` 精确但要维护；靠"部门负责人"推断省维护但表达不了"同部门多层级"。生产常**两者结合**：优先读显式上级，没有则回退部门负责人（上面代码就是这逻辑）。

**为什么不直接用 CP6 的 RBAC（Sys_Role）当组织？**
角色（能干什么）和组织（在哪个部门、归谁管）是两个正交维度。角色解决"权限"，组织解决"汇报关系和审批路由"。一个人可以是"采购经理(角色) + 隶属华东采购部(组织)"。混用会导致"按角色审批"和"按部门审批"纠缠不清。**复用 Sys_Role 做 `role` 类型审批人，但部门关系独立建模。**

---

## ⚠️ 踩坑记录

1. **纯递归查子树**：部门层级深时 N+1 查询爆炸。用 `Path` 物化路径一句 LIKE 搞定。
2. **审批人解析返回空导致卡单**：员工没配上级、部门没配负责人，单子卡死。解析器要有兜底（转上级部门/转管理员/明确报错），不能静默。
3. **一人多部门没定主部门**：`leader` 该找哪个部门的上级？必须有 `IsPrimary` 主部门。
4. **部门移动忘了刷 Path**：调整组织后子孙 Path 没更新，子树查询出错。移动部门要级联更新 Path（封装成一个方法）。
5. **离职/停用没处理**：审批人离职了单子还派给他。解析时过滤 `Sys_User.Enable=false`，并提供改派。

---

## 🧪 自检题

1. 审批人 type 列出至少 5 种，并说明 `leader` 怎么算。
2. 部门树为什么要存 Path？它优化了什么查询、代价是什么？
3. 直属上级"存字段"和"靠部门推断"各自利弊？怎么结合？
4. 角色(Sys_Role)和组织(OrgUnit)为什么不能混为一谈？
5. 审批人解析为空时，引擎应该怎么兜底？

---

## 🔗 延伸阅读 / 动手清单

**动手清单：**
- [ ] 建表 `T_Oa_OrgUnit` / `T_Oa_StaffOrg`，部门树带 Path
- [ ] 给现有 `Sys_User` 通过 StaffOrg 关联部门 + 上级（不改 Sys_User 结构也可）
- [ ] 写 `OrgResolver.ResolveAssignees`，支持 user/role/leader/dept/starter/formField
- [ ] 回到第02章把 `FlowEngine` 里硬编码的审批人换成真解析
- [ ] 部门维护页（树形增删改 + 拖拽调整 + Path 级联更新）

**下一章** → [05. 规则引擎：显隐 / 计算 / 联动 / 条件分支](./05-rule-engine.md)，解决表单字段联动和流程条件求值。
