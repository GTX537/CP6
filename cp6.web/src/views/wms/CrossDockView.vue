<template>
  <CpPageShell title="越库历史记录" :count="result?.total">
    <p>以下为当前权限内的旧记录。记录数量和状态不代表实际实收、发运或当前余量；来源及原交易结果尚待核对。新建、执行和取消暂不可用。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="越库单号"><el-input v-model="query.xDockNo" clearable /></el-form-item>
      <el-form-item label="产品"><el-input v-model="query.productCd" clearable /></el-form-item>
      <el-form-item label="记录状态"><el-select v-model="query.status" clearable><el-option label="历史计划" :value="0" /><el-option label="历史执行标记" :value="1" /><el-option label="历史取消标记" :value="9" /></el-select></el-form-item>
      <el-button type="primary" :disabled="!permitted()" :loading="loading" @click="search">查询</el-button>
    </el-form>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <el-table :data="result.items" row-key="id" border>
        <el-table-column prop="xDockNo" label="越库单号" /><el-table-column prop="productCd" label="产品" />
        <el-table-column label="记录数量"><template #default="{ row }">{{ observationText(row.qty) }}</template></el-table-column>
        <el-table-column label="记录状态"><template #default="{ row }">{{ statusLabel(row.legacyStatus) }}</template></el-table-column>
        <el-table-column prop="warehouseCd" label="仓库" /><el-table-column prop="tempLocationCd" label="暂存库位" />
        <el-table-column prop="inboundNo" label="记录的入库单号" /><el-table-column prop="outboundNo" label="记录的出库单号" />
        <el-table-column label="查看"><template #default="{ row }"><el-button link @click="openDetail(row)">详情</el-button></template></el-table-column>
      </el-table>
      <p>当前可见 {{ result.total }} 条，第 {{ result.page }} 页</p>
      <el-button :disabled="result.page <= 1" @click="goPage(result.page - 1)">上一页</el-button>
      <el-button :disabled="result.page * result.pageSize >= result.total" @click="goPage(result.page + 1)">下一页</el-button>
    </template>
    <p v-if="detailLoading">正在读取当前详情…</p>
    <section v-if="detail">
      <h3>{{ detail.xDockNo }}</h3>
      <p>来源及当前实收/发运情况待核对。以下单号均为历史记录引用。</p>
      <el-descriptions :column="2" border>
        <el-descriptions-item label="记录数量">{{ observationText(detail.qty) }}</el-descriptions-item>
        <el-descriptions-item label="原批次">{{ detail.lotNo || '无批次' }}</el-descriptions-item>
        <el-descriptions-item label="原入库交易引用">{{ detail.inTxnNo ?? '未记录' }}</el-descriptions-item>
        <el-descriptions-item label="原出库交易引用">{{ detail.outTxnNo ?? '未记录' }}</el-descriptions-item>
        <el-descriptions-item label="旧到达口文本">{{ detail.fromDock ?? '—' }}</el-descriptions-item>
        <el-descriptions-item label="旧发出口文本">{{ detail.toDock ?? '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.supplierCdReadable" label="记录供方">{{ detail.supplierCd ?? '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.customerCdReadable" label="记录客户">{{ detail.customerCd ?? '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.operatorCdReadable" label="记录操作人">{{ detail.operatorCd ?? '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.remarksReadable" label="记录备注">{{ detail.remarks ?? '—' }}</el-descriptions-item>
      </el-descriptions>
    </section>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { crossDockApi } from '@/api/wms/logistics'
import { crossDockVersion, type CrossDockPage, type CrossDockQuery, type CrossDockObservation } from '@/types/wms/crossDockOwner'
import { observationText } from '@/utils/inventoryObservation'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

const permission = usePermissionStore()
const query = reactive<CrossDockQuery>({ xDockNo: '', productCd: '', page: 1, pageSize: 50 })
const result = ref<CrossDockPage | null>(null), detail = ref<CrossDockObservation | null>(null)
const loading = ref(false), detailLoading = ref(false), error = ref('')
let sequence = 0, detailSequence = 0
function clear() { sequence++; detailSequence++; result.value = null; detail.value = null; loading.value = false; detailLoading.value = false }
const intent = useOperationsIntent(() => { clear(); error.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-cross-dock:query')
watch(() => [permission.loaded, permission.has('wms-cross-dock:query'), permission.has('wms-inbound-order:query'), permission.has('wms-outbound-order:query')], clear, { flush: 'sync' })
watch(() => [query.xDockNo, query.productCd, query.status, query.inboundNo, query.outboundNo, query.dateFrom, query.dateTo, query.page, query.pageSize], clear, { flush: 'sync' })
const statusLabel = (s: number) => (({ 0: '历史计划', 1: '历史执行标记', 9: '历史取消标记' } as Record<number, string>)[s] ?? '历史未知状态')
function validRow(row: CrossDockObservation) { return row?.version === crossDockVersion && row.knowledge === 'UNKNOWN' && row.sourceState === 'UNRESOLVED_SOURCE' && typeof row.qty === 'string' }
async function load(input: CrossDockQuery) {
  const q = { ...input }, token = intent.capture(); clear(); const read = sequence
  if (q.status === null || String(q.status) === '') q.status = undefined
  const current = () => intent.current(token) && read === sequence
  loading.value = true; error.value = ''
  try {
    if (!permitted()) throw new Error('Read unavailable')
    const response = await crossDockApi.searchOwner(q); if (!current()) return
    const value = response.data
    if (!permitted() || response.code !== 0 || value?.version !== crossDockVersion || value.coverage !== 'AUTHORIZED_LEGACY_OBSERVATION' ||
      value.writesAvailable !== false || value.writeBlocker !== 'CROSS_DOCK_OWNER_ADOPTION_UNPROVEN' || value.page !== q.page || value.pageSize !== q.pageSize ||
      !Array.isArray(value.items) || !value.items.every(validRow)) throw new Error('Observation unavailable')
    result.value = value
  } catch { if (current()) { clear(); error.value = '无法读取当前权限内的越库记录，请重新查询。' } }
  finally { if (current()) loading.value = false }
}
function search() { query.page = 1; void load({ ...query, status: query.status === undefined || query.status === null || String(query.status) === '' ? undefined : query.status }) }
function goPage(page: number) { query.page = page; void load({ ...query }) }
async function openDetail(row: CrossDockObservation) {
  const selected = result.value?.items.find(x => x.id === row.id)
  if (!selected || !permitted()) return
  const token = intent.capture(), read = sequence, request = ++detailSequence, number = selected.xDockNo
  const current = () => intent.current(token) && read === sequence && request === detailSequence
  detail.value = null; detailLoading.value = true; error.value = ''
  try {
    const response = await crossDockApi.getOwner(number); if (!current()) return
    if (!permitted() || response.code !== 0 || !validRow(response.data) || response.data.id !== selected.id || response.data.xDockNo !== number) throw new Error('Detail unavailable')
    detail.value = response.data
  } catch { if (current()) { clear(); error.value = '无法读取该记录，已清除旧结果，请重新查询。' } }
  finally { if (current()) detailLoading.value = false }
}
onMounted(search)
</script>
