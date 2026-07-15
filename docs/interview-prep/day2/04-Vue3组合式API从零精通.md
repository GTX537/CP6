# Day 2 · 第 4 章：JavaScript/TypeScript 地基 + Vue 3 组合式 API 从零精通

> 面向对象：有后端（尤其是 C# / .NET）经验、Vue 前端从零起步的学员。
> 目标岗位：制造业生产管理系统前端开发工程师（JS / CSS / Vue / Element Plus，5 年经验强度）。
> 全部代码标本来自真实生产前端 `C:\CP6\cp6.web`（Vue 3.5 + TypeScript + Vite + Element Plus + Pinia + vue-router + vue-i18n）。
> 学习方式固定为：**概念（与 C# 类比）→ cp6.web 真实代码（标路径）→ 逐行解析 → 坑 → 面试问答**。

---

## 本章导航

| 小节 | 主题 | 面试权重 |
|---|---|---|
| §1 | 现代 JavaScript 速成（后端转全栈视角） | ★★★★ |
| §2 | TypeScript 速成 | ★★★★ |
| §3 | Vue 3 心智模型（声明式 vs 命令式、SFC 三段结构） | ★★★ |
| §4 | 响应式系统深讲（ref/reactive/computed/watch/Proxy） | ★★★★★ |
| §5 | 模板语法全集（v-if/v-for/v-model/修饰符） | ★★★★★ |
| §6 | 生命周期钩子 | ★★★★ |
| §7 | `<script setup>` 语法糖（defineProps/Emits/Expose） | ★★★★ |
| §8 | 组合式函数 composables | ★★★★ |
| §9 | 选项式 API 对照（Vue2 经验应对） | ★★★ |
| §10 | 后端视角类比总结表 | ★★★ |
| §11 | 面试题 20 问 + 自测清单 + 动手练习 | ★★★★★ |

---

# §1 现代 JavaScript 速成（后端转全栈视角）

你已经会 C#。JavaScript 的语法乍看陌生，但**核心概念 80% 都能在 C# 里找到对应物**。这一节我们用「C# 你已经会的东西」当锚点，把 JS 一次性铺平。

## 1.1 `let` / `const`（≈ C# 的局部变量与 `readonly` 局部）

**概念（与 C# 类比）**

| JS | C# 类比 | 说明 |
|---|---|---|
| `const x = 1` | `readonly` 局部（借喻） | 变量**绑定**不可重新赋值；但对象内部仍可改 |
| `let x = 1` | 普通局部变量 | 可重新赋值 |
| `var x = 1` | ❌ 老写法，函数作用域、有变量提升坑 | **现代代码禁用** |

关键点：`const` 锁的是「变量指向谁」，不是「对象内容」。这点跟 C# 的 `readonly` 引用字段一样——`readonly List<T> list` 你不能 `list = new()`，但能 `list.Add()`。

```js
const arr = [1, 2, 3]
arr.push(4)      // ✅ 合法，改的是数组内容
// arr = [5, 6]  // ❌ TypeError: Assignment to constant variable
```

**cp6.web 真实代码**（`C:\CP6\cp6.web\src\composables\useBreakpoint.ts` 第 3-11 行）：

```ts
const MOBILE_MAX = 767
const TABLET_MAX = 991

const width = ref(typeof window !== 'undefined' ? window.innerWidth : 1280)

let listenerCount = 0
function onResize() {
  width.value = window.innerWidth
}
```

**逐行解析**
- `const MOBILE_MAX = 767`：常量阈值，永不改，用 `const`。这是全项目约定——**默认一律 `const`，只有确定要重新赋值时才改 `let`**。
- `const width = ref(...)`：`width` 这个**绑定**是常量，但它指向的 ref 对象内部的 `.value` 会变（见 §4）。这是 Vue 里 `const` + `ref` 的经典组合——绑定不变，值可变。
- `let listenerCount = 0`：这个数字要 `listenerCount++`（重新赋值），所以必须 `let`。

**坑**
- 用 `const` 声明对象后以为「整个对象冻结了」——错，只是绑定冻结。要真冻结用 `Object.freeze()`。
- 面试高频：`var` 的**变量提升（hoisting）**和**函数作用域**会导致 `for` 循环里闭包全部捕获同一个变量。`let` 是块级作用域，每轮循环一个新绑定，没这毛病。记住结论即可：**永远别用 `var`**。

## 1.2 箭头函数与 `this`（面试必考的 `this` 绑定）

**概念（与 C# 类比）**

箭头函数 `=>` 语法上像 C# 的 lambda `=>`，但有个**决定性区别**：

- C# lambda 的 `this` 指向定义它的类实例（词法绑定），你从没为此烦恼过。
- JS **普通函数** `function(){}` 的 `this` 是**调用时决定的**（谁调用就指谁），这是 JS 最大的坑之一。
- JS **箭头函数** `()=>{}` 没有自己的 `this`，它捕获**定义时外层的 `this`**——**这才和 C# lambda 一致**。

```js
// 普通函数：this 由调用方决定，容易丢
const obj = {
  name: 'A',
  greetBad: function () {
    setTimeout(function () {
      console.log(this.name)   // ❌ undefined，this 是 setTimeout 的调用上下文
    }, 100)
  },
  greetGood: function () {
    setTimeout(() => {
      console.log(this.name)   // ✅ 'A'，箭头函数捕获外层 this
    }, 100)
  },
}
```

**cp6.web 真实代码**（`useBreakpoint.ts` 第 28-30 行）——箭头函数作为 computed 的 getter：

```ts
const isMobile = computed(() => width.value <= MOBILE_MAX)
const isTablet = computed(() => width.value > MOBILE_MAX && width.value <= TABLET_MAX)
const isDesktop = computed(() => width.value > TABLET_MAX)
```

**逐行解析**
- `() => width.value <= MOBILE_MAX`：无参箭头函数，函数体是单表达式时**自动 return**（省略 `{}` 和 `return`）。等价于 `() => { return width.value <= MOBILE_MAX }`。
- 在 `<script setup>` 组合式 API 里，你**几乎不会用到 `this`**——数据都是 `ref`/`reactive` 变量，直接闭包访问，没有 `this` 概念。这也是组合式 API 相比 Vue2 选项式的一大优势（选项式里 `data`/`methods` 全靠 `this` 串起来，`this` 一丢就崩）。

**坑**
- 箭头函数**不能**当构造函数（不能 `new`），没有 `arguments` 对象，不能用作对象方法里需要动态 `this` 的场景。
- 面试题：「箭头函数和普通函数区别？」标准答案三点：① `this` 词法绑定（不可被 `call`/`bind` 改变）② 无 `arguments` ③ 不能 `new`。

## 1.3 解构（destructuring，≈ C# 元组解构 / 模式匹配）

**概念（与 C# 类比）**

C# 你写过 `var (a, b) = tuple;` 或 `var (x, y) = point;`。JS 的解构更强，能从对象/数组按名字/位置拆值。

```js
// 对象解构（按属性名）
const user = { id: 1, name: 'A', role: 'admin' }
const { name, role } = user           // name='A', role='admin'
const { name: userName } = user       // 改名：userName='A'
const { age = 18 } = user             // 默认值：user 没 age，age=18

// 数组解构（按位置）
const [first, second] = [10, 20]      // first=10, second=20
```

**cp6.web 真实代码**（`C:\CP6\cp6.web\src\utils\format.ts` 第 78 行）——从 `useI18n()` 返回值里解构出需要的方法：

```ts
export function useFormat() {
  const { n, d, locale } = useI18n()
  return {
    formatDate: (v: DateInput, fmt: DateFormatKey = 'short') => {
      const dt = toDate(v)
      return dt ? d(dt, fmt) : ''
    },
    // ...
  }
}
```

**逐行解析**
- `const { n, d, locale } = useI18n()`：`useI18n()` 返回一个大对象（含十几个属性），我们**只解构出 `n`（数字格式化）、`d`（日期格式化）、`locale`（当前语言）** 三个。这是 Vue 生态最常见的用法——组合式函数返回一堆东西，调用方按需解构。
- 对比 §4 会讲的坑：**从 `reactive` 对象解构会丢失响应性**，但从组合式函数解构 `ref`/`computed`/函数**不会丢**（因为解构出来的还是原来那个 ref 对象引用）。这个区别是面试重灾区，§4 详解。

**坑**
- 解构 `reactive` 对象的**普通属性值**会丢响应性（拷贝的是当时的值快照）。
- 函数参数解构 + 默认值组合很常见：`function f({ page = 1, size = 20 } = {}) {}`——最后那个 `= {}` 是防止不传参时 `undefined` 无法解构报错。

## 1.4 展开运算符 `...`（spread / rest，≈ C# `params` + 集合初始化器）

**概念（与 C# 类比）**

`...` 有两个身份：
- **展开（spread）**：把数组/对象「摊开」，类似 C# 的集合初始化器展开。
- **收集（rest）**：函数参数里收集剩余参数，类似 C# 的 `params object[]`。

```js
// 展开数组（浅拷贝 + 合并）
const a = [1, 2], b = [3, 4]
const merged = [...a, ...b]            // [1, 2, 3, 4]

// 展开对象（浅拷贝 + 覆盖）—— 不可变更新的核心技巧
const state = { page: 1, size: 20, keyword: '' }
const next = { ...state, page: 2 }    // { page: 2, size: 20, keyword: '' }

// rest 收集参数
function sum(...nums) { return nums.reduce((a, b) => a + b, 0) }
```

**cp6.web 真实代码**（`C:\CP6\cp6.web\src\views\dashboard\DashboardView.vue` 第 614 行）——不可变更新一个数组：

```ts
conn.on('BusinessNotification', (n: Notice) => {
  latestNotice.value = n
  feed.value = [n, ...feed.value].slice(0, 12)   // 新通知插到最前，只保留最新 12 条
  // ...
})
```

**逐行解析**
- `feed.value = [n, ...feed.value].slice(0, 12)`：
  - `[n, ...feed.value]`：新建一个数组，`n`（最新通知）放第一个，然后把旧数组 `feed.value` 全部展开接在后面。
  - `.slice(0, 12)`：截取前 12 个。
  - **整体是「不可变更新」**——不去 `feed.value.unshift(n)`（原地改），而是造一个**全新数组**赋值回去。这在 Vue 响应式里更安全，也是 React/Redux 时代传下来的好习惯。

**坑**
- `...` 是**浅拷贝**，嵌套对象仍共享引用。深拷贝要 `structuredClone(obj)` 或 `JSON.parse(JSON.stringify(obj))`（后者丢 Date/函数）。

## 1.5 模板字符串（template literals，≈ C# 字符串插值 `$"..."`）

**概念（与 C# 类比）**

C# 的 `$"Hello {name}"`，JS 是**反引号** `` `Hello ${name}` ``，占位符用 `${}`。支持多行、支持任意表达式。

```js
const name = 'A', count = 3
const msg = `用户 ${name} 有 ${count} 条待办`   // 用户 A 有 3 条待办
const calc = `总价 ${price * qty} 元`           // 里面能写表达式
```

**cp6.web 真实代码**（`StockQueryView.vue` 第 38 行，模板里用了模板字符串拼 i18n key）：

```html
<el-tag :type="qcTagOf(row.qcStatus)" size="small">{{ t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) }}</el-tag>
```

**逐行解析**
- `` `wms.stock.qc.${row.qcStatus || 'PENDING'}` ``：动态拼出一个国际化 key。如果 `row.qcStatus` 是 `'PASSED'`，key 就是 `wms.stock.qc.PASSED`；如果为空（`|| 'PENDING'` 兜底），就是 `wms.stock.qc.PENDING`。
- `t(...)` 是 vue-i18n 的翻译函数，把 key 变成当前语言的文案。

**坑**
- 别和普通单引号 `'...'`、双引号 `"..."` 混——只有**反引号**才有 `${}` 插值能力。写 `'Hello ${name}'` 不会插值，会原样输出 `Hello ${name}`。

## 1.6 数组方法（map/filter/reduce/find/some/every）—— 与 C# LINQ 对照表（重点！）

**概念（与 C# 类比）**

这是后端转前端**最爽的一节**——JS 数组方法几乎就是 LINQ，只是名字略变、语法链式。你会 LINQ，这节 5 分钟就懂。

### JS 数组方法 ↔ C# LINQ 对照表

| JS 方法 | C# LINQ | 作用 | 返回 |
|---|---|---|---|
| `arr.map(x => ...)` | `.Select(x => ...)` | 映射/投影，每项变换 | 新数组（等长） |
| `arr.filter(x => ...)` | `.Where(x => ...)` | 过滤，保留满足条件的 | 新数组（≤原长） |
| `arr.find(x => ...)` | `.FirstOrDefault(x => ...)` | 找第一个满足的 | 元素或 `undefined` |
| `arr.findIndex(x => ...)` | `.ToList().FindIndex(...)` | 找第一个满足的**下标** | 数字或 `-1` |
| `arr.some(x => ...)` | `.Any(x => ...)` | 是否**至少一个**满足 | `boolean` |
| `arr.every(x => ...)` | `.All(x => ...)` | 是否**全部**满足 | `boolean` |
| `arr.reduce((acc,x)=>...,init)` | `.Aggregate(init,(acc,x)=>...)` | 折叠/聚合成单值 | 任意类型 |
| `arr.includes(v)` | `.Contains(v)` | 是否包含某值 | `boolean` |
| `arr.sort((a,b)=>a-b)` | `.OrderBy(...)` | 排序（**原地修改！**） | 同一数组 |
| `arr.slice(0, n)` | `.Take(n)` | 取前 n 个 | 新数组 |
| `arr.flatMap(x=>...)` | `.SelectMany(...)` | 映射后拍平一层 | 新数组 |
| `arr.length` | `.Count()` / `.Length` | 元素个数 | 数字 |

**关键差异（面试爱问）**
1. LINQ 是**惰性求值**（延迟执行，`ToList()` 才真跑）；JS 数组方法是**立即求值**，每个 `.map`/`.filter` 立刻产生新数组。链多了会有中间数组开销（大数据量注意）。
2. `sort()` **原地修改并返回同一数组**，LINQ 的 `OrderBy` 返回新序列不改原集合。JS 的 `sort` 是坑——想不改原数组要先 `[...arr].sort()`。
3. `sort()` 默认按**字符串**比较（`[10,9,1].sort()` → `[1,10,9]`！），数字排序必须传比较器 `(a,b)=>a-b`。

