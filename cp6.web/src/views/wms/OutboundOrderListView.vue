<!--
  出庫指示一覧 —— CpListPage 模板首个真实消费者（Task 11 / Milestone B 试点；Milestone C 契约扩展回填）。
  结构：CpPageShell（标题 + 计数 pill(:count←@total-change) + actions：新建 / 桥接展开）→ CpListPage。
  码值列（種別/状態/優先度）与日期列走 ListColumn map / kind:'date' 声明式映射（不再用 col-<prop> 插槽）；
  優先度沿用原纯文本呈现（无 tone），故只用 map 不设 kind:'tag'。
  数据源 outboundOrderApi.search 返回扁平数组且无 total，故 fetch 包装内做客户端分页（见 fetchList）。
-->
<template>
  <CpPageShell :title="t('wms.outbound.titleList')" :count="total">
    <template #actions>
      <el-button @click="goCreate">{{ t('wms.common.create') }}</el-button>
      <el-button type="warning" @click="bridgeDialog = true">{{ t('wms.outbound.bridge.title') }}</el-button>
    </template>

    <CpListPage
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
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
import { type Tone } from '@/components/base/CpTag.vue'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, OutboundOrderSearchQuery } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

// —— 头部计数 pill：CpListPage @total-change 回填 ——
const total = ref<number>()

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

// status 码 → CpTag 语义色调（沿用原 statusTagOf 意图，映射到设计系统共享 Tone）
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'warn', 2: 'warn', 3: 'info', 4: 'ok', 9: 'muted' } as const)[s as 0] || 'muted'
}

// 码值 → 文案；未命中回退原值（null/undefined → 空，与原插槽插值行为一致）
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

// —— 列定义 ——（種別/状態/優先度 走 map 声明式映射；計画出庫日 走 kind:'date'；操作 走具名插槽） ——
const columns = computed<ListColumn[]>(() => [
  { prop: 'outboundNo', label: t('wms.outbound.fld.no'), kind: 'mono', width: 180 },
  { prop: 'outboundType', label: t('wms.common.type'), width: 90, kind: 'tag',
    map: (v) => ({ label: codeLabel(typeMap.value, v), tone: v === 1 ? 'info' : 'warn' }) },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'workOrderNo', label: t('wms.outbound.fld.workOrderNo'), width: 160 },
  { prop: 'webOrderNo', label: t('wms.outbound.fld.webOrderNo'), width: 160 },
  { prop: 'customerName', label: t('wms.outbound.fld.customerName'), minWidth: 160, overflowTooltip: true },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'plannedDate', label: t('wms.outbound.fld.plannedDate'), width: 110, kind: 'date' },
  { prop: 'priority', label: t('wms.outbound.fld.priority'), width: 80,
    map: (v) => ({ label: codeLabel(priorityMap.value, v) }) }, // 原页即纯文本，无 tone → 不设 kind:'tag'
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
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
