<template>
  <CpPageShell title="批次追溯与召回标记" :count="result?.nodes.length">
    <p>显示当前权限范围内的批次记录，关联对象可能不完整。召回标记不代表实际召回已完成，也不会改变库存数量。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item :label="t('wms.common.product')"><el-input v-model="query.productCd" clearable /></el-form-item>
      <el-form-item :label="t('wms.common.lot')"><el-input v-model="query.lotNo" placeholder="留空表示无批次" clearable /></el-form-item>
      <el-form-item :label="t('wms.lotTrace.fld.direction')">
        <el-radio-group v-model="query.direction"><el-radio-button value="FORWARD">流向</el-radio-button><el-radio-button value="BACKWARD">来源</el-radio-button></el-radio-group>
      </el-form-item>
      <el-button type="primary" :loading="loading" :disabled="!permitted() || intent.busy.value" @click="search">查询</el-button>
    </el-form>
    <p v-if="readError || intent.error.value" role="alert">{{ readError || intent.error.value }}</p>
    <p v-if="message" role="status">{{ message }}</p>
    <template v-if="result">
      <p>{{ result.productCd }} · {{ result.lotNo || '无批次' }} · {{ result.direction === 'FORWARD' ? '流向' : '来源' }}</p>
      <el-descriptions v-if="result.stock" :column="3" border>
        <el-descriptions-item label="当前可见物理数量">{{ observationText(result.stock.physicalQty) }}</el-descriptions-item>
        <el-descriptions-item label="当前可见可用数量">{{ observationText(result.stock.availableQty) }}</el-descriptions-item>
        <el-descriptions-item label="库存记录数">{{ result.stock.stockCount }}</el-descriptions-item>
        <el-descriptions-item label="召回标记">{{ recallLabel(result.stock.recallState) }}</el-descriptions-item>
        <el-descriptions-item label="最早效期">{{ result.stock.expiryDate?.slice(0, 10) ?? '—' }}</el-descriptions-item>
      </el-descriptions>
      <p v-else>当前权限范围内没有该批次的库存记录。</p>
      <div v-if="recallAllowed() && result.stock">
        <el-button type="danger" :disabled="!canRecall()" @click="setRecall(true)">设置召回标记</el-button>
        <el-button :disabled="!canRecall()" @click="setRecall(false)">解除召回标记</el-button>
      </div>
      <el-table v-if="result.direction === 'FORWARD'" :data="result.customers" border>
        <el-table-column prop="outboundNo" label="出库单" /><el-table-column prop="customerCd" label="客户代码" />
        <el-table-column v-if="result.customerNameReadable" prop="customerName" label="客户名称" />
        <el-table-column label="关联出库数量"><template #default="{ row }">{{ observationText(row.qty) }}</template></el-table-column>
        <el-table-column prop="shippedAt" label="关联交易时间" />
      </el-table>
      <el-table v-else :data="result.suppliers" border>
        <el-table-column prop="receiptNo" label="收货单" /><el-table-column prop="inboundNo" label="入库计划" />
        <el-table-column prop="supplierCd" label="供应商代码" /><el-table-column v-if="result.supplierNameReadable" prop="supplierName" label="供应商名称" />
        <el-table-column label="关联入库数量"><template #default="{ row }">{{ observationText(row.qty) }}</template></el-table-column>
        <el-table-column prop="receivedAt" label="关联交易时间" />
      </el-table>
      <el-table :data="result.nodes" row-key="txnNo" border>
        <el-table-column prop="txnNo" label="库存交易" /><el-table-column prop="txnType" label="类型" />
        <el-table-column prop="txnAt" label="时间" /><el-table-column prop="warehouseCd" label="仓库" /><el-table-column prop="locationCd" label="库位" />
        <el-table-column label="数量"><template #default="{ row }">{{ observationText(row.qty) }}</template></el-table-column>
        <el-table-column prop="relatedNo" label="记录中的关联单号" />
        <el-table-column v-if="result.operatorReadable" prop="operatorCd" label="操作人" /><el-table-column v-if="result.remarkReadable" prop="remark" label="备注" />
      </el-table>
    </template>
  </CpPageShell>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessageBox } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { lotTraceApi } from '@/api/wms/lotTrace'
