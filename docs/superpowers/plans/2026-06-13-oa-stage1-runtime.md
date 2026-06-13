# OA 阶段1 · 可用 OA 运行时（章01消费+02表单+03流程+04绑定+08存储）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**OA 第一份计划**（共三份）。依赖 **PUB B0 组织模型计划已落地**（`Sys_Dept` 树 + `Sys_User.DeptId/ManagerId`）——OA 阶段0=消费 PUB，不自建组织模型。

**Goal:** 落地 OA "运行时优先"的阶段1——一个**手配 JSON 的可用 OA**：审批人解析器（消费 PUB 组织模型，4 策略 + 缺位兜底）+ 表单引擎运行时（schema 驱动动态渲染）+ **流程引擎运行时（状态机 + 会签三规则 + 条件流转，★最硬核）** + 表单×流程绑定（节点字段权限 + 待办中心 + 我的申请 + 审批痕迹）。完成后一张单从提交、待办、审批到结束全程走通（难看但能用）。

**Architecture:** 引擎落 `Wf` 命名空间（`CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`cp6.web/src/views/wf`），与 Erp/Mes/Wms/Sys 平级。核心是两台**解释器** + 一台**状态机**：表单解释器读 `Wf_FormDef.SchemaJson` 动态渲染；流程引擎是状态机——`FlowInstance.CurrentNode` 是状态，一次 tick = `(当前节点, 动作) → 下一节点 + 副作用(建 FlowTask/记 FlowHistory)`，全状态落库、幂等可重放；审批人解析委托 `IApproverResolver`（消费 PUB `Sys_Dept`/`Sys_User`）。审批数据用 **SQL Server JSON 列**（`SchemaJson`/`DataJson`/`VarsJson`），不用 EAV（08 章决策）。OA **不碰业务表**（阶段2 才接业务，走同步回调）。

**Tech Stack:** .NET 8 + EF Core 8（JSON 列）+ SignalR（待办实时推送，复用现有 Hub）/ xUnit + EF Core InMemory / Vue 3.5 + element-plus + Pinia。源文档：`docs/approval/01·02·03·04·08`（引用 PUB 章00）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 文档原意 | 现状/对账 | **本稿建议值** |
|---|---|---|---|---|
| **OA-D1** | **组织模型来源** | OA 章01 自带 `Sys_Dept`(Code/Name, Code 路径) | **归 PUB**（B0 计划已建 `Sys_Dept` DeptCode/DeptName + Id 路径 + `Sys_User.DeptId/ManagerId`） | **消费 PUB B0，不重建**。`IApproverResolver` 读 PUB 的 `Sys_Dept`/`Sys_User`。OA 章01 的 Code 路径以 PUB B0 的 Id 路径为准（解析器走 `ParentId`/`ManagerId` 链，不依赖路径编码） |
| **OA-D2** | **ApproverRule.RoleId 类型** | OA 章01/PUB 章00 §8 写 `Guid? RoleId` | 实际 `Sys_Role.RoleId`/`Sys_User.RoleId` 是 **int**（同 PUB B1-D1） | **int**（`ApproverRule.RoleId` int?，Role 策略查 `Sys_User.RoleId == rule.RoleId`） |
| **OA-D3** | **TenantId / 审计** | Wf 全表 TenantId | 零多租户（同 PUB/Space） | 本阶段不引入 TenantId（章节内索引去前缀，OA 阶段4 章10 多租户统一）；Wf 表继承 `BaseEntity`（GUID Id + Creator/CreateDate/Modifier/ModifyDate） |
| **OA-D4** | **审批数据存储** | 08 章 JSON 列 vs EAV | SQL Server 支持 JSON | **JSON 列**（`SchemaJson`/`DataJson`/`VarsJson` 直接 NVARCHAR(MAX)），08 章决策；查询用 `JSON_VALUE`/`OPENJSON` 按需 |
| **OA-D5** | **PUB 字段权限叠加** | 04 章 节点字段权限 ∩ PUB 角色字段权限（取更严） | PUB B1 字段权限是后续 | **阶段1 只做节点字段权限**；与 PUB B1 的角色字段权限取交集（更严的赢）**留到 PUB B1 落地后接**（本计划节点权限自洽，不阻塞） |
| **OA-D6** | **condition 表达式求值** | 03 章 "安全小表达式求值器，不 eval 任意代码" | 无现成 | 实现**白名单字段 + 比较/逻辑运算**的小求值器（如 `days > 3 && type == 'annual'`），**禁止任意代码 eval**（防 schema 注入）。可用轻量库（如 DynamicExpresso）或手写递归下降 |

> **测试基建**：xUnit + InMemory。流程引擎状态机/会签三规则/条件求值/幂等可纯单测（核心价值）；JSON 列查询真实翻译需 `[需真库]` 兜底。

---

## File Structure

### 组织消费 + 审批人解析（章01）
- `CP6.Core/Services/Wf/IApproverResolver.cs`（`ApproverRule`/`ApproverResolveResult`/`ApproverResolveContext` + 策略枚举）
- `CP6.Core/Services/Wf/ApproverResolver.cs`（DirectManager/DeptLeader/Role/Specified + 缺位兜底，消费 PUB `Sys_Dept`/`Sys_User`）

### 表单引擎（章02）
- `CP6.Entity/DomainModels/Wf/{FormDef,FormData}.cs`（Wf_ 前缀，JSON 列）
- `CP6.Core/Services/Wf/IFormService.cs`/`FormService.cs`（CRUD def + 提交 data + 后端 schema 校验）
- `cp6.web/src/views/wf/DynamicForm.vue`（schema 驱动渲染器 + buildRules 校验）

### 流程引擎（章03 ★）
- `CP6.Entity/DomainModels/Wf/{FlowDef,FlowInstance,FlowTask,FlowHistory}.cs`
- `CP6.Core/Services/Wf/FlowEngine.cs`（Submit/Act/EnterNode/EvaluateNode/NextNode）+ `FlowSchema.cs`（schema DTO 反序列化）+ `ConditionEvaluator.cs`（安全求值器）+ `IFlowEngine.cs`

### 绑定 + 待办（章04）
- `CP6.Core/Services/Wf/TaskCenterService.cs`（MyTodos/MyApplications/撤回）
- 节点字段权限 mask 合成（前端 buildFieldMask）+ SignalR 推送（复用现有 Hub）
- `cp6.web/src/views/wf/{TodoCenter,MyApplications,FlowTrace}.vue` + `DynamicForm` 接 mask

### 控制器 + DI + 迁移 + 测试
- `CP6.WebApi/Controllers/Wf/{FormController,FlowController,TaskController}.cs`
- 迁移 `*_OaStage1`；DI 注册 Wf 服务
- 测试：`ApproverResolverTests`（★4 策略+缺位）、`FlowEngineTests`（★状态机+会签三规则+条件+幂等）、`FormServiceTests`、`TaskCenterServiceTests`

---

## 实施分四阶段（对应章01/02/03/04）

- **Phase A**（A-1）：审批人解析器（章01，消费 PUB）— 流程引擎的前置
- **Phase B**（B-1..B-2）：表单引擎运行时（章02 + 08 JSON 存储）
- **Phase C**（C-1..C-4）：流程引擎状态机（章03 ★最硬核）
- **Phase D**（D-1..D-3）：绑定 + 待办中心 + 我的申请 + 痕迹（章04）→ 可用 OA 闭合

---

# Phase A — 审批人解析器（章01，消费 PUB 组织模型）

## Task A-1: IApproverResolver + ApproverResolver（4 策略 + 缺位兜底）★

**Files:** Create `CP6.Core/Services/Wf/IApproverResolver.cs`, `ApproverResolver.cs`; Test `CP6.Tests/ApproverResolverTests.cs`

> 依赖 PUB B0 的 `Sys_Dept`(ParentId/LeaderId) + `Sys_User`(DeptId/ManagerId/RoleId/Enable)。**纯查询、不抛异常**，缺位返回原因（OA-D1）。

- [ ] **Step 1: 失败测试（★核心，4 策略 + 边界）** `[InMemory]`

```csharp
public class ApproverResolverTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task DirectManager_ChainShorterThanN_ReturnsTop()
    {
        using var db = Db();
        var top = Guid.NewGuid(); var mid = Guid.NewGuid(); var low = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User{Id=top, UserName="top", Password="x", Enable=true},
            new Sys_User{Id=mid, UserName="mid", Password="x", ManagerId=top, Enable=true},
            new Sys_User{Id=low, UserName="low", Password="x", ManagerId=mid, Enable=true});
        await db.SaveChangesAsync();
        var r = new ApproverResolver(db);
        // 想上溯 5 级，但链只有 2 级 → 取链顶 top
        var res = await r.ResolveAsync(new ApproverRule(ApproverStrategy.DirectManager, 5, null, null),
            new ApproverResolveContext { StarterUserId = low });
        Assert.True(res.Resolved); Assert.Equal(top, res.ApproverIds.Single());
    }

    [Fact]
    public async Task DirectManager_NoManager_Unresolved()
    {
        using var db = Db();
        var u = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User{Id=u, UserName="u", Password="x", Enable=true});
        await db.SaveChangesAsync();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DirectManager,1,null,null), new(){StarterUserId=u});
        Assert.False(res.Resolved); Assert.NotNull(res.UnresolvedReason);
    }

    [Fact]
    public async Task DeptLeader_WalksUpToFirstLeader()
    {
        using var db = Db();
        var leader = Guid.NewGuid(); var parent = Guid.NewGuid(); var child = Guid.NewGuid(); var u = Guid.NewGuid();
        db.Sys_Depts.AddRange(
            new Sys_Dept{Id=parent, DeptCode="P", DeptName="P", LeaderId=leader, Enable=true, Path=$"/{parent}/"},
            new Sys_Dept{Id=child, DeptCode="C", DeptName="C", ParentId=parent, Enable=true, Path=$"/{parent}/{child}/"}); // 子部门无 leader
        db.Sys_Users.AddRange(new Sys_User{Id=leader,UserName="L",Password="x",Enable=true},
            new Sys_User{Id=u,UserName="u",Password="x",DeptId=child,Enable=true});
        await db.SaveChangesAsync();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DeptLeader,null,null,null), new(){StarterUserId=u});
        Assert.Equal(leader, res.ApproverIds.Single());   // 沿父链找到 P 的负责人
    }

    [Fact]
    public async Task Role_ExcludesDisabledUsers()
    {
        using var db = Db();
        db.Sys_Users.AddRange(
            new Sys_User{Id=Guid.NewGuid(),UserName="a",Password="x",RoleId=5,Enable=true},
            new Sys_User{Id=Guid.NewGuid(),UserName="b",Password="x",RoleId=5,Enable=false});  // 停用排除
        await db.SaveChangesAsync();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Role,null,5,null), new(){StarterUserId=Guid.NewGuid()});
        Assert.Single(res.ApproverIds);
    }
}
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现**

