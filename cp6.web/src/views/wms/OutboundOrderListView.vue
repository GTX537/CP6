<!--
  出庫指示一覧 —— CpListPage 模板首个真实消费者（Task 11 / Milestone B 试点）。
  结构：CpPageShell（标题 + actions：新建 / 桥接展开）→ CpListPage（搜索区 + 表格卡 + 分页）。
  数据源 outboundOrderApi.search 返回扁平数组且无 total，故 fetch 包装内做客户端分页（见 fetchList）。
-->
<template>
  <CpPageShell :title="t('wms.outbound.titleList')">
    <template #actions>
      <el-button @click="goCreate">{{ t('wms.common.create') }}</el-button>
      <el-button type="warning" @click="bridgeDialog = true">{{ t('wms.outbound.bridge.title') }}</el-button>
    </template>

    <CpListPage :columns="columns" :fetch="fetchList" :search-fields="searchFields" :filter-labels="filterLabels">
      <!-- 種別 -->
      <template #col-outboundType="{ row }">
        <CpTag :tone="row.outboundType === 1 ? 'info' : 'warn'">
          {{ typeMap[row.outboundType] || row.outboundType }}
        </CpTag>
      </template>
      <!-- 状態 -->
      <template #col-status="{ row }">
        <CpTag :tone="statusTone(row.status)">
          {{ statusMap[row.status] || row.status }}
        </CpTag>
      </template>
      <!-- 計画出庫日（切到 yyyy-MM-dd） -->
      <template #col-plannedDate="{ row }">{{ row.plannedDate?.slice(0, 10) }}</template>
      <!-- 優先度 -->
      <template #col-priority="{ row }">{{ priorityMap[row.priority] || row.priority }}</template>
      <!-- 操作 -->
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="goEdit(row)">{{ t('wms.common.open') }}</el-button>
      </template>
    </CpListPage>

    <!-- 桥接展开：MES 製造指図 / PA 受注 → 出庫指示（保留原对话框，CpListPage 无对应契约） -->
    <el-dialog v-model="bridgeDialog" :title="t('wms.outbound.bridge.title')" width="500">
      <el-form size="small" label-width="160px">
        <el-form-item :label="t('wms.outbound.bridge.fromWo')">
          <el-input v-model="bridgeWoNo" :placeholder="t('例: {sample}', { sample: 'WO20260522-0001' })">
            <template #append>
              <el-button type="primary" @click="onBridgeWo" :loading="bridging">{{ t('wms.common.expand') }}</el-button>
            </template>
          </el-input>
        </el-form-item>
        <el-divider />
        <el-form-item :label="t('wms.outbound.bridge.fromOrder')">
          <el-input v-model="bridgeOrderNo" :placeholder="t('例: {sample}', { sample: 'O20260522-0001' })">
            <template #append>
              <el-button type="primary" @click="onBridgeOrder" :loading="bridging">{{ t('wms.common.expand') }}</el-button>
            </template>
          </el-input>
        </el-form-item>
      </el-form>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, OutboundOrderSearchQuery } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

// —— 桥接展开对话框状态 ——
const bridgeDialog = ref(false)
const bridgeWoNo = ref('')
const bridgeOrderNo = ref('')
const bridging = ref(false)

// —— 码值映射（i18n 反应式） ——
const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.outbound.type.material'),
  2: t('wms.outbound.type.shipping'),
  3: t('wms.outbound.type.transfer'),
  9: t('wms.outbound.type.other'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.outbound.status.draft'),
  1: t('wms.outbound.status.confirmed'),
  2: t('wms.outbound.status.allocated'),
  3: t('wms.outbound.status.picking'),
  4: t('wms.outbound.status.completed'),
  9: t('wms.outbound.status.cancelled'),
}))
const priorityMap = computed<Record<number, string>>(() => ({
  1: t('wms.outbound.priority.normal'),
  2: t('wms.outbound.priority.urgent'),
  3: t('wms.outbound.priority.express'),
}))

