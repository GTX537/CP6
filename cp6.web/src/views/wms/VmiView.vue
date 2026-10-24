<template>
  <CpPageShell title="VMI 库存与历史估算" :count="result?.total">
    <p>库存按客户、单位分别显示当前可见数量。可用于指定作业的数量和库存估值尚未评估。历史账单是时点估算，旧确认标记不代表已送付或财务入账。新计费和确认暂不可用。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="查看"><el-select v-model="mode"><el-option label="客户库存" value="customers" /><el-option label="库存明细" value="stock" /><el-option label="历史账单" value="billings" /></el-select></el-form-item>
      <el-form-item label="客户代码"><el-input v-model="query.customerCd" clearable /></el-form-item>
      <template v-if="mode === 'billings'">
        <el-form-item label="账单年月"><el-input v-model="query.yearMonth" placeholder="202610" clearable /></el-form-item>
        <el-form-item label="旧确认标记"><el-select v-model="confirmed" clearable><el-option label="已标记" value="true" /><el-option label="未标记" value="false" /></el-select></el-form-item>
      </template>
      <el-button type="primary" :disabled="!permitted()" :loading="loading" @click="search">查询</el-button>
    </el-form>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <el-table v-if="mode === 'customers'" :data="result.items" border>
        <el-table-column prop="customerCd" label="客户" />
        <el-table-column label="单位"><template #default="{ row }">{{ row.unitCd || '未知' }}</template></el-table-column>
        <el-table-column prop="skuCount" label="SKU 数" />
        <el-table-column label="实物数量"><template #default="{ row }">{{ row.physicalQty ?? '单位未知，不合计' }}</template></el-table-column>
        <el-table-column prop="legacyAllocatedQty" label="旧分配数量" />
        <el-table-column prop="legacyAvailableQty" label="旧可用余额（未评估用途）" />
        <el-table-column label="明细"><template #default="{ row }"><el-button link @click="openCustomer(row.customerCd)">查看库存</el-button></template></el-table-column>
      </el-table>
      <el-table v-else-if="mode === 'stock'" :data="result.items" row-key="id" border>
        <el-table-column prop="customerCd" label="客户" /><el-table-column prop="productCd" label="产品" /><el-table-column prop="lotNo" label="批号" />
        <el-table-column prop="warehouseCd" label="仓库" /><el-table-column prop="locationCd" label="库位" />
        <el-table-column label="单位"><template #default="{ row }">{{ row.unitCd || '未知' }}</template></el-table-column>
        <el-table-column prop="physicalQty" label="实物数量" /><el-table-column prop="legacyAllocatedQty" label="旧分配数量" /><el-table-column prop="legacyAvailableQty" label="旧可用余额（未评估用途）" />
        <el-table-column prop="receiveDate" label="接收日期" /><el-table-column prop="expiryDate" label="到期日期" />
      </el-table>
      <el-table v-else :data="result.items" row-key="id" border>
        <el-table-column prop="billingNo" label="历史账单号" /><el-table-column prop="customerCd" label="客户" />
        <el-table-column label="客户名称"><template #default="{ row }">{{ row.customerNameReadable ? (row.customerName ?? '未记录') : '当前不可披露' }}</template></el-table-column>
        <el-table-column prop="yearMonth" label="年月" /><el-table-column prop="beginQty" label="旧期初估算" /><el-table-column prop="endQty" label="旧期末估算" /><el-table-column prop="avgQty" label="旧平均估算" />
        <el-table-column label="历史费率"><template #default="{ row }">{{ row.rateReadable ? row.dailyStorageRate : '当前不可披露' }}</template></el-table-column>
        <el-table-column label="历史金额（币种未知）"><template #default="{ row }">{{ row.amountReadable ? row.billingAmount : '当前不可披露' }}</template></el-table-column>
        <el-table-column label="旧确认标记"><template #default="{ row }">{{ row.legacyConfirmedFlag ? '已标记，财务结果未知' : '未标记' }}</template></el-table-column>
        <el-table-column label="备注"><template #default="{ row }">{{ row.remarksReadable ? (row.remarks ?? '未记录') : '当前不可披露' }}</template></el-table-column>
      </el-table>
      <p>当前可见 {{ result.total }} 条，第 {{ result.page }} 页。分页反映当前观察结果。</p>
      <el-button :disabled="result.page <= 1" @click="goPage(result.page - 1)">上一页</el-button>
      <el-button :disabled="result.page * result.pageSize >= result.total" @click="goPage(result.page + 1)">下一页</el-button>
    </template>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { vmiApi } from '@/api/wms/paperIndustry'
import { vmiVersion, type VmiQuery, type VmiPage, type VmiCustomerObservation, type VmiStockObservation, type VmiBillingObservation } from '@/types/wms/vmiOwner'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