```csharp
// IApproverResolver.cs
namespace CP6.Core.Services.Wf;

public enum ApproverStrategy { DirectManager, DeptLeader, Role, Specified, Starter }
public record ApproverRule(ApproverStrategy Strategy, int? Levels, int? RoleId, Guid? SpecifiedUserId);  // RoleId int（OA-D2）
public class ApproverResolveContext { public Guid StarterUserId { get; set; } }
public class ApproverResolveResult
{
    public List<Guid> ApproverIds { get; set; } = new();
    public bool Resolved => ApproverIds.Count > 0;
    public string? UnresolvedReason { get; set; }
    public static ApproverResolveResult Ok(params Guid[] ids) => new() { ApproverIds = ids.ToList() };
    public static ApproverResolveResult Unres(string why) => new() { UnresolvedReason = why };
}
public interface IApproverResolver { Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx); }
```

```csharp
// ApproverResolver.cs（消费 PUB Sys_Dept/Sys_User）
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public class ApproverResolver : IApproverResolver
{
    private readonly CP6Context _db;
    public ApproverResolver(CP6Context db) => _db = db;

    public Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx) => rule.Strategy switch
    {
        ApproverStrategy.DirectManager => DirectManagerAsync(rule, ctx),
        ApproverStrategy.DeptLeader    => DeptLeaderAsync(ctx),
        ApproverStrategy.Role          => RoleAsync(rule),
        ApproverStrategy.Specified     => Task.FromResult(rule.SpecifiedUserId is Guid u ? ApproverResolveResult.Ok(u) : ApproverResolveResult.Unres("未指定审批人")),
        ApproverStrategy.Starter       => Task.FromResult(ApproverResolveResult.Ok(ctx.StarterUserId)),
        _ => Task.FromResult(ApproverResolveResult.Unres("未知审批人策略")),
    };

    private async Task<ApproverResolveResult> DirectManagerAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        var levels = rule.Levels is int l && l >= 1 ? l : 1;
        var current = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        if (current == null) return ApproverResolveResult.Unres("发起人不存在");
        Sys_User? manager = null;
        for (int i = 0; i < levels; i++)
        {
            if (current.ManagerId is not Guid mid) break;
            var next = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == mid && u.Enable);
            if (next == null) break;
            manager = next; current = next;
        }
        return manager == null ? ApproverResolveResult.Unres("发起人无直属上级，需人工指派") : ApproverResolveResult.Ok(manager.Id);
    }

    private async Task<ApproverResolveResult> DeptLeaderAsync(ApproverResolveContext ctx)
    {
        var user = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        if (user?.DeptId is not Guid did) return ApproverResolveResult.Unres("发起人无部门");
        var dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == did && d.Enable);
        while (dept != null)
        {
            if (dept.LeaderId is Guid lid)
            {
                var leader = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == lid && u.Enable);
                if (leader != null) return ApproverResolveResult.Ok(leader.Id);
            }
            if (dept.ParentId is not Guid pid) break;
            dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == pid && d.Enable);
        }
        return ApproverResolveResult.Unres("沿部门树未找到有效负责人，需人工指派");
    }

    private async Task<ApproverResolveResult> RoleAsync(ApproverRule rule)
    {
        if (rule.RoleId is not int rid) return ApproverResolveResult.Unres("未指定角色");
        var ids = await _db.Sys_Users.Where(u => u.RoleId == rid && u.Enable).Select(u => u.Id).ToListAsync();
        return ids.Count > 0 ? ApproverResolveResult.Ok(ids.ToArray()) : ApproverResolveResult.Unres($"角色 {rid} 下无启用用户");
    }
}
```

