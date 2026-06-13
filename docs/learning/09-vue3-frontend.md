# 09 · Vue3 + TS + Pinia + 动态路由

## 📍 学习目标

1. Vue3 Composition API 跟 Options API 的根本区别？什么时候 `ref` 什么时候 `reactive`？
2. CP6 的动态路由怎么实现？刷新页面后怎么不丢路由？
3. Pinia 的 store 切片粒度怎么定？什么时候用 store，什么时候用 props？
4. `defineAsyncComponent` 和 `() => import('...')` 有什么区别？
5. axios 拦截器怎么处理 401 自动续签 / 重新登录？

---

## 🔎 真实代码切片

### `main.ts` —— 入口编排

```typescript
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import './assets/main.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import App from './App.vue'
import router from './router'
import i18n, { initI18n } from './i18n'

async function bootstrap() {
  // 关键：先从 API 加载翻译，再创建 app（防止首屏白屏卡在 i18n key）
  await initI18n()

  const app = createApp(App)

  // 全局错误处理：吞掉 Vue patch 阶段瞬态错误
  app.config.errorHandler = (err, _instance, info) => {
    const msg = (err as Error)?.message ?? String(err)
    if (/Cannot read properties of null \(reading '(parentNode|subTree|el)'\)/.test(msg)) {
      console.warn('[Recoverable patch error swallowed]', info, msg)
      return
    }
    console.error('[Vue error]', info, err)
  }

  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.use(ElementPlus)

  for (const [key, component] of Object.entries(ElementPlusIconsVue))
    app.component(key, component)

  app.mount('#app')
}

bootstrap()
```

### `router/index.ts` —— 动态路由 + 守卫

```typescript
const viewModules: Record<string, () => Promise<any>> = {
  '/dashboard': () => import('@/views/dashboard/DashboardView.vue'),
  '/order':     () => import('@/views/erp/OrderEntryView.vue'),
  '/wms/stock': () => import('@/views/wms/StockQueryView.vue'),
  // ... 100+ 路径全部静态映射
}

const staticRoutes: RouteRecordRaw[] = [
  { path: '/login',    component: () => import('@/views/LoginView.vue') },
  // 独立窗口模式（popup）：no Layout
  { path: '/order/window',  component: () => import('@/views/erp/OrderEntryView.vue'),
    meta: { standalone: true } },
  { path: '/',  name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    children: []   // 等动态填充
  }
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: staticRoutes
})

let dynamicRoutesAdded = false

export function addDynamicRoutes(menus: any[]) {
  const routeMenus = menus.filter(m => m.routePath && viewModules[m.routePath])
  const firstRoute = routeMenus[0]?.routePath || '/login'

  // 重建 layout 路由让 redirect 生效
  router.removeRoute('layout')
  router.addRoute({
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    redirect: firstRoute,
    children: routeMenus.map(menu => ({
      path: menu.routePath.replace(/^\//, ''),
      name: menu.routePath.replace(/^\//, ''),
      component: viewModules[menu.routePath]
    }))
  })

  dynamicRoutesAdded = true
}

router.beforeEach((to, _from, next) => {
  const token = localStorage.getItem('token')
  if (to.path === '/login') return next()
  if (!token) return next('/login')
  if (to.meta?.standalone) return next()

  // 刷新页面后路由丢了的情况：从 localStorage 恢复
  if (!dynamicRoutesAdded) {
    const menusStr = localStorage.getItem('menus')
    if (menusStr) {
      addDynamicRoutes(JSON.parse(menusStr))
      return next({ ...to, replace: true })
    }
    return next('/login')
  }
  next()
})
```

### `api/http.ts` —— axios 拦截器

```typescript
import axios from 'axios'
import router from '@/router'
import { ElMessage } from 'element-plus'

const instance = axios.create({
  baseURL: '/api',
  timeout: 30000
})

// 请求拦截：附加 JWT
instance.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// 响应拦截：统一解包 + 401 处理
instance.interceptors.response.use(
  res => {
    // CP6 后端约定：{ code, message, data }
    if (res.data?.code === 200) return res.data.data
    ElMessage.error(res.data?.message || '操作失败')
    return Promise.reject(new Error(res.data?.message))
  },
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      localStorage.removeItem('menus')
      router.push('/login')
      ElMessage.warning('登录已过期，请重新登录')
    } else {
      ElMessage.error(err.message || '网络错误')
    }
    return Promise.reject(err)
  }
)

export default instance
```