// status 码 → CpTag 语义色调（沿用原 statusTagOf 意图，映射到设计系统 tone）
function statusTone(s: number): 'ok' | 'warn' | 'danger' | 'info' | 'muted' {
  return ({ 0: 'muted', 1: 'warn', 2: 'warn', 3: 'info', 4: 'ok', 9: 'muted' } as const)[s as 0] || 'muted'
}

// —— 列定义 ——（種別/状態/計画出庫日/優先度/操作 走具名列插槽） ——
const columns = computed<ListColumn[]>(() => [
  { prop: 'outboundNo', label: t('wms.outbound.fld.no'), kind: 'mono', width: 180 },
  { prop: 'outboundType', label: t('wms.common.type'), width: 90 },
  { prop: 'status', label: t('wms.common.status'), width: 110 },
  { prop: 'workOrderNo', label: t('wms.outbound.fld.workOrderNo'), width: 160 },
  { prop: 'webOrderNo', label: t('wms.outbound.fld.webOrderNo'), width: 160 },
  { prop: 'customerName', label: t('wms.outbound.fld.customerName') },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'plannedDate', label: t('wms.outbound.fld.plannedDate'), width: 110 },
  { prop: 'priority', label: t('wms.outbound.fld.priority'), width: 80 },
  { prop: '_action', label: t('wms.common.action'), width: 100 },
])

// —— CpFilterBar 按钮文案接 i18n（沿用现有词条；expand/collapse 无对应 key，留组件默认） ——
const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

// —— 搜索字段 ——（对应原 el-form 6 个查询项） ——
const searchFields = computed<FilterField[]>(() => [
  { key: 'outboundNo', label: t('wms.outbound.fld.no'), type: 'text' },
  {
    key: 'outboundType', label: t('wms.common.type'), type: 'select',
    options: Object.entries(typeMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  { key: 'workOrderNo', label: t('wms.outbound.fld.workOrderNo'), type: 'text' },
  { key: 'webOrderNo', label: t('wms.outbound.fld.webOrderNo'), type: 'text' },
  { key: 'customerCd', label: t('wms.outbound.fld.customerCd'), type: 'text' },
])

// —— 取数：包装 outboundOrderApi.search；后端返回扁平数组且无 total → 客户端分页 ——
const PAGE_CAP = 500
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: OutboundOrderSearchQuery = { pageSize: PAGE_CAP }
  if (f.outboundNo) q.outboundNo = String(f.outboundNo)
  if (f.outboundType !== undefined && f.outboundType !== '') q.outboundType = Number(f.outboundType)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  if (f.workOrderNo) q.workOrderNo = String(f.workOrderNo)
  if (f.webOrderNo) q.webOrderNo = String(f.webOrderNo)
  if (f.customerCd) q.customerCd = String(f.customerCd)
  const res = await outboundOrderApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 导航 / 桥接 ——
function goCreate() { router.push({ path: '/wms/outbound-order', query: { mode: 'new' } }) }
function goEdit(row: OutboundOrder) { router.push({ path: '/wms/outbound-order', query: { no: row.outboundNo } }) }

async function onBridgeWo() {
  if (!bridgeWoNo.value) return
  bridging.value = true
  try {
    const res = await outboundOrderApi.fromWorkOrder(bridgeWoNo.value)
    ElMessage.success(t('wms.outbound.bridge.expanded', { no: res.data.outboundNo }))
    bridgeDialog.value = false
    bridgeWoNo.value = ''
    router.push({ path: '/wms/outbound-order', query: { no: res.data.outboundNo } })
  } finally { bridging.value = false }
}

async function onBridgeOrder() {
  if (!bridgeOrderNo.value) return
  bridging.value = true
  try {
    const res = await outboundOrderApi.fromOrder(bridgeOrderNo.value)
    ElMessage.success(t('wms.outbound.bridge.expanded', { no: res.data.outboundNo }))
    bridgeDialog.value = false
    bridgeOrderNo.value = ''
    router.push({ path: '/wms/outbound-order', query: { no: res.data.outboundNo } })
  } finally { bridging.value = false }
}
</script>