- [ ] **Step 4: 跑绿 → Step 5: DI + 提交** → `git commit -m "feat(wf): ApproverResolver 4 strategies + unresolved fallback (consume PUB org) (ch01)"`

---

# Phase B — 表单引擎运行时（章02 + 08 JSON 存储）

## Task B-1: Wf_FormDef/FormData 实体 + FormService + 迁移（章02 §3）

**Files:** Create `Wf_FormDef.cs`/`Wf_FormData.cs`(表名 Wf_FormDef/Wf_FormData), `IFormService.cs`/`FormService.cs`; Modify `CP6Context.cs`; migration; Test `FormServiceTests.cs`

- [ ] **Step 1: 失败测试**（建 FormDef 存 SchemaJson；提交 FormData 存 DataJson；后端按 schema 复核 required → 缺必填报错；FormDef.Version 改版不动旧 data）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体继承 BaseEntity，FormKey/SchemaJson/Version/Enable + FormKey/BizId/DataJson；FormService：SaveDef/GetDef/SubmitData[按 schema 服务端复核 required/类型]；FormKey 唯一索引）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + DI + 提交** → `git commit -m "feat(wf): Wf_FormDef/FormData + FormService + backend schema validation (ch02/08)"`

## Task B-2: 前端 DynamicForm schema 驱动渲染器（章02 §4/§5）