**cp6.web 真实代码 1**（`useValidation.ts` 第 117 行）——`.some()` 判断「至少有一个数量 > 0」：

```ts
// 見積り数は 1 件以上必須
const hasQty = (dto.estimateQtys ?? []).some((q) => (q ?? 0) > 0)
if (!hasQty) {
  errors.push(t('MSG-W10011 見積り数量を 1 件以上入力してください'))
}
```

**逐行解析**
- `(dto.estimateQtys ?? [])`：`estimateQtys` 可能是 `undefined`，用 `?? []`（空合并，见 §1.8）兜底成空数组，避免对 `undefined` 调 `.some()` 报错。
- `.some((q) => (q ?? 0) > 0)`：只要**任意一个** `q` 大于 0 就返回 `true`。等价 C# 的 `dto.EstimateQtys.Any(q => (q ?? 0) > 0)`。

**cp6.web 真实代码 2**（`useLinkage.ts` 第 29 行）——`.find()` 查找是否存在：

```ts
if (basicInfo.value.staffCd && !staffList.value.find((s) => s.staffCd === basicInfo.value.staffCd)) {
  basicInfo.value.staffCd = undefined
}
```

**逐行解析**
- `staffList.value.find((s) => s.staffCd === basicInfo.value.staffCd)`：在担当者列表里找 `staffCd` 匹配当前选中值的那一项，找不到返回 `undefined`。
- `!...find(...)`：`undefined` 是假值，`!undefined` 为 `true`——即「当前选中的担当者**不在**新列表里」。
- 业务含义：切换受注拠点后重新拉了担当者列表，若原来选的人不在新名单，清空选择。等价 C# `if (staffCd != null && !staffList.Any(s => s.StaffCd == staffCd)) staffCd = null;`。

**cp6.web 真实代码 3**（`StockQueryView.vue` 第 151-167 行）——`.map` 思想的近亲：`columns` 用 `computed` 返回一个数组字面量，每个元素还带 `map` 回调格式化单元格：

```ts
const columns = computed<ListColumn[]>(() => [
  { prop: 'physicalQty', label: t('wms.stock.col.physical'), width: 120, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  // ...
])
```

- 这里的 `map: (v) => ({ label: formatQty(v as number) })` 是**列渲染回调**（把原始数量值格式化成带千分位的字符串）。注意 `(v) => ({ ... })`：箭头函数**返回对象字面量**必须用 `()` 包起来，否则 `{}` 会被当成函数体！这是 JS 高频语法坑。

**reduce 补充例**（LINQ `Aggregate` 对应，项目里用于求和/构建对象）：

```js
// 求和
const total = [1, 2, 3].reduce((acc, x) => acc + x, 0)          // 6
// 分组计数（构建对象）
const byType = orders.reduce((acc, o) => {
  acc[o.type] = (acc[o.type] || 0) + 1
  return acc
}, {})
```

**坑汇总**
- `map` **必须 return**（除非用简写体），忘了 return 得到一堆 `undefined`。
- 想遍历「做副作用、不要返回值」用 `forEach`（≈ C# `foreach`），别滥用 `map`。
- `find` 找不到返回 `undefined` 不是 `null`——判空要注意。

## 1.7 Promise / async-await（与 C# `Task` / `async-await` 对照）

**概念（与 C# 类比）**

这也是后端转前端的**送分题**——JS 的 `async/await` 和 C# 的 `async/await` 心智模型几乎一样。

| JS | C# | 说明 |
|---|---|---|
| `Promise<T>` | `Task<T>` | 表示未来的值 |
| `Promise`（无值） | `Task` | 表示未来完成 |
| `async function f(): Promise<T>` | `async Task<T> F()` | 异步方法 |
| `await p` | `await t` | 等待完成，拿到结果 |
| `Promise.all([...])` | `Task.WhenAll(...)` | 并发等全部 |
| `Promise.race([...])` | `Task.WhenAny(...)` | 等第一个 |
| `.then(v => ...)` | `.ContinueWith(...)` | 回调式（少用，优先 await） |
| `try/catch` | `try/catch` | 异步异常捕获，一样 |
| `.finally(...)` | `finally` | 无论成败都执行 |

**核心差异**
1. C# 有 `ConfigureAwait(false)` 和同步上下文问题；JS **没有**——JS 是单线程事件循环，`await` 只是把后续代码排进微任务队列，不涉及线程切换。
2. JS 一切异步都是 `Promise`（网络请求、定时器包装、SignalR 调用）。**`await` 只能用在 `async` 函数里**（顶层 `<script setup>` 支持顶层 await，但一般不用）。
3. 忘了 `await` 一个 Promise，C# 会警告，JS **静默**——你拿到的是 `Promise` 对象本身而不是结果值，这是最常见 bug。

**cp6.web 真实代码**（`StockQueryView.vue` 第 224-242 行）——完整的 async/await + try/catch/finally 请求处理：

```ts
async function onQcSave() {
  if (!qcTarget.value || !qcNewStatus.value) return
  qcSaving.value = true
  try {
    const res = await stockApi.setQcStatus(qcTarget.value.id, qcNewStatus.value, qcReason.value || undefined)
    if (res.code === 0 && res.data) {
      ElMessage.success(t('wms.stock.qc.savedMsg'))
      qcDialogVisible.value = false
      if (qcTarget.value) qcTarget.value.qcStatus = res.data.qcStatus
      reloadList()
    } else {
      ElMessage.error(res.message || 'Unknown error')
    }
  } catch (e: any) {
    ElMessage.error(e?.message ?? 'Network error')
  } finally {
    qcSaving.value = false
  }
}
```

**逐行解析**
- `async function onQcSave()`：声明异步函数，内部才能用 `await`。
- `qcSaving.value = true`：进入请求前把「保存中」标志位置真——模板里按钮 `:loading="qcSaving"` 会转圈、`:disabled` 会禁用，防止重复提交。这是**loading 态管理**的标准套路。
- `const res = await stockApi.setQcStatus(...)`：`await` 等 HTTP 请求返回。等价 C# `var res = await stockApi.SetQcStatusAsync(...)`。
- `if (res.code === 0 && res.data)`：业务成功码判断（后端约定 `code === 0` 成功）。
- `catch (e: any)`：**网络层异常**（超时、断网、500）走这里，`e?.message` 取错误消息（`?.` 可选链见 §1.8）。
- `finally { qcSaving.value = false }`：**无论成功失败**都要复位 loading——这是 finally 的经典用途，忘写会导致失败后按钮永远转圈。

**坑**
- **忘了 `await`**：`const res = stockApi.setQcStatus(...)`（漏 await），`res` 是 Promise 不是数据，`res.code` 恒为 `undefined`。
- 循环里 `await`：`for (const x of list) { await f(x) }` 是**串行**（一个接一个），想并发用 `await Promise.all(list.map(x => f(x)))`（≈ `Task.WhenAll`）。
- `async` 函数**总是返回 Promise**，即使你 `return 5`，外部拿到的是 `Promise<5>`，要 `await` 或 `.then`。

## 1.8 可选链 `?.` 与空合并 `??`（C# 同款语法糖）

**概念（与 C# 类比）**

这两个**和 C# 一模一样**，你已经会了：

| JS | C# | 作用 |
|---|---|---|
| `a?.b` | `a?.B` | a 为 null/undefined 时短路返回 undefined，不报错 |
| `a?.b?.c` | `a?.B?.C` | 链式安全访问 |
| `a?.()` | —（JS 特有） | 函数存在才调用 |
| `arr?.[i]` | —（JS 特有） | 数组安全下标 |
| `a ?? b` | `a ?? b` | a 为 null/undefined 时用 b |
| `a ??= b` | `a ??= b` | a 为空才赋值 |

**唯一要注意的差异**：JS 里 `??` 只在 `null`/`undefined` 时兜底，**不包括** `0`、`''`、`false`。这跟 `||`（逻辑或）不同——`||` 对所有「假值」（`0`/`''`/`false`/`null`/`undefined`/`NaN`）都兜底。所以数字/字符串字段要用 `??` 而不是 `||`，否则 `0` 会被误当空值替换掉。

```js
const qty = 0
qty || 10      // 10 ❌ 把合法的 0 也替换了
qty ?? 10      // 0  ✅ 只有 null/undefined 才替换
```

**cp6.web 真实代码 1**（`useValidation.ts` 第 117、123 行）——`??` 兜底：

```ts
const hasQty = (dto.estimateQtys ?? []).some((q) => (q ?? 0) > 0)
// ...
if ((dto.bladeWidth ?? 0) > 0 && (dto.bladeFlow ?? 0) <= 0) {
```

- `dto.estimateQtys ?? []`：数组可能没定义，兜空数组。
- `(q ?? 0) > 0`：单个元素可能是 `undefined`，兜 `0` 再比较——这里**必须用 `??` 不能用 `||`**，虽然 `0 || 0` 也是 0，但语义上 `??` 更精确（只处理空值）。

**cp6.web 真实代码 2**（`StockQueryView.vue` 第 26、237 行）——`?.` 可选链：

```html
<template #col-expiryDate="{ row }">{{ row.expiryDate?.slice(0, 10) || '—' }}</template>
```
```ts
} catch (e: any) {
  ElMessage.error(e?.message ?? 'Network error')
}
```

**逐行解析**
- `row.expiryDate?.slice(0, 10)`：`expiryDate` 可能为 `null`（无期限），`?.` 保证 null 时不调 `.slice` 报错，整体返回 `undefined`，再 `|| '—'` 显示占位符。
- `e?.message ?? 'Network error'`：错误对象可能不规整，`e?.message` 安全取消息，`??` 兜底默认文案。这是 catch 块的**防御式写法**范本。

**坑**
- `?.` 短路后返回的是 `undefined`（不是 `null`），后续 `?? '默认'` 要用 `??` 才能兜住。
- 别过度链式 `a?.b?.c?.d?.e`——如果 `a` 一定存在，只在真正可能为空的环节加 `?.`，否则掩盖 bug。

## 1.9 模块 `import` / `export`（≈ C# `using` + `namespace`，但更细粒度）

**概念（与 C# 类比）**

C# 一个文件可以有多个类，`using Namespace` 引入整个命名空间。JS 的 ES Module 是**文件即模块**，显式 `export` 什么、`import` 什么，粒度到「符号级」。

| JS | 说明 | C# 类比 |
|---|---|---|
| `export function f() {}` | 命名导出 | `public` 成员 |
| `export default X` | 默认导出（一个文件一个） | 类似「主类型」 |
| `export type Foo = ...` | 导出类型（TS） | `public` 类型 |
| `import { a, b } from '...'` | 具名导入 | `using static` 挑成员 |
| `import X from '...'` | 导入默认导出 | — |
| `import * as ns from '...'` | 全部导入到命名空间 | `using Namespace` |
| `import type { T } from '...'` | 只导入类型（编译期擦除） | — |

**cp6.web 真实代码**（`format.ts` 第 12-19 行 + `StockQueryView.vue` 第 114-122 行）：

```ts
// format.ts —— 导出侧
import i18n from '@/i18n'                    // 默认导入
import { useI18n } from 'vue-i18n'           // 具名导入
export type DateFormatKey = 'short' | 'long' | 'time'   // 类型导出
export function formatDate(...) { ... }      // 具名函数导出
export function useFormat() { ... }          // 具名函数导出
```
```ts
// StockQueryView.vue —— 导入侧
import { ref, computed } from 'vue'                                  // Vue 核心 API
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'     // 默认导入组件
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'  // 混合
import { stockApi } from '@/api/wms/stock'
import type { Stock, StockTransaction } from '@/types/wms/wms'       // 纯类型导入
import { formatQty as fmtQty } from '@/utils/format'                 // 导入并重命名
```

**逐行解析**
- `import { ref, computed } from 'vue'`：从 vue 包挑出这两个函数。**用什么导什么**，没用的别导（打包体积、可读性）。
- `import CpPageShell from '...'`：`.vue` 文件默认导出组件对象，用无花括号的默认导入。
- `import CpListPage, { type ListColumn, type ListFetch } from '...'`：**混合导入**——默认导出（组件）+ 具名类型导出，`type` 前缀表示只在类型层面用（编译后擦除，零运行时开销）。
- `import type { Stock, StockTransaction }`：**整句都是类型导入**，编译后这行完全消失。TS 项目推荐把纯类型导入用 `import type` 明确标注，帮助打包器 tree-shaking。
- `import { formatQty as fmtQty }`：导入时重命名，避免和本文件内定义的同名 `formatQty` 局部函数冲突（第 133 行有个本地 `formatQty` 包了一层）。
- `@/` 是路径别名，配置在 vite 里指向 `src/`——省得写 `../../../` 相对路径地狱。

**坑**
- **循环依赖**：A import B，B import A，可能导致某一方拿到 `undefined`。JS 不像 C# 那样宽容，设计模块时注意分层。
- 默认导出 vs 具名导出选择：**一个文件只导一个主体用 default（如组件），工具集/多函数用具名导出**。cp6 约定工具、composable 全用具名导出（`format.ts` 导出十几个函数）。

---

# §2 TypeScript 速成

## 2.1 为什么要类型（对 C# 开发者是「回家」）

你从 C# 来，天然理解**静态类型的价值**：编译期抓错、IDE 智能提示、重构安全、类型即文档。JavaScript **本身是动态弱类型**（像放飞的 Python），大项目里 `undefined is not a function` 满天飞。TypeScript = JavaScript + 类型系统，编译成纯 JS 运行。

**CP6 前端全 TS 的意义**：这个系统是**制造业生产管理系统**，实体繁多（订单、库存、工序、原纸、刀版……字段几十上百个），跨模块（WMS/ERP/MES/FIN/OA）。全 TS 意味着：
- 后端 DTO 变了，前端类型对不上，**编译期就报错**，不用等运行时白屏。
- `stock.availableQty` 打错成 `stock.avaliableQty`，红波浪线立刻提示。
- 团队 5 年经验强度协作，类型是**契约文档**，新人接手看类型定义就懂数据形状。

**关键认知**：TS 的类型**只在编译期存在**，打包后**全部擦除**，运行时就是纯 JS，没有任何类型检查开销（和 C# 运行时保留类型信息不同）。所以 TS 类型**不能**用于运行时判断（不能 `if (x is Foo)`），运行时判断得靠 `typeof`/`in`/自定义类型守卫。

## 2.2 `interface` / `type`（≈ C# `interface` / `class` DTO）

**概念（与 C# 类比）**

