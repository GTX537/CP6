<template>
  <CpPageShell title="套件主数据与历史订单" :count="result?.total">
    <p>当前配方仅供查阅，不能证明历史订单采用的原配方。旧执行、取消标记和流水引用不代表完整组拆成功；创建、修改、执行和取消暂不可用。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item label="记录类型"><el-select v-model="mode"><el-option label="当前主数据" value="masters" /><el-option label="历史订单" value="orders" /></el-select></el-form-item>
      <el-form-item label="套件编码 / 单号"><el-input v-model="query.keyword" clearable /></el-form-item>
      <el-form-item label="仓库"><el-input v-model="query.warehouseCd" clearable /></el-form-item>
      <el-form-item v-if="mode === 'masters'" label="启用标记"><el-select v-model="query.active" clearable><el-option label="启用" :value="true" /><el-option label="停用" :value="false" /></el-select></el-form-item>
      <el-form-item v-else label="历史状态"><el-select v-model="query.status" clearable><el-option label="旧草稿标记" :value="0" /><el-option label="旧执行标记" :value="1" /><el-option label="旧取消标记" :value="9" /></el-select></el-form-item>
      <el-button type="primary" :disabled="!permitted()" :loading="loading" @click="search">查询</el-button>
    </el-form>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <el-table :data="result.items" row-key="id" border>
        <el-table-column v-if="mode === 'orders'" prop="kitOrderNo" label="订单号" />
        <el-table-column prop="kitSku" label="套件编码" />
        <el-table-column label="名称"><template #default="{ row }">{{ row.kitNameReadable ? row.kitName ?? '未记录' : '当前不可披露' }}</template></el-table-column>
        <el-table-column :prop="mode === 'masters' ? 'defaultWarehouseCd' : 'warehouseCd'" label="仓库" />
        <el-table-column v-if="mode === 'masters'" label="启用标记"><template #default="{ row }">{{ row.activeFlg ? '启用' : '停用' }}</template></el-table-column>
        <template v-else><el-table-column prop="qty" label="原记录数量（单位未记录）" /><el-table-column label="历史状态"><template #default="{ row }">{{ statusLabel(row.legacyStatus) }}</template></el-table-column></template>
        <el-table-column label="查看"><template #default="{ row }"><el-button link @click="openDetail(row)">详情</el-button></template></el-table-column>
      </el-table>
      <p>当前可见 {{ result.total }} 条，第 {{ result.page }} 页</p>
      <el-button :disabled="result.page <= 1" @click="goPage(result.page - 1)">上一页</el-button>
      <el-button :disabled="result.page * result.pageSize >= result.total" @click="goPage(result.page + 1)">下一页</el-button>
    </template>
    <p v-if="detailLoading">正在读取当前详情…</p>
    <section v-if="masterDetail">
      <h3>{{ masterDetail.master.kitSku }}：当前配方</h3>
      <p>以下为现存组件记录，完整工程版本、配方基准及换算尚未核准，不能据此重新执行或拆解历史套件。</p>
      <p v-if="masterDetail.master.remarksReadable">{{ masterDetail.master.remarks ?? '未记录备注' }}</p>
      <el-table :data="masterDetail.components" row-key="id" border>
        <el-table-column prop="lineNo" label="原行号" /><el-table-column prop="componentProductCd" label="组件编码" />
        <el-table-column label="名称"><template #default="{ row }">{{ row.componentNameReadable ? row.componentName ?? '未记录' : '当前不可披露' }}</template></el-table-column>
        <el-table-column prop="requiredQty" label="原数量" /><el-table-column label="原单位"><template #default="{ row }">{{ row.unitCd || '未记录' }}</template></el-table-column>
      </el-table>
    </section>
    <section v-if="orderDetail">
      <h3>{{ orderDetail.kitOrderNo }}：历史订单</h3>
      <p>{{ statusLabel(orderDetail.legacyStatus) }}；完整组成及物理结果尚未验证。</p>
      <p>原数量：{{ orderDetail.qty }}（单位未记录）；原方向：{{ orderDetail.direction }}；原仓位：{{ orderDetail.warehouseCd }} / {{ orderDetail.kitLocationCd }}</p>
      <p v-if="orderDetail.operatorCdReadable">记录操作人：{{ orderDetail.operatorCd ?? '未记录' }}</p>
      <p v-if="orderDetail.remarksReadable">记录备注：{{ orderDetail.remarks ?? '未记录' }}</p>
      <template v-if="orderDetail.executedTxnNosReadable"><p>当前有权查阅的原流水引用；引用齐全不证明完整组拆成功。</p><pre>{{ orderDetail.executedTxnNos }}</pre></template>
      <p v-else-if="orderDetail.transactionReferencesState === 'NOT_RECORDED'">未记录流水引用，不能据此认定没有发生库存变化。</p>
      <p v-else>当前无法完整披露流水引用，原记录仍保留。</p>
    </section>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { kittingApi } from '@/api/wms/kitting'
