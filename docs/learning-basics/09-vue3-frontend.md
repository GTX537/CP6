# 09 · Vue3 + Pinia + 动态路由

## 🌱 你将学到

- `ref` 和 `reactive` 到底有什么区别——别再蒙
- 看懂 CP6 的"登录后才知道有哪些菜单"是怎么实现的
- 理解 axios 拦截器在干嘛
- 知道 Pinia 的 store 该装什么、不装什么

---

## 🍳 生活类比：办公桌 vs 公共白板

你和同事共用一个办公室。

- 你桌上的便签 = 组件内部的 ref（只有你看到、改）
- 办公室中央的白板 = Pinia store（所有人都看、都改）

不是所有东西都要写白板。"我今天要打的电话" 写自己便签就行。"今天的会议室预约表"才写白板。

**写 store 的标准**：跨组件共享 + 跨页面持久。

---

## 🔎 看 CP6 代码

### main.ts —— 应用入口

`D:\CP6\cp6.web\src\main.ts`：

```typescript
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'

import App from './App.vue'
import router from './router'
import i18n, { initI18n } from './i18n'

async function bootstrap() {
  // 关键：先加载翻译（防止首屏白屏卡在 i18n key）
  await initI18n()

  const app = createApp(App)

  // 全局错误处理：吞掉某种瞬态错误
  app.config.errorHandler = (err, _instance, info) => {
    const msg = (err as Error)?.message ?? String(err)
    if (/Cannot read properties of null/.test(msg)) {
      console.warn('[忽略]', info, msg)
      return
    }
    console.error('[Vue 错误]', info, err)
  }

  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.use(ElementPlus)

  app.mount('#app')
}

bootstrap()
```

注意 `await initI18n()` 必须在 `createApp` 之前。否则组件先渲染，翻译还没好，用户看到 `{{ $t('login.title') }}` 这种原始 key。

### 一个简单组件（典型套路）

```vue
<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessageBox } from 'element-plus'
import http from '@/api/http'
import type { Stock } from '@/types/stock'

// 组件内部状态用 ref
const list = ref<Stock[]>([])
const loading = ref(false)
const keyword = ref('')

// 计算属性
const filtered = computed(() =>
  list.value.filter(s => s.productCd.includes(keyword.value))
)

// 方法
async function loadList() {
  loading.value = true
  try {
    list.value = await http.get<Stock[]>('/wms/stock')
  } finally {
    loading.value = false
  }
}

async function setQcStatus(row: Stock, status: string) {
  await ElMessageBox.confirm(`将 ${row.productCd} 改为 ${status}?`, '确认')
  await http.post('/wms/stock/qc-status', { stockId: row.id, status })
  await loadList()
}

// 生命周期
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

### axios 拦截器

`cp6.web/src/api/http.ts` 风格：

```typescript
import axios from 'axios'
import router from '@/router'
import { ElMessage } from 'element-plus'

const instance = axios.create({
  baseURL: '/api',
  timeout: 30000
})

