# 06 · JavaScript 与 TypeScript：C# 开发者迁移心智

## 1. 最大误区：语法像，运行模型就一样

TypeScript 的类型主要在编译期被擦除，浏览器运行的是 JavaScript。C# 类型通常保留到 CLR 元数据并参与运行时反射。

因此 TS `interface` 不能直接用于运行时 `instanceof`，后端返回的数据也不会因为写了接口就自动验证。

## 2. 变量与相等

- 默认 `const`，需要重新赋值才用 `let`。
- `const` 只禁止变量重新绑定，不让对象深度不可变。
- 使用 `===`/`!==`，避免 `==` 的隐式类型转换。

```ts
const stock = { qty: 1 }
stock.qty = 2       // 可以
// stock = { qty: 3 } // 不可以
```

## 3. `undefined` 与 `null`

JS 有两种空值：

- `undefined`：未提供/未初始化/属性不存在。
- `null`：显式表示空。

API 契约要统一。可选属性 `ownerCd?: string` 意味着可能 undefined；`ownerCd: string | null` 意味着字段存在但可 null。

`?.` 和 `??` 与 C# 外观相似；`||` 会把 `0`、空字符串、false 也当后备条件，不能随意代替 `??`。

## 4. 对象复制是浅复制

```ts
const next = { ...old, status: 'PASSED' }
```

只复制第一层。嵌套数组/对象仍共享引用。与 C# record `with` 的浅复制边界类似。

## 5. 数组方法与 LINQ

| JS/TS | LINQ | 注意 |
|---|---|---|
| `map` | Select | 立即创建新数组 |
| `filter` | Where | JS 数组上立即遍历 |
| `find` | FirstOrDefault 类似 | 返回 undefined |
| `some` | Any | 短路 |
| `every` | All | 空数组返回 true |
| `reduce` | Aggregate | 初始值和类型推断 |
| `sort` | OrderBy 的表面对应 | 原地修改数组 |

JS `sort()` 默认按字符串排序：`[2,10].sort()` 得到 `[10,2]`。数字排序写 `(a,b)=>a-b`。`toSorted` 返回新数组，避免原地修改响应式状态。

## 6. 闭包和 `this`

箭头函数不创建自己的 `this`，捕获外层；普通函数的 this 取决于调用方式。

Vue 组合式 API 更少依赖 this。面试遇到 this 题仍要会：

```ts
const obj = {
  value: 1,
  normal() { return this.value },
  arrow: () => this // 不是 obj
}
```

## 7. Promise 与 async/await

Promise 表示未来结果，async 函数总返回 Promise。

```ts
const [stock, history] = await Promise.all([
  stockApi.search(query),
  stockApi.history(id, 30),
])
```

与 C# `Task.WhenAll` 类似，但取消模型不同。浏览器通常用 `AbortController`：

```ts
const controller = new AbortController()
fetch(url, { signal: controller.signal })
controller.abort()
```

axios 也支持 signal。组件卸载或新搜索覆盖旧搜索时应取消旧请求或丢弃过期响应。

## 8. 事件循环

JavaScript 主线程执行调用栈；Promise continuation 属于 microtask，timer 属于 task/macrotask。一次同步长计算会阻塞页面渲染和交互。

典型顺序题：

```ts
console.log('A')
setTimeout(() => console.log('B'), 0)
Promise.resolve().then(() => console.log('C'))
console.log('D')
// A D C B
```

不要把 Promise 当多线程。Web Worker 才提供独立 worker 线程模型。

## 9. `interface` 与 `type`

- interface 擅长对象契约、声明合并、extends。
- type 可表达联合、交叉、映射和条件类型。

团队一致性比教条选择重要。

```ts
type QcStatus = 'PENDING' | 'PASSED' | 'FAILED' | 'HOLD'

interface Stock {
  id: string
  productCd: string
  availableQty: number
  qcStatus: QcStatus
}
```

字面量联合能让无效状态在编译期报错，比任意 string 更强。

## 10. `any`、`unknown`、`never`

- `any` 关闭类型检查，污染会传播。
- `unknown` 表示未知，使用前必须收窄。
- `never` 表示不会有值，可做穷尽检查。

```ts
function assertNever(value: never): never {
  throw new Error(`Unexpected: ${String(value)}`)
}
```

API 响应是外部输入，TypeScript 类型断言不等于运行时验证。高风险边界可用 schema validator。

## 11. 泛型

```ts
interface ApiResponse<T> {
  code: number
  message: string
  data: T
}
```

TS 泛型约束：

```ts
function byId<T extends { id: string }>(rows: T[]): Map<string, T> {
  return new Map(rows.map(row => [row.id, row]))
}
```

与 C# 不同的是类型擦除和结构类型系统：只要形状兼容就可赋值，不必显式实现接口。

## 12. 类型收窄

```ts
function label(value: string | number) {
  if (typeof value === 'number') return value.toFixed(2)
  return value.trim()
}
```

对象联合最好用判别字段：

```ts
type Result =
  | { ok: true; data: Stock[] }
  | { ok: false; error: string }
```

## 13. 模块

ES module 的 import/export 是运行时模块系统的一部分，不等于 C# namespace。`import type` 只用于类型，可从输出中移除并减少循环依赖风险。

## 14. 数值和日期差异

JS `number` 是双精度浮点，没有 C# decimal。金额前端展示/临时计算要遵循后端规则；关键金额计算放权威后端，或使用经过选择的 decimal 库。

Date 代表时间点但 API 和本地时区转换容易踩坑。不要用字符串 `slice(0,10)` 处理所有日期语义；纯业务日期、UTC 时间点和本地时间应区分。当前 CP6 某些页面为显示方便使用 slice，这是可工作的局部实现，不是通用日期方案。

## 高频陷阱

1. TypeScript 接口会在运行时验证响应。
2. const 对象不可修改。
3. `||` 和 `??` 等价。
4. spread 是深复制。
5. Promise 等于线程。
6. Array.sort 默认按数字。

## 闭卷验收

- [ ] 解释 TS 类型擦除和运行时验证差异。
- [ ] 写字面量联合和穷尽检查。
- [ ] 解释事件循环输出顺序。
- [ ] 比较 Promise 与 Task 的取消方式。
- [ ] 找出一个当前前端的 any 边界并提出收紧方案。

