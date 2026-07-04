<!--
  返品 (RMA) —— list+detail 単一ファイル（mode トグル）。
  list モード → CpPageShell（:count←total）+ CpListPage（単表スクロール paginated=false）。
    状態=kind:'tag'+map（共有 Tone）；申請日=kind:'date'；create は CpPageShell #actions。
  detail モード（特殊エディタ領域：新規/閲覧兼用フォーム + 明細編集テーブル）→ ヘッダ状態を CpTag 化、action-bar を token 化。
    back で mode=list → CpListPage が再マウントし自動 reload。
-->
<template>
  <div class="wms-rma">
    <!-- ───── 一覧 ───── -->
    <CpPageShell v-if="mode === 'list'" :title="t('wms.rma.title')" :count="total">
      <template #actions>
        <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
      </template>

      <CpListPage
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
          <el-button @click="mode = 'list'">{{ t('wms.common.back') }}</el-button>
          <el-button v-if="isNew" type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
          <el-button v-if="canReceive" type="primary" @click="onReceive">{{ t('wms.rma.btn.receive') }}</el-button>
          <el-button v-if="canStartInsp" type="primary" @click="onStartInsp">{{ t('wms.rma.btn.startInspection') }}</el-button>
          <el-button v-if="canJudge" type="success" @click="onJudge">{{ t('wms.rma.btn.judge') }}</el-button>
          <el-button v-if="canClose" type="success" plain @click="onClose">{{ t('wms.rma.btn.close') }}</el-button>
          <el-button v-if="canCancel" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
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

const { t } = useI18n()

const mode = ref<'list' | 'detail'>('list')
const total = ref<number>()
const query = reactive<RmaSearchQuery>({ pageSize: 500 })

const current = ref<RmaHeader | null>(null)
const saving = ref(false)

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
const canReceive = computed(() => current.value && current.value.status === 1)
const canStartInsp = computed(() => current.value && current.value.status === 2)
const canJudge = computed(() => current.value && (current.value.status === 2 || current.value.status === 3))
const canClose = computed(() => current.value && current.value.status === 4)
const canCancel = computed(() => current.value && current.value.status !== 5 && current.value.status !== 9 && !isNew.value)

function statusTone(s: number): Tone {
  return ({ 0: 'info', 1: 'info', 2: 'warn', 3: 'warn', 4: 'ok', 5: 'ok', 9: 'muted' } as const)[s as 0] || 'info'
}

// —— 一覧 ——
const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const columns = computed<ListColumn[]>(() => [
  { prop: 'rmaNo', label: t('wms.rma.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 120, kind: 'tag',
    map: (v) => ({ label: statusMap.value[v as number] ?? '', tone: statusTone(v as number) }) },
  { prop: 'customerCd', label: t('wms.outbound.fld.customerCd'), width: 100 },
  { prop: 'customerName', label: t('wms.outbound.fld.customerName'), minWidth: 140, overflowTooltip: true },
  { prop: 'originalShippingNo', label: t('wms.rma.fld.originalShipping'), width: 180 },
  { prop: 'appliedDate', label: t('wms.rma.fld.appliedDate'), width: 120, kind: 'date' },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 90 },
  { prop: 'returnReason', label: t('wms.rma.fld.returnReason'), minWidth: 180, overflowTooltip: true },
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

const fetchList: ListFetch = async ({ filters }) => {
  const f = filters as Record<string, unknown>
  const q: RmaSearchQuery = { ...query }
  q.rmaNo = f.rmaNo ? String(f.rmaNo) : undefined
  q.customerCd = f.customerCd ? String(f.customerCd) : undefined
  q.originalShippingNo = f.originalShippingNo ? String(f.originalShippingNo) : undefined
  q.status = f.status !== undefined && f.status !== '' ? Number(f.status) : undefined
  const all = (await rmaApi.search(q)).data || []
  return { rows: all, total: all.length }
}

function openCreate() {
  current.value = {
    customerCd: '',
    appliedDate: new Date().toISOString().slice(0, 10),
    warehouseCd: '',
    status: 0,
    details: [],
  }
  mode.value = 'detail'
}

async function openDetail(no: string) {
  const res = await rmaApi.get(no)
  current.value = res.data
  mode.value = 'detail'
}

function addLine() {
  if (!current.value) return
  current.value.details.push({
    lineNo: current.value.details.length + 1, productCd: '', lotNo: '', qty: 0,
    conditionLevel: 'OPEN',
  } as RmaDetail)
}
function removeLine(idx: number) {
  if (!current.value) return
  current.value.details.splice(idx, 1)
  current.value.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function onSave() {
  if (!current.value) return
  if (!current.value.customerCd || !current.value.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  if (current.value.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    const res = await rmaApi.create(current.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.rmaNo}`)
    await openDetail(res.data.rmaNo)
  } finally { saving.value = false }
}

async function onReceive() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await rmaApi.receive(current.value.rmaNo!)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
}

async function onStartInsp() {
  if (!current.value) return
  await rmaApi.startInspection(current.value.rmaNo!)
  ElMessage.success(t('wms.common.success'))
  await openDetail(current.value.rmaNo!)
}

async function onJudge() {
  if (!current.value) return
  // 全行に判定があるか確認
  const missing = current.value.details.filter(d => !d.judgement)
  if (missing.length > 0) { ElMessage.warning(t('wms.rma.msg.judgementRequired')); return }
  try {
    await ElMessageBox.confirm(t('wms.outbound.msg.allocateAsk'), t('wms.common.confirm'), { type: 'warning' })
    const inputs: RmaDispositionInput[] = current.value.details.map(d => ({
      lineNo: d.lineNo, judgement: d.judgement!, destLocationCd: d.destLocationCd,
    }))
    await rmaApi.judge(current.value.rmaNo!, inputs)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
}

async function onClose() {
  if (!current.value) return
  await rmaApi.close(current.value.rmaNo!)
  ElMessage.success(t('wms.common.success'))
  await openDetail(current.value.rmaNo!)
}

async function onCancel() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await rmaApi.cancel(current.value.rmaNo!)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
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
