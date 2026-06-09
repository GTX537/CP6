<template>
  <div class="wms-stock-take-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.stocktake.fld.no')"><el-input v-model="query.stockTakeNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.type')">
          <el-select v-model="query.stockTakeType" clearable style="width: 140px">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 140px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.targetWarehouseCd" clearable style="width: 130px" /></el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="planDialog = true">{{ t('wms.stocktake.btn.snapshot') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="stockTakeNo" :label="t('wms.stocktake.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.type')" width="100">
          <template #default="{ row }">{{ typeMap[row.stockTakeType] || row.stockTakeType }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.status')" width="130">
          <template #default="{ row }">
            <el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] || row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="targetWarehouseCd" :label="t('wms.common.warehouse')" width="100" />
        <el-table-column prop="targetLocationPrefix" :label="t('wms.common.location')" width="120" />
        <el-table-column prop="targetProductCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="plannedDate" :label="t('wms.stocktake.fld.plannedDate')" width="120">
          <template #default="{ row }">{{ row.plannedDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="actualDate" :label="t('wms.stocktake.fld.actualDate')" width="120">
          <template #default="{ row }">{{ row.actualDate?.slice(0, 10) || '—' }}</template>
        </el-table-column>
        <el-table-column prop="completedDate" :label="t('wms.stocktake.fld.completedDate')" width="120">
          <template #default="{ row }">{{ row.completedDate?.slice(0, 10) || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goDetail(row)">{{ t('wms.common.open') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="planDialog" :title="t('wms.stocktake.btn.snapshot')" width="540">
      <el-form :model="planForm" label-width="160px" size="small">
        <el-form-item :label="t('wms.common.type')">
          <el-select v-model="planForm.stockTakeType">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.stocktake.fld.plannedDate')">
          <el-date-picker v-model="planForm.plannedDate" type="date" value-format="YYYY-MM-DD" />
        </el-form-item>
        <el-form-item :label="t('wms.stocktake.fld.targetWh')" required>
          <el-input v-model="planForm.targetWarehouseCd" maxlength="10" />
        </el-form-item>
        <el-form-item :label="t('wms.stocktake.fld.targetLocPrefix')">
          <el-input v-model="planForm.targetLocationPrefix" placeholder="例: A-01-" maxlength="30" />
        </el-form-item>
        <el-form-item :label="t('wms.stocktake.fld.targetProduct')"><el-input v-model="planForm.targetProductCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.stocktake.fld.threshold')">
          <el-input-number v-model="planForm.approvalThresholdAmount" :min="0" :precision="2" controls-position="right" />
        </el-form-item>
        <el-form-item :label="t('wms.common.remarks')"><el-input v-model="planForm.remarks" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="planDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreatePlan" :loading="creating">{{ t('wms.common.create') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { stockTakeApi } from '@/api/wms/stockTake'
import type { StockTake, StockTakeSearchQuery, StockTakePlanRequest } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

const query = reactive<StockTakeSearchQuery>({ pageSize: 100 })
const rows = ref<StockTake[]>([])
const loading = ref(false)

const planDialog = ref(false)
const creating = ref(false)
const planForm = reactive<StockTakePlanRequest>({
  stockTakeType: 1,
  plannedDate: new Date().toISOString().slice(0, 10),
  targetWarehouseCd: '',
})

const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.stocktake.type.full'),
  2: t('wms.stocktake.type.cycle'),
  3: t('wms.stocktake.type.adhoc'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.stocktake.status.planned'),
  1: t('wms.stocktake.status.counting'),
  2: t('wms.stocktake.status.diffReview'),
  3: t('wms.stocktake.status.awaitingApproval'),
  4: t('wms.stocktake.status.completed'),
  9: t('wms.stocktake.status.cancelled'),
}))

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'danger' | 'success' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'danger', 4: 'success', 9: 'info' } as const)[s as 0] || 'info'
}

async function reload() {
  loading.value = true
  try {
    const res = await stockTakeApi.search(query)
    rows.value = res.data || []
  } finally { loading.value = false }
}

function goDetail(row: StockTake) {
  router.push({ path: '/wms/stock-take', query: { no: row.stockTakeNo } })
}

async function onCreatePlan() {
  if (!planForm.targetWarehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  creating.value = true
  try {
    const res = await stockTakeApi.createPlan(planForm)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.stockTakeNo}`)
    planDialog.value = false
    router.push({ path: '/wms/stock-take', query: { no: res.data.stockTakeNo } })
  } finally { creating.value = false }
}

onMounted(reload)
</script>

<style scoped>
.wms-stock-take-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
