# Day 2 第 6 章：Element Plus 实战与 CP6 前端架构解剖（Day 2 收官章）

> **本章定位**：前五章你学了 Vue 3 响应式、Composition API、组件通信、路由与 Pinia。本章把这些知识落到岗位日常——**"管理系统页面开发"**。制造业生产管理系统的前端，90% 的工作就是：查询列表页 + 编辑弹窗 + 权限按钮 + 多语言 + 实时推送。这五样东西本章全部用 CP6 真实生产代码讲透。
>
> **全栈闭环**：Day 1 你精读了后端 `StockController.Search`（库存查询接口）。本章主标本就是调用它的前端页面 `StockQueryView.vue`——学完本章，你能从浏览器点击"搜索"按钮开始，一路讲到 SQL 查询返回，再讲回表格渲染。这是面试官最爱听的"全链路叙述能力"。
>
> **标本仓库**：`C:\CP6\cp6.web`——Vue 3.5 + Element Plus 2.13 + TypeScript + Vite 8 + Pinia 3 + vue-router 5 + vue-i18n 11（版本出自 `cp6.web/package.json` 实测）。

---

## 目录

- [6.1 Element Plus 全景：它是什么、怎么进项目](#61)
- [6.2 管理系统页面解剖：StockQueryView.vue 全文精读（本章主线）](#62)
- [6.3 el-table 深用：插槽、标签映射、排序、固定列、多选、大数据](#63)
- [6.4 el-form 与校验：rules / validator / validate() 全景](#64)
- [6.5 弹窗与反馈：el-dialog 双模式复用、ElMessage、ElMessageBox](#65)
- [6.6 常用组件速查：select / date-picker / tabs / descriptions / badge](#66)
- [6.7 权限指令 v-permission 完整精读（面试亮点）](#67)
- [6.8 多语言实战：vue-i18n 在 CP6 的工业级用法](#68)
- [6.9 SignalR 实时推送前端侧](#69)
- [6.10 移动端适配案例：767px 卡片化改造](#610)
- [6.11 前端架构总结：目录全景与新页面开发步骤](#611)
- [6.12 面试专题：ElementUI vs Element Plus、表格性能、一万条数据](#612)
- [6.13 面试题 15 问（详细答案）](#613)
- [6.14 自测清单](#614)
- [6.15 动手练习 3 个](#615)

---

<a id="61"></a>
## 6.1 Element Plus 全景：它是什么、怎么进项目

### 6.1.1 组件/模式：Element Plus 是什么

一句话：**Element Plus 是 ElementUI 的 Vue 3 版本**，由饿了么前端团队开源的桌面端组件库，是国内管理系统（后台/ERP/MES/WMS）事实上的标准组件库。

面试时 JD 写"ElementUI"，你要主动说清这层关系：

| | ElementUI | Element Plus |
|---|---|---|
| 适配框架 | Vue 2 | Vue 3 |
| 语言 | JavaScript | TypeScript（类型完备） |
| 组件前缀 | `el-` | `el-`（一致） |
| 事件模型 | `this.$emit` / `sync` 修饰符 | `v-model:xxx` 多 v-model |
| 弹出层实现 | 手动挂 body | teleport（Vue 3 内置） |
| 图标 | 字体图标 `el-icon-edit` | 独立 SVG 包 `@element-plus/icons-vue` |
| 日期底层 | 自研 | day.js |

CP6 用的是 **Element Plus 2.13**（`package.json`: `"element-plus": "^2.13.6"`）——组件 API 与你在教程里看到的官方文档完全一致。

### 6.1.2 cp6.web 真实代码：全量引入（`C:\CP6\cp6.web\src\main.ts`）

```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
// ...字体与设计令牌样式
import './styles/element-overrides.css'   // ← 覆盖 Element Plus 默认观感的自定义样式
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

// ...
app.use(ElementPlus)

for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}
```

### 6.1.3 逐行解析

1. `import ElementPlus from 'element-plus'` + `app.use(ElementPlus)`：**全量引入**。所有 `el-xxx` 组件全局注册，模板里直接写 `<el-table>` 不需要 import。
2. `import 'element-plus/dist/index.css'`：全量样式。全量引入时必须手动引 CSS（按需引入时由插件自动注入）。
3. `import './styles/element-overrides.css'`：CP6 有自己的设计系统（`--cp-*` CSS 变量），这个文件覆盖 Element Plus 的默认外观（比如分页器、按钮圆角），让组件库长得像自家产品。**这是生产项目的常见做法：组件库提供行为，皮肤自己控。**
4. 图标循环注册：`@element-plus/icons-vue` 是独立包，图标是**SVG 组件**而非字体。这里用 `Object.entries` 把全部图标注册为全局组件，所以模板里可以直接 `<el-icon><Bell /></el-icon>`。业务代码里也有局部引入的写法（更利于 tree-shaking）：

```ts
// C:\CP6\cp6.web\src\views\oa\inbox\InboxPending.vue
import { Refresh } from '@element-plus/icons-vue'
// 模板：<el-button :icon="Refresh" circle size="small" ... />
```

### 6.1.4 全量引入 vs 按需引入

| 方式 | 写法 | 优点 | 缺点 |
|---|---|---|---|
| **全量**（CP6 现状） | `app.use(ElementPlus)` | 零配置、写页面不用 import 组件 | 首包大（几百 KB gzip 前） |
| **按需（自动导入）** | `unplugin-vue-components` + `unplugin-auto-import` 插件，模板里写 `el-button` 自动按需引入 | 包体最优、写法与全量一样爽 | 需要构建插件配置；类型提示要额外配 |
| **手动按需** | `import { ElButton } from 'element-plus'` | 精确可控 | 每个文件手动 import，繁琐 |

**CP6 的真实选择很有代表性**：入口 `main.ts` 全量注册（业务页面爽），但**基建模板组件里仍然显式 import**——看 `CpListPage.vue`：

```ts
// C:\CP6\cp6.web\src\components\templates\CpListPage.vue
import { ElMessage, ElPagination, ElTable, ElTableColumn, vLoading } from 'element-plus'
```

为什么基建组件要显式 import？因为**这些模板组件会被单元测试单独挂载**（vitest + @vue/test-utils），测试环境里没有 `app.use(ElementPlus)` 全局注册，显式 import 让组件自带依赖、可独立测试。这是一个面试可讲的工程细节。

另外注意 `vLoading`——`v-loading` 是**指令**不是组件，按需引入时要单独引指令对象。

### 6.1.5 全局配置：size 与 locale

Element Plus 支持通过 `app.use(ElementPlus, { size: 'small', locale: ja })` 或 `<el-config-provider :locale="...">` 做全局配置：

- `size`：全局组件尺寸（large/default/small）。
- `locale`：**组件库内置文案的语言**——分页器的"共 x 条/条每页"、日期面板的星期几、清空按钮等。

**CP6 的真实现状（诚实讲）**：我在 `cp6.web/src` 全局搜索 `el-config-provider` 和 `element-plus/es/locale`，**均无匹配**。也就是说 CP6 的多语言是"业务词条全走后端 API 的 vue-i18n 体系（见 6.8），Element Plus 组件内置文案未接 locale 同步"——组件内置文案停留在默认语言。这是一个真实的已知缺口，面试反而可以拿来讲："我知道完整方案是 `el-config-provider` 包住根组件、`:locale` 绑一个 computed，随 vue-i18n 的 locale 切换映射到 `element-plus/es/locale/lang/ja` 等语言包；我们项目业务词条已全链路 i18n，组件库内置文案同步是排期中的优化项。"——比背标准答案更像 5 年经验。

标准接法（面试要会写）：

```vue
<template>
  <el-config-provider :locale="epLocale">
    <router-view />
  </el-config-provider>
</template>
<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ja from 'element-plus/es/locale/lang/ja'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'

const { locale } = useI18n()
const epLocale = computed(() => ({ ja, 'zh-CN': zhCn, en }[locale.value] ?? ja))
</script>
```

### 6.1.6 坑

1. **全量 CSS 与按需组件混用**：如果切到按需引入却仍然 `import 'element-plus/dist/index.css'`，包体白省。按需时样式由插件按组件注入。
2. **图标全局循环注册**会把几百个图标组件都打进包（除非构建器能摇掉）；大型项目更推荐页面内局部 import（CP6 两种写法并存）。
3. **`v-loading` 按需引入漏指令**：`unplugin` 自动导入组件但不自动导入指令，手动 `import { vLoading }`（`<script setup>` 里局部注册指令的约定名就是 `vXxx`）。
4. **locale 不接就上多语言项目**：用户切到日语，业务文案变了但分页器还是"Total 100"——观感割裂。上多语言第一天就该接 `el-config-provider`。

### 6.1.7 面试问答

**Q：你们项目 Element Plus 是全量还是按需？为什么？**
A：入口全量注册 + 全量 CSS，因为是内部管理系统、部署在内网/固定用户群，首包大小不是第一矛盾，开发效率优先；但基建模板组件（如我们封装的 CpListPage）内部显式 import 所需组件，保证组件可脱离全局注册被单测挂载。如果要优化首包，我会引入 `unplugin-vue-components` 的 ElementPlusResolver 做自动按需。

**Q：ElementUI 项目迁移到 Element Plus，图标怎么处理？**
A：ElementUI 是字体图标（class 写法 `el-icon-edit`），Element Plus 换成了 `@element-plus/icons-vue` 的 SVG 组件，要么全局循环注册要么局部 import，模板从 `<i class="el-icon-edit">` 改为 `<el-icon><Edit /></el-icon>`，按钮的 `:icon` 属性从字符串改为组件引用。

---

<a id="62"></a>
## 6.2 管理系统页面解剖：StockQueryView.vue 全文精读（本章主线）

这是本章的心脏。目标：**你能对着面试官白板，把一个查询页从模板到网络请求完整讲一遍**。

### 6.2.1 先看全局：CP6 的"两层页面架构"

打开 `C:\CP6\cp6.web\src\views\wms\StockQueryView.vue`，你会发现它**没有直接写 `el-table` / `el-pagination` / 搜索表单**，而是用了三个自家模板组件：

```
CpPageShell（页面壳：标题 + 计数 + 右上角动作区）
  └── CpListPage（查询页模板：筛选条 + 表格卡 + 分页，130+ 查询页共用）
        ├── CpFilterBar（搜索表单区，内部是 el-form inline 形态的字段渲染）
        ├── el-table（数据表格）
        └── el-pagination（分页）
```

这是成熟管理系统的标志性做法：**一个系统里有 130+ 个长得几乎一样的查询页，把"筛选→查询→表格→分页→loading→空态→错误提示"这套固定剧本抽成一个模板组件，业务页只声明"列、筛选字段、取数函数"三样东西**。面试官问"你们怎么保证几十个列表页体验一致"，答案就是这个。

所以精读顺序：先读底层 `CpListPage`（理解 el-table/el-pagination 的原生用法都封在哪），再读业务页 `StockQueryView`。

### 6.2.2 底层精读：CpListPage.vue（`C:\CP6\cp6.web\src\components\templates\CpListPage.vue`）

#### （1）对外契约：类型定义

```ts
export interface ListColumn {
  prop: string
  label: string
  width?: number
  minWidth?: number
  align?: 'left' | 'right' | 'center'
  kind?: 'text' | 'num' | 'mono' | 'tag' | 'date'
  overflowTooltip?: boolean
  fixed?: 'left' | 'right'
  map?: (val: unknown, row: unknown) => { label: string; tone?: Tone }
  sortable?: 'custom' // 仅服务端排序；不接受 true（避免混入 el-table 客户端排序语义）
}
export type ListFetch = (q: {
  page: number
  size: number
  filters: Record<string, unknown>
  statusKey?: string
  sortField?: string
  sortOrder?: SortOrder
}) => Promise<{ rows: unknown[]; total: number }>
```

逐行解析：

- `ListColumn` 是**列的声明式描述**：业务页不写 `<el-table-column>` 标签，只给对象数组。`kind` 控制预置格式（`num` 右对齐数字、`mono` 单号等宽字体、`tag` 渲染成标签、`date` 截前 10 位）。
- `map`：**码值→文案/色调的声明式映射**。数据库存 `1/2/3`，表格显示"原料仓/半成品仓/成品仓"，就靠它。
- `sortable?: 'custom'`：**只允许服务端排序**。el-table 的 `sortable: true` 是客户端排序（只排当前页 20 条，业务上是错的），CP6 在类型层面直接禁掉了 `true`——**用 TS 类型把错误用法挡在编译期**，这是很好的面试素材。
- `ListFetch` 是**取数函数契约**：模板组件把"当前页码、每页条数、筛选值、排序"打包传给业务页提供的函数，业务页返回 `{ rows, total }`。**模板管交互状态，业务页管数据协议**——职责切割干净。

#### （2）核心取数逻辑：`load()` 与乱序守卫

```ts
const page = ref(1)
const size = ref(20)
const filters = ref<Record<string, unknown>>({})
const rows = ref<unknown[]>([])
const total = ref(0)
const loading = ref(false)

let seq = 0
async function load() {
  const id = ++seq
  loading.value = true
  try {
    const res = await props.fetch({
      page: props.paginated ? page.value : 1,
      size: props.paginated ? size.value : UNPAGED_SIZE,
      filters: filters.value,
      statusKey: statusKey.value,
      ...(sortField.value !== undefined
        ? { sortField: sortField.value, sortOrder: sortOrder.value }
        : {})
    })
    if (id !== seq) return // 已有更新的请求发出，丢弃本次结果
    rows.value = res.rows
    total.value = res.total
    emit('total-change', res.total)
  } catch (e) {
    if (id !== seq) return
    ElMessage.error((e as Error)?.message ?? String(e)) // 保留旧 rows/total
  } finally {
    if (id === seq) loading.value = false
  }
}
onMounted(() => { if (!props.lazy) load() })
```

逐行解析（这段是面试金矿）：

1. `let seq = 0; const id = ++seq`：**竞态（race condition）守卫**。用户快速点两次搜索，两个请求并发，如果第一个请求慢、后返回，会覆盖第二个请求的新结果——经典的"乱序响应"bug。解法：每次请求领一个自增序号，响应回来时 `if (id !== seq) return`，只有**最新一次**请求的结果才生效。面试问"接口竞态怎么处理"，这就是标准答案之一（另一个是 AbortController 取消旧请求）。
2. `loading.value = true` → 模板上 `v-loading="loading"`：整卡加载遮罩。
3. 失败分支：`ElMessage.error` 提示 + **保留旧数据**（rows/total 不清空）——失败时表格不闪空，用户体验决策写进了组件契约（源码头注称为"错误硬化契约"）。
4. `finally` 里也检查 `id === seq`：如果自己已经是过期请求，不能把新请求的 loading 关掉。
5. `onMounted(() => { if (!props.lazy) load() })`：默认进页面自动查询；`lazy=true` 是"先选条件再查"的 search-first 模式（ERP 常见：数据量大的页面不允许无条件全查）。

#### （3）交互事件：搜索/重置/翻页/排序全部收敛到 load()

```ts
function onSearch() { page.value = 1; load() }
function onReset() { page.value = 1; emit('reset'); load() }
function onStatus(key: string) { statusKey.value = key; page.value = 1; load() }
function onPageChange() { load() }
function onSizeChange() { page.value = 1; load() }

function onSortChange({ prop, order }: { prop: string; order: 'ascending' | 'descending' | null }) {
  sortField.value = order == null ? undefined : prop
  sortOrder.value = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : undefined
  page.value = 1
  emit('sort-change', { field: sortField.value, order: sortOrder.value })
  load()
}
```

划重点：

- **搜索/重置/切状态卡/改每页条数/排序 → 一律 `page.value = 1`**。只有纯翻页不重置页码。为什么？条件变了，结果集变了，停留在第 5 页可能是空页。这是列表页的铁律，面试常挖的细节。
- `onSortChange` 把 el-table 的 `ascending/descending/null` **规范化**为 `asc/desc/undefined` 再传给后端——组件库方言不外泄到 API 层。

#### （4）命令式刷新：defineExpose({ reload })

```ts
function reload() { return load() }
defineExpose({ reload }) // 仅暴露 reload，内部状态不外露
```

业务页在"编辑保存/删除成功"后调用 `listRef.value?.reload()`，**保留当前筛选和页码**原地刷新。对比另一种做法（给组件加 `:key` 强制重挂载）——reload 不丢用户的查询上下文，代价更小。`defineExpose` 是 `<script setup>` 下父组件通过 ref 调子组件方法的唯一通道（第 4 章讲过），这里是真实用例。

#### （5）模板：el-table + el-pagination 的原生用法都在这

```vue
<el-table
  v-loading="loading"
  :data="rows"
  :row-key="rowKey"
  :highlight-current-row="highlightCurrentRow"
  @selection-change="emit('selection-change', $event)"
  @sort-change="onSortChange"
>
  <el-table-column v-if="$slots.expand" type="expand">
    <template #default="{ row }"><slot name="expand" :row="row" /></template>
  </el-table-column>

  <el-table-column v-if="selectable" type="selection" width="44" />

  <el-table-column
    v-for="c in columns"
    :key="c.prop"
    :prop="c.prop"
    :label="c.label"
    :width="c.width"
    :min-width="c.minWidth"
    :show-overflow-tooltip="c.overflowTooltip"
    :fixed="c.fixed"
    :sortable="c.sortable ?? false"
    :align="colAlign(c)"
  >
    <template #default="{ row }">
      <slot :name="`col-${c.prop}`" :row="row">
        <CpTag v-if="c.kind === 'tag' && c.map" :tone="mapTone(c, row)">{{ display(c, row) }}</CpTag>
        <span v-else-if="c.kind === 'mono'" class="cp-mono">{{ display(c, row) }}</span>
        <span v-else-if="c.kind === 'num'" class="num">{{ display(c, row) }}</span>
        <template v-else>{{ display(c, row) }}</template>
      </slot>
    </template>
  </el-table-column>

  <template #empty>
    <CpEmpty v-if="!loading" :text="emptyText" />
  </template>
</el-table>

<div v-if="paginated" class="pager">
  <el-pagination
    v-model:current-page="page"
    v-model:page-size="size"
    :total="total"
    :page-sizes="[20, 50, 100]"
    layout="total, sizes, prev, pager, next"
    @current-change="onPageChange"
    @size-change="onSizeChange"
  />
</div>
```

逐行解析：

1. `v-loading="loading"`：Element Plus 的加载指令，在目标元素上盖半透明遮罩 + 转圈。加在 el-table 上=表格级 loading。
2. `:data="rows"`：el-table 的数据源，数组，一行一对象。
3. `type="expand"`：展开行列（点击加号展开详情），通过作用域插槽把 `row` 转发给业务页的 `#expand` 插槽——**插槽转发**模式。
4. `type="selection"`：多选列（复选框），勾选变化触发 `@selection-change`，参数是选中行数组。
5. `v-for` 渲染 `<el-table-column>`：**动态列**。每列内部又开了一个 `#default="{ row }"` 作用域插槽，先尝试渲染业务页传入的 `col-<prop>` 具名插槽（**插槽兜底模式**：`<slot name="col-xxx" :row="row">` 的标签体就是默认内容），业务页没提供该列插槽时按 `kind` 走预置渲染。**一套模板同时支持"声明式列"和"完全自定义列"**。
6. `#empty` 插槽：无数据时渲染自家 `CpEmpty` 空态组件（且 loading 中不显示空态，避免"加载中闪一下暂无数据"）。
7. `el-pagination`：
   - `v-model:current-page="page"` / `v-model:page-size="size"`：**双 v-model**（Vue 3 特性），页码和每页条数双向绑定。
   - `layout="total, sizes, prev, pager, next"`：分页器由这些"积木"按序拼出——总数、每页条数选择器、上一页、页码、下一页。
   - `@current-change` / `@size-change`：翻页/改条数的事件。注意 **size-change 里要重置 page=1**（第 3 点交互代码），否则"第 5 页每页 20 条"切成"每页 100 条"可能落在不存在的页。

### 6.2.3 业务页精读：StockQueryView.vue（`C:\CP6\cp6.web\src\views\wms\StockQueryView.vue`，全文 248 行）

现在读主标本。这是 WMS 库存查询页（在庫照会），后端对应 Day 1 精读的 `StockController.Search`（`GET /api/wms/stock`）。

#### （1）文件头注释——生产代码的自文档习惯

```vue
<!--
  在庫照会 —— CpPageShell + CpListPage 迁移（WMS 批次5，服务端分页）。
  数量列 map（formatQty）；有効在庫 col slot（负数红字）；期限/所有者/リコール/QC 走 col slot 保留条件 tag/占位。
  hasStockOnly 复选（CpFilterBar 无 boolean 字段类型，缺口 #15）→ CpListPage toolbar slot，fetch 闭包读取 + 切换后 reload()。
  QC 設定弹窗(radio+textarea, 自定义 res.code 处理) / 履歴弹窗(只读 descriptions+表) → 保留原 el-dialog（逃生舱）。
  分页服务端：fetch 透传 page/size；QC 保存后 listRef.reload()。
-->
```

注意两个词：**"缺口 #15"**（模板组件不支持布尔筛选字段，业务页用 toolbar 插槽绕过并记录了缺口编号）和**"逃生舱"**（复杂弹窗不硬塞进模板，保留原生 el-dialog）。**成熟框架的标志不是覆盖一切，而是给复杂场景留出口并记账**——面试聊组件封装时的高级观点。

#### （2）模板骨架

```vue
<CpPageShell :title="t('wms.stock.title')" :count="total">
  <CpListPage
    ref="listRef"
    :columns="columns"
    :fetch="fetchList"
    :search-fields="searchFields"
    :filter-labels="filterLabels"
    @total-change="total = $event"
  >
    <template #toolbar>
      <el-checkbox v-model="hasStockOnly" @change="reloadList">{{ t('wms.stock.fld.hasStockOnly') }}</el-checkbox>
    </template>
```

- `:title="t('wms.stock.title')"`：页面标题走 i18n（6.8 节详解）。
- `:count="total"` + `@total-change="total = $event"`：模板每次查询成功把 total 发上来，页面壳在标题旁显示"共 n 条"。
- `#toolbar` 插槽：放"仅有库存"复选框。它不在 CpFilterBar 的 filters 里（模板不支持布尔字段），而是页面自己的 `ref`，**fetch 闭包直接读它**，切换时 `reloadList()` 触发重查——用闭包捕获外部筛选状态，绕过模板契约的实用技巧。

#### （3）列插槽：四个真实的自定义列

```vue
<template #col-availableQty="{ row }">
  <span :class="{ neg: row.availableQty < 0 }">{{ formatQty(row.availableQty) }}</span>
</template>

<template #col-expiryDate="{ row }">{{ row.expiryDate?.slice(0, 10) || '—' }}</template>

<template #col-owner="{ row }">
  <el-tag v-if="row.ownerType === 'CUSTOMER'" type="warning" size="small">{{ t('wms.stock.flag.vmi') }}</el-tag>
  <span v-else>—</span>
</template>

<template #col-qc="{ row }">
  <el-tag :type="qcTagOf(row.qcStatus)" size="small">{{ t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) }}</el-tag>
</template>

<template #col-_action="{ row }">
  <el-button link type="primary" size="small" @click="openHistory(row)">{{ t('wms.common.history') }}</el-button>
  <el-button v-permission="'wms-stock-qc:set'" link type="warning" size="small" @click="openQcDialog(row)">{{ t('wms.stock.qc.btn') }}</el-button>
</template>
```

逐行解析：

1. `#col-availableQty="{ row }"`：**作用域插槽解构**。插槽名 `col-可用库存列`，`{ row }` 解构出当前行对象。**有効在庫为负数时红字**（`:class="{ neg: ... }"` + scoped 样式 `.neg { color: var(--cp-danger); font-weight: 600; }`）——制造业库存页的经典需求：负库存必须一眼看见。
2. `row.expiryDate?.slice(0, 10) || '—'`：可选链 + 截取 `yyyy-MM-dd` + 空值占位符"—"。后端返回 ISO 字符串 `2026-07-15T00:00:00`，前端只要日期部分。
3. `#col-owner`：**条件标签**。VMI（供应商管理库存）货主是客户时显示橙色警示 tag，否则占位。
4. `#col-qc`：**状态→标签色映射**（详见 6.3），并且 tag 文案也走 i18n：`` t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) `` ——**动态拼 i18n key**，四种 QC 状态对应四个词条。
5. `#col-_action`：操作列。约定 `_action` 前缀表示非数据列。两个 `link` 型小按钮（无边框文字按钮，表格操作列标配）。**第二个按钮带 `v-permission="'wms-stock-qc:set'"`**——没有"设置 QC 状态"权限的用户根本看不到这个按钮（6.7 节精读）。

#### （4）列定义与筛选字段：computed 包住 i18n

```ts
const columns = computed<ListColumn[]>(() => [
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 120 },
  { prop: 'physicalQty', label: t('wms.stock.col.physical'), width: 120, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'allocatedQty', label: t('wms.stock.col.allocated'), width: 120, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'availableQty', label: t('wms.stock.col.available'), width: 120, align: 'right' },
  // ...
  { prop: '_action', label: t('wms.common.action'), width: 180, fixed: 'right' },
])

const searchFields = computed<FilterField[]>(() => [
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  { key: 'locationCd', label: t('wms.common.location'), type: 'text' },
  { key: 'productCd', label: t('wms.common.product'), type: 'text' },
  { key: 'lotNo', label: t('wms.common.lot'), type: 'text' },
  {
    key: 'ownerType', label: t('wms.stock.fld.owner'), type: 'select',
    options: [
      { label: t('wms.stock.fld.ownerSelf'), value: 'SELF' },
      { label: t('wms.stock.fld.ownerCustomer'), value: 'CUSTOMER' },
    ],
  },
])
```

**为什么 columns 要用 `computed` 而不是普通常量数组？** 因为 `label: t('...')`——如果写成普通常量，模块求值时 t() 只执行一次，**用户切换语言后表头不会变**。包进 computed，t() 依赖的 locale 是响应式的，语言一切、computed 重算、表头跟着变。这是多语言管理系统的必背模式（`filterLabels`、`warehouseTypeMap` 同理）。

- `physicalQty/allocatedQty` 用 `map` 做数字格式化（千分位、最多 4 位小数，见 6.8 的 formatQty）；`availableQty` 却不用 map 而用列插槽——因为它除了格式化还要**加样式**（负数红字），map 只能改文案不能改 DOM。**两种自定义方式的分工线：只改文案用 map，要改结构/样式用插槽。**
- `fixed: 'right'`：操作列固定在右侧，横向滚动时不跟着跑。
- 搜索区五个字段就是后端 `StockController.Search` 的查询参数——**搜索表单字段 = API 查询参数 = 后端 LINQ Where 条件**，全栈闭环第一环。

#### （5）取数函数：前后端协议的翻译层

```ts
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: Record<string, unknown> = { page, pageSize: size, hasStockOnly: hasStockOnly.value }
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.locationCd) q.locationCd = String(f.locationCd)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.lotNo) q.lotNo = String(f.lotNo)
  if (f.ownerType) q.ownerType = String(f.ownerType)
  const res = await stockApi.search(q as never)
  return { rows: res.data.items, total: res.data.total }
}
```

逐行解析：

1. 入参 `{ page, size, filters }` 来自 CpListPage 的 load()；出参 `{ rows, total }` 回给模板渲染。
2. `pageSize: size`：**命名翻译**——组件内部叫 size，后端 DTO 叫 pageSize。翻译层的价值就在这：组件契约和 API 契约各自稳定，中间一个函数适配。
3. `hasStockOnly: hasStockOnly.value`：闭包读取页面级 ref（toolbar 复选框），前文说的"模板外筛选状态"从这里汇入请求。
4. `if (f.warehouseCd)`：**空值不传**。空字符串不进 query，后端就不会拼这个 Where 条件。
5. `stockApi.search(q)`：API 层（`C:\CP6\cp6.web\src\api\wms\stock.ts`）：

```ts
export const stockApi = {
  /** 在庫照会 */
  search(query: StockSearchQuery = {}) {
    return http.get<any, WmsApi<WmsPaged<Stock>>>('/wms/stock', { params: query })
  },
  history(stockId: string, days = 90) {
    return http.get<any, WmsApi<{ stock: Stock; transactions: StockTransaction[] }>>(
      `/wms/stock/${stockId}/history`, { params: { days } })
  },
  setQcStatus(stockId: string, newStatus: string, reason?: string) {
    return http.post<any, WmsApi<{ stockId: string; qcStatus: string }>>(
      `/wms/stock-qc/${stockId}/set`, { newStatus, reason })
  },
  // ...
}
```

API 层是**纯函数集合**：一个后端端点一个方法，泛型标注返回类型（`WmsApi<WmsPaged<Stock>>` = `{ code, message, data: { items, total } }`），页面永远不直接写 URL。`http` 是带拦截器的 axios 实例（`src/api/http.ts`：自动带 httpOnly Cookie、非 GET 注入 CSRF 头、401 自动 refresh 重放、业务错误码走 i18n 翻译后 toast——Day 2 第 5 章已讲，这里是消费端）。

#### （6）整页数据流总图（面试口述版）

```
用户输入筛选条件（CpFilterBar 内部 v-model 收集到 filters）
  → 点"検索" → CpListPage.onSearch(): page=1 + load()
    → load(): seq+1、loading=true
      → fetchList({page, size, filters}) —— 业务页翻译参数
        → stockApi.search(q) —— axios GET /api/wms/stock?productCd=...&page=1&pageSize=20
          → [Vite 代理/网关] → 后端 StockController.Search
            → EF Core: Where(条件).OrderBy(...).Skip((page-1)*size).Take(size) + Count
          ← { code:0, data:{ items:[...20 条], total: 1372 } }
        ← 响应拦截器解包 response.data
      ← { rows, total }
    → seq 校验通过 → rows/total 写入 → el-table 因 :data 响应式自动重渲染
    → emit('total-change') → 页头显示"共 1372 条"
    → loading=false，v-loading 遮罩消失
```

**每个箭头你都要能展开讲**。这就是"完整讲出一个 CRUD 页面每一层"的含义。

#### （7）页内两个弹窗（预览，6.5 详讲）

- QC 设置弹窗：`el-dialog` + `el-radio-group`（四种 QC 状态）+ textarea 理由（`maxlength="200" show-word-limit`），保存成功后 `ElMessage.success` + 关弹窗 + `reloadList()`。
- 履历弹窗：`el-descriptions`（库存四要素只读展示）+ 内嵌 `el-table`（`max-height="500"` 滚动）展示一年内的出入库流水——**点行→调 history 接口→弹窗展示**是详情查看的轻量形态（不跳页）。

### 6.2.4 面试白板版：不依赖内部模板，原生 Element Plus 手写同一个页面

面试官没见过 CpListPage，可能直接说"你用原生 Element Plus 写个列表页看看"。下面是把 6.2.2/6.2.3 的知识压缩成的**白板版最小完整实现**（所有模式——竞态守卫、页码重置、失败保留旧数据——都来自 CP6 真实代码，只是去掉了封装层）：

```vue
<template>
  <!-- ① 搜索表单区：el-form inline -->
  <el-form inline :model="query" @submit.prevent="onSearch">
    <el-form-item :label="t('wms.common.product')">
      <el-input v-model="query.productCd" clearable />
    </el-form-item>
    <el-form-item :label="t('wms.common.warehouse')">
      <el-input v-model="query.warehouseCd" clearable />
    </el-form-item>
    <el-form-item>
      <el-button type="primary" native-type="submit">{{ t('wms.common.search') }}</el-button>
      <el-button @click="onReset">{{ t('wms.common.clear') }}</el-button>
    </el-form-item>
  </el-form>

  <!-- ② 数据表格：v-loading + 插槽列 -->
  <el-table v-loading="loading" :data="rows" border stripe>
    <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
    <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
    <el-table-column prop="availableQty" :label="t('wms.stock.col.available')" align="right" width="120">
      <template #default="{ row }">
        <span :class="{ neg: row.availableQty < 0 }">{{ formatQty(row.availableQty) }}</span>
      </template>
    </el-table-column>
    <el-table-column :label="t('wms.common.action')" width="120" fixed="right">
      <template #default="{ row }">
        <el-button link type="primary" size="small" @click="openHistory(row)">{{ t('wms.common.history') }}</el-button>
      </template>
    </el-table-column>
  </el-table>

  <!-- ③ 分页 -->
  <el-pagination
    v-model:current-page="page"
    v-model:page-size="size"
    :total="total"
    :page-sizes="[20, 50, 100]"
    layout="total, sizes, prev, pager, next"
    @current-change="load"
    @size-change="onSizeChange"
  />
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { stockApi } from '@/api/wms/stock'
import { formatQty } from '@/utils/format'

const { t } = useI18n()
const query = reactive({ productCd: '', warehouseCd: '' })
const rows = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(20)
const loading = ref(false)

let seq = 0                                  // 竞态守卫（同 CpListPage）
async function load() {
  const id = ++seq
  loading.value = true
  try {
    const res = await stockApi.search({
      page: page.value, pageSize: size.value,
      ...(query.productCd ? { productCd: query.productCd } : {}),
      ...(query.warehouseCd ? { warehouseCd: query.warehouseCd } : {}),
    } as never)
    if (id !== seq) return
    rows.value = res.data.items
    total.value = res.data.total
  } catch (e: any) {
    if (id !== seq) return
    ElMessage.error(e?.message ?? 'Load failed') // 失败保留旧数据
  } finally {
    if (id === seq) loading.value = false
  }
}
function onSearch() { page.value = 1; load() }          // 条件变 → 回第 1 页
function onReset() { query.productCd = ''; query.warehouseCd = ''; page.value = 1; load() }
function onSizeChange() { page.value = 1; load() }
load()
</script>
```

三点讲解词（写完主动说，体现深度）：

1. "el-form 加 `inline` 就是搜索区横排形态；`@submit.prevent` + `native-type="submit"` 让输入框里按回车也能搜——细节体验。"
2. "load 里的自增 seq 是竞态守卫，快慢请求乱序返回时只认最新一次；失败分支保留旧数据只 toast，表格不闪空。"
3. "所有会改变结果集的操作（搜索/重置/改每页条数）都重置 page=1，只有纯翻页不重置。真实项目里我们把这整套剧本抽成了模板组件，业务页只声明列和 fetch 函数。"

### 6.2.5 坑（本节合集）

1. **columns 忘记包 computed** → 切语言表头不更新（最常见的多语言回归 bug）。
2. **改条件/条数不重置页码** → 用户看到"第 5 页空白"。凡是会改变结果集的操作一律回第 1 页。
3. **没有竞态守卫** → 慢请求覆盖快请求的结果，表格显示旧条件的数据。seq 或 AbortController 二选一。
4. **失败时清空表格** → 网络抖一下用户数据全消失。CP6 的契约是"失败保留旧数据 + toast"。
5. **模板组件包打天下** → 复杂弹窗硬塞声明式配置，配置项爆炸。CP6 的解法：模板覆盖 80% 常规页 + 原生 el-dialog 做逃生舱 + 缺口记编号。

### 6.2.6 面试问答

**Q：讲一下你们一个典型列表页的结构？**
A（背熟）：我们把 130 多个查询页收敛到一个 CpListPage 模板组件：业务页只声明三样——列定义数组（computed 包 i18n 的 label + kind 预置格式 + map 码值映射）、筛选字段数组、一个 fetch 函数。模板内部统一管：分页状态、loading、空态、错误 toast、竞态守卫（请求序号，过期响应丢弃）、"改条件重置页码"这类交互铁律；自定义列走 `col-<prop>` 作用域插槽兜底。业务页通过 `defineExpose` 的 reload() 在增删改后原地刷新，保留筛选和页码。

**Q：服务端分页的参数和响应长什么样？**
A：请求 `page/pageSize + 各筛选字段`（空值不传）；响应 `{ code, message, data: { items, total } }`。total 用于分页器和页头计数；后端 `Skip/Take` 出当页，`Count` 出总数。排序也是服务端的：el-table 列配 `sortable="custom"`，`@sort-change` 拿到 `prop/order` 规范化成 `sortField/sortOrder` 传给后端。

---

<a id="63"></a>
## 6.3 el-table 深用

### 6.3.1 列插槽 `#default="{ row }"`

el-table 每列的单元格内容由该列的 `default` 作用域插槽决定，插槽作用域是 `{ row, column, $index }`。cp6.web 真实用例（StockQueryView 履历弹窗内嵌表）：

```vue
<el-table :data="historyTxns" border stripe size="small" max-height="500">
  <el-table-column prop="txnDateTime" :label="t('wms.stock.col.txnDateTime')" width="170">
    <template #default="{ row }">{{ row.txnDateTime?.replace('T', ' ').slice(0, 19) }}</template>
  </el-table-column>
  <el-table-column prop="txnType" :label="t('wms.stock.col.txnType')" width="80">
    <template #default="{ row }"><el-tag size="small" :type="txnTagOf(row.txnType)">{{ row.txnType }}</el-tag></template>
  </el-table-column>
  <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
    <template #default="{ row }">{{ formatQty(row.qty) }}</template>
  </el-table-column>
  <el-table-column prop="relatedNo" :label="t('wms.stock.col.relatedNo')" width="180" />
  <el-table-column prop="remark" :label="t('wms.common.remarks')" show-overflow-tooltip />
</el-table>
```

要点：

- 有插槽时 `prop` 只剩语义作用（排序/筛选定位用），显示内容完全由插槽决定；无插槽时直接渲染 `row[prop]`。
- `border stripe size="small"`：边框 + 斑马纹 + 紧凑尺寸，数据密集型弹窗表的标配。
- `max-height="500"`：超高滚动，**表头自动固定**——弹窗里放表格必配。
- `show-overflow-tooltip`：文本超宽省略号 + 悬停气泡完整内容，备注列标配。

### 6.3.2 状态标签映射（el-tag）

管理系统最高频的渲染需求：**枚举值→彩色标签**。cp6.web 的两个真实映射函数（StockQueryView）：

```ts
function txnTagOf(v: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[v as 'IN'] || 'info'
}
function qcTagOf(s?: string): 'success' | 'danger' | 'warning' | 'info' {
  switch (s) {
    case 'PASSED': return 'success'
    case 'FAILED': return 'danger'
    case 'HOLD': return 'warning'
    case 'PENDING':
    default: return 'info'
  }
}
```

- 入库绿/出库红/预留黄——**颜色语义全系统一致**（这是设计系统的职责）。
- 返回值类型用字面量联合类型精确标注 el-tag 的 `type` 合法值——TS 在这种地方的价值。
- 更进一步是 CpListPage 的声明式版本：`kind: 'tag', map: (v) => ({ label: ..., tone: ... })`（WarehouseListView 的仓库种别列就是），把"取文案"和"取色调"合并成一个纯函数。源码头注特别强调 **map 须为纯函数**（label 和 tone 会分别调用取值，带副作用会导致两者不一致）——纯函数意识的真实案例。

### 6.3.3 排序与筛选

- **客户端排序**：`sortable`（true）——只排当前页数据。服务端分页下**语义是错的**（用户以为在排全量），所以 CP6 的 ListColumn 类型只接受 `sortable: 'custom'`。
- **服务端排序**：`sortable="custom"` + `@sort-change="{ prop, order }"`，order 是 `ascending/descending/null`（第三次点击取消排序为 null），规范化后并入查询参数、重置 page=1 重查（6.2.2 代码）。
- **列筛选**：`:filters="[{text, value}]" + filter-method`（客户端）或 `@filter-change`（自己发请求）。CP6 的选择是**不用表头筛选，筛选统一放顶部 CpFilterBar**——大表格里表头漏斗可发现性差，且服务端分页下同样有"只筛当前页"的语义陷阱。

### 6.3.4 固定列与多选

- `fixed: 'right'`：StockQueryView 操作列 `{ prop: '_action', ..., fixed: 'right' }`——列多横向滚动时操作按钮永远可见。左侧固定常用于单号列。坑：固定列是**克隆出的独立表格层**，行高不一致时会错位（自定义单元格里放了不同高度的内容要小心）。
- 多选：`<el-table-column type="selection" width="44" />` + `@selection-change`。CP6 真实用例是 InboxPending 的批量审批（6.10 还会讲它的跨断点选中态回填）：勾选行 → `selected` 数组 → 批量条显示"已选 n 条" + 批准/退回按钮。配合 `row-key` 可以做翻页保持勾选（`reserve-selection`）。
- 命令式勾选：`tableRef.value.toggleRowSelection(row, true)`（InboxPending 真实调用，用于断点切换后回填选中态）。

### 6.3.5 合计行

`show-summary` 开启表尾合计（默认对数字列求和），`:summary-method` 自定义（比如金额列求和、数量列不合计、文案"合计"放第一列）。制造业场景：入出库明细的数量/金额合计。注意：**服务端分页下合计行只合计当前页**——真要"全量合计"应该由后端在响应里多返回一个 aggregates 字段，前端 summary-method 直接展示它，这是面试可讲的正确姿势。

### 6.3.6 大数据量与虚拟滚动

- el-table 是**全量真实 DOM** 渲染：1000 行 × 15 列 = 15000+ 单元格组件，滚动明显卡。
- 阈值经验：一屏渲染 ≤ 200 行没问题；上千行考虑分页（首选）或虚拟滚动。
- **el-table-v2**：Element Plus 提供的虚拟化表格，只渲染可视窗口内的行，滚动时复用 DOM。代价：不支持普通 el-table 的部分特性（自动行高、部分插槽形态），列宽必须显式。
- **CP6 的现实答案**：全部查询页走**服务端分页**（每页 20/50/100），从架构上让前端永远只碰一页数据——这是管理系统的第一性解法，虚拟滚动是"确实需要一次看几千行"（如日志流水）时的补充。

### 6.3.7 坑

1. 服务端分页 + 客户端排序/筛选/合计 = 三个同构陷阱（都只作用于当前页）。**凡是数据不全在前端，一切数据运算都应发回服务端。**
2. `:data` 换新数组引用才触发整表刷新；行内字段变化（如 StockQueryView 保存 QC 后 `qcTarget.value.qcStatus = res.data.qcStatus`）依赖行对象本身是响应式的（数组来自 ref，深层响应式，直接改行字段即可生效）。
3. 固定列错位：内容高度不一致/图片异步加载后要 `doLayout()`。
4. `selection-change` 是**单向覆盖**：表格重挂载（v-if 切换）后内部选中态清零但你的数组还留着，需要手动回填（InboxPending 的 watch + toggleRowSelection 就是修这个真实 bug 的，注释写得非常清楚）。

### 6.3.8 面试问答

**Q：el-table 一万行卡顿怎么办？**
A：先问业务要不要一万行。①首选服务端分页+服务端筛选排序，前端每次只渲染一页（我们全系统 130+ 列表页统一如此）；②真要长列表滚动浏览，用 el-table-v2 虚拟滚动，只渲染视口行；③辅助手段：列上 `show-overflow-tooltip` 代替换行、减少列插槽里的组件层级、避免每个单元格挂 watcher。合计/排序在数据不全在前端时一律交给后端。

---

<a id="64"></a>
## 6.4 el-form 与校验

### 6.4.1 组件/模式

el-form 的四件套：`:model`（表单数据对象）、`:rules`（校验规则）、`el-form-item` 的 `prop`（把规则挂到字段）、`ref` 拿 `FormInstance` 调 `validate()/resetFields()/clearValidate()`。

### 6.4.2 cp6.web 真实代码一：编辑弹窗表单（`C:\CP6\cp6.web\src\views\wms\WarehouseListView.vue`）

仓库主数据页——一个完整的"列表 + 新建/编辑弹窗 + 删除确认"CRUD 页：

```vue
<CpFormDialog
  v-model="dialogVisible"
  :title="dialogTitle"
  width="560"
  :form="form"
  :rules="rules"
  :submit="onSave"
  :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
  @saved="reloadList"
>
  <el-form-item :label="t('wms.warehouse.fld.cd')" prop="warehouseCd">
    <el-input v-model="form.warehouseCd" :disabled="!!form.id" maxlength="10" />
  </el-form-item>
  <el-form-item :label="t('wms.warehouse.fld.name')" prop="warehouseName">
    <el-input v-model="form.warehouseName" maxlength="100" />
  </el-form-item>
  <el-form-item :label="t('wms.warehouse.fld.type')">
    <el-select v-model="form.warehouseType">
      <el-option v-for="(label, val) in warehouseTypeMap" :key="val" :label="label" :value="Number(val)" />
    </el-select>
  </el-form-item>
  <el-form-item :label="t('wms.warehouse.fld.allowNegative')"><el-switch v-model="form.allowNegative" /></el-form-item>
  <el-form-item :label="t('wms.common.remarks')"><el-input v-model="form.remarks" type="textarea" :rows="2" /></el-form-item>
</CpFormDialog>
```

```ts
const form = reactive<Warehouse>({ warehouseCd: '', warehouseName: '', warehouseType: 1, allowNegative: false })
const rules = computed<FormRules>(() => ({
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  warehouseName: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
```

逐行解析：

1. `form` 用 `reactive`（对象整体响应式），字段用 `v-model="form.xxx"` 双绑。
2. `rules` 又是 **computed 包 i18n**——校验错误文案也要随语言切换。
3. `FormRules` 类型来自 `import { type FormRules } from 'element-plus'`，key 对应 `el-form-item` 的 `prop`。
4. `{ required: true, message: ..., trigger: 'blur' }`：必填规则，失焦触发。trigger 经验法则：**输入框 blur、下拉/日期 change**（CpFormDialog 的自动规则生成正是这么分的：`f.type === 'select' || f.type === 'date' ? 'change' : 'blur'`）。
5. `:disabled="!!form.id"`：**编辑模式下主键字段禁改**（仓库 CD 是业务主键，编辑时锁死）——one-dialog-two-modes 的细节。

### 6.4.3 cp6.web 真实代码二：validate() 在哪里被调（`C:\CP6\cp6.web\src\components\templates\CpFormDialog.vue`）

业务页看不到 validate()，因为它被封装进了表单弹窗模板：

```ts
const formRef = ref<FormInstance>()
const submitting = ref(false)

async function onConfirm() {
  if (submitting.value) return // 防双提交：校验/提交在途时二次点击直接忽略
  submitting.value = true      // 提前置位——覆盖 validate 在途窗口，杜绝并发进入
  try {
    const valid = await formRef.value?.validate().catch(() => false)
    if (!valid) return // 校验失败：不提交、不关闭、el-form 已内联提示
    await props.submit(props.form)
    emit('saved')
    emit('update:modelValue', false)
  } catch (err) {
    ElMessage.error((err as Error)?.message ?? String(err))
  } finally {
    submitting.value = false
  }
}
```

逐行解析（这 15 行浓缩了表单提交的全部工程细节）：

1. `formRef = ref<FormInstance>()` + 模板 `<el-form ref="formRef" :model="form" :rules="mergedRules">`：拿表单实例。
2. `validate()` 返回 Promise：**通过 resolve(true)，不通过 reject**。这里 `.catch(() => false)` 把 reject 驯化成布尔值，避免校验失败走到外层 catch 弹全局错误（校验失败 el-form 已经在字段下方内联标红提示了，再 toast 就重复打扰）。
3. **防双提交双保险**：`submitting` 在 validate 之前就置 true（不是提交前）——否则"校验是异步的"这个窗口期里连点两下会并发进入；同时确认按钮 `:loading="submitting"` 给视觉反馈。
4. 提交失败（后端拒绝）：toast 错误 + **弹窗保持打开**，用户改完可以直接重交，不丢已填内容。
5. 成功：`emit('saved')`（业务页借此 `reloadList()`）+ 关弹窗。
6. 补充：`mergedRules` computed 会为声明式 `fields` 里 `required: true` 的字段**自动生成必填规则**，业务显式 rules 同 key 覆盖（explicit wins）——规则也能默认化。

### 6.4.4 自定义 validator 与跨字段校验（真实三例）

**数字必须大于 0**（`C:\CP6\cp6.web\src\views\wms\VmiView.vue:217`）：

```ts
const calcRules = computed<FormRules>(() => ({
  yearMonth: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  dailyStorageRate: [{ required: true, trigger: 'change',
    validator: (_r, v, cb) => (typeof v === 'number' && v > 0 ? cb() : cb(new Error(t('wms.common.required')))) }],
}))
```

**规则可叠加**（`C:\CP6\cp6.web\src\views\wms\PaperRollView.vue:223`）——同一字段数组里放多条规则，按序校验：

```ts
consumeLen: [{ required: true, message: t('wms.common.required'), trigger: 'change' },
  { validator: (_r, v, cb) => (Number(v) > 0 ? cb() : cb(new Error(t('wms.common.required')))), trigger: 'change' }],
```

**跨字段校验：确认密码**（`C:\CP6\cp6.web\src\views\pms\ChangePasswordView.vue`）：

```ts
const validateConfirm = (_rule: any, value: string, callback: (err?: Error) => void) => {
  if (value !== form.value.newPassword) {
    callback(new Error(t('sec.changePwd.mismatch')))
  } else {
    callback()
  }
}
const rules = computed<FormRules>(() => ({
  currentPassword: [{ required: true, message: t('sec.changePwd.required'), trigger: 'blur' }],
  newPassword: [{ required: true, message: t('sec.changePwd.required'), trigger: 'blur' }],
  confirmPassword: [{ validator: validateConfirm, trigger: 'blur' }]
}))

async function handleSubmit() {
  if (!formRef.value) return
  await formRef.value.validate()
  // ...调 API
}
```

validator 签名 `(rule, value, callback)`：**闭包捕获 form 就能访问其它字段**（`value !== form.value.newPassword`），这是跨字段校验的通用手法。`callback()` 无参=通过，`callback(new Error(msg))`=失败并显示 msg。异步校验（比如"编码是否已存在"查后端）同样用这个 callback 形态：validator 里 await API 再回调，或返回 Promise。

这个页面还展示了 validate() 的另一种用法：**不 catch 直接 await**——校验失败 reject 会中断 handleSubmit（往下的 API 调用不执行），错误由内联提示承担。两种风格（驯化成布尔 vs 让它中断）都要认识。

### 6.4.5 resetFields() 与"重开弹窗残留"问题

`resetFields()` 把字段重置到**表单挂载时的初始值**并清校验态。注意 CP6 的 CpFormDialog 选择了另一条路：**父级持有 form 对象并负责重置**（头注原话）——WarehouseListView 的做法：

```ts
function openCreate() {
  Object.assign(form, {
    id: undefined, warehouseCd: '', warehouseName: '', warehouseType: 1, baseCd: '',
    managerCd: '', addressText: '', allowNegative: false, remarks: '',
  })
  dialogVisible.value = true
}
function openEdit(row: Warehouse) {
  Object.assign(form, { baseCd: '', managerCd: '', addressText: '', remarks: '', ...row })
  dialogVisible.value = true
}
```

- 每次打开前 `Object.assign(form, 完整字段集)` **显式覆盖全部字段**。openEdit 先铺默认空值再展开 `...row`——防止 row 里缺某字段时残留上一次编辑的值（**弹窗复用最经典的 bug**：编辑 A 再新建，表单里还挂着 A 的备注）。
- 为什么不 `form = row`？①reactive 对象不能整体重赋值（丢响应式连接）；②直接引用 row 会把表格行对象和表单绑到同一引用，**输入框每敲一个字表格跟着变**，取消编辑也回不去——必须拷贝。

### 6.4.6 坑

1. `el-form-item` 忘写 `prop` → 规则永远不触发（rules 的 key 靠 prop 定位）。必填却不标 prop 是新手第一坑。
2. reactive 表单整体重赋值丢响应式；用 `Object.assign` 或改用 `ref({})` 整体换 `.value`。
3. `resetFields()` 的"初始值"是**挂载瞬间**的值——如果弹窗打开后才填入编辑数据，reset 会回到编辑数据而非空表单。CP6 干脆不依赖它，openCreate/openEdit 显式赋全量。
4. validate 是异步的：`if (submitting) return` 要在 validate 之前置位，否则连点仍会双提交。
5. 校验文案硬编码中文 → 多语言穿帮。rules 必须 computed + t()。

### 6.4.7 面试问答

**Q：el-form 怎么做"确认密码一致"？**
A：自定义 validator，闭包访问表单对象里的 newPassword 对比，`callback(new Error(...))` 报错。规则挂在 confirmPassword 上、trigger blur。进阶：newPassword 变化时应 `formRef.validateField('confirmPassword')` 重校一次，防止先填确认再改新密码绕过。

**Q：新增/编辑共用一个弹窗，打开时怎么保证表单干净？**
A：父组件持有 reactive form，openCreate/openEdit 都用 Object.assign 显式覆盖**全部**字段（编辑时先铺默认值再展开行数据，防缺字段残留）；行数据必须拷贝进表单而不是引用，否则输入会直接改表格行。另外打开时 clearValidate 清掉上次的校验红字。

---

<a id="65"></a>
## 6.5 弹窗与反馈

### 6.5.1 el-dialog：一个弹窗复用"新增/编辑"两种模式

模式识别的枢纽是 `form.id`（WarehouseListView 真实代码）：

```ts
const dialogTitle = computed(() => (form.id ? t('wms.warehouse.dlg.edit') : t('wms.warehouse.dlg.create')))

async function onSave() {
  if (form.id) {
    await warehouseApi.update(form.warehouseCd, form)
  } else {
    await warehouseApi.create(form)
  }
  ElMessage.success(t('wms.common.success'))
}
```

四件事全由"有没有 id"决定：**标题**（新建/编辑）、**主键字段是否禁用**（`:disabled="!!form.id"`）、**提交走 create 还是 update**、（本例没有但常见的）编辑时是否先拉详情回填。这是管理系统最标准的 one-dialog-two-modes 写法，比维护两个弹窗组件省一半代码。

el-dialog 本体要点（StockQueryView 的两个原生 dialog）：

```vue
<el-dialog v-model="qcDialogVisible" :title="t('wms.stock.qc.dlgTitle')" width="520">
  <!-- 内容 -->
  <template #footer>
    <el-button @click="qcDialogVisible = false" :disabled="qcSaving">{{ t('wms.common.cancel') }}</el-button>
    <el-button type="primary" :loading="qcSaving" :disabled="!qcNewStatus" @click="onQcSave">
      {{ t('wms.common.confirm') }}
    </el-button>
  </template>
</el-dialog>
```

- `v-model` 控开合（内部是 `update:modelValue`）。
- `#footer` 插槽放按钮：**取消钮在提交中要 disabled**（`:disabled="qcSaving"`），确认钮 `:loading` ——防止提交中点取消关掉弹窗造成"到底存没存"的悬念。
- 提交按钮还有**业务级禁用** `:disabled="!qcNewStatus"`：没选新状态不许交——校验前移到按钮态。

### 6.5.2 ElMessage / ElMessageBox

**成功/失败 toast**（ElMessage）——StockQueryView 的保存回调是完整范式：

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

注意三层结果处理：业务成功（code===0）/业务失败（code!==0，接口 200 但后端拒绝）/网络异常（catch）。**res.code 自定义处理是这个页面特意保留原生 dialog 的原因之一**（文件头注写明）。成功后除了 reloadList，还就地改了行对象的 qcStatus——弹窗还没关的瞬间表格 tag 已同步变色（深层响应式的实战价值）。

**删除确认**（ElMessageBox.confirm）——WarehouseListView 真实代码：

```ts
async function onDelete(row: Warehouse) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${row.warehouseCd}]`, t('wms.common.confirm'), { type: 'warning' })
    await warehouseApi.delete(row.warehouseCd)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
```

逐行解析：

1. `ElMessageBox.confirm(消息, 标题, 选项)` 返回 Promise：**点确定 resolve，点取消/关闭 reject**。
2. 所以整段包 try/catch：`await confirm` 之后的代码天然只在"用户确认后"执行；catch 空体吞掉"用户取消"这个 reject（取消不是错误）。这是最优雅的确认流写法——**没有回调地狱，一条直线**。
3. 消息里拼了业务主键 `[row.warehouseCd]`——删除确认必须让用户看见"删的是哪条"。
4. `{ type: 'warning' }` 加黄色警示图标。
5. 删除成功 → toast + reloadList。已知边界（CpListPage 头注记录在案）：删掉当前页最后一行后 reload 停留原页码可能显示空页——生产代码连这种边角都有记账。

**ElMessage vs ElMessageBox vs ElNotification 选型**：

| 组件 | 形态 | 场景 |
|---|---|---|
| ElMessage | 顶部飘 toast，几秒自动消失 | 操作结果反馈（保存成功/失败） |
| ElMessageBox | 模态确认框，阻断操作 | 危险操作二次确认（删除/批量） |
| ElNotification | 角落通知卡片，可带标题 | **系统主动推送**（新审批到达，见 6.9） |

### 6.5.3 el-drawer

抽屉=从侧边滑出的 dialog，API 几乎相同（v-model、title、方向 `direction`、size）。选型：**表单编辑用 dialog（聚焦）、长详情/多 tab 详情用 drawer（纵向空间大、可保持列表上下文）**。CP6 的 OA 信箱详情走的是页面内嵌面板 + 路由 query 的形态，WMS 侧编辑基本统一 dialog。

### 6.5.4 加载与骨架屏

- `v-loading`：区域遮罩，CP6 全部列表/弹窗用它（`v-loading="reviewLoading"` 连移动端卡片容器上也能挂——它是指令，不挑元素）。
- `el-skeleton`：骨架屏，首屏体验更好但要为每种布局定制模板，CP6 的取舍是列表页统一 v-loading（模板组件内建），成本低、一致性高。
- 按钮级 `:loading`：提交中按钮转圈并自动禁点——**凡 async 提交必配**（本章每个真实例子都带）。

### 6.5.5 坑

1. **confirm 不 catch**：用户点取消 → unhandled promise rejection 刷控制台。要么 catch 空体，要么 `.catch(() => {})`。
2. **dialog 内容销毁时机**：默认关闭不销毁内容（隐藏而已）。需要每次打开重置的，靠 open 时显式赋值（CP6 做法）或 `destroy-on-close`。
3. **嵌套弹窗 z-index**：dialog 里再弹 MessageBox 一般没事（自动递增 z-index），但自己写的浮层要用 `append-to-body`。
4. **ElMessage 连环轰炸**：批量操作逐条 toast 失败（InboxPending 的 doBatch 对每个失败项 `ElMessage.error`）在失败很多时会刷屏——数量大时应聚合成一条或用结果弹窗。

### 6.5.6 面试问答

**Q：删除按钮的完整交互链路？**
A：按钮挂 `v-permission`（无权者不可见）→ 点击 `ElMessageBox.confirm` 带上业务主键的警示确认 → await 确认后调 delete API（axios 拦截器自动带 CSRF 头）→ 成功 ElMessage.success + 列表 reload（保留筛选页码）→ 用户取消走 catch 静默。后端侧同样有 `[RequirePermission]` 强校验，前端隐藏只是体验层。

---

<a id="66"></a>
## 6.6 常用组件速查（全部对应 cp6.web 真实用法）

### el-select 下拉

基本形态（WarehouseListView 仓库种别）：

```vue
<el-select v-model="form.warehouseType">
  <el-option v-for="(label, val) in warehouseTypeMap" :key="val" :label="label" :value="Number(val)" />
</el-select>
```

- `el-option` 的 `:value` 注意类型：对象键遍历出来是字符串，存的是数字就要 `Number(val)` 转回去，否则回显对不上（**select 回显失败十有八九是类型不一致**：'1' !== 1）。
- 远程搜索形态：`filterable remote :remote-method="search" :loading="loading"`——用户敲字触发 remote-method 调后端模糊查询，选项动态更新。适用：客户/品目这类上万条的主数据选择（配合防抖）。
- 大数据量下拉的终极形态是 `el-select-v2`（虚拟化下拉）。

### el-date-picker 日期

- 单日期：CpFormDialog 里 `type="date"`。
- 范围 + 快捷项（面试常问）：

```vue
<el-date-picker v-model="range" type="daterange" value-format="YYYY-MM-DD"
  :shortcuts="[
    { text: t('近7天'), value: () => [dayjs().subtract(7,'d').toDate(), new Date()] },
    { text: t('本月'), value: () => [dayjs().startOf('month').toDate(), new Date()] },
  ]" />
```

- 关键属性 `value-format`：不设时 v-model 拿到的是 Date 对象，设 `YYYY-MM-DD` 直接拿字符串——**送后端前必须统一格式，否则时区偏移一天**（Date 序列化成 UTC ISO 串，东九区的 7/15 00:00 变成 7/14T15:00Z）。
- 范围值是 `[start, end]` 数组，送后端拆成 from/to 两个参数（stockApi.transactions 的 `from?/to?` 就是这个协议）。

### el-cascader 级联

省市区/多级分类/组织树选择。`:options` 嵌套结构 + `:props="{ checkStrictly: true }"` 允许选任意层级。CP6 的部门树场景用的是 el-tree 形态（DeptTreeView），级联选择在制造业常见于"大分类→中分类→品目"。

### el-upload 上传

要点：`action`（或 `http-request` 自定义上传函数走自家 axios 带 token/CSRF）、`:limit`、`:on-exceed`、`before-upload` 做类型/大小校验、`file-list` 回显。管理系统里通常封装成 AttachmentUpload 组件挂在单据编辑页。注意 CP6 的认证是 httpOnly Cookie（`http.ts` 的 `withCredentials: true`），el-upload 默认用原生 XHR，需要 `with-credentials` 属性带 Cookie + 手动补 CSRF 头——**自定义 http-request 统一走 axios 实例是最省心的做法**（拦截器逻辑全复用）。

### el-tabs 标签页

InboxPending 真实用例（待审 / 抄送两个 tab + 懒加载）：

```vue
<el-tabs v-model="activeTab" @tab-change="onTabChange">
  <el-tab-pane :label="t('oa.pending.toReview')" name="review">...</el-tab-pane>
  <el-tab-pane :label="t('oa.pending.cc')" name="cc">...</el-tab-pane>
</el-tabs>
```

```ts
function onTabChange(name: string | number) {
  if (name === 'cc' && !ccRows.value.length) loadCc()
}
```

**切到 CC tab 且还没数据才去加载**——tab 惰性取数，避免进页面并发打两个接口。默认所有 tab-pane 都会渲染 DOM（只是隐藏），重型内容可加 `lazy`。

### el-descriptions 详情

StockQueryView 履历弹窗真实用例：

```vue
<el-descriptions :column="4" size="small" border>
  <el-descriptions-item :label="t('wms.common.product')">{{ historyStock.productCd }}</el-descriptions-item>
  <el-descriptions-item :label="t('wms.common.lot')">{{ historyStock.lotNo }}</el-descriptions-item>
  <el-descriptions-item :label="t('wms.common.warehouse')">{{ historyStock.warehouseCd }}</el-descriptions-item>
  <el-descriptions-item :label="t('wms.common.location')">{{ historyStock.locationCd }}</el-descriptions-item>
</el-descriptions>
```

`:column="4"` 一行四组"标签: 值"。**详情页/详情弹窗的头部信息区标配**，比自己排 label-value 网格省事且样式统一。

### el-popover + el-badge（通知铃铛）

NotificationBell 真实组合（6.9 详讲）：`el-badge` 红点角标（`:value="unreadCount" :max="99" :hidden="unreadCount === 0"`）套住铃铛按钮，作为 `el-popover` 的 `#reference` 触发器，`@show` 时懒加载通知列表。

---

<a id="67"></a>
## 6.7 权限指令 v-permission 完整精读（面试亮点）

这一节是面试的高光素材：**自定义指令 + Pinia + 前后端双层防线**，三个知识点串成一个真实功能。

### 6.7.1 指令本体（`C:\CP6\cp6.web\src\directives\permission.ts`，全文 17 行）

```ts
import type { Directive } from 'vue'
import { usePermissionStore } from '@/stores/permission'

/**
 * v-permission="'order:export'" —— 无该操作权则移除元素。
 * 注意：仅 UX 层；后端 [RequirePermission] 才是强校验。
 * store 未加载完成（loaded=false）时 fail-open（保留元素），避免首屏误删。
 */
export const permission: Directive<HTMLElement, string> = {
  mounted(el, binding) {
    const store = usePermissionStore()
    const key = binding.value
    if (store.loaded && key && !store.has(key)) {
      el.parentNode?.removeChild(el)
    }
  }
}
```

逐行解析：

1. `Directive<HTMLElement, string>`：Vue 自定义指令的 TS 泛型——宿主元素类型 + 绑定值类型。指令是一个**含生命周期钩子的对象**：`created/beforeMount/mounted/beforeUpdate/updated/beforeUnmount/unmounted`（与组件生命周期一一对应，作用对象是"挂了指令的那个元素"）。
2. 这里只实现了 `mounted`：元素插入 DOM 后执行一次。`el` 是真实 DOM 元素，`binding.value` 是 `v-permission="'wms-stock-qc:set'"` 里引号内那个字符串（指令值是 JS 表达式，所以字符串要双层引号）。
3. `usePermissionStore()` 在指令里直接调——Pinia store 可以在**任何 setup 上下文之外**使用（只要 pinia 实例已安装），指令钩子执行时机在 app 挂载后，安全。
4. 判定：`store.loaded && key && !store.has(key)` 三条件齐才删元素。
5. `el.parentNode?.removeChild(el)`：**直接从 DOM 移除元素**。

三个设计决策值得在面试展开：

**决策一：DOM 移除 vs disabled？**
移除=无权者完全不知道功能存在（信息最小暴露，界面干净）；disabled=告知"功能存在但你不能用"（引导申请权限）。CP6 选移除——制造业现场终端用户多、角色差异大，砍掉无关按钮降低误触与培训成本。要 disabled 语义时留给业务用 `:disabled="!permStore.has(key)"` 显式写。注意移除是**指令级一次性操作**：Vue 的 vdom 不知道元素没了，所以这个指令只适合"挂载时权限已确定"的场景（见决策三）。

**决策二：fail-open（未加载时先保留）**
权限集是异步从后端拉的（下文 store）。首屏渲染时 `loaded=false`，此时**宁可先显示按钮也不误删**——因为删了就回不来（mounted 只跑一次），而多显示的按钮点下去后端会 403。这是"前端只是 UX 层"哲学的自洽推论：**误显示的代价是一次 403 提示，误隐藏的代价是功能永久消失**。反过来，如果这个指令承担安全职责，就必须 fail-closed——但它不承担（见 6.7.4）。

**决策三：为什么没实现 updated？**
任务书里提到的 `updated` 钩子这里刻意没写。因为权限集在一次会话内基本不变（登录时拉取），且"删掉的元素无法在 updated 里复活"。代价是：如果权限在页面已挂载后才加载完成（loaded 从 false 翻 true），已渲染的按钮不会补删——CP6 用**启动时预拉**（main.ts 最后一行）把这个窗口压到最小。更完备的实现（面试可提的改进）：`mounted/updated` 里根据权限切换 `el.style.display`，或用 `v-if="permStore.has(key)"` 让 vdom 全权管理（代价是每处都要 import store）。

### 6.7.2 数据源：Pinia permission store（`C:\CP6\cp6.web\src\stores\permission.ts`，全文 34 行）

```ts
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

逐行解析：

1. Setup 语法的 Pinia store（第 5 章讲过）：ref=state、函数=action。
2. `actionKeys` 用 **Set 而非数组**：`has()` 是 O(1)，一个页面可能有几十个 v-permission 判定。
3. key 的形状是 **`"menuKey:action"`**（如 `wms-stock-qc:set`、`wms-warehouse:del`、`order:export`）——**资源:操作**两段式，与后端权限种子（MenuAction/RoleAction 表，逐租户）同一命名法。注意 CP6 全仓约定：**键用连字符不用下划线**。
4. `loadMyActions()` 调 `GET rolePerm.myActions()` 拿"当前用户在当前租户的全部操作键"，失败静默（未登录时首屏也不能崩）。
5. `reset()`：登出/切换用户时清空——权限是会话态。

### 6.7.3 注册：main.ts 两行接线

```ts
import { permission } from './directives/permission'
import { usePermissionStore } from './stores/permission'
// ...
// PUB 章02：注册 v-permission 指令
app.directive('permission', permission)

app.mount('#app')

// 已登录则预拉当前用户操作权（v-permission 数据源）；未登录会静默失败
usePermissionStore().loadMyActions()
```

- `app.directive('permission', permission)`：全局注册，指令名 permission → 模板里 `v-permission`。
- **mount 之后立刻预拉权限**：让 loaded 尽早翻 true，缩小 fail-open 窗口。已登录用户（httpOnly Cookie 还在）刷新页面也能拿到权限集；未登录 401 静默。
- 登录成功的流程里还会再调一次 loadMyActions（登录后才有权限上下文）。

### 6.7.4 与后端 RequirePermission 的对应：为什么前端隐藏不是安全边界（必背）

```
前端: <el-button v-permission="'wms-stock-qc:set'" @click="openQcDialog">   ← UX 层
后端: [RequirePermission("wms-stock-qc", "set")]                             ← 安全层
      public async Task<IActionResult> SetQcStatus(...)
```

两层用**同一个权限键**（`wms-stock-qc:set`），但职责完全不同：

- **前端隐藏 = 体验**：不让用户看到自己用不了的功能。它可以被任意绕过——打开 DevTools 删掉指令效果、直接用 curl/Postman 打接口、篡改 store 的 actionKeys（前端内存里的 Set 想改就改）。**任何在客户端执行的检查都在攻击者的控制范围内。**
- **后端 403 = 安全**：`[RequirePermission]` 特性在服务端从当前用户身份（httpOnly Cookie 里的 token → 用户 → 角色 → RoleAction 表）重新判定，客户端无法伪造。CP6 的上线验收里专门做过"无认证打高危端点必须 403"的线上实证。
- **两层缺一不可**：只有后端 → 用户满屏点不动的按钮点了才报错，体验差；只有前端 → 裸奔（用户"看不到"导出按钮但脚本照样导数据）。
- 同构的老话题：**表单校验也是两层**——前端 rules 是体验（即时反馈），后端 DTO 校验是防线。所有"客户端约束"都遵循同一定律。

看真实消费端（本章已出现两次）：

```vue
<!-- StockQueryView.vue -->
<el-button v-permission="'wms-stock-qc:set'" link type="warning" size="small" @click="openQcDialog(row)">
<!-- WarehouseListView.vue -->
<el-button v-permission="'wms-warehouse:del'" link type="danger" size="small" @click="onDelete(row)">
```

规律：**挂 v-permission 的都是变更类/危险类操作**（设置 QC、删除），只读查询按钮不挂——权限粒度落在"操作"而非"页面"（页面级由路由+菜单控制，操作级由按钮控制，数据级由后端租户过滤控制，三级递进）。

### 6.7.5 坑

1. `v-permission="wms-stock-qc:set"`（少了内层引号）→ 被当 JS 表达式求值 → undefined → 判定短路 → 按钮全显示。**指令值是表达式，字符串必须双层引号**。
2. 挂在**组件**上（`<MyButton v-permission=...>`）：自定义指令作用在组件根元素上，多根组件会警告失效；且移除组件根 DOM 不等于卸载组件实例（潜在内存驻留）。CP6 只用于原生元素/单根 EP 组件。
3. 权限键手滑写错（`wms-stockqc:set`）不会报错，只会"按钮神秘消失"——权限键该有常量/类型化治理（CP6 后端有五源对账测试兜底键面一致性）。
4. 把"藏按钮"当安全上线 → 渗透测试一打一个准。汇报安全能力时永远说后端那层。

### 6.7.6 面试问答

**Q：手写一个按钮权限指令的思路？**
A：全局指令 + Pinia 权限 store。登录后拉一次"当前用户操作键集合"存 Set；指令 mounted 里 `binding.value` 拿权限键，`store.has(key)` 为 false 就把 `el` 从 parentNode 移除。细节：store 未加载完 fail-open 防首屏误删（删了不可逆）；Set 保证 O(1) 判定；键形状 `资源:操作` 与后端授权特性同名同源。同时强调这只是 UX——真正的强校验是后端同键的 RequirePermission，客户端一切检查都可被绕过。

**Q：v-permission 和 v-if 做权限有什么区别？**
A：v-if 由 vdom 管理，权限响应式变化能自动恢复元素，但每处要引 store 写表达式；自定义指令语法糖更干净、可全局治理，但 mounted 移除是一次性动作、不响应后续变化。我们的场景权限在会话内不变，指令方案更简洁；要动态权限（如切租户不刷新）就得 v-if 或指令里改 display 而非移除。

---

<a id="68"></a>
## 6.8 多语言实战：vue-i18n 在 CP6 的工业级用法

CP6 是日企场景系统，**五语言**（ja 默认 / zh-CN / zh-TW / en / ko），词条不在前端仓库里而是**后端数据库统一管理、API 下发**。这套方案比"前端 JSON 文件"高一个量级，面试很出彩。

### 6.8.1 页面里的日常用法

本章每个标本都在用：

```ts
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
```

```vue
<CpPageShell :title="t('wms.stock.title')">
<el-tag>{{ t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) }}</el-tag>   <!-- 动态拼 key -->
{{ t('共 {n} 条', { n: reviewRows.length }) }}                            <!-- 带参数插值 -->
```

三种形态：静态 key、动态拼 key（枚举后缀）、带具名参数（`{n}` 占位）。**再强调一次：任何进 computed/常量的 t() 都要让外层是 computed**，否则切语言不刷新。

### 6.8.2 i18n 初始化与"启动即多语言"（`C:\CP6\cp6.web\src\i18n\index.ts`，250 行）

```ts
const i18n = createI18n({
  legacy: false,                      // Composition API 模式（useI18n 的前提）
  locale: localStorage.getItem('lang') || 'ja',
  fallbackLocale: { 'zh-CN': ['zh-TW', 'ja'], 'zh-TW': ['zh-CN', 'ja'], en: ['ja'], ko: ['en', 'ja'], default: ['ja'] },
  flatJson: true,                     // {"a.b.c":"v"} 扁平 key 直接可查
  missingWarn: false,
  fallbackWarn: false,
  datetimeFormats: ...,               // 每语言的日期格式（short/long/time）
  numberFormats: ...,                 // 每语言的数字格式（decimal/integer/percent）
  messages: {},                       // ← 启动时是空的！词条全部异步从 API 拉
})
```

关键设计逐条解析：

1. **`messages: {}` 空启动**：词条不打包进前端。`main.ts` 的 bootstrap 里 `await initI18n()` **先于 createApp**——先拉当前语言（+回退链语言）的核心词条包再挂载应用，保证首屏不闪裸 key。
2. **回退链（fallbackLocale）**：zh-CN 缺词条→查 zh-TW→再查 ja。**逐级回退而非一律回默认语言**——简繁互备内容最接近，比直接跳日语体验好。
3. **`flatJson: true`**：后端下发的是 `{"wms.stock.title": "在庫照会"}` 这种扁平字典，无需嵌套结构。
4. **命名空间懒加载（性能优化）**：词条量大（keys.generated.json 100KB+），拆成 `_core`（导航/登录/通用，启动即载）+ 大模块包（`wms/sales/erp/mes`）。路由守卫调 `ensureNamespacesForPath(path)`：进 `/wms/**` 路由时才拉 wms 包（连同回退链语言的 wms 包）。`loadedPacks` Set 防重复加载。
5. **切换语言 `changeLang(langCode)`**：拉新语言的基础包 + **本会话访问过的所有模块包**（neededNamespaces 记账）+ 回退链包，全就绪后才切 `i18n.global.locale.value` 并写 localStorage——**先备货再切换**，避免切完满屏裸 key。
6. **伪本地化（dev-only 彩蛋，面试加分项）**：开发构建多一个 `🔣 Pseudo (QA)` 语言，把英文词条重音化加长 40%（`Warehouse` → `⟦Ŵåŕéĥöüšé·····⟧`）。作用：①硬编码文案不会变形，一眼揪出漏 i18n 的字符串；②加长文本撑爆布局，提前发现溢出。`{占位}` 保持原样不变形。这是国际化工程的业界正规手法（pseudo-localization），出现在真实代码里非常有说服力。

### 6.8.3 缺 key 兜底：tOr（`C:\CP6\cp6.web\src\i18n\tOr.ts`）

```ts
export function tOr(i18n: I18nLike, key: string, fallback?: string): string {
  if (!key) return fallback ?? key
  return i18n.te(key) ? i18n.t(key) : (fallback ?? key)
}
```

`te()`（translation exists）先探测，词条存在才 t()，否则回 fallback 或 key 原文。典型用途（注释原话）：**后端错误码**——`E-SPACE-3xx` 注册了词条就本地化，没注册原样透出（工程师能拿码排查）。而 flatJson + `missingWarn:false` 下裸 t() 缺 key 的表现是**返回 key 本身**——所以 CP6 的 UI 缺词条时不会白屏/报错，而是显示 `wms.stock.xxx` 这样的 key，QA 一眼可辨。

axios 拦截器里的消费（`http.ts`）：

```ts
const raw = error.response?.data?.message
ElMessage.error((raw ? t(raw) : '') || error.response?.data?.title || t('请求失败'))
```

后端返回错误码（如 `E-FIN-107`）→ t() 翻译成当前语言的友好文案；后端返回自由文本 → key 不存在原样回显。**一行代码同时处理"错误码本地化"与"自由文本透传"**。

### 6.8.4 日期/数字本地化（`C:\CP6\cp6.web\src\utils\format.ts`）

```ts
export function formatQty(v: NumberInput, maxFrac = 4): string {
  const n = toNumber(v)
  if (n === null) return ''
  return new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: maxFrac }).format(n)
}
export function formatCurrency(v: NumberInput, currency = 'JPY'): string {
  const n = toNumber(v)
  if (n === null) return ''
  return new Intl.NumberFormat(currentLocale(), { style: 'currency', currency }).format(n)
}
```

设计要点（文件头注写得很清楚）：

- 日期/数字走 vue-i18n 的 `d()/n()`（格式定义集中在 i18n/index.ts 的 datetimeFormats/numberFormats）；**货币因为多币种动态（JPY/CNY/USD），改用 `Intl.NumberFormat` 按 locale+currency 即时格式化**。
- **双形态 API**：组件 setup 内用 `useFormat()`（内部 useI18n，locale 是响应式的，**切语言自动重渲染**）；非 setup 处（el-table `:formatter` 回调、工具函数）import 顶层函数（读全局 i18n 实例的当前 locale）。StockQueryView 用的就是顶层形态：`import { formatQty as fmtQty } from '@/utils/format'`。
- 库存数量 `formatQty(n, 4)`：千分位 + 最多 4 位小数不留尾零——制造业数量既有整箱也有 0.125 卷。

### 6.8.5 坑

1. computed 漏包（表头/rules/下拉选项），本章第三次强调，因为它是真实回归 bug 榜首。
2. 动态拼 key（`t(\`x.${status}\`)`）在"key 静态扫描"工具下会漏检——CP6 用 `i18n:check` 脚本 + 生成的 keys.generated.ts 做键面核对。
3. Element Plus 组件内置文案没接 locale（6.1.5 的真实缺口）——业务词条切了、分页器没切。
4. 切语言不预载目标语言词条就切 locale → 满屏裸 key 闪烁。CP6 的 changeLang 先 await 全部包再切。

### 6.8.6 面试问答

**Q：你们多语言方案的整体架构？**
A：词条不进前端仓库，后端库统一管理、API 下发，前端 vue-i18n `messages:{}` 空启动、bootstrap 时 await 核心包再挂载。三层优化：①命名空间懒加载——通用词条 `_core` 启动即载，wms/erp 等大模块包由路由守卫按 path 按需拉；②显式回退链——zh-CN→zh-TW→ja 逐级回退；③dev 构建带伪本地化语言，硬编码文案和布局溢出一眼现形。切语言先把目标语言的基础包+已访问模块包全部拉齐再切 locale，避免裸 key 闪烁。日期数字走 d()/n() 的每语言格式表，货币多币种用 Intl.NumberFormat。

---

<a id="69"></a>
## 6.9 SignalR 实时推送前端侧

制造业系统的刚需：库存变动、审批到达、设备状态要**推**给前端，不能靠用户刷新。CP6 用 ASP.NET Core SignalR，前端 `@microsoft/signalr` v10。

### 6.9.1 连接管理单例（`C:\CP6\cp6.web\src\utils\signalr.ts`，全文 43 行）

```ts
import * as signalR from '@microsoft/signalr'

let connection: signalR.HubConnection | null = null

export function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/notify')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // 重连间隔
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}

export async function startConnection() {
  const conn = getConnection()
  if (conn.state === signalR.HubConnectionState.Disconnected) {
    try {
      await conn.start()
      console.log('[SignalR] Connected')
    } catch (err) {
      console.warn('[SignalR] Connection failed, will retry:', err)
    }
  }
}

export function stopConnection() {
  if (connection) {
    connection.stop()
    connection = null
  }
}
```

逐行解析：

1. **模块级单例**：`let connection` 模块作用域 + 惰性创建。整个应用共享一条 WebSocket——每个组件各建连接会打爆服务端连接数。
2. `HubConnectionBuilder` 流式构建：`.withUrl('/hubs/notify')` 相对地址走同源（Vite 代理/网关转发）。
3. `.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`：断线自动重连，**退避数组**=立即、2 秒、5 秒、10 秒、30 秒各试一次（数组用尽放弃，触发 onclose）。不传参默认只重试 4 次固定间隔——生产上自定义退避是标配。
4. `start()` 前查 `state === Disconnected`：避免对已连接/连接中的实例重复 start 抛异常。启动失败只 warn 不抛——推送是增强功能，不能阻塞主流程。
5. **认证方式（重要且要讲准确）**：这里**没有** `accessTokenFactory`。因为 CP6 的认证是 **httpOnly Cookie**（`http.ts` 里 `withCredentials: true`，token 在 `cp6_at` Cookie），SignalR 同源协商（negotiate 请求）时浏览器自动携带 Cookie，服务端从 Cookie 认证。如果是 "JWT 放 localStorage" 的架构，才需要 `.withUrl(url, { accessTokenFactory: () => token })`（token 走 query string / header）。面试两种都要会说，并能解释 CP6 为何选 Cookie（XSS 拿不到 httpOnly token）。CP6 波①还专门处理过 **hub 端点的 CSRF 豁免**（negotiate 是 POST，被全局 CSRF 拦截过——真实踩坑）。

### 6.9.2 多 Hub 分连接（`C:\CP6\cp6.web\src\utils\wmsHub.ts`）

```ts
/**
 * WMS SignalR Hub クライアント（独立 connection）
 * /hubs/wms に接続して以下イベントを購読：
 *   StockChanged / InboundReceived / OutboundShipped / StockTakeCompleted
 * 既存の /hubs/notify（汎用）/ /hubs/mes と完全独立 — Hub ごとに connection 分離。
 */
export interface StockChangedPayload {
  txnNo: string
  txnType: 'IN' | 'OUT' | 'MOVE' | 'ADJ' | 'RSV' | 'UNRSV'
  txnAt: string
  warehouseCd: string
  // ...
}

export async function subscribeWarehouse(warehouseCd: string) {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('SubscribeWarehouse', warehouseCd)
  }
}
```

- 三条 Hub 三条连接：`/hubs/notify`（通用通知）、`/hubs/wms`、`/hubs/mes`——**按域拆 Hub**，各域推送互不干扰、可独立伸缩。
- **推送 payload 有 TS interface**：`StockChangedPayload` 把服务端推的事件形状类型化——实时消息也是前后端契约的一部分。
- `invoke('SubscribeWarehouse', cd)`：**客户端调服务端方法**（双向 RPC），服务端把该连接加入"仓库分组"，之后只收本仓库的变动——**分组订阅**降噪。注释诚实记录了限制：断线重连后**需要重新订阅**（服务端分组是按连接的，新连接=新分组成员资格）——正规解法是在 `onreconnected` 回调里重放订阅。
- `on('事件名', handler)` 收服务端推送；WmsDashboardView 消费这些事件刷新看板。

### 6.9.3 组件消费全范式（`C:\CP6\cp6.web\src\views\oa\notification\NotificationBell.vue`）

顶栏通知铃铛——**SignalR 前端侧的教科书组件**：

```ts
import { getConnection } from '@/utils/signalr'
import { ElNotification } from 'element-plus'

// ── SignalR 接线 ───────────────────────────────
// 保留 handler 引用，卸载时 off() 移除，防止内存泄漏
const wfNotificationHandler = () => {
  refreshUnread()                 // 角标计数重拉
  if (popoverOpen.value) {
    refreshList()                 // 面板开着才刷列表
  }
  ElNotification({
    title: t('oa.notify.title'),
    message: t('oa.notify.newArrived'),
    type: 'info',
    duration: 3000,
    position: 'bottom-right',
  })
}

// ── 60s 轮询兜底（防 SignalR 掉线后角标失效）──
let pollTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  refreshUnread()
  getConnection().on('WfNotification', wfNotificationHandler)
  pollTimer = setInterval(refreshUnread, 60_000)
})

onUnmounted(() => {
  getConnection().off('WfNotification', wfNotificationHandler)
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
```

五个必讲要点：

1. **handler 存具名引用**：`off('事件', handler)` 按引用移除。写匿名箭头函数注册就永远 off 不掉——**订阅泄漏**，组件反复挂卸后一条推送触发 N 次回调。这是 SignalR/EventBus/addEventListener 共通的经典坑。
2. **onUnmounted 成对清理**：off 事件 + clearInterval。凡 onMounted 里做的外部登记，onUnmounted 必须逆操作。
3. **收推后拉数据而非直接用推送体**：handler 里不解析推送内容、而是 `refreshUnread()` 重新拉计数——**"通知即失效信号"（notify-then-fetch）模式**：推送只说"有新东西"，数据以拉取为准。好处：推送丢一条也只是晚 60 秒（轮询兜底），不会状态错乱；权限过滤逻辑集中在查询接口。
4. **轮询兜底**：60 秒 setInterval 重拉角标——SignalR 掉线（重连退避用尽）时体验降级为准实时而非失效。**实时通道 + 轮询兜底**是生产级健壮性的标准双保险。
5. **UI 呈现三件套**：`el-badge` 角标未读数 → `ElNotification` 右下角轻提示（3 秒自动消失，不打断作业）→ el-popover 点开看列表。注释还有一个安全意识点："不依赖客户端 userId 过滤——后端已按 auth scope 推送到本人"（客户端过滤可被篡改，推送范围由服务端裁定）。

### 6.9.4 坑

1. 匿名函数注册无法 off（上面第 1 点）。
2. 每组件各自 new 连接——必须模块级单例/按 Hub 单例。
3. 断线重连后分组订阅丢失——onreconnected 里重放 invoke。
4. 把推送体直接当真相写状态——推送乱序/丢失会脏；notify-then-fetch 更稳。
5. hub 端点被全局 CSRF/鉴权中间件误拦（CP6 真实踩坑：negotiate 403）——网关和中间件要给 `/hubs` 前缀精确豁免形状。

### 6.9.5 面试问答

**Q：前端 SignalR 怎么保证不漏消息、不泄漏？**
A：连接层——按 Hub 模块级单例 + withAutomaticReconnect 自定义退避；消息层——用 notify-then-fetch：推送只当失效信号，收到后重拉接口，另加 60s 轮询兜底，掉线也只是降级成准实时；生命周期层——handler 具名引用，onMounted 里 on/setInterval、onUnmounted 里 off/clearInterval 严格成对；重连后在 onreconnected 里重放分组订阅。认证走 httpOnly Cookie 自动携带（同源），若是 token 架构则 accessTokenFactory。

---

<a id="610"></a>
## 6.10 移动端适配案例：767px 卡片化改造

背景：OA 审批信箱要在车间/外出场景用手机处理，波④做了 767px 断点的移动端改造，**验收硬指标是"桌面像素零回归"**。标本：`C:\CP6\cp6.web\src\views\oa\inbox\InboxPending.vue`（待审列表）。

### 6.10.1 方案选型：JS 断点条件渲染 + CSS 微调，双轨并用

**主方案：`useBreakpoint()` 组合式 + v-if 双渲染**（`C:\CP6\cp6.web\src\composables\useBreakpoint.ts`）：

```ts
const MOBILE_MAX = 767
const width = ref(typeof window !== 'undefined' ? window.innerWidth : 1280)

let listenerCount = 0
function onResize() { width.value = window.innerWidth }

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
    if (listenerCount === 0) window.removeEventListener('resize', onResize)
  })
  const isMobile = computed(() => width.value <= MOBILE_MAX)
  // isTablet / isDesktop ...
  return { width: readonly(width), isMobile, isTablet, isDesktop }
}
```

亮点：`width` 是**模块级共享 ref**（全应用一份），`listenerCount` 引用计数——多少个组件用这个 composable，resize 监听只挂一个，最后一个用户卸载时才移除。**composable 的资源共享范式**，面试可写。

消费端（InboxPending）：

```vue
<el-table v-if="!isMobile" :data="reviewRows" ... @selection-change="onSelectionChange" @row-click="onReviewRowClick">
  <el-table-column type="selection" width="46" />
  <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
  ...
</el-table>

<div v-if="isMobile" class="mobile-list" v-loading="reviewLoading">
  <div v-for="row in reviewRows" :key="row.taskId" class="mobile-row"
       :class="{ 'row-unread': !row.isRead }" @click="onReviewRowClick(row)">
    <div class="mobile-main">
      <el-checkbox :model-value="isSelected(row)" @click.stop @change="toggleMobileSelect(row)" />
      <span class="mobile-flow">{{ row.flowName }}</span>
      <CpTag tone="info">{{ row.stageName || row.nodeId }}</CpTag>
    </div>
    <div class="mobile-meta">
      <span class="mobile-key">{{ row.flowKey }}</span>
      <span>{{ row.starterName }}</span>
      <span>{{ formatTime(row.sentAt) }}</span>
    </div>
  </div>
</div>
```

**为什么选 v-if 双渲染而不是纯 CSS（display 切换或表格塞 @media 变形）？**

1. **"桌面像素零回归"怎么保证**：桌面分支的 el-table 标签**一个字符没改**——桌面 DOM 与改造前完全一致，回归风险物理隔离。如果用 CSS 把 el-table 强行变形成卡片（`display:block` 各种 hack），桌面样式极易被连带波及，而且 el-table 内部结构复杂根本 hack 不动。
2. 移动端呈现的**不是"变窄的表格"而是不同的信息设计**：主行（复选框+流程名+节点 tag）/副行（单号+发起人+时间），列的取舍和层级重排——这只有条件渲染才做得出。
3. v-if（而非 v-show）保证同一时刻只有一套 DOM，移动端不用付出渲染整张表格的成本。
4. 代价与对策：状态在两套 UI 间要共享。数据层天然共享（同一个 `reviewRows/selected`）；但 el-table 的**内部选中态**是组件私有的——引出下面的真实 bug。

**跨断点选中态回填**（源码里注释最长的一段，工程含金量极高）：

```ts
/**
 * 跨断点多选回填：el-table 受 v-if 门控，mobile→desktop 时表格重挂载内部选中态为空，
 * 而 @selection-change 是单向覆盖 selected 数组——用户在移动端勾选后旋转到桌面再触碰任一
 * 原生复选框，会把之前的移动端选择静默丢弃（批量条计数与提交 ids 背离）。
 */
watch(isMobile, async (mobile, prev) => {
  if (prev === true && mobile === false) {
    await nextTick()
    const ids = new Set(selected.value.map((r) => r.taskId))
    for (const row of reviewRows.value) {
      if (ids.has(row.taskId)) reviewTableRef.value?.toggleRowSelection(row, true)
    }
  }
})
```

场景：手机横过来变桌面宽度 → v-if 切换 → el-table **重新挂载**（内部勾选全空）→ 但页面的 `selected` 数组还留着移动端的勾选 → 此时用户再点表格任一复选框，`@selection-change` 用表格的（不完整）选中集**整体覆盖** `selected` → 之前勾的静默丢失 → 批量审批提交的 ids 和用户认知不一致。修法：watch 断点从 mobile 翻 desktop，nextTick 等表格挂载完，把 `selected` 里的行逐一 `toggleRowSelection(row, true)` **回填**进表格。反方向不用处理（卡片直接读 `selected` 数组渲染勾选态，单一数据源天然一致）。移动端勾选则维护同一数组（`toggleMobileSelect`），**批量操作条和 doBatch 零改动复用**。

**辅方案：@media 微调**（同文件 style 尾部，767px 真实案例）：

```css
@media (max-width: 767px) {
  .batch-bar {
    flex-wrap: wrap;
  }
  .batch-bar .el-input {
    width: 100% !important;
    order: 3;
  }
}
```

批量操作条不需要换 DOM 结构，只是窄屏下换行：允许 wrap + 意见输入框占满整行挪到末位（flex `order`）。**选型分界线：信息结构要变→v-if 双渲染；只是布局挤一挤→@media**。同目录 `InboxView.vue:355`、`FormDetail.vue:365` 也各有 767px 的 @media 块（同一波改造，断点全系统统一 767px，与 useBreakpoint 的 MOBILE_MAX 常量一致——**断点数字双处定义要同源**，这也是个记录在案的注意点）。

### 6.10.2 坑

1. CSS 强行把 el-table 变卡片——组件内部结构不受你控，版本升级即碎。
2. v-if 双渲染丢组件内部状态（选中/展开/滚动位置）——数据外提到页面级 + 切换后回填。
3. `@click.stop`：移动卡片里复选框点击要阻止冒泡，否则勾选同时触发行点击进详情。
4. resize 监听不清理/每组件一个监听——composable 引用计数。

### 6.10.3 面试问答

**Q：管理系统表格页怎么做移动端？**
A：分两档。布局级问题（工具条换行、按钮堆叠）用 @media 微调；信息结构级问题（表格→卡片）用 JS 断点（共享 ref+引用计数的 useBreakpoint composable）v-if 条件渲染两套 UI——桌面 el-table 原封不动保证零回归，移动端按"主行+副行"重新做信息设计。关键陷阱是组件内部状态：el-table 重挂载后选中态清零，而 selection-change 是覆盖式的，要在断点切换 watch 里 nextTick 后 toggleRowSelection 回填，保证批量操作的数据源单一。

---

<a id="611"></a>
## 6.11 前端架构总结：目录全景与新页面开发步骤

### 6.11.1 目录全景（`C:\CP6\cp6.web\src`，实测结构）

```
src/
├── api/            # API 层：axios 实例(http.ts 拦截器：Cookie 认证/CSRF/401 刷新/错误码 i18n)
│                   #   + 按域分目录(wms/oa/sys/erp...)的端点函数集，泛型标注响应类型
├── assets/         # 静态资源
├── components/     # 通用组件：base/(CpTag/CpEmpty 原子) templates/(CpPageShell/CpListPage/
│                   #   CpFilterBar/CpFormDialog 页面级模板——130+ 页面的形态收敛点)
├── composables/    # 组合式函数(useBreakpoint 等可复用逻辑)
├── directives/     # 自定义指令(permission.ts = v-permission)
├── i18n/           # 多语言：index.ts(空启动/懒加载/回退链/伪本地化) tOr.ts(缺 key 兜底)
│                   #   keys.generated.*(脚本从后端拉的键面快照，做类型与核对)
├── router/         # 路由 + 守卫(登录态/动态菜单/i18n 命名空间预载)
├── stores/         # Pinia(permission/用户会话/oaActingAs 等)
├── styles/         # 设计系统：tokens.css(--cp-* 变量) tokens-dark.css element-overrides.css
├── types/          # TS 类型：按域的实体/DTO 接口(types/wms/wms.ts 的 Stock 等)
├── utils/          # 工具：format.ts(locale 感知格式化) signalr.ts/wmsHub.ts/mesHub.ts(实时)
├── views/          # 业务页面，按模块分目录：wms/ oa/ erp/ mes/ pms/ space/ wf/ platform/...
├── space-editor/   # 特殊模块：3D 空间(仓库/工厂布局)编辑器(Konva/three.js 技术栈)
└── space-viewer/   # 特殊模块：3D 空间查看器(与编辑器分离打包，查看端更轻)
```

一句话总纲（面试"你们前端怎么组织"的标准答案）：**按技术角色分层（api/stores/components/views），层内按业务域分目录（wms/oa/erp）；页面形态收敛到 templates 模板组件，横切关注点（权限/多语言/实时/格式化）各有唯一的基建入口**（directives/i18n/utils），两个 3D 模块因技术栈重（three/konva）独立成顶层目录隔离依赖。

### 6.11.2 一个新页面的标准开发步骤清单（背下来）

以"新增一个 WMS 查询页"为例：

1. **建 View**：`src/views/wms/XxxView.vue`——CpPageShell + CpListPage 起手，声明 columns（computed+t()）/searchFields/fetchList。
2. **配路由**：router 里加 `/wms/xxx` 路由（挂到 WMS 布局下；菜单项由后端菜单表配置下发，MenuKey 与路由对齐）。
3. **写 API 层**：`src/api/wms/xxx.ts` 端点函数 + `src/types/wms/` 里定义响应 DTO 接口（与后端 DTO 字段对齐）。
4. **接权限**：变更类按钮挂 `v-permission="'wms-xxx:action'"`；确认后端已种 MenuAction/RoleAction 同键种子（**前后端同一个键，连字符命名**）。
5. **加 i18n key**：后端词条库登记 `wms.xxx.*` 各语言词条（wms 命名空间随路由懒加载），前端跑 `npm run i18n:check` 核对键面。
6. **写测试**：vitest 组件测试（模板组件契约已有测试兜底，业务页测 fetch 参数翻译/插槽渲染分支），必要时 Playwright e2e。
7. 自查四件事：切语言表头会不会变（computed）、改条件页码归 1、无权限按钮消失、失败时旧数据保留。

### 6.11.3 面试问答

**Q：新人进你们项目写一个列表页要多久？**
A：半天以内。因为路径是铺好的：模板组件收掉交互剧本，API/类型/i18n/权限各有固定接线点，照清单走 7 步。新人不需要知道竞态守卫、分页重置这些细节——它们在 CpListPage 里只实现了一次。这也是我理解的前端架构：**把正确性沉到基建，让业务页只剩业务**。

---

<a id="612"></a>
## 6.12 面试专题

### 6.12.1 ElementUI(Vue2) → Element Plus(Vue3) 迁移差异清单

1. **v-model 变化**：组件 prop 从 `value/@input` 统一为 `modelValue/@update:modelValue`；`.sync` 修饰符废除，改多 v-model（`v-model:visible` → 现在 el-dialog 直接 `v-model`）。
2. **生命周期**：`destroyed→unmounted` 等重命名；指令钩子从 `bind/inserted/update` 改为与组件一致的 `mounted/updated/unmounted`（v-permission 若从 Vue2 迁移，`inserted` 要改成 `mounted`）。
3. **图标体系**：字体 class → SVG 组件包 `@element-plus/icons-vue`（6.1.7 详述）。
4. **弹出层**：基于 teleport，`append-to-body` 类问题大幅减少；MessageBox/Message 引入方式变为具名导出 `import { ElMessage } from 'element-plus'`（Vue2 时代是 `this.$message`——**没有 this 的 `<script setup>` 里必须具名导入**）。
5. **日期组件**：底层换 day.js，`value-format` 的 token 从 `yyyy-MM-dd` 改为 `YYYY-MM-DD`（大小写！迁移最阴的坑之一）。
6. **插槽语法**：`slot="xxx" slot-scope` 全面换 `#xxx="scope"`。
7. **类型**：Element Plus 全 TS，`FormInstance/FormRules/ListColumn` 这类类型可直接导入（本章标本随处可见）。
8. 全局配置从 `Vue.use(ElementUI, {size})` 变为 `app.use(ElementPlus, {...})` / `el-config-provider`。

### 6.12.2 表格页常见性能问题排查表

| 症状 | 病因 | 处方 |
|---|---|---|
| 首渲染慢 | 一次塞几千行 | 服务端分页（第一性解法） |
| 滚动卡 | 全量 DOM + 每格重组件 | el-table-v2 虚拟滚动；简化单元格插槽 |
| 输入卡 | 表格与表单同页，输入触发大表格重渲染 | 拆组件隔离响应式依赖；v-memo |
| 切语言/主题卡 | columns 等大 computed 全量重算+全表重渲染 | 正常代价，可接受；避免在 map 里做重计算（纯函数+轻量） |
| 内存涨 | 事件/连接/定时器不清理 | onUnmounted 成对清理（6.9 范式） |

### 6.12.3 "后端返回一万条怎么办"标准答案（三层递进）

1. **协议层（首选）**：不让它返回一万条——服务端分页+筛选+排序，`page/pageSize/filters/sort` 全部下沉后端（CP6 全系统如此，CpListPage 的 ListFetch 契约就是这个协议的类型化）。顺带把"合计"也要成后端聚合字段。
2. **渲染层（协议改不了时）**：一万条已经到手，用 el-table-v2 虚拟滚动只渲染视口；或前端自行切片假分页。
3. **交互层**：一万条本身就是需求错误——用户不会看一万行，引导"先筛后看"（CP6 的 lazy search-first 模式：不选条件不查询），导出类需求走后端生成文件而非前端渲染。

---

<a id="613"></a>
## 6.13 面试题 15 问（详细答案）

**1. Element Plus 和 ElementUI 什么关系？迁移要注意什么？**
同团队作品，Element Plus 是 Vue 3 + TS 重写版，组件命名和视觉延续。迁移六大点：v-model 协议（value/@input→modelValue，.sync 废除）、指令钩子改名（inserted→mounted）、图标改 SVG 组件包、Message 类 API 从 this.$xxx 改具名导入、日期 value-format 的 token 改大写 YYYY、插槽全面 `#` 语法。建议迁移顺序：先升 Vue3 兼容构建跑通，再替组件库，用 TS 类型和运行时警告收尾。

**2. 全量引入和按需引入怎么选？**
内网管理系统、迭代速度优先→全量（我们项目如此：main.ts `app.use(ElementPlus)` + 全量 CSS）；对首包敏感（公网/移动端）→ unplugin-vue-components 自动按需。补充细节：基建组件内部仍显式 import 所需组件，保证脱离全局注册可单测；按需时 v-loading 这类指令要单独引。

**3. 描述一个完整查询列表页的数据流。**
（用 6.2.3(6) 的链路图口述：筛选收集→搜索重置页码→load 加 seq、开 loading→fetch 翻译参数→axios 带 Cookie/参数→后端 Where+Skip/Take+Count→响应拦截器解包→seq 校验→写 rows/total→el-table 响应式重渲染→total-change 更新页头计数→关 loading。补一句失败分支：toast+保留旧数据。）

**4. 接口竞态（快慢请求乱序返回）怎么处理？**
自增请求序号：每次请求 `const id = ++seq`，响应回来 `if (id !== seq) return` 丢弃过期结果，loading 的关闭也要判 `id === seq`。或 AbortController 直接取消旧请求（更省流量）。我们模板组件用序号方案，一处实现全系统受益。

**5. el-table 自定义列渲染有哪几种方式？怎么选？**
①`prop` 直渲；②`:formatter` 回调（只能返回文本）；③`#default="{ row }"` 作用域插槽（任意 DOM/组件）；④我们模板层的 `map` 声明式映射（码值→{label, tone}，纯函数）。分工：只变文案用 formatter/map，要样式结构（负数红字、el-tag、按钮）用插槽。注意 map 必须纯函数——会被多次调用取 label 和 tone，带副作用会两者不一致。

**6. 服务端分页下，排序、筛选、合计有什么共同陷阱？**
数据不全在前端，任何客户端数据运算都只作用于当前页——排序只排 20 条、筛选只筛 20 条、合计只加 20 条，语义全错。统一解法：交互事件规范化后发给后端（sortable="custom"+@sort-change、筛选进查询参数、合计要后端聚合字段），并且条件变化一律重置 page=1。我们在类型层禁掉了 `sortable: true` 只允许 'custom'。

**7. el-form 的校验体系讲一遍。**
`:model` 绑数据、`:rules` 绑规则、el-form-item 的 `prop` 定位字段（漏 prop 规则不生效）、ref 拿 FormInstance。规则形态：required/min/max/pattern 内置 + `validator(rule, value, callback)` 自定义（闭包访问表单实现跨字段，如确认密码；callback(new Error) 报错）；同字段可叠多条规则；trigger 输入框 blur、选择器 change。validate() 返回 Promise，提交前 await，失败靠内联红字不再 toast。多语言下 rules 必须 computed 包 t()。

**8. 新增/编辑共用弹窗的完整模式？**
`form.id` 有无判定模式：标题 computed、主键字段 `:disabled="!!form.id"`、提交分流 create/update。打开前 Object.assign 全字段覆盖（编辑先铺默认值再 `...row` 防残留），行数据必须拷贝不能引用（否则输入直接改表格行）。提交范式：submitting 在 validate 前置位防双提交、按钮 :loading、校验失败不关窗、后端失败 toast+保持打开、成功 emit saved 让列表 reload。

**9. 删除确认怎么写最干净？**
`await ElMessageBox.confirm(带业务主键的消息, 标题, {type:'warning'})`——确定 resolve 取消 reject，所以 try 块里 confirm 之后直接写删除逻辑，catch 空体吞取消。成功后 ElMessage.success + 列表原地 reload（保留筛选页码）。按钮本身挂 v-permission，后端同键 RequirePermission 兜底。

**10. 手写按钮级权限控制的完整方案。**
（6.7.6 第一问答案 + 补充）三层递进：路由/菜单控页面可见、v-permission 控按钮、后端特性控真正的执行权。指令 mounted 里查 Pinia store 的 Set（登录后一次性拉取），无权移除 DOM；store 未就绪 fail-open 防误删。强调："前端藏按钮是体验，后端 403 才是安全——客户端一切检查都运行在攻击者可控环境里，必须假设会被绕过。"

**11. 为什么说前端隐藏不是安全边界？**
因为判定逻辑和数据都在客户端内存：DevTools 可改 store、可直接构造 HTTP 请求绕过整个前端。安全不变量只能在服务端维护——后端从会话身份重新解出角色权限判定每个请求。前端层的价值是把"注定失败的操作"从界面上拿掉，减少困惑与误触。两层用同一权限键保证口径一致。

**12. 多语言项目里最容易出的三个 bug？**
①表头/rules/选项等进了非响应式容器——t() 只算一次，切语言不更新；解法：一律 computed。②切语言时目标语言词条未加载就切 locale——裸 key 闪屏；解法：先 await 词条包再切。③组件库内置文案没接 el-config-provider——业务变了分页器没变。加分：伪本地化语言在开发期揪硬编码和布局溢出；缺 key 策略（te() 探测 + fallback + 错误码透传）。

**13. SignalR（或 WebSocket）在组件里使用的注意点？**
单例连接、自动重连退避、handler 具名引用 on/off 成对、onUnmounted 清理定时器、重连后重放分组订阅、notify-then-fetch + 轮询兜底、认证方式（Cookie 自动带 vs accessTokenFactory）。UI 侧用 ElNotification 轻提示 + el-badge 角标，面板打开才拉列表（懒加载）。

**14. 表格页要支持手机怎么做？**
（6.10.3 答案：@media 微调 vs useBreakpoint+v-if 双渲染的分界；桌面零回归靠桌面分支零改动；跨断点组件内部状态丢失要回填——讲 toggleRowSelection 那个真实 bug 最有说服力。）

**15. 你们前端目录怎么组织的？新页面的开发流程？**
（6.11.1 总纲 + 6.11.2 七步清单。收尾句：模板组件把 130+ 列表页收敛成"声明列、声明筛选、给个 fetch"三件事，正确性——竞态、分页重置、错误保留旧数据——沉在基建层只实现一次。）

---

<a id="614"></a>
## 6.14 自测清单

能全部打勾再进 Day 3：

- [ ] 能说清 Element Plus 与 ElementUI 的关系、全量/按需引入的取舍与写法
- [ ] 能画出 StockQueryView 从点击搜索到表格刷新的完整数据流（含拦截器、后端分页）
- [ ] 能默写 el-pagination 的双 v-model + 两个 change 事件，并解释哪些操作要重置 page=1
- [ ] 能解释 seq 竞态守卫解决什么问题、怎么工作
- [ ] 能写列插槽 `#default="{ row }"`，并说出 map/formatter 与插槽的分工线
- [ ] 能写 el-tag 状态映射函数（枚举→type 字面量联合类型）
- [ ] 能解释 `sortable="custom"` 为什么是服务端分页下唯一正确的排序
- [ ] 能手写：required 规则、自定义 validator（>0）、跨字段确认密码校验
- [ ] 能讲 CpFormDialog 的提交范式：submitting 前置、validate 驯化、失败不关窗
- [ ] 能写 ElMessageBox.confirm 的 try/await/catch 删除确认
- [ ] 能背 v-permission 全文（17 行）并讲三个设计决策（移除 vs 禁用、fail-open、无 updated）
- [ ] 能讲"前端藏按钮=体验、后端 403=安全"并举绕过手段
- [ ] 能说出 CP6 i18n 的四个工程点：空启动+懒加载命名空间、回退链、伪本地化、tOr 兜底
- [ ] 能讲 NotificationBell 的 SignalR 五要点（具名 handler/成对清理/notify-then-fetch/轮询兜底/ElNotification）
- [ ] 能讲 767px 改造的方案选型和跨断点选中态回填 bug
- [ ] 能不看资料说出 src 目录 11+2 个子目录的职责和新页面 7 步清单

---

<a id="615"></a>
## 6.15 动手练习 3 个

### 练习 1：给库存查询页加一个带权限控制的"导出 CSV"按钮（完整步骤，核心练习）

目标：`StockQueryView` 工具条上加"导出"按钮，有 `wms-stock:export` 权限才可见，点击按当前筛选条件导出。

1. **定权限键**：`wms-stock:export`（连字符命名、资源=菜单 MenuKey、操作=export）。
2. **后端两件事**（Day 1 知识回接）：`StockController` 加 `Export` 端点并贴 `[RequirePermission("wms-stock", "export")]`；权限种子表（MenuAction+RoleAction，逐租户）插入该键并授予目标角色——**先有后端强校验和种子，前端才有的判**。
3. **API 层**：`src/api/wms/stock.ts` 加方法：
   ```ts
   exportCsv(query: StockSearchQuery = {}) {
     return http.get<any, Blob>('/wms/stock/export', { params: query, responseType: 'blob' })
   }
   ```
4. **页面按钮**：CpListPage 的 `#toolbar` 插槽里（复选框旁）：
   ```vue
   <el-button v-permission="'wms-stock:export'" :loading="exporting" @click="onExport">
     {{ t('wms.stock.export') }}
   </el-button>
   ```
5. **导出函数**：复用当前筛选（注意 CpListPage 不外露 filters——这正是模板契约的真实约束，可以给页面自己维护一份最近查询参数，或者练习扩展 CpListPage 的 expose）：
   ```ts
   const exporting = ref(false)
   async function onExport() {
     exporting.value = true
     try {
       const blob = await stockApi.exportCsv(lastQuery.value)
       const url = URL.createObjectURL(blob)
       const a = document.createElement('a')
       a.href = url; a.download = `stock-${Date.now()}.csv`; a.click()
       URL.revokeObjectURL(url)
     } catch (e: any) {
       ElMessage.error(e?.message ?? 'Export failed')
     } finally { exporting.value = false }
   }
   ```
6. **i18n**：登记 `wms.stock.export` 五语言词条（wms 命名空间），跑 `npm run i18n:check`。
7. **验证清单**：有权账号见按钮、无权账号不见；用无权账号直接 curl 该端点必须 403（证明安全在后端）；切语言按钮文案变；导出中按钮 loading 防连点。

### 练习 2：把 WarehouseListView 的种别下拉改造成"远程搜索客户选择器"

用 `el-select filterable remote :remote-method` 接一个模糊查询 API，加 300ms 防抖、`:loading`、选中回显（注意 value 类型一致）、清空行为。进阶：换 `el-select-v2` 对比大数据量表现。

### 练习 3：给 NotificationBell 补"断线重连后状态自愈"

利用 `getConnection().onreconnected(cb)`：重连成功后立即 `refreshUnread()`（追平掉线期间的角标），并思考如果这个组件订阅了 wmsHub 的仓库分组，重连回调里还要补什么（重放 `subscribeWarehouse`）。顺手验证卸载清理：反复进出页面，在 DevTools 里确认 `WfNotification` 回调不会叠加触发。

---

## 附录：本章标本文件索引（面试前最后一晚快速重读清单）

| 标本 | 路径 | 本章讲了什么 |
|---|---|---|
| 主标本：库存查询页 | `C:\CP6\cp6.web\src\views\wms\StockQueryView.vue` | 列表页全解剖：列插槽/tag 映射/闭包筛选/QC 弹窗/履历弹窗/v-permission 消费 |
| 查询页模板 | `C:\CP6\cp6.web\src\components\templates\CpListPage.vue` | el-table/el-pagination 原生用法、seq 竞态守卫、页码重置铁律、defineExpose(reload) |
| 表单弹窗模板 | `C:\CP6\cp6.web\src\components\templates\CpFormDialog.vue` | validate() 驯化、防双提交双保险、失败不关窗契约 |
| CRUD 页（表单校验+删除确认） | `C:\CP6\cp6.web\src\views\wms\WarehouseListView.vue` | rules/one-dialog-two-modes/Object.assign 重置/ElMessageBox.confirm |
| 自定义 validator | `C:\CP6\cp6.web\src\views\wms\VmiView.vue:217`、`PaperRollView.vue:223` | validator 回调形态、规则叠加 |
| 跨字段校验 | `C:\CP6\cp6.web\src\views\pms\ChangePasswordView.vue` | 确认密码 validateConfirm、validate() 直接 await 风格 |
| 权限指令 | `C:\CP6\cp6.web\src\directives\permission.ts` | 17 行全文精读、三个设计决策 |
| 权限 store | `C:\CP6\cp6.web\src\stores\permission.ts` | Set 权限键、loadMyActions、fail-open 数据源 |
| 应用入口 | `C:\CP6\cp6.web\src\main.ts` | 全量引入、图标注册、指令注册、initI18n 先于挂载、权限预拉 |
| axios 实例 | `C:\CP6\cp6.web\src\api\http.ts` | Cookie 认证/CSRF 注入/401 刷新重放/错误码 i18n |
| API 层样例 | `C:\CP6\cp6.web\src\api\wms\stock.ts` | 端点函数+泛型响应类型 |
| i18n 核心 | `C:\CP6\cp6.web\src\i18n\index.ts` | 空启动/命名空间懒加载/回退链/伪本地化/changeLang |
| 缺 key 兜底 | `C:\CP6\cp6.web\src\i18n\tOr.ts` | te() 探测 + fallback + 错误码透传 |
| 格式化工具 | `C:\CP6\cp6.web\src\utils\format.ts` | d()/n() 与 Intl 双轨、useFormat 组合式 |
| SignalR 基座 | `C:\CP6\cp6.web\src\utils\signalr.ts` | 单例/重连退避/Cookie 认证 |
| WMS Hub | `C:\CP6\cp6.web\src\utils\wmsHub.ts` | 按域分连接、payload 类型化、分组订阅与重连限制 |
| 通知铃铛 | `C:\CP6\cp6.web\src\views\oa\notification\NotificationBell.vue` | on/off 成对、notify-then-fetch、轮询兜底、ElNotification/el-badge/el-popover |
| 移动端改造 | `C:\CP6\cp6.web\src\views\oa\inbox\InboxPending.vue` | v-if 双渲染、跨断点选中回填、@media 767px 批量条 |
| 断点组合式 | `C:\CP6\cp6.web\src\composables\useBreakpoint.ts` | 共享 ref + 监听引用计数 |
| 依赖清单 | `C:\CP6\cp6.web\package.json` | Vue 3.5 / Element Plus 2.13 / vue-i18n 11 / signalr 10 / Pinia 3 版本口径 |

---

## 章末寄语

这一章之后，你手里有了一条完整的垂直切面：**浏览器点击 → Element Plus 组件 → 模板组件契约 → API 层 → axios 拦截器 → 后端 Controller → EF Core → SQL，再原路返回渲染**。面试里无论从哪一层被提问，你都能向上向下各讲一层——这就是"5 年经验"的谈吐结构。Day 3 我们做整合演练：把三天的内容组装成项目叙述与自我介绍。
