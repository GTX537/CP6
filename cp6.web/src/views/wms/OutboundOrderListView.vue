<template>
  <div class="wms-outbound-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.outbound.fld.no')"><el-input v-model="query.outboundNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.type')">
          <el-select v-model="query.outboundType" clearable style="width: 130px">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 140px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.outbound.fld.workOrderNo')"><el-input v-model="query.workOrderNo" clearable style="width: 160px" /></el-form-item>
        <el-form-item :label="t('wms.outbound.fld.webOrderNo')"><el-input v-model="query.webOrderNo" clearable style="width: 160px" /></el-form-item>
        <el-form-item :label="t('wms.outbound.fld.customerCd')"><el-input v-model="query.customerCd" clearable style="width: 130px" /></el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="goCreate">{{ t('wms.common.create') }}</el-button>
          <el-button type="warning" @click="bridgeDialog = true">{{ t('wms.outbound.bridge.title') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="outboundNo" :label="t('wms.outbound.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.type')" width="90">
          <template #default="{ row }">
            <el-tag :type="row.outboundType === 1 ? 'info' : 'warning'" size="small">{{ typeMap[row.outboundType] || row.outboundType }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }">
            <el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] || row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="workOrderNo" :label="t('wms.outbound.fld.workOrderNo')" width="160" />
        <el-table-column prop="webOrderNo" :label="t('wms.outbound.fld.webOrderNo')" width="160" />
        <el-table-column prop="customerName" :label="t('wms.outbound.fld.customerName')" min-width="160" show-overflow-tooltip />
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="80" />
        <el-table-column prop="plannedDate" :label="t('wms.outbound.fld.plannedDate')" width="110">
          <template #default="{ row }">{{ row.plannedDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.outbound.fld.priority')" width="80">
          <template #default="{ row }">{{ priorityMap[row.priority] || row.priority }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goEdit(row)">{{ t('wms.common.open') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, OutboundOrderSearchQuery } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

const query = reactive<OutboundOrderSearchQuery>({ pageSize: 100 })
const rows = ref<OutboundOrder[]>([])
const loading = ref(false)

const bridgeDialog = ref(false)
const bridgeWoNo = ref('')
const bridgeOrderNo = ref('')
const bridging = ref(false)

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

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'warning', 4: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}

async function reload() {
  loading.value = true
  try {
    const res = await outboundOrderApi.search(query)
    rows.value = res.data || []
  } finally { loading.value = false }
}

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

onMounted(reload)
</script>

<style scoped>
.wms-outbound-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
