<!--
  返品 (RMA) —— list+detail 単一ファイル（mode トグル）。
  list モード → CpPageShell（:count←total）+ CpListPage（単表スクロール paginated=false）。
    状態=kind:'tag'+map（共有 Tone）；申請日=kind:'date'；create は CpPageShell #actions。
  detail モード（特殊エディタ領域：新規/閲覧兼用フォーム + 明細編集テーブル）→ ヘッダ状態を CpTag 化、action-bar を token 化。
    back で mode=list → CpListPage が再マウントし自動 reload。
-->
<template>
  <div class="wms-rma">
    <el-alert v-if="!writesAvailable" :title="t('退货暂只支持查询，授权、收货和处置功能尚未开通。')" type="info" :closable="false" show-icon />
    <el-alert v-if="readError || intent.error.value" :title="readError || intent.error.value" type="warning" :closable="false" show-icon />
    <!-- ───── 一覧 ───── -->
    <CpPageShell v-if="mode === 'list'" :title="t('wms.rma.title')" :count="total">
      <template #actions>
        <el-button @click="reloadList">{{ t('刷新') }}</el-button>
        <el-button v-permission="'wms-rma:add'" :disabled="!canCreate" @click="openCreate">{{ t('wms.common.create') }}</el-button>
      </template>

      <CpListPage
        :key="listEpoch"
        :lazy="reloadRequired"
        :clear-on-load="true"
        :empty-text="readError || undefined"
        :columns="columns"
        :fetch="fetchList"
        :search-fields="searchFields"
        :filter-labels="filterLabels"
        :paginated="false"
        @total-change="total = $event"
      >
        <template #col-_action="{ row }">
          <el-button link type="primary" size="small" @click="openDetail(row.rmaNo)">{{ t('wms.common.open') }}</el-button>
        </template>
      </CpListPage>
    </CpPageShell>

    <!-- ───── 詳細 / 新規 ───── -->
    <template v-if="mode === 'detail' && current">
      <el-card shadow="never">
        <template #header>
          <div class="card-hd">
            <span class="hd-title">{{ isNew ? t('wms.rma.titleNew') : `${t('wms.rma.title')} [${current.rmaNo}]` }}</span>
            <CpTag v-if="!isNew" :tone="statusTone(current.status)">{{ statusMap[current.status] }}</CpTag>
          </div>
        </template>
        <el-form :model="current" label-width="160px" size="small">
          <el-row :gutter="16">
            <el-col :span="8"><el-form-item :label="t('wms.outbound.fld.customerCd')" required><el-input v-model="current.customerCd" :disabled="!isNew" maxlength="20" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.outbound.fld.customerName')"><el-input v-model="current.customerName" :disabled="!isNew" maxlength="100" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.rma.fld.appliedDate')"><el-date-picker v-model="current.appliedDate" type="date" value-format="YYYY-MM-DD" :disabled="!isNew" style="width: 100%" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="current.warehouseCd" :disabled="!isNew" maxlength="10" /></el-form-item></el-col>
            <el-col :span="16"><el-form-item :label="t('wms.rma.fld.originalShipping')"><el-input v-model="current.originalShippingNo" :disabled="!isNew" maxlength="20" /></el-form-item></el-col>
            <el-col :span="24"><el-form-item :label="t('wms.rma.fld.returnReason')"><el-input v-model="current.returnReason" type="textarea" :rows="2" :disabled="!isNew" /></el-form-item></el-col>
          </el-row>
        </el-form>
      </el-card>

      <el-card shadow="never" style="margin-top: 12px">
        <template #header>
          <div class="card-hd hd-between">
            <span class="hd-title">{{ t('wms.common.detail') }}</span>
            <el-button v-if="isNew" type="primary" size="small" @click="addLine">{{ t('wms.common.addLine') }}</el-button>
          </div>
        </template>
        <el-table :data="current.details" border size="small" max-height="500">
          <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
          <el-table-column :label="t('wms.common.product')" min-width="120">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.productCd" maxlength="20" />
              <span v-else>{{ row.productCd }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.productName')" min-width="160">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.productName" maxlength="100" />
              <span v-else>{{ row.productName }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.lot')" width="140">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.lotNo" maxlength="30" />
              <span v-else>{{ row.lotNo || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.qty')" width="110">
            <template #default="{ row }">
              <el-input-number v-if="isNew" v-model="row.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
              <span v-else>{{ formatQty(row.qty) }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.condition')" width="120">
            <template #default="{ row }">
              <el-select v-model="row.conditionLevel" :disabled="!isNew" style="width: 100%">
                <el-option v-for="(l, v) in conditionMap" :key="v" :label="l" :value="v" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.judgement')" width="140">
            <template #default="{ row }">
              <el-select v-model="row.judgement" :disabled="!canJudge" clearable style="width: 100%">
                <el-option v-for="(l, v) in judgementMap" :key="v" :label="l" :value="v" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.destLoc')" width="160">
            <template #default="{ row }">
              <el-input v-model="row.destLocationCd" :disabled="!canJudge" maxlength="30" />
            </template>
          </el-table-column>
          <el-table-column label="TXN" width="100">
            <template #default="{ row }">
              <el-tooltip v-if="row.inboundTxnNo" :content="row.inboundTxnNo"><el-tag size="small">IN</el-tag></el-tooltip>
              <el-tooltip v-if="row.dispositionTxnNo" :content="row.dispositionTxnNo"><el-tag type="success" size="small" style="margin-left: 4px">DISP</el-tag></el-tooltip>
            </template>
          </el-table-column>
          <el-table-column v-if="isNew" :label="t('wms.common.action')" width="80" fixed="right">
            <template #default="{ $index }">
              <el-button link type="danger" size="small" @click="removeLine($index)">{{ t('wms.common.delete') }}</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-affix position="bottom" :offset="0">
        <div class="action-bar">
          <el-button @click="reloadList">{{ t('wms.common.back') }}</el-button>
          <el-button v-if="isNew" v-permission="'wms-rma:add'" type="primary" :disabled="!canCreate" @click="onSave" :loading="intent.busy.value">{{ t('wms.common.save') }}</el-button>
          <el-button v-if="canReceive" v-permission="'wms-rma:receive'" type="primary" @click="onReceive">{{ t('wms.rma.btn.receive') }}</el-button>
          <el-button v-if="canStartInsp" v-permission="'wms-rma:inspect'" type="primary" @click="onStartInsp">{{ t('wms.rma.btn.startInspection') }}</el-button>
          <el-button v-if="canJudge" v-permission="'wms-rma:judge'" type="success" @click="onJudge">{{ t('wms.rma.btn.judge') }}</el-button>
          <el-button v-if="canClose" v-permission="'wms-rma:close'" type="success" plain @click="onClose">{{ t('wms.rma.btn.close') }}</el-button>
          <el-button v-if="canCancel" v-permission="'wms-rma:cancel'" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { rmaApi } from '@/api/wms/rma'
import type { RmaHeader, RmaDetail, RmaSearchQuery, RmaDispositionInput } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n()

const mode = ref<'list' | 'detail'>('list')
const total = ref<number>()
const query = reactive<RmaSearchQuery>({ pageSize: 500 })

const current = ref<RmaHeader | null>(null)
const writesAvailable = ref(false), readError = ref(''), listEpoch = ref(0), reloadRequired = ref(false)
let readSequence = 0
const permission = usePermissionStore()
const intent = useOperationsIntent(() => {
  readSequence++; current.value = null; writesAvailable.value = false; total.value = undefined
  readError.value = ''; reloadRequired.value = true; listEpoch.value++; mode.value = 'list'
})
const allowed = (action: string) => permission.loaded && permission.has(`wms-rma:${action}`)
const writable = (action: string) => writesAvailable.value && allowed(action) && !intent.uncertain.value
const canCreate = computed(() => writable('add') && !intent.busy.value)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.rma.status.applied'),
  1: t('wms.rma.status.authorized'),
  2: t('wms.rma.status.received'),
  3: t('wms.rma.status.inspecting'),
  4: t('wms.rma.status.judged'),
  5: t('wms.rma.status.closed'),
  9: t('wms.rma.status.cancelled'),
}))
const conditionMap = computed<Record<string, string>>(() => ({
  NEW: t('wms.rma.cond.new'),
  OPEN: t('wms.rma.cond.open'),
  DAMAGED: t('wms.rma.cond.damaged'),
}))
const judgementMap = computed<Record<string, string>>(() => ({
  RESELL: t('wms.rma.judge.resell'),
  REPAIR: t('wms.rma.judge.repair'),
  SCRAP: t('wms.rma.judge.scrap'),
  SUPPLIER_RETURN: t('wms.rma.judge.supplierReturn'),
}))

const isNew = computed(() => current.value !== null && !current.value.rmaNo)
const canReceive = computed(() => writable('receive') && current.value?.status === 1)
const canStartInsp = computed(() => writable('inspect') && current.value?.status === 2)
const canJudge = computed(() => writable('judge') && (current.value?.status === 2 || current.value?.status === 3))
const canClose = computed(() => writable('close') && current.value?.status === 4)
const canCancel = computed(() => writable('cancel') && !!current.value && current.value.status !== 5 && current.value.status !== 9 && !isNew.value)

function statusTone(s: number): Tone {
  return ({ 0: 'info', 1: 'info', 2: 'warn', 3: 'warn', 4: 'ok', 5: 'ok', 9: 'muted' } as const)[s as 0] || 'info'
}

// —— 一覧 ——
const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const columns = computed<ListColumn<RmaHeader>[]>(() => [
  { prop: 'rmaNo', label: t('wms.rma.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 120, kind: 'tag',
    map: (v) => ({ label: statusMap.value[v as number] ?? '', tone: statusTone(v as number) }) },
  { prop: 'customerCd', label: t('wms.outbound.fld.customerCd'), width: 100 },
  { prop: 'originalShippingNo', label: t('wms.rma.fld.originalShipping'), width: 180 },
  { prop: 'appliedDate', label: t('wms.rma.fld.appliedDate'), width: 120, kind: 'date' },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 90 },
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
])

const searchFields = computed<FilterField[]>(() => [
  { key: 'rmaNo', label: t('wms.rma.fld.no'), type: 'text' },
  { key: 'customerCd', label: t('wms.outbound.fld.customerCd'), type: 'text' },
  { key: 'originalShippingNo', label: t('wms.rma.fld.originalShipping'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const fetchList: ListFetch<RmaHeader> = async ({ filters }) => {
  const token = intent.capture(), sequence = ++readSequence
  current.value = null; writesAvailable.value = false; total.value = undefined; readError.value = ''
  const f = filters as Record<string, unknown>
  const q: RmaSearchQuery = { ...query }
  q.rmaNo = f.rmaNo ? String(f.rmaNo) : undefined
  q.customerCd = f.customerCd ? String(f.customerCd) : undefined
  q.originalShippingNo = f.originalShippingNo ? String(f.originalShippingNo) : undefined
  q.status = f.status !== undefined && f.status !== '' ? Number(f.status) : undefined
  try {
    const available = await readCapabilities()
    if (!intent.current(token) || sequence !== readSequence) throw new Error('Stale read')
    const result = await rmaApi.search(q)
    if (!intent.current(token) || sequence !== readSequence || !allowed('query')) throw new Error('Stale read')
    if (result.code !== 0 || !Array.isArray(result.data)) throw new Error('Unavailable list')
    writesAvailable.value = available
    return { rows: result.data, total: result.data.length }
  } catch {
    if (intent.current(token) && sequence === readSequence) clearFailedRead()
    throw new Error(t('退货数据未能读取，请刷新重试。'))
  }
}

async function readCapabilities() {
  if (!allowed('query')) throw new Error('Missing query permission')
  const result = await rmaApi.capabilities()
  if (result.code !== 0 || typeof result.data?.writesAvailable !== 'boolean') throw new Error('Unavailable capabilities')
  return result.data.writesAvailable && result.data.status === 'LEGACY'
}
function clearFailedRead() {
  current.value = null; writesAvailable.value = false; total.value = undefined
  readError.value = t('退货数据未能读取，请刷新重试。')
  if (mode.value !== 'list') { reloadRequired.value = true; listEpoch.value++; mode.value = 'list' }
}
function reloadList() {
  readSequence++; current.value = null; writesAvailable.value = false; readError.value = ''
  reloadRequired.value = false; listEpoch.value++; mode.value = 'list'
}
function openCreate() {
  if (!canCreate.value) return
  readSequence++
  current.value = {
    customerCd: '',
    appliedDate: new Date().toISOString().slice(0, 10),
    warehouseCd: '',
    status: 0,
    details: [],
  }
  mode.value = 'detail'
}

async function openDetail(no?: string) {
  if (!no) return
  const token = intent.capture(), sequence = ++readSequence
  current.value = null; writesAvailable.value = false; readError.value = ''
  try {
    const available = await readCapabilities()
    if (!intent.current(token) || sequence !== readSequence) return
    const res = await rmaApi.get(no)
    if (!intent.current(token) || sequence !== readSequence || !allowed('query')) return
    if (res.code !== 0 || !res.data || !Array.isArray(res.data.details)) throw new Error('Unavailable detail')
    current.value = res.data; writesAvailable.value = available; mode.value = 'detail'
  } catch {
    if (intent.current(token) && sequence === readSequence) clearFailedRead()
  }
}

function addLine() {
  if (!isNew.value || !canCreate.value || !current.value) return
  current.value.details.push({
    lineNo: current.value.details.length + 1, productCd: '', lotNo: '', qty: 0,
    conditionLevel: 'OPEN',
  } as RmaDetail)
}
function removeLine(idx: number) {
  if (!isNew.value || !canCreate.value || !current.value) return
  current.value.details.splice(idx, 1)
  current.value.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function onSave() {
  if (!canCreate.value || !isNew.value || !current.value) return
  if (!current.value.customerCd || !current.value.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  if (current.value.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  const body: RmaHeader = JSON.parse(JSON.stringify(current.value)), token = intent.begin()
  if (!token) return
  try {
    const res = await rmaApi.create(body)
    if (!intent.current(token)) return
    if (res.code !== 0 || !res.data?.rmaNo) throw new Error('Unavailable result')
    ElMessage.success(`${t('wms.common.success')}: ${res.data.rmaNo}`)
    await openDetail(res.data.rmaNo)
  } catch (error) { intent.failed(token, error) }
  finally { intent.finish(token) }
}

async function command(action: string, ready: () => boolean, send: (no: string) => Promise<{ code: number }>, confirmation?: string) {
  if (!ready() || !current.value?.rmaNo) return
  const no = current.value.rmaNo, token = intent.begin()
  if (!token) return
  try {
    if (confirmation) await ElMessageBox.confirm(t(confirmation), t('wms.common.confirm'), { type: 'warning' })
    if (!intent.current(token) || !writable(action) || !ready() || current.value?.rmaNo !== no) return
    const result = await send(no)
    if (!intent.current(token)) return
    if (result.code !== 0) throw new Error('Unavailable result')
    ElMessage.success(t('wms.common.success'))
    await openDetail(no)
  } catch (error) { intent.failed(token, error) }
  finally { intent.finish(token) }
}
function onReceive() {
  return command('receive', () => canReceive.value, no => rmaApi.receive(no), 'wms.inbound.msg.confirmAsk')
}
function onStartInsp() {
  return command('inspect', () => canStartInsp.value, no => rmaApi.startInspection(no))
}

async function onJudge() {
  if (!canJudge.value || !current.value) return
  // 全行に判定があるか確認
  const missing = current.value.details.filter(d => !d.judgement)
  if (missing.length > 0) { ElMessage.warning(t('wms.rma.msg.judgementRequired')); return }
  const inputs: RmaDispositionInput[] = current.value.details.map(d => ({
    lineNo: d.lineNo, judgement: d.judgement!, destLocationCd: d.destLocationCd,
  }))
  return command('judge', () => canJudge.value, no => rmaApi.judge(no, inputs), 'wms.outbound.msg.allocateAsk')
}

function onClose() {
  return command('close', () => canClose.value, no => rmaApi.close(no))
}

function onCancel() {
  return command('cancel', () => canCancel.value, no => rmaApi.cancel(no), 'wms.inbound.msg.cancelAsk')
}
</script>

<style scoped>
.wms-rma { padding: 16px; padding-bottom: 60px; }
.card-hd { display: flex; align-items: center; gap: 12px; }
.hd-between { justify-content: space-between; }
.hd-title { font-weight: 600; }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line-soft); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
