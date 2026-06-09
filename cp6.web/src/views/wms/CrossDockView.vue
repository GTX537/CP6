<template>
  <div class="wms-xdock">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.xdock.fld.no')"><el-input v-model="query.xdockNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.product')"><el-input v-model="query.productCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="xdockNo" :label="t('wms.xdock.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.qty) }}</template>
        </el-table-column>
        <el-table-column prop="supplierCd" :label="t('wms.inbound.fld.supplierCd')" width="120" />
        <el-table-column prop="customerCd" :label="t('wms.outbound.fld.customerCd')" width="120" />
        <el-table-column prop="fromDock" :label="t('wms.xdock.fld.fromDock')" width="140" />
        <el-table-column prop="toDock" :label="t('wms.xdock.fld.toDock')" width="140" />
        <el-table-column prop="tempLocationCd" :label="t('wms.xdock.fld.tempLoc')" width="140" />
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

    <el-dialog v-model="dialogVisible" :title="t('wms.xdock.dlg.create')" width="560">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-form-item :label="t('wms.common.product')" required><el-input v-model="editing.productCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.common.productName')"><el-input v-model="editing.productName" maxlength="100" /></el-form-item>
        <el-form-item :label="t('wms.common.qty')" required>
          <el-input-number v-model="editing.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item>
        <el-form-item :label="t('wms.xdock.fld.tempLoc')" required><el-input v-model="editing.tempLocationCd" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.common.lot')"><el-input v-model="editing.lotNo" placeholder="auto: XD<date>-<seq>" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.xdock.fld.fromDock')"><el-input v-model="editing.fromDock" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.xdock.fld.toDock')"><el-input v-model="editing.toDock" maxlength="30" /></el-form-item>
        <el-form-item :label="t('wms.inbound.fld.supplierCd')"><el-input v-model="editing.supplierCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.outbound.fld.customerCd')"><el-input v-model="editing.customerCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item>
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
import { crossDockApi } from '@/api/wms/logistics'
import type { CrossDockOrder, CrossDockSearchQuery } from '@/types/wms/wms'

const { t } = useI18n()
const query = reactive<CrossDockSearchQuery>({ pageSize: 100 })
const rows = ref<CrossDockOrder[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editing = ref<CrossDockOrder | null>(null)
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.xdock.status.planned'),
  1: t('wms.xdock.status.executed'),
  9: t('wms.xdock.status.cancelled'),
}))

function statusTagOf(s: number): 'info' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reload() {
  loading.value = true
  try { rows.value = (await crossDockApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    productCd: '', qty: 0, warehouseCd: '', tempLocationCd: '', lotNo: '', status: 0,
  }
  dialogVisible.value = true
}

async function onSave() {
  if (!editing.value) return
  if (!editing.value.productCd || !editing.value.warehouseCd || !editing.value.tempLocationCd) {
    ElMessage.warning(t('wms.common.required')); return
  }
  saving.value = true
  try {
    const res = await crossDockApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.xdockNo}`)
    dialogVisible.value = false
    await reload()
  } finally { saving.value = false }
}

async function onExecute(row: CrossDockOrder) {
  try {
    await ElMessageBox.confirm(t('wms.xdock.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    await crossDockApi.execute(row.xdockNo!)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function onCancel(row: CrossDockOrder) {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await crossDockApi.cancel(row.xdockNo!)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-xdock { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
