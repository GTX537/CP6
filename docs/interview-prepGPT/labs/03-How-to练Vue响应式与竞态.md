# How to 建立 Vue 响应式与请求竞态实验

你将创建一个最小 Vue 3 + TypeScript 项目，观察 reactive 解构和异步响应竞态。

## 前置

- Node 20.19+ 或 22.12+（与当前前端 engines 对齐）。
- npm。

## 步骤 1：创建项目

在系统临时目录执行：

```powershell
$labParent = Join-Path $env:TEMP 'cp6-interview-vue'
New-Item -ItemType Directory -Force -Path $labParent | Out-Null
Set-Location $labParent
npm create vue@latest cp6-interview-vue-lab
$lab = Join-Path $labParent 'cp6-interview-vue-lab'
Set-Location $lab
npm install
npm run dev
```

在交互选项中至少启用 TypeScript；测试可按需要启用 Vitest。浏览器看到起始页即完成第一结果。

## 步骤 2：复现解构丢响应性

在一个组件中：

```vue
<script setup lang="ts">
import { reactive, toRefs } from 'vue'

const state = reactive({ count: 0 })
const { count: brokenCount } = state
const { count: liveCount } = toRefs(state)
</script>

<template>
  <button @click="state.count++">increment</button>
  <p>state: {{ state.count }}</p>
  <p>broken: {{ brokenCount }}</p>
  <p>live: {{ liveCount }}</p>
</template>
```

点击后观察 broken 不更新。

## 步骤 3：复现请求竞态

```ts
import { ref } from 'vue'

const result = ref('')

function fakeSearch(query: string): Promise<string> {
  const delay = query === 'A' ? 800 : 100
  return new Promise(resolve =>
    window.setTimeout(() => resolve(`result:${query}`), delay)
  )
}

async function search(query: string) {
  result.value = await fakeSearch(query)
}
```

快速调用 `search('A')` 再 `search('B')`，最终 A 覆盖 B。

## 步骤 4：用请求序号修复

```ts
let requestId = 0

async function safeSearch(query: string) {
  const id = ++requestId
  const next = await fakeSearch(query)
  if (id === requestId) result.value = next
}
```

重复 A→B，最终应保留 B。

## 步骤 5：理解 AbortController 版本

真实 fetch/axios 可取消旧请求：

```ts
let controller: AbortController | undefined

async function load(url: string) {
  controller?.abort()
  controller = new AbortController()
  const response = await fetch(url, { signal: controller.signal })
  return response.json()
}
```

组件卸载时 abort。取消错误应识别并静默，不显示“网络故障”。

## 步骤 6：写最小测试

若启用 Vitest，使用 fake timers 控制 800/100ms，断言 safeSearch 最终为 B。测试不要依赖真实等待一秒。

## 验证

```powershell
npm run type-check
npm run test:unit
npm run build
```

若项目未启用测试脚本，至少运行 type-check 和 build，并手工记录竞态前后结果。

## 排错

- Node 版本不满足：先 `node --version`，切到符合 engines 的版本。
- 目录已经存在：换一个项目名；不要用 `--force` 覆盖自己需要保留的实验。
- AbortError 被 toast：在 catch 中先识别 `error.name === 'AbortError'`。
- brokenCount 也更新：确认你没有使用 `toRefs` 或 computed 包装它。

## 完成后

用 60 秒回答：“Vue 响应式为什么会因解构丢失？搜索请求为何会乱序？我怎样选择取消或忽略旧响应？”
