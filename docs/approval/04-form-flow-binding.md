# 04 · 表单 × 流程绑定：字段权限 + 待办中心

> **阶段 1 · 合体闭合。** 表单（02）和流程（03）两台引擎到这里合体：同一张表单在不同审批节点**字段权限不同**，再配上待办中心、我的申请、审批痕迹三个页面。本章结束时，一个**手配 JSON 的可用 OA** 跑起来——难看，但能从发起、待办、审批到结束全程走通。那一刻你就真正理解了 SmartOA。
>
> 上游：[02 表单引擎](./02-form-runtime.md)（渲染器留的 mask 入口）、[03 流程引擎](./03-flow-runtime.md)（FlowTask/Instance/History）。横切：[PUB 字段权限](../pub/README.md)（角色级，与本章节点级叠加）。

---

## 一、为什么需要"绑定"：同一张表单，不同节点不同权限

一张采购申请单，在不同审批环节，字段的可改/可见是不一样的：

| 节点 | 金额 | 事由 | 成本价 |
|---|---|---|---|
| 发起人填写 | 可编辑 | 可编辑 | 隐藏 |
| 部门长审批 | 只读 | 只读 | 隐藏 |
| 财务审批 | 只读 | 只读 | 可编辑（核成本） |

表单引擎只知道"有哪些字段"，流程引擎只知道"走到哪个节点"——**"这个节点这个字段能不能改"是两者的交集，既不属于表单也不属于流程，需要一层绑定**。这就是本章。

---

## 二、节点字段权限：挂在 flow schema 的节点上

最简单的落法：在 [03 章](./03-flow-runtime.md)的 flow schema 节点上加一块 `fieldPerms`：

```jsonc
{
  "nodes": [
    { "id": "start", "type": "start" },
    { "id": "n1", "type": "approval", "name": "部门长",
      "approver": { "type": "deptLeader" },
      "fieldPerms": { "amount": "readonly", "cost": "hidden" } },   // ★节点级字段权限
    { "id": "n2", "type": "approval", "name": "财务",
      "approver": { "type": "role", "roleId": 5 },
      "fieldPerms": { "cost": "edit" } }
  ]
}
```

权限三态，与 [PUB 字段权限](../pub/README.md)对齐：

| 值 | 含义 | 渲染/落地 |
|---|---|---|
| `edit` | 可编辑 | 正常控件 |
| `readonly` | 只读 | 控件 `disabled` |
| `hidden` | 隐藏 | 不渲染 + **后端置空** |

> 未在 `fieldPerms` 里列出的字段，取节点默认：发起节点默认 `edit`，审批节点默认 `readonly`。**审批节点默认只读**是安全的默认——审批是"看了表态"，不是"随便改"，要改得显式开 `edit`。

---

## 三、绑定如何作用于渲染器

[02 章的 `DynamicForm`](./02-form-runtime.md) 已经预留了 mask 入口，这里把"节点字段权限"算成 mask 传进去：

```ts
// 打开一个待办时：表单 schema + 该节点的 fieldPerms → 合成 mask
function buildFieldMask(nodeFieldPerms: Record<string,string>, schema: any) {
  const mask: Record<string, 'edit'|'readonly'|'hidden'> = {}
  for (const f of schema.fields)
    mask[f.key] = nodeFieldPerms[f.key]               // 节点显式配的
               ?? (isStartNode ? 'edit' : 'readonly') // 否则按节点默认
  return mask
}
```

```vue
<!-- DynamicForm 按 mask 渲染（02 章留的入口） -->
<el-form-item v-for="f in schema.fields" :key="f.key"
              v-if="mask[f.key] !== 'hidden'">     <!-- hidden 不渲染 -->
  <component :is="controlOf(f)" v-model="model[f.key]"
            :disabled="mask[f.key] === 'readonly'" /> <!-- readonly 禁用 -->
</el-form-item>
```

**后端兜底**：`hidden` 字段在返回 DTO 时**服务端置空/脱敏**，`readonly` 字段在提交时**服务端拒绝其变更**——否则绕过前端照样能看能改。这与 [02 校验](./02-form-runtime.md)、[PUB 强校验](../pub/README.md)同一条铁律：**前端是体验，后端才是边界**。

> **节点字段权限 vs PUB 字段权限**：PUB 的是"角色 × 资源 × 字段"的**长期**权限（采购员永远看不到成本）；本章是"流程节点 × 字段"的**临时**权限（这一审批环节成本只读）。两者**取交集（更严的赢）**：PUB 说隐藏就隐藏，PUB 允许时再看节点权限。

---

## 四、待办中心：查"该我办的"

待办中心就是查当前用户**状态为待办的 `FlowTask`**：