### 一个典型 View（Composition API）

```vue
<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessageBox } from 'element-plus'
import http from '@/api/http'
import type { Stock } from '@/types/stock'

const list = ref<Stock[]>([])
const loading = ref(false)
const keyword = ref('')

const filtered = computed(() =>
  list.value.filter(s => s.productCd.includes(keyword.value))
)

async function loadList() {
  loading.value = true
  try {
    list.value = await http.get<Stock[]>('/wms/stock')
  } finally {
    loading.value = false
  }
}

async function setQcStatus(row: Stock, status: string) {
  await ElMessageBox.confirm(`确认将 ${row.productCd} 改为 ${status}?`, '提示')
  await http.post('/wms/stock/qc-status', { stockId: row.id, status })
  await loadList()
}

onMounted(loadList)
</script>

<template>
  <el-input v-model="keyword" placeholder="搜索零件号" />
  <el-table v-loading="loading" :data="filtered">
    <el-table-column prop="productCd" label="零件" />
    <el-table-column prop="availableQty" label="可用" />
    <el-table-column prop="qcStatus" label="QC 状态" />
    <el-table-column>
      <template #default="{ row }">
        <el-button @click="setQcStatus(row, 'HOLD')">挂起</el-button>
      </template>
    </el-table-column>
  </el-table>
</template>
```

---

## 💡 资深视角

### Composition API vs Options API

| 维度 | Options API | Composition API |
|---|---|---|
| 写法 | `data() / methods / computed` | `setup()` 或 `<script setup>` |
| 逻辑组织 | 按"种类"分散 | 按"关注点"聚合 |
| TypeScript | 类型推断弱 | 类型推断好 |
| 复用 | mixin（有冲突风险） | composable（纯函数式） |
| 心智模型 | 像 Vue 2 类组件 | 像 React Hooks |

CP6 全部用 `<script setup>` 风格的 Composition API，正确选择。

### `ref` vs `reactive`

```typescript
const count = ref(0)                    // 基本类型，.value 访问
const user = reactive({ name: 'tt' })   // 对象，直接访问

// 在模板里 .value 自动解开
// 在 JS 里要写 count.value++
// 但 reactive 解构会丢响应：const { name } = user → name 不再响应
// 要保留响应：const { name } = toRefs(user)
```

**经验法则**：

- 基本类型用 `ref`（数字、字符串、布尔）
- 对象/数组用 `ref` 也行（推荐统一），或用 `reactive`
- **永远用 `ref`** 也可以，CP6 大部分用 `ref`

`reactive` 的坑：

```typescript
let user = reactive({ name: 'a' })
user = reactive({ name: 'b' })   // ❌ 整个换掉 → 失去响应（旧引用还在模板里）
```

`ref` 不会有这个问题：

```typescript
const user = ref({ name: 'a' })
user.value = { name: 'b' }   // ✅ 响应正常
```

### 动态路由刷新页面问题

```
1. 登录成功 → addDynamicRoutes(menus) → router 有 /dashboard 等路由
2. F5 刷新 → 整个 SPA 重启 → router 回到 staticRoutes（没有 /dashboard）
3. 浏览器尝试导航到 /dashboard → 404
```

CP6 的解法是**两手准备**：

1. 登录时 `localStorage.setItem('menus', JSON.stringify(menus))`
2. `router.beforeEach` 守卫里如果 `!dynamicRoutesAdded && menus 在 localStorage` → 立刻 addDynamicRoutes 再 next

```typescript
if (!dynamicRoutesAdded) {
  const menusStr = localStorage.getItem('menus')
  if (menusStr) {
    addDynamicRoutes(JSON.parse(menusStr))
    return next({ ...to, replace: true })  // 重新匹配 → 找到 /dashboard
  }
  return next('/login')
}
```

`replace: true` 不留历史记录，避免后退乱跳。

### Pinia store 切片粒度

CP6 当前 stores 文件夹只有 `counter.ts`（基本是 placeholder），生产应该按业务切：

