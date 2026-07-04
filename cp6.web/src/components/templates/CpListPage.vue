<!--
  CpListPage —— 业务查询页核心模板（设计系统 §9.2；130+ 查询页的目标模板）。
  组合结构：CpStatusStrip?（状态速览）→ CpFilterBar?（查询区）→ 表格卡 .tcard（toolbar slot + el-table + 分页）。
  行为：mounted 自动 fetch；search/reset/翻页/切状态卡重新 fetch（search/reset/切卡均重置 page=1）；
  fetch 期间 v-loading；fetch reject → ElMessage.error 且保留旧数据；rows 空 → CpEmpty；乱序响应只取最新。

  Props:
    - columns: ListColumn[]        列声明；kind 控制格式化（num→.num 右对齐 / mono→单号样式 / tag→CpTag / date→暂原样）。
    - fetch: ListFetch             数据源：({ page,size,filters,statusKey? }) => Promise<{ rows,total }>。
    - searchFields?: FilterField[] 有值时渲染 CpFilterBar。
    - statusTabs?: StatusTab[]     有值时渲染 CpStatusStrip；初始 statusKey 取第一项 key。
    - selectable?: boolean         勾选列；rowKey?: string 透传 el-table row-key。
  Slots: toolbar（批量操作区）｜ col-<prop>（自定义列，scope={row}）｜ expand（展开行，scope={row}）
  Emits: selection-change(rows)

  使用示例：
    <CpPageShell title="出庫指示一覧" :count="total">
      <CpListPage
        :columns="[{ prop:'no', label:'单号', kind:'mono' }, { prop:'qty', label:'数量', kind:'num' }]"
        :fetch="loadShipments"
        :search-fields="[{ key:'q', label:'单号', type:'text' }]"
        :status-tabs="[{ key:'all', label:'全部', count:28 }]"
        selectable row-key="id"
        @selection-change="onSel">
        <template #toolbar><el-button>批量出库</el-button></template>
      </CpListPage>
    </CpPageShell>
-->
<script lang="ts">
export interface ListColumn {
  prop: string
  label: string
  width?: number
  align?: 'left' | 'right' | 'center'
  kind?: 'text' | 'num' | 'mono' | 'tag' | 'date'
}
export type ListFetch = (q: {
  page: number
  size: number
  filters: Record<string, unknown>
  statusKey?: string
}) => Promise<{ rows: unknown[]; total: number }>
export interface StatusTab { key: string; label: string; count: number; tone?: string }
</script>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElPagination, ElTable, ElTableColumn, vLoading } from 'element-plus'
import CpStatusStrip from './CpStatusStrip.vue'
import CpFilterBar, { type FilterField } from './CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const props = defineProps<{
  columns: ListColumn[]
  fetch: ListFetch
  searchFields?: FilterField[]
  statusTabs?: StatusTab[]
  selectable?: boolean
  rowKey?: string
}>()

const emit = defineEmits<{ (e: 'selection-change', rows: unknown[]): void }>()

// —— 内部状态 ——
const page = ref(1)
const size = ref(20)
const filters = ref<Record<string, unknown>>({})
const statusKey = ref<string | undefined>(props.statusTabs?.[0]?.key)
const rows = ref<unknown[]>([])
const total = ref(0)
const loading = ref(false)

// —— load()：唯一取数入口；seq 守卫乱序响应（只有最新一次请求的结果生效）——
let seq = 0
async function load() {
  const id = ++seq
  loading.value = true
  try {
    const res = await props.fetch({
      page: page.value,
      size: size.value,
      filters: filters.value,
      statusKey: statusKey.value
    })
    if (id !== seq) return // 已有更新的请求发出，丢弃本次结果
    rows.value = res.rows
    total.value = res.total
  } catch (e) {
    if (id !== seq) return
    ElMessage.error((e as Error).message) // 保留旧 rows/total
  } finally {
    if (id === seq) loading.value = false
  }
}
onMounted(load)

// —— 交互 ——
function onSearch() { page.value = 1; load() }
function onReset() { page.value = 1; load() } // CpFilterBar 已先回写清空后的 filters
function onStatus(key: string) { statusKey.value = key; page.value = 1; load() }
function onPageChange() { load() }
function onSizeChange() { page.value = 1; load() }

// —— 列渲染 ——
function cell(row: unknown, prop: string): unknown {
  return (row as Record<string, unknown>)?.[prop]
}
function colAlign(c: ListColumn): 'left' | 'right' | 'center' {
  return c.align ?? (c.kind === 'num' ? 'right' : 'left')
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
      @search="onSearch"
      @reset="onReset"
    />

    <div class="tcard">
      <div v-if="$slots.toolbar" class="toolbar"><slot name="toolbar" /></div>

      <el-table
        v-loading="loading"
        :data="rows"
        :row-key="rowKey"
        @selection-change="emit('selection-change', $event)"
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
          :align="colAlign(c)"
        >
          <template #default="{ row }">
            <slot :name="`col-${c.prop}`" :row="row">
              <CpTag v-if="c.kind === 'tag'" :status="String(cell(row, c.prop) ?? '')" />
              <span v-else-if="c.kind === 'mono'" class="cp-mono">{{ cell(row, c.prop) }}</span>
              <span v-else-if="c.kind === 'num'" class="num">{{ cell(row, c.prop) }}</span>
              <template v-else>{{ cell(row, c.prop) }}</template>
            </slot>
          </template>
        </el-table-column>

        <template #empty>
          <CpEmpty v-if="!loading" />
        </template>
      </el-table>

      <div class="pager">
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
