<!--
  入庫指示一覧 —— CpPageShell + CpListPage 迁移（WMS 批次1）。
  状態列走 kind:'tag'+map（码→i18n+tone）；種別列纯 map（原页无 tag 视觉）；日期列 kind:'date'；操作列 col slot。
  原「予定入荷 从/至」两个单日期查询合并为 CpFilterBar daterange（filterbar 无单日 type）；
  daterange 返回 Date 对象（filterbar 未透传 value-format），故 fetch 内本地格式化为 YYYY-MM-DD（模板缺口 #9）。
  数据源 inboundOrderApi.search 返回扁平数组无 total → 客户端分页（同样板）。
-->
<template>
  <CpPageShell :title="t('wms.inbound.titleList')" :count="total">
    <template #actions>
      <el-button @click="goCreate">{{ t('wms.common.create') }}</el-button>
    </template>

    <CpListPage
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="goEdit(row)">{{ t('wms.common.open') }}</el-button>
        <el-button link type="success" size="small" @click="goReceive(row)">{{ t('wms.inbound.btn.receipt') }}</el-button>
      </template>
    </CpListPage>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { inboundOrderApi } from '@/api/wms/inboundOrder'
import type { InboundOrder, InboundOrderSearchQuery } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

const total = ref<number>()

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.inbound.status.draft'),
  1: t('wms.inbound.status.confirmed'),
  2: t('wms.inbound.status.partial'),
  3: t('wms.inbound.status.completed'),
  9: t('wms.inbound.status.cancelled'),
}))
const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.inbound.type.purchase'),
  2: t('wms.inbound.type.rework'),
  3: t('wms.inbound.type.return'),
  9: t('wms.inbound.type.other'),
}))

// 沿用原 statusTagOf（info/primary/warning/success/danger）意图 → 设计系统共享 Tone
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'info', 2: 'warn', 3: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'inboundNo', label: t('wms.inbound.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 120, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'inboundType', label: t('wms.common.type'), width: 120,
    map: (v) => ({ label: codeLabel(typeMap.value, v) }) },
  { prop: 'supplierName', label: t('wms.inbound.fld.supplierName'), minWidth: 180, overflowTooltip: true },
  { prop: 'poNo', label: t('wms.inbound.fld.poNo'), width: 140 },
  { prop: 'expectedArrivalDate', label: t('wms.inbound.fld.expectedArrival'), width: 120, kind: 'date' },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'createDate', label: t('wms.common.createDate'), width: 120, kind: 'date' },
  { prop: '_action', label: t('wms.common.action'), width: 160, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'inboundNo', label: t('wms.inbound.fld.no'), type: 'text' },
  { key: 'supplierCd', label: t('wms.inbound.fld.supplierCd'), type: 'text' },
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  { key: 'arrival', label: `${t('wms.inbound.fld.expectedArrival')}`, type: 'daterange' },
])

// Date → YYYY-MM-DD（本地时区，避免 toISOString UTC 偏移）
function ymd(d: unknown): string | undefined {
  if (!d) return undefined
  const dt = d instanceof Date ? d : new Date(String(d))
  if (Number.isNaN(dt.getTime())) return undefined
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`
}

const PAGE_CAP = 500
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: InboundOrderSearchQuery = { pageSize: PAGE_CAP }
  if (f.inboundNo) q.inboundNo = String(f.inboundNo)
  if (f.supplierCd) q.supplierCd = String(f.supplierCd)
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  if (Array.isArray(f.arrival)) {
    q.arrivalFrom = ymd(f.arrival[0])
    q.arrivalTo = ymd(f.arrival[1])
  }
  const res = await inboundOrderApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

function goCreate() { router.push({ path: '/wms/inbound-order', query: { mode: 'new' } }) }
function goEdit(row: InboundOrder) { router.push({ path: '/wms/inbound-order', query: { no: row.inboundNo } }) }
function goReceive(row: InboundOrder) { router.push({ path: '/wms/inbound-receipt', query: { inboundNo: row.inboundNo } }) }
</script>
