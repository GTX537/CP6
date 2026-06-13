# 03 · 表单 × 流程：字段级权限 + 待办中心（阶段 2）

> 阶段0 你有了能渲染的表单，阶段1 你有了能流转的引擎。本章把两者**合体**，产出一个**手工配 JSON、但真正能用的 OA**。完成这一章，你就拥有了一个"难看但完整"的审批系统——那一刻你就真懂 SmartOA 了。

## 📍 学习目标

1. 表单和流程是怎么"绑"在一起的？绑的到底是什么？
2. "这个节点这个字段能改、那个节点只读"——字段级权限存在哪、怎么生效？
3. 待办中心、我的申请、已办、抄送，数据从哪来？
4. 一张单子的完整生命周期，前后端各发生了什么？
5. 审批痕迹（谁改了什么）怎么留？

---

## 🔎 绑定点一：流程关联表单

`FlowDef` 里有 `formKey`，这就是最外层的绑定——一个流程用哪张表单。发起时：

```
用户在"请假流程"点发起
  → 取 FlowDef(leave_flow)，读到 formKey=leave_apply
  → 取 FormDef(leave_apply)，渲染表单让用户填
  → 提交：先存 FormData（拿到 bizId），再 FlowEngine.StartAsync(flowKey, bizId)
```

**一张单子 = 一条 FormData（数据） + 一条 FlowInstance（流程状态），靠 `bizId` 串起来。**

---

## 🔎 绑定点二：节点字段权限（本章灵魂）

你用 SmartOA 时最有"融为一体"感觉的，就是**走到不同审批节点，同一张表单的字段可填/只读/隐藏不一样**。比如：

- 发起人节点：所有字段可填
- 直属上级节点：申请内容只读，只能填"审批意见"
- HR 节点：能改"实际核定天数"，其余只读

这个能力，本质是**在流程节点上挂一张"字段权限表"**，放进 `FlowDef` 的节点定义里：

```json
{
  "id": "n2", "type": "approval", "name": "HR审批",
  "assignee": { "type": "role", "value": "HR" },
  "fieldPerms": {
    "leaveType":  "readonly",
    "reason":     "readonly",
    "verifiedDays": "edit",      // 只有 HR 节点能改"核定天数"
    "attachment": "hidden"
  }
}
```

运行时怎么生效？**前端打开待办时，把"表单 schema"和"当前节点的 fieldPerms"一起拿到，渲染器按 perms 决定每个字段 disabled/隐藏**：

```ts
// 渲染待办时合并 schema + 当前节点权限
function effectiveFields(schema, fieldPerms) {
  return schema.fields
    .filter(f => fieldPerms[f.field] !== 'hidden')        // 隐藏的不渲染
    .map(f => ({ ...f, readonly: fieldPerms[f.field] === 'readonly' || f.readonly }))
}
```

> ⚠️ 和表单校验一样，**字段权限前端做体验、后端必须复核**：提交时后端根据"当前节点的 fieldPerms"校验"这次提交只动了允许改的字段"，否则 Postman 能改任意字段。这是低代码 OA 最容易被忽略的安全点。

---

## 🔎 待办中心：四个列表的数据来源

OA 首页那几个常见列表，全是对前两章那几张表的查询：

| 列表 | 含义 | 查询 |
|---|---|---|
| **待办** | 该我处理的 | `FlowTask WHERE AssigneeId=我 AND State='pending'` |
| **已办** | 我处理过的 | `FlowTask WHERE AssigneeId=我 AND State='done'` |
| **我的申请** | 我发起的 | `FlowInstance WHERE StarterId=我` |
| **抄送我的** | 知会我的 | `FlowCc WHERE UserId=我`（抄送是独立轻表） |

新待办产生时，用 CP6 已有的 **SignalR** 实时推给对应用户的待办角标——这就是"有人提交单子，你右上角立刻 +1"的实现。

---

## 🔎 一张单子的完整生命周期（把全书串起来）