```typescript
// stores/auth.ts
export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem('token') || '')
  const user = ref<User | null>(null)
  const menus = ref<Menu[]>([])

  const isLoggedIn = computed(() => !!token.value)

  async function login(req: LoginRequest) {
    const data = await http.post<LoginResponse>('/auth/login', req)
    token.value = data.token
    user.value = { id: data.userId, name: data.userName }
    menus.value = data.menus
    localStorage.setItem('token', data.token)
    localStorage.setItem('menus', JSON.stringify(data.menus))
    addDynamicRoutes(data.menus)
  }

  function logout() {
    token.value = ''
    user.value = null
    menus.value = []
    localStorage.clear()
    resetRoutes()
    router.push('/login')
  }

  return { token, user, menus, isLoggedIn, login, logout }
})
```

**切片原则**：

- 一个业务域一个 store（auth / dashboard / wms-stock / ...）
- 跨多 View 共享的状态进 store
- 单 View 内部状态用 `ref`（不进 store）
- 不要把所有数据都塞一个 store（"大泥球 store"）

### Composable 复用

跨 View 的逻辑抽成 composable：

```typescript
// composables/useTable.ts
export function useTable<T>(loader: () => Promise<T[]>) {
  const list = ref<T[]>([])
  const loading = ref(false)

  async function refresh() {
    loading.value = true
    try { list.value = await loader() }
    finally { loading.value = false }
  }

  return { list, loading, refresh }
}

// View 里
const { list: stocks, loading, refresh } = useTable(() => http.get('/wms/stock'))
onMounted(refresh)
```

CP6 的 `composables/` 文件夹就是干这事的。

### Vite 的开发优化

```typescript
// vite.config.ts
export default defineConfig({
  server: {
    port: 5173,
    proxy: {
      '/api':  { target: 'http://localhost:9991', changeOrigin: true },
      '/hubs': { target: 'http://localhost:9991', changeOrigin: true, ws: true }  // ws: true 是 WebSocket
    }
  }
})
```

- `/api` 代理到本地后端（开发期免 CORS）
- `/hubs` `ws: true` 让 SignalR WebSocket 也走代理
- HMR（热更新）默认开

### 全局 errorHandler 吞瞬态错误

```typescript
app.config.errorHandler = (err, _instance, info) => {
  if (/Cannot read properties of null \(reading '(parentNode|subTree|el)'\)/.test(msg)) {
    console.warn('[Recoverable patch error swallowed]', info, msg)
    return
  }
  console.error('[Vue error]', info, err)
}
```

这是 Vue 3 路由切换时偶发的 patch 阶段错误（DOM 已卸载但 patch 还在跑）。如果不处理整个组件树会崩溃。CP6 选择吞掉这类特定 message，其他异常正常打印。

**反例**：什么都 catch 然后 return —— 等于隐藏所有 bug。CP6 的实现只匹配特定字符串，安全。

---

## ⚠️ 踩坑记录

### 坑 1：`ref` 在模板自动解包，但深层路径不行

```vue
<template>
  <!-- 模板里 ref 自动 .value -->
  <div>{{ count }}</div>

  <!-- 但深层属性不自动 -->
  <div>{{ obj.list }}</div>  <!-- obj.list 是 ref → 模板里要 obj.list.value -->
</template>

<script setup>
const obj = { list: ref([]) }  // ❌ 这样写 obj.list 在模板里要 .value
const obj = ref({ list: [] })  // ✅ 推荐这样
</script>
```

### 坑 2：`v-for` 和 `v-if` 同级

Vue 3 里 `v-if` 优先级高于 `v-for`，跟 Vue 2 反过来。要在 v-for 上加条件用 computed 或 v-if 包外层：

```vue
<!-- ❌ Vue 3 这样会先判断 active（item 还没定义） -->
<el-tag v-for="item in list" v-if="item.active">{{ item.name }}</el-tag>

<!-- ✅ -->
<template v-for="item in list">
  <el-tag v-if="item.active">{{ item.name }}</el-tag>
</template>
```

### 坑 3：动态导入路径不能是变量

```typescript
// ❌ Vite 不能 bundle
const path = '/views/' + name + '.vue'
const Comp = () => import(path)

// ✅ 静态字符串或 glob
const modules = import.meta.glob('@/views/**/*.vue')
const Comp = modules[`/src/views/${name}.vue`]
```