```csharp
// CP6.Core/Services/Wf/TaskCenterService.cs
public async Task<List<TodoDto>> MyTodosAsync(Guid userId)
{
    return await (from t in _db.FlowTasks
                  join i in _db.FlowInstances on t.InstanceId equals i.Id
                  where t.AssigneeId == userId && t.Status == 0          // 待办
                  orderby t.CreateDate descending
                  select new TodoDto {
                      TaskId = t.Id, InstanceId = i.Id, FlowName = i.FlowKey,
                      NodeName = t.NodeId, Starter = i.StarterId, At = t.CreateDate
                  }).ToListAsync();
}
```

打开一个待办 → 渲染表单（带节点 mask）+ 同意/驳回按钮 → 调 [03 章 `ActAsync`](./03-flow-runtime.md)。

**实时提醒**：新 `FlowTask` 建出来时，复用 CP6 现有 **SignalR Hub** 推一条给对应审批人，待办角标实时 +1，不用刷页面（总纲第六节"待办实时提醒复用 SignalR"在此兑现）。

---

## 五、我的申请：查"我发起的"

```csharp
public async Task<List<MyAppDto>> MyApplicationsAsync(Guid userId) =>
    await _db.FlowInstances.Where(i => i.StarterId == userId)
            .OrderByDescending(i => i.CreateDate)
            .Select(i => new MyAppDto {
                InstanceId = i.Id, FlowName = i.FlowKey,
                CurrentNode = i.CurrentNode,
                Status = i.Status,            // 进行中/通过/驳回/撤回
                StatusText = StatusText(i.Status)
            }).ToListAsync();
```

进度展示：把 flow schema 的节点画成一条横向步骤条，`CurrentNode` 高亮，已过的打勾——用户一眼看到"卡在谁那"。撤回（发起人主动取消进行中的单）也在这页，置 `Status=3` 并清掉在途 `FlowTask`。

---

## 六、审批痕迹：只追加的时间线

`FlowHistory` 是只追加流水（[03 章](./03-flow-runtime.md)），渲染成时间线即可，谁在哪个节点做了什么、写了什么意见、什么时间，一目了然：

```
2026-06-11 09:00  张三  提交           「下月年假5天」
2026-06-11 10:30  李四(部门长)  同意    「准」
2026-06-11 14:20  王五(财务)  驳回      「预算已超，下季再议」
```

> 痕迹**永不修改、永不删除**。审计、追责、合规全靠它。哪怕单据被撤回，痕迹也保留——"撤回"本身也是追加一条 history，而不是抹掉历史。

---

## 七、资深视角

**字段权限放 schema 里 vs 单独建表？** 学习/MVP 期放 flow schema 节点里最简单（一处配齐）。将来字段权限规则复杂（按角色 × 节点 × 字段三维）再抽 `Wf_NodeFieldPerm` 表。先 YAGNI。

**待办为什么查 `FlowTask` 而不是扫 `FlowInstance`？** 因为"该我办的"天然就是 `FlowTask.AssigneeId == 我 && 待办`。`FlowTask` 就是为"待办视图"而存在的派生状态——一个节点多人就是多条 task，每人各看各的。

**合体之后才算"理解 OA"**：表单引擎、流程引擎单独看都不难，难在它们的接缝（字段权限）和三个面向用户的页面。把这一层做通，你才真正拥有一个"难看但能用"的 OA——这正是 SmartOA 这类产品的内核。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 节点字段权限 | **钉钉/飞书审批 表单控件权限** | 每个审批节点配字段 可见/可编辑/隐藏 |
| 待办/已办/我发起 | **Flowable Task / 任意 OA 工作台** | task 查询、流程跟踪步骤条 |
| 审批痕迹/批注 | **泛微 e-cology 流程日志** | 只追加日志、意见留痕 |

---

## 九、阶段1 闭合自检

- [ ] "节点字段权限"为什么既不属于表单也不属于流程，要单独一层绑定？
- [ ] `edit`/`readonly`/`hidden` 三态分别怎么渲染？`hidden` 后端要做什么？
- [ ] 节点字段权限和 PUB 角色字段权限怎么叠加？（取交集，更严的赢）
- [ ] 待办中心为什么查 `FlowTask` 而不是 `FlowInstance`？
- [ ] 审批痕迹为什么只追加、撤回也不抹历史？

全部能答 → **阶段 1 闭合**：组织（01）+ 表单（02）+ 流程（03）+ 绑定（04）= 一个手配 JSON 的可用 OA。下一步 [05 集成](./05-integration.md) 是 ★MVP 价值点：用 `IApprovalService`/`IApprovalCallback` 同步回调，把采购 PR/PO、财务付款的审批桩真正接通。

---

*配套教学见 [docs/oa/03](../oa/03-form-flow-binding.md)。实现落 `CP6.Core/Services/Wf/TaskCenterService.cs`、`cp6.web/src/views/wf/{TodoCenter,MyApplications,FlowTrace}.vue`。*
