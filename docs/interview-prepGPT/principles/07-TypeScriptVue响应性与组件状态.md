# 07 · TypeScript、Vue 响应性与组件状态

Vue 代码的难点不是记 `ref`、`watch` 和生命周期，而是让状态只有一个可信所有者，让派生值可推导，让副作用可取消，让 API 边界有类型。本章从 JavaScript 运行模型到 Vue Proxy，再到组件、Pinia、路由和 HTTP。

## 1. JavaScript 值与引用

原始值复制独立；对象变量复制引用。浅拷贝只复制一层：

```ts
const next = { ...state }
next.nested.count++ // state.nested 也变
```

需要不可变更新时复制被修改路径，或使用合适工具。不要用 JSON stringify 深拷贝 Date、Map、undefined、循环引用。

## 2. 事件循环

同步调用栈执行完后处理 microtask（Promise continuation），再进入后续 task（timer、事件等，具体宿主调度更复杂）。

```ts
console.log('A')
setTimeout(() => console.log('B'), 0)
Promise.resolve().then(() => console.log('C'))
console.log('D')
// A D C B
```

理解事件循环能解释 nextTick、Promise 回调、定时器和 UI 渲染时点。

## 3. TypeScript 只在编译期保护

类型会被擦除。服务器返回错误 JSON，`as Stock` 不会运行时验证。

边界策略：

- 内部代码靠静态类型。
- 外部输入用 schema/手工校验。
- 错误 normalize 为稳定 AppError。
- 不用 `as any/as never` 掩盖不匹配。

## 4. `unknown` 优于 `any`

catch 变量/外部 JSON 先 unknown：

```ts
function messageOf(error: unknown): string {
  if (axios.isAxiosError(error))
    return String(error.response?.data?.message ?? error.message)
  return error instanceof Error ? error.message : 'Unknown error'
}
```

any 关闭后续类型检查；unknown 强迫收窄。

## 5. 联合类型与状态机

不要用三个可能矛盾布尔：

```ts
loading, loaded, error
```

使用判别联合：

```ts
type LoadState<T> =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'success'; data: T }
  | { kind: 'error'; error: AppError }
```

switch exhaustive 检查减少非法组合。

## 6. Vue 响应性模型

`ref` 用 getter/setter 跟踪 `.value`；`reactive` 用 Proxy 拦截对象属性读取/写入。effect/computed/watch 在执行时收集依赖，变化时调度。

模板自动解包 ref；脚本中通常 `.value`。

## 7. `ref` 还是 `reactive`

- 单值、可整体替换、异步结果：ref。
- 一组始终一起操作的字段：reactive 可读。
- 大对象/第三方实例可用 shallowRef，避免深代理。

不要混成教条。更重要的是是否整体替换、是否解构、所有权是否清晰。

## 8. 解构失去响应性

```ts
const state = reactive({ count: 0 })
const { count } = state // 普通 number 快照
```

用 `toRefs(state)`、computed 或继续访问 `state.count`。Pinia store 状态用 `storeToRefs`。

## 9. computed 与 watch

computed 是纯派生值，有缓存：

```ts
const available = computed(() => physical.value - allocated.value)
```

watch 用于副作用：请求、localStorage、imperative API。若能 computed，不要 watch 后再把结果写进另一个 ref，容易双源。

## 10. watch 清理

搜索词变化触发请求时取消旧请求：

```ts
watch(query, async (q, _, onCleanup) => {
  const controller = new AbortController()
  onCleanup(() => controller.abort())
  result.value = await search(q, controller.signal)
})
```

清理还适用于 timer、事件订阅、observer。组件卸载前必须释放外部副作用。

## 11. flush 与 nextTick

Vue 批量状态更新并异步刷新 DOM。修改状态后立刻读 DOM 可能还是旧值；`await nextTick()` 等本轮渲染。

watch flush pre/post/sync 决定相对渲染时点。sync 风险高，可能频繁触发/循环。

## 12. Props 单向数据流

子组件不直接修改 prop。通过 emit、v-model 契约或 store 让所有者更新。