- `interface`：描述对象的「形状」，可扩展、可合并声明，最接近 C# 的 DTO/POCO 定义。
- `type`：类型别名，能表达 interface 能表达的一切，**还能表达联合类型、元组、映射类型**等 interface 做不到的。

经验法则：**描述对象结构优先 `interface`，需要联合/交叉/工具类型用 `type`**。

**cp6.web 真实代码**（`C:\CP6\cp6.web\src\types\wms\wms.ts` 第 3-14、51-64 行）：

```ts
export interface WmsApi<T> {
  code: number
  message: string
  data: T
}

export interface WmsPaged<T> {
  total: number
  page: number
  pageSize: number
  items: T[]
}

export interface Warehouse {
  id?: string
  warehouseCd: string
  warehouseName: string
  warehouseType: number // 1=原材料 2=半製品 3=完成品 4=不良品 5=外注
  baseCd?: string
  addressText?: string
  managerCd?: string
  allowNegative: boolean
  remarks?: string
  createDate?: string
  modifyDate?: string
  isDeleted?: boolean
}
```

**逐行解析**
- `export interface WmsApi<T>`：**泛型接口**（见 2.3），统一后端响应封装。等价 C# `public class WmsApi<T> { int Code; string Message; T Data; }`。
- `warehouseCd: string`：必填字段。
- `baseCd?: string`：**`?` 表示可选属性**（可以不存在或 `undefined`），等价 C# 的可空/可选。这个 `?` 极其重要——它告诉你「这个字段可能没有」，用之前得 `?.` 或 `??` 兜底。
- `warehouseType: number // 1=原材料...`：注释把「魔法数字」的业务含义写清楚，这是维护性好习惯。
- `allowNegative: boolean`：布尔字段，注意**没有 `?`**，是必填的。

**坑**
- 后端返回的字段前端 interface **少写了一个**，TS 不会报错（多出来的字段被忽略），但你 `.那个字段` 会报「不存在」。保持前后端类型同步是纪律活。
- `id?: string`——后端主键往往是「新建时没有、保存后才有」，所以标可选。别看到 `?` 就无脑非空断言 `id!`。

## 2.3 泛型（与 C# 泛型对照，几乎一模一样）

**概念（与 C# 类比）**

TS 泛型和 C# 泛型**心智完全一致**：类型参数 `<T>`、约束 `T extends X`（≈ C# `where T : X`）、多参数 `<T, U>`。

| TS | C# |
|---|---|
| `interface Box<T> { value: T }` | `class Box<T> { T Value; }` |
| `function f<T>(x: T): T` | `T F<T>(T x)` |
| `<T extends Base>` | `where T : Base` |
| `Array<T>` / `T[]` | `List<T>` / `T[]` |
| `Record<K, V>` | `Dictionary<K, V>`（近似） |
| `Partial<T>` | 无直接对应（所有字段变可选） |

**cp6.web 真实代码**（`wms.ts` 泛型接口的实际使用）：

```ts
export interface WmsApi<T> {   // 定义泛型
  code: number
  message: string
  data: T
}
export interface WmsPaged<T> {
  total: number
  items: T[]
}
```

用起来（`StockQueryView.vue` 第 196-197 行的 `res.data.items` / `res.data.total`）：

```ts
const res = await stockApi.search(q as never)
return { rows: res.data.items, total: res.data.total }
```

**逐行解析**
- `WmsApi<WmsPaged<Stock>>` 这样嵌套，`res.data` 就是 `WmsPaged<Stock>`，`res.data.items` 就是 `Stock[]`，`res.data.total` 是 `number`——**IDE 全程精确提示**，等价 C# `ApiResponse<Paged<Stock>>`。
- 泛型让「响应封装」写一次到处用，不用给每个实体都写 `StockApiResponse`、`OrderApiResponse`。

**坑**
- `q as never`：这是**类型断言**逃生舱（见 2.5），当类型推导对不上又确信运行时正确时的临时手段。真实项目里偶尔出现，面试可以诚实说「断言是最后手段，能不用就不用」。

## 2.4 联合类型 / 字面量类型（C# 没有直接对应，很强大）

**概念**

这是 TS 比 C# **更强**的地方，C# 没有原生等价物（C# 得用 enum 或继承层次模拟）。

- **联合类型** `A | B`：值可以是 A 或 B。
- **字面量类型** `'IN' | 'OUT'`：值只能是这几个**具体字符串/数字**之一——相当于「轻量级枚举」，但更灵活，直接是字符串值。

**cp6.web 真实代码**（`wmsHub.ts` 第 12-23 行 + `format.ts` 第 15-19 行）：

```ts
export interface StockChangedPayload {
  txnNo: string
  txnType: 'IN' | 'OUT' | 'MOVE' | 'ADJ' | 'RSV' | 'UNRSV'   // 字面量联合
  txnAt: string
  // ...
}
```
```ts
export type DateFormatKey = 'short' | 'long' | 'time'
export type NumberFormatKey = 'decimal' | 'integer' | 'percent'
type DateInput = Date | string | number | null | undefined     // 联合类型
type NumberInput = number | string | null | undefined
```

**逐行解析**
- `txnType: 'IN' | 'OUT' | 'MOVE' | 'ADJ' | 'RSV' | 'UNRSV'`：交易类型只能是这 6 个字符串之一。写错成 `'in'`（小写）编译期就报错。比 C# `enum TxnType { IN, OUT, ... }` 更轻——不用引入枚举类型，直接用字符串值，序列化 JSON 时天然就是字符串。
- `type DateInput = Date | string | number | null | undefined`：格式化函数**能接受多种输入**——真正的 `Date` 对象、ISO 字符串、时间戳数字、或空值。这让工具函数极其宽容好用（`format.ts` 第 21-25 行 `toDate` 就统一收敛这些输入）。

**cp6.web 真实代码 2**（`StockQueryView.vue` 第 138 行）——函数返回字面量联合：

```ts
function txnTagOf(v: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[v as 'IN'] || 'info'
}
```

- 返回值类型精确到 Element Plus 的 tag `type` 允许的几个值，传错颜色编译期就拦住。`as const` 让对象字面量的值被推断为**字面量类型而非宽泛的 string**，这样索引出来的结果才匹配返回类型。

**坑**
- 字面量联合改一个值（比如后端新增 `'RETURN'` 交易类型），要记得同步更新类型，否则新值编译期报错——这其实是**好事**，逼你不漏改。

## 2.5 类型收窄、`any` / `unknown` / `never`

**类型收窄（narrowing）**：TS 能根据 `if`/`typeof`/`in`/`===` 等把宽类型「收窄」到具体类型。

```ts
function toDate(v: DateInput): Date | null {   // format.ts 第 21-25 行
  if (v === null || v === undefined || v === '') return null   // 收窄：排除空值
  const d = v instanceof Date ? v : new Date(v)                // instanceof 收窄
  return isNaN(d.getTime()) ? null : d
}
```

**逐行解析**
- 入参 `v` 是 `Date | string | number | null | undefined`（很宽）。
- `if (v === null || ...) return null`：处理掉空值后，剩下 `Date | string | number`。
- `v instanceof Date ? v : new Date(v)`：`instanceof` 把 `v` 收窄——是 `Date` 直接用，否则（string/number）交给 `new Date()` 转换。这就是**类型收窄**，等价 C# 的 `is` 模式匹配。

**`any` / `unknown` / `never` 三兄弟**（面试常问区别）：

| 类型 | 含义 | 何时用 | C# 类比 |
|---|---|---|---|
| `any` | 关闭类型检查，什么都行 | **能不用就不用**，逃生舱 | `dynamic` |
| `unknown` | 「未知类型」，用之前**必须收窄** | 接不确定数据（如 JSON.parse） | `object`（但更严） |
| `never` | 「永不发生」的类型 | 穷尽检查、抛异常函数返回 | 无直接对应 |

- `any` 是**病毒**——一处 `any` 会污染整条链，类型全丢。CP6 里 `catch (e: any)` 是常见妥协（异常类型确实不确定），但业务数据尽量别 `any`。
- `unknown` 更安全：`const data: unknown = JSON.parse(s); data.foo`（❌ 报错，必须先 `if (typeof data === 'object' && data && 'foo' in data)` 收窄）。
- `never`：函数一定抛异常或死循环时返回 `never`；`switch` 穷尽所有字面量后 `default` 分支的变量会被收窄成 `never`，可用于「编译期强制处理所有情况」。

**坑**
- 看到别人代码里一堆 `any` 别学——那是技术债。面试若问「怎么减少 any」：优先 `unknown` + 类型守卫，给外部数据定义 interface，泛型传递类型。

---

# §3 Vue 3 心智模型

## 3.1 声明式渲染 vs 命令式 DOM 操作（jQuery 时代对比）

**核心转变**：你可能听过 jQuery。jQuery 是**命令式**——你手动「找到元素、改它」：

```js
// jQuery 命令式（老时代，别这么写了）
$('#qty').text(stock.availableQty)
if (stock.availableQty < 0) $('#qty').addClass('neg')
$('#saveBtn').on('click', () => { /* ... */ })
```

问题：数据一变，你得**手动记得**去更新每一处 DOM，漏一处就界面和数据不一致。5000 行页面全是「找元素、改元素」，无法维护。

**Vue 是声明式**——你只描述「数据长这样时，界面应该长啥样」，数据变了 Vue **自动**更新 DOM：

```html
<!-- Vue 声明式：StockQueryView.vue 第 22-24 行 -->
<span :class="{ neg: row.availableQty < 0 }">{{ formatQty(row.availableQty) }}</span>
```

- 你**声明**：`availableQty < 0` 时加 `neg` 类，内容是 `formatQty(row.availableQty)`。
- `availableQty` 变了？Vue 自动重渲染这个 `<span>`，自动加/去 `neg` 类。**你从不碰 DOM**。

**类比 C#**：命令式 ≈ WinForms 手动 `label.Text = ...`；声明式 ≈ WPF/XAML 数据绑定 `{Binding Qty}`。Vue 就是「Web 版的数据绑定」，你会 WPF 的话这个心智直接迁移。

## 3.2 组件树

Vue 应用是一棵**组件树**：根组件 `App.vue` 挂载子组件，子组件再挂子组件。数据通过 **props 向下流**（父传子），事件通过 **emit 向上冒**（子通知父）。

```
App.vue
 └─ Layout
     └─ StockQueryView.vue          （页面级组件）
         ├─ CpPageShell             （外壳：标题+计数）
         │   └─ CpListPage          （通用列表：分页+搜索+表格）
         └─ el-dialog               （Element Plus 弹窗组件）
             └─ el-form / el-radio-group / el-input
```

`StockQueryView.vue` 第 9-17 行就是这个树的写法：

```html
<CpPageShell :title="t('wms.stock.title')" :count="total">
  <CpListPage
    ref="listRef"
    :columns="columns"
    :fetch="fetchList"
    @total-change="total = $event"
  >
```

- `<CpPageShell>` 是父，`<CpListPage>` 是它的子（写在其内容里）。
- `:title="..."`、`:columns="..."` 是**props 向下传数据**。
- `@total-change="total = $event"` 是**监听子组件冒上来的事件**（子组件算出总数，emit 出来，父接住赋给 `total`）。

**类比 C#**：组件 ≈ 类，组件树 ≈ 对象组合树，props ≈ 构造参数/属性注入，emit ≈ 事件（`event`/`Action` 回调）。详见 §10 总表。

## 3.3 单文件组件 SFC 三段结构

Vue 的 `.vue` 文件叫 **SFC（Single File Component，单文件组件）**，一个文件三段：

```html
<template>  <!-- 结构：HTML + Vue 指令 -->
  ...
</template>

<script setup lang="ts">  <!-- 逻辑：TS + 组合式 API -->
  ...
</script>

<style scoped>  <!-- 样式：CSS，scoped 隔离到本组件 -->
  ...
</style>
```

**cp6.web 真实结构**（`StockQueryView.vue` 的三段边界）：
- 第 8-111 行 `<template>`：整个页面 UI。
- 第 113-243 行 `<script setup lang="ts">`：所有逻辑（ref、computed、请求函数）。
- 第 245-248 行 `<style scoped>`：

```html
<style scoped>
.neg { color: var(--cp-danger); font-weight: 600; }
.qc-info { margin-bottom: 12px; padding: 8px 12px; background: var(--cp-bg-th); border-radius: var(--cp-r-sm); font-size: var(--cp-fs-base); }
</style>
```

**三段要点**
- `<script setup lang="ts">`：`setup` 是组合式 API 语法糖（§7），`lang="ts"` 启用 TypeScript。**这是 CP6 全项目统一写法**。
- `<style scoped>`：`scoped` 让这些 CSS **只作用于本组件**，不污染全局（Vue 编译时给元素加唯一属性 `data-v-xxx`，选择器自动带上）。所以你写 `.neg` 不怕和别的页面 `.neg` 冲突。
- `var(--cp-danger)` 是 CSS 变量（设计系统统一色板），不是硬编码颜色——换肤/暗色模式只改变量。

**类比 C#**：SFC ≈ 把 XAML（template）+ code-behind（script）+ 样式资源（style）打包进一个文件的「自包含控件」。

---

# §4 响应式系统深讲（面试核心！）

这是 Vue 面试的**第一考点**，也是新手最容易栽的地方。务必吃透。

## 4.1 什么是「响应式」

**响应式（reactivity）** = 数据变了，依赖它的地方（模板、computed、watch）**自动更新**。你不用手动通知。

普通 JS 变量做不到这点：

```js
let count = 0
count = 5        // 模板不会知道，界面不更新
```

Vue 需要把变量「包装」成响应式对象，才能在它变化时触发更新。包装方式有两种：`ref` 和 `reactive`。

## 4.2 `ref` vs `reactive`（面试必背对照表）

### ref vs reactive 对照表

| 维度 | `ref` | `reactive` |
|---|---|---|
| 适用类型 | **任何值**（原始值 number/string/boolean + 对象） | **只能对象/数组/Map/Set** |
| 访问方式 | `.value`（脚本里） | 直接 `.属性`（无 `.value`） |
| 模板里 | **自动解包**，不写 `.value` | 直接用 |
| 重新赋整个值 | `x.value = newObj` ✅ | `x = newObj` ❌（丢响应性） |
| 解构 | 解构出的 ref 保持响应（`toRefs` 场景） | **解构普通属性丢响应性** ⚠️ |
| 底层 | `RefImpl` 对象 + getter/setter | ES6 `Proxy` |
| 推荐度 | **CP6 首选，几乎全用 ref** | 少数「一组固定字段」场景 |