import { lotTraceVersion, type LotSnapshot, type LotQuery } from '@/types/wms/lotTraceOwner'
import { observationText } from '@/utils/inventoryObservation'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'

const { t } = useI18n(), permission = usePermissionStore()
const query = reactive<LotQuery>({ productCd: '', lotNo: '', direction: 'FORWARD' })
const result = ref<LotSnapshot | null>(null), loading = ref(false), readError = ref(''), message = ref('')
let sequence = 0
function clearProtected() { sequence++; result.value = null; loading.value = false; message.value = '' }
const intent = useOperationsIntent(() => { clearProtected(); readError.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-lot-trace:query') && permission.has('wms-stock:query')
const recallAllowed = () => permitted() && permission.has('wms-lot-trace:recall')
const canRecall = () => recallAllowed() && !!result.value?.stock?.recallVersion && !intent.busy.value && !intent.uncertain.value
const recallLabel = (state: string) => (({ NONE: '未标记', MIXED: '部分已标记', ALL: '全部已标记' } as Record<string, string>)[state] ?? '未知')
watch(permitted, allowed => { if (!allowed) clearProtected() }, { flush: 'sync' })
watch(() => [query.productCd, query.lotNo, query.direction], () => clearProtected(), { flush: 'sync' })
async function load(input: LotQuery) {
  const token = intent.capture(); clearProtected(); const read = sequence
  const current = () => intent.current(token) && read === sequence
  loading.value = true; readError.value = ''
  try {
    if (!permitted() || !input.productCd.trim()) throw new Error('Query unavailable')
    const response = await lotTraceApi.read({ ...input })
    if (!current()) return
    const value = response.data
    if (!permitted() || response.code !== 0 || value?.version !== lotTraceVersion || value.coverage !== 'AUTHORIZED_LOT_OBSERVATION' ||
      value.productCd !== input.productCd || value.lotNo !== input.lotNo || value.direction !== input.direction) throw new Error('Read unavailable')
    result.value = value; intent.uncertain.value = false; intent.error.value = ''
  } catch { if (current()) { result.value = null; readError.value = '无法读取该批次，请重新查询当前权限范围。' } }
  finally { if (current()) loading.value = false }
}
function search() { if (!intent.busy.value) void load({ ...query }) }
async function setRecall(flag: boolean) {
  if (!canRecall()) return
  const snapshot = result.value!, recallVersion = snapshot.stock!.recallVersion
  const token = intent.begin(); if (!token) return
  let operation = sequence, submitted = false
  const current = () => intent.current(token) && operation === sequence
  try {
    await ElMessageBox.confirm(`${snapshot.productCd} · ${snapshot.lotNo || '无批次'}：${flag ? '设置' : '解除'}召回标记？这不会改变库存数量。`, '确认批次', { type: 'warning' })
    if (!current() || !recallAllowed() || result.value?.stock?.recallVersion !== recallVersion) return
    const request = { productCd: snapshot.productCd, lotNo: snapshot.lotNo, recallVersion, flag }
    clearProtected(); operation = sequence; submitted = true
    const response = await lotTraceApi.recall(request)
    if (!current()) return
    if (!recallAllowed() || response.code !== 0 || response.data?.version !== lotTraceVersion || response.data.productCd !== snapshot.productCd ||
      response.data.lotNo !== snapshot.lotNo || response.data.flag !== flag) throw new Error('Result unavailable')
    await load({ productCd: snapshot.productCd, lotNo: snapshot.lotNo, direction: snapshot.direction })
    if (intent.current(token) && permitted() && result.value) message.value = '召回标记已更新；库存数量未变。'
  } catch (error) { if (submitted && current()) { intent.failed(token, error); clearProtected() } }
  finally { intent.finish(token) }
}
</script>
