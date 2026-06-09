<template>
  <div class="wms-inbound-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.inbound.fld.no')"><el-input v-model="query.inboundNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.inbound.fld.supplierCd')"><el-input v-model="query.supplierCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.warehouseCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 140px">
            <el-option v-for="(label, val) in statusMap" :key="val" :label="label" :value="Number(val)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="`${t('wms.inbound.fld.expectedArrival')} ${t('wms.common.from')}`">
          <el-date-picker v-model="query.arrivalFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('wms.common.to')">
          <el-date-picker v-model="query.arrivalTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="goCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="inboundNo" :label="t('wms.inbound.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="120">
          <template #default="{ row }">
            <el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] || row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.type')" width="120">
          <template #default="{ row }">{{ typeMap[row.inboundType] || row.inboundType }}</template>
        </el-table-column>
        <el-table-column prop="supplierName" :label="t('wms.inbound.fld.supplierName')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="poNo" :label="t('wms.inbound.fld.poNo')" width="140" />
        <el-table-column prop="expectedArrivalDate" :label="t('wms.inbound.fld.expectedArrival')" width="120">
          <template #default="{ row }">{{ row.expectedArrivalDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="80" />
        <el-table-column prop="createDate" :label="t('wms.common.createDate')" width="120">
          <template #default="{ row }">{{ row.createDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="160" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goEdit(row)">{{ t('wms.common.open') }}</el-button>
            <el-button link type="success" size="small" @click="goReceive(row)">{{ t('wms.inbound.btn.receipt') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { inboundOrderApi } from '@/api/wms/inboundOrder'
import type { InboundOrder, InboundOrderSearchQuery } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

const query = reactive<InboundOrderSearchQuery>({ pageSize: 100 })
const rows = ref<InboundOrder[]>([])
const loading = ref(false)

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

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}

async function reload() {
  loading.value = true
  try {
    const res = await inboundOrderApi.search(query)
    rows.value = res.data || []
  } finally { loading.value = false }
}

function goCreate() { router.push({ path: '/wms/inbound-order', query: { mode: 'new' } }) }
function goEdit(row: InboundOrder) { router.push({ path: '/wms/inbound-order', query: { no: row.inboundNo } }) }
function goReceive(row: InboundOrder) { router.push({ path: '/wms/inbound-receipt', query: { inboundNo: row.inboundNo } }) }

onMounted(reload)
</script>

<style scoped>
.wms-inbound-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
