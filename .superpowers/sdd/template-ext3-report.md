# 模板契约扩展三轮报告 —— CpListPage lazy + sortable:'custom'

日期：2026-07-04 ｜ 分支：feat/ui-migrate-erp ｜ 规格：docs/superpowers/plans/2026-07-04-ui-restyle.md「ERP批次1 复盘」缺口 #18/#19

## 范围

仅 `cp6.web/src/components/templates/CpListPage.vue` + 其 spec + 计划文档缺口标记。无页面 retrofit（本轮无 ERP 页消费者，下一批迁移 dogfood）；纯 token/契约层，未触任何后端或 API。

## #18 lazy search-first 模式

- `lazy?: boolean` prop，默认 false（withDefaults 显式 `lazy: false`）——非 lazy 路径行为完全不变（既有 27 个测试未改动、全绿）。
- `lazy=true`：`onMounted(() => { if (!props.lazy) load() })` 抑制自动 fetch；表格空态起步（rows=[]、total=0、loading=false → CpEmpty 可见、分页器因 total=0 自然惰性，未做隐藏/禁用特判）。
- 首查触发面 = 全部既有显式手势：CpFilterBar search/reset、状态卡点击、exposed `reload()`、分页交互、列排序（sort-change 视为显式意图，lazy 未加载也触发首查）。实现上 lazy 只改 onMounted 一处——所有手势本就走唯一取数入口 `load()`，零分叉。
- `@total-change` 首查成功前不 emit（原有「仅成功后 emit + seq 守卫」逻辑天然满足，无需额外代码）。

## #19 sortable:'custom' 服务端排序透传

- `ListColumn.sortable?: 'custom'`——类型上**仅接受 'custom'**，不放开 `true`，避免混入 el-table 客户端排序语义（服务端分页下客户端排序只排当前页，属语义陷阱）。
- 模板 `:sortable="c.sortable ?? false"` 透传 el-table-column；未声明列保持不可排序。
- el-table `@sort-change` → `onSortChange`：
  - 规范化：`'ascending'→'asc'`、`'descending'→'desc'`、`null→undefined`（order 为 null 时 sortField 亦置 undefined）。
  - `page=1` 重置 → `emit('sort-change', {field, order})` → `load()`（走 seq 乱序守卫）。
  - query 合并：条件展开 `...(sortField !== undefined ? {sortField, sortOrder} : {})`——未排序/取消排序时**两键在 query 对象中不存在**（非 undefined 占位），消费端可直接展开进请求参数。
- `ListFetch` 类型扩展 `sortField?: string; sortOrder?: SortOrder`；新导出 `export type SortOrder = 'asc' | 'desc'`。
- 排序状态在后续翻页/搜索 fetch 中保持（state 驻留，测试覆盖翻页携带 desc）。

## TDD 过程

1. 先写 11 个新测试（两个 describe 块：`契约扩展三轮：#18 lazy` ×5、`#19 sortable:'custom'` ×6），实现前运行：9 failed / 29 passed（两个「键不存在」断言在旧实现下平凡通过，属预期——绝对缺失断言无法在未实现态失败）。
2. 实现后单文件 38/38 绿。
3. 全量 `npm run test`：**46 files / 315 passed**（基线 304 + 11 新，0 失败，输出 pristine）。
4. `npm run type-check`（vue-tsc --build）：0 error。

新测试清单：
- lazy=true mounted 不 fetch + CpEmpty 可见 + loading 无遮罩 + total-change 未 emit
- lazy 首查由 search 触发且 fetch 恰好一次（page=1）、成功后 total-change=[[1]]
- lazy 首查由状态卡触发（statusKey 正确）
- lazy 首查由 reload() 触发
- lazy + sort-change 触发首查（携带 sortField/sortOrder）
- sortable:'custom' 透传 el-table-column（未声明列为 false）
- sort-change → fetch 收到 sortField:'no'/sortOrder:'asc' 且 page 由 2 重置为 1
- descending→'desc' 且后续翻页 fetch 保持排序键
- 取消排序（order:null）→ query 两键 `in` 断言均 false
- 未排序默认 fetch → query 两键不存在
- sort-change 事件外发 {field,order}，取消时两者 undefined

## 文档

- CpListPage 头注：行为行补 lazy/排序；Props 补 `lazy` 与 `sortable:'custom'` 全语义（含取消排序键移除、lazy 首查触发面）；ListFetch 签名更新；Emits 补 sort-change。
- 计划文档缺口 #18/#19 已标 ✅ 已实现 + 一行摘要。

## 决策记录 / 注意点

- **emit sort-change 的取消形态**：取消排序时外发 `{field: undefined, order: undefined}`（与 query 键移除对齐，field 不保留原列名）——消费页据 `order === undefined` 判「无排序」。
- lazy 模式下 **reset 也会触发首查**（reset 属 CpFilterBar 显式手势，与 search 同一 load() 通道；未做「首查前 reset 静默」特判——简单正确优先，如后续批次出现反例再收紧契约）。
- 分页器在 lazy 未加载时保持渲染（total=0 自然惰性），与控制器决策一致。
- 无 retrofit、无真栈 pass（本轮契约+单测即验收口径；下一 ERP 批次实弹 dogfood FscChecklist/OrderPriceCorrection（lazy）与 BusinessPartnerList（sortable））。