**Files:** Create `cp6.web/src/views/wf/DynamicForm.vue`, `src/api/wf/form.ts`, `src/types/wf/form.ts`

- [ ] **Step 1: 实现**——`<DynamicForm :schema :modelValue :mask?>`：`v-for f in schema.fields` 按 `f.type` 映射 Element Plus 控件（input/textarea/number/select/radio/checkbox/date/datetime/user/dept/upload）；`buildRules` 把 required/maxLength/pattern 翻成 el-form rules；预留 `mask` 入口（D-1 节点字段权限用：hidden 不渲染、readonly disabled）。
- [ ] **Step 2: 冒烟（手写 leave schema → 渲染请假单 → 提交存 DataJson）+ 提交** → `git commit -m "feat(wf): DynamicForm schema-driven renderer + validation (ch02)"`

---

# Phase C — 流程引擎状态机（章03 ★最硬核）

## Task C-1: Flow 实体 + FlowSchema DTO + 迁移（章03 §2/§3）

**Files:** Create `Wf_FlowDef.cs`/`Wf_FlowInstance.cs`/`Wf_FlowTask.cs`/`Wf_FlowHistory.cs`, `FlowSchema.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体（照 03 §3：FlowDef[FlowKey/FormKey/SchemaJson/Version]、FlowInstance[FlowKey/BizType/BizId/CurrentNode/Status/VarsJson/StarterId]、FlowTask[InstanceId/NodeId/AssigneeId/Status/Countersign/Comment]、FlowHistory[InstanceId/NodeId/ActorId/Action/Comment]）+ FlowSchema/FlowNode/FlowEdge 反序列化 DTO + 索引（FlowTask.InstanceId+NodeId、AssigneeId+Status）**
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(wf): flow def/instance/task/history entities + schema DTO (ch03 §2/§3)"`

