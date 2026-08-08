# 06 · Vue 页面状态、请求竞态与安全

前端页面不是“把接口数据放进表格”。它同时维护筛选、分页、弹窗、权限、请求、错误、登录刷新和用户连续操作。本章用当前 `StockQueryView.vue`、`CpListPage`、`http.ts` 和 `v-permission` 建立一套开发者读页面的方法：先画状态机，再找并发写入，再验证用户在慢网、过期登录和重复点击下看到什么。

## 1. 先按状态所有权拆页面

`StockQueryView.vue` 当前把通用列表行为交给 `CpListPage`，领域页面保留：

- 列配置和筛选字段。
- `hasStockOnly`。
- 库存历史弹窗。
- QC 状态弹窗。
- QC 保存状态与重载。

状态所有权图：

```text
CpListPage
  ├─ filters
  ├─ page / size
  ├─ rows / loading / total
  └─ reload()

StockQueryView
  ├─ hasStockOnly
  ├─ historyVisible / historyStock / historyTxns
  ├─ qcDialogVisible / qcTarget / qcNewStatus / qcReason
  └─ qcSaving
```

组件抽象是否成功，不看代码少了多少行，而看状态所有权是否清楚。若父子两边都能修改 page、loading 或 rows，竞态会更难控制。

## 2. `ref` 与 `computed` 不只是语法

### 2.1 为什么 columns 是 computed

列标题调用 `t(...)`。当语言切换时，computed 会重新计算列标签。如果 columns 在模块加载时创建为普通数组，界面可能继续显示旧语言。

```ts
const columns = computed<ListColumn[]>(() => [
  { prop: 'warehouseCd', label: t('wms.common.warehouse') },
])
```

这里 computed 表达“列定义依赖当前 locale”，不是为了炫技。

### 2.2 为什么 `listRef` 的类型值得写完整

```ts
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
```

模板挂载前值为 null，因此调用使用可选链。若子组件没有通过 `defineExpose` 暴露 `reload`，类型或运行时会失败。组件 ref 是跨组件命令式通道，应只暴露少量明确动作，避免父组件操控子组件内部状态。

### 2.3 reactive 解构为什么可能失活

`reactive` 返回 Proxy。直接：

```ts
const { count } = reactive({ count: 0 })
```

得到的是当前原始值，不再通过 Proxy 读取。需要 `toRefs`、computed 或保留 `state.count`。Pinia setup store 的状态解构也应使用 `storeToRefs`，方法可直接解构。

## 3. 列表 fetch 是一个协议

当前 `fetchList` 接收：

```ts
({ page, size, filters })
```

返回：

```ts
{ rows, total }
```

它把 `CpListPage` 的通用协议适配为库存 API：

```ts
const q = { page, pageSize: size, hasStockOnly: hasStockOnly.value }
// 按存在性加入筛选
const res = await stockApi.search(q)
return { rows: res.data.items, total: res.data.total }
```

阅读这种适配器时要核对：

- filters 的值类型是否真的由 FilterField 保证。
- `as never` 是否掩盖了 API 参数类型不匹配。
- API envelope 是否稳定为 `res.data.items`。
- total 可能 undefined 时模板如何表现。
- 组件是否防旧请求覆盖新请求。

`as never` 让编译器闭嘴，却也移除了参数契约检查。更好的方式是声明 `StockSearchParams`，逐字段构造并让 TypeScript 校验。

## 4. 请求竞态：最后发出不等于最后返回

```text
用户查 Product A → 请求 1，慢
用户马上查 Product B → 请求 2，快
请求 2 返回 → rows=B
请求 1 返回 → rows 被改回 A
```

如果 `CpListPage` 没有 latest-only 或取消逻辑，页面会显示与筛选框不一致的数据。

### 4.1 请求序号方案

```ts
let requestId = 0

async function reload() {
  const id = ++requestId
  loading.value = true
  try {
    const result = await props.fetch(currentQuery())
    if (id !== requestId) return
    rows.value = result.rows
    total.value = result.total
  } finally {
    if (id === requestId) loading.value = false
  }
}
```

不仅结果要 latest-only，`loading` 也要由最新请求关闭，否则旧请求 finally 可能把新请求的 loading 提前关掉。

### 4.2 AbortController 方案

新请求发起前取消旧请求，能减少后端和网络浪费。axios 支持 `signal`。但取消不是业务错误，统一拦截器不应 toast “请求失败”。

取消与序号可以同时使用：取消尽力停止旧工作，序号作为最终状态保护。

## 5. `hasStockOnly` 为什么走 toolbar slot

注释说明 `CpFilterBar` 当前没有 boolean 字段类型，所以复选框放在 toolbar slot，fetch 通过闭包读取 `hasStockOnly.value`，切换时调用 `reload()`。

这是合理的“逃生舱”：通用组件不必为了一个页面立刻支持所有控件。但要检查：

