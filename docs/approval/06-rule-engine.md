# 06 · 规则引擎：显隐 / 计算 / 联动 / 条件分支

> **阶段 3 · 让表单"活"起来。** 02 章的表单是死的——填什么显示什么。本章加一台规则引擎：选了"病假"才显示"病假证明"、填了起止日期自动算出"天数"、选了省自动联动出市。这些联动也是一段 JSON 规则，运行时监听字段变化、匹配规则、执行动作。本章结束时，表单有了"反应"。
>
> 上游：[02 表单引擎](./02-form-runtime.md)（字段与渲染器）。同源：[03 流程引擎](./03-flow-runtime.md)的连线 `condition` 用的是同一套表达式求值。横切：[04 绑定](./04-form-flow-binding.md)的字段权限与本章显隐叠加。

---

## 一、题眼：联动也是 JSON，运行时执行

规则引擎和表单引擎是同一个套路——**不写死、配规则**：

> **一条规则 = "当某条件成立（when），就对某字段执行某动作（then）"。引擎监听表单字段变化，每次变化重新匹配所有规则、应用效果。规则存在 form schema 里，运行时解释执行。**

form-create、宜搭、简道云的"字段联动"全是这一个模型。没有魔法：你在设计器里连一根"A 变了改 B"的线，就是往 schema 的 `rules` 数组加一条 `{when, then}`。

---

## 二、规则能干的四类事

| 类型 | 例子 | 动作 |
|---|---|---|
| **显隐** | 选"病假"才显示"病假证明" | `show` / `hide` |
| **计算** | 天数 = 结束 − 开始 + 1 | `compute` |
| **联动取值** | 选省 → 市下拉变 / 选客户 → 带出地址 | `setOptions` / `setValue` |
| **条件必填/禁用** | 请假 >3 天则"事由"必填 | `require` / `disable` |

> **条件分支**（按字段值走不同审批节点）严格说也属"规则"，但它发生在**流程**层、用 [03 章](./03-flow-runtime.md)连线 `condition`，不在表单 `rules` 里。两者**共用同一个表达式求值器**，只是作用对象不同：表单规则改"字段状态"，流程条件选"下一节点"。

---

## 三、rule schema 长什么样

挂在 [02 章](./02-form-runtime.md) form schema 上，与 `fields` 平级：

```jsonc
{
  "formKey": "leave",
  "fields": [ /* …02章的字段… */ ],
  "rules": [
    { "when": "leaveType == 'sick'",
      "then": [ { "action": "show", "target": "medicalCert" } ] },

    { "when": "always",
      "then": [ { "action": "compute", "target": "days",
                  "expr": "dateDiff(startDate, endDate) + 1" } ] },

    { "when": "days > 3",
      "then": [ { "action": "require", "target": "reason" } ] },

    { "when": "province changed",
      "then": [ { "action": "setOptions", "target": "city", "source": "cityOf(province)" } ] }
  ]
}
```

- `when`：触发条件，对当前表单值求值（`always` = 任意变化都跑，`X changed` = X 变化时跑）。
- `then`：一组动作，作用到目标字段的"状态"（可见/必填/值/选项）。

---

## 四、运行时：监听变化 → 匹配 → 应用

```ts
// cp6.web/src/views/wf/ruleEngine.ts —— 表单状态由规则驱动
function applyRules(schema: any, model: Record<string, any>) {
  const effect = { visible: {} as any, required: {} as any, disabled: {} as any, options: {} as any }
  // 默认：字段按 schema 原始设定
  for (const f of schema.fields) { effect.visible[f.key] = true; effect.required[f.key] = !!f.required }

  for (const rule of schema.rules ?? []) {
    if (!evalWhen(rule.when, model)) continue           // 条件不成立，跳过
    for (const act of rule.then) {
      switch (act.action) {
        case 'show':   effect.visible[act.target] = true;  break
        case 'hide':   effect.visible[act.target] = false; break
        case 'require':effect.required[act.target] = true; break
        case 'disable':effect.disabled[act.target] = true; break
        case 'compute':model[act.target] = evalExpr(act.expr, model); break   // ★算出来写回 model
        case 'setOptions': effect.options[act.target] = evalExpr(act.source, model); break
      }
    }
  }
  return effect
}
```