## Task C-2: ConditionEvaluator 安全求值器（章03 §6，OA-D6）

**Files:** Create `CP6.Core/Services/Wf/ConditionEvaluator.cs`; Test `ConditionEvaluatorTests.cs`

- [ ] **Step 1: 失败测试**（`days > 3` 对 vars{days:5}→true；`type == 'annual' && days <= 3`；未知字段/非法表达式→安全失败不抛/不 eval 代码）
- [ ] **Step 2: 跑红 → Step 3: 实现**（白名单字段 + 比较 `> < >= <= == !=` + 逻辑 `&& ||`；递归下降小解析器或受限库；禁任意代码）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): safe condition evaluator (whitelist fields, no eval) (ch03 §6)"`

## Task C-3: FlowEngine — Submit/Act/EnterNode/会签三规则/条件流转（章03 §4/§5/§6）★★

**Files:** Create `IFlowEngine.cs`/`FlowEngine.cs`; Test `FlowEngineTests.cs`

- [ ] **Step 1: 失败测试（★状态机全链路）**

```csharp
public class FlowEngineTests
{
    [Fact]
    public async Task Submit_EntersFirstNode_CreatesTask()
    {
        // 起 leave 流程 → CurrentNode=n1，建 FlowTask 给直属上级
    }
    [Fact]
    public async Task Act_Approve_AdvancesToNextNode() { /* n1 同意 → 进 n2 */ }
    [Fact]
    public async Task Act_IsIdempotent_OnAlreadyDoneTask()
    {
        // 同一 task 第二次 Act → 无效（task.Status!=0 闸门），不重复流转
    }
    [Fact]
    public void EvaluateNode_Countersign_AllAnyVeto()
    {
        // all: 全同意才过/任一驳回即否；any: 任一同意即过/全驳才否；veto: 任一反对即死
        Assert.Equal((true,false), FlowEngine.EvaluateNodeCounts(approved:1, rejected:1, total:3, "all"));
        Assert.Equal((true,true),  FlowEngine.EvaluateNodeCounts(1,0,3,"any"));
        Assert.Equal((false,false),FlowEngine.EvaluateNodeCounts(2,0,3,"all"));
    }
    [Fact]
    public async Task NextNode_PicksBranchByCondition()
    {
        // days=2 → n1 后走 end（days<=3）；days=5 → 走 n2（days>3）
    }
    [Fact]
    public async Task EnterNode_UnresolvedApprover_SuspendsNotCrash()
    {
        // 审批人算不出 → 实例挂起待指派，不抛异常
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 03 §4/§5/§6：SubmitAsync 建实例+EnterNode；ActAsync 幂等闸门[task.Status!=0 return]+记 history+EvaluateNode 会签判定+流转 NextNode；EnterNode 调 ApproverResolver 算人[Unresolved→挂起]+建 FlowTask[一节点多人多条]；EvaluateNodeCounts 纯函数会签三规则[抽 static 便于单测]；NextNode 沿 edges 用 ConditionEvaluator 选分支；end 节点→Status=1 通过）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): FlowEngine state machine — submit/act/countersign(all/any/veto)/condition/idempotent (ch03 §4-6)"`

