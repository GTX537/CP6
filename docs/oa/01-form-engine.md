# 01 · 表单引擎：schema 驱动的动态表单（阶段 0）

> 这是你最熟悉的入口。你在 SmartOA 里拖出来的"微表单"，本章把它的内部机制全部拆开。学完这章，你会做出一个**改一段 JSON 就能改页面**的动态表单引擎——这一刻，微表单对你不再是黑盒。

## 📍 学习目标

1. 微表单拖出来的页面，到底以什么形式存在系统里？
2. "动态渲染器"是怎么把一段 JSON 变成可填写的页面的？
3. 表单数据（用户填的值）该怎么存？JSON 列、EAV、动态建表怎么选？
4. 字段校验、必填、默认值放在哪一层做？
5. 为什么"先做运行时、后做设计器"是对的顺序？

---

## 🔎 核心：一张微表单 = 一段 form schema

你拖控件拖出来的东西，存进数据库就是这样一段 JSON（存在 `FormDef.SchemaJson` 字段）：

```json
{
  "formKey": "leave_apply",
  "title": "请假申请",
  "fields": [
    { "field": "applicant", "type": "user",     "label": "申请人", "readonly": true, "defaultValue": "$currentUser" },
    { "field": "leaveType", "type": "select",   "label": "请假类型", "required": true,
      "options": [ {"label":"年假","value":"annual"}, {"label":"病假","value":"sick"} ] },
    { "field": "startDate", "type": "date",      "label": "开始日期", "required": true },
    { "field": "days",      "type": "number",    "label": "天数",     "required": true, "min": 0.5 },
    { "field": "reason",    "type": "textarea",  "label": "事由" },
    { "field": "attachment","type": "upload",    "label": "附件" }
  ]
}
```

记住三件事：

- **`field`** 是数据的 key（存值用），**`label`** 是给人看的，**`type`** 决定用哪个控件渲染。
- 校验规则（`required`/`min`/`max`/正则）就挂在字段上，**前后端都读同一份 schema 做校验**。
- 这段 JSON **就是你拖拽的全部成果**。设计器（阶段4）做的事，无非是用界面生成这段 JSON。

---

## 🔎 运行时：动态渲染器（整章的灵魂）

普通页面是写死的：`<el-input v-model="form.reason" />`。动态表单不能写死——因为字段在 JSON 里、运行时才知道。所以要写一个**循环 schema、按 type 动态选控件**的渲染器。

```vue
<!-- cp6.web/src/components/oa/DynamicForm.vue（阶段0要写的核心组件） -->
<template>
  <el-form :model="formData" label-width="120px">
    <el-form-item
      v-for="f in schema.fields"
      :key="f.field"
      :label="f.label"
      :required="f.required"
    >
      <!-- 按字段类型选控件：这就是"解释器"的核心 -->
      <component
        :is="resolveControl(f.type)"
        v-model="formData[f.field]"
        :disabled="f.readonly"
        v-bind="controlProps(f)"
      />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { reactive } from 'vue'
import { ElInput, ElInputNumber, ElSelect, ElDatePicker } from 'element-plus'

const props = defineProps<{ schema: any }>()
const formData = reactive<Record<string, any>>({})

// type → 控件 的映射表，就是"渲染器"的查找表
const CONTROL_MAP: Record<string, any> = {
  input: ElInput,
  textarea: ElInput,    // 配 props type=textarea
  number: ElInputNumber,
  select: ElSelect,
  date: ElDatePicker,
}
function resolveControl(type: string) {
  return CONTROL_MAP[type] ?? ElInput   // 兜底，未知类型当文本
}
function controlProps(f: any) {
  if (f.type === 'textarea') return { type: 'textarea' }
  if (f.type === 'select')   return { options: f.options } // 视组件封装
  if (f.type === 'number')   return { min: f.min, max: f.max }
  return {}
}
</script>
```

**整个微表单的"魔法"就在 `CONTROL_MAP` 这张查找表 + `v-for` 循环里**。换一段 schema，页面就变了，**一行业务页面代码都不用改**。你在 SmartOA 看到的"拖一个控件页面就多一个框"，对应的就是 schema 多了一条、渲染器 `v-for` 多循环一次。

> 💡 真实的渲染器还要处理：分组/标签页/栅格布局、子表单（一对多明细）、富文本、级联选择、控件二次封装。但**内核就是上面这段**。先把这段跑通，再加复杂控件。

---

## 🔎 数据怎么存：三条路，OA 选 JSON 列

用户填完提交，`{leaveType:"annual", days:3, reason:"..."}` 这堆值存哪？这是表单引擎**最大的架构决策**（[第08章](./08-data-storage.md)专门讲，这里给结论）：