对象 prop 内部字段虽技术上可改，但会产生隐式父状态修改。把“能改”与“应该改”分开。

## 13. `v-model` 原理

默认等价：

```vue
<Child :model-value="value" @update:model-value="value = $event" />
```

组件应声明 prop/emit 类型。多个 v-model 用参数名。不要在子组件维护一份长期不同步的本地 copy，除非明确 draft/commit 模式。

## 14. Slot 是结构注入

`CpListPage` 用 slots 允许领域页面替换列、toolbar 等局部结构。Slot props 是子传父模板作用域的数据。

通用组件应提供稳定 slot/prop 协议和 escape hatch；不是把每个页面需求都变成布尔 prop。

## 15. 组合式函数

Composable 封装可复用状态+行为，如 `useLatestRequest`、`usePagination`。要求：

- 输入输出明确。
- 清理与组件生命周期绑定。
- 不偷偷操作全局 singleton。
- 测试可注入 API/时间。

只被一个组件使用且逻辑简单时，不必为了“高级”抽取。

## 16. Pinia 状态边界

跨页面用户、权限、菜单、长期选择进 store；弹窗开关和单页草稿留组件。

Store action 需要表达 loading/error/重试；退出登录 reset。不要把每个 API response 永久缓存而没有新鲜度策略。

Set/Map 在 ref 内替换更容易触发清晰更新：

```ts
actionKeys.value = new Set(keys)
```

## 17. 路由不是安全边界

守卫负责 UX：登录跳转、动态菜单路由、强制改密。后端端点仍认证授权。

动态路由要测：刷新、无菜单、未知 component、不同角色重登、重复名称、退出清理。

## 18. HTTP 层

统一实例管理 baseURL、timeout、Cookie、CSRF、refresh、错误 normalization。业务 API 模块保留 endpoint 与 DTO。

拦截器不要同时吞业务错误又 toast，页面会失去处理 409/字段错误的能力。

## 19. 请求竞态

序号/AbortController 让旧响应不覆盖新状态。loading 也需最新请求保护。

多个相同请求可 single-flight 或缓存，但 key 必须包含所有影响结果的参数、租户/用户上下文。

## 20. 乐观 UI

先更新 UI 再请求适合可回滚、冲突低操作。库存/财务高风险写入通常等待服务器确认，或显示明确 pending 状态。

乐观更新需保存 previous state、处理并发响应和 rollback，不能只 catch 后 reload。

## 21. Element Plus 表单

规则分：即时格式、提交业务、服务端并发。表单 reset 要区分新建/编辑初值；关闭弹窗时异步请求可能仍回写已换对象。

表格性能先服务端分页和列投影；行多再虚拟化。slot 内避免每次渲染昂贵计算。

## 22. i18n

代码存稳定 key，不把自由文本当 key。动态 key 要有类型/回退。后端返回稳定 error code，前端翻译。

日期、数字、数量按 locale 格式化，但业务编码不做文化比较。

## 23. 可访问性

按钮图标有可读 label；对话框焦点、键盘、错误关联；颜色不是唯一状态信号；表单 label 与输入关联。

组件库默认并不保证业务组合后的可访问性。

## 24. 测试

- 纯函数/composable：Vitest。
- component contract：Vue Test Utils。
- store/router：http mock。
- Cookie/浏览器/完整路由：Playwright。

异步用 deferred/fake timer，不真实 sleep。

## 25. 必做实验

1. reactive 解构失活与 toRefs。
2. watch 请求取消。
3. A/B 乱序响应与 latest-only。
4. 三个 401 single-flight refresh。
5. 权限 loaded/error 状态和 DOM。
6. QC 400/409/500/cancel 提示次数。

## 26. 闭卷问题

1. TS 为什么不能验证服务器 JSON？
2. unknown 比 any 好在哪里？
3. reactive 解构为何失活？
4. computed 与 watch 的决策规则？
5. nextTick 在等什么？
6. v-model 的 prop/emit 是什么？
7. 哪些状态不该进 Pinia？
8. 路由守卫为什么不是授权？
9. 旧响应怎样破坏页面？
10. 拦截器错误所有权如何设计？