// 请求拦截：自动附加 JWT
instance.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// 响应拦截：统一解包 + 401 处理
instance.interceptors.response.use(
  res => {
    if (res.data?.code === 200) return res.data.data   // 解开 { code, data } 包装
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

业务代码因此可以直接：

```typescript
const list = await http.get<Stock[]>('/wms/stock')   // list 直接是 Stock[]
```

而不是：

```typescript
const res = await axios.get('/api/wms/stock')
if (res.data.code === 200) {
    const list = res.data.data   // 每次都要解包
}
```

### 动态路由

`cp6.web/src/router/index.ts`：

```typescript
// 所有可能的页面映射（硬编码字典）
const viewModules: Record<string, () => Promise<any>> = {
  '/dashboard': () => import('@/views/dashboard/DashboardView.vue'),
  '/order':     () => import('@/views/erp/OrderEntryView.vue'),
  '/wms/stock': () => import('@/views/wms/StockQueryView.vue'),
  // ... 100+ 路径
}

// 静态路由（不需要登录的页面）
const staticRoutes: RouteRecordRaw[] = [
  { path: '/login',    component: () => import('@/views/LoginView.vue') },
  { path: '/',  name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    children: []   // 等动态填充
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes: staticRoutes
})

let dynamicRoutesAdded = false

// 登录后调这个，把用户菜单变成路由
export function addDynamicRoutes(menus: any[]) {
  const routeMenus = menus.filter(m => m.routePath && viewModules[m.routePath])
  routeMenus.forEach(menu => {
    router.addRoute('layout', {
      path: menu.routePath.replace(/^\//, ''),
      name: menu.routePath.replace(/^\//, ''),
      component: viewModules[menu.routePath]
    })
  })
  dynamicRoutesAdded = true
}

// 路由守卫
router.beforeEach((to, _from, next) => {
  const token = localStorage.getItem('token')

  if (to.path === '/login') return next()
  if (!token) return next('/login')

  // 刷新页面后路由丢了，从 localStorage 恢复
  if (!dynamicRoutesAdded) {
    const menusStr = localStorage.getItem('menus')
    if (menusStr) {
      addDynamicRoutes(JSON.parse(menusStr))
      return next({ ...to, replace: true })   // 重新匹配
    }
    return next('/login')
  }
  next()
})
```

---

## 🤔 为什么这样

### Q1: ref 和 reactive 区别

```typescript
const a = ref(0)                    // 基本类型
const b = ref({ name: 'tt' })       // 对象 也可以
const c = reactive({ name: 'tt' })  // 对象 只能

// 访问
a.value             // ref 要 .value
b.value.name        // ref 也是 .value
c.name              // reactive 直接
```

模板里 ref 会自动 `.value`，所以模板里两者写法一致。

**为什么推荐 ref**：

- 整体替换不丢响应性：`a.value = 5`、`b.value = { name: 'new' }` 都行
- reactive 解构丢响应性：`const { name } = c` → name 不是响应的
- reactive 整体替换丢响应性：`c = { name: 'new' }` → 失效

**统一 ref**心智简单，CP6 大部分用 ref。

### Q2: Composition API 和 Options API 区别

```vue
<!-- Options API（老） -->
<script>
export default {
  data() { return { count: 0 } },
  methods: {
    increment() { this.count++ }
  },
  computed: {
    doubled() { return this.count * 2 }
  }
}
</script>

<!-- Composition API（新，CP6 用这个） -->
<script setup>
import { ref, computed } from 'vue'
const count = ref(0)
const doubled = computed(() => count.value * 2)
function increment() { count.value++ }
</script>
```

Composition API 让"一个功能的状态 + 方法 + 计算属性"写在一起，复杂组件更易维护。

### Q3: 动态路由为什么这么麻烦

普通项目：所有路由一开始就注册好。

CP6：不同用户看不同菜单（基于角色），所以路由要"登录后才知道"。

流程：

1. 用户登录 → API 返回菜单列表
2. `addDynamicRoutes(menus)` 把菜单变成路由 + 存 localStorage
3. 进入页面正常用
4. **F5 刷新整个 SPA** → 路由列表丢了（回到只有静态路由）
5. 路由守卫看到 token 还在但 `dynamicRoutesAdded = false` → 从 localStorage 拿 menus → 再 addDynamicRoutes

这是 SPA + 动态路由的标准套路，看一遍后就懂了。

### Q4: 为什么 axios 要拦截器

不拦截的话每个业务代码都要：

```typescript
const res = await axios.get('/api/orders', {
  headers: { Authorization: `Bearer ${token}` }
})
if (res.data.code === 200) {
  return res.data.data
} else {
  ElMessage.error(res.data.message)
  throw new Error(...)
}
if (res.status === 401) { /* 跳登录 */ }
```

写 100 个接口写 100 次。拦截器把这些**横切关注点**抽出来，业务代码只关心业务：

```typescript
const orders = await http.get<Order[]>('/orders')   // 一行
```

---

## ⚠️ 容易搞错的地方

### 1. ref 在 JS 里忘 .value

```typescript
const count = ref(0)
count++           // ❌ TS 报错，count 不是数字
count.value++     // ✅
```

### 2. reactive 整体重赋

```typescript
let user = reactive({ name: 'a' })
user = reactive({ name: 'b' })   // ❌ 模板不会更新
```

改 ref 就没这个问题。

### 3. v-for + v-if 同级

```vue
<!-- ❌ Vue 3 里 v-if 先于 v-for 求值，item 还没定义 -->
<el-tag v-for="item in list" v-if="item.active">{{ item.name }}</el-tag>

<!-- ✅ 用 template 包 -->
<template v-for="item in list" :key="item.id">
  <el-tag v-if="item.active">{{ item.name }}</el-tag>
</template>
```

### 4. onMounted 监听了没 onUnmounted 解绑

详见第 08 章。SignalR、定时器、事件监听都要成对解绑。

### 5. 全局错误处理吞掉所有错误

```typescript
// ❌ 反例
app.config.errorHandler = (err) => { /* 什么都不做 */ }
```

CP6 只吞特定瞬态错误（"Cannot read properties of null"），其他正常打印。否则所有 bug 都被藏起来。

### 6. localStorage 存敏感数据

```typescript
localStorage.setItem('password', user.password)   // ❌
```

localStorage 受 XSS 威胁（攻击者 JS 能读）。token 存这里是接受的折中，密码绝不能存。

---

## ✋ 动手试试

### 任务 1：理解 ref 的"自动解包"

新建一个 Vue 组件试试：

```vue
<script setup>
import { ref } from 'vue'
const count = ref(0)
console.log(count)         // 看控制台是什么
console.log(count.value)   // 是数字
</script>

<template>
  <div>{{ count }}</div>   <!-- 这里不用 .value -->
  <button @click="count++">点</button>   <!-- 模板里居然可以 ++ -->
</template>
```

亲手感受"模板里自动解包、JS 里要 .value"。

### 任务 2：在 DevTools Vue 标签看响应性

启动前端，浏览器 F12，安装 Vue DevTools 扩展。

打开 Vue 标签，选一个组件，看它的 setup state。修改某个 ref → 观察界面变化。

这就是"响应式"的视觉证据。

### 任务 3：跟一次"登录 → 菜单 → 动态路由"流程

启动前端，登录。

打开 F12 → Network → 看登录响应里有 `menus` 字段（一个数组）。

打开 Application → Local Storage → 看 `token` 和 `menus` 都存进去了。

F5 刷新页面 → 路由守卫检测到 `dynamicRoutesAdded = false` + localStorage 有 menus → 调 `addDynamicRoutes` → 你又看到完整菜单。

故意去 Application → Local Storage 把 `menus` 删掉 → 再 F5 → 你被踢到 login（因为路由守卫没法恢复）。这就是"动态路由 + 刷新恢复"的设计。

### 任务 4：自己加一个 Pinia store

新建 `cp6.web/src/stores/counter.ts`（CP6 已经有这个文件，看一眼）：

```typescript
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useCounterStore = defineStore('counter', () => {
  const count = ref(0)
  const doubled = computed(() => count.value * 2)
  function increment() { count.value++ }
  return { count, doubled, increment }
})
```

在某个组件用：

```vue
<script setup>
import { useCounterStore } from '@/stores/counter'
const counter = useCounterStore()
</script>

<template>
  <button @click="counter.increment">{{ counter.count }} / {{ counter.doubled }}</button>
</template>
```

体验"跨组件共享状态"。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/09-vue3-frontend.md`](../learning/09-vue3-frontend.md)
- Vue 3 官方：[Composition API FAQ](https://vuejs.org/guide/extras/composition-api-faq.html)
- Pinia 官方：[文档](https://pinia.vuejs.org/)
- 关键词搜索："ref vs reactive"、"Vue 3 动态路由"
