# 02 · 表单引擎运行时：schema 驱动的动态表单

> **阶段 1 · 第一块引擎。** 本章做"表单解释器"：手写一段 form schema（JSON），让运行时读它、把页面画出来、把校验跑起来——**不写一行针对具体表单的页面代码**。本章结束时，改一段 JSON 就能给"请假单"加一个字段，页面自动多出来。
>
> 上游：[总纲](./README.md) 题眼（拖拽产生的不是代码，是 JSON；做 OA = 做两个解释器）。配套教学：[docs/oa/01 表单引擎](../oa/01-form-engine.md)。下游：[04 表单×流程绑定](./04-form-flow-binding.md) 给字段加节点级权限。

---

## 一、先记住：先做运行时，最后做设计器

低代码平台最反直觉的一点：**拖拽设计器是最后做的，不是最先做的**。

很多人一上来就想做"可视化拖拽器"，结果陷在画布交互里，半年出不了一个能用的表单。正确顺序是**从运行时倒推**：

1. 先**手写** form schema（JSON），做一个**渲染器**读它画页面——这一步就有了"能用的动态表单"。
2. 校验、数据存取都跑通。
3. **最后**才做"生成这段 JSON 的拖拽设计器"（[09 章](./09-designers.md)）。

> 设计器的产出，必须和你手写的 schema **同一种结构**。只要运行时认这套 JSON，谁生成的（手写 / 拖拽）都一样。所以**运行时是地基，设计器只是 JSON 的生产工具**。本章只做地基。

---

## 二、form schema 长什么样

一张表单 = 一段 JSON。核心是 `fields` 数组，每个字段描述"什么控件、绑哪个 key、怎么校验"：

```jsonc
{
  "formKey": "leave",            // 表单标识
  "title": "请假申请",
  "fields": [
    { "key": "leaveType", "label": "请假类型", "type": "select", "required": true,
      "options": [ { "label": "年假", "value": "annual" }, { "label": "病假", "value": "sick" } ] },
    { "key": "startDate", "label": "开始日期", "type": "date", "required": true },
    { "key": "endDate",   "label": "结束日期", "type": "date", "required": true },
    { "key": "days",      "label": "天数",     "type": "number", "required": true },
    { "key": "reason",    "label": "事由",     "type": "textarea", "maxLength": 500 }
  ]
}
```

**字段类型清单**（运行时渲染器要为每种 type 准备一个控件映射）：

| type | 渲染成（Element Plus） | 说明 |
|---|---|---|
| `input` / `textarea` | `el-input` | 单行/多行文本 |
| `number` | `el-input-number` | 数值 |
| `select` | `el-select` + options | 下拉 |
| `radio` / `checkbox` | `el-radio-group` / `el-checkbox-group` | 单/多选 |
| `date` / `datetime` | `el-date-picker` | 日期 |
| `user` / `dept` | 选人/选部门组件 | 复用 [01 组织模型](./01-org-model.md) |
| `upload` | 附件组件 | 复用 [PUB 附件](../pub/README.md) |

---

## 三、数据模型：定义与数据分离

```csharp
// CP6.Entity/DomainModels/Wf/FormDef.cs —— 表单定义（设计期产物）
[Table("Wf_FormDef")]
public class FormDef : BaseEntity
{
    public string FormKey    { get; set; } = "";   // 唯一标识，如 leave
    public string Name       { get; set; } = "";
    public string SchemaJson { get; set; } = "";   // ★整段 form schema JSON
    public int    Version    { get; set; } = 1;    // 改版留痕，旧单据按旧版渲染
    public bool   Enable     { get; set; } = true;
}

// CP6.Entity/DomainModels/Wf/FormData.cs —— 表单数据（运行期提交）
[Table("Wf_FormData")]
public class FormData : BaseEntity
{
    public string FormKey { get; set; } = "";      // 哪张表单
    public Guid   BizId   { get; set; }            // 关联的业务/流程实例
    public string DataJson{ get; set; } = "";      // ★用户填的值，JSON 列存
}
```

**为什么 `SchemaJson` 和 `DataJson` 都是整段 JSON 列、而不是拆成一堆字段表（EAV）？**

- 动态表单的字段是**用户配出来的**，编译期根本不知道有哪些列——没法建固定表。
- SQL Server 原生支持 JSON 列（`JSON_VALUE`/`OPENJSON` 可查可索引），存一段 `DataJson` 既灵活又能查。
- EAV（把每个字段拆成一行 key-value）查询要反复自连接、性能与可读性都差。

> 这是低代码"存储模型"的核心抉择，[08 章](./08-data-storage.md)专门展开 JSON 列 vs EAV vs 动态建表的权衡。本章先用 JSON 列，**够用且最简单**。

**为什么定义要带 `Version`？** 表单会改版（加字段、改校验）。已经提交的旧单据必须按**提交时那一版** schema 渲染，否则历史单据会错乱。所以 `FormData` 记录它用的是哪版 `FormDef`，定义改版不动旧数据。

---

## 四、运行时渲染器：读 schema 画页面

