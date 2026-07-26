# 07 · Vue 3 响应式、组件与生命周期

## 1. Vue 的核心循环

```text
响应式状态被读取
→ Vue 记录依赖
→ 状态变化触发相关 effect
→ 生成新虚拟 DOM
→ diff / patch 最小更新真实 DOM
```

声明式渲染的价值是描述“状态对应什么 UI”，而不是手工找 DOM 改文字。

## 2. `ref`

```ts
const loading = ref(false)
const rows = ref<Stock[]>([])
```

脚本里通过 `.value`，模板会自动解包。适合基本类型，也适合需要整体替换的对象/数组。

## 3. `reactive`

```ts
const filters = reactive({
  warehouseCd: '',
  productCd: '',
})
```

返回 Proxy，适合一组相关字段。不能随意整体替换变量保持同一代理引用：

```ts
// filters = newFilters // const 不允许；即使用 let 也会让依赖复杂
Object.assign(filters, newFilters)
```

## 4. 解构丢响应性

```ts
const state = reactive({ count: 0 })
const { count } = state // count 是普通快照
```

使用 `toRefs(state)` 或直接 `state.count`。Pinia setup store 返回的 state 也用 `storeToRefs`，方法直接解构即可。

## 5. ref vs reactive 的选择

没有绝对答案。一个实用约定：

- 需要整体替换、基本类型、数组：ref。
- 紧密相关的表单对象：reactive。
- 公共组合式函数返回多个独立状态：多个 ref，方便解构。

比“哪一个性能更好”更重要的是一致性和不丢响应性。

## 6. computed

computed 表达派生状态，有缓存，依赖不变时不重复求值。

```ts
const availableRows = computed(() =>
  rows.value.filter(x => x.availableQty > 0)
)
```

computed getter 应尽量纯，不在其中发请求或修改其他状态。带 setter 的 computed 可用于双向映射，但要保持可预测。

## 7. watch 与 watchEffect

- `watch(source, cb)` 明确来源，可拿新旧值。
- `watchEffect(cb)` 自动追踪同步执行期间读取的依赖。

搜索框防抖示例：

```ts
watch(keyword, (value, _, onCleanup) => {
  const timer = window.setTimeout(() => search(value), 300)
  onCleanup(() => window.clearTimeout(timer))
})
```

异步竞态还需 AbortController 或请求序号，单纯清 timer 不会取消已经发出的请求。

## 8. shallowRef 与 markRaw

大型第三方对象如 Three.js 场景不需要深度响应式代理。可用 `shallowRef` 或 `markRaw`，避免代理复杂对象和不必要追踪。

不要为普通业务对象过早使用浅响应；它会让嵌套修改不触发更新，需要整体替换。

## 9. 模板指令

### `v-if` vs `v-show`

- v-if 创建/销毁子树，切换成本高，初始不成立时不渲染。
- v-show 始终渲染，仅切 display，适合频繁切换。

### `v-for` key

key 表示节点身份。使用稳定业务 Id，不用数组 index，特别是可插入/排序的表单列表，否则组件状态可能错位。

### `v-model`

组件上的 v-model 默认等价：

```vue
<Child :model-value="value" @update:model-value="value = $event" />
```

Vue 3.4+ 可使用 `defineModel`，但要根据项目版本与约定。

## 10. 组件通信

| 方式 | 适合 | 不适合 |
|---|---|---|
| props down | 父传子数据 | 子直接修改父状态 |
| emits up | 子报告事件 | 跨多层传播大量事件 |
| v-model | 表单双向契约 | 隐藏复杂副作用 |
| slots | 父定义子布局片段 | 全局状态 |
| provide/inject | 深层上下文 | 普通业务全局 store |
| Pinia | 跨页面/跨组件共享 | 临时局部状态 |

“组件通信方式有哪些”回答后必须给选择规则。

## 11. `<script setup>`

编译期宏：`defineProps`、`defineEmits`、`defineExpose`、`withDefaults` 等不需要导入。

```ts
const props = defineProps<{
  stock: Stock
  readonly?: boolean
}>()

const emit = defineEmits<{
  saved: [id: string]
}>()
```

不要直接修改 prop；创建本地编辑副本或通过 emit 请求父更新。

## 12. 生命周期

| 钩子 | 常见用途 |
|---|---|
| onMounted | DOM/第三方库、初始请求 |
| onUpdated | 少用；避免在其中无条件改状态 |
| onBeforeUnmount/onUnmounted | 清理 timer、监听、连接、observer |
| onErrorCaptured | 局部错误边界 |

`setup` 本身已经执行初始化逻辑，不是所有请求都必须等 mounted；不依赖 DOM 的数据请求可在 setup 或路由数据层启动。

## 13. composable

组合式函数封装可复用的有状态逻辑：

```ts
export function useRequest<T>(loader: () => Promise<T>) {
  const data = ref<T>()
  const loading = ref(false)
  const error = ref<unknown>()

  async function run() {
    loading.value = true
    error.value = undefined
    try { data.value = await loader() }
    catch (e) { error.value = e; throw e }
    finally { loading.value = false }
  }

  return { data, loading, error, run }
}
```

良好 composable：输入明确、返回稳定、清理副作用、可单测、不偷偷依赖全局单例。

## 14. 异步竞态

用户先搜 A，再快速搜 B；A 响应后到，覆盖 B。

方案：

- 取消旧请求。
- 递增 requestId，只接受最新。
- 使用 TanStack Query 等管理请求缓存/竞态；当前项目未必使用，不要说成现状。

## 15. CP6 StockQueryView 当前结构

页面使用：

- `CpPageShell` 提供页面壳。
- `CpListPage` 处理服务端分页与筛选。
- computed 生成 columns/searchFields，支持 i18n 更新。
- toolbar slot 放 `hasStockOnly`。
- column slots 定制数量、QC、操作按钮。
- 原生 el-dialog 保留复杂 QC 与历史弹窗。

这是“模板覆盖 80% + slot/escape hatch 处理特殊 20%”的组件架构。

## 高频陷阱

1. reactive 解构后仍响应。
2. computed 适合发 API 请求。
3. watch deep 默认没有成本。
4. v-for 用 index 做 key 永远安全。
5. onMounted 是唯一能请求数据的地方。
6. composable 只是把函数移到另一个文件。

## 闭卷验收

- [ ] 用 Proxy/依赖追踪解释响应式。
- [ ] 修复 reactive 解构问题。
- [ ] 写可清理的防抖 watch。
- [ ] 给六种组件通信选择场景。
- [ ] 解释 StockQueryView 的模板/slot 分层。