| 方案 | 怎么存 | 优点 | 致命缺点 |
|---|---|---|---|
| **动态建表** | 每个表单建一张真实表 | 查询快、能做外键 | 加字段要改表结构，几百个表单=几百张表，迁移地狱 |
| **EAV** | 一张表存"实例×字段×值"多行 | 极灵活 | 报表/多条件查询极痛苦，一条记录要拼很多行 |
| **JSON 列** ✅ | 整张表单存成一个 JSON 字段 | 简单、加字段零成本、天然贴合"一张审批单" | 跨记录按字段查询弱（但 OA 审批单很少需要） |

**OA 审批单 → 用 JSON 列。** SQL Server 2016+ 原生支持 JSON（`JSON_VALUE` / `OPENJSON`），CP6 用的就是 SQL Server，所以这条路零额外依赖：

```csharp
// CP6.Entity/DomainModels/Oa/FormData.cs（阶段0要建的表）
[Table("T_Oa_FormData")]
public class FormData : BaseEntity
{
    [Required, MaxLength(64)]
    public string FormKey { get; set; } = "";   // 哪张表单
    [Required, MaxLength(64)]
    public string BizId  { get; set; } = "";     // 这张单子的业务编号
    [Required]
    public string DataJson { get; set; } = "{}"; // 用户填的全部值，存 JSON
}
```

需要按某字段查时（如"查所有病假单"），用 SQL Server 的 `JSON_VALUE(DataJson, '$.leaveType') = 'sick'`，加计算列 + 索引还能优化。**先别过度设计**——阶段0就一个 JSON 列足够。

---

## 💡 资深视角

**为什么校验规则要放进 schema、而不是写在后端代码里？**
因为低代码的承诺是"配置即变更"。如果"天数必填"写死在 C# 里，那业务一改规则就得改代码、发版——这就不是低代码了。规则进 schema，前端校验做体验、后端**用同一份 schema 再校验一次**做安全（前端校验永远可绕过）。这就要求你写一个**能读 schema 跑校验的后端校验器**，前后端共用同一份规则定义。

**为什么先做运行时、后做设计器？**
设计器的唯一产出就是 `SchemaJson`。如果你连"拿到一段 schema 能不能正确渲染和存储"都没验证，就去做拖拽设计器，等于先盖二楼。**手写几段 JSON 把渲染器、校验器、存储跑通**，设计器才有意义——它只是个"少打字的工具"。

**`$currentUser` 这种动态默认值怎么处理？**
schema 里写的是占位符（`defaultValue: "$currentUser"`），运行时由引擎解析成实际值（当前登录人）。这引出下一步——**字段联动/计算/显隐**（[第05章 规则引擎](./05-rule-engine.md)）：比如"天数>3 时显示附件"，本质也是 schema 里一段规则、运行时解释执行。

---

## ⚠️ 踩坑记录

1. **用 `v-if` 堆控件类型**：新手会写一长串 `v-if="f.type==='input'" ... v-else-if`。控件一多就爆炸。**用 `<component :is>` + 映射表**，加控件只改映射表。
2. **schema 没版本号**：表单改版后，旧单子用旧 schema 渲染、新单子用新 schema。`FormDef` 必须有 `Version`，`FormData` 要记住它提交时用的是哪版，否则旧单子打开会错位。
3. **只在前端校验**：前端校验是体验，**后端必须用同一份 schema 再校一遍**，否则 Postman 直接绕过。
4. **JSON 列裸存不留痕**：审批单改了什么值要能追溯，结合 [第03章](./03-form-flow-binding.md) 的审批痕迹一起设计，别只存最终态。
5. **过早上 EAV**：被"以后要按字段查"吓到，一上来做 EAV，结果开发效率暴跌。OA 场景 JSON 列足够，真有重查询需求再加计算列。

---

## 🧪 自检题

1. 一张微表单在数据库里以什么形式存在？设计器的产出最终落到哪个字段？
2. 动态渲染器的核心是哪两样东西？为什么不能用 `v-if` 堆类型？
3. 表单数据三种存储方案各自的致命缺点是什么？OA 为什么选 JSON 列？
4. 为什么前端校验过了，后端还要再校一次？两边怎么共用规则？
5. 表单改版后，已提交的旧单子怎么保证还能正确渲染？

---

## 🔗 延伸阅读 / 动手清单

**读源码（建立"原来就这么回事"的确信）：**
- `form-create`、`variant-form / vue-form-making` —— 看它的 schema 结构和动态渲染器，和本章 `DynamicForm.vue` 对照。

**阶段 0 动手清单（做完即过关）：**
- [ ] 建表 `T_Oa_FormDef`（存 schema）、`T_Oa_FormData`（存数据，JSON 列）
- [ ] 写 `DynamicForm.vue` 动态渲染器，支持 input/textarea/number/select/date 5 种控件
- [ ] 后端写一个读 schema 跑必填/min/max 校验的校验器
- [ ] 手写 2 段不同的 form schema（请假、报销），验证**不改页面代码、只换 JSON** 页面就变
- [ ] 提交一张单子，确认数据以 JSON 正确落库、能读回渲染

**下一章** → [02. 流程引擎：审批流就是一台状态机](./02-flow-engine.md)，进入阶段1，啃 OA 最硬核的部分。