- 切换后是否重置 page=1。
- reload 时是否读取最新值。
- URL/页面恢复是否需要持久化这个条件。
- 多个页面都需要 boolean 后，是否该提升到 FilterField 协议。

抽象边界应由重复需求推动，而不是看到一次特殊控件就扩展通用组件。

## 6. 历史弹窗的竞态

当前：

```ts
historyStock.value = row
const res = await stockApi.history(row.id, 365)
historyTxns.value = res.data.transactions
historyVisible.value = true
```

如果用户快速点击行 A 再点击行 B：

```text
historyStock = A，发请求 A
historyStock = B，发请求 B
B 先返回，historyTxns = B，打开弹窗
A 后返回，historyTxns = A，但 historyStock 仍是 B
```

标题区域显示 B，表格却是 A。这是比列表竞态更隐蔽的“主从状态撕裂”。

修复：用 request id，并在接受响应前确认当前 target id：

```ts
let historyRequest = 0

async function openHistory(row: Stock) {
  const id = ++historyRequest
  historyStock.value = row
  historyVisible.value = true
  historyTxns.value = []
  const res = await stockApi.history(row.id, 365)
  if (id !== historyRequest || historyStock.value?.id !== row.id) return
  historyTxns.value = res.data.transactions
}
```

弹窗先打开并显示 loading，用户反馈更及时；关闭弹窗时也可取消请求。

## 7. QC 保存状态机

当前状态：

```text
closed
→ open(row)
→ editing
→ saving
   ├─ success → close + local update + reload
   └─ failure → remain open + show error
```

### 7.1 重复点击

按钮使用 `:loading="qcSaving"` 和 `:disabled`，能降低重复点击。但 JavaScript handler 仍可被其他方式再次触发，函数开头最好加：

```ts
if (qcSaving.value) return
```

服务端仍需幂等/并发控制，前端禁用按钮不是数据一致性保证。

### 7.2 先本地修改又 reload 的取舍

成功后代码先：

```ts
qcTarget.value.qcStatus = res.data.qcStatus
```

再 `reloadList()`。本地修改让 UI 立即变化，reload 用服务器真相校正。若 reload 失败，用户至少看到成功响应中的状态；但还要提示刷新失败与否，避免以为列表其他字段已同步。

### 7.3 409 应由页面处理

HTTP 拦截器对 409 不全局 toast，允许页面展示“数据已被其他人修改，是否刷新”。QC 更新如果带 rowversion，应在冲突时保留用户输入，展示服务器最新状态并让用户决定，而不是自动覆盖。

## 8. 当前存在双重错误提示的可能

`http.ts` 对除 401/409 外的错误全局 `ElMessage.error`。`onQcSave` 的 catch 又：

```ts
ElMessage.error(e?.message ?? 'Network error')
```

同一个 500/400 可能先被拦截器提示业务错误，再被页面提示 Axios message，用户看到两条 toast，而且第二条更差。

错误所有权需要协议：

- 全局只处理真正全局的 401、网络离线和未知 5xx。
- 业务端点错误由页面根据 error code 展示字段/对话框。
- 或 config 加 `suppressGlobalError`，调用方明确接管。
- 取消请求不提示。

不能让拦截器和页面都默认“我负责提示”。

## 9. 401 single-flight 的完整状态机

模块级 `refreshPromise` 保证并发 401 只触发一次 refresh。调用底层 http 发 refresh，同时通过 URL 特判避免 refresh 自身无限递归。

需要测试：

1. 三个普通请求同时 401，只发一次 `/auth/refresh`。
2. refresh 成功，三个请求各重放一次。
3. refresh 失败，只导航一次登录页，避免三条 toast。
4. 已 `_retried` 的请求再次 401，不再 refresh。
5. login 本身 401 不 refresh。

### 9.1 `_retried` 类型

当前直接给 Axios config 增加字段。应使用 module augmentation 扩展类型，避免 `any`：

```ts
declare module 'axios' {
  export interface InternalAxiosRequestConfig {
    _retried?: boolean
    suppressGlobalError?: boolean
  }
}
```

### 9.2 非幂等请求重放

若 401 来自认证中间件，业务 handler 未执行，重放通常安全。但若代理、自定义中间件或响应丢失让客户端误判，POST 可能重复。服务端应支持 idempotency key；前端不能把所有 POST 重放视为绝对安全。

## 10. CSRF 双提交的前端细节

`cp6_csrf` Cookie 非 httpOnly，前端读取后写入 `X-CSRF-Token`。认证 token 是 httpOnly，前端不读取。

开发者要区分：

```text
cp6_at / cp6_rt：凭证，httpOnly
cp6_csrf：请求证明，JS 可读
cp6_authed：非敏感 UX 标志，不是凭证
```

`getCookie` 用动态 RegExp 构造。当前 name 是内部常量，风险可控；若接受外部 name，需要转义正则字符。

## 11. `v-permission` 的 fail-open UX