**cp6.web 真实代码——ref 遍地都是**（`StockQueryView.vue` 第 126-215 行节选）：

```ts
const total = ref<number>()                                    // 数字 ref（初始 undefined）
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)   // 组件实例 ref
const hasStockOnly = ref(true)                                 // 布尔 ref
const historyVisible = ref(false)
const historyStock = ref<Stock | null>(null)                   // 对象 ref
const historyTxns = ref<StockTransaction[]>([])                // 数组 ref
const qcNewStatus = ref<string>('')
const qcReason = ref('')
const qcSaving = ref(false)
```

**逐行解析**
- `const total = ref<number>()`：泛型 `<number>` 声明这个 ref 装数字，不传初值默认 `undefined`。**注意 `const`**——绑定不变，`total.value` 可变（`total.value = 5`）。
- `ref<Stock | null>(null)`：装「Stock 对象或 null」，初值 null。用的时候 `historyStock.value` 才是真正的对象。
- `ref<StockTransaction[]>([])`：装数组，初值空数组。

**脚本里访问必须 `.value`**（第 128、204-208 行）：

```ts
function reloadList() { listRef.value?.reload() }   // .value 拿组件实例，?. 安全调用

async function openHistory(row: Stock) {
  historyStock.value = row                          // 写：.value =
  const res = await stockApi.history(row.id, 365)
  historyTxns.value = res.data.transactions         // 写：整个数组替换
  historyVisible.value = true                       // 写：布尔翻转
}
```

**模板里自动解包，不写 `.value`**（第 48、86 行）：

```html
<el-dialog v-model="qcDialogVisible" ...>   <!-- 模板里直接 qcDialogVisible，不是 .value -->
<el-dialog v-model="historyVisible" ...>
```

- **这是新手最迷惑的点**：脚本里 `qcDialogVisible.value`，模板里 `qcDialogVisible`。因为 Vue 模板编译器会**自动解包顶层 ref**，帮你省掉 `.value`。

**reactive 的用法与坑**（`BridgeHealthView.vue` / `IotMonitorView.vue` 第 129 行有 `reactive` 导入，常用于一组表单字段）：

```ts
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
// 典型 reactive 用法：一组关联的表单/查询字段打包
const query = reactive({ page: 1, size: 20, keyword: '' })
query.page = 2   // 直接改属性，无 .value
```

**reactive 三大坑（面试高频）**
1. **解构丢响应性**：
   ```ts
   const state = reactive({ count: 0 })
   const { count } = state      // ❌ count 是普通数字快照，state.count 变了 count 不变
   ```
   解决：用 `toRefs(state)` 解构，或干脆全用 `ref`。
2. **整体替换丢响应性**：
   ```ts
   let state = reactive({ count: 0 })
   state = reactive({ count: 5 })   // ❌ 替换了绑定，原来的响应式追踪断了，模板还盯着旧对象
   ```
   `ref` 没这问题：`state.value = { count: 5 }` ✅ 照样响应。
3. **不能装原始值**：`reactive(0)` 无效（Proxy 只能代理对象）。

**为什么 CP6（和社区）首选 `ref`**：一致性——原始值、对象、数组全用 `ref`，规则统一（写就加 `.value`），不用纠结「这个该 ref 还是 reactive」，也避开 reactive 的解构/替换坑。`reactive` 只在「一组必然一起用、不需整体替换、不解构」的字段组时偶尔用。

## 4.3 Proxy 原理（Vue2 defineProperty 对比——面试深水区）

**Vue 3 用 `Proxy` 实现响应式**（ES6 特性）。`Proxy` 能拦截对象的**所有操作**——读属性（get，收集依赖）、写属性（set，触发更新）、新增属性、删除属性、数组下标改动，全都拦得住。

**Vue 2 用 `Object.defineProperty`**，只能给**已存在的属性**逐个装 getter/setter。由此产生 Vue2 两大历史坑：

| Vue2 坑（`Object.defineProperty`） | Vue3（`Proxy`）如何解决 |
|---|---|
| **新增属性不响应**：`obj.newProp = 1` 不触发更新，得用 `Vue.set(obj, 'newProp', 1)` | Proxy 拦截 set，新增属性**自动响应** |
| **删除属性不响应**：得用 `Vue.delete` | Proxy 拦截 deleteProperty，自动响应 |
| **数组下标/length 不响应**：`arr[0] = x`、`arr.length = 0` 不触发，只能用 `push`/`splice` 等被 Vue2 重写过的方法 | Proxy 拦截数组索引写入，`arr[0] = x` **也响应** |
| 初始化时**递归遍历**所有属性装 getter/setter，深对象开销大 | Proxy **惰性代理**——访问到哪层才代理哪层，性能更好 |

**面试标准答案模板**：「Vue2 用 `Object.defineProperty` 劫持属性的 getter/setter，缺陷是无法检测**对象属性的新增/删除**和**数组下标赋值/length 修改**，需要 `Vue.set`/`Vue.delete`/数组变异方法绕过；且初始化要递归遍历。Vue3 改用 `Proxy`，能拦截整个对象的读写增删和数组操作，无需特殊 API，且惰性递归性能更好。代价是 `Proxy` 不兼容 IE11。」

## 4.4 `computed`（计算属性，缓存机制 vs 方法调用）

**概念（与 C# 类比）**

`computed` ≈ C# 的**只读计算属性 + 缓存**：`public bool IsMobile => Width <= 767;`，但 Vue 的 computed 会**缓存结果**，只有依赖变化才重算。

**cp6.web 真实代码**（`useBreakpoint.ts` 第 28-30 行）：

```ts
const isMobile = computed(() => width.value <= MOBILE_MAX)
const isTablet = computed(() => width.value > MOBILE_MAX && width.value <= TABLET_MAX)
const isDesktop = computed(() => width.value > TABLET_MAX)
```

**逐行解析**
- `computed(() => width.value <= MOBILE_MAX)`：声明一个计算值，依赖 `width`。
- **缓存机制**：只要 `width.value` 不变，多次读 `isMobile.value` 都返回缓存，**不重新执行函数**。`width` 一变，标记为「脏」，下次读才重算。
- 用在模板里控制响应式布局：`v-if="isMobile"` 显示手机版、`v-else` 显示桌面版。

**computed vs 方法（method）—— 面试必考**

```html
<!-- computed：isMobile 缓存，width 不变时不重算 -->
<div v-if="isMobile">手机版</div>

<!-- method：每次重渲染都调用，无缓存 -->
<div v-if="checkMobile()">手机版</div>
```

| | `computed` | 方法 `method` |
|---|---|---|
| 缓存 | ✅ 有，依赖不变不重算 | ❌ 无，每次渲染都执行 |
| 适用 | 派生值（基于响应式数据算出的值） | 事件处理、需要传参、有副作用 |
| 性能 | 昂贵计算首选 | 频繁调用昂贵计算会卡 |

**cp6.web 更复杂的 computed**（`useFieldControl.ts` 第 111-117 行）——computed 返回对象：

```ts
const buttonVisibility = computed(() => ({
  save: store.isNew || store.isEdit || store.isCopy,
  next: store.isNew || store.isEdit || store.isCopy || store.isView,
  del: store.isDelete,
  close: store.isView,
  cancel: true,
}))
```

- 依赖 Pinia store 的多个状态，算出「哪些按钮该显示」。store 状态一变，这个对象自动重算。模板里 `v-if="buttonVisibility.save"` 控制保存按钮显隐。

**computed 也可写（getter + setter）**——较少用，`v-model` 绑 computed 时会用到：

```ts
const fullName = computed({
  get: () => `${first.value} ${last.value}`,
  set: (v) => { [first.value, last.value] = v.split(' ') },
})
```

**坑**
- computed **不该有副作用**（别在里面发请求、改别的状态）——它是「纯派生」。要副作用用 `watch`。
- computed 依赖必须是响应式数据（ref/reactive/其他 computed）。依赖一个普通变量，那变量变了 computed 不会重算。

## 4.5 `watch` / `watchEffect`（侦听器，deep / immediate / 清理副作用）

**概念（与 C# 类比）**

`watch` ≈ C# 的 `INotifyPropertyChanged` + `PropertyChanged` 事件处理——**某个值变了，执行一段副作用逻辑**（发请求、联动清空、写日志）。

**cp6.web 真实代码**（`useLinkage.ts` 第 19-33 行）——真实的联动侦听：

```ts
watch(
  () => basicInfo.value.orderBaseCd,          // ① 侦听源：受注拠点
  async (newBase) => {                        // ② 回调：拿到新值
    if (!newBase) {
      staffList.value = []
      basicInfo.value.staffCd = undefined
      return
    }
    const res = await masterApi.getStaffs(newBase)   // ③ 副作用：按新拠点拉担当者
    staffList.value = res.data ?? []
    if (basicInfo.value.staffCd && !staffList.value.find((s) => s.staffCd === basicInfo.value.staffCd)) {
      basicInfo.value.staffCd = undefined            // ④ 联动清空非法选择
    }
  }
)
```

**逐行解析**
- **第一个参数是「侦听源」**：`() => basicInfo.value.orderBaseCd`——用**getter 函数**指定要盯的值（盯 reactive 对象的**某个属性**必须用 getter，不能直接传属性值，否则传的是当时的快照）。
- **第二个参数是「回调」**：`async (newBase) => {...}`——值变化时执行，参数是新值（还有第二个参数是旧值 `(newVal, oldVal)`）。
- 业务逻辑：受注拠点一变 → 拉对应担当者列表 → 若原选中的人不在新名单则清空。这是**表单联动**的经典 watch 用法。
- 回调是 `async`——watch 回调**支持异步**，里面能 `await` 发请求。

**watch 的三个关键配置项**（`watch(源, 回调, 配置)`）：

| 配置 | 作用 | 场景 |
|---|---|---|
| `immediate: true` | **立即执行一次**（不等首次变化） | 组件初始化就要跑一遍逻辑 |
| `deep: true` | **深度侦听**对象内部嵌套属性变化 | 侦听整个对象而非某属性 |
| `flush: 'post'` | 回调在 DOM 更新后执行 | 需要读更新后的 DOM |

```ts
watch(source, callback, { immediate: true, deep: true })
```

**`watchEffect` —— watch 的自动依赖版**

```ts
watchEffect(() => {
  // 自动收集里面用到的所有响应式依赖，任一变化就重跑；组件挂载时立即执行一次
  console.log(`宽度 ${width.value}，是否手机 ${isMobile.value}`)
})
```

| | `watch` | `watchEffect` |
|---|---|---|
| 依赖声明 | **显式**（第一个参数指定） | **自动**（用到啥追踪啥） |
| 首次执行 | 默认否（`immediate` 才是） | **默认立即执行一次** |
| 拿旧值 | ✅ 回调有 `oldVal` | ❌ 拿不到旧值 |
| 精确控制 | 强（明确盯谁） | 弱（可能追踪多余依赖） |

> 注：CP6 代码库里 `watchEffect` **没有实际使用**（全用显式 `watch`）——这本身是个信号：生产项目更偏爱 `watch` 的**可控性**（明确盯什么、能拿旧值、能条件跳过）。面试若问「为什么少用 watchEffect」，可答「依赖不透明、拿不到旧值、易误触发」。

**清理副作用（`onCleanup` / `onInvalidate`）—— 面试进阶**

watch/watchEffect 回调可接一个清理函数，在**下次触发前**或**组件卸载时**执行，用于取消上次未完成的异步（防竞态）：

```ts
watch(keyword, async (kw, _old, onCleanup) => {
  const controller = new AbortController()
  onCleanup(() => controller.abort())   // 下次输入变化时，取消上一次未完成的请求
  const res = await fetch(`/search?q=${kw}`, { signal: controller.signal })
  // ...
})
```

**坑**
- **直接传 reactive 属性当源**：`watch(basicInfo.value.orderBaseCd, ...)`（❌ 传的是当时的字符串快照，永远不触发）。必须 `watch(() => basicInfo.value.orderBaseCd, ...)` 用 getter。
- **侦听对象但没 `deep`**：`watch(() => obj, ...)`（obj 是 reactive），改 `obj.a` 不触发（引用没变）。要么加 `deep: true`，要么盯具体属性 `() => obj.a`。
- watch 默认**惰性**（首次不跑），要初始化就执行加 `immediate: true`。

## 4.6 `shallowRef`（浅层 ref，性能场景）

**概念**

`ref` 默认**深度响应**（对象内部任意层级改动都追踪，Vue 递归代理）。`shallowRef` **只追踪 `.value` 本身的替换**，不追踪内部属性——省掉深度代理开销。

**典型场景**：装**大型不可变数据**或**第三方实例**（图表实例、3D 场景对象、大表格数据），你只会整体替换、不会改内部，或内部改动由第三方库自己管。

```ts
import { shallowRef, triggerRef } from 'vue'

const chart = shallowRef<EChartsInstance | null>(null)   // 图表实例，别深度代理
chart.value = echarts.init(el)                            // 整体替换 → 触发
chart.value.setOption(opt)                                // 改内部 → 不触发 Vue 更新（对，我们不想它触发）

// 大数据数组：整体替换才更新，避免逐行深度追踪的性能开销
const bigList = shallowRef<Row[]>([])
bigList.value = await fetchTenThousandRows()               // 触发
// bigList.value.push(x)  // 不触发，需要手动 triggerRef(bigList)
```

**类比 C#**：普通 `ref` 像深度 `INotifyPropertyChanged`（每个子属性都通知）；`shallowRef` 像只在「换了整个对象引用」时才通知。

**坑**
- CP6 里 3D Space 模块、大数据表格这类场景才需要，**普通业务表单别用 shallowRef**（内部字段改了不更新，反而是 bug）。
- 想手动强制触发一次浅 ref 更新用 `triggerRef(shallowRef)`。

---

# §5 模板语法全集

## 5.1 插值 `{{ }}`（Mustache 语法）

```html
<!-- StockQueryView.vue 第 50 行 -->
<div><strong>{{ t('wms.common.product') }}</strong>: {{ qcTarget.productCd }} / ...</div>
```

- `{{ 表达式 }}`：把 JS 表达式的值渲染成文本。可以是变量、函数调用、三元、拼接。
- **只能放表达式，不能放语句**（不能 `{{ if(x){} }}`、不能 `{{ const a=1 }}`）。
- 数据变了自动更新这段文本（响应式）。

