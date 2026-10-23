<template>
  <CpPageShell :title="t('wms.expiry.title')" :count="result?.total">
    <p>观察有到期日期且数量大于零的可见库存，包含已过期库存。所选日期用于观察，不代表仓库批准日期。</p>
    <p>单价与数量乘积仅供参考，币种与财务估值尚未核定。报废处置暂不可用。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="观察日期"><el-date-picker v-model="query.asOfDate" type="date" value-format="YYYY-MM-DD" /></el-form-item>
      <el-form-item :label="t('wms.expiry.fld.daysWithin')"><el-input-number v-model="query.days" :min="0" :max="365" :precision="0" /></el-form-item>
      <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.warehouseCd" clearable /></el-form-item>
      <el-button type="primary" :loading="loading" :disabled="!permitted()" @click="search">{{ t('wms.common.search') }}</el-button>
    </el-form>
    <p v-if="readError" role="alert">{{ readError }}</p>
    <p v-if="result">观察日期 {{ result.asOfDate.slice(0, 10) }} · {{ result.days }} 日内 · {{ result.total }} 条</p>
    <el-table :data="result?.rows ?? []" row-key="stockId" border v-loading="loading">
      <el-table-column prop="productCd" :label="t('wms.common.product')" min-width="120" />
      <el-table-column prop="lotNo" :label="t('wms.common.lot')" min-width="120" />
      <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" />
      <el-table-column prop="locationCd" :label="t('wms.common.location')" />
      <el-table-column :label="t('wms.stock.col.physical')" min-width="180" align="right"><template #default="{ row }">{{ observationText(row.physicalQty) }}</template></el-table-column>
      <el-table-column label="到期日期" min-width="110"><template #default="{ row }">{{ row.expiryDate.slice(0, 10) }}</template></el-table-column>
      <el-table-column prop="daysUntilExpiry" :label="t('wms.expiry.col.daysLeft')" />
      <el-table-column label="存储单价（币种未知）" min-width="180"><template #default="{ row }">{{ observationText(row.unitPrice) }}</template></el-table-column>
      <el-table-column label="单价参考算术（未核币种）" min-width="220"><template #default="{ row }">{{ observationText(row.referenceValue) }}</template></el-table-column>
    </el-table>
    <el-pagination v-if="result" :current-page="result.page" :page-size="result.pageSize" :total="result.total"
      layout="total, prev, pager, next" @current-change="pageChanged" />
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { expiryApi } from '@/api/wms/expiry'
import type { ExpiryObservation, ExpiryObservationQuery } from '@/types/wms/inventoryObservation'
import { inventoryObservationVersion, observationText, observationToday } from '@/utils/inventoryObservation'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n(), permission = usePermissionStore()
const query = reactive<ExpiryObservationQuery>({ asOfDate: observationToday(), days: 30, page: 1, pageSize: 50 })
const result = ref<ExpiryObservation | null>(null), loading = ref(false), readError = ref('')
let sequence = 0, applied: ExpiryObservationQuery | null = null
function clearProtected() { sequence++; result.value = null; applied = null; loading.value = false }
const intent = useOperationsIntent(() => { clearProtected(); readError.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-expiry:view') && permission.has('wms-stock:query')
watch(permitted, allowed => { if (!allowed) clearProtected() })
async function load(input: ExpiryObservationQuery) {
  const token = intent.capture(), read = ++sequence
  const current = () => intent.current(token) && read === sequence
  result.value = null; loading.value = true; readError.value = ''
  try {
    if (!permitted() || !input.asOfDate) throw new Error('Current view and explicit observation date required')
    const res = await expiryApi.observation({ ...input })
    if (!current()) return
    if (!permitted() || res?.code !== 0 || res.data?.version !== inventoryObservationVersion) throw new Error('Current observation unavailable')
    result.value = res.data; applied = { ...input }
  } catch {
    if (current()) { result.value = null; applied = null; readError.value = '读取失败，请重新查询当前权限范围。' }
  } finally { if (current()) loading.value = false }
}
function search() { void load({ ...query, warehouseCd: query.warehouseCd?.trim() || undefined, page: 1 }) }
function pageChanged(page: number) { if (applied) void load({ ...applied, page }) }
onMounted(search)
</script>