权限 store `loaded=false` 时，指令先保留按钮并 watch；加载成功后无权限则 `el.remove()`。优点是避免权限还没回来就永久误删；缺点是首屏可能短暂显示不该有的按钮。

安全不依赖它，因为后端强校验。UX 可选择：

- 当前 fail-open：减少布局闪烁，但短暂露按钮。
- fail-closed：权限加载前隐藏，更安全感，但所有按钮后出现。
- skeleton：保留布局，不展示真实动作。

### 11.1 store 加载失败的状态缺失

`loadMyActions` catch 后仍保持 `loaded=false`。指令会一直保留元素，且 UI 不知道是在加载还是失败。

更清晰状态：

```text
idle → loading → loaded
               ↘ error
```

失败时可重试或按产品策略隐藏敏感动作。即使后端安全，用户不断点击 403 也是差体验。

### 11.2 WeakMap 的作用

指令为每个 DOM 保存 watch stop handle，updated 时先停止旧 watcher，unmounted 时清理，避免 watcher 泄漏。读自定义指令时要检查生命周期是否对称：mounted 创建，updated 替换，unmounted 释放。

## 12. 类型安全不是把所有东西写成 `any`

当前几个减弱类型的地方：

- i18n global 强转 `any`。
- API query `as never`。
- catch `e: any`。
- axios 自定义 `_retried` 未正式扩展。

改进顺序应按风险：

1. API 请求/响应 DTO，直接保护业务数据。
2. 错误 normalizer，把 unknown 变成稳定 `AppError`。
3. Axios config augmentation。
4. i18n typed keys，减少拼写错误。

TypeScript 的价值是把边界错误提前到构建期，不是追求“零 any”数字。

## 13. 页面性能怎么量

库存页服务端分页，通常不会一次渲染万行。性能排查仍要分：

- 请求慢：后端/SQL。
- JSON 大：字段投影或 pageSize。
- 渲染慢：列 slot、复杂组件、DOM 数量。
- 输入卡：watch 过重、同步计算。
- 语言切换卡：大量 computed 重建。

用浏览器 Performance、Vue Devtools 和 Network 分别找证据。不要看到 el-table 就直接上虚拟列表。

## 14. 必做实验 A：历史弹窗撕裂

让 A 的 history 延迟 800ms、B 延迟 100ms，快速点击 A→B。写测试断言：

```text
标题是 B
表格也必须是 B
A 的迟到响应被忽略
```

先写一个会失败的测试，再加 request id 修复。

## 15. 必做实验 B：并发 401

使用 Vitest mock axios adapter：

1. 三个请求首次返回 401。
2. refresh 返回成功。
3. 重放返回各自数据。
4. 断言 refresh 调用次数为 1。
5. 再让 refresh 失败，断言 auth signal 清理和导航行为只发生预期次数。

共享 module state 会污染测试，测试间要重置模块或暴露受控 reset。

## 16. 必做实验 C：错误提示所有权

让 QC API 返回 400、409、500、网络取消四种结果。记录 toast 数量和内容。目标：

| 错误 | 全局提示 | 页面提示 |
|---|---|---|
| 400 业务校验 | 可关闭 | 字段/业务消息一次 |
| 409 并发 | 无 | 冲突对话框 |
| 500 未知 | 一次 | 不重复 |
| cancel | 无 | 无 |

## 17. 必做实验 D：权限加载失败

让 `myActions`：延迟成功、立即失败、退出后重登不同角色。验证按钮闪烁、watch 清理和 store reset。检查后端 API 在所有情况下仍返回正确 403/成功。

## 18. 面试回答模板

> 当前库存页通过 CpPageShell/CpListPage 复用列表壳层，领域页保留筛选、列、历史和 QC 状态。开发上我会重点看状态所有权和异步竞态，而不是只看 Composition API 语法。例如历史弹窗快速点 A 再点 B，旧响应可能让标题和表格属于不同库存，需要 request id 或取消；列表 loading 也必须只由最新请求关闭。HTTP 层用 httpOnly Cookie、CSRF header 和共享 refreshPromise，能把并发 401 合并成一次刷新，但非幂等重放仍需服务端幂等。当前全局拦截器和 QC catch 还可能重复 toast，权限 store 加载失败也缺少 error 状态，这些都应该用可控 Promise 的单测覆盖。

## 19. 闭卷验收

1. 画 StockQueryView 与 CpListPage 的状态所有权。
2. 复现列表或历史弹窗的乱序响应。
3. 写出 latest-only 对 rows 和 loading 的双重保护。
4. 解释 401 single-flight 的五个测试场景。
5. 区分认证 Cookie、CSRF Cookie 和 UX 标志。
6. 找出当前四个类型逃生口并按风险排序。
7. 解释 `v-permission` fail-open 的 UX 取舍和安全边界。
8. 设计 400/409/500/cancel 不重复提示的错误协议。