## 5.2 `v-bind` / `:`（属性绑定）

`v-bind:属性` 简写 `:属性`——把属性值绑定到 JS 表达式（动态属性），区别于静态字符串属性。

```html
<!-- StockQueryView.vue 第 9、38、43 行 -->
<CpPageShell :title="t('wms.stock.title')" :count="total">       <!-- 绑定 prop -->
<el-tag :type="qcTagOf(row.qcStatus)" size="small">...</el-tag>  <!-- :type 动态, size 静态 -->
<el-button v-permission="'wms-stock-qc:set'" link type="warning" ...>  <!-- v-permission 自定义指令 -->
```

**逐行解析**
- `:title="t('wms.stock.title')"`：title 的值是**表达式求值结果**（i18n 翻译）。没有 `:` 的话 `title="t('...')"` 会当成字面字符串 `"t('...')"`。
- `:count="total"`：把 ref `total` 传给子组件（自动解包）。
- `size="small"`：**没有 `:`**，是静态字符串 `"small"`。
- **class/style 特殊绑定**（第 22 行）：`:class="{ neg: row.availableQty < 0 }"`——对象语法，key 是类名、value 是布尔条件，`true` 才加该类。这是最常用的条件样式写法。

## 5.3 `v-on` / `@`（事件绑定）

`v-on:事件` 简写 `@事件`——绑定事件处理器。

```html
<!-- StockQueryView.vue 第 17、19、42-43、78-80 行 -->
<CpListPage @total-change="total = $event">              <!-- 内联表达式，$event 是载荷 -->
<el-checkbox v-model="hasStockOnly" @change="reloadList">  <!-- 绑定方法名 -->
<el-button @click="openHistory(row)">...</el-button>       <!-- 调用方法传参 -->
<el-button @click="qcDialogVisible = false" :disabled="qcSaving">取消</el-button>  <!-- 内联赋值 -->
<el-button :loading="qcSaving" :disabled="!qcNewStatus" @click="onQcSave">确认</el-button>
```

**逐行解析**
- `@total-change="total = $event"`：监听子组件 emit 的 `total-change` 事件，`$event` 是事件载荷（子组件 emit 出来的值），直接赋给 `total`。
- `@change="reloadList"`：传**方法名**，事件触发时调用。
- `@click="openHistory(row)"`：**需要传参**时写成调用形式（这里传当前行 `row`）。
- `@click="qcDialogVisible = false"`：内联简单语句（关弹窗）。

## 5.4 `v-if` vs `v-show`（对比表——面试必考）

### v-if vs v-show 对比表

| 维度 | `v-if` | `v-show` |
|---|---|---|
| 实现 | **真正增删 DOM**（条件为假时元素不存在） | **CSS `display:none`**（元素始终在 DOM，只是隐藏） |
| 初始开销 | 低（假则不渲染） | 高（无论真假都渲染一次） |
| 切换开销 | 高（反复销毁/重建） | 低（只切 CSS） |
| 支持 `v-else` | ✅ 支持 `v-else`/`v-else-if` | ❌ 不支持 |
| 组件生命周期 | 切换会触发挂载/卸载钩子 | 不触发（一直挂载） |
| 适用 | **很少切换**、或初始可能不渲染 | **频繁切换**显隐 |

**cp6.web 真实代码**（`StockQueryView.vue` 第 29-34、49、87 行——全用 `v-if`）：

```html
<el-tag v-if="row.ownerType === 'CUSTOMER'" type="warning" size="small">{{ t('wms.stock.flag.vmi') }}</el-tag>
<span v-else>—</span>                                        <!-- v-if 配 v-else -->

<el-tag v-if="row.recallFlag" type="danger" size="small">{{ t('wms.stock.flag.recall') }}</el-tag>

<div v-if="qcTarget" class="qc-info">...</div>              <!-- 条件为假整块不渲染 -->
<div v-if="historyStock" style="margin-bottom: 8px">...</div>
```

**逐行解析**
- `v-if="row.ownerType === 'CUSTOMER'"` + `<span v-else>—</span>`：VMI（客户寄售）显示标签，否则显示占位符「—」。这些是**列表渲染，条件基本不变**，用 `v-if` 合适（假时干脆不渲染 DOM）。
- `v-if="qcTarget"`：`qcTarget` 为 null 时整个信息块不存在——**兼具防空作用**（null 时不会去访问 `qcTarget.productCd` 报错）。

**面试要点**：「频繁切换用 `v-show`（避免反复销毁重建），条件很少变或初始不需渲染用 `v-if`（省初始开销）。`v-if` 有更高的切换代价但更低的初始代价；`v-show` 相反。」CP6 弹窗内条件块多用 `v-if` 因为切换不频繁且要防空。

## 5.5 `v-for` 与 `key`（就地复用坑——面试重灾区）

`v-for` 遍历数组/对象渲染列表。

```html
<!-- 简化示例（CP6 表格列多用组件封装，这里给标准 v-for 形态） -->
<el-radio-button
  v-for="s in ['PENDING', 'PASSED', 'FAILED', 'HOLD']"
  :key="s"
  :value="s"
>{{ t(`wms.stock.qc.${s}`) }}</el-radio-button>
```

**语法**
- `v-for="item in list"`：遍历，`item` 是每项。
- `v-for="(item, index) in list"`：带下标。
- `v-for="(val, key) in obj"`：遍历对象。
- **必须配 `:key`**——给每项一个**稳定唯一**的标识。

### 为什么 `v-for` 必须要 `key`（高频面试题）

Vue 更新列表时用**虚拟 DOM diff**算法比对新旧列表。`key` 是每个节点的「身份证」，让 Vue 能**精确识别哪个是哪个**，从而最小化 DOM 操作（移动而非重建）。

**没有 key（或用 index 当 key）的「就地复用」坑**：
Vue 默认策略是「就地复用」——按位置复用旧 DOM 节点，只更新内容。当列表**顺序变化、中间插入/删除**时：
- 用 `index` 当 key：插入一项后，后面所有项的 index 全变了，Vue 以为「每一项都变了」，可能导致：
  - **表单输入错位**：第 2 行输入框的内容「粘」在了第 3 行（DOM 复用了但状态没跟着走）。
  - **勾选状态错乱**、动画错误、组件状态残留。
- 用**稳定唯一 id**（如 `row.id`）当 key：Vue 精确知道「这一项被删了、那一项移动了」，正确复用/移动。

**正确姿势**：`:key` 用数据的**业务唯一标识**（`row.id`、`stock.stockId`），**绝不用 `index`**（除非列表纯静态、永不重排增删）。

**面试标准答案**：「`key` 帮助 Vue 的 diff 算法识别节点身份，实现高效的 DOM 复用和正确的状态维护。不写 key 或用 index 当 key，在列表增删/重排时会因『就地复用』策略导致 DOM 节点错误复用——表现为表单状态错位、勾选错乱。应使用稳定唯一的业务 id。」

## 5.6 `v-model` 原理（语法糖拆解——面试必考）

`v-model` 实现**双向绑定**——表单控件的值和数据变量同步。

```html
<!-- StockQueryView.vue 第 48、57、66 行 -->
<el-dialog v-model="qcDialogVisible" ...>          <!-- 弹窗开关双向绑定 -->
<el-radio-group v-model="qcNewStatus">...</el-radio-group>   <!-- 选中值双向绑定 -->
<el-input v-model="qcReason" type="textarea" ... />           <!-- 输入内容双向绑定 -->
```

**语法糖拆解（核心考点）**

`v-model` 在**组件**上默认展开为：

```html
<!-- v-model="x" 等价于： -->
<Comp :modelValue="x" @update:modelValue="x = $event" />
```

即两件事：
1. **`:modelValue="x"`**：把 `x` 作为名为 `modelValue` 的 prop 传下去（值向下流）。
2. **`@update:modelValue="x = $event"`**：监听子组件 emit 的 `update:modelValue` 事件，把新值写回 `x`（事件向上冒）。

所以 `v-model` = **prop 下传 + event 上冒的固定组合**，本质仍是 Vue「单向数据流 + 事件」，只是语法糖包装成「双向」的样子。

**原生元素上**（`<input>`）：`v-model` 展开为 `:value` + `@input`（并处理中文输入法等细节）。

**自定义组件实现 v-model**（面试可能让你手写）：

```ts
// 子组件
const props = defineProps<{ modelValue: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()
// 输入时：emit('update:modelValue', 新值)
```

**多个 v-model**（Vue 3 支持具名）：`v-model:title="a" v-model:content="b"` 分别对应 `title`/`update:title`、`content`/`update:content`。

**cp6.web v-model 用法解析**
- `v-model="qcDialogVisible"`：Element Plus 的 `el-dialog` 内部用 `modelValue` 控制显隐，你改 `qcDialogVisible.value = false`（第 78 行）弹窗关闭；用户点遮罩关闭，组件 emit `update:modelValue: false`，`qcDialogVisible` 自动变 false。**双向同步**。
- `v-model="qcReason"`（textarea）：用户打字 → `qcReason.value` 实时更新；代码里 `qcReason.value = ''`（第 221 行）→ 输入框清空。

## 5.7 修饰符（.prevent / .stop / .trim / .number 等）

修饰符是 Vue 给指令加的「后缀小语法」，省掉常见样板代码。

**事件修饰符**（跟在 `@事件` 后）：

| 修饰符 | 作用 | 等价原生代码 |
|---|---|---|
| `.prevent` | 阻止默认行为 | `event.preventDefault()` |
| `.stop` | 阻止冒泡 | `event.stopPropagation()` |
| `.self` | 只有事件目标是元素自身才触发 | `if (e.target === e.currentTarget)` |
| `.once` | 只触发一次 | `{ once: true }` |
| `.capture` | 捕获阶段触发 | `{ capture: true }` |
| `.passive` | 被动监听（滚动优化） | `{ passive: true }` |

```html
<form @submit.prevent="onSubmit">       <!-- 阻止表单默认提交刷新页面 -->
<div @click.stop="doSomething">          <!-- 点击不冒泡到父元素 -->
<div @click.self="closeModal">           <!-- 只有点遮罩本身才关，点内容不关 -->
```

**按键修饰符**（键盘事件）：

```html
<el-input @keyup.enter="search" />       <!-- 回车触发搜索 -->
<el-input @keyup.esc="cancel" />          <!-- Esc 取消 -->
```

**v-model 修饰符**（跟在 `v-model` 后）：

| 修饰符 | 作用 |
|---|---|
| `.trim` | 自动去掉首尾空格 |
| `.number` | 自动转数字（表单输入默认是字符串） |
| `.lazy` | 用 `change` 而非 `input` 事件同步（失焦才更新） |

```html
<el-input v-model.trim="keyword" />       <!-- 搜索关键词自动去空格 -->
<el-input v-model.number="qty" />          <!-- 数量自动转 number，避免字符串 "5" -->
```

**坑**
- `.number` 只在能转成数字时转，转不了保留原字符串——校验仍要做。
- 修饰符可**链式**：`@click.stop.prevent="fn"`（先 stop 再 prevent）。
- `.passive` 和 `.prevent` **不能一起用**（矛盾——被动监听承诺不 preventDefault）。

---

# §6 生命周期

## 6.1 组合式 API 生命周期钩子全表

组件从「创建 → 挂载 → 更新 → 卸载」有一系列**生命周期钩子**（≈ C# 组件的 `OnInitialized`/`Dispose` 等回调）。组合式 API 里以 `onXxx` 函数形式在 `setup` 中注册。

### 生命周期钩子全表

| 组合式钩子 | 时机 | 典型用途 | 选项式对应 |
|---|---|---|---|
| （setup 本身） | 组件创建最早期 | 声明状态、composable | `beforeCreate`/`created` |
| `onBeforeMount` | 挂载到 DOM 前 | 少用 | `beforeMount` |
| `onMounted` | **已挂载到 DOM 后** | **拉数据、初始化第三方库、启动定时器/连接、访问 DOM** | `mounted` |
| `onBeforeUpdate` | 响应式数据变、DOM 更新前 | 更新前读旧 DOM | `beforeUpdate` |
| `onUpdated` | DOM 更新后 | 少用（易死循环） | `updated` |
| `onBeforeUnmount` | 卸载前（组件仍完整） | **清理：移除监听器、停定时器** | `beforeUnmount` |
| `onUnmounted` | **已卸载后** | **清理：断开连接、清定时器、退订** | `unmounted` |
| `onErrorCaptured` | 捕获后代组件错误 | 错误边界 | `errorCaptured` |
| `onActivated` | keep-alive 组件被激活 | 缓存组件重新进入 | `activated` |
| `onDeactivated` | keep-alive 组件被缓存 | 缓存组件离开 | `deactivated` |

### 生命周期图（ASCII）

```
       setup() 执行（组合式 API 的一切在这里）
              │  声明 ref / reactive / computed / 注册钩子
              ▼
        onBeforeMount   ── 挂载前（DOM 还没有）
              │
              ▼
     ┌──► onMounted ◄── DOM 已就绪！【拉数据 / 定时器 / SignalR / 访问 DOM】
     │        │
     │        ▼  （响应式数据变化时循环）
     │   onBeforeUpdate ──► DOM 更新 ──► onUpdated
     │        │
     │        ▼
     └── onBeforeUnmount ── 组件即将销毁（DOM 还在）【移除监听器】
              │
              ▼
        onUnmounted ── 已销毁【断连接 / 清定时器 / 退订】── 防内存泄漏！
```

## 6.2 `onMounted` 拉数据（最常用）

**cp6.web 真实代码**（`IotMonitorView.vue` 第 241-244 行）：

```ts
onMounted(() => {
  reload()                                    // 首次拉数据
  timer = window.setInterval(reload, 30000)   // 启动 30 秒自动刷新
})
```

**逐行解析**
- `onMounted(() => {...})`：DOM 挂载完成后执行。
- `reload()`：进页面先拉一次数据（在 `onMounted` 里拉数据是**标准套路**——此时 DOM 已就绪，能安全操作）。
- `timer = window.setInterval(reload, 30000)`：启动定时器，每 30 秒刷新。**`timer` 变量存起来，卸载时要清**（见 6.3）。

**DashboardView.vue 第 601-626 行**——`onMounted` 里 `async` + 初始化 SignalR：