```
[发起] 用户填表单 → 存 FormData(bizId) → StartAsync → 建 FlowInstance(CurrentNode=n1)
        → n1 解析审批人(第04章) → 建 FlowTask(待李经理办) → SignalR 推送
[审批] 李经理打开待办 → 取 FormData + n1.fieldPerms 渲染（内容只读+意见可填）
        → 点同意 → AdvanceAsync(approve) → 关待办、记 FlowHistory
        → 按边+条件(第05章) 算下一节点 → 到 n2 建新待办 推送 HR
[结束] HR 同意 → 到 end → Status=approved → OnApproved 发集成事件(第09章)
        → BridgeHook 回写业务（如登记考勤）
[追溯] 任何人看这张单：FormData 看填了啥，FlowHistory 看谁批的、什么意见、几点批的
```

**这条链就是一个完整 OA 的全部。** 阶段2 结束，你应该能手工配出请假、报销两个流程并各跑通一张单。

---

## 💡 资深视角

**为什么字段权限放在流程节点、而不是表单里？**
因为权限是"随流程位置变化"的——同一字段在不同节点权限不同。放表单里只能表达"静态权限"，表达不了"走到哪一步变了"。放节点上，才对应你拖审批流时配的"本节点表单权限"。这也说明：**表单引擎和流程引擎是两个引擎，但在节点这一层交汇**。

**抄送(Cc)为什么单独一张表，不塞进 FlowTask？**
待办是"必须处理才能往下走"，抄送是"知会、不影响流转"。语义不同、查询不同、状态机不依赖它。混在一起会让"还有几个待办没办完才能流转"的判断变复杂。保持 FlowTask 纯粹。

**草稿、撤回、转发怎么归位？**
草稿=FormData 已存但没 StartAsync；撤回=发起人在单子还没被处理时把 Instance 置 canceled、关待办；转发/委派=改 FlowTask.AssigneeId（[第06章](./06-advanced-flow.md)）。都是对这几张表的状态操作，没有新魔法。

---

## ⚠️ 踩坑记录

1. **字段权限只在前端做**：最高频的安全漏洞。后端提交时必须按当前节点 fieldPerms 复核改动范围。
2. **待办和实例状态不同步**：单子已结束(approved)，但还残留 pending 待办，用户点进去能"再审一次"。结束/驳回时务必关掉所有未办待办。
3. **bizId 用自增 ID**：表单数据和流程实例用数据库自增 ID 关联，跨库/分库就崩。用业务流水号或 GUID。
4. **审批意见丢失**：只改状态不写 FlowHistory，事后无法追溯"谁说了什么"。每次 Advance 必写历史。
5. **抄送当待办**：把抄送做成必须点"已读"才消失，用户会把它和真待办混淆，漏批真单子。

---

## 🧪 自检题

1. 一张单子由哪两条记录组成？靠什么字段关联？
2. "节点字段权限"存在 FlowDef 的哪里？前端和后端各做什么？
3. 待办/已办/我的申请三个列表分别查哪张表的什么条件？
4. 描述一张请假单从发起到结束，FormData / FlowInstance / FlowTask / FlowHistory 各自何时被写。
5. 撤回和驳回，在数据上分别改了什么？

---

## 🔗 延伸阅读 / 动手清单

**阶段 2 动手清单（做完即拥有可用 OA）：**
- [ ] 发起接口：存 FormData → StartAsync，串起表单和流程
- [ ] 待办详情接口：返回 FormData + 当前节点 fieldPerms
- [ ] `DynamicForm.vue` 支持按 fieldPerms 渲染只读/隐藏
- [ ] 提交接口后端复核字段改动范围
- [ ] 四个列表页：待办 / 已办 / 我的申请 / 抄送
- [ ] 接 SignalR：新待办实时推送角标
- [ ] **里程碑**：手工配请假 + 报销两个流程，各跑通一张端到端单子

**下一章** → [04. 组织引擎：部门树 / 上下级 / 审批人解析](./04-org-engine.md)，补上 CP6 的硬缺口，让"直属上级"算得出来。
