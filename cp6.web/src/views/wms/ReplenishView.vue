<template>
  <div class="wms-replenish">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.replenish.fld.no')"><el-input v-model="query.replenishNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.product')"><el-input v-model="query.productCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.warehouseCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
          <el-button type="warning" @click="batchDialog = true">{{ t('wms.replenish.btn.genBatch') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="replenishNo" :label="t('wms.replenish.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="100">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.replenish.fld.priority')" width="90">
          <template #default="{ row }">
            <el-tag :type="row.priority === 1 ? 'danger' : 'info'" size="small">{{ priorityMap[row.priority] }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.replenish.fld.trigger')" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="row.triggerType === 'BATCH' ? 'success' : (row.triggerType === 'ALERT' ? 'warning' : 'info')">
              {{ triggerMap[row.triggerType] }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="fromLocationCd" :label="t('wms.replenish.fld.fromLoc')" width="140" />
        <el-table-column prop="toLocationCd" :label="t('wms.replenish.fld.toLoc')" width="140" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
        <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.qty) }}</template>
        </el-table-column>
        <el-table-column prop="executedAt" :label="t('wms.kit.fld.executedAt')" width="160">
          <template #default="{ row }">{{ row.executedAt?.replace('T', ' ').slice(0, 16) || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="160" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="success" size="small" @click="onExecute(row)">{{ t('wms.kit.btn.execute') }}</el-button>
            <el-button v-if="row.status === 0" link type="danger" size="small" @click="onCancel(row)">{{ t('wms.outbound.btn.cancel') }}</el-button>
            <span v-else style="color: #909399">—</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="batchDialog" :title="t('wms.replenish.dlg.batch')" width="500">
      <el-form :model="batchForm" label-width="160px" size="small">
        <el-form-item :label="t('wms.common.warehouse')" required>
          <el-input v-model="batchForm.warehouseCd" maxlength="10" />
        </el-form-item>
        <el-form-item :label="t('wms.replenish.fld.minQty')">
          <el-input-number v-model="batchForm.minQty" :min="1" :precision="2" controls-position="right" />
        </el-form-item>
        <el-alert type="info" :closable="false" :title="t('wms.replenish.msg.batchHint')" />
      </el-form>
      <template #footer>
        <el-button @click="batchDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onGenerateBatch" :loading="saving">{{ t('wms.replenish.btn.genBatch') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="dialogVisible" :title="t('wms.replenish.dlg.create')" width="500">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-form-item :label="t('wms.replenish.fld.priority')">
          <el-select v-model="editing.priority"><el-option :label="t('wms.replenish.priority.urgent')" :value="1" /><el-option :label="t('wms.replenish.priority.normal')" :value="2" /></el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.product')" required><el-input v-model="editing.productCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item>
        <el-form-item :label="t('wms.replenish.fld.fromLoc')" required><el-input v-model="editing.fromLocationCd" placeholder="RES-A-01" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.replenish.fld.toLoc')" required><el-input v-model="editing.toLocationCd" placeholder="PIK-A-01" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.common.lot')"><el-input v-model="editing.lotNo" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.common.qty')" required>
          <el-input-number v-model="editing.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { replenishApi } from '@/api/wms/logistics'
import type { ReplenishOrder, ReplenishSearchQuery } from '@/types/wms/wms'

const { t } = useI18n()
const query = reactive<ReplenishSearchQuery>({ pageSize: 100 })
const rows = ref<ReplenishOrder[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const batchDialog = ref(false)
const editing = ref<ReplenishOrder | null>(null)
const batchForm = reactive({ warehouseCd: '', minQty: 10 })
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.replenish.status.pending'),
  1: t('wms.replenish.status.executed'),
  9: t('wms.replenish.status.cancelled'),
}))
const priorityMap = computed<Record<number, string>>(() => ({
  1: t('wms.replenish.priority.urgent'),
  2: t('wms.replenish.priority.normal'),
}))
const triggerMap = computed<Record<string, string>>(() => ({
  BATCH: t('wms.replenish.trigger.batch'),
  MANUAL: t('wms.replenish.trigger.manual'),
  ALERT: t('wms.replenish.trigger.alert'),
}))

function statusTagOf(s: number): 'info' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reload() {
  loading.value = true
  try { rows.value = (await replenishApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    priority: 2, productCd: '', warehouseCd: '',
    fromLocationCd: '', toLocationCd: '', lotNo: '', qty: 0,
    triggerType: 'MANUAL', status: 0,
  }
  dialogVisible.value = true
}

async function onSave() {
  if (!editing.value) return
  saving.value = true
  try {
    const res = await replenishApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.replenishNo}`)
    dialogVisible.value = false
    await reload()
  } finally { saving.value = false }
}

async function onGenerateBatch() {
  if (!batchForm.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    const res = await replenishApi.generateBatch(batchForm.warehouseCd, batchForm.minQty)
    ElMessage.success(t('wms.replenish.msg.batchGen', { n: res.data.generated }))
    batchDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onExecute(row: ReplenishOrder) {
  try {
    await ElMessageBox.confirm(t('wms.replenish.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    await replenishApi.execute(row.replenishNo!)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function onCancel(row: ReplenishOrder) {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await replenishApi.cancel(row.replenishNo!)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-replenish { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
