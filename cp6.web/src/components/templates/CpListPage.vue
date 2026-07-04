<!--
  CpListPage —— 业务查询页核心模板（设计系统 §9.2；130+ 查询页的目标模板）。
  组合结构：CpStatusStrip?（状态速览）→ CpFilterBar?（查询区）→ 表格卡 .tcard（toolbar slot + el-table + 分页）。
  行为：mounted 自动 fetch（lazy=true 时抑制，见下）；search/reset/翻页/切状态卡/列排序重新 fetch
  （search/reset/切卡/排序均重置 page=1）；fetch 期间 v-loading；fetch reject → ElMessage.error 且保留旧数据；
  rows 空 → CpEmpty；乱序响应只取最新。

  Props:
    - columns: ListColumn[]        列声明；kind 控制格式化（num→.num 右对齐 / mono→单号样式 / tag→CpTag /
                                   date→String(val).slice(0,10)，null/undefined 渲染空）。
                                   列级透传：width / minWidth→min-width / overflowTooltip→show-overflow-tooltip /
                                   fixed:'left'|'right'→fixed（钉列）。
                                   map?: (val,row)=>{ label, tone? }：码值列声明式映射——label 替换单元格文案（任意 kind 生效）；
                                   kind:'tag' 时按 tone 渲染 CpTag（tone 缺省 muted）。col-<prop> 插槽优先级高于 map。
                                   map 的 tone 仅在 kind:'tag' 时生效，其他 kind 忽略（仅 label 替换文本）。
                                   map 须为纯函数——每单元格可能被调用多次（label 与 tone 分别取值），含副作用或非确定性会导致两者不一致。
                                   sortable?: 'custom'：服务端排序列（仅接受 'custom'，不提供客户端排序语义）——
                                   透传 el-table-column sortable，点击表头触发 @sort-change：page 重置 1、
                                   sortField/sortOrder 并入 ListFetch query（order 规范化 ascending→'asc'/
                                   descending→'desc'；取消排序时两键从 query 移除）并 emit sort-change。
    - fetch: ListFetch             数据源：({ page,size,filters,statusKey?,sortField?,sortOrder? }) => Promise<{ rows,total }>。
                                   sortField/sortOrder 仅在存在有效排序时出现（未排序/取消排序则两键缺省）。
    - lazy?: boolean               默认 false。true = search-first 模式：mounted 不自动 fetch，表格以空态起步
                                   （total=0、CpEmpty 可见、loading=false，分页器因 total=0 自然惰性）；
                                   首查仅由显式手势触发——CpFilterBar search/reset、状态卡点击、exposed reload()、
                                   分页交互或列排序（均为用户显式意图）。首查成功前不 emit total-change。
                                   适配「先选必填条件再查询」的 ERP 形态（拠点必須・自動取得なし）。
    - searchFields?: FilterField[] 有值时渲染 CpFilterBar。
    - statusTabs?: StatusTab[]     有值时渲染 CpStatusStrip；初始 statusKey 取第一项 key；tone 用 CpTag 共享 Tone。
    - selectable?: boolean         勾选列；rowKey?: string 透传 el-table row-key。
    - highlightCurrentRow?: boolean 透传 el-table 当前行高亮；默认 true（迁移页默认保留原行为）。
    - paginated?: boolean          默认 true。false 时隐藏分页器、page 锁定 1，fetch 收到 size=UNPAGED_SIZE(1000)
                                   一次取全量（单表滚动 + 跨全量勾选形态，如賞味期限一括廃棄）。
    - filterLabels?: FilterBarLabels 透传 CpFilterBar 按钮文案覆盖（业务侧接 i18n；缺省中文）。
    - emptyText?: string           透传 CpEmpty 空状态文案（缺省「暂无数据」）。
  Slots: toolbar（批量操作区）｜ col-<prop>（自定义列，scope={row}）｜ expand（展开行，scope={row}）
  Emits: selection-change(rows) ｜ total-change(n)（每次成功加载后携带最新 total，供 CpPageShell :count 接线；
         受 seq 乱序守卫，过期响应不 emit）｜ sort-change({field,order})（排序状态变更时外发规范化值；
         取消排序时 field/order 均为 undefined）｜ reset()（CpFilterBar 重置时透传，**先于**重置触发的
         load() 同步 emit——页面级外部筛选状态（toolbar checkbox 等，缺口 #22）可在监听器内清理自身 ref，
         保证紧随其后的 fetch closure 读到的已是清理后的值）
  Expose: reload()（仅此一项）——命令式重新 fetch，保留当前 filters / page / statusKey（页内 in-place 变更后刷新，
          替代重挂载 :key 方案）。注意：删除当前页最后一行后 reload() 仍停留原 page，可能显示空页（不自动收拢页码，
          与原页 reload() 行为一致，记录在案）。

  使用示例：
    <CpPageShell title="出庫指示一覧" :count="total">
      <CpListPage
        :columns="[{ prop:'no', label:'单号', kind:'mono' }, { prop:'qty', label:'数量', kind:'num' },
                   { prop:'st', label:'状态', kind:'tag', map: (v) => ({ label: stLabel(v), tone: stTone(v) }) }]"
        :fetch="loadShipments"
        :search-fields="[{ key:'q', label:'单号', type:'text' }]"
        :status-tabs="[{ key:'all', label:'全部', count:28 }]"
        selectable row-key="id"
        @selection-change="onSel"
        @total-change="total = $event">
        <template #toolbar><el-button>批量出库</el-button></template>
      </CpListPage>
    </CpPageShell>