import { validKitPage, validMasterDetail, validOrder, type KitQuery, type KitPage, type KitMasterRow, type KitMasterDetail, type KitOrderRow } from '@/types/wms/kittingOwner'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

type Mode = 'masters' | 'orders'
type Row = KitMasterRow | KitOrderRow
const permission = usePermissionStore(), mode = ref<Mode>('masters')
const query = reactive<KitQuery>({ keyword: '', warehouseCd: '', page: 1, pageSize: 50 })
const result = ref<KitPage<Row> | null>(null), masterDetail = ref<KitMasterDetail | null>(null), orderDetail = ref<KitOrderRow | null>(null)
const loading = ref(false), detailLoading = ref(false), error = ref('')
let sequence = 0, detailSequence = 0
function clear() { sequence++; detailSequence++; result.value = null; masterDetail.value = null; orderDetail.value = null; loading.value = false; detailLoading.value = false }
const intent = useOperationsIntent(() => { clear(); error.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-kitting:query')
watch(() => [permission.loaded, permission.has('wms-kitting:query'), permission.has('wms-stock:query')], clear, { flush: 'sync' })
watch(() => [query.keyword, query.warehouseCd, query.direction, query.status, query.active, query.page, query.pageSize], clear, { flush: 'sync' })
watch(mode, () => { clear(); query.active = undefined; query.status = undefined; query.direction = undefined; search() }, { flush: 'sync' })
const statusLabel = (s: number) => (({ 0: '旧草稿标记', 1: '旧执行标记', 9: '旧取消标记' } as Record<number, string>)[s] ?? '未知旧标记')
async function load(input: KitQuery) {
  const kind = mode.value, q = { ...input }, token = intent.capture(); clear(); const read = sequence
  if (kind === 'masters') { delete q.direction; delete q.status; if (typeof q.active !== 'boolean') delete q.active }
  else { delete q.active; if (q.status == null || String(q.status) === '') delete q.status; if (!q.direction) delete q.direction }
  const current = () => intent.current(token) && read === sequence && kind === mode.value
  loading.value = true; error.value = ''
  try {
    if (!permitted()) throw new Error('Read unavailable')
    const response = kind === 'masters' ? await kittingApi.searchMastersOwner(q) : await kittingApi.searchOrdersOwner(q)
    if (!current()) return
    if (!permitted() || response.code !== 0 || !validKitPage(response.data, q, kind)) throw new Error('Observation unavailable')
    result.value = response.data
  } catch { if (current()) { clear(); error.value = '无法读取当前权限内的套件记录，请重新查询。' } }
  finally { if (current()) loading.value = false }
}
function search() { query.page = 1; void load({ ...query }) }
function goPage(page: number) { query.page = page; void load({ ...query }) }
async function openDetail(row: Row) {
  const selected = result.value?.items.find(x => x.id === row.id)
  if (!selected || !permitted()) return
  const kind = mode.value, token = intent.capture(), read = sequence, request = ++detailSequence
  const key = kind === 'masters' ? selected.kitSku : (selected as KitOrderRow).kitOrderNo
  const current = () => intent.current(token) && read === sequence && request === detailSequence && kind === mode.value
  masterDetail.value = null; orderDetail.value = null; detailLoading.value = true; error.value = ''
  try {
    if (kind === 'masters') {
      const response = await kittingApi.getMasterOwner(key); if (!current()) return
      if (!permitted() || response.code !== 0 || !validMasterDetail(response.data) || response.data.master.id !== selected.id || response.data.master.kitSku !== key) throw new Error('Detail unavailable')
      masterDetail.value = response.data
    } else {
      const response = await kittingApi.getOrderOwner(key); if (!current()) return
      if (!permitted() || response.code !== 0 || !validOrder(response.data) || response.data.id !== selected.id || response.data.kitOrderNo !== key) throw new Error('Detail unavailable')
      orderDetail.value = response.data
    }
  } catch { if (current()) { clear(); error.value = '无法读取该记录，旧结果已清除，请重新查询。' } }
  finally { if (current()) detailLoading.value = false }
}
onMounted(search)
</script>
