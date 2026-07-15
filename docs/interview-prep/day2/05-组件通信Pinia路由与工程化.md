# Day 2 · 第 5 章：组件通信、Pinia、路由与前端工程化

> 面向对象：3 天冲刺"制造业生产管理系统开发工程师"面试的学员（JD：熟悉 JavaScript / CSS / VUE / ElementUI，5 年经验强度）。
> 标本来源：`C:\CP6\cp6.web` —— 一套真实在跑的生产前端（Vue 3.5 + TypeScript + Vite 8 + Element Plus + Pinia 3 + vue-router 5 + vue-i18n + Vitest + Playwright），多租户 SaaS 制造业 ERP/MES/WMS/OA。
> 上一章（第 4 章）讲了组合式 API 基础（`ref` / `reactive` / `computed` / `watch` / 生命周期 / `<script setup>`）。本章把这些"单组件内"的知识，扩展到"组件之间""页面之间""前后端之间""开发到部署之间"。

**本章固定讲解结构**：每个知识点都走一遍
`概念（用后端概念类比）→ cp6.web 真实代码（标出文件路径）→ 逐行解析 → 坑 → 面试问答`。

**学习目标**（面试官会顺着 JD 一路问下来，你要能接住）：
1. 说清 7 种组件通信方式，以及"什么时候用哪种"的决策依据；
2. 讲透 Pinia：为什么需要它、`defineStore` 怎么写、`storeToRefs` 的坑；
3. 讲透路由：SPA 原理、懒加载、动态路由、**路由守卫做登录/权限拦截**；
4. 逐行读懂 `http.ts`（axios 封装）——这是前后端联调的枢纽；
5. CSS 硬功夫：盒模型、Flex、Grid、scoped 原理、`:deep()`、响应式；
6. Vite / 构建 / 环境变量 / 部署；
7. 前端测试（Vitest + Playwright）；
8. 工程质量（ESLint / TS 严格模式 / i18n 工程化 / CI 拦截）；
9. 把一个"库存查询"功能，从 Vue 页面一路串到 SQL。

---

## 目录