type Mode = 'customers' | 'stock' | 'billings'
type Row = VmiCustomerObservation | VmiStockObservation | VmiBillingObservation
const permission = usePermissionStore(), mode = ref<Mode>('customers'), confirmed = ref('')
const query = reactive<VmiQuery>({ customerCd: '', yearMonth: '', page: 1, pageSize: 50 })
const result = ref<VmiPage<Row> | null>(null), loading = ref(false), error = ref('')
let sequence = 0
function clear() { sequence++; result.value = null; loading.value = false }
const intent = useOperationsIntent(() => { clear(); error.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-vmi:query') && (mode.value === 'billings' || permission.has('wms-stock:query'))
watch(() => [permission.loaded, ...['wms-vmi:query', 'wms-stock:query', 'wms-vmi:amount-read', 'wms-vmi:rate-read'].map(x => permission.has(x))], clear, { flush: 'sync' })
watch(() => [mode.value, confirmed.value, query.customerCd, query.yearMonth, query.page, query.pageSize], clear, { flush: 'sync' })
const exact = (x: unknown) => typeof x === 'string' && /^-?\d+(?:\.\d+)?$/.test(x)
function validRow(row: Row, kind: Mode): boolean {
  if (!row || typeof row.customerCd !== 'string') return false
  if (kind === 'billings') {
    const r = row as VmiBillingObservation
    return r.legacySemantics === 'POINT_IN_TIME_ESTIMATE' && r.financeOutcome === 'UNKNOWN' && r.unitKnowledge === 'UNKNOWN' && r.currencyKnowledge === 'UNKNOWN' &&
      [r.beginQty, r.endQty, r.avgQty].every(exact) && typeof r.legacyConfirmedFlag === 'boolean' &&
      (r.amountReadable === true ? permission.has('wms-vmi:amount-read') && exact(r.billingAmount) : r.amountReadable === false && r.billingAmount === null) &&
      (r.rateReadable === true ? permission.has('wms-vmi:rate-read') && exact(r.dailyStorageRate) : r.rateReadable === false && r.dailyStorageRate === null) &&
      (r.customerNameReadable === true ? r.customerName === null || typeof r.customerName === 'string' : r.customerNameReadable === false && r.customerName === null) &&
      (r.remarksReadable === true ? r.remarks === null || typeof r.remarks === 'string' : r.remarksReadable === false && r.remarks === null)
  }
  const r = row as VmiCustomerObservation | VmiStockObservation
  if (r.availabilityKnowledge !== 'NOT_EVALUATED' || r.free !== null) return false
  const quantities = [r.physicalQty, r.legacyAllocatedQty, r.legacyAvailableQty]
  if (kind === 'stock') return quantities.every(exact)
  const customer = r as VmiCustomerObservation
  return customer.valuationKnowledge === 'UNKNOWN' && customer.estimatedValue === null &&
    (typeof customer.unitCd === 'string' && customer.unitCd.trim() !== '' ? customer.unitKnowledge === 'RECORDED' && quantities.every(exact) : customer.unitKnowledge === 'UNKNOWN' && quantities.every(x => x === null))
}
async function load(input: VmiQuery) {
  const kind = mode.value, q = { ...input }, token = intent.capture(); clear(); const read = sequence
  if (kind !== 'billings') { delete q.yearMonth; delete q.confirmed } else { q.yearMonth ||= undefined }
  if (!q.customerCd) q.customerCd = undefined
  const current = () => intent.current(token) && read === sequence
  loading.value = true; error.value = ''
  try {
    if (!permitted() || kind === 'stock' && !q.customerCd) throw new Error('Read unavailable')
    const response = kind === 'customers' ? await vmiApi.customersOwner(q) : kind === 'stock' ? await vmiApi.stockOwner(q.customerCd!, q) : await vmiApi.billingsOwner(q)
    if (!current()) return
    const value = response.data, source = kind === 'billings' ? 'HISTORICAL_BILLING_ESTIMATE' : 'CURRENT_STOCK_OBSERVATION'
    if (!permitted() || response.code !== 0 || value?.version !== vmiVersion || value.coverage !== 'AUTHORIZED_LEGACY_OBSERVATION' || value.source !== source ||
      value.writesAvailable !== false || value.writeBlocker !== 'VMI_OWNER_ADOPTION_UNPROVEN' || value.page !== q.page || value.pageSize !== q.pageSize ||
      !Number.isSafeInteger(value.total) || value.total < 0 || !Array.isArray(value.items) || value.items.length > q.pageSize ||
      !value.items.every(row => validRow(row, kind) && (!q.customerCd || row.customerCd === q.customerCd))) throw new Error('Observation unavailable')
    result.value = value
  } catch { if (current()) { clear(); error.value = '无法读取当前权限内的 VMI 记录，请核对客户和查询条件后重试。' } }
  finally { if (current()) loading.value = false }
}
function search() { query.page = 1; void load({ ...query, confirmed: confirmed.value === 'true' ? true : confirmed.value === 'false' ? false : undefined }) }
function goPage(page: number) { query.page = page; void load({ ...query, confirmed: confirmed.value === 'true' ? true : confirmed.value === 'false' ? false : undefined }) }
function openCustomer(customerCd: string) { if (!result.value?.items.some(x => x.customerCd === customerCd)) return; mode.value = 'stock'; query.customerCd = customerCd; search() }
onMounted(search)
</script>
