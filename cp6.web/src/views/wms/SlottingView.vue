<template>
  <CpPageShell title="库位优化历史记录" :count="result?.total">
    <p>以下为当前权限内的旧方案。旧统计按交易行计数，批准标记和任务引用不代表实际搬运完成；取消标记不代表全部后续任务已关闭。新分析、批准和取消暂不可用。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="仓库"><el-input v-model="query.warehouseCd" clearable /></el-form-item>
      <el-form-item label="记录状态"><el-select v-model="query.status" clearable><el-option label="历史分析标记" :value="0" /><el-option label="历史建议标记" :value="1" /><el-option label="历史批准标记" :value="2" /><el-option label="历史取消标记" :value="9" /></el-select></el-form-item>
      <el-button type="primary" :disabled="!permitted()" :loading="loading" @click="search">查询</el-button>
    </el-form>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <el-table :data="result.items" row-key="id" border>
        <el-table-column prop="slottingPlanNo" label="方案号" /><el-table-column prop="warehouseCd" label="仓库" />
        <el-table-column label="记录状态"><template #default="{ row }">{{ statusLabel(row.legacyStatus) }}</template></el-table-column>
        <el-table-column prop="analysisDays" label="旧分析天数" />
        <el-table-column label="旧统计行数"><template #default="{ row }">{{ row.snapshotReadable ? row.txnSampleCount : '当前不可披露' }}</template></el-table-column>
        <el-table-column label="查看"><template #default="{ row }"><el-button link @click="openDetail(row)">详情</el-button></template></el-table-column>
      </el-table>
      <p>当前可见 {{ result.total }} 条，第 {{ result.page }} 页</p>
      <el-button :disabled="result.page <= 1" @click="goPage(result.page - 1)">上一页</el-button>
      <el-button :disabled="result.page * result.pageSize >= result.total" @click="goPage(result.page + 1)">下一页</el-button>
    </template>
    <p v-if="detailLoading">正在读取当前详情…</p>
    <section v-if="detail">
      <h3>{{ detail.slottingPlanNo }}</h3>
      <p>实际移动结果待原作业核对；这里保留旧记录，不重新分析或派发。</p>
      <p v-if="detail.approverCdReadable">记录批准人：{{ detail.approverCd ?? '未记录' }}</p>
      <p v-if="detail.remarksReadable">记录备注：{{ detail.remarks ?? '未记录' }}</p>
      <template v-if="detail.snapshotReadable">
        <p>旧统计行数：{{ detail.txnSampleCount }}；旧建议条数：{{ detail.recommendationCount }}</p>
        <p v-if="detail.jsonState === 'LEGACY_PARSE_ERROR'" role="alert">旧建议内容无法解析，以下原文已保留。</p>
        <p v-else-if="detail.jsonState === 'NOT_RECORDED'">未记录建议原文。</p>
        <p v-else>旧建议原文中的数量、库位提示和任务号仅为历史记录。</p>
        <pre v-if="detail.recommendationsJson !== null">{{ detail.recommendationsJson }}</pre>
      </template>
      <p v-else>当前权限不足以完整披露旧建议及统计值，内容仍保存在原记录中。</p>
    </section>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { slottingApi } from '@/api/wms/logistics'
import { slottingVersion, type SlottingPage, type SlottingQuery, type SlottingObservation } from '@/types/wms/slottingOwner'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

const permission = usePermissionStore()
const query = reactive<SlottingQuery>({ warehouseCd: '', page: 1, pageSize: 50 })
const result = ref<SlottingPage | null>(null), detail = ref<SlottingObservation | null>(null)
const loading = ref(false), detailLoading = ref(false), error = ref('')
let sequence = 0, detailSequence = 0
function clear() { sequence++; detailSequence++; result.value = null; detail.value = null; loading.value = false; detailLoading.value = false }
const intent = useOperationsIntent(() => { clear(); error.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-slotting:query')
watch(() => [permission.loaded, permission.has('wms-slotting:query'), permission.has('wms-stock:query'), permission.has('wms-mobile:query'), permission.has('wms-outbound-order:query')], clear, { flush: 'sync' })
watch(() => [query.warehouseCd, query.status, query.page, query.pageSize], clear, { flush: 'sync' })
const statusLabel = (s: number) => (({ 0: '历史分析标记', 1: '历史建议标记', 2: '历史批准标记', 9: '历史取消标记' } as Record<number, string>)[s] ?? '历史未知状态')
function validRow(row: SlottingObservation) {
  if (row?.version !== slottingVersion || row.knowledge !== 'UNKNOWN' || row.statisticsMethod !== 'LEGACY_ROW_COUNT') return false
  if (row.snapshotReadable === false) return row.snapshotState === 'SOURCE_DISCLOSURE_UNAVAILABLE' && row.jsonState === 'NOT_DISCLOSED' &&
    row.recommendationsJson === null && row.txnSampleCount === null && row.recommendationCount === null
  return row.snapshotReadable === true && row.snapshotState === 'AUTHORIZED_LEGACY_SNAPSHOT' &&
    ['JSON_ARRAY', 'LEGACY_PARSE_ERROR', 'NOT_RECORDED'].includes(row.jsonState) &&
    (typeof row.recommendationsJson === 'string' || row.recommendationsJson === null)
}
async function load(input: SlottingQuery) {
  const q = { ...input }, token = intent.capture(); clear(); const read = sequence
  if (q.status === null || String(q.status) === '') q.status = undefined
  const current = () => intent.current(token) && read === sequence
  loading.value = true; error.value = ''
  try {
    if (!permitted()) throw new Error('Read unavailable')
    const response = await slottingApi.searchOwner(q); if (!current()) return
    const value = response.data
    if (!permitted() || response.code !== 0 || value?.version !== slottingVersion || value.coverage !== 'AUTHORIZED_LEGACY_OBSERVATION' ||
      value.writesAvailable !== false || value.writeBlocker !== 'SLOTTING_OWNER_ADOPTION_UNPROVEN' || value.page !== q.page || value.pageSize !== q.pageSize ||
      !Array.isArray(value.items) || !value.items.every(validRow)) throw new Error('Observation unavailable')
    result.value = value
  } catch { if (current()) { clear(); error.value = '无法读取当前权限内的库位优化记录，请重新查询。' } }
  finally { if (current()) loading.value = false }
}
function search() { query.page = 1; void load({ ...query, status: query.status === undefined || query.status === null || String(query.status) === '' ? undefined : query.status }) }
function goPage(page: number) { query.page = page; void load({ ...query }) }
async function openDetail(row: SlottingObservation) {
  const selected = result.value?.items.find(x => x.id === row.id)
  if (!selected || !permitted()) return
  const token = intent.capture(), read = sequence, request = ++detailSequence, number = selected.slottingPlanNo
  const current = () => intent.current(token) && read === sequence && request === detailSequence
  detail.value = null; detailLoading.value = true; error.value = ''
  try {
    const response = await slottingApi.getOwner(number); if (!current()) return
    if (!permitted() || response.code !== 0 || !validRow(response.data) || response.data.id !== selected.id || response.data.slottingPlanNo !== number) throw new Error('Detail unavailable')
    detail.value = response.data
  } catch { if (current()) { clear(); error.value = '无法读取该记录，已清除旧结果，请重新查询。' } }
  finally { if (current()) detailLoading.value = false }
}
onMounted(search)
</script>