接进 [02 章 `DynamicForm`](./02-form-runtime.md)：用 Vue 的 `watch(model, () => effect = applyRules(...))`，字段一变就重算 `effect`，渲染据此显隐/禁用/刷新选项。**整张表单的"反应"就是这一个重算循环**。

---

## 五、安全的表达式求值（关键）

`when`/`expr` 是从 schema 来的字符串，**绝不能 `eval()` 任意 JS**——那样 schema 就成了 XSS/注入入口。用一个**受限表达式求值器**：

- 只允许：字段引用（白名单 = schema 里声明的 key）、比较/逻辑运算（`== != > < && || !`）、少量内置函数（`dateDiff`、`cityOf`、`sum`…）。
- 禁止：函数定义、属性访问到原型、`window`/`document`、任意调用。
- 实现：用一个小型表达式解析库（如 `expr-eval`）或自写 AST 求值，**不碰 `eval`/`new Function`**。

> 这条和 [03 章流程 `condition`](./03-flow-runtime.md) 完全同源——同一个求值器，前端表单和后端流程都用它。统一一处，安全口子只守一道。

---

## 六、前端体验 vs 后端复算

规则引擎主要跑在前端（即时反应）。但**计算结果不能只信前端**：

- `compute` 出来的 `days`、金额合计等，**提交时后端按同一规则重算一遍**，和前端不符就拒绝——否则用户改前端就能伪造"天数=1"。
- `require`（条件必填）后端也要复核：满足 `when` 时该字段非空。

> 又是那条铁律：**前端规则是体验，后端复算是边界**（和 [02 校验](./02-form-runtime.md)、[05 强校验](./05-integration.md) 一致）。要做到这一点，求值器必须**前后端共享同一套逻辑**——所以表达式语言要简单到能在 C# 和 TS 两边都实现，别用某端独有的语法糖。

---

## 七、资深视角

**规则放 schema vs 写代码？** 放 schema 才叫低代码——业务改联动不用发版。代价是表达式语言要克制（太弱表达不了、太强不安全）。**取一个"够用的小语言"**：比较、逻辑、几个内置函数，覆盖 90% 联动，剩下 10% 真复杂的走自定义函数注册，不硬塞进表达式。

**计算字段会不会循环依赖？** A 算 B、B 又算 A 就死循环。引擎要么做拓扑排序按依赖顺序算，要么限制"一次变化只重算一轮"（不级联触发再触发）。MVP 用后者最简单，配规则的人避免成环即可。

**为什么和流程条件共用求值器？** 因为"days > 3 必填事由"（表单规则）和"days > 3 走部门长"（流程条件）是同一个判断。共用求值器既省代码，又保证"前端看到要走部门长"和"后端真的走部门长"用的是同一套语义，不会一边一个样。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 字段联动规则 | **form-create rule / 宜搭 公式联动** | when→then、计算字段、级联选项 |
| 安全表达式 | **expr-eval / Jexl** | 受限语法、白名单变量、无 eval |
| 前后端共享规则 | **JSONLogic** | 一套 JSON 规则，多端实现同语义 |

> JSONLogic 就是为"一套规则前后端都能跑"设计的——和本章"前端体验、后端复算、共用求值器"的诉求一致，可直接借鉴它的表达式结构。

---

## 九、阶段3（规则部分）自检

- [ ] 规则引擎的核心循环是什么？（监听变化→匹配 when→应用 then）
- [ ] 表单 `rules` 和流程 `condition` 什么关系？为什么共用求值器？
- [ ] 为什么绝不能 `eval()` schema 里的表达式？怎么安全求值？
- [ ] `compute` 出来的值为什么后端还要重算？
- [ ] 计算字段循环依赖怎么避免？

全部能答 → 表单有了"反应"。下一步 [07 高级流程](./07-advanced-flow.md) 给流程加"反应"——退回/加签/超时/委派，接住真实审批里那些不走寻常路的场景。

---

*配套教学见 [docs/oa/05](../oa/05-rule-engine.md)。实现落 `cp6.web/src/views/wf/ruleEngine.ts` + 后端共享求值器 `CP6.Core/Services/Wf/ExpressionEvaluator.cs`。*