-->
<script lang="ts">
import type { Tone } from '@/components/base/CpTag.vue'

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
export type SortOrder = 'asc' | 'desc'
export type ListFetch = (q: {
  page: number
  size: number
  filters: Record<string, unknown>
  statusKey?: string
  sortField?: string // 仅在存在有效排序时出现（见 sortable:'custom'）
  sortOrder?: SortOrder
}) => Promise<{ rows: unknown[]; total: number }>
export interface StatusTab { key: string; label: string; count: number; tone?: Tone }
</script>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElPagination, ElTable, ElTableColumn, vLoading } from 'element-plus'
import CpStatusStrip from './CpStatusStrip.vue'
import CpFilterBar, { type FilterField, type FilterBarLabels } from './CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const props = withDefaults(defineProps<{
  columns: ListColumn[]
  fetch: ListFetch
  searchFields?: FilterField[]
  statusTabs?: StatusTab[]
  selectable?: boolean
  rowKey?: string
  highlightCurrentRow?: boolean
  paginated?: boolean
  lazy?: boolean
  filterLabels?: FilterBarLabels
  emptyText?: string
}>(), { highlightCurrentRow: true, paginated: true, lazy: false })

const emit = defineEmits<{
  (e: 'selection-change', rows: unknown[]): void
  (e: 'total-change', total: number): void
  (e: 'sort-change', payload: { field?: string; order?: SortOrder }): void
  (e: 'reset'): void
}>()

// —— 内部状态 ——
const page = ref(1)
const size = ref(20)
const filters = ref<Record<string, unknown>>({})
const statusKey = ref<string | undefined>(props.statusTabs?.[0]?.key)
const rows = ref<unknown[]>([])
const total = ref(0)
const loading = ref(false)
const sortField = ref<string | undefined>(undefined)
const sortOrder = ref<SortOrder | undefined>(undefined)

// paginated=false 时一次取数的 size 上限（无分页形态的全量口径；超过此量的数据源应保持分页）
const UNPAGED_SIZE = 1000

// —— load()：唯一取数入口；seq 守卫乱序响应（只有最新一次请求的结果生效）——
let seq = 0
async function load() {
  const id = ++seq
  loading.value = true
  try {
    const res = await props.fetch({
      page: props.paginated ? page.value : 1, // 无分页形态 page 锁定 1
      size: props.paginated ? size.value : UNPAGED_SIZE,
      filters: filters.value,
      statusKey: statusKey.value,
      // 仅在存在有效排序时并入两键（取消排序=两键均不出现，而非 undefined 占位）
      ...(sortField.value !== undefined
        ? { sortField: sortField.value, sortOrder: sortOrder.value }
        : {})
    })
    if (id !== seq) return // 已有更新的请求发出，丢弃本次结果
    rows.value = res.rows
    total.value = res.total
    emit('total-change', res.total) // 仅最新请求成功后 emit（供 CpPageShell :count 接线）
  } catch (e) {
    if (id !== seq) return
    ElMessage.error((e as Error)?.message ?? String(e)) // 保留旧 rows/total（与 CpFormDialog 同一错误硬化契约）
  } finally {
    if (id === seq) loading.value = false
  }
}
onMounted(() => { if (!props.lazy) load() }) // lazy=true：search-first，首查由显式手势触发（见头注）