```ts
onMounted(async () => {
  await loadData()
  loadUnshipped()
  loadStockDwell()
  try {
    await startConnection()
    const conn = getConnection()
    conn.on('BusinessNotification', (n: Notice) => { /* 收到推送刷新 */ })
    conn.on('NewOperLog', () => { loadData() })
  } catch {
    // SignalR 失败不阻塞仪表盘外壳
  }
})
```

- `onMounted(async () => {...})`：钩子回调可 `async`，里面 `await` 拉数据、建立实时连接。
- `conn.on('BusinessNotification', ...)`：订阅服务器推送事件（SignalR，类似 C# 的 SignalR Hub 客户端）。**订阅了就得在卸载时退订**，否则内存泄漏。

## 6.3 `onUnmounted` / `onBeforeUnmount` 清理（防内存泄漏——面试重点）

**为什么必须清理**：定时器、事件监听、WebSocket/SignalR 连接、订阅——这些**不会随组件销毁自动消失**。不清理会导致：
- **内存泄漏**：组件没了，定时器还在跑，闭包引用的东西无法回收。
- **报错**：定时器回调去操作已销毁组件的状态。
- **重复订阅**：反复进出页面，事件监听越叠越多，一个推送触发 N 次。

**cp6.web 真实代码 1**（`IotMonitorView.vue` 第 240-245 行）——清定时器：

```ts
let timer: number | undefined
onMounted(() => {
  reload()
  timer = window.setInterval(reload, 30000)
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })
```

- `onUnmounted(() => { if (timer) window.clearInterval(timer) })`：组件卸载后**清掉定时器**。`if (timer)` 防止 timer 未定义时报错。**onMounted 起的定时器，onUnmounted 必须清**——这是配对纪律。

**cp6.web 真实代码 2**（`BridgeHealthView.vue` 第 193-195 行）——同款模式：

```ts
onUnmounted(() => {
  if (refreshTimer) window.clearInterval(refreshTimer)
})
```

**cp6.web 真实代码 3**（`DashboardView.vue` 第 628-632 行）——退订 SignalR 事件：

```ts
onUnmounted(() => {
  const conn = getConnection()
  conn.off('BusinessNotification')   // 退订，对应 onMounted 里的 conn.on(...)
  conn.off('NewOperLog')
})
```

- `conn.off(...)`：解除事件订阅。**`onMounted` 里 `conn.on` 订阅了几个，`onUnmounted` 里就 `conn.off` 退订几个**——严格配对。否则用户反复进出仪表盘，订阅叠加，一条推送触发多次刷新。

**cp6.web 真实代码 4**（`useBreakpoint.ts` 第 13-26 行）——composable 里用 `onBeforeUnmount` 清理全局监听 + 引用计数：

```ts
export function useBreakpoint() {
  onMounted(() => {
    if (listenerCount === 0) {
      window.addEventListener('resize', onResize)   // 首个使用者才装监听
      width.value = window.innerWidth
    }
    listenerCount++
  })
  onBeforeUnmount(() => {
    listenerCount--
    if (listenerCount === 0) {
      window.removeEventListener('resize', onResize)  // 最后一个使用者才移除
    }
  })
  // ...
}
```

**逐行解析（这是个精妙的清理模式）**
- 多个组件都用 `useBreakpoint()`，但只需**一个** `resize` 监听器。用 `listenerCount` **引用计数**：
  - `onMounted`：计数为 0（首个使用者）才真正 `addEventListener`，然后 `listenerCount++`。
  - `onBeforeUnmount`：`listenerCount--`，减到 0（最后一个使用者也走了）才 `removeEventListener`。
- 这样 N 个组件共享 1 个监听器，且全部卸载后干净移除——**零泄漏、零重复**。这是生产级 composable 的清理典范。

**`onUnmounted` vs `onBeforeUnmount` 何时用哪个**：
- `onBeforeUnmount`：组件**还完整存在**（DOM/状态可访问）——移除监听器、保存草稿。
- `onUnmounted`：组件**已销毁**——纯清理外部资源（定时器、连接）。
- 大多数清理两者都行；CP6 里定时器/退订用 `onUnmounted`，需要引用组件自身状态的清理用 `onBeforeUnmount`。

---

# §7 `<script setup>` 语法糖

## 7.1 `<script setup>` 是什么

`<script setup>` 是组合式 API 的**编译期语法糖**，让你在 SFC 里**直接写 setup 逻辑**，无需 `export default { setup() { return {...} } }` 的样板。**顶层声明的变量、函数、import 的组件，模板自动可用**，不用手动 return。

```html
<script setup lang="ts">
import { ref } from 'vue'
const count = ref(0)              // 模板直接能用 count
function inc() { count.value++ }  // 模板直接能用 inc
</script>
<template>
  <button @click="inc">{{ count }}</button>   <!-- 无需任何 return -->
</template>
```

**CP6 全项目统一用 `<script setup lang="ts">`**——所有 `.vue` 都是这个开头。

## 7.2 `defineProps`（声明 props——组件的「输入参数」）

`defineProps` 声明组件接收的 props（≈ C# 类的构造参数/公开属性）。CP6 用**基于类型的声明**（TS 风格）。

**cp6.web 真实代码**（`CpStatCard.vue` 第 22-31 行）：

```ts
const props = withDefaults(defineProps<{
  label: string
  value: number | string
  suffix?: string
  tone?: 'brand' | 'info' | 'warn' | 'danger'
  trend?: number[]
  sub?: string
  clickable?: boolean
}>(), { tone: 'brand' })
```

**逐行解析**
- `defineProps<{...}>()`：**泛型参数是 props 的类型定义**（一个对象类型）。
- `label: string`：必填 prop。
- `tone?: 'brand' | 'info' | 'warn' | 'danger'`：可选 prop（`?`），且用**字面量联合**限定只能这 4 个值——传别的编译期报错。
- `trend?: number[]`：可选数组 prop。
- **`defineProps` 是编译器宏**——不用 import，编译时处理掉。

**父组件传 props**（`CpStatCard.vue` 注释第 17-19 行示范）：

```html
<CpStatCard label="在制指令" :value="10" suffix="件" tone="brand" sub="完成率 36.4%">
  <template #icon><SetUp /></template>
</CpStatCard>
```

- `label="在制指令"`：静态字符串 prop。
- `:value="10"`：`:` 绑定，传的是**数字** `10`（不带 `:` 会传字符串 `"10"`）。

## 7.3 `withDefaults`（给可选 props 设默认值）

TS 类型声明式的 `defineProps` 没法直接写默认值，用 `withDefaults` 包一层：

```ts
withDefaults(defineProps<{...}>(), { tone: 'brand' })
```

- 第二个参数是默认值对象：`tone` 不传时默认 `'brand'`。
- 模板里 `props.tone` 一定有值（要么用户传的，要么 `'brand'`），`CpStatCard` 第 71 行 `chipStyle[props.tone]` 才能安全索引。

## 7.4 `defineEmits`（声明组件能发出的事件——「输出」）

`defineEmits` 声明组件可以 emit 哪些事件（≈ C# 类暴露的 `event`/回调）。

**cp6.web 真实代码**（`CpListPage.vue` 第 110-113 行）：

```ts
const emit = defineEmits<{
  (e: 'selection-change', rows: unknown[]): void
  (e: 'total-change', total: number): void
  (e: 'sort-change', payload: { field?: string; order?: SortOrder }): void
}>()
```

**逐行解析**
- `defineEmits<{...}>()`：泛型定义**事件签名**——事件名 + 载荷类型。
- `(e: 'total-change', total: number): void`：声明可 emit 名为 `total-change`、载荷是 `number` 的事件。
- 组件内部触发：`emit('total-change', 123)`——把总数发给父组件。
- 父组件接收（`StockQueryView.vue` 第 17 行）：`@total-change="total = $event"`，`$event` 就是那个 `123`。
- **类型安全**：`emit('total-chang', ...)`（拼错）或载荷类型不对，编译期报错。

**props + emits 构成组件的完整接口契约**：props 是输入（父→子），emits 是输出（子→父）。这就是 Vue 的**单向数据流**——数据向下、事件向上，`v-model` 只是这套机制的语法糖（回看 §5.6）。

## 7.5 `defineExpose`（暴露子组件的方法/属性给父组件）

默认 `<script setup>` 组件对外**是封闭的**（父组件拿到 ref 也访问不到内部）。`defineExpose` 显式暴露指定成员，父组件才能通过模板 ref 调用。

**cp6.web 真实用例**（`StockQueryView.vue` 第 127-128 行父侧调用 + `CpListPage` 内部 expose）：

```ts
// 父组件（StockQueryView）：拿到子组件实例 ref
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }   // 调用子组件暴露的 reload()
```

对应模板第 10 行 `<CpListPage ref="listRef" ...>`，`CpListPage` 内部必然有：

```ts
// 子组件（CpListPage）内部
function reload() { /* 重新拉数据 */ }
defineExpose({ reload })   // 暴露 reload 方法，父组件才能 listRef.value.reload()
```

**逐行解析**
- `ref<InstanceType<typeof CpListPage> | null>(null)`：`InstanceType<typeof CpListPage>` 是 TS 取组件实例类型的写法，让 `listRef.value.reload()` 有类型提示。
- `listRef.value?.reload()`：`?.` 防 null（组件还没挂载时 `listRef.value` 是 null）。
- **父命令子**的场景（如「保存后让列表刷新」）用 `defineExpose` + 模板 ref。但优先考虑 props/emits，`defineExpose` 用于确实需要「命令式调用子组件方法」时。

## 7.6 其他 setup 宏速览

| 宏 | 作用 |
|---|---|
| `defineProps` | 声明输入 props |
| `defineEmits` | 声明输出事件 |
| `defineExpose` | 暴露内部成员给父 |
| `defineModel`（3.4+） | 简化自定义 v-model（自动处理 modelValue + update） |
| `defineOptions` | 声明组件名等选项 |
| `defineSlots` | 声明插槽类型 |

这些都是**编译器宏**——无需 import、只能在 `<script setup>` 顶层用、编译时处理。

---

# §8 组合式函数 composables

## 8.1 什么是 composable

**composable（组合式函数）** = 一个**以 `use` 开头命名、封装了响应式逻辑（ref/computed/watch/生命周期）并可复用**的普通函数。它是组合式 API 时代**逻辑复用**的核心手段。

**类比 C#**：composable ≈ **可注入的服务（Service）** 或**扩展方法**——把一段有状态、可复用的逻辑抽出来，多个组件「注入使用」。`const { isMobile } = useBreakpoint()` 就像 `var svc = GetService<IBreakpoint>()`。

**特征**
1. 命名约定 **`useXxx`**（`useBreakpoint`、`useFormat`、`useStep1Validation`）——见名知意，工具会据此识别。
2. 内部可用 `ref`/`computed`/`watch`/`onMounted` 等所有组合式 API。
3. **返回**响应式状态和方法供组件解构使用。
4. 每次调用产生独立状态（除非刻意共享模块级状态，见 `useBreakpoint`）。

## 8.2 CP6 全部 composables 清单

`C:\CP6\cp6.web\src\composables\` 下共 **7 个** composable：

| 文件 | 职责 |
|---|---|
| `useBreakpoint.ts` | 响应式断点（isMobile/isTablet/isDesktop），监听窗口宽度 |
| `useValidation.ts` | 表单校验规则（Element Plus FormRules）+ 业务级校验 |
| `useLinkage.ts` | 表单字段联动（拠点→担当者、刃渡り→寸法 auto-calc） |
| `useFieldControl.ts` | 字段状态控制（可编辑/只读/禁用/必填）+ 按钮显隐 |
| `useConflictHandler.ts` | 并发冲突处理（乐观锁 / 版本冲突） |
| `useProductConflictHandler.ts` | 商品维护专用冲突处理 |
| `usePubExcel.ts` | 发布模块 Excel 导入/导出逻辑 |

## 8.3 精读 1：`useBreakpoint.ts`（响应式布局断点）

**完整代码**（`C:\CP6\cp6.web\src\composables\useBreakpoint.ts`）：

```ts
import { ref, computed, onMounted, onBeforeUnmount, readonly } from 'vue'

const MOBILE_MAX = 767
const TABLET_MAX = 991

const width = ref(typeof window !== 'undefined' ? window.innerWidth : 1280)

let listenerCount = 0
function onResize() {
  width.value = window.innerWidth
}