## Task C-4: FlowController（起流程/办理/查实例）

**Files:** Create `CP6.WebApi/Controllers/Wf/FlowController.cs`; DI

- [ ] **Step 1-3: 实现**（`/flow/def` CRUD、`/flow/submit`(flowKey+formData)、`/task/{id}/act`(approve/reject+comment)、`/flow/instance/{id}`(实例+痕迹) + DI + 集成测起→办→结束冒烟 + 提交）

---

# Phase D — 绑定 + 待办中心（章04）→ 可用 OA 闭合

## Task D-1: 节点字段权限 mask（章04 §2/§3）

**Files:** Modify `FlowSchema.cs`(node.fieldPerms)、`DynamicForm.vue`; Create 前端 buildFieldMask; 后端 mask 兜底

- [ ] **Step 1: 实现**——flow schema 节点加 `fieldPerms{field:edit|readonly|hidden}`；打开待办时 `buildFieldMask`(节点 fieldPerms + 默认[发起 edit/审批 readonly])→传 DynamicForm；后端兜底：hidden 返回置空、readonly 提交拒变更（与 PUB B1 字段权限取交集留 OA-D5 后接）。
- [ ] **Step 2: 冒烟 + 提交** → `git commit -m "feat(wf): node-level field permission mask (ch04 §2/§3)"`

## Task D-2: TaskCenterService 待办/我的申请/撤回 + SignalR（章04 §4/§5/§6）

**Files:** Create `TaskCenterService.cs`, `TaskController.cs`; Test `TaskCenterServiceTests.cs`

- [ ] **Step 1: 失败测试**（MyTodos 查当前用户 Status=0 的 FlowTask join 实例；MyApplications 查 StarterId；撤回置 Status=3 + 清在途 FlowTask）
- [ ] **Step 2: 跑红 → Step 3: 实现**（MyTodosAsync/MyApplicationsAsync/WithdrawAsync；建 FlowTask 时经现有 SignalR Hub 推送待办给 AssigneeId——复用 CP6 现成 Hub）
- [ ] **Step 4: 跑绿 → Step 5: TaskController(`/wf/my-todos`、`/wf/my-applications`、`/flow/{id}/withdraw`) + 提交** → `git commit -m "feat(wf): task center (todos/my-apps/withdraw) + SignalR push (ch04 §4-6)"`

## Task D-3: 前端三页面 + 阶段1 闭合冒烟（章04 §4/§5/§6）

**Files:** Create `cp6.web/src/views/wf/{TodoCenter,MyApplications,FlowTrace}.vue`; 路由

