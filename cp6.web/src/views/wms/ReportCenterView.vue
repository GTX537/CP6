<template>
  <CpPageShell title="库存与流水记录" :count="result?.total">
    <p>这里显示当前有权查阅的原记录。正式月报、ABC、呆滞判断和固定快照导出尚未接通，不能用这些记录直接推算。金额未知，可用量未按用途评估。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="记录类型"><el-select v-model="mode"><el-option label="当前库存" value="stocks" /><el-option label="原库存流水" value="transactions" /></el-select></el-form-item>
      <el-form-item label="仓库"><el-input v-model="query.warehouseCd" clearable /></el-form-item>
      <el-form-item label="产品"><el-input v-model="query.productCd" clearable /></el-form-item>
      <el-form-item label="批次"><el-input v-model="query.lotNo" clearable /></el-form-item>
      <template v-if="mode === 'transactions'">
        <el-form-item label="原流水类型"><el-input v-model="query.txnType" clearable /></el-form-item>
        <el-form-item label="记录起日"><el-date-picker v-model="query.fromDate" type="date" value-format="YYYY-MM-DD" clearable /></el-form-item>
        <el-form-item label="记录止日"><el-date-picker v-model="query.toDate" type="date" value-format="YYYY-MM-DD" clearable /></el-form-item>
      </template>
      <el-button type="primary" :disabled="!permitted()" :loading="loading" @click="search">查询</el-button>
    </el-form>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <p v-if="mode === 'transactions'">按原记录日期筛选，未换算仓库当地时区；一条流水不等于一次真实出库发生。原数量符号保持，不合计不同单位；相关业务来源暂不可披露。</p>
      <p v-else>“原分配量”和“原可用标记量”是旧记录值，不能据此认定某项用途可使用。缺失单位或未知货主保持原状态。</p>
      <el-table :data="result.items" row-key="id" border>
        <template v-if="mode === 'transactions'"><el-table-column prop="txnNo" label="原流水号" /><el-table-column prop="txnType" label="原类型" /><el-table-column prop="txnDateTime" label="原记录时间" /><el-table-column prop="qty" label="原数量" /></template>
        <el-table-column prop="warehouseCd" label="仓库" /><el-table-column prop="locationCd" label="库位" /><el-table-column prop="productCd" label="产品" /><el-table-column prop="lotNo" label="批次" />
        <template v-if="mode === 'stocks'"><el-table-column prop="physicalQty" label="原物理量" /><el-table-column prop="legacyAllocatedQty" label="原分配量" /><el-table-column prop="legacyAvailableQty" label="原可用标记量" /></template>
        <el-table-column label="原单位"><template #default="{ row }">{{ row.unitState === 'RECORDED' ? row.unitCd : '未记录' }}</template></el-table-column>
        <el-table-column label="原货主类型"><template #default="{ row }">{{ row.ownerType || '未记录' }}{{ row.ownerState === 'UNKNOWN' ? '（未知）' : '' }}</template></el-table-column>
        <el-table-column label="原货主编码"><template #default="{ row }">{{ row.ownerCdReadable ? row.ownerCd ?? '未记录' : '当前不可披露' }}</template></el-table-column>
        <template v-if="mode === 'transactions'">
          <el-table-column label="记录操作人"><template #default="{ row }">{{ row.operatorCdReadable ? row.operatorCd ?? '未记录' : '当前不可披露' }}</template></el-table-column>
          <el-table-column label="原备注"><template #default="{ row }">{{ row.remarkReadable ? row.remark ?? '未记录' : '当前不可披露' }}</template></el-table-column>
        </template>
      </el-table>
      <p>当前可见 {{ result.total }} 条，第 {{ result.page }} 页。翻页会重新核对当前数据和权限，本结果不是冻结快照。</p>
      <el-button :disabled="result.page <= 1" @click="goPage(result.page - 1)">上一页</el-button>
      <el-button :disabled="result.page * result.pageSize >= result.total" @click="goPage(result.page + 1)">下一页</el-button>
    </template>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { reportApi } from '@/api/wms/reportCenter'
import { validReportPage, type ReportMode, type ReportQuery, type ReportPage } from '@/types/wms/reportCenterOwner'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

const permission = usePermissionStore(), mode = ref<ReportMode>('stocks')
const query = reactive<ReportQuery>({ warehouseCd: '', productCd: '', lotNo: '', page: 1, pageSize: 50 })
const result = ref<ReportPage | null>(null), loading = ref(false), error = ref('')
let sequence = 0
function clear() { sequence++; result.value = null; loading.value = false }
const intent = useOperationsIntent(() => { clear(); error.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-report-center:query') && permission.has('wms-stock:query')
watch(() => [permission.loaded, permission.has('wms-report-center:query'), permission.has('wms-stock:query')], clear, { flush: 'sync' })
watch(() => [query.warehouseCd, query.productCd, query.lotNo, query.txnType, query.fromDate, query.toDate, query.page, query.pageSize], clear, { flush: 'sync' })
watch(mode, () => { clear(); query.txnType = undefined; query.fromDate = undefined; query.toDate = undefined; search() }, { flush: 'sync' })
async function load(input: ReportQuery) {
  const kind = mode.value, q = { ...input }, token = intent.capture(); clear(); const read = sequence
  for (const key of ['warehouseCd', 'productCd', 'lotNo', 'txnType', 'fromDate', 'toDate'] as const) if (!q[key]) delete q[key]
  if (kind === 'stocks') { delete q.txnType; delete q.fromDate; delete q.toDate }
  const current = () => intent.current(token) && read === sequence && kind === mode.value
  loading.value = true; error.value = ''
  try {
    if (!permitted()) throw new Error('Read unavailable')
    const response = kind === 'stocks' ? await reportApi.stockOwner(q) : await reportApi.transactionsOwner(q)
    if (!current()) return
    if (!permitted() || response.code !== 0 || !validReportPage(response.data, q, kind)) throw new Error('Observation unavailable')
    result.value = response.data
  } catch { if (current()) { clear(); error.value = '无法读取当前权限内的库存或流水记录，请核对条件后重新查询。' } }
  finally { if (current()) loading.value = false }
}
function search() { query.page = 1; void load({ ...query }) }
function goPage(page: number) { query.page = page; void load({ ...query }) }
onMounted(search)
</script>