前端一个**动态渲染组件**，输入 `schema + 数据`，输出整张表单。骨架（Vue 3 + Element Plus）：

```vue
<!-- cp6.web/src/views/wf/DynamicForm.vue -->
<template>
  <el-form :model="model" label-width="100px">
    <el-form-item v-for="f in schema.fields" :key="f.key"
                  :label="f.label" :required="f.required">
      <!-- 按 type 映射到具体控件 -->
      <el-input        v-if="f.type === 'input'"    v-model="model[f.key]" />
      <el-input        v-else-if="f.type === 'textarea'" type="textarea" v-model="model[f.key]" />
      <el-input-number v-else-if="f.type === 'number'" v-model="model[f.key]" />
      <el-date-picker  v-else-if="f.type === 'date'" v-model="model[f.key]" type="date" />
      <el-select       v-else-if="f.type === 'select'" v-model="model[f.key]">
        <el-option v-for="o in f.options" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <!-- …其余 type 依次映射 -->
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
const props = defineProps<{ schema: any; modelValue: Record<string, any> }>()
const model = props.modelValue   // 双向绑定到 DataJson 反序列化出来的对象
</script>
```

**关键就这一句**：`v-for="f in schema.fields"` + 按 `f.type` 选控件。**页面不再为每张表单写死**——schema 多一个字段，页面自动多一行。这就是"schema 驱动"。改 JSON 改页面、不改代码，[总纲题眼](./README.md)在此兑现。

---

## 五、校验：运行时执行 schema 里的规则

字段上的 `required`/`maxLength`/`pattern` 也是 schema 的一部分，渲染器把它翻译成 Element Plus 的 `rules`：

```ts
function buildRules(field: any) {
  const rules: any[] = []
  if (field.required) rules.push({ required: true, message: `${field.label}必填` })
  if (field.maxLength) rules.push({ max: field.maxLength, message: `不超过${field.maxLength}字` })
  if (field.pattern)   rules.push({ pattern: new RegExp(field.pattern), message: field.patternMsg })
  return rules
}
```

> 前端校验是体验；和权限一样，**关键校验后端也要兜**（提交时按同一份 schema 在服务端复核 required/类型），否则绕过前端可塞脏数据。这条与 [PUB 后端强校验](../pub/README.md) 同理。

---

## 六、与流程的接缝（预告）

本章的表单是"裸表单"。真实审批里，**同一张表单在不同审批节点字段权限不同**——发起人能填全部、部门长只读金额、财务能改税额。这个"节点 × 字段 → 可见/可编辑"的绑定不在表单引擎里，在 [04 表单×流程绑定](./04-form-flow-binding.md)。本章只保证：**给定一份 schema 和一份权限掩码，渲染器能按掩码渲染**（留好 `disabled`/`hidden` 的入口即可）。

---

## 七、资深视角

**schema 要不要存"布局"（行列、分组、Tab）？** 要，但分层。先把 `fields` 跑通（线性表单），再加 `layout`（grid/tabs/group）作为 schema 的可选块——渲染器对没有 layout 的退化为线性。**别一开始就上复杂布局**，否则又掉进"先做设计器"的坑。

**前端硬编码 type→控件映射，会不会不够灵活？** 够用阶段就这么干（一个 `v-if` 链或 map）。要做"自定义控件市场"时，再升级为组件注册表（`componentMap[type]`）。YAGNI。

**版本化为什么重要？** 没有 `Version`，有人改了表单，三个月前的历史单据打开就缺字段/多字段、校验对不上。版本化让"定义可演进、数据可回放"——这是任何 schema 驱动系统的必修。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| schema → 动态渲染 | **form-create / variant-form** | fields 结构、type→控件映射、运行时渲染 |
| JSON 数据存储 | **SQL Server JSON / Postgres jsonb** | `JSON_VALUE`/`OPENJSON` 查动态数据 |
| 表单版本化 | **JeecgBoot online 表单** | 定义版本与历史数据回放 |

> form-create 的"维护一个 rule 数组、运行时渲染"和本章一模一样——确认"原来就这么回事"，没有魔法。

---

## 九、阶段1（表单部分）自检

- [ ] 为什么先做运行时、最后做设计器？设计器产出的 JSON 和手写的什么关系？
- [ ] `SchemaJson` 和 `DataJson` 为什么都用 JSON 列，而不是 EAV 或动态建表？
- [ ] `FormDef.Version` 解决什么问题？不存会怎样？
- [ ] 渲染器"schema 驱动"的那一句核心代码是什么？（`v-for fields` + 按 type 选控件）
- [ ] 前端校验够不够？为什么后端也要复核？

全部能答 → 表单解释器立住了。下一步 [03 流程引擎](./03-flow-runtime.md) 做"流程解释器"——审批流就是一台状态机，调用 [01 章](./01-org-model.md) 的解析器算审批人、建 `FlowTask`。

---

*配套教学（从零造一遍的更细讲解）见 [docs/oa/01](../oa/01-form-engine.md)。实现落 `CP6.Entity/DomainModels/Wf`、`cp6.web/src/views/wf/DynamicForm.vue`。*