- [ ] **Step 1: 实现**——待办中心（列 FlowTask→打开渲染 DynamicForm[带 mask]+同意/驳回→调 act）；我的申请（列实例+步骤条[CurrentNode 高亮]+撤回）；审批痕迹（FlowHistory 时间线）。
- [ ] **Step 2: 阶段1 闭合 e2e**——手配 leave form schema + flow schema → 发起请假单 → 直属上级待办→同意 → (>3天走部门长→或签) → 结束 → 痕迹完整。**这条即"手配 JSON 的可用 OA"验证**。
- [ ] **Step 3: 提交** → `git commit -m "feat(wf): todo center + my apps + trace pages — stage1 usable OA (ch04)"`

---

## Self-Review（对照章01/02/03/04/08 覆盖）

- **章01**：ApproverResolver 4 策略(A-1) ✅ / 缺位兜底(A-1) ✅ / DirectManager 链顶取舍+缺位(A-1) ✅ / DeptLeader 沿树兜底(A-1) ✅ / Role 排除停用(A-1) ✅ / 消费 PUB 组织(OA-D1) ✅
- **章02**：FormDef/FormData JSON 列(B-1) ✅ / Version 改版(B-1) ✅ / schema 驱动渲染(B-2) ✅ / 前端校验+后端复核(B-1/B-2) ✅
- **章03**：状态机 tick Submit/Act(C-3) ✅ / 实例/待办/痕迹三表(C-1) ✅ / 会签 all/any/veto(C-3) ✅ / 条件流转(C-2/C-3) ✅ / 幂等闸门(C-3) ✅ / 缺位挂起(C-3) ✅ / schema 版本化(C-1) ✅
- **章04**：节点字段权限 mask(D-1) ✅ / 待办中心(D-2/D-3) ✅ / 我的申请+步骤条+撤回(D-2/D-3) ✅ / 审批痕迹时间线(D-3) ✅ / SignalR 推送(D-2) ✅
- **章08**：JSON 列存储(B-1/C-1，OA-D4) ✅（决策落地，EAV/动态建表权衡为文档讨论，不实现）

**已知缺口/推迟（已标注）：**
1. **PUB 角色字段权限 ∩ 节点字段权限**（OA-D5）—— PUB B1 落地后接（取更严）。
2. **TenantId**（OA-D3）—— OA 阶段4 章10 多租户统一。
3. **高级流程**（退回/加签/超时/委派）—— 章07，OA Plan 2（阶段3）。
4. **接业务（IApprovalService/Callback）**—— 章05，OA Plan 2（阶段2 MVP）。
5. **布局 schema（grid/tabs/group）**（章02 §7）—— v1 线性表单，留扩展。

**Type 一致性：** `ApproverRule(int? RoleId)`(A-1，OA-D2) ↔ FlowEngine.EnterNode 调用(C-3)；`FlowInstance.CurrentNode`/`FlowTask.Countersign`(C-1) ↔ FlowEngine(C-3)；`EvaluateNodeCounts`(C-3) 纯函数会签判定；DynamicForm mask 入口(B-2) ↔ buildFieldMask(D-1)；消费 PUB `Sys_Dept`/`Sys_User`(A-1) 来自 PUB B0。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-oa-stage1-runtime.md`。**OA 第一份（阶段1 可用 OA）**。后续：
- OA Plan 2 = `2026-06-13-oa-stage2-3-integration-advanced.md`（章05 接采购/财务 IApprovalService/Callback + 章06 规则引擎 + 章07 高级流程：退回/加签/超时/委派）
- OA Plan 3 = `2026-06-13-oa-stage4-designers.md`（章09 自研表单/流程设计器 + 章10 多租户商业化）

**下一步按工作流是你修订**（拍板 OA-D1~D6）。定稿后执行：依赖链 PUB B0 → **OA 阶段1** → OA 阶段2(接采购/财务) → 阶段3 → 阶段4。

---

*初稿生成于 2026-06-13。源：docs/approval/01·02·03·04（08 决策）。已勘察：组织模型归 PUB B0（消费不重建）、Sys_Role/User int 键(ApproverRule.RoleId 改 int)、零多租户、SignalR Hub 现成、Wf 命名空间新建、xUnit+InMemory。*
