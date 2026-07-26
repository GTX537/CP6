# 项目 3 · 实现 latest-only 的 Vue 查询状态机

## 目标

为 `CpListPage` 或独立 composable 实现“只有最新请求可以修改 rows、total、loading 和 error”。支持取消、组件卸载、条件变更重置页码，并用可控 Promise 证明 A/B 乱序不会污染页面。

## 1. 状态定义

```ts
type QueryState<T> =
  | { kind: 'idle'; rows: T[]; total: number }
  | { kind: 'loading'; rows: T[]; total: number; requestId: number }
  | { kind: 'success'; rows: T[]; total: number; requestId: number }
  | { kind: 'error'; rows: T[]; total: number; requestId: number; error: AppError }
```

是否在 loading 时保留旧 rows 是产品决定。列表页通常保留并加 loading 遮罩，首次加载可显示 skeleton。

## 2. API

```ts
interface QueryInput<F> {
  page: number
  size: number
  filters: F
}

interface QueryResult<T> {
  rows: T[]
  total: number
}

type Fetcher<T, F> = (
  input: QueryInput<F>,
  signal: AbortSignal
) => Promise<QueryResult<T>>
```

让 fetcher 接收 signal，而不是 composable 猜 axios/fetch 实现。

## 3. 核心算法

```ts
let sequence = 0
let controller: AbortController | undefined

async function execute(input: QueryInput<F>) {
  const requestId = ++sequence
  controller?.abort()
  controller = new AbortController()

  state.value = {
    kind: 'loading',
    rows: currentRows(),
    total: currentTotal(),
    requestId,
  }

  try {
    const result = await fetcher(input, controller.signal)
    if (requestId !== sequence) return
    state.value = { kind: 'success', ...result, requestId }
  } catch (error: unknown) {
    if (requestId !== sequence || isCanceled(error)) return
    state.value = {
      kind: 'error',
      rows: currentRows(),
      total: currentTotal(),
      requestId,
      error: normalizeError(error),
    }
  }
}
```

序号是最终保护；AbortController 减少浪费。旧请求即使不响应取消，也不能写状态。

## 4. 页码规则

```text
filters change → page=1 → execute
size change → page=1 → execute
page change → keep filters → execute
reload after save → keep current page，若本页变空可回前一页
reset → default filters/page=1
```

避免多个 watcher 同时触发两次请求。可以统一 command 或对变化做单一 watch。

## 5. 组件卸载

`onBeforeUnmount`：增加 sequence 使旧结果失效，并 abort controller。取消不 toast。

若 composable 可在组件外使用，提供 `dispose()`，不要硬绑定生命周期而无法测试。

## 6. 错误协议

```ts
interface AppError {
  kind: 'network' | 'validation' | 'conflict' | 'unauthorized' | 'server'
  code?: string
  message: string
  traceId?: string
}
```

409 保留状态和用户输入；401 由 HTTP 层 refresh；cancel 不进入 error；未知 500 显示一次并带 trace id。

## 7. 可控 Promise 测试

```ts
const a = deferred<QueryResult<Row>>()
const b = deferred<QueryResult<Row>>()
fetcher.mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise)

const pa = execute(queryA)
const pb = execute(queryB)
b.resolve({ rows: [rowB], total: 1 })
await pb
a.resolve({ rows: [rowA], total: 1 })
await pa

expect(state.value.rows).toEqual([rowB])
```

还要断言 A 的 finally 没把 B 的 loading 关掉。

## 8. 测试矩阵

- A 慢 B 快。
- A 快 B 慢。
- A 错 B 成功。
- A 成功 B 错。
- 旧请求不支持 abort。
- 卸载后响应。
- filters+page 同时变只发一次。
- 409 不清旧 rows。
- cancel 零 toast。

## 9. 接入库存历史弹窗

历史是 keyed latest request：当前 `stockId` 也是接受响应条件。打开 B 后 A 响应必须忽略。弹窗先显示 B 标题与 loading，不能保留 A 表格。

## 10. 自我评审

- rows/total/loading/error 是否只有一个所有者？
- 所有完成路径都检查 requestId 吗？
- abort error 是否被全局提示？
- watchers 会重复发请求吗？
- 测试是否控制完成顺序而非 sleep？
- unmount 后是否写状态？

## 11. 面试口述

不要只说“用 AbortController”。说明取消是优化，requestId 是正确性保护；rows 与 loading 都必须 latest-only；用 deferred test 稳定复现，不依赖真实延迟。