// —— 命令式刷新：保留当前 filters / page / statusKey 重新 fetch（页内 in-place 变更后调用）——
function reload() { return load() }
defineExpose({ reload }) // 仅暴露 reload，内部状态不外露

// —— 交互 ——
function onSearch() { page.value = 1; load() }
// CpFilterBar 已先回写清空后的 filters；emit('reset') 同步于 load() 之前——
// 监听器（页面级 toolbar checkbox 等外部筛选，缺口 #22）先清自身 ref，随后的 fetch 才读到清理后的值
function onReset() { page.value = 1; emit('reset'); load() }
function onStatus(key: string) { statusKey.value = key; page.value = 1; load() }
function onPageChange() { load() }
function onSizeChange() { page.value = 1; load() }

// —— 服务端排序（#19）：el-table @sort-change → 规范化 → page=1 重新 fetch + emit ——
// lazy 列表从未加载过时排序同样触发首查（用户手势=显式意图）。
function onSortChange({ prop, order }: { prop: string; order: 'ascending' | 'descending' | null }) {
  sortField.value = order == null ? undefined : prop
  sortOrder.value = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : undefined
  page.value = 1
  emit('sort-change', { field: sortField.value, order: sortOrder.value })
  load()
}

// —— 列渲染 ——
function cell(row: unknown, prop: string): unknown {
  return (row as Record<string, unknown>)?.[prop]
}
function colAlign(c: ListColumn): 'left' | 'right' | 'center' {
  return c.align ?? (c.kind === 'num' ? 'right' : 'left')
}
// 单元格文案：map.label > date 截断(yyyy-MM-dd) > 原值
function display(c: ListColumn, row: unknown): unknown {
  const v = cell(row, c.prop)
  if (c.map) return c.map(v, row).label
  if (c.kind === 'date') return v == null ? '' : String(v).slice(0, 10)
  return v
}
function mapTone(c: ListColumn, row: unknown): Tone | undefined {
  return c.map?.(cell(row, c.prop), row).tone
}
</script>

<template>
  <div class="cp-list">
    <CpStatusStrip
      v-if="statusTabs?.length"
      :items="statusTabs"
      :model-value="statusKey ?? ''"
      @update:model-value="onStatus"
    />

    <CpFilterBar
      v-if="searchFields?.length"
      v-model="filters"
      :fields="searchFields"
      :labels="filterLabels"
      @search="onSearch"
      @reset="onReset"
    />

    <div class="tcard">
      <div v-if="$slots.toolbar" class="toolbar"><slot name="toolbar" /></div>

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
              <CpTag v-else-if="c.kind === 'tag'" :status="String(cell(row, c.prop) ?? '')" />
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
    </div>
  </div>
</template>

<style scoped>
.cp-list { display:flex; flex-direction:column; gap:16px; }

/* 表格卡（mockup-final-b .tcard） */
.tcard { background:var(--cp-card); border-radius:var(--cp-r-md); box-shadow:var(--cp-shadow-1);
  overflow:hidden; display:flex; flex-direction:column; }

/* 批量操作区（mockup-final-b .toolbar） */
.toolbar { display:flex; align-items:center; gap:8px; padding:12px 16px;
  border-bottom:1px solid var(--cp-line-soft); }

/* 分页行（mockup-final-b .pager；控件观感由 Element Plus overrides 保证） */
.pager { display:flex; align-items:center; justify-content:flex-end; gap:8px; padding:13px 16px;
  font-size:var(--cp-fs-sm); font-weight:700; color:var(--cp-muted); }

/* 单号样式（mockup-final-b .mono；暂无全局工具类，先在模板内定义） */
.cp-mono { font-weight:800; color:var(--cp-brand-deep); font-size:var(--cp-fs-sm); }
</style>