CP6 的 `viewModules` 字典就是手动列举每个路径 —— 没用 glob 是因为要精确控制路由集合 + 类型安全。

### 坑 4：onUnmounted 里不解绑 SignalR 监听

```typescript
onMounted(() => conn.on('NewOperLog', handler))
onUnmounted(() => conn.off('NewOperLog', handler))  // 必须解绑！
```

不解绑会让多次进入页面累加监听，内存泄漏 + 重复执行。

### 坑 5：axios 拦截器里跳 router 导致死循环

```typescript
// ❌ 反例
instance.interceptors.response.use(null, err => {
  if (err.response?.status === 401) {
    router.push('/login')   // 如果 /login 页面又调一个需要 token 的接口 → 又 401 → 又跳
  }
})
```

修复：跳 login 前清空 token + 在 router 守卫里跳过 /login 自身。CP6 的实现做对了这点。

### 坑 6：刷新路由用 `next({...to, replace: true})` 而不是 `next()`

如果用 `next()`，原 URL 没变但路由已经加载，可能错过新加的路由匹配。`next({...to})` 让 router 重新走一遍 match。CP6 的实现是对的。

---

## 🧪 自检题

1. **响应性陷阱**：`const list = reactive([]); list = [1,2,3]` 之后模板会更新吗？  
   <details><summary>答案</summary>不会。reactive 返回的是 Proxy，整体赋值会丢响应。要么 <code>list.push(1,2,3)</code>，要么用 ref：<code>const list = ref([]); list.value = [1,2,3]</code>。</details>

2. **store 边界**：用户列表加载状态（loading）放 store 还是 ref？  
   <details><summary>答案</summary>如果只有一个 UserListView 用 → ref；如果多个 View 共享（如顶部进度条），放 store。<b>原则：能放 ref 就放 ref，store 是上层共享必要时才用</b>。</details>

3. **路由题**：登录后 F5 刷新到 /wms/stock 直接跳了 /login，怎么排查？  
   <details><summary>答案</summary>(1) F12 看 localStorage 有没有 token 和 menus；(2) 看 router.beforeEach 守卫里 dynamicRoutesAdded 是否走到 addDynamicRoutes 分支；(3) 看 viewModules 有没有 '/wms/stock' 这个 key（CP6 的硬编码字典容易漏）；(4) 看登录响应是否真的返回了包含 /wms/stock 的 menus。</details>

4. **重构题**：当前 viewModules 字典有 100 行，维护麻烦，怎么用 Vite 的 import.meta.glob 替代？  
   <details><summary>答案</summary>
   <pre><code>const modules = import.meta.glob('@/views/**/*View.vue')
function pathToComponent(routePath: string) {
  // /wms/stock → @/views/wms/StockQueryView.vue
  // 但映射不直接，CP6 的 routePath 跟文件名不一一对应
}</code></pre>
   不容易完全替代字典，因为 CP6 的 routePath 跟文件名规则不一致（如 /wms/stock → StockQueryView）。<b>最好混合</b>：字典只放规则不同的，其余用约定 + glob 自动匹配。
   </details>

5. **质疑题**：有人说 Vue 3 的 Composition API 跟 React Hooks 一样复杂，options API 更简单。怎么回应？  
   <details><summary>答案</summary>Composition API 的复杂主要来自<b>响应性原理</b>（ref/reactive/computed/watch）而不是 API 数量。对于<b>小项目</b>，Options API 确实更直观（data/methods/computed 各管各的）。对于<b>大项目 + 复杂状态</b>，Composition API 能把"一个功能"的代码聚合在一个 composable 文件里而不是散在 data/methods/computed/watch 多处。CP6 这种几十个 View + SignalR + i18n + 动态路由的复杂度，Composition API 维护性显著更好。</details>

---

## 🔗 延伸阅读

- [Vue 3 文档 - Composition API](https://vuejs.org/guide/extras/composition-api-faq.html)
- [Pinia 文档](https://pinia.vuejs.org/)
- [Vue Router 4 - Dynamic Routing](https://router.vuejs.org/guide/advanced/dynamic-routing.html)
- [VueUse](https://vueuse.org/) — 大量现成的 composable
- 项目内：`cp6.web/src/main.ts`、`cp6.web/src/router/index.ts`、`cp6.web/src/api/http.ts`