- [5.0 先建立一张"全景地图"](#50-先建立一张全景地图)
- [5.1 组件通信全景（面试必考清单）](#51-组件通信全景面试必考清单)
- [5.2 Pinia 深讲](#52-pinia-深讲)
- [5.3 vue-router 深讲](#53-vue-router-深讲)
- [5.4 axios 与 API 层设计（本章主标本 http.ts 逐行）](#54-axios-与-api-层设计本章主标本-httpts-逐行)
- [5.5 CSS 专题（JD 明确要求）](#55-css-专题jd-明确要求)
- [5.6 Vite 与构建](#56-vite-与构建)
- [5.7 前端测试](#57-前端测试)
- [5.8 工程质量（ESLint / TS / i18n / CI）](#58-工程质量eslint--ts--i18n--ci)
- [5.9 前后端协作全链路串讲（库存查询）](#59-前后端协作全链路串讲库存查询)
- [章末：面试题 20 问](#章末面试题-20-问详细答案)
- [自测清单](#自测清单)
- [动手练习 3 个](#动手练习-3-个)

---

## 5.0 先建立一张"全景地图"

在钻细节之前，先看清一个 Vue 3 前端工程有哪几层。用后端的分层思想类比你就秒懂了：

```
后端（你 Day 1 学的）              前端 cp6.web 对应层
──────────────────────           ──────────────────────────────────
Controller（HTTP 入口）           views/*.vue（页面组件）
Service（业务逻辑）               stores/*.ts（Pinia 状态）+ 组件方法
Repository / EF（数据访问）        api/*.ts（axios 请求封装）
DbContext / 连接串                api/http.ts（axios 实例 + 拦截器）
依赖注入容器                      Pinia / provide-inject
路由（[Route] / MapControllers）  router/index.ts（前端路由）
中间件（Auth / 异常处理）         路由守卫 beforeEach + axios 拦截器
appsettings.json                 .env / import.meta.env / vite.config.ts
资源文件 .resx（多语言）          i18n（vue-i18n + 后端 Sys_Lang 表）
xUnit 单测                        Vitest 单测
集成/E2E 测试                     Playwright E2E
dotnet build / publish            vite build / vue-tsc
```

真实目录（`C:\CP6\cp6.web\src`）：

```
src/
├── api/            # 请求封装层（http.ts + 按模块分的子目录）
│   ├── http.ts     # ★ axios 实例 + 拦截器（本章主标本）
│   ├── sys/        # 系统管理相关 API（rolePerm.ts …）
│   ├── wms/        # 仓库模块 API（stock.ts / warehouse.ts …22 个文件）
│   └── ...
├── stores/         # Pinia 状态（permission.ts / counter.ts / order.ts …）
├── router/
│   └── index.ts    # ★ 路由表 + 动态路由 + 守卫
├── i18n/
│   └── index.ts    # ★ 多语言引擎（懒加载 + 回退链）
├── views/          # 页面组件（按模块分：wms/ erp/ mes/ oa/ fin/ …）
├── components/     # 可复用组件（templates/ base/ …）
├── composables/    # 组合式函数（useBreakpoint …）
└── types/          # TypeScript 类型定义
```

一句话记忆：**页面（views）调 API 层（api），API 层调 http.ts 发请求，跨页面共享的数据放 Pinia（stores），页面之间靠 router 跳转，语言靠 i18n，全部由 Vite 打包。** 本章就是把这张图上每根线讲透。

---

## 5.1 组件通信全景（面试必考清单）

> 这一节几乎是每场 Vue 面试的必问。面试官往往问："父子组件怎么通信？""兄弟组件怎么通信？""跨了好几层怎么办？"你要能背出全部 7 种方式 + 各自适用场景。

### 5.1.0 概念：为什么"组件通信"是个问题？

组件是**封装**的（跟后端一个 class 封装私有字段是同一个道理）。组件 A 内部的 `ref` 变量，组件 B 默认看不见也改不了。可 UI 是拼出来的：一个页面由几十个组件组成，它们必须协作——父组件把数据传给子组件显示，子组件把用户操作报告给父组件。这就是"通信"。

Vue 的通信方式，本质是围绕**组件树**（一棵父子嵌套的树）设计的：

```
        App
         │
      LayoutView          ← 布局壳（侧边栏 + 头部 + <router-view/>）
         │
     StockQueryView       ← 页面（父）
      ┌──┴──────┐
  CpFilterBar   VolTable  ← 子组件（查询栏、表格）
```

- 数据从**上往下**流（父 → 子）：用 **props**。
- 事件从**下往上**报（子 → 父）：用 **emit**。
- 双向绑定（既传值又能改）：**v-model**（本质是 props + emit 的语法糖）。
- 跨很多层（祖先 → 很深的后代）：**provide / inject**。
- 父想直接调子的方法：**模板 ref + defineExpose**。
- 跟组件树无关、任意组件都要读写的全局数据：**Pinia**。

### 5.1.1 props 下行（只读！）

**概念（类比后端）**：props 就像调用一个方法时传进去的**入参**。方法（子组件）能读入参，但改了入参**不该影响调用方（父组件）的原始变量**——尤其是值类型，改了也没用；引用类型改了会污染上游，是 bug。Vue 干脆规定：**props 是只读的，子组件不能直接改 props**。

**真实代码**（`src/components/templates/CpFilterBar.vue`，第 58-62 行）：

```ts
const props = defineProps<{
  fields: FilterField[]                     // 查询字段声明表
  modelValue: Record<string, unknown>       // 查询条件对象（v-model 的值）
  labels?: FilterBarLabels                  // 可选：按钮文案覆盖
}>()
```

模板里读 props（第 91-100 行）：

```html
<div v-for="f in visibleFields" :key="f.key" class="fld">
  <label>{{ f.label }}</label>
  <el-input
    v-if="f.type === 'text'"
    :model-value="(modelValue[f.key] as string | undefined)"
    :placeholder="f.placeholder"
    clearable
    @update:model-value="setField(f.key, $event)"
  />
```

**逐行解析**：
- `defineProps<{...}>()` 是 `<script setup>` 里声明 props 的**编译宏**（不用 import，编译器认识它）。用 TypeScript 泛型写类型，等价于运行时 `defineProps({ fields: Array, ... })` 但类型更强。
- `fields: FilterField[]` —— 父组件通过 `:fields="[...]"` 传进来的字段列表。
- `modelValue` —— 这是 `v-model` 的约定名（见 5.1.3）。
- `labels?` —— `?` 表示可选 prop。
- 模板里 `{{ f.label }}`、`:placeholder="f.placeholder"` 都是**读** props，完全合法。

**为什么子组件不能改 props？（面试高频）**

看这个组件的关键设计（第 76-79 行）——用户改了某个查询字段，它**没有**去改 `props.modelValue`，而是：

```ts
// 变更单个字段：始终抛出全新对象，不原地修改 prop
function setField(key: string, value: unknown) {
  emit('update:modelValue', { ...props.modelValue, [key]: value })
}
```

它用 `{ ...props.modelValue, [key]: value }` 造了一个**全新对象**，然后 `emit` 上去让父组件更新。它绝不写 `props.modelValue[key] = value`。

原因有三层：
1. **单向数据流原则**：数据只能父 → 子。子组件若能随手改 props，那"这个值现在是多少、谁改的"就无法追踪了，跟后端把 DTO 到处乱改一样是维护灾难。
2. **值类型改了白改**：如果 prop 是 `number`，子组件 `props.count = 5` 在开发模式会直接报 Vue 警告 `Set operation on key "count" failed: target is readonly`，且父组件下次重渲染会把它覆盖回去。
3. **引用类型改了会"偷偷"污染父**：如果 prop 是对象/数组，`props.obj.x = 1` 语法上不报错，但你**绕过了父组件**直接改了父的数据，父组件根本不知道，造成"数据变了但视图没同步""两个组件抢着改同一个对象"的诡异 bug。

**正确做法（3 种）**：
1. **要通知父组件改** → `emit` 一个事件让父改（上面 CpFilterBar 就是这么做的）。
2. **只是本地要个可编辑副本** → 用 `ref` 拷贝一份，如 `VolForm.vue` 第 95、101-107 行：
   ```ts
   const localData = ref<any>({})
   watch(() => props.formData, (val) => { localData.value = { ...val } }, { deep: true })
   ```
   父传进来的 `formData` 只读；组件内部编辑的是 `localData` 这个副本，提交时再 `emit('submit', localData.value)`。
3. **基于 prop 派生只读值** → 用 `computed`。

**坑**：
- 对象/数组 prop 的"只读"是**浅只读**。`props.obj = {}`（换引用）会被 Vue 拦截报错；但 `props.obj.x = 1`（改内部）不报错却是 bug。别以为不报错就没事。
- `defineProps` 是编译宏，**不能**把它的返回值解构后还想保持响应性（`const { fields } = defineProps()` 会丢失响应性，除非用 Vue 3.5 的响应式解构或 `toRefs`）。cp6.web 里统一写 `const props = defineProps(...)` 然后 `props.xxx` 访问，就是为了不踩这个坑。

**面试问答**：
> **Q：为什么 Vue 规定 props 是只读的？子组件想改怎么办？**
> A：为了保证单向数据流——数据只从父流向子，来源单一、可追踪。子组件想改有三种正确姿势：① 需要父同步更新，就 `emit` 事件让父改（配合 `v-model` 更顺）；② 只是本地临时编辑，就用 `ref`/`reactive` 拷一份副本改副本；③ 只是基于 prop 算个衍生值，用 `computed`。直接改 props——尤其是改对象内部字段——会绕过父组件污染上游状态，是典型 bug。

### 5.1.2 emit 上行（`defineEmits` 类型化）

**概念（类比后端）**：emit 就像子组件对父组件发起的一个**回调 / 事件通知**——"我这儿发生了点事，你（父）看着办"。类似后端的领域事件（domain event）或回调委托。子组件不知道父组件具体要干嘛，它只负责"报告"。

**真实代码**（`CpFilterBar.vue` 第 63-67 行）：

```ts
const emit = defineEmits<{
  (e: 'update:modelValue', next: Record<string, unknown>): void
  (e: 'search'): void
  (e: 'reset'): void
}>()
```

触发（第 78、85、160 行）：

```ts
emit('update:modelValue', { ...props.modelValue, [key]: value })  // 值变了
emit('reset')                                                     // 点了重置
```
```html
<el-button type="primary" @click="emit('search')">{{ labels?.search ?? '查询' }}</el-button>
```

父组件监听（组件注释第 22-29 行给的用法示例）：

```html
<CpFilterBar v-model="query" :fields="[...]" @search="load" @reset="load" />
```

**逐行解析**：
- `defineEmits<{...}>()` 用 TS 声明"我这个组件会 emit 哪些事件、每个事件带什么参数"。这就是**类型化的 emit**——面试爱问 Vue 3 相对 Vue 2 的进步之一。
- `(e: 'search'): void` 表示 `search` 事件不带参数；`(e: 'update:modelValue', next: ...)` 表示这个事件带一个 `next` 参数。
- 类型化的好处：父组件写 `@searchh="..."`（拼错）或 `emit('serch')`（拼错）时，TypeScript / IDE 直接报错，而不是运行时静默失效。
- 父组件用 `@事件名="处理函数"` 监听。`@search="load"` = 子组件 `emit('search')` 时调父组件的 `load()`。

**坑**：
- 事件名大小写：模板里推荐 **kebab-case**（`@update:model-value`），JS 里用 camelCase（`update:modelValue`）。Vue 会自动转换，但混用容易懵。cp6.web 模板里用 `@update:model-value`，`defineEmits` 里写 `'update:modelValue'`——正是这个约定。
- 别在 `emit` 里传 `props`：`emit('x', props.obj)` 把父自己的对象又传回给父，容易造成"同一个引用被两边改"。CpFilterBar 传的是**新对象** `{ ...props.modelValue }`。

**面试问答**：
> **Q：`defineEmits` 相比 Vue 2 的 `this.$emit` 好在哪？**
> A：① 显式声明——组件对外的"事件契约"一目了然，别人看 `defineEmits` 就知道能监听哪些事件；② 类型安全——事件名和参数类型都受 TS 检查，拼错编译期就报；③ 配合 `<script setup>` 拿到的是一个普通函数 `emit`，不依赖 `this`，逻辑更好抽取和测试。

### 5.1.3 v-model 双向绑定（自定义组件实现 + 多 v-model）

**概念（类比后端）**：`v-model` 是"双向绑定"的语法糖。你可以理解成一个**读写属性（property）**——外面既能读它当前值，也能写新值进去。它本质上 = **一个 prop（传值下去）+ 一个 emit（改了报上来）** 的组合。

**Vue 3 的约定**（务必记牢，面试常问）：
- 默认 `v-model` ⇔ prop 名 `modelValue` + 事件名 `update:modelValue`。
- 具名 `v-model:foo` ⇔ prop 名 `foo` + 事件名 `update:foo`。
- 一个组件可以有**多个** `v-model`（Vue 3 特性，Vue 2 只能一个 `v-model` + 多个 `.sync`）。

**真实代码 A —— 手写 prop + emit 实现 v-model**（`CpFilterBar.vue`）：

父用法：`<CpFilterBar v-model="query" ... />`
组件内部：
```ts
const props = defineProps<{ modelValue: Record<string, unknown>; ... }>()  // 接 v-model 的值
const emit  = defineEmits<{ (e: 'update:modelValue', next): void; ... }>()  // 报回改动
function setField(key, value) {
  emit('update:modelValue', { ...props.modelValue, [key]: value })         // 改了 → emit
}
```
这就是 `v-model` 的"真身"：`v-model="query"` 被 Vue 编译成 `:model-value="query" @update:model-value="query = $event"`。所以只要你的组件收 `modelValue` prop、发 `update:modelValue` 事件，父就能用 `v-model` 绑它。

**真实代码 B —— `defineModel` 语法糖（Vue 3.4+，cp6.web 已用）**（`src/components/VolForm.vue` 第 90 行）：

```ts
const visible = defineModel<boolean>('visible', { default: false })
```

父组件用法（`src/components/VolTable.vue` 第 188 行）：

```html
<VolForm v-model:visible="formVisible" ... />
```

**逐行解析（defineModel 是重点）**：
- `defineModel<boolean>('visible', { default: false })` 一行就搞定了"声明 `visible` prop + 声明 `update:visible` 事件 + 返回一个可读写 ref"。
- 返回值 `visible` 是个 **ref**：组件内部 `visible.value = false` 会自动 `emit('update:visible', false)` 通知父；父组件 `v-model:visible="formVisible"` 里的 `formVisible` 就同步变了。
- `'visible'` 是这个 model 的名字（对应 `v-model:visible`）。不传名字就是默认的 `modelValue`。
- 这是**手写 prop+emit 的极简替代**，省掉了 CpFilterBar 里那一堆样板。cp6.web 里新组件（VolForm、PubImportDialog）用 `defineModel`，老一点的组件（CpFilterBar、MasterReferenceDialog）还是手写 prop+emit——两种你都要认识。

**真实代码 C —— 多个 v-model（对话框 + 分页同时双向绑定）**（`VolTable.vue` 第 163-164、188 行）：

```html
<el-pagination
  v-model:current-page="page"
  v-model:page-size="pageSize"
/>
...
<VolForm v-model:visible="formVisible" ... />
```

一个 `el-pagination` 上挂了两个 v-model：`current-page` 和 `page-size` 各自双向绑定。这就是 Vue 3 的"多 v-model"。

**为什么对话框可见性适合用 v-model？** 因为"弹窗开/关"这个状态，父组件要能控制（点按钮打开 → 父设 `formVisible=true`），弹窗内部也要能改（点关闭/提交成功 → 组件把 `visible.value=false`）。双方都要读写 → 天生就是 `v-model` 场景。

**坑**：
- `v-model` 的默认约定名在 Vue 2 是 `value` + `input`，Vue 3 改成了 `modelValue` + `update:modelValue`。面试常拿这个考你 Vue 2/3 差异。
- `defineModel` 需要 Vue 3.4+；cp6.web 是 3.5，能用。老项目不能用就得手写 prop+emit。
- 别在子组件里对 `modelValue` prop 直接赋值（`props.modelValue = x`）——那是改 props，报错。要么走 emit，要么用 `defineModel` 返回的那个 ref。

**面试问答**：
> **Q：自定义组件怎么支持 v-model？多个 v-model 怎么写？**
> A：默认 v-model = 接 `modelValue` prop + emit `update:modelValue` 事件；具名 v-model:foo = 接 `foo` prop + emit `update:foo`。一个组件挂多个 v-model 就用多个具名的即可，如 `v-model:current-page` + `v-model:page-size`。Vue 3.4+ 可以用 `defineModel('foo')` 一行代替手写 prop+emit，它返回一个可读写 ref，改 `.value` 会自动 emit。

### 5.1.4 provide / inject（跨层级）

**概念（类比后端）**：provide/inject 就是 Vue 内建的**依赖注入（DI）**。祖先组件 `provide('key', value)` 把某个东西"注册"进去，任意深度的后代 `inject('key')` 就能"注入"取到——**中间层组件不需要一层层往下传 props**。这跟 ASP.NET Core 的 DI 容器（`services.AddScoped<IFoo>()` → 构造函数注入 `IFoo`）是一个思想：提供方和消费方解耦，中间不用手动搬运。

**它解决什么痛点？—— prop drilling（逐层透传）**：

```
A (有数据 theme)
 └ B (自己不用 theme，但得接 props 再传给 C)  ← 无辜的中间层
    └ C (自己不用，再传给 D)                   ← 又一个无辜中间层
       └ D (终于用到 theme)
```

如果只用 props，B、C 明明不关心 `theme`，却被迫声明它、透传它——这叫 prop drilling，层数一多就是噩梦。provide/inject 让 A 直接 `provide`，D 直接 `inject`，B/C 完全不用管。

**cp6.web 的真实情况（诚实说明）**：

我在 `C:\CP6\cp6.web\src` 全量搜索 `provide(`，**没有找到业务代码里的显式使用**（`Grep "provide(" → No matches`）。这本身是一个**很值得讲的工程决策**，面试时说出来会加分：

> cp6.web 这种规模的应用，跨层级共享的状态（当前用户权限、当前语言、act-as 代理态、平台管理员身份）几乎都是**全局单例**性质，团队统一用 **Pinia** 来承载，而不是 provide/inject。原因：Pinia 有 devtools 可视化、有类型推导、能在任意 `.ts` 文件（不只是组件）里 `useXxxStore()` 调用、还能做持久化——比 provide/inject 更适合"应用级全局状态"。

那 provide/inject 什么时候才该用？

- **组件库/设计系统内部**：一个 `<el-form>` 要把 `size`、`disabled`、校验规则"暗中"传给它内部任意深度的 `<el-form-item>` / `<el-input>`——用 provide/inject 最自然（Element Plus 内部大量这么做）。
- **一棵局部子树共享上下文**，但这个上下文**不是全局**、不该进 Pinia（比如一个复杂向导 Wizard 组件把"当前步骤"provide 给它的各个 Step 子组件）。

**标准写法**（背下来即可，语法你要会写）：

```ts
// 祖先组件
import { provide, ref } from 'vue'
const theme = ref('dark')
provide('theme', theme)          // key 用字符串，或用 InjectionKey<T> 做类型安全

// 任意后代组件
import { inject } from 'vue'
const theme = inject('theme', 'light')  // 第二参是"没人 provide 时"的默认值
```

**坑**：
- **默认非响应式陷阱**：provide 一个普通值（`provide('n', 1)`）后代拿到的就是死值。要响应式，provide 一个 `ref`/`reactive`（cp6.web 若用会 provide 一个 ref）。
- **来源不透明**：inject 拿到的东西"从哪来的"不明显，滥用会让代码难追踪——这也是 cp6.web 倾向 Pinia 的原因之一（Pinia 至少 `useXxxStore` 名字清清楚楚）。
- **建议单向**：约定"只有 provide 方能改值，inject 方只读"，或者 provide 方连同修改函数一起 provide 下去，避免后代乱改导致源头失控。

**面试问答**：
> **Q：provide/inject 和 Pinia 都能跨组件共享数据，怎么选？**
> A：provide/inject 是"沿组件树某条分支"注入上下文，适合组件库内部、局部子树共享的场景（如表单向导把步骤状态传给子步骤），且中间层不用透传。Pinia 是"应用级全局单例状态"，跟组件树无关，任意组件甚至任意 `.ts` 文件都能取，有 devtools、类型推导、持久化。经验法则：**全局状态用 Pinia，局部子树上下文用 provide/inject，父子直连用 props/emit**。我们这套系统权限、语言、代理态都是全局的，所以统一走 Pinia，业务代码里几乎不用 provide/inject。

### 5.1.5 模板引用 ref + `defineExpose`

**概念（类比后端）**：模板 ref 让父组件拿到子组件（或 DOM 元素）的**实例引用**，从而**直接调它的方法**——类似你拿到一个对象引用后直接调它的 public 方法。但组件默认对外是"封闭"的（`<script setup>` 里的东西默认私有），子组件必须用 `defineExpose` **显式暴露**哪些方法/属性可以被父调用（相当于把某些成员标成 public）。

**真实代码**（`src/components/templates/CpListPage.vue` 用了 defineExpose；`VolForm.vue` 第 94 行拿 DOM ref）：

```ts
// VolForm.vue —— 拿到 el-form 实例，用于触发校验
const formRef = ref<FormInstance>()
...
async function handleSubmit() {
  await formRef.value?.validate()   // 直接调 Element Plus 表单的 validate()
  ...
}
```

模板里：`<el-form ref="formRef" ...>` —— `ref="formRef"` 把这个 `el-form` 的实例塞进 `formRef.value`。

子组件对外暴露方法的写法（`defineExpose` 在 CpListPage / 各 Step 组件里用）：

```ts
// 子组件
function reload() { /* 重新拉数据 */ }
function focus() { inputRef.value?.focus() }
defineExpose({ reload, focus })   // 只有这两个方法父组件能调，别的都私有
```

父组件：
```html
<CpListPage ref="listRef" ... />
```
```ts
const listRef = ref()
function onSaved() {
  listRef.value?.reload()   // 保存成功后，让列表刷新
}
```

**逐行解析**：
- `ref="formRef"` 写在模板元素上 + `const formRef = ref()` 声明——两者靠**同名**关联，Vue 挂载后自动把实例填进 `formRef.value`。
- `<el-form>` 是组件，`formRef.value` 就是 `el-form` 组件实例，能调它 `defineExpose` 出来的方法（Element Plus 暴露了 `validate` / `resetFields` 等）。
- 如果 `ref` 写在原生 DOM 上（`<input ref="x">`），`x.value` 就是那个 `HTMLInputElement`，能调 `.focus()`。
- **为什么要 defineExpose**：`<script setup>` 是天然封闭的——里面定义的变量/函数默认**不会**暴露给父组件的模板 ref。你必须 `defineExpose({...})` 白名单式地放出去。这是 Vue 3 的安全默认（Vue 2 里父能直接 `this.$refs.child.任意方法`，太开放）。

**坑**：
- **能用 props/emit 就别用 ref 调方法**。模板 ref 直接命令子组件，是"强耦合"，破坏单向数据流，只在"命令式操作"（聚焦、滚动、手动触发校验、播放动画、重新加载）时才用。cp6.web 用它就是为了"父让列表 reload""触发表单 validate"这类命令，不是传数据。
- `ref` 在**挂载后**才有值，`onMounted` 之前 `formRef.value` 是 `undefined`。所以代码里都是 `formRef.value?.validate()` 带可选链。
- `v-for` 里的 ref 在 Vue 3 要用函数 ref 或数组处理，跟单个 ref 不一样。

**面试问答**：
> **Q：什么时候用模板 ref + defineExpose，而不是 props/emit？**
> A：props/emit 是"数据通信"，ref 是"命令式调用"。当父组件需要**主动触发子组件的一个动作**——聚焦输入框、手动跑一次表单校验、让列表重新加载、控制子组件里的动画——这些不是"传数据"而是"下命令"，就用模板 ref 拿到子实例调方法。子组件必须 `defineExpose` 显式暴露可调的方法，因为 `<script setup>` 默认全私有。原则：优先 props/emit，ref 只用于命令式场景。

### 5.1.6 Pinia 共享状态（详见 5.2）

**概念**：当两个**互不相干**（不是父子、不是兄弟）的组件要读写同一份数据——比如"顶栏显示当前用户名"和"某个深层按钮判断当前用户有没有权限"——用 props/emit 传要传疯，这时就把数据放进 Pinia store，任意组件 `useXxxStore()` 直接取。这是 5.2 整节的主题。

cp6.web 真实例子：`usePermissionStore()`（当前用户的操作权限集合）被无数个页面的 `v-permission` 指令读取，用来决定按钮显不显示——不可能靠 props 一层层传，必须 Pinia。

### 5.1.7 组件通信方式决策表（★ 面试可直接背）

| 通信方式 | 方向 | 适用场景 | cp6.web 真实用例 | 关键约束 |
|---|---|---|---|---|
| **props** | 父 → 子 | 父给子传显示数据/配置 | `CpFilterBar` 收 `fields` / `modelValue` | 只读，不能改 |
| **emit** | 子 → 父 | 子把用户操作/事件报给父 | `CpFilterBar` emit `search` / `reset` | 用 `defineEmits` 类型化 |
| **v-model** | 父 ⇄ 子 | 双向绑定（表单值、弹窗开关、分页） | `VolForm` 的 `defineModel('visible')`、`el-pagination` 双 v-model | =props+emit 语法糖 |
| **provide/inject** | 祖先 → 后代 | 跨多层、局部子树共享上下文 | （业务代码未用；组件库内部/向导场景才用） | 默认非响应，需 provide ref |
| **模板 ref + defineExpose** | 父 → 子（命令） | 父主动调子的方法（focus/validate/reload） | `VolForm` 调 `formRef.validate()` | 仅命令式，子需 expose |
| **Pinia** | 任意 ⇄ 任意 | 全局/跨无关组件共享状态 | `usePermissionStore` 权限集 | 全局单例，见 5.2 |
| **事件总线（EventBus）** | 任意 → 任意 | （Vue 3 已不推荐，用 Pinia 或 mitt 替代） | 不用 | Vue 3 移除了 `$on/$emit` 总线 |

**决策口诀**：
- **父子直连** → props（下）/ emit（上）/ v-model（双向）。
- **父下命令给子** → 模板 ref + defineExpose。
- **跨很多层、局部子树** → provide/inject。
- **八竿子打不着的组件共享** → Pinia。
- **千万别用**：Vue 3 里手搓全局 EventBus（难追踪、易内存泄漏），要"广播"就用 Pinia 或专门的 mitt 库。

---

## 5.2 Pinia 深讲

> Pinia 是 Vue 官方现在推荐的状态管理库（取代 Vuex）。面试必问："为什么要状态管理？""Pinia 和 Vuex 区别？""storeToRefs 是干嘛的？"

### 5.2.1 概念：为什么需要状态管理？（prop drilling 痛点）

**类比后端**：状态管理库 ≈ 一个**应用级的单例服务容器**。后端你会把"当前登录用户""配置""缓存"放进 DI 容器的单例服务里，任何 Controller/Service 注入即用。前端的 Pinia 就是干这个的：把"跨页面、跨组件都要用"的数据放进 store，任何组件/任何 `.ts` 文件 `useXxxStore()` 即取。

**没有状态管理的痛点（面试要能具体说）**：

1. **prop drilling（逐层透传）**：顶层拿到用户信息，最底层的一个头像组件要用，中间隔着 6 层组件，全得声明+透传 `user` prop。
2. **兄弟组件同步难**：侧边栏改了语言，顶栏、内容区都要跟着变——它们是兄弟，没有直接通信通道，只能把状态提到共同父组件再往下传，父组件被塞满不属于它的状态。
3. **状态散落、来源不明**：同一份"当前用户权限"在多个组件各存一份 `ref`，改了一个另一个不知道，数据不一致。
4. **非组件代码拿不到**：`http.ts`（一个纯 `.ts` 文件，不是组件）要读"当前 act-as 代理身份"——它根本没有组件上下文，props/inject 都用不了。**只有 store 能被普通 ts 文件调用。**

cp6.web 的 `http.ts` 第 5、40 行就是第 4 点的活证据：

```ts
import { getActingAs } from '@/stores/oaActingAs'
...
const actingAs = getActingAs()
if (actingAs && config.url?.includes('/oa/')) {
  config.headers['X-Acting-As'] = actingAs.userId    // 在 axios 拦截器（非组件）里读全局态
}
```

### 5.2.2 defineStore 两种风格：setup store vs option store

Pinia 定义 store 有两种写法，面试可能问"你们用哪种、为什么"。

**风格一：Setup Store（组合式，cp6.web 全用这种）**

看 `src/stores/counter.ts`（最简单的入门标本，全文）：

```ts
import { ref, computed } from 'vue'
import { defineStore } from 'pinia'

export const useCounterStore = defineStore('counter', () => {
  const count = ref(0)                                // state
  const doubleCount = computed(() => count.value * 2) // getter
  function increment() {                              // action
    count.value++
  }
  return { count, doubleCount, increment }
})
```

**逐行解析**：
- `defineStore('counter', () => {...})` —— 第一个参数 `'counter'` 是 store 的**唯一 id**（devtools 里显示、也是内部去重的 key）；第二个参数是一个**函数**（这就是 setup store 风格，跟组件的 `setup()` 长得一模一样）。
- `const count = ref(0)` —— 相当于 Vuex 的 **state**。
- `const doubleCount = computed(...)` —— 相当于 **getter**（派生状态）。
- `function increment()` —— 相当于 **action**（改状态的方法）。setup store 里**同步/异步 action 写法完全一样**，都是普通函数（Vuex 里 mutation 和 action 还分家，Pinia 不分）。
- `return { count, doubleCount, increment }` —— **必须 return** 你想暴露的东西，没 return 的就是私有的。

一句话：**setup store 就是把组件的 `<script setup>` 那套（ref/computed/function）搬进 `defineStore`。** 你上一章学的组合式 API，在这里 100% 复用。

**风格二：Option Store（选项式，像 Vuex）**——cp6.web 没用，但你要认识：

```ts
export const useCounterStore = defineStore('counter', {
  state: () => ({ count: 0 }),
  getters: { doubleCount: (s) => s.count * 2 },
  actions: { increment() { this.count++ } },   // 注意这里用 this
})
```

**两种对比**：

| | Setup Store（cp6.web 用） | Option Store |
|---|---|---|
| 写法 | 函数，用 ref/computed/function | 对象，用 state/getters/actions |
| 访问自身 | 直接变量名 | 靠 `this` |
| 灵活性 | 高（能用任意组合式 API、watch、私有变量） | 中规中矩 |
| 与组件一致性 | 完全一致（学一套即可） | 另一套心智 |
| TypeScript | 推导更自然 | 也 OK |

**cp6.web 为什么全用 setup store？** 因为整个前端已经是组合式 API 心智，setup store 让"组件里怎么写状态"和"store 里怎么写状态"**完全统一**，还能在 store 里用 `watch`、定义私有变量（不 return 就是私有）。

### 5.2.3 精读权限 store（和后端 RoleAction 权限体系对接的真实标本）

`src/stores/permission.ts`（全文，这是本节的核心标本——它连接了前端 UI 和后端权限体系）：

```ts
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { rolePermApi } from '@/api/sys/rolePerm'

/**
 * PUB 章02/04 前端权限 store。
 * actionKeys = 当前用户全部操作键（"menuKey:action"），供 v-permission 判定。
 * 注意：前端隐藏只是 UX，真正强校验在后端 [RequirePermission]（双保）。
 */
export const usePermissionStore = defineStore('permission', () => {
  const actionKeys = ref<Set<string>>(new Set())
  const loaded = ref(false)

  async function loadMyActions() {
    try {
      const keys = await rolePermApi.myActions()
      actionKeys.value = new Set(keys)
      loaded.value = true
    } catch {
      // 未登录/失败：保持空集，不阻塞页面
    }
  }

  function has(key: string): boolean {
    return actionKeys.value.has(key)
  }

  function reset() {
    actionKeys.value = new Set()
    loaded.value = false
  }

  return { actionKeys, loaded, loadMyActions, has, reset }
})
```

**逐行解析（把前端权限和后端串起来）**：
- `actionKeys = ref<Set<string>>(new Set())` —— 用 **Set** 存当前用户所有"操作键"。每个键形如 `"wms/stock:query"`（菜单键 : 动作），对应后端数据库 `RoleAction` 表里"这个角色被授予了哪些操作"。用 Set 是因为 `has()` 判定 O(1)，页面上成百上千个按钮都要判权限，性能关键。
- `loaded` —— 标记"权限有没有加载过"，避免重复请求、也让 UI 知道"还在加载 / 已就绪"。
- `loadMyActions()` —— **action**：调后端 API `rolePermApi.myActions()` 拉当前用户的操作键集合。登录成功后调一次。`catch` 里**故意吞掉错误**——拿不到权限就当空集（所有按钮都不显示），绝不因为权限接口失败就白屏卡死。
- `has(key)` —— **getter 式的判定方法**：给一个操作键，返回用户有没有。这个方法被全局的 `v-permission` 指令调用：`v-permission="'wms/stock:apply'"` 的按钮，指令内部就是 `usePermissionStore().has('wms/stock:apply')`，false 就把按钮藏了。
- `reset()` —— 退出登录时清空，防止下一个用户看到上一个用户的权限缓存。
- 注释第 7-8 行是**面试金句**：**"前端隐藏只是 UX，真正强校验在后端 [RequirePermission]（双保）。"** 前端藏按钮只是不让用户看见/误点，一个懂行的人照样能直接发 HTTP 请求——所以后端每个接口还有 `[RequirePermission]` 特性做真正的拦截。前端权限 = 体验优化，后端权限 = 安全边界。这条你面试一定要主动讲，体现你懂"前端安全不可信"。

**这就是一个真实的前后端权限闭环**：
```
用户登录 → 前端 loadMyActions() → GET /api/sys/role-perm/my-actions
        → 后端查 RoleAction 表返回该用户的操作键集合
        → 存进 Pinia permission store 的 actionKeys(Set)
        → v-permission 指令读 store.has(key) 决定按钮显隐（UX 层）
        → 用户真点了按钮发请求 → 后端 [RequirePermission] 再查一次（安全层）
```

### 5.2.4 state / getters / actions 小结

- **state**：`ref` / `reactive` 声明的响应式数据（`actionKeys`、`loaded`、`count`）。
- **getters**：`computed` 派生（`doubleCount`），或像 `has()` 这样的只读判定方法。
- **actions**：改 state 的函数（`loadMyActions`、`increment`、`reset`），同步异步都行。

### 5.2.5 storeToRefs：解构会丢响应性的坑（★ 面试高频）

这是 Pinia 面试**最爱考的一个坑**。看代码：

```ts
import { usePermissionStore } from '@/stores/permission'
import { storeToRefs } from 'pinia'

const permStore = usePermissionStore()

// ❌ 错误：直接解构，loaded / actionKeys 变成"快照"，丢了响应性！
const { loaded, actionKeys } = permStore
// 之后 store 里 loaded 变 true，这里的 loaded 还是 false，视图不更新

// ✅ 正确：用 storeToRefs 解构 state / getters，保持响应性
const { loaded, actionKeys } = storeToRefs(permStore)
// loaded 现在是 Ref，store 变它就变；模板里 loaded 自动解包

// ✅ action（方法）直接从 store 解构没问题，方法不需要响应性
const { loadMyActions, has, reset } = permStore
```

**为什么直接解构会丢响应性？**
- Pinia 的 store 实例是一个 `reactive` 对象。`reactive` 对象**一旦被解构**（`const { x } = obj`），`x` 就变成了那一刻的普通值/引用，脱离了响应式代理——之后 `obj.x` 再变，`x` 不会跟着变。这跟第 4 章讲的"`reactive` 不能解构、要用 `toRefs`"是**同一个原理**。
- `storeToRefs()` 专门解决这个：它把 store 里的 **state 和 getter** 转成一组 `ref`（引用还连着 store），解构出来仍是响应式的；同时它**跳过 action**（方法不需要也不该被转 ref）。

**记忆法**：**解构 state/getter 用 `storeToRefs`；解构 action 直接从 store 拿。**

**面试问答**：
> **Q：`const { count } = useStore()` 有什么问题？**
> A：store 是 reactive 对象，直接解构会丢响应性——`count` 变成解构那一刻的静态值，store 后续更新它不会变，视图也不更新。要用 `storeToRefs(store)` 解构 state 和 getter，它返回的是 ref，保持响应式。action（方法）不受影响，可以直接从 store 解构。根因和 `reactive` 对象不能直接解构、要用 `toRefs` 是一样的。

### 5.2.6 store 之间互相调用

一个 store 可以调另一个 store——直接在 action 里 `useOtherStore()` 即可：

```ts
export const useAuthStore = defineStore('auth', () => {
  const permStore = usePermissionStore()   // 在 action/函数里取另一个 store
  async function logout() {
    await http.post('/auth/logout')
    permStore.reset()                       // 调另一个 store 的 action
    resetRoutes()
  }
  return { logout }
})
```

cp6.web 里典型链路：登录成功后 auth 相关逻辑会调 `usePermissionStore().loadMyActions()` 把权限拉进来；退出时调 `permStore.reset()`。**注意**：在 setup store 顶层调 `useOtherStore()` 要确保 Pinia 已安装（一般在函数体/action 内调最安全）。

### 5.2.7 Vuex 对照（面试可能问）

| | Vuex（旧） | Pinia（新，官方现推荐） |
|---|---|---|
| 改状态 | 必须走 mutation（同步）+ action（异步）**两层** | action 一层搞定，同步异步都行 |
| 模块 | `modules` 嵌套，命名空间麻烦 | 每个 `defineStore` 就是一个独立 store，天然模块化 |
| TypeScript | 支持差，类型要手写一堆 | 类型自动推导，一等公民 |
| 组合式 API | 支持勉强 | 原生组合式（setup store） |
| `this` | option 风格靠 `this`，setup 里不好用 | setup store 无 `this`，纯函数 |
| 体积 | 较大 | 极小（~1KB） |
| 心智负担 | state/getters/mutations/actions 四件套 | state/getters/actions 三件套，且 setup 风格连这都省了 |

**一句话**：Pinia = 去掉 mutation 的、TypeScript 友好的、天然模块化的、组合式的 Vuex。Vue 3 新项目一律 Pinia。

---

## 5.3 vue-router 深讲

> 路由是 SPA 的骨架。面试必问："SPA 路由原理？""history 和 hash 区别？""路由懒加载？""路由守卫怎么做登录拦截？"

### 5.3.1 概念：SPA 路由原理（history vs hash）

**类比后端**：后端路由是"URL → Controller Action"的映射（`[Route("api/wms/stock")]`）。前端路由是"URL → 组件"的映射（`/wms/stock` → `StockQueryView.vue`）。区别在于：**后端每次导航是一次新的 HTTP 请求换整页；SPA（单页应用）不刷新页面，只是 JS 把 URL 改一改、把对应组件换一换，体验像原生 App。**

SPA 怎么做到"改 URL 但不刷新页面"？两种模式：

**history 模式（cp6.web 用的）**：
- URL 长这样：`https://app.com/wms/stock`（干净，像真实路径）。
- 靠浏览器的 **History API**（`history.pushState()`）改 URL 而不触发页面请求。
- **代价**：用户直接访问 `/wms/stock` 或按 F5 刷新时，浏览器会真的向服务器请求 `/wms/stock` 这个路径——但服务器上根本没有这个文件（SPA 只有一个 `index.html`）。所以**服务器必须配置"所有未匹配路径都回退到 index.html"**（nginx 的 `try_files $uri /index.html`），否则刷新就 404。这是 history 模式部署的头号坑。

**hash 模式**：
- URL 长这样：`https://app.com/#/wms/stock`（带 `#`）。
- `#` 后面的部分叫 hash，**改 hash 浏览器永远不发请求**（hash 本来是页内锚点用的）。
- 优点：不用服务器配置，刷新永远不 404（服务器只看到 `/`，`#` 后面它收不到）。
- 缺点：URL 丑（带 `#`），对 SEO 不友好。

**cp6.web 的选择**（`src/router/index.ts` 第 294-297 行）：

```ts
const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: staticRoutes
})
```

- `createWebHistory()` = **history 模式**（干净 URL）。对应的还有 `createWebHashHistory()`（hash 模式）。
- `import.meta.env.BASE_URL` = 部署基路径（Vite 注入，见 5.6），一般是 `/`。
- 因为用了 history 模式，cp6.web 部署时 nginx/容器必须配置 fallback 到 index.html（否则刷新 404）——这是它容器化部署的必备一环。

### 5.3.2 路由表结构 + 懒加载（精读 cp6.web 真实路由组织）

cp6.web 的路由组织有点特别，**很值得讲**——它是"菜单驱动的动态路由"，不是把所有路由写死。

**第一部分：路径 → 组件的映射表**（`router/index.ts` 第 6-184 行，节选）：

```ts
// 路由路径 → 组件的映射表（所有可能的页面）
const viewModules: Record<string, () => Promise<any>> = {
  '/dashboard': () => import('@/views/dashboard/DashboardView.vue'),
  // ───── WMS 倉庫管理 ─────
  '/wms/warehouse': () => import('@/views/wms/WarehouseListView.vue'),
  '/wms/stock': () => import('@/views/wms/StockQueryView.vue'),
  // ───── 财务 (Fin) ─────
  '/fin/account': () => import('@/views/fin/GlAccountView.vue'),
  '/fin/journal': () => import('@/views/fin/JournalEntryView.vue'),
  // ...（几百条，覆盖 PMS/OA/FIN/PUR/ERP/MES/WMS/Space 全模块）
}
```

**逐行解析——这里藏着"懒加载"这个必考点**：
- `() => import('@/views/wms/StockQueryView.vue')` —— 注意这是一个**返回 `import()` 的箭头函数**，不是 `import StockQueryView from '...'`（静态导入）。
- **静态 import**：打包时把该组件的代码**塞进主包**，首屏就得下载全部页面代码——几百个页面全塞进去，首屏包体积爆炸，加载慢。
- **动态 `import()`（懒加载 / 路由级代码分割）**：Vite/webpack 看到 `() => import(...)` 会把这个组件**单独打成一个 chunk 文件**，只有当用户**真正访问**这个路由时，才去下载对应的 chunk。首屏只加载当前页，其余按需。
- 效果：这套系统有几百个页面，但用户打开首页只下载首页的代码，点到"库存查询"才下载 `StockQueryView` 那个 chunk。这是大型 SPA 性能的关键，面试必答。

**第二部分：静态路由（不需要登录/不走布局壳的页面）**（第 187-292 行，节选）：

```ts
const staticRoutes: RouteRecordRaw[] = [
  { path: '/wf/todo', redirect: '/oa/inbox' },            // 重定向（老路径 → 新路径）
  { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue') },
  {
    path: '/sys/change-password',
    name: 'change-password',
    component: () => import('@/views/pms/ChangePasswordView.vue'),
    meta: { standalone: true, title: '修改密码' }          // ← 路由元信息 meta
  },
  {
    path: '/space/editor/:floorId',                        // ← 动态路由参数 :floorId
    name: 'space-editor',
    component: () => import('@/views/space/editor/FloorEditor.vue'),
    meta: { standalone: true, title: 'Space 编辑器' }
  },
  {
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    children: []                                           // ← 空！子路由动态填充
  }
]
```

**看点**：
- `redirect` —— 路由重定向（老 URL `/wf/todo` 自动跳到 `/oa/inbox`，兼容历史链接）。
- `meta: { standalone: true, title: '...' }` —— **路由元信息**（见 5.3.6）。`standalone` 是自定义标记，守卫用它判断"这个页面不走带侧边栏的布局"。
- `/space/editor/:floorId` —— **动态路由参数**（`:floorId` 是占位，`/space/editor/123` 里 `123` 就是 `floorId`，见 5.3.4）。
- `path: '/', name: 'layout', children: []` —— 这是**布局壳**（LayoutView：侧边栏+顶栏+内容区），它的 `children` 是**空的**，等登录后根据用户菜单**动态填充**。这是 cp6.web 路由的精髓。

### 5.3.3 动态路由：根据用户菜单权限生成路由

**为什么动态？** 这是个多租户权限系统——不同用户能看的菜单不同（管理员看全部，仓库工只看 WMS）。如果把所有路由写死，那没权限的用户虽然点不到菜单，但手动敲 URL 还是能进页面。cp6.web 的做法是：**登录后，后端返回该用户的菜单列表，前端据此动态注册路由**——没权限的页面**根本没注册进路由表**，敲 URL 也匹配不到。

`router/index.ts` 第 333-372 行 `addDynamicRoutes`：

```ts
export function addDynamicRoutes(menus: any[]) {
  // 先找到有 routePath、且在 viewModules 里有对应组件的菜单
  const routeMenus = menus.filter(m => m.routePath && viewModules[m.routePath])
  const firstRoute = routeMenus[0]?.routePath || '/login'   // 第一个有效页做默认跳转

  router.removeRoute('layout')            // 先移除旧的空 layout
  router.addRoute({                       // 重新加一个带子路由的 layout
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    redirect: firstRoute,                 // 访问 / 自动跳到第一个有权限的页
    children: [
      ...routeMenus.map(menu => ({
        path: menu.routePath.replace(/^\//, ''),   // 去掉开头 /，变相对路径
        name: menu.routePath.replace(/^\//, ''),
        component: viewModules[menu.routePath]      // 从映射表取懒加载组件
      })) as RouteRecordRaw[],
      ...platformChildren(),              // 平台管理页（带外，守卫另管）
      ...oaSubChildren()                  // OA 子页（非菜单，程序跳转用）
    ]
  })
  dynamicRoutesAdded = true
}
```

**逐行解析**：
- `menus.filter(m => m.routePath && viewModules[m.routePath])` —— 只注册"后端给了这个菜单**且**前端有对应组件"的路由，双向校验。
- `router.addRoute(...)` —— vue-router 的**动态添加路由** API（运行时往路由表里塞路由）。
- `menu.routePath.replace(/^\//, '')` —— 子路由的 path 要写相对（`wms/stock` 而非 `/wms/stock`），因为它挂在 `/` 布局下。
- `component: viewModules[menu.routePath]` —— 从前面那张映射表取出对应的懒加载函数。
- `redirect: firstRoute` —— 用户访问根 `/`，自动跳到他有权限的第一个页面。

配套还有 `resetRoutes()`（第 377-387 行）——退出登录时把 layout 的子路由全清空，防止下一个账号继承上一个的路由。

**这套设计的面试价值**：你可以主动讲"我们做的是**动态路由 + 菜单驱动权限**，登录后按后端返回的菜单动态 `addRoute`，无权限页面不注册，配合路由守卫和后端 `[RequirePermission]` 做到三层防护"——这是能力信号。

### 5.3.4 动态路由参数 / 查询参数

**动态段参数（params）**：URL 路径里的占位。
```ts
{ path: '/space/editor/:floorId', ... }   // 定义
```
组件里读取：
```ts
import { useRoute } from 'vue-router'
const route = useRoute()
const floorId = route.params.floorId       // /space/editor/123 → '123'
```

**查询参数（query）**：URL 里 `?` 后面的部分，`/wms/stock?warehouse=W01&page=2`。
```ts
const warehouse = route.query.warehouse     // 'W01'
const page = route.query.page               // '2'（注意：query 永远是字符串）
```

**params vs query 区别（面试常问）**：
- params 是路径的一部分（`/editor/:floorId`），**必填**（缺了匹配不上），语义上"资源标识"。
- query 是 `?key=value`，**可选**，语义上"筛选/分页/排序等参数"，改 query 通常不算换页面。
- cp6.web 里 `/space/viewer/:siteId`（哪个站点，params）配 `?floorId=x`（可选看哪层，query），第 273-278 行注释就写了"siteId 来自路由参数，floorId 可选 query"。

### 5.3.5 嵌套路由与 `<router-view>` + 编程式导航

**嵌套路由**：`children` 里的路由渲染在父路由组件的 `<router-view />` 里。cp6.web 的 LayoutView 大致是：

```html
<!-- LayoutView.vue（布局壳）结构示意 -->
<div class="layout">
  <Sidebar />               <!-- 侧边栏（菜单） -->
  <div class="main">
    <TopBar />              <!-- 顶栏 -->
    <router-view />         <!-- ★ 子路由组件在这里渲染（StockQueryView 等） -->
  </div>
</div>
```

- 访问 `/wms/stock` → 匹配 `layout`（`/`）+ 它的子路由 `wms/stock` → LayoutView 渲染在外层，`StockQueryView` 渲染进 LayoutView 的 `<router-view>`。侧边栏、顶栏不重绘，只换中间内容——这就是 SPA 布局复用。

**编程式导航**（在 JS 里跳转，不是点 `<router-link>`）：
```ts
import { useRouter } from 'vue-router'
const router = useRouter()

router.push('/wms/stock')                              // 跳转（压历史栈，可后退）
router.push({ path: '/space/editor/123' })             // 对象形式
router.push({ name: 'space-editor', params: { floorId: '123' } })  // 用命名路由
router.replace('/login')                               // 替换（不压栈，不能后退）
router.back()                                          // 后退
```

`http.ts` 第 73、84 行就在用编程式导航——401 时 `router.push('/login')` 把用户踢回登录页（见 5.4）。

**`useRouter` vs `useRoute` 别搞混**：`useRouter()` 拿**路由器实例**（用来 push/replace 跳转，是动词）；`useRoute()` 拿**当前路由信息**（读 params/query/meta，是名词）。

### 5.3.6 路由守卫（★★ 面试必考：登录检查 + 权限检查）

**概念（类比后端）**：路由守卫 = 前端的**中间件 / 过滤器**。就像 ASP.NET Core 的认证中间件在请求进 Controller 前拦一道（没登录踢走、没权限拒绝），路由守卫在**每次导航前**拦一道，决定"放行 / 改道 / 拦截"。

cp6.web 的全局前置守卫 `router.beforeEach`（`router/index.ts` 第 390-462 行，**这是本节最重要的标本**，完整逐段读）：

```ts
router.beforeEach(async (to, _from, next) => {
  // 登录态信号：httpOnly token 前端读不到，故用非敏感标志 cp6_authed
  const authed = localStorage.getItem('cp6_authed')

  // 1. 去登录页，放行
  if (to.path === '/login') { next(); return }

  // 1b. SSO 落地屏：回调后此刻尚无 cp6_authed，须无条件放行
  if (to.path === '/sso/landing') { next(); return }

  // 1c. 2FA 第二因素屏：密码已过但 auth cookie 未签发，无条件放行
  if (to.path === '/sys/2fa-challenge' || to.path === '/sys/2fa-enroll') { next(); return }

  // 2. 没有登录态，跳登录
  if (!authed) { next('/login'); return }

  // 2b. 平台超管区守卫（UX 层）：非平台超管访问 /platform/* → 回首页
  //     真闸门在后端 [RequirePlatformAdmin]
  if (to.path.startsWith('/platform/') && !usePlatformStore().isPlatformAdmin) {
    next('/'); return
  }

  // 3. 强制改密：登录态下若 mustChangePwd，除改密页自身外一律拦到改密页（防死循环）
  if (localStorage.getItem('cp6_mustChangePwd') === '1' && to.path !== '/sys/change-password') {
    next('/sys/change-password'); return
  }

  // 4. 独立窗口(popup)/改密页(standalone)：有登录态即可，不依赖动态菜单
  if (to.meta?.standalone) {
    await ensureNamespacesForPath(to.path)   // 确保该页语言包就绪
    next(); return
  }

  // 5. 有登录态但还没加载动态路由（页面刷新的情况）
  if (!dynamicRoutesAdded) {
    const menusStr = localStorage.getItem('menus')
    if (menusStr) {
      addDynamicRoutes(JSON.parse(menusStr))   // 从 localStorage 恢复菜单 → 重建路由
      next({ ...to, replace: true }); return    // 重新导航（路由刚加，需重新匹配）
    } else {
      next('/login'); return
    }
  }

  // 6. 路由已加载：确保命名空间就绪再放行
  await ensureNamespacesForPath(to.path)
  next()
})
```

**逐段精讲（面试就问这些）**：

- **守卫三参数 `(to, from, next)`**：`to` = 要去哪（目标路由），`from` = 从哪来（当前路由），`next` = 放行/改道的控制函数。
  - `next()` = 放行到 `to`；
  - `next('/login')` = 改道去登录页（拦截）；
  - `next({ ...to, replace: true })` = 重新导航到 to，但用 replace（不压历史栈）；
  - `next(false)` = 取消导航（留在原地）。
  - **务必每条分支都调恰好一次 `next`**，漏了导航就卡死，多了报警告。cp6.web 每个 `if` 都 `next(...); return` 配对，就是防这个。

- **步骤 1/1b/1c——白名单放行**：登录页、SSO 落地页、2FA 验证页，这些页面**本来就没登录态**（正在登录中），必须无条件放行，否则会被步骤 2 踢回登录，造成死循环。这是登录守卫最容易漏的边界。

- **步骤 2——登录检查**：没有 `cp6_authed`（登录标志）就 `next('/login')`。注意注释：真正的 token 是 **httpOnly Cookie**（`cp6_at`），JS 读不到（防 XSS 偷 token），所以前端另存一个**非敏感**的 `cp6_authed='1'` 只用来判断"登没登"。安全设计的活教材。

- **步骤 2b——权限检查（角色级）**：访问 `/platform/*` 但不是平台超管 → 踢回首页。注意注释强调"**真闸门在后端 [RequirePlatformAdmin]**"——和权限 store 一个道理，**前端守卫只是 UX，后端才是安全边界**。这句话面试必讲。

- **步骤 3——业务规则拦截**：强制改密。用户必须改密时，除了改密页本身，去哪都拦到改密页。`to.path !== '/sys/change-password'` 这个条件是**防死循环**（不排除改密页自己，就会"拦到改密页→又触发守卫→又拦到改密页"无限循环）。

- **步骤 4——meta 分流**：`to.meta?.standalone` 为真的页面（弹窗、改密页）走简化路径，不依赖动态菜单。展示了 **meta 元信息驱动守卫逻辑**。

- **步骤 5——刷新恢复（重点难点）**：SPA 刷新后 JS 全部重跑，内存里的动态路由**没了**（`dynamicRoutesAdded=false`）。但用户明明登录着（cookie 还在、localStorage 的 `menus` 还在）。所以守卫从 `localStorage` 读回菜单，`addDynamicRoutes` 重建路由，再 `next({ ...to, replace: true })` 重新走一遍导航（因为刚加的路由需要重新匹配）。**这是"刷新不掉登录、不 404"的关键**，面试问"刷新后动态路由丢了怎么办"就答这段。

- **`async` 守卫 + `await ensureNamespacesForPath`**：守卫是 `async` 的，进页面前 `await` 确保**该页面需要的语言包已加载**（见 5.8 i18n），加载完才 `next()`。展示了守卫可以做异步前置准备。

**守卫种类小结**（面试可能追问）：
- 全局前置 `router.beforeEach`（cp6.web 用的，最常见，做登录/权限）；
- 全局解析 `router.beforeResolve`；
- 全局后置 `router.afterEach`（无 `next`，常用来改页面标题、埋点）；
- 路由独享 `beforeEnter`（写在单条路由上）；
- 组件内 `onBeforeRouteLeave` / `onBeforeRouteUpdate`（如"表单没保存别离开"）。

### 5.3.7 路由元信息 meta

`meta` 是你挂在路由上的**自定义数据袋**，守卫和组件都能读。cp6.web 的用法：

```ts
meta: { standalone: true, title: '修改密码' }
```
- `standalone: true` —— 自定义标记，守卫据此判断"这页不走带侧边栏的布局，有登录态就放行"。
- `title` —— 页面标题（可在 `afterEach` 里 `document.title = to.meta.title` 设置浏览器标签标题）。

**meta 典型用途**：`requiresAuth`（是否要登录）、`roles`（允许的角色）、`title`（标题）、`keepAlive`（是否缓存组件）、`layout`（用哪个布局）。它让"路由的策略"数据化，守卫读 meta 决策，比在守卫里写死一堆 `if (path === ...)` 优雅。

**面试问答**：
> **Q：怎么用 vue-router 实现"未登录跳登录页 + 无权限拦截"？**
> A：用全局前置守卫 `router.beforeEach((to, from, next) => {...})`。先放行登录页等白名单页（否则死循环）；读登录标志，没登录 `next('/login')`；再按 `to.meta` 或路径判断权限，无权限 `next('/')` 或 `next(false)`；都通过 `next()` 放行。我们真实项目里还处理了刷新后动态路由丢失（从 localStorage 恢复菜单重建路由再 `next({...to, replace:true})`），以及"前端守卫只做 UX、后端 `[RequirePermission]` 才是真安全边界"的双层设计。

---

## 5.4 axios 与 API 层设计（本章主标本 http.ts 逐行）

> 这是全章的枢纽。`http.ts` 是前端所有请求的"总闸"，把它讲透，你就懂了前后端联调的一切：认证、CSRF、token 刷新、错误统一处理、多语言错误码。面试官若问"你们前端怎么统一处理请求？"这一节就是满分答案。

### 5.4.1 概念：为什么要封装 axios？

**类比后端**：axios 实例封装 ≈ 后端一个配好的 `HttpClient` + 一套统一的请求/响应管道（middleware）。你不会在每个 Controller 里手写连接串、手动加认证头、手动 try-catch 每个异常——你配一次，全局生效。前端同理：不会在每个页面手写 `fetch`、手动拼 baseURL、每次手动处理 401——封装一个 `http` 实例，配好拦截器，全项目复用。

**拦截器（interceptor）就是 axios 的"中间件"**：
- **请求拦截器**：请求发出**前**统一加工（加认证头、加 CSRF token、加 loading）。
- **响应拦截器**：响应回来**后**统一加工（拆数据、统一错误提示、401 自动刷新 token）。

### 5.4.2 逐行精读 `src/api/http.ts`（全文 101 行）

**第一段：导入 + 工具函数（1-21 行）**

```ts
import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'
import i18n from '@/i18n'
import { getActingAs } from '@/stores/oaActingAs'

// 模块级取全局 t（i18n↔http 运行时循环引用，仅在拦截器回调运行期取用，安全）
const t = (k: string) => (i18n.global as any).t(k)

// 从 document.cookie 读指定名字的值（CSRF 双提交：cp6_csrf 非 httpOnly，JS 可读）
function getCookie(name: string): string {
  const m = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'))
  return m && m[1] ? decodeURIComponent(m[1]) : ''
}

// 清登录态信号（httpOnly token 由后端清；前端仅清非敏感标志）
function clearAuthSignal() {
  localStorage.removeItem('cp6_authed')
  localStorage.removeItem('cp6_mustChangePwd')
}
```

**解析**：
- 导入 `ElMessage`（Element Plus 全局提示，用来弹错误 toast）、`router`（401 时跳登录）、`i18n`（翻译错误码）、`getActingAs`（读代理身份 store）。
- `const t = (k) => i18n.global.t(k)` —— 在**非组件**的 ts 文件里取 i18n 翻译函数。注释解释了 i18n 和 http 互相 import 是循环引用，但因为只在**拦截器回调运行时**（那时模块都加载完了）才调用，不是模块加载时就调，所以安全。这是工程细节，能讲出来显功力。
- `getCookie` —— 手动解析 `document.cookie`。为什么？因为 **CSRF token（`cp6_csrf`）是"非 httpOnly"的 Cookie，JS 能读**——这是 CSRF 双提交防护的设计（见下）。
- `clearAuthSignal` —— 登出/token 失效时清前端的登录标志（真 token 在 httpOnly cookie，前端删不了，得后端清）。

**第二段：创建 axios 实例（23-28 行）**

```ts
const http = axios.create({
  baseURL: '/api',          // 通过 Vite 代理转发到后端
  timeout: 10000,
  withCredentials: true     // 携带 httpOnly Cookie（cp6_at/cp6_rt/cp6_csrf）
})
```

**解析**：
- `baseURL: '/api'` —— 所有请求自动加 `/api` 前缀。页面里写 `http.get('/wms/stock')` 实际请求 `/api/wms/stock`。开发时 Vite 代理把 `/api` 转发到后端（见 5.6 vite.config），生产时 nginx 把 `/api` 转到后端容器。**前端代码永远只写相对路径 `/api/...`，不写后端真实地址**——环境无关，这是关键设计。
- `timeout: 10000` —— 10 秒超时，防请求永久挂起。
- `withCredentials: true` —— **跨请求携带 Cookie**。因为认证 token 存在 httpOnly Cookie（`cp6_at` 访问令牌、`cp6_rt` 刷新令牌、`cp6_csrf`），不加这个，浏览器不会带 Cookie，后端认不出登录态。httpOnly 是为了 JS 偷不到 token（防 XSS）。

**第三段：请求拦截器——注入 CSRF 头 + act-as 头（30-45 行）**

```ts
http.interceptors.request.use((config) => {
  const method = (config.method || 'get').toLowerCase()
  if (method !== 'get' && method !== 'head' && method !== 'options') {
    const csrf = getCookie('cp6_csrf')
    if (csrf) {
      config.headers['X-CSRF-Token'] = csrf     // 非安全方法注入 CSRF 头
    }
  }
  const actingAs = getActingAs()
  if (actingAs && config.url?.includes('/oa/')) {
    config.headers['X-Acting-As'] = actingAs.userId   // OA 代理态
  }
  return config
})
```

**解析（CSRF 防护是面试加分项）**：
- 只对**非安全方法**（POST/PUT/DELETE/PATCH，会改数据的）注入 CSRF 头；GET/HEAD/OPTIONS（只读）不注入。这是 **CSRF 双提交 Cookie（double-submit cookie）** 模式：
  - 后端登录时下发一个 `cp6_csrf` Cookie（非 httpOnly，JS 可读）。
  - 前端每次改数据的请求，读这个 Cookie 值，塞进 `X-CSRF-Token` 请求头。
  - 后端校验"Cookie 里的 csrf" == "请求头里的 csrf"。
  - 为什么防 CSRF？跨站攻击页面能诱导浏览器**自动带上 Cookie**发请求，但它**读不到你的 Cookie 值**（跨域限制），也就没法塞对 `X-CSRF-Token` 头 → 后端校验失败 → 拦截。
- `X-Acting-As` —— OA 模块的"代理办公"：某人代另一人处理审批时，请求头带上"我在代谁"，且只对 `/oa/` 请求注入。这就是 5.2.1 说的"`http.ts` 这个非组件文件读 Pinia store"的实例。
- 拦截器**必须 `return config`**，否则请求发不出去。

**第四段：401 自动刷新 token 的并发控制（47-58 行）**

```ts
let refreshPromise: Promise<any> | null = null   // 模块级共享 refresh promise

function doRefresh() {
  if (!refreshPromise) {
    // 直接用底层 axios 发，避免再次进入响应拦截器的 401 处理
    refreshPromise = http.post('/auth/refresh').finally(() => {
      refreshPromise = null
    })
  }
  return refreshPromise
}
```

**解析（这是并发处理的精妙点）**：
- 问题：token 过期时，页面上**同时**发了 5 个请求，全部 401。如果每个都去刷新 token，就会打 5 次 `/auth/refresh`——重复、还可能互相把 token 刷乱。
- 解法：用一个**模块级共享的 `refreshPromise`**。第一个 401 触发 `doRefresh()` 创建刷新 promise；后面 4 个 401 进来发现 `refreshPromise` 已存在，**复用同一个 promise**，不再重发。刷新完 `.finally` 把它置回 null。
- 这叫**请求去重 / 并发合并**，面试问"多个请求同时 401 怎么办"就答这个。

**第五段：响应拦截器——拆数据 + 统一错误 + 401 刷新重放（60-99 行）**

```ts
http.interceptors.response.use(
  (response) => response.data,          // ★ 成功：直接返回 data，剥掉 axios 外壳
  async (error) => {
    const status = error.response?.status
    const config = error.config || {}
    const url: string = config.url || ''

    if (status === 401) {
      const isAuthEndpoint = url.includes('/auth/refresh') || url.includes('/auth/login')
      if (isAuthEndpoint || config._retried) {
        // refresh/login 本身 401，或已重放过 → 不再刷新，清登录态跳登录
        clearAuthSignal()
        router.push('/login')
        if (!isAuthEndpoint) ElMessage.error(t('登录已过期，请重新登录'))
        return Promise.reject(error)
      }
      // 否则：自动 refresh 一次再重放原请求
      try {
        await doRefresh()
        config._retried = true
        return await http(config)        // 重放原请求（拦截器会返回 response.data）
      } catch (refreshErr) {
        clearAuthSignal()
        router.push('/login')
        ElMessage.error(t('登录已过期，请重新登录'))
        return Promise.reject(refreshErr)
      }
    } else if (status === 409) {
      // 乐观锁冲突：交给调用方自己处理（弹对话框/重新拉取），这里不自动 toast
    } else {
      // 后端业务错误码（如 E-FIN-107）走 i18n 翻译为友好文案
      const raw = error.response?.data?.message
      ElMessage.error((raw ? t(raw) : '') || error.response?.data?.title || t('请求失败'))
    }
    return Promise.reject(error)
  }
)

export default http
```

**逐点解析（这是整个 http.ts 的高潮）**：

1. **成功回调 `(response) => response.data`**（第 62 行）——**极其重要**。axios 原始返回是 `{ data, status, headers, config, ... }` 一个大对象，真正的业务数据在 `.data` 里。这里统一 `return response.data`，**把外壳剥掉**。所以页面里 `const list = await stockApi.search()` 拿到的直接就是后端返回的业务数据，不用每次 `.data.data`。这也是为什么 5.4.3 里 `stock.ts` 的返回类型能直接写成 `WmsApi<...>`。

2. **401 处理——自动刷新 + 重放（核心）**：
   - 如果 401 的是 `/auth/refresh` 或 `/auth/login` 本身（刷新都失败了），或**已经重放过**（`config._retried`），说明真的登录失效了 → 清登录态、跳登录页。`_retried` 标志防止无限重放死循环。
   - 否则：`await doRefresh()`（用 token 刷新拿新 token），标记 `config._retried = true`，然后 `return await http(config)` **用原来的配置重发一次原请求**。用户完全无感——他的请求只是"慢了一点点"，token 已经在背后悄悄续上了。这就是"**无感刷新 / 静默续签**"，面试高频。

3. **409 特殊处理**——**乐观锁冲突**故意什么都不做（不弹 toast），注释说"交给调用方自己决定"。因为 409（两个人同时改一条数据）需要业务层弹专门的对话框（"数据已被他人修改，是否覆盖/重新加载"），全局统一 toast 反而碍事。**展示了"全局兜底 + 局部定制"的分层错误处理**。

4. **其他错误——业务错误码 i18n 翻译**：后端返回的 `message` 可能是 `E-FIN-107`（结构化错误码）。`t(raw)` 把它翻成当前语言的友好文案（"会计期间已关闭，无法记账"）。如果这个错误码没有对应翻译，`t()` 会**回退返回 key 本身**（安全，不会崩）。层层兜底：`翻译后的 message || 后端 title || '请求失败'`。这把**后端错误码体系和前端多语言体系打通了**——后端只发错误码，前端负责翻译展示，语言切换错误提示也跟着变。

**http.ts 整体价值总结（面试满分表述）**：
> 我们前端所有请求走一个封装的 axios 实例。请求拦截器负责 CSRF 双提交防护（改数据的请求从非 httpOnly cookie 读 csrf 塞进请求头）和代理态注入；响应拦截器统一剥离 data 外壳、把后端结构化错误码经 i18n 翻成当前语言的友好提示、并实现 401 无感刷新——多个请求同时过期时用共享 promise 合并成一次 refresh，刷新成功后自动重放原请求，重放失败才踢回登录。token 存 httpOnly cookie 防 XSS，前端只留一个非敏感登录标志。这一层把认证、安全、错误处理、多语言全收口了，页面代码只管调 API 拿数据。

### 5.4.3 API 模块化组织（一个 WMS 模块 API 文件全貌）

**概念**：不把请求散落在各页面，而是**按后端模块分文件**集中管理。`api/` 下按 `wms/`、`sys/`、`fin/` 等子目录组织，每个文件导出一个 API 对象。这样"某个接口地址变了"只改一处，页面调的是语义化方法名。

`src/api/wms/stock.ts`（全文，库存模块 API）：

```ts
import http from '../http'
import type {
  Stock, StockTransaction, StockSearchQuery,
  StockMovementRequest, StockMoveRequest, WmsApi, WmsPaged,
} from '@/types/wms/wms'

export const stockApi = {
  /** 在庫照会（库存查询） */
  search(query: StockSearchQuery = {}) {
    return http.get<any, WmsApi<WmsPaged<Stock>>>('/wms/stock', { params: query })
  },

  /** 在庫の変動履歴 */
  history(stockId: string, days = 90) {
    return http.get<any, WmsApi<{ stock: Stock; transactions: StockTransaction[] }>>(
      `/wms/stock/${stockId}/history`, { params: { days } },
    )
  },

  /** 在庫変動 1 件適用 */
  apply(req: StockMovementRequest) {
    return http.post<any, WmsApi<{ txnNo: string }>>('/wms/stock/apply', req)
  },

  /** 棚移動（货位转移） */
  move(req: StockMoveRequest) {
    return http.post<any, WmsApi<{ outTxnNo: string; inTxnNo: string }>>('/wms/stock/move', req)
  },

  // ... transactions / setQcStatus / setQcStatusByWorkOrder
}
```

**逐行解析**：
- `import http from '../http'` —— 用的就是 5.4.2 那个封装好的实例（自带拦截器）。
- `stockApi` 是个**对象**，每个方法对应一个后端接口。页面里 `import { stockApi } from '@/api/wms/stock'` 然后 `stockApi.search({ warehouse: 'W01' })`——**语义清晰，不用记 URL**。
- `search(query = {})` —— `http.get('/wms/stock', { params: query })`，`params` 会被 axios 拼成 query string（`/api/wms/stock?warehouse=W01&page=2`）。
- `apply(req)` —— `http.post('/wms/stock/apply', req)`，第二参 `req` 是请求体（JSON body）。
- `` `/wms/stock/${stockId}/history` `` —— 模板字符串拼动态路径（RESTful 风格）。
- **TypeScript 类型化响应**：`http.get<any, WmsApi<WmsPaged<Stock>>>(...)` 的第二个泛型 `WmsApi<WmsPaged<Stock>>` 就是**这个接口返回数据的类型**。因为拦截器 `return response.data` 剥了壳，所以这里的类型直接是业务数据类型。`WmsPaged<Stock>` 是"分页的库存列表"，`WmsApi<T>` 是统一响应包装。有了类型，页面里 `const res = await stockApi.search()`，`res` 就有完整类型提示（`res.items[0].productCd` 有智能补全，拼错编译报错）。

**这套 API 层的好处（面试可讲）**：
1. 接口地址集中，改一处即可；
2. 语义化方法名，页面可读性高；
3. TypeScript 端到端类型（后端字段变了，前端编译期就红）；
4. 页面不碰 http/axios 细节，只依赖 API 对象——关注点分离。

**面试问答**：
> **Q：你们前端 API 层怎么组织的？**
> A：三层：`http.ts` 封装 axios 实例和拦截器（认证/CSRF/错误/刷新统一收口）；`api/模块/` 下按后端模块分文件，每个导出一个 API 对象，方法名语义化、返回类型用 TS 泛型标注；页面只 import API 对象调方法。好处是接口地址集中管理、端到端类型安全、页面与网络细节解耦。

---

## 5.5 CSS 专题（JD 明确要求）

> JD 白纸黑字写"熟悉 CSS"。面试可能现场问盒模型、Flex 居中、scoped 原理。这节是硬功夫，配 cp6.web 真实样式。

### 5.5.1 盒模型（box model）

每个元素都是一个盒子，从内到外四层：**content（内容）→ padding（内边距）→ border（边框）→ margin（外边距）**。

```
┌─────────── margin（透明，元素之间的间距）───────────┐
│  ┌──────── border（边框）─────────┐                │
│  │  ┌───── padding（内边距）────┐  │                │
│  │  │  ┌── content（内容）──┐  │  │                │
│  │  │  │   width × height  │  │  │                │
│  │  │  └───────────────────┘  │  │                │
│  │  └───────────────────────┘  │                │
│  └─────────────────────────────┘                │
└──────────────────────────────────────────────────┘
```

**两种盒模型（面试必考）**：
- `box-sizing: content-box`（W3C 默认）：`width` 只算 content，实际占宽 = width + padding + border。你设 `width:100px; padding:10px`，实际占 120px——容易算错。
- `box-sizing: border-box`（现代项目标配）：`width` 包含 content+padding+border。设 `width:100px` 就是占 100px，padding/border 往里挤。**所见即所得，推荐**。现代 CSS reset 通常全局 `* { box-sizing: border-box }`。

**面试问答**：
> **Q：`box-sizing: border-box` 和 `content-box` 区别？**
> A：`content-box`（默认）下 `width` 只算内容宽，加 padding/border 会让盒子实际变宽，布局易算错；`border-box` 下 `width` 包含 padding 和 border，设多宽盒子就多宽，padding 往里挤，布局直观。现在几乎都全局设 `border-box`。

### 5.5.2 Flex 布局速成（★ 最重要，面试必问居中）

**概念**：Flexbox 是**一维**布局（一行 或 一列）。给容器 `display: flex`，它的直接子元素就成了"弹性项"，沿**主轴**排列。

**两根轴**（Flex 的灵魂）：
- **主轴（main axis）**：由 `flex-direction` 决定。`row`（默认）主轴水平→，`column` 主轴垂直↓。
- **交叉轴（cross axis）**：与主轴垂直。

```
flex-direction: row（默认）
  主轴 →→→→→→→→→→→
  ┌────┬────┬────┐   ↑
  │ 1  │ 2  │ 3  │   交叉轴
  └────┴────┴────┘   ↓
  justify-content 管主轴（水平）方向的对齐/分布
  align-items     管交叉轴（垂直）方向的对齐
```

**Flex 属性速查表（背下来）**：

| 属性 | 加在哪 | 作用 | 常用值 |
|---|---|---|---|
| `display: flex` | 容器 | 开启 flex | — |
| `flex-direction` | 容器 | 主轴方向 | `row`（默认）/ `column` |
| `justify-content` | 容器 | **主轴**对齐/分布 | `flex-start` / `center` / `space-between` / `space-around` / `flex-end` |
| `align-items` | 容器 | **交叉轴**对齐 | `stretch`（默认）/ `center` / `flex-start` / `flex-end` |
| `flex-wrap` | 容器 | 是否换行 | `nowrap`（默认）/ `wrap` |
| `gap` | 容器 | 子元素间距 | `12px` / `8px 12px` |
| `flex` | 子项 | 伸缩比例 | `1`（占满剩余）/ `0 0 auto` |
| `align-self` | 子项 | 单个子项覆盖 align-items | `center` 等 |

**真实代码**（`CpFilterBar.vue` 第 165-174 行 `<style scoped>`）：

```css
.cp-filter {
  display: flex;
  flex-wrap: wrap;              /* 字段多了换行 */
  gap: 12px;                    /* 子元素间距 12px */
  align-items: flex-end;        /* 交叉轴：底部对齐（label 在上、控件在下，底对齐好看） */
  background: var(--cp-card);
  border-radius: var(--cp-r-md);
  padding: 14px 18px;
}
.fld { display: flex; flex-direction: column; gap: 5px; }  /* 每个字段：竖排 label+控件 */
.cp-filter .spacer { flex: 1; }   /* 占满剩余空间，把按钮挤到最右 */
.fbtns { display: flex; gap: 8px; align-items: center; }
```

**解析**：
- 外层 `.cp-filter` 横向 flex + `flex-wrap: wrap`（字段多自动换行）+ `gap:12px`。
- 每个字段 `.fld` 是**嵌套 flex**，`flex-direction: column` 让 label 在上、控件在下。
- `.spacer { flex: 1 }` 是个**空占位 span**（模板第 150 行 `<span class="spacer" />`），`flex:1` 让它吃掉所有剩余空间，从而把右边的按钮组顶到最右——这是 flex 布局"左右分栏"的经典技巧。

**居中三法（面试现场可能让你手写"让一个 div 水平垂直居中"）**：

```css
/* 法一：Flex（最常用，推荐） */
.parent { display: flex; justify-content: center; align-items: center; }

/* 法二：Grid（更简洁） */
.parent { display: grid; place-items: center; }

/* 法三：绝对定位 + transform（老办法，不依赖 flex/grid） */
.parent { position: relative; }
.child  { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); }
```

**面试问答**：
> **Q：Flex 里怎么让子元素水平垂直居中？主轴交叉轴分别由谁管？**
> A：`display:flex` 后，`justify-content:center` 管主轴（默认 row 时是水平）居中，`align-items:center` 管交叉轴（垂直）居中，两个一起就是完全居中。主轴由 `flex-direction` 定（row 水平 / column 垂直），交叉轴永远垂直于主轴——所以改成 column 时，justify-content 就变成管垂直了，这点最容易记反。

### 5.5.3 Grid 认识（二维布局）

Flex 是一维（一行或一列），**Grid 是二维**（同时管行和列，像表格）。适合整体页面布局、卡片矩阵。

```css
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);  /* 3 等宽列（1fr = 1 份剩余空间） */
  gap: 16px;                              /* 行列间距 */
}
/* 响应式卡片：列数随宽度自适应（不用媒体查询） */
.cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
}
```

`auto-fill + minmax` 意思是"每列最小 240px，能塞几列塞几列，多的均分"——宽屏 4 列、窄屏自动降到 1 列，很多仪表盘用这招。

**Flex vs Grid 一句话**：一维（工具栏、按钮组、导航）用 Flex；二维（整页栅格、卡片墙、复杂表单布局）用 Grid。

### 5.5.4 scoped 样式原理（属性选择器 hash）★ 面试必问

**问题**：CSS 默认是**全局**的。A 组件写 `.title { color: red }`，会污染 B 组件所有 `.title`。Vue 的 `<style scoped>` 解决这个：**让样式只作用于当前组件**。

**原理（背下来）**：Vue 编译带 `scoped` 的组件时：
1. 给该组件每个 DOM 元素加一个**唯一的属性**，如 `data-v-7ba5bd90`；
2. 把 CSS 选择器改写成**带属性选择器**的形式。

```html
<!-- 源码 -->
<template><div class="title">库存</div></template>
<style scoped>.title { color: red }</style>

<!-- 编译后 -->
<div class="title" data-v-7ba5bd90>库存</div>
<style>.title[data-v-7ba5bd90] { color: red }</style>
```

`.title[data-v-7ba5bd90]` 这个选择器只能命中带同一个 hash 属性的元素——也就是**只有本组件的元素**。别的组件 hash 不同，样式互不干扰。

**关键点**：scoped 靠的是**属性选择器 + 编译期 hash**，**不是** Shadow DOM（那是 Web Components 的隔离机制，两回事）。面试爱拿这个考。

### 5.5.5 `:deep()` 穿透（改子组件/第三方组件内部样式）

**问题**：scoped 样式加了 hash 属性，**只能命中当前组件模板里的元素**。但 Element Plus 的 `<el-card>`、`<el-input>` 内部会渲染出 `.el-card__header`、`.el-input__wrapper` 这些元素——它们是**子组件渲染的**，没有你的 hash 属性，所以 scoped 样式**够不着**它们。

**解法 `:deep()`**——穿透到子组件内部：

真实代码（`src/views/wms/ProductionInboundView.vue` 第 217 行等）：

```css
.big-card :deep(.el-card__header) { background: var(--cp-brand-bg); }
.task-card :deep(.el-card__body) { padding: 8px; max-height: 720px; overflow-y: auto; }
:deep(.is-selected td) { background: var(--cp-brand-bg) !important; }
.search-box :deep(.el-input__wrapper) { /* 改 Element 输入框内壳 */ }
```

**原理**：`:deep(.el-card__header)` 编译后大致是 `.big-card[data-v-xxx] .el-card__header`——hash 加在**前面的父选择器**上（保证还是本组件范围内），后面的 `.el-card__header` **不加 hash**，从而能命中子组件渲染的内部元素。

**用途**：定制 Element Plus / 第三方 UI 库组件的内部观感（改表头背景、内边距、输入框圆角）。cp6.web 大量用它统一 Element Plus 组件的外观到自己的设计系统。

**面试问答**：
> **Q：scoped 样式为什么改不了 el-input 内部？怎么改？**
> A：scoped 靠给本组件元素加 hash 属性 + 属性选择器实现隔离，只能命中当前组件模板里的元素。Element Plus 组件内部的 `.el-input__wrapper` 是子组件渲染的，没有本组件的 hash，所以 scoped 选择器选不中。要改就用深度选择器 `:deep(.el-input__wrapper)`，它编译后只给前面的父选择器加 hash、目标选择器不加，从而穿透到子组件内部。

### 5.5.6 CSS 变量与主题

CSS 自定义属性（变量）：`--x: value` 定义，`var(--x)` 使用。cp6.web 满屏用它做**设计令牌（design token）**：

```css
/* CpFilterBar 里用的变量（在全局 :root 定义一次，全项目复用） */
background: var(--cp-card);           /* 卡片背景色 */
border-radius: var(--cp-r-md);        /* 中号圆角 */
box-shadow: var(--cp-shadow-1);       /* 一级阴影 */
color: var(--cp-brand-deep);          /* 品牌深色 */
font-size: var(--cp-fs-sm);           /* 小号字 */
```

好处：
- **一处改，全局变**：改 `:root { --cp-brand-deep: #新色 }`，所有用到的地方一起变——**换肤/主题**的基础。
- **运行时可改**：CSS 变量能被 JS 动态改（`document.documentElement.style.setProperty('--cp-brand', '#f00')`），可做暗色模式、多主题切换，这是 Sass 变量做不到的（Sass 变量编译期就固定了）。

### 5.5.7 BEM 命名认识

BEM = **Block__Element--Modifier**，一种 class 命名约定，让样式作用域从命名上就清晰：
- `Block`：独立块，`.card`
- `Element`：块的部件，双下划线，`.card__header`、`.card__body`
- `Modifier`：状态/变体，双连字符，`.card--active`、`.button--primary`

你注意 Element Plus 的 class 就是 BEM：`.el-card__header`、`.el-input__wrapper`、`.el-button--primary`。BEM 的价值是**扁平、无嵌套、语义化、避免层层后代选择器**，配合 scoped 其实 cp6.web 业务代码更多靠 scoped 隔离，但认识 BEM 你才能读懂 Element Plus 的内部 class（进而写 `:deep()`）。

### 5.5.8 响应式 `@media`（真实 767px 移动端适配精读）

**概念**：媒体查询 `@media` 根据**屏幕宽度等条件**应用不同样式，实现"一套代码，桌面/平板/手机都好看"。

真实代码（`src/views/oa/inbox/FormDetail.vue` 第 365-390 行，OA 审批详情页的移动端适配）：

```css
@media (max-width: 767px) {          /* 屏幕 ≤767px（手机）时生效 */
  .detail-left {
    border-right: none;              /* 桌面是左右分栏，手机去掉分隔线 */
    padding-right: 0;
  }
  .detail-right {
    max-height: none;                /* 桌面右栏限高滚动，手机放开 */
    padding-left: 0;
    margin-top: 16px;                /* 手机改上下堆叠，加顶部间距 */
    overflow-y: visible;
  }
  /* 审批操作栏在手机上钉底（sticky footer） */
  .action-bar {
    position: sticky;
    bottom: 0;                       /* 吸底 */
    z-index: 5;
    flex-wrap: wrap;
    background: var(--cp-card);
    box-shadow: var(--cp-shadow-up);
    margin: 16px -16px 0;
    padding: 10px 12px calc(10px + env(safe-area-inset-bottom));  /* 适配 iPhone 底部安全区 */
  }
}
```

**逐行解析**：
- `@media (max-width: 767px)` —— 断点 767px 是移动端常用界限（≤767 视为手机，≥768 视为平板/桌面）。
- 桌面版 FormDetail 是**左右两栏**（左边表单内容 `.detail-left`，右边审批历史 `.detail-right`）；手机屏窄，媒体查询里把它改成**上下堆叠**（去掉右边框、右栏加 `margin-top` 落到下面）。这是响应式最经典的"多栏→单列"重排。
- `.action-bar { position: sticky; bottom: 0 }` —— 手机上把"批准/驳回"操作按钮**钉在屏幕底部**（sticky footer），拇指好按。
- `env(safe-area-inset-bottom)` —— 适配 iPhone 底部小黑条（安全区），避免按钮被遮。这是真实移动端工程细节，能讲出来是加分。

**响应式两种主流思路**：
- **桌面优先**：默认写桌面样式，`@media (max-width: 767px)` 里覆盖成手机样式（cp6.web 这个例子就是）。
- **移动优先（mobile-first）**：默认写手机样式，`@media (min-width: 768px)` 里升级成桌面。现代更推荐移动优先。

cp6.web 还有个更工程化的做法——`useBreakpoint` 组合式函数（`VolForm.vue` 第 82、92 行 `const { isMobile } = useBreakpoint()`），在 **JS 层**判断是否移动端，用来切换组件行为（不只是样式）。CSS 的 `@media` 管样式，JS 的 breakpoint 管逻辑/结构，两者配合。

**面试问答**：
> **Q：移动端适配怎么做？断点一般取多少？**
> A：用媒体查询 `@media` 按屏宽切样式，常用断点 768px（手机/平板界）、1024px（平板/桌面界）。思路有桌面优先（默认桌面，窄屏覆盖）和移动优先（默认手机，宽屏升级，现在更推荐）。典型改法是多栏布局在窄屏重排成单列、操作栏吸底、配合 `env(safe-area-inset-*)` 适配刘海屏安全区。纯样式变化用 CSS `@media`；如果要改组件结构/逻辑（比如手机换成卡片、桌面用表格），会在 JS 里用一个 breakpoint 组合式函数判断 `isMobile` 来切。

---

## 5.6 Vite 与构建

> 面试问"为什么用 Vite 不用 webpack？""开发跨域怎么解决？""环境变量怎么配？"

### 5.6.1 为什么 Vite 快？（dev 原生 ESM vs webpack 全量打包）

**核心区别（背下来）**：

| | webpack（旧） | Vite（新，cp6.web 用） |
|---|---|---|
| **dev 启动** | 先把**整个项目**打包成 bundle 才能启动 → 项目越大启动越慢（几十秒~几分钟） | **不打包**，直接起服务；浏览器请求哪个模块才编译哪个 → 秒级启动 |
| **dev 原理** | 打包器（bundle-based） | 原生 ESM（浏览器直接 `import`）+ 按需编译（esbuild，Go 写的极快） |
| **热更新 HMR** | 改一个文件可能要重新打包一块 → 慢 | 只失效改动的那个模块 → 快，几乎瞬时 |
| **依赖预构建** | 每次打包 | 用 esbuild 预构建 node_modules 一次并缓存 |
| **生产构建** | webpack | Rollup（Vite 底层，tree-shaking 好） |

**一句话原理**：现代浏览器原生支持 ES Module（`<script type="module">` 里能直接 `import`）。Vite 开发时**不打包**，把你的源码当成一个个 ESM 模块，浏览器请求 `StockQueryView.vue` 时 Vite 才即时把它编译成 JS 返回——**按需、增量、用 esbuild（比 JS 写的 webpack 快 10-100 倍）**。所以项目再大，dev 启动都是秒级；改代码 HMR 只更新那一个模块。webpack 则要先把全部依赖图打成 bundle 才能跑，大项目启动慢是它的死穴。

**注意**：Vite 的"快"主要在**开发期**。生产构建时它用 Rollup 还是要完整打包（这时会做代码分割、tree-shaking、压缩），生产构建时间和 webpack 是一个量级。

### 5.6.2 精读 vite.config.ts（proxy 代理 + 别名 + 插件）

`vite.config.ts`（全文）：

```ts
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

export default defineConfig({
  plugins: [
    vue(),           // 让 Vite 认识 .vue 单文件组件
    vueDevTools(),   // 开发调试工具面板
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))   // @ = src 目录
    },
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: false,
    // 把 /api 请求代理到后端，避免跨域问题
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5177',
        changeOrigin: true,
      },
      '/hubs': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5177',
        changeOrigin: true,
        ws: true,     // 支持 WebSocket（SignalR 实时推送）
      }
    }
  }
})
```

**逐行解析**：

- **`plugins`**：
  - `vue()` —— 官方插件，让 Vite 能编译 `.vue` 单文件组件（把 template/script/style 拆开处理）。没它 Vite 不认识 `.vue`。
  - `vueDevTools()` —— 开发期浮层调试工具。

- **`resolve.alias` 路径别名（重要）**：
  - `'@': .../src` —— 定义 `@` 指向 `src` 目录。所以全项目 `import { stockApi } from '@/api/wms/stock'` 里的 `@/` 就是 `src/`。
  - 好处：不用写 `../../../api/wms/stock` 这种脆弱的相对路径（文件一移动全断）。`@/api/...` 从根开始，稳定清晰。你在 router、http、i18n 里看到的 `@/xxx` 全靠这行。
  - （TS 里还要在 `tsconfig.json` 配 `paths` 对应，别名才有类型提示——两处要一致。）

- **`server.proxy` 开发代理（★ 解决开发跨域，面试常问）**：
  - 问题：开发时前端跑在 `localhost:5173`（Vite dev server），后端跑在 `localhost:5177`。浏览器同源策略下，5173 直接请求 5177 是**跨域**，会被拦（CORS）。
  - 解法：配 proxy，让**前端请求先发给 Vite dev server（同源，不跨域），Vite 再转发给后端**。`http.ts` 里 `baseURL:'/api'`，请求 `/api/wms/stock` 打到 5173，proxy 规则 `'/api'` 命中，转发到 `http://localhost:5177/api/wms/stock`。浏览器只跟 5173 说话，永远同源，跨域问题消失。
  - `target: process.env.VITE_API_TARGET || 'http://localhost:5177'` —— 后端地址可用环境变量覆盖（注释说：本地 dotnet run 是 5177，Docker 里是 9991）。
  - `changeOrigin: true` —— 转发时把请求头的 Host 改成目标地址（很多后端校验 Host 需要）。
  - `'/hubs'` 带 `ws: true` —— 代理 WebSocket（SignalR 实时推送用，如工单状态实时刷新）。
  - **注意**：proxy 只在**开发**生效。生产环境是 nginx/容器做同样的 `/api` 转发（前端静态站和后端 API 由 nginx 统一入口，也就没有跨域）。

**面试问答**：
> **Q：开发时前端 5173、后端 5177 跨域怎么解决？生产呢？**
> A：开发用 Vite 的 `server.proxy`：前端请求走相对路径 `/api/...` 打到 dev server（同源不跨域），Vite 按 proxy 规则转发给后端，浏览器全程只跟 dev server 同源通信，绕过 CORS。生产环境前端打包成静态文件由 nginx 托管，nginx 再把 `/api` 反向代理到后端容器，同样同源。关键是前端代码永远只写相对路径 `/api`，从不写后端真实域名——环境无关。

### 5.6.3 环境变量 import.meta.env

Vite 用 `import.meta.env` 读环境变量（不是 Node 的 `process.env`）：

真实用例：
- `router/index.ts` 第 295 行：`createWebHistory(import.meta.env.BASE_URL)` —— 部署基路径。
- `i18n/index.ts` 第 11 行：`import.meta.env.DEV ? [...] : []` —— **`DEV`** 是 Vite 内置布尔，开发为 true、生产为 false（这里 dev 才加"伪本地化 QA 语言"）。
- `i18n/index.ts` 第 24 行：`import.meta.env.VITE_I18N_MODE` —— 自定义变量。

**规则**：
- Vite 内置：`import.meta.env.MODE`（development/production）、`.DEV`、`.PROD`、`.BASE_URL`。
- 自定义变量**必须以 `VITE_` 开头**才会暴露给前端代码（如 `VITE_API_TARGET`、`VITE_I18N_MODE`）——防止把后端密钥之类误打进前端包。
- 定义在 `.env` / `.env.development` / `.env.production` 文件里，Vite 按当前 mode 加载。

### 5.6.4 build 产物与部署 + type-check 关系（读 package.json scripts）

`package.json` 的 scripts（第 6-20 行）逐条讲：

```jsonc
"scripts": {
  "dev": "vite",                                      // 起开发服务器（HMR）
  "build": "run-p type-check \"build-only {@}\" --",  // ★ 生产构建：并行跑类型检查+打包
  "preview": "vite preview",                          // 本地预览 build 产物
  "build-only": "vite build",                         // 只打包，不类型检查
  "type-check": "vue-tsc --build",                    // 只类型检查，不打包
  "test": "vitest run",                               // 跑单元测试（一次性）
  "test:unit": "vitest run",
  "test:watch": "vitest",                             // 监听模式跑测试
  "e2e": "playwright test",                           // 跑端到端测试
  "e2e:ui": "playwright test --ui",
  "e2e:report": "playwright show-report e2e/report",
  "i18n:pull": "node scripts/i18n-pull-keys.mjs",     // 从后端拉 i18n key 快照
  "i18n:check": "node scripts/i18n-check-keys.mjs",   // 校验代码里的 key 都存在（CI）
  "i18n:gen-types": "node scripts/i18n-gen-types.mjs" // 生成 i18n key 的 TS 类型
}
```

**重点讲 `build` 和 type-check 的关系（面试常问）**：
- `"build": "run-p type-check \"build-only {@}\""` —— `run-p`（npm-run-all2 提供）**并行**跑两个任务：`type-check`（`vue-tsc` 类型检查）和 `build-only`（`vite build` 打包）。
- **为什么分开又并行？** 因为 **Vite/esbuild 打包时为了快，默认"抹掉类型直接转译"，不做完整 TS 类型检查**（它只 strip types，不校验类型对不对）。所以光 `vite build` 能打包成功，但**类型错误它不管**。要保证类型安全，必须**另外**跑 `vue-tsc`（Vue 官方的 TS 检查器，能检查 .vue 里的类型）。`build` 脚本把两者并行跑，任一失败整个 build 失败——既快又保证类型正确。
- `vue-tsc` vs `tsc`：`vue-tsc` 是套了一层能理解 `.vue` 单文件组件的 `tsc`。普通 `tsc` 不认识 `.vue`。

**build 产物与部署**：
- `vite build` 输出到 `dist/`：一个 `index.html` + 一堆带 hash 的 JS/CSS chunk（`StockQueryView-a1b2c3.js` 等，就是懒加载分出来的）+ 静态资源。
- 这是**纯静态文件**，没有 Node 运行时。部署方式：
  1. 扔进 nginx 静态托管，nginx 配 `try_files $uri /index.html`（history 模式 fallback）+ `/api` 反向代理到后端；
  2. 或打进 Docker 镜像（cp6.web 就是容器化：前端静态站 + 后端 API 容器 + DB 容器，一套 compose 起）。
- 带 hash 的文件名用于**长期缓存**：内容变了 hash 才变，浏览器能放心长缓存不变的 chunk。

**面试问答**：
> **Q：`vite build` 会做类型检查吗？为什么 build 脚本还要单独跑 vue-tsc？**
> A：不会。Vite 打包用 esbuild，为了速度只做"类型剥离+转译"，不校验类型对错——有类型错误也能打包成功。所以要类型安全必须单独跑 `vue-tsc`（能检查 .vue 内类型的 TS 检查器）。我们的 build 脚本用 `run-p` 把 `type-check` 和 `build-only` 并行跑，任一失败 build 就失败，兼顾速度和类型正确性。

---

## 5.7 前端测试

> 面试可能问"前端怎么测？""单测和 E2E 区别？""测试金字塔？"

### 5.7.1 测试金字塔

```
        ╱╲          E2E（端到端，Playwright）
       ╱  ╲         少、慢、贵，但最接近真实用户
      ╱────╲
     ╱      ╲       集成测试（组件挂载 + 交互，@vue/test-utils）
    ╱────────╲      中等数量
   ╱          ╲
  ╱────────────╲    单元测试（纯函数/store/工具，Vitest）
 ╱______________╲   多、快、便宜
```

原则：**底层多写（单测，跑得飞快）、顶层少写（E2E，只覆盖关键流程）**。cp6.web 正是这个结构——一大堆 `.spec.ts` 单测 + 少量 `e2e/*.spec.ts`。

### 5.7.2 Vitest 单元测试（精读真实测试文件）

**Vitest** 是 Vite 生态的单测框架（API 兼容 Jest，但用 Vite 编译，跟项目共用配置，快）。`package.json` 里 `"test": "vitest run"`。

真实标本 `src/stores/oaActingAs.test.ts`（全文，测一个纯逻辑模块）：

```ts
// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { setActingAs, getActingAs, clearActingAs } from './oaActingAs'

describe('oaActingAs', () => {
  beforeEach(() => sessionStorage.clear())      // 每个用例前清空，隔离

  it('set/get/clear roundtrip', () => {
    expect(getActingAs()).toBeNull()
    setActingAs({ userId: 'u1', userName: 'X 经理' })
    expect(getActingAs()?.userId).toBe('u1')
    expect(getActingAs()?.userName).toBe('X 经理')
    clearActingAs()
    expect(getActingAs()).toBeNull()
  })

  it('get returns null for malformed JSON', () => {
    sessionStorage.setItem('cp6_oa_acting_as', 'not-json')
    expect(getActingAs()).toBeNull()            // 坏数据不崩，返回 null
  })
})
```

**逐行解析（Vitest 基本 API 全在这）**：
- `// @vitest-environment jsdom` —— 注释指令，指定这个测试文件用 **jsdom** 环境（模拟浏览器的 `sessionStorage`、`document` 等，因为被测代码用了 `sessionStorage`，Node 里没有）。
- `describe('oaActingAs', () => {...})` —— **测试套件**，把相关用例分组。
- `it('...', () => {...})` —— **单个测试用例**（`it` = `test` 别名）。
- `beforeEach(() => sessionStorage.clear())` —— 每个用例**前**跑，清 sessionStorage 保证用例间**互相隔离**（一个用例的状态不污染下一个）。
- `expect(x).toBe(y)` / `.toBeNull()` —— **断言**。`toBe` 严格相等，`toBeNull` 判 null。
- 第二个用例测**边界/异常**：`sessionStorage` 存了坏 JSON，`getActingAs()` 应该优雅返回 null 而不是抛错（对应源码里 `try { JSON.parse } catch { return null }`）。**测异常路径**是好测试的标志。

**这个测试对应的源码**（`oaActingAs.ts`）就是 5.2.1 那个纯函数模块——**纯逻辑、无 UI、无网络，最好测**（输入→输出确定）。这是单测的理想目标：store 逻辑、工具函数、格式化函数、校验规则。cp6.web 里 `ruleEngine.test.ts`、`designValidate.spec.ts`、`coords.spec.ts` 都是这类。

### 5.7.3 @vue/test-utils（挂载组件测交互）

测**组件**（有模板、有交互）要用 `@vue/test-utils`（`package.json` devDependencies 有 `@vue/test-utils`）。核心 API：

```ts
import { mount } from '@vue/test-utils'
import CpStatusStrip from '../CpStatusStrip.vue'

it('emits update:modelValue with the clicked item key', async () => {
  const w = mount(CpStatusStrip, { props: { items: [...], modelValue: '' } })  // 挂载组件
  await w.find('.strip-item').trigger('click')                // 找元素、触发点击
  expect(w.emitted('update:modelValue')).toEqual([['done']])  // 断言 emit 了正确事件
})
```

（上面是 cp6.web `CpStatusStrip.spec.ts` 第 23-26 行的真实断言 `expect(w.emitted('update:modelValue')).toEqual([['done']])` 的还原。）

**三板斧**：
- `mount(Component, { props })` —— 把组件挂到虚拟 DOM，可传 props。
- `wrapper.find('选择器')` —— 找元素；`.trigger('click')` 触发事件；`.setValue()` 设输入值。
- `wrapper.emitted('事件名')` —— 拿组件 emit 的事件记录，断言"点了按钮确实 emit 了正确事件+参数"。
- `await` —— DOM 更新是异步的，操作后要 `await`（等 nextTick）再断言。

cp6.web 里 `components/templates/__tests__/*.spec.ts`（CpFilterBar、CpListPage、CpFormDialog 等）就是用它测组件的 props/emit/交互。

### 5.7.4 Playwright E2E（真浏览器跑完整流程）

**E2E（端到端）** 用真实浏览器模拟真实用户，从登录到下单走完整流程。cp6.web 的 `e2e/` 目录：

```
e2e/
├── auth.setup.ts            # 登录一次存会话，供其他用例复用
├── golden-path.spec.ts      # 黄金路径（关键业务流程）
├── erp-front-flow.spec.ts   # ERP 前台流程
├── smoke-all-screens.spec.ts# 冒烟：所有页面能打开
├── space-viewer.spec.ts     # 3D 空间浏览器
└── .auth/admin.json         # 保存的登录态（storageState）
```

精读 `e2e/auth.setup.ts`（登录 setup，E2E 的入门标本）：

```ts
import { test as setup, expect } from '@playwright/test'
const authFile = 'e2e/.auth/admin.json'

setup('authenticate as admin', async ({ page }) => {
  await page.goto('/login')                                    // 打开登录页
  const userInput = page.locator('input').first()             // 定位第一个输入框=用户名
  await userInput.click()
  await userInput.pressSequentially('admin')                  // 逐字符输入（触发 Vue 响应）
  const pwInput = page.locator('input[type="password"]')
  await pwInput.pressSequentially('123456')
  await page.locator('.login-button').click()                 // 点登录
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })  // 等跳走
  const authed = await page.evaluate(() => localStorage.getItem('cp6_authed'))
  expect(authed, 'login should set cp6_authed flag').toBe('1') // 验证登录标志
  await page.context().storageState({ path: authFile })       // 保存会话到文件
})
```

**逐行 + 面试点**：
- `page.goto` / `page.locator` / `.click()` / `.pressSequentially()` —— Playwright 的核心 API：导航、定位元素、操作。
- **`pressSequentially` 而非 `fill`（注释里的坑，很真实）**：Element Plus 的 `v-model` 输入框，用 `.fill()` 直接设 value 不会触发 Vue 的 input 事件，el-form 校验会认为是空值挡住提交。所以要 `pressSequentially`（逐字符输入，每个字符触发 input 事件），Vue 表单模型才真正更新。**这是"真实用户输入"和"程序设值"的区别**，E2E 常踩。
- `waitForURL` —— 等 URL 变化（登录成功跳走），E2E 要显式等异步。
- `storageState({ path })` —— 把登录态（cookie + localStorage）存文件，其他用例复用**免得每次都登录**（`auth.setup.ts` 的意义）。对应注释：httpOnly token 在 cookie 里，storageState 会捕获。

**单测 vs E2E 一句话**：单测测"一个函数/组件对不对"（快、多、隔离）；E2E 测"整个系统串起来用户能不能走通"（慢、少、真实）。面试就答测试金字塔。

**面试问答**：
> **Q：前端怎么做测试？单测和 E2E 分别测什么？**
> A：按测试金字塔分三层。底层单元测试用 Vitest 测纯逻辑——store、工具函数、校验规则，快而多；中层用 @vue/test-utils `mount` 组件，测 props/emit/交互；顶层 E2E 用 Playwright 开真浏览器跑关键业务流程（登录→下单），少而真实。原则是底层多写、顶层少写。我们项目单测覆盖 store 和领域逻辑（如权限、流程校验），E2E 覆盖黄金路径和全屏幕冒烟，还用 storageState 复用登录态加速。

---

## 5.8 工程质量（ESLint / TS / i18n / CI）

### 5.8.1 ESLint / Prettier 概念

- **ESLint**：**代码质量**检查器——查潜在 bug、坏味道（未用变量、`==` 该用 `===`、误用 hooks）。可 `--fix` 自动修部分。
- **Prettier**：**代码格式化**器——只管排版（缩进、引号、分号、换行），不管逻辑。保证团队代码风格统一，消除"格式化 diff 噪音"。
- 分工：**ESLint 管对不对，Prettier 管好不好看**。通常一起用（Prettier 格式化 + ESLint 查质量），配合 Git pre-commit hook（husky + lint-staged）在提交前自动跑，脏代码进不了仓库。

### 5.8.2 TypeScript 严格模式

TS 的 `strict: true`（cp6.web 走严格模式）开启一组严格检查，最重要几条：
- `strictNullChecks`：`null`/`undefined` 必须显式处理——这就是为什么代码里满是 `formRef.value?.validate()`（可选链）、`error.response?.status`。防"undefined is not a function"这类运行时崩溃在**编译期**就被抓。
- `noImplicitAny`：不允许隐式 `any`，逼你标类型。
- 严格模式的价值：**把大量运行时错误提前到编译期**，对大型项目（cp6.web 几百个页面）是质量生命线。面试可讲"我们全严格模式，配合 vue-tsc 在 CI 卡类型错误"。

### 5.8.3 i18n 工程化（后端 Sys_Lang 数据库驱动的多语言链路）★

这是 cp6.web 一个很有工程含量的点，讲好加分很多。

**目录结构**（`src/i18n/`）：
```
i18n/
├── index.ts            # ★ 引擎：createI18n + 懒加载 + 回退链 + 伪本地化
├── keys.generated.json # 后端拉来的 key 快照（CI 校验用）
├── keys.generated.ts   # 由快照生成的 TS 类型
├── tOr.ts              # 带默认值的翻译工具
└── __tests__/tOr.spec.ts
```

**核心特点（读 `i18n/index.ts`）**：

1. **数据库驱动，不是硬编码 JSON**。传统 i18n 把文案写死在前端 JSON。cp6.web 的文案存在**后端数据库 `Sys_Lang` 表**，前端**运行时通过 API 拉取**（`index.ts` 第 131 行 `http.get('/lang/${lang}/ns/_core')`）。好处：运营改文案不用改前端代码、不用重新部署——DB 改了刷新就变。链路：
   ```
   Sys_Lang 表(DB) → 后端 /lang API → 前端 http.get → i18n.setLocaleMessage → 页面 t('key')
   ```

2. **按命名空间懒加载**（第 28、141-151 行）。大模块（wms/sales/erp/mes）的翻译单独成"命名空间"，进到对应路由时才加载（`ensureNamespacesForPath`，就是 5.3.6 路由守卫里 `await` 的那个）。启动只加载 `_core`（通用文案），省首屏流量。**和路由懒加载同一个思想**。

3. **显式回退链**（第 15-21 行）。`zh-CN` 缺某个 key → 回退到 `zh-TW` → 再回退 `ja`，而不是一律回英文。`fallbackChain` 精细控制。

4. **`flatJson: true`**（第 63 行）。key 用点分平铺（`{"a.b.c": "值"}`），缺失时**回退返回 key 本身**——这就是 5.4.2 里错误码翻译"没翻译就原样显示 key，不崩"的底层保证。

5. **伪本地化 QA 语言**（第 156-193 行，dev only）。把英文文案重音化+加长 40%（`⟦Šţöçķ····⟧`），用来**一眼揪出没做国际化的硬编码文案**（没经过 t() 的文案不会变形）和**文本溢出**（加长撑爆布局就暴露）。这是国际化质量保障的高级技巧，能讲出来非常加分。

**三个 i18n npm scripts 是干嘛的**（`package.json` 第 18-20 行）：
- `i18n:pull`（`i18n-pull-keys.mjs`）：**从后端拉取**当前所有合法 key，生成 `keys.generated.json` 快照（需后端在跑）。
- `i18n:check`（`i18n-check-keys.mjs`）：**CI 校验闸**——扫代码里所有 `t('...')`，比对快照，**引用了但快照里没有的 key = 报错、非零退出**，卡住 CI。防止"代码写了 `t('新文案')` 但后端没配这个 key，上线后用户看到裸露的 key"。
- `i18n:gen-types`（`i18n-gen-types.mjs`）：由快照**生成 TS 类型**，让 `t('key')` 的 key 也有类型提示/校验。

**读 `scripts/i18n-check-keys.mjs` 开头理解它怎么工作**（第 1-58 行核心逻辑）：

```js
// 1. 加载后端拉来的合法 key 快照
const known = new Set(JSON.parse(readFileSync(SNAP, 'utf-8')))

// 2. 递归遍历 src 下所有 .vue / .ts 文件
function walk(dir, acc = []) { /* 收集所有源文件路径 */ }

// 3. 正则匹配 t('x') / $t("x")，只捕获字面量 key
const RE = /(?<![\w$])\$?t\(\s*(['"])((?:\\.|(?!\1).)*)\1/g

// 4. 逐文件扫描，字面量 key 不在快照里 → 记进 missing
for (const f of files) {
  for (const m of code.matchAll(RE)) {
    const key = m[2]
    if (/^\s*\+/.test(after) || key.endsWith('.')) { dynamicCount++; continue } // 跳过动态拼接
    if (!known.has(key)) { /* 记为缺失，最终报错退出 */ }
  }
}
```

**逐点解析**：
- 它是个**纯 Node 脚本**（`.mjs`），不依赖 DB（用提交进仓库的 `keys.generated.json` 快照）——所以 CI 里能跑，不用连数据库。
- 正则只抓**字面量** key（`t('订单')`）；**动态拼接**的 key（`t('前缀.' + 变量)`）静态没法判定，跳过并计数。这是静态分析的固有局限，脚本诚实地承认并统计。
- 缺失就非零退出 → CI 红 → PR 合不进去。**这就是"多语言键缺失如何在 CI 拦截"的答案**。

**面试问答**：
> **Q：多语言（i18n）怎么工程化？怎么防止漏翻译上线？**
> A：我们文案存在后端数据库，前端运行时按语言+命名空间懒加载（进对应模块路由才拉那块翻译），配显式回退链和 flatJson（缺 key 回退返回 key 本身不崩）。防漏翻译靠 CI：先 `i18n:pull` 从后端拉合法 key 生成快照提交进仓库，CI 跑 `i18n:check` 用正则扫代码里所有 `t('...')` 字面量 key，比对快照，有引用但快照里没有的就非零退出卡住合并。还有 dev-only 的伪本地化语言（重音化+加长文案），一眼揪出没走 t() 的硬编码文案和文本溢出。

### 5.8.4 CI 质量门总结

cp6.web 的一次提交要过的闸（体现工程成熟度）：
1. `type-check`（vue-tsc）——类型必须对；
2. `test`（vitest run）——单测必须过；
3. `i18n:check`——不能有缺失的翻译 key；
4. ESLint——代码质量；
5. E2E（关键流程）——黄金路径不能断。
任一红灯，PR 合不进 main。

---

## 5.9 前后端协作全链路串讲（库存查询）

> 把 Day 1（后端）+ Day 2（前端）全串起来。面试官若问"一个功能从点击到数据库怎么走的？"你照这张图讲，稳拿高分。

**场景**：用户在"库存查询"页选了仓库 W01，点"查询"，看到库存列表。

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         用户点「查询」按钮                                     │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                     │
  ① 页面组件 StockQueryView.vue（前端·视图层）
     - CpFilterBar v-model="query" 收集查询条件 { warehouse:'W01', page:1 }
     - @search="load" → 触发 load()
     - load() { const res = await stockApi.search(query) }
                                     │  调 API 对象方法
                                     ▼
  ② API 层 api/wms/stock.ts（前端·请求封装）
     search(query) { return http.get('/wms/stock', { params: query }) }
                                     │  委托给 http 实例
                                     ▼
  ③ http.ts 请求拦截器（前端·中间件）
     - baseURL '/api' → 实际路径 /api/wms/stock?warehouse=W01&page=1
     - withCredentials:true → 自动带 httpOnly Cookie(cp6_at 认证 token)
     - GET 是安全方法 → 不注入 CSRF 头
                                     │  发出 HTTP 请求
                                     ▼
  ④ Vite dev proxy（开发期）/ nginx（生产期）
     - /api 前缀命中 → 转发到后端 localhost:5177（或容器 9991）
     - 前端只跟同源 dev-server/nginx 说话 → 无跨域
                                     │  HTTP GET /api/wms/stock?...
                                     ▼
┌═══════════════════════════════ 后端（Day 1）═══════════════════════════════┐
│  ⑤ ASP.NET Core 中间件管道                                                   │
│     - 认证中间件：从 Cookie(cp6_at) 解出 JWT → 校验 → 得到当前用户/租户       │
│     - [RequirePermission("wms/stock:query")] → 查该用户有无此操作权限         │
│       （前端 v-permission 藏了按钮只是 UX，这里才是真闸门）                    │
│                                     │                                        │
│  ⑥ StockController.Search([FromQuery] StockSearchQuery q)                    │
│     - 模型绑定：query string → StockSearchQuery 对象                          │
│                                     │  调 Service                            │
│  ⑦ StockService（业务逻辑）                                                   │
│     - 加租户过滤（多租户：只查当前租户的库存）                                 │
│     - 组装查询条件、分页                                                      │
│                                     │  调 Repository / DbContext             │
│  ⑧ EF Core（数据访问）                                                        │
│     - LINQ: _db.Stocks.Where(s => s.TenantId==tid && s.Warehouse=="W01")     │
│              .Skip(0).Take(20)                                               │
│     - 翻译成 SQL                                                             │
│                                     │                                        │
│  ⑨ SQL Server                                                               │
│     SELECT * FROM Stocks WHERE TenantId=@tid AND Warehouse='W01'             │
│       ORDER BY ... OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY                     │
│                                     │  返回行                                │
│  ⑧←⑨ EF 把行映射成 Stock 实体列表                                           │
│  ⑦←⑧ Service 组装成分页 DTO { items:[...], total:123, page:1 }              │
│  ⑥←⑦ Controller 包成统一响应 { code:0, data:{...}, message:null } → JSON    │
└═════════════════════════════════════╤══════════════════════════════════════┘
                                     │  HTTP 200 + JSON body
                                     ▼
  ③←④ http.ts 响应拦截器（前端）
     - 成功：return response.data → 剥掉 axios 外壳，返回业务数据
     - （若 401：自动 refresh + 重放；若业务错误码：ElMessage.error(t(code)) 弹提示）
                                     │  Promise resolve
                                     ▼
  ②←③ stockApi.search() 拿到类型化数据 WmsApi<WmsPaged<Stock>>
                                     │
                                     ▼
  ①←② StockQueryView：const res = await stockApi.search(query)
     - tableData.value = res.data.items   ← 响应式赋值
     - Vue 侦测到 ref 变化 → 重渲染 VolTable → 用户看到库存列表 ✅
```

**串讲要点（面试口述版）**：
> 用户点查询，页面组件收集条件调 `stockApi.search()`；API 层委托给封装的 axios 实例；请求拦截器加 baseURL、带 httpOnly cookie 认证、按需加 CSRF；经 Vite proxy/nginx 转发到后端（同源无跨域）。后端认证中间件解 JWT 校验身份和租户，`[RequirePermission]` 查权限（前端藏按钮只是 UX，这才是真闸门），Controller 模型绑定→Service 加租户过滤业务逻辑→EF 把 LINQ 翻成 SQL→SQL Server 查数据，原路返回，Controller 包成统一 JSON。前端响应拦截器剥壳返回业务数据（顺带统一处理 401 刷新和错误提示），页面把数据赋给响应式 ref，Vue 自动重渲染表格。整条链路里前端管体验和请求编排，后端管业务和安全，权限做了前端 UX + 后端强校验的双层。

**这张图的价值**：它把本章（前端）和 Day 1（后端）焊在一起，还自然带出了权限双层、多租户、拦截器、响应式渲染——一图讲清你对全栈数据流的理解。

---

## 章末：面试题 20 问（详细答案）

**1. 父子组件怎么通信？props 为什么是只读的？**
props 父→子传数据，emit 子→父传事件。props 只读是为保证单向数据流、来源可追踪。子组件想改：需父同步就 emit 让父改（配 v-model）；只是本地编辑就 ref 拷副本；派生值用 computed。直接改对象型 prop 内部会绕过父污染上游，是 bug。

**2. v-model 的本质？自定义组件怎么支持？多个 v-model？**
v-model = 一个 prop（默认 `modelValue`）+ 一个 emit（`update:modelValue`）的语法糖。自定义组件接 `modelValue` prop、emit `update:modelValue` 即可；具名用 `v-model:foo`（prop `foo` + emit `update:foo`），一个组件挂多个具名 v-model 支持多向绑定。Vue 3.4+ 用 `defineModel('foo')` 一行返回可读写 ref 代替手写。

**3. provide/inject 和 Pinia 怎么选？**
provide/inject 是沿组件树分支注入上下文，适合组件库内部/局部子树共享，中间层免透传，默认非响应（要 provide ref）。Pinia 是应用级全局单例，跟树无关、任意组件甚至 .ts 文件可用、有 devtools/类型/持久化。全局状态用 Pinia，局部子树上下文用 provide/inject，父子直连用 props/emit。

**4. 什么时候用模板 ref + defineExpose？**
用于命令式调用——父主动触发子的动作（focus、手动 validate、reload、控制动画），不是传数据。子必须 `defineExpose` 显式暴露方法（`<script setup>` 默认全私有）。优先 props/emit，ref 只用于命令场景。

**5. 为什么需要状态管理？prop drilling 是什么？**
跨无关组件/多层共享同一份数据时，props 逐层透传（prop drilling）不可维护、兄弟组件难同步、非组件代码拿不到状态。状态管理把这类数据集中成全局单例，任意组件/文件直接取，来源单一、可追踪。

**6. Pinia 相比 Vuex 的优势？**
去掉 mutation（action 一层，同步异步统一）、TS 一等公民自动推导、每个 store 天然模块化无命名空间烦恼、支持组合式 setup store、体积极小。Vue 3 新项目一律 Pinia。

**7. `const { count } = useStore()` 有什么问题？**
store 是 reactive 对象，直接解构丢响应性——count 变静态值，store 更新它不变。要用 `storeToRefs(store)` 解构 state/getter（返回 ref 保持响应），action 可直接从 store 解构。根因同 reactive 对象不能直接解构。

**8. setup store 和 option store 区别？**
setup store 是传函数，用 ref/computed/function（和组件 setup 一致，能用任意组合式 API、定义私有变量），必须 return 暴露项。option store 是传对象 state/getters/actions，靠 this。cp6.web 全用 setup store，心智和组件统一。

**9. SPA 路由原理？history 和 hash 区别？**
SPA 用 JS 改 URL 换组件而不刷新整页。history 模式（History API，URL 干净如 `/wms/stock`）需服务器把未匹配路径 fallback 到 index.html，否则刷新 404。hash 模式（URL 带 `#`，改 hash 不发请求）无需服务器配置、刷新不 404，但 URL 丑、SEO 差。

**10. 路由懒加载怎么做？为什么？**
路由组件写成 `() => import('...')` 动态导入，打包器把每个页面单独打成 chunk，用户访问到才下载。避免几百个页面全塞进主包导致首屏巨大，实现按需加载。cp6.web 几百条路由全用懒加载。

**11. 怎么用路由守卫做登录/权限拦截？**
全局前置守卫 `router.beforeEach((to,from,next)=>{})`。先放行登录页等白名单（否则死循环）；读登录标志没登录 `next('/login')`；按 meta/路径判权限无权限改道；通过则 `next()`。每分支恰好调一次 next。cp6.web 还处理刷新后动态路由丢失（从 localStorage 恢复菜单重建路由再 `next({...to,replace:true})`）。

**12. 前端权限控制怎么做？前端藏了按钮就安全了吗？**
前端把权限键存 Pinia（Set），v-permission 指令读 `store.has(key)` 决定按钮显隐——这只是 UX。绝不安全：懂行的人能直接发 HTTP 请求。真正安全边界在后端每个接口的 `[RequirePermission]`。前端权限 = 体验，后端权限 = 安全，双层。

**13. 为什么要封装 axios？拦截器干嘛的？**
统一收口认证、CSRF、错误处理、多语言、baseURL，避免每个请求重复写。请求拦截器发请求前加工（加 CSRF 头、代理态头）；响应拦截器回来后加工（剥 data 外壳、统一错误 toast、401 自动刷新 token 重放）。

**14. 多个请求同时 401 怎么办？**
用模块级共享的 refresh promise：第一个 401 触发刷新并存 promise，后续 401 复用同一个 promise 不重发，刷新完置空。避免并发重复刷新把 token 刷乱。刷新成功后用 `config._retried` 标志重放原请求（防无限重放），重放/刷新失败才踢回登录。

**15. CSRF 怎么防的？为什么 token 放 httpOnly cookie？**
双提交 cookie：后端下发非 httpOnly 的 csrf cookie（JS 可读），前端改数据的请求从 cookie 读值塞进 `X-CSRF-Token` 头，后端校验 cookie 与头一致。跨站攻击能自动带 cookie 但读不到值、塞不对头 → 拦截。认证 token 放 httpOnly cookie 是让 JS 偷不到（防 XSS）；前端另存非敏感登录标志判断登没登。

**16. 盒模型 content-box 和 border-box 区别？**
content-box（默认）width 只算内容，加 padding/border 盒子变宽易算错；border-box width 含 padding/border，设多宽就多宽 padding 往里挤，直观。现代项目全局 `border-box`。

**17. Flex 怎么水平垂直居中？主轴交叉轴谁管？**
`display:flex` 后 `justify-content:center`（主轴，默认 row 时水平）+ `align-items:center`（交叉轴，垂直）。主轴由 flex-direction 决定（row 水平 / column 垂直），交叉轴垂直于主轴——改 column 后 justify-content 变管垂直，最易记反。

**18. scoped 样式原理？为什么改不了 el-input 内部？怎么改？**
scoped 编译期给本组件元素加唯一属性（data-v-hash）并把选择器改写成属性选择器 `.x[data-v-hash]`，只命中本组件元素（不是 Shadow DOM）。Element 组件内部元素是子组件渲染的没有该 hash，选不中。用 `:deep(.el-input__wrapper)` 穿透——它只给前面父选择器加 hash、目标不加。

**19. 为什么 Vite 比 webpack 快？**
dev 期 Vite 不打包，用浏览器原生 ESM + esbuild 按需即时编译（请求哪个模块编译哪个），秒级启动、HMR 只更新改动模块；webpack 要先把整个依赖图打成 bundle 才能启动，大项目慢。生产构建 Vite 用 Rollup 仍完整打包，两者一个量级。

**20. `vite build` 做类型检查吗？开发跨域怎么解决？**
不做。esbuild 只剥类型转译不校验，所以 build 脚本用 run-p 并行单独跑 `vue-tsc` 保证类型正确。开发跨域用 `server.proxy`：前端走相对路径 `/api` 打到 dev server（同源），Vite 转发给后端，绕过 CORS；生产由 nginx 做同样的 /api 反代。前端代码永远只写相对路径。

---

## 自测清单

对着下面每一条，能不看答案讲清楚就算过：

- [ ] 说出 7 种组件通信方式 + 各自适用场景（背决策表）
- [ ] 解释 props 为什么只读，子组件想改的 3 种正确做法
- [ ] 手写一个支持 `v-model` 的自定义组件（prop+emit 版 和 defineModel 版各一遍）
- [ ] 说清 provide/inject 和 Pinia 的选型边界
- [ ] 讲 defineExpose 为什么必要
- [ ] 用 counter.ts 讲 setup store 的 state/getter/action
- [ ] 讲 permission.ts 如何和后端 RoleAction 权限体系对接（前端 UX + 后端强校验双层）
- [ ] 解释 storeToRefs 的坑和原理
- [ ] 说 history vs hash 模式，及 history 模式部署要配什么
- [ ] 解释路由懒加载 `() => import()` 的作用
- [ ] 逐段讲 cp6.web 的 beforeEach 守卫（白名单/登录/权限/刷新恢复）
- [ ] 逐行讲 http.ts：baseURL、withCredentials、CSRF 注入、401 刷新重放、错误码 i18n
- [ ] 说清 API 层三层结构（http.ts / api 模块 / 页面）
- [ ] 讲盒模型、Flex 主轴交叉轴、居中三法
- [ ] 讲 scoped 原理和 `:deep()` 为什么需要
- [ ] 读懂一段 `@media (max-width:767px)` 并说出多栏→单列重排
- [ ] 说 Vite 为什么快（dev ESM 按需编译）
- [ ] 讲 vite.config 的 proxy 怎么解决开发跨域、alias `@` 是什么
- [ ] 说 build 脚本为什么要并行跑 vue-tsc
- [ ] 讲测试金字塔 + Vitest 单测 + Playwright E2E 各测什么
- [ ] 讲 i18n 工程化：DB 驱动 + 懒加载 + CI 缺 key 拦截
- [ ] 完整口述"库存查询"从 Vue 到 SQL 的全链路

---

## 动手练习 3 个

> 建议在 `C:\CP6\cp6.web` 里真做（`npm run dev` 起服务），做完对照真实代码。

### 练习 1：手写一个带 v-model 的搜索框组件（组件通信）
**目标**：巩固 props / emit / v-model / defineModel。
**要求**：
1. 新建 `SearchInput.vue`，支持 `v-model`（输入框值）和 `v-model:loading`（加载态，第二个 model）。
2. 内部有个"清空"按钮，点了把值清空并 emit 一个 `clear` 事件。
3. 用两种方式各实现一遍：(a) 手写 `defineProps`+`defineEmits`；(b) 用 `defineModel`。
4. 在一个父页面里用它：`<SearchInput v-model="kw" v-model:loading="busy" @clear="onClear" />`。
**验收**：输入时父的 `kw` 跟着变；点清空父 `kw` 变空且 `onClear` 被调；对照 `CpFilterBar.vue`（手写版）和 `VolForm.vue` 第 90 行（defineModel 版）看差异。

### 练习 2：给路由加一个"页面标题 + 权限 meta"守卫（路由 + Pinia）
**目标**：巩固 meta、beforeEach、afterEach、Pinia。
**要求**：
1. 给 2-3 条路由的 `meta` 加 `title` 和 `requirePerm`（如 `'wms/stock:query'`）。
2. 写一个 `router.afterEach`，用 `to.meta.title` 设置 `document.title`。
3. 在 `beforeEach` 里加一段：如果 `to.meta.requirePerm` 存在且 `usePermissionStore().has(perm)` 为 false，就 `next('/')` 并 `ElMessage.warning('无权限')`。
4. 用 storeToRefs 在某个组件里响应式读 `permission` store 的 `loaded` 状态，loaded 前显示骨架屏。
**验收**：切页面时浏览器标签标题变；模拟无权限时被拦回首页；对照 `router/index.ts` 的真实 beforeEach 和 `permission.ts`。

### 练习 3：给一个 store 写 Vitest 单测 + 造一个 axios 错误处理场景（测试 + http）
**目标**：巩固 Vitest、store 测试、拦截器理解。
**要求**：
1. 参照 `oaActingAs.test.ts`，给 `counter.ts` 写单测：测 `increment` 让 `count` +1、`doubleCount` 是 `count*2`。（提示：测 Pinia store 要先 `setActivePinia(createPinia())`。）
2. 写一个"坏数据不崩"的用例（如给 store 一个非法输入，断言优雅处理）。
3. 阅读题（写成注释）：在 `http.ts` 响应拦截器里，如果后端返回 `409`，为什么故意不弹全局 toast？如果同时来 3 个 401，会发几次 `/auth/refresh`？为什么？
**验收**：`npm run test` 全绿；两个阅读题答案对照 5.4.2 的 409 段和 doRefresh 段。

---

> **本章小结**：组件通信是"组件之间怎么说话"，Pinia 是"全局怎么共享"，路由是"页面之间怎么跳 + 怎么拦"，http.ts 是"前后端怎么联"，CSS 是"长什么样 + 各屏幕怎么适配"，Vite 是"怎么开发和打包"，测试和工程质量是"怎么保证不出错"。把 cp6.web 的 `http.ts`、`router/index.ts`、`permission.ts` 这三个标本讲透，你就拿下了这场面试前端部分的绝大多数问题。明天（Day 3）我们会做综合模拟面试，把 Day 1 后端 + Day 2 前端串起来实战。