export function useBreakpoint() {
  onMounted(() => {
    if (listenerCount === 0) {
      window.addEventListener('resize', onResize)
      width.value = window.innerWidth
    }
    listenerCount++
  })
  onBeforeUnmount(() => {
    listenerCount--
    if (listenerCount === 0) {
      window.removeEventListener('resize', onResize)
    }
  })

  const isMobile = computed(() => width.value <= MOBILE_MAX)
  const isTablet = computed(() => width.value > MOBILE_MAX && width.value <= TABLET_MAX)
  const isDesktop = computed(() => width.value > TABLET_MAX)

  return {
    width: readonly(width),
    isMobile,
    isTablet,
    isDesktop,
  }
}
```

**逐行精读**
- **第 6 行 `const width = ref(...)` 定义在函数外（模块级）**：这是**故意的共享状态**——所有组件共用同一个 `width` ref 和同一个 resize 监听器（配合 `listenerCount` 引用计数）。若把 `width` 放函数内，每个组件一个独立 ref + 独立监听器，浪费。**这是 composable 的一个高级模式：模块级单例状态**。
- `typeof window !== 'undefined' ? window.innerWidth : 1280`：SSR 安全兜底——服务端渲染时没有 `window`，给个默认宽度防报错（CP6 是 SPA，但这写法是好习惯）。
- `onMounted`/`onBeforeUnmount` + `listenerCount`：§6.3 精讲过的引用计数清理——首个使用者装监听，最后一个卸载移除。**注意：钩子在函数内，所以每个使用组件都注册自己的挂载/卸载钩子**，但真正的 add/removeEventListener 靠计数守卫只执行一次。
- `computed(() => width.value <= MOBILE_MAX)`：派生断点，`width` 变化时自动重算。
- `return { width: readonly(width), isMobile, isTablet, isDesktop }`：
  - `readonly(width)`：暴露只读版 `width`——**防止使用方误改**（`width` 应只由 resize 更新）。这是防御式 API 设计，等价 C# 暴露 `IReadOnlyList` 而非 `List`。
  - `isMobile` 等：computed 直接暴露。

**使用方式**（任意组件内）：

```ts
const { isMobile, isDesktop } = useBreakpoint()
// 模板里：<MobileView v-if="isMobile" /> <DesktopView v-else />
```

**为什么这是好 composable**：单一职责（只管断点）、清理干净（引用计数零泄漏）、API 安全（readonly）、可复用（任何组件一行接入）。

## 8.4 精读 2：`useValidation.ts`（表单校验，返回规则 + 方法）

**核心代码**（`C:\CP6\cp6.web\src\composables\useValidation.ts` 节选）：

```ts
import type { FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import type { EstimateCalcDto } from '@/types/erp/estimateCalc'

export function useStep1Validation() {
  const { t } = useI18n()
  const rules: FormRules = {
    proCd: [{ required: true, message: t('MSG-111 商品コードを入力してください'), trigger: 'blur' }],
    qtnDate: [{ required: true, message: t('MSG-112 見積日を指定してください'), trigger: 'change' }],
    // ... 19 条必填规则
    orderQty: [
      { required: true, message: t('MSG-121 受注数量を入力してください'), trigger: 'blur' },
      {
        validator: (_r, v, cb) => (typeof v === 'number' && v > 0 ? cb() : cb(new Error(t('MSG-121 受注数量は 0 より大きくしてください')))),
        trigger: 'blur',
      },
    ],
  }

  function validateBusiness(dto: EstimateCalcDto): string[] {
    const errors: string[] = []
    if (dto.productCategorySml && !dto.productCategoryMid) {
      errors.push(t('MSG-W10010 商品小分類が指定されたのに中分類が空です'))
    }
    const hasQty = (dto.estimateQtys ?? []).some((q) => (q ?? 0) > 0)
    if (!hasQty) {
      errors.push(t('MSG-W10011 見積り数量を 1 件以上入力してください'))
    }
    if ((dto.bladeWidth ?? 0) > 0 && (dto.bladeFlow ?? 0) <= 0) {
      errors.push(t('MSG-W10012 刃渡りを入力した場合、流れも必須です'))
    }
    return errors
  }

  return { rules, validateBusiness }
}
```

**逐行精读**
- `const { t } = useI18n()`：composable 里**可以调用其他 composable**（`useI18n` 也是 composable）——组合式的「组合」正在于此，逻辑像乐高一样拼。
- `const rules: FormRules = {...}`：返回 Element Plus 表单校验规则对象。key 是字段名（**约定必须与 DTO 字段名一致**，注释第 9 行强调），value 是规则数组。
- `{ required: true, message: t(...), trigger: 'blur' }`：必填规则——失焦（blur）时校验，报错显示翻译后的消息。
- `orderQty` 有**两条规则**：先必填，再 `validator` 自定义校验（数字且 > 0）。`validator: (_r, v, cb) => ...` 是 Element Plus 的自定义校验签名——`v` 是当前值，`cb()` 通过、`cb(new Error(...))` 失败。
- **`validateBusiness` 分离「rules 表达不了的业务校验」**：规则表能表达单字段必填，但「有小分類必须有中分類」这种**跨字段联动校验**用普通函数手写，返回错误消息数组（空即通过）。这是**声明式规则 + 命令式业务校验**的分层设计——面试可讲这个「分层」思路。
- `return { rules, validateBusiness }`：暴露规则对象（绑到 `<el-form :rules>`）和业务校验函数（提交前手动调）。

## 8.5 composable vs mixin（Vue2 遗产——为什么组合式更好，面试常问）

Vue2 时代逻辑复用靠 **mixin**：把 `data`/`methods`/`computed`/生命周期打包成对象，`mixins: [myMixin]` 混入组件。mixin 有**三宗罪**：

| 问题 | mixin（Vue2） | composable（Vue3） |
|---|---|---|
| **来源不清** | 模板用了 `foo`，不知道来自哪个 mixin（隐式注入） | `const { foo } = useFoo()` **显式**，一眼看清来源 |
| **命名冲突** | 多个 mixin 有同名 `data`/`method` 会**静默覆盖** | 解构时自己命名 `const { foo: myFoo } = ...`，冲突自己解 |
| **类型支持差** | mixin 的 this 类型难推导，TS 支持烂 | 普通函数 + 返回值，**TS 类型完美推导** |
| **逻辑分散** | 一个功能的 data/method/生命周期散在 mixin 各处 | 一个 composable 内聚一个功能的全部逻辑 |
| **嵌套复用** | mixin 难以互相组合 | composable 可**自由调用其他 composable** |

**面试标准答案**：「mixin 的问题是**来源不清晰**（模板里的属性不知来自哪个 mixin）、**命名冲突静默覆盖**、**类型推导差**。组合式函数用显式的 `const { x } = useX()` 解决来源问题，命名冲突可在解构时重命名，是普通 TypeScript 函数所以类型完美，且能自由嵌套组合。这是 Vue3 组合式 API 相比 Vue2 选项式最重要的改进之一。」

---

# §9 选项式 API 对照（应对「Vue2 经验」提问）

面试官可能问「你用过 Vue2 吗」或给一段选项式代码让你读。你必须**能读懂选项式**，即使日常写组合式。

## 9.1 选项式 API 长什么样

选项式把组件拆成**固定的选项对象**：`data`/`methods`/`computed`/`watch`/生命周期钩子，全靠 `this` 串联。

```html
<script>
export default {
  name: 'Counter',
  props: {                          // ← 对应 defineProps
    step: { type: Number, default: 1 },
  },
  data() {                          // ← 对应 ref/reactive
    return { count: 0 }
  },
  computed: {                       // ← 对应 computed()
    double() { return this.count * 2 },   // 用 this.count
  },
  watch: {                          // ← 对应 watch()
    count(newVal, oldVal) { console.log(newVal) },
  },
  methods: {                        // ← 对应普通函数
    inc() { this.count += this.step },    // this.count / this.step
  },
  mounted() {                       // ← 对应 onMounted()
    this.inc()
  },
}
</script>
<template>
  <button @click="inc">{{ count }} / {{ double }}</button>
</template>
```

**关键区别**：选项式里**一切通过 `this`** 访问——`this.count`、`this.step`、`this.inc()`。数据（data）、方法（methods）、props 全挂在 `this` 上。这也是选项式的**痛点**：一个功能的逻辑被拆散在 `data`/`methods`/`computed`/`mounted` 不同选项里，大组件里追踪一个功能要上下反复跳。

## 9.2 组合式 vs 选项式对照表

| 概念 | 选项式 API | 组合式 API (`<script setup>`) |
|---|---|---|
| 响应式数据 | `data() { return { count: 0 } }` | `const count = ref(0)` |
| 访问数据 | `this.count` | `count.value`（脚本）/ `count`（模板） |
| 计算属性 | `computed: { double() {...} }` | `const double = computed(() => ...)` |
| 侦听 | `watch: { count(n, o) {} }` | `watch(count, (n, o) => {})` |
| 方法 | `methods: { inc() {} }` | `function inc() {}` |
| props | `props: { step: {...} }` | `defineProps<{ step: number }>()` |
| emit | `this.$emit('evt', v)` | `const emit = defineEmits(...); emit('evt', v)` |
| 挂载钩子 | `mounted() {}` | `onMounted(() => {})` |
| 逻辑复用 | mixin | composable（`useXxx`） |
| 逻辑组织 | 按**选项种类**分散 | 按**功能**内聚 |
| TS 支持 | 弱（this 类型难推） | 强（纯函数，完美推导） |

**面试要点**：「选项式按选项类型（data/methods/computed）组织代码，逻辑分散、靠 this、TS 支持弱、复用靠 mixin；组合式按功能内聚，用 ref/computed/函数、无 this、TS 完美、复用靠 composable。大型组件组合式的可维护性明显更好。Vue3 两者都支持，但新项目（如 CP6）统一用组合式 + `<script setup>`。」

---

# §10 后端视角类比总结表（C# 开发者迁移心智）

把 Vue/前端概念映射到你熟悉的 C#/后端世界，一张表打通任督二脉：

| Vue / 前端概念 | C# / 后端类比 | 说明 |
|---|---|---|
| 组件（Component） | **类（Class）** | 封装状态 + 行为 + 模板 |
| SFC（.vue 文件） | 自包含控件（XAML+code-behind+样式） | 三段合一 |
| props | **构造参数 / 只读属性** | 父传子的输入，单向不可改 |
| emit（事件） | **event / Action 回调** | 子通知父的输出 |
| props + emits | **接口契约（输入/输出）** | 组件的公开 API |
| `v-model` | **双向数据绑定（WPF Binding TwoWay）** | prop 下传 + event 上冒语法糖 |
| composable（useXxx） | **可注入的服务（DI Service）/ 扩展方法** | 有状态可复用逻辑 |
| Pinia store | **单例服务（Singleton Service）/ 全局状态容器** | 跨组件共享状态 |
| `ref` / `reactive` | **`INotifyPropertyChanged` 属性** | 变化自动通知 UI |
| `computed` | **只读计算属性 + 缓存** | `=> expr`，依赖不变不重算 |
| `watch` | **PropertyChanged 事件处理** | 值变触发副作用 |
| 生命周期钩子 | **OnInitialized / Dispose 回调** | 组件生老病死的挂钩点 |
| `onMounted` | **OnInitializedAsync（拉数据）** | DOM 就绪，初始化 |
| `onUnmounted` | **Dispose（释放资源）** | 清理定时器/连接，防泄漏 |
| 组件树 | **对象组合树 / 控件树** | 父子嵌套 |
| 声明式渲染 | **数据绑定（XAML）** | 描述「数据→UI」，非手动操作 DOM |
| 虚拟 DOM diff | （无直接对应，理解为「增量 UI 更新」） | 最小化真实 DOM 操作 |
| TypeScript interface | **DTO / POCO / interface** | 数据形状契约 |
| 泛型 `<T>` | **泛型 `<T>`** | 几乎一模一样 |
| `import`/`export` | **using / namespace（更细粒度）** | 文件级模块 |
| async/await | **async/await + Task** | 心智一致 |
| 数组 map/filter/reduce | **LINQ Select/Where/Aggregate** | 送分对照 |
| vue-router | **ASP.NET 路由 / 页面导航** | URL → 组件映射 |
| vue-i18n | **资源文件（.resx）本地化** | key → 多语言文案 |
| SignalR 客户端（`conn.on`） | **SignalR Hub 客户端** | 就是同一个 SignalR，服务端推送 |

**迁移心法**：把「写组件」想成「写一个带模板的类」——props 是构造参数，data(ref) 是字段，computed 是只读属性，methods 是方法，emit 是事件，生命周期是初始化/析构。composable 是你抽出去的 service。Pinia 是单例 service。这样上手 Vue，你的 5 年后端功力直接迁移 70%。

---

# §11 面试题 20 问 + 自测清单 + 动手练习

## 面试题 20 问（详细答案）

**Q1. Vue2 和 Vue3 的响应式原理区别？（必考）**

Vue2 用 `Object.defineProperty` 劫持对象已有属性的 getter/setter 实现响应式。缺陷：① 无法检测**对象属性的新增/删除**（需 `Vue.set`/`Vue.delete`）；② 无法检测**数组下标赋值和 length 修改**（`arr[0]=x`、`arr.length=0` 不触发，只能用被重写的 `push`/`splice` 等变异方法）；③ 初始化要**递归遍历**所有属性装 getter/setter，深对象开销大。
Vue3 改用 ES6 `Proxy` 代理整个对象，能拦截**读、写、新增、删除、数组索引操作**全部行为，无需特殊 API；且**惰性代理**（访问到才递归），性能更好。代价是 `Proxy` 不支持 IE11。

**Q2. `ref` 和 `reactive` 的区别？分别什么时候用？**

`ref` 可包装**任意值**（原始值 + 对象），脚本里通过 `.value` 访问，模板里自动解包；`reactive` **只能**包装对象/数组/Map/Set，直接访问属性无 `.value`。
三个关键差异：① `ref` 能装原始值，`reactive` 不能；② `reactive` **解构会丢响应性**（拷贝快照），`reactive` **整体重新赋值也丢响应性**（断了 Proxy 追踪），`ref` 都没这问题（`x.value = 新对象` 照常响应）；③ 底层 `ref` 是 RefImpl + getter/setter，`reactive` 是 Proxy。
实践：**优先 `ref`**（规则统一、避坑），`reactive` 仅用于「一组固定字段、不解构、不整体替换」的场景。

**Q3. `computed` 和 `method` 的区别？**

`computed` 有**缓存**——依赖不变时多次读取返回缓存值，不重新计算；`method` 每次调用（每次重渲染）都执行。`computed` 用于基于响应式数据的**派生值**（且应无副作用），`method` 用于事件处理、需传参、有副作用的场景。昂贵计算用 computed 能显著优化性能。

**Q4. `watch` 和 `watchEffect` 的区别？**

`watch` **显式**指定侦听源，回调能拿到**新值和旧值**，默认**惰性**（首次不执行，需 `immediate: true`）；`watchEffect` **自动收集**回调内用到的响应式依赖，**立即执行一次**，但**拿不到旧值**、依赖不透明。需要精确控制、拿旧值、条件触发用 `watch`；简单的「用到啥就追踪啥」的副作用可用 `watchEffect`。生产项目（如 CP6）偏爱 `watch` 的可控性。

**Q5. `watch` 的 `deep` 和 `immediate` 是什么？**

`immediate: true` 让 watch **创建时立即执行一次**回调（不等首次变化）——适合初始化就要跑的逻辑。`deep: true` 开启**深度侦听**——侦听对象内部**嵌套属性**的变化（默认只侦听引用变化，改内部属性不触发）。深度侦听有性能成本，能盯具体属性就别 deep 整个对象。

**Q6. `v-if` 和 `v-show` 的区别？何时用哪个？**

`v-if` **真正增删 DOM**（假则元素不存在），有更高的切换开销、更低的初始开销，支持 `v-else`，切换触发组件挂载/卸载钩子。`v-show` 用 **CSS `display:none`**（元素始终在 DOM，只是隐藏），切换开销低、初始开销高（总要渲染一次），不支持 `v-else`。**频繁切换**用 `v-show`，**条件很少变或初始不需渲染**用 `v-if`。

**Q7. `v-for` 为什么必须要 `key`？用 index 当 key 有什么问题？（必考）**

`key` 是列表节点的唯一身份标识，帮助 Vue 的虚拟 DOM diff 算法**精确识别哪个节点是哪个**，实现高效复用（移动而非重建）和正确的状态维护。
不写 key 或用 `index` 当 key，Vue 采用「就地复用」策略。当列表**中间插入/删除或重排**时，index 会错位，Vue 误判节点身份，导致 DOM 节点被错误复用——表现为**表单输入内容错位、勾选状态错乱、动画错误、组件内部状态残留**。应使用**稳定唯一的业务 id**（如 `row.id`），只有纯静态永不重排的列表才可用 index。

**Q8. `v-model` 的原理？（必考）**

`v-model` 是双向绑定的语法糖。在组件上，`v-model="x"` 等价于 `:modelValue="x"` + `@update:modelValue="x = $event"`——即 ① 把 `x` 作为 `modelValue` prop 下传，② 监听子组件 emit 的 `update:modelValue` 事件把新值写回 `x`。本质仍是「prop 下传 + event 上冒」的单向数据流组合。原生元素上展开为 `:value` + `@input`。Vue3 支持多个具名 v-model（`v-model:title`）。自定义组件实现 v-model 就是声明 `modelValue` prop + emit `update:modelValue`。

**Q9. 组合式 API 相比选项式 API 的优势？**

① **逻辑内聚**：一个功能的所有逻辑（状态/计算/侦听/生命周期）写在一起，而非分散在 data/methods/computed 各选项；② **更好的逻辑复用**：composable 替代 mixin，来源清晰、无命名冲突、可嵌套组合；③ **完美的 TypeScript 支持**：纯函数 + 返回值，类型自动推导，没有 this 类型难题；④ **更小的打包体积**（更好的 tree-shaking）；⑤ 无需处理 `this` 指向问题。

**Q10. composable 是什么？和 mixin 比好在哪？**

composable 是以 `use` 开头、封装可复用响应式逻辑（ref/computed/watch/生命周期）并返回状态与方法的函数。相比 Vue2 的 mixin：① **来源清晰**——`const { x } = useX()` 显式，mixin 是隐式注入不知来源；② **命名冲突可控**——解构可重命名，mixin 同名静默覆盖；③ **类型完美**——普通 TS 函数，mixin 的 this 类型难推；④ **可自由嵌套组合**。

**Q11. `<script setup>` 是什么？有什么好处？**

`<script setup>` 是组合式 API 的编译期语法糖。好处：① 顶层声明的变量、函数、导入的组件**模板自动可用**，无需手动 return；② 代码更简洁（省掉 `export default { setup() { return {} } }` 样板）；③ 更好的运行时性能和 TS 推导；④ 提供 `defineProps`/`defineEmits`/`defineExpose` 等编译器宏。CP6 全项目统一使用。

**Q12. `defineProps`/`defineEmits`/`defineExpose` 各是什么？**

三个都是 `<script setup>` 编译器宏（无需 import）。`defineProps` 声明组件接收的输入 props（可用 TS 类型声明 + `withDefaults` 设默认值）；`defineEmits` 声明组件能 emit 的输出事件及载荷类型；`defineExpose` 显式暴露内部成员给父组件（`<script setup>` 默认封闭，父组件通过模板 ref 才能访问被 expose 的方法/属性）。props+emits 构成组件对外接口契约。

**Q13. 组件的生命周期有哪些？`onMounted` 里适合做什么？**

主要钩子（组合式）：`onBeforeMount`→`onMounted`（DOM 就绪）→`onBeforeUpdate`→`onUpdated`→`onBeforeUnmount`→`onUnmounted`。`onMounted` 适合：**拉取初始数据、初始化第三方库、访问/操作 DOM、启动定时器、建立 SignalR/WebSocket 连接**——因为此时 DOM 已挂载可安全操作。对应清理必须放 `onBeforeUnmount`/`onUnmounted`。

**Q14. 为什么要在 `onUnmounted` 里做清理？不清理会怎样？（必考）**

定时器、事件监听器、WebSocket/SignalR 连接、订阅这些资源**不随组件销毁自动消失**。不清理导致：① **内存泄漏**——组件已销毁但定时器/闭包仍持有引用无法回收；② **报错**——回调操作已销毁组件的状态；③ **重复订阅**——反复进出页面订阅叠加，一次推送触发多次。所以 `onMounted` 里 `setInterval`/`addEventListener`/`conn.on` 建立的，必须在 `onUnmounted` 里 `clearInterval`/`removeEventListener`/`conn.off` 严格配对清理。（CP6 的 `DashboardView` 退订 SignalR、`IotMonitorView` 清定时器都是范例。）

**Q15. `shallowRef` 是什么？什么场景用？**

`ref` 默认深度响应（递归代理对象内部所有层级）；`shallowRef` **只追踪 `.value` 本身的替换**，不追踪内部属性变化，省掉深度代理开销。适合装**大型不可变数据**（大数组整体替换）或**第三方库实例**（图表、3D 场景对象——内部由库自己管，不需 Vue 追踪）。用它性能更好，但内部改动不触发更新（需要时用 `triggerRef` 手动触发）。普通业务表单**不要**用（会漏更新）。

**Q16. 箭头函数和普通函数的区别？**

① **`this` 绑定**：箭头函数没有自己的 `this`，词法捕获定义时外层的 `this`（且不可被 `call`/`bind`/`apply` 改变）；普通函数的 `this` 由调用方式决定（谁调用指谁）。② 箭头函数**没有 `arguments` 对象**。③ 箭头函数**不能作构造函数**（不能 `new`）。组合式 API 里几乎不用 this，回调普遍用箭头函数避免 this 丢失。

**Q17. `??`（空合并）和 `||`（逻辑或）的区别？**

`??` 只在左值为 **`null` 或 `undefined`** 时返回右值；`||` 对**所有假值**（`0`/`''`/`false`/`NaN`/`null`/`undefined`）都返回右值。处理数字、字符串等**合法假值有意义**的字段必须用 `??`，否则 `0`、`''` 会被误替换。例：`qty ?? 10` 保留合法的 `0`，`qty || 10` 会把 `0` 换成 `10`（bug）。

**Q18. TypeScript 的 `interface` 和 `type` 有什么区别？**

`interface` 描述对象形状，支持**声明合并**（同名自动合并）和 `extends` 继承，更接近传统 OOP。`type` 是类型别名，能表达 interface 的一切，**还能**表达联合类型（`A | B`）、交叉类型、元组、映射类型、条件类型等 interface 做不到的。经验法则：**描述对象结构优先 `interface`，需要联合/交叉/工具类型用 `type`**。两者大部分场景可互换。

**Q19. `any`、`unknown`、`never` 的区别？**

`any` 关闭类型检查（什么都能赋值、能调任意方法），是「逃生舱」但会**污染**整条类型链，应尽量避免。`unknown` 是「类型安全的 any」——能接收任意值，但**使用前必须先收窄**（`typeof`/`in`/类型守卫），适合接外部不确定数据。`never` 表示「永不出现的值」——用于一定抛异常/死循环的函数返回类型，以及 `switch` 穷尽检查后 default 分支（配合编译期强制处理所有情况）。优先用 `unknown` 代替 `any`。

**Q20. Vue 的单向数据流是什么意思？props 能直接改吗？**

单向数据流指数据**从父组件通过 props 流向子组件（向下）**，子组件**通过 emit 事件通知父组件（向上）** 由父修改数据源——数据流向单一、可预测。子组件**不应直接修改 props**（Vue 会警告，且父组件重渲染会覆盖你的改动）。要「改」props 值，应 emit 事件让父组件改，或在子组件内基于 prop 建本地 ref/computed。`v-model` 正是这套机制（prop 下传 + update 事件上冒）的语法糖，看似双向实则仍是单向数据流。

---

## 自测清单（能全部脱口而出才算过关）

- [ ] 能说清 `const` 锁的是绑定不是内容，能举 `const arr; arr.push()` 合法的例子
- [ ] 能解释箭头函数 `this` 词法绑定，并说出与普通函数的三点区别
- [ ] 能默写 JS 数组方法 ↔ LINQ 对照（map/filter/find/some/every/reduce）
- [ ] 知道 `sort()` 原地修改、默认按字符串排、数字要传比较器
- [ ] 能写完整 async/await + try/catch/finally 的请求处理（含 loading 态）
- [ ] 说得清 `??` 和 `||` 的区别，知道数字字段该用哪个
- [ ] 能解释 `import type` 为什么零运行时开销
- [ ] 能背 ref vs reactive 对照表，尤其 reactive 解构/整体替换丢响应性两坑
- [ ] 能讲 Vue2 defineProperty vs Vue3 Proxy 的响应式区别（含数组/新增属性坑）
- [ ] 说得清 computed 缓存机制 vs method 每次执行
- [ ] 能说 watch 的 immediate/deep 作用，知道侦听 reactive 属性要用 getter
- [ ] 能背 v-if vs v-show 对比表
- [ ] 能讲清 v-for 用 index 当 key 导致表单错位的「就地复用」坑
- [ ] 能拆解 v-model = `:modelValue` + `@update:modelValue`
- [ ] 知道 `.prevent`/`.stop`/`.trim`/`.number` 各做什么
- [ ] 能画生命周期顺序图，说清 onMounted 拉数据、onUnmounted 清理
- [ ] 能说为什么 onMounted 起的定时器/订阅必须在 onUnmounted 清理
- [ ] 能解释 defineProps/defineEmits/defineExpose 的作用
- [ ] 能说 composable 是什么、useXxx 命名约定、和 mixin 的三点优势
- [ ] 能背组合式 vs 选项式对照表，能读懂选项式代码
- [ ] 能把组件/props/emit/composable/Pinia 映射到 C# 概念

---

## 动手练习 3 个

### 练习 1：写一个 `useCountdown` composable（巩固 §4 §6 §8）

**需求**：封装一个倒计时组合式函数，用于「发送验证码后 60 秒禁用按钮」。
**要求**：
- 命名 `useCountdown`，接收初始秒数参数（默认 60）。
- 返回 `{ remaining, running, start }`——`remaining`（剩余秒数 ref）、`running`（是否进行中 computed）、`start()`（开始倒计时方法）。
- 用 `setInterval` 每秒递减，到 0 自动停止。
- **在 `onUnmounted` 里 `clearInterval` 清理**（防泄漏）——这是考点。

**参考骨架**：
```ts
import { ref, computed, onUnmounted } from 'vue'
export function useCountdown(initial = 60) {
  const remaining = ref(0)
  const running = computed(() => remaining.value > 0)
  let timer: number | undefined
  function start() {
    remaining.value = initial
    timer = window.setInterval(() => {
      remaining.value--
      if (remaining.value <= 0 && timer) window.clearInterval(timer)
    }, 1000)
  }
  onUnmounted(() => { if (timer) window.clearInterval(timer) })
  return { remaining, running, start }
}
```
**自检**：`running` 为什么用 computed 而非 ref？（答：它是派生自 remaining 的值，用 computed 自动同步且缓存。）为什么 onUnmounted 清理必不可少？（答：组件在倒计时中途被销毁，定时器仍跑会泄漏/报错。）

### 练习 2：仿 `StockQueryView` 写一个带 loading 的保存函数（巩固 §1.7 §4.2）

**需求**：写一个 `onSave` 异步函数，保存一个表单，要求：
- 有 `saving` ref 控制按钮 loading。
- `try/catch/finally` 结构，成功弹 `ElMessage.success`，失败弹 `ElMessage.error(e?.message ?? '错误')`。
- `finally` 里复位 `saving.value = false`。
- 保存成功后调用 `listRef.value?.reload()` 刷新列表。

**自检**：对照 `StockQueryView.vue` 第 224-242 行的 `onQcSave`，检查你是否：① 请求前置 `saving=true`；② `catch` 用了可选链 `e?.message`；③ `finally` 复位 loading（不能放 try 里，失败就不复位了）。

### 练习 3：把一段选项式组件改写成组合式 `<script setup>`（巩固 §9）

**给定选项式代码**：
```js
export default {
  props: { userId: { type: String, required: true } },
  data() { return { user: null, loading: false } },
  computed: { displayName() { return this.user?.name ?? '(未加载)' } },
  watch: { userId() { this.fetchUser() } },
  methods: {
    async fetchUser() {
      this.loading = true
      try { this.user = (await api.getUser(this.userId)).data }
      finally { this.loading = false }
    },
  },
  mounted() { this.fetchUser() },
}
```

**要求**：改写为 `<script setup lang="ts">`，用 `defineProps`/`ref`/`computed`/`watch`/`onMounted`。

**参考答案**：
```ts
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
const props = defineProps<{ userId: string }>()
const user = ref<User | null>(null)
const loading = ref(false)
const displayName = computed(() => user.value?.name ?? '(未加载)')
async function fetchUser() {
  loading.value = true
  try { user.value = (await api.getUser(props.userId)).data }
  finally { loading.value = false }
}
watch(() => props.userId, fetchUser)   // 注意：侦听 prop 用 getter
onMounted(fetchUser)
</script>
```
**自检**：① `this.userId` → `props.userId`；② `this.user` → `user.value`；③ `watch: { userId() {} }` → `watch(() => props.userId, fetchUser)`（**必须用 getter 包 prop**，这是坑）；④ `mounted()` → `onMounted()`；⑤ 全程无 `this`。

---

## 本章小结

你已经打通：现代 JS（用 LINQ/Task/C# 语法糖当锚点）→ TypeScript（回到熟悉的静态类型世界）→ Vue 3 心智模型（声明式 = WPF 数据绑定）→ 响应式系统（ref/reactive/computed/watch + Proxy 原理，面试第一考点）→ 模板语法（v-if/v-for/v-model 全集与坑）→ 生命周期（onMounted 拉数据、onUnmounted 清理）→ `<script setup>` 宏 → composable（替代 mixin 的复用利器）→ 选项式对照 → C# 迁移总表。

所有代码都是 CP6 生产前端的真实标本——`useBreakpoint`/`useValidation`/`useLinkage` composable、`StockQueryView` 的完整 CRUD、`format.ts` 的 TS 类型、`wmsHub`/`DashboardView` 的 SignalR 清理。**面试时你能直接说「我读过一个制造业生产管理系统的前端，它的响应式布局 composable 用引用计数管理全局 resize 监听、卸载时零泄漏清理」——这就是 5 年经验强度的谈资。**

下一章（Day 2 第 5 章）将进入 Element Plus 组件库 + 表格/表单/弹窗实战，继续用 CP6 真实页面拆解。

> **温习动作**：合上本章，尝试脱口回答自测清单每一项；卡壳的回到对应小节重读那段 CP6 代码。面试前一晚只需过一遍「面试题 20 问」+「ref vs reactive / v-if vs v-show / 生命周期图」三张核心表。
