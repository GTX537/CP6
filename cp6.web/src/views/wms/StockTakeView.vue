<template>
  <div class="wms-stock-take">
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; align-items: center; gap: 12px">
          <span style="font-weight: 600">{{ t('wms.stocktake.title') }} [{{ form.stockTakeNo }}]</span>
          <CpTag :tone="statusTone(form.status)">{{ statusMap[form.status] }}</CpTag>
          <CpTag tone="info">{{ typeMap[form.stockTakeType] }}</CpTag>
        </div>
      </template>
      <el-descriptions :column="4" size="small" border>
        <el-descriptions-item :label="t('wms.stocktake.fld.targetWh')">{{ form.targetWarehouseCd }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.targetLocPrefix')">{{ form.targetLocationPrefix || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.targetProduct')">{{ form.targetProductCd || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.threshold')">{{ form.approvalThresholdAmount ? `¥${form.approvalThresholdAmount}` : '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.plannedDate')">{{ form.plannedDate?.slice(0, 10) }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.actualDate')">{{ form.actualDate?.slice(0, 10) || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.completedDate')">{{ form.completedDate?.slice(0, 10) || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('wms.stocktake.fld.approver')">{{ form.approverCd || '—' }}</el-descriptions-item>
      </el-descriptions>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center">
          <span style="font-weight: 600">
            {{ t('wms.common.detail') }} ({{ form.details.length }}) / {{ t('wms.stocktake.col.diff') }} {{ diffCount }}
          </span>
          <el-input v-model="filterText" :placeholder="`${t('wms.common.product')} / ${t('wms.common.location')} / ${t('wms.common.lot')}`" clearable style="width: 280px" size="small" />
        </div>
      </template>

      <el-table :data="filteredDetails" border size="small" max-height="500" stripe>
        <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
        <el-table-column :label="t('wms.stocktake.col.book')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.bookQty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stocktake.col.counted')" width="130">
          <template #default="{ row }">
            <el-input-number v-model="row.countedQty" :min="0" :precision="2" controls-position="right"
              :disabled="!editable" style="width: 100%" @change="recalcDiff(row)" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.stocktake.col.diff')" width="100" align="right">
          <template #default="{ row }">
            <span v-if="row.countedQty == null" class="dash">—</span>
            <span v-else :class="{ minus: (row.diffQty ?? 0) < 0, plus: (row.diffQty ?? 0) > 0 }">
              {{ formatQty(row.diffQty) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.stocktake.col.diffAmount')" width="120" align="right">
          <template #default="{ row }">{{ row.diffAmount != null ? `¥${formatQty(row.diffAmount)}` : '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unitPrice')" width="100" align="right">
          <template #default="{ row }">{{ row.unitPrice != null ? `¥${formatQty(row.unitPrice)}` : '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stocktake.col.reason')" width="180">
          <template #default="{ row }">
            <el-input v-model="row.diffReasonCd" :disabled="!editable" maxlength="20" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.status')" width="100">
          <template #default="{ row }">
            <CpTag :tone="approvalTone(row.approvalStatus)">{{ approvalMap[row.approvalStatus] }}</CpTag>
          </template>
        </el-table-column>
        <el-table-column label="ADJ" width="140">
          <template #default="{ row }">
            <el-tooltip v-if="row.adjustTxnNo" :content="row.adjustTxnNo"><CpTag tone="ok">ADJ</CpTag></el-tooltip>
            <span v-else class="dash">—</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-affix position="bottom" :offset="0">
      <div class="action-bar">
        <el-button @click="goBack">{{ t('wms.common.back') }}</el-button>
        <el-button v-if="canStartCount" type="primary" @click="onStartCount" :disabled="intent.busy.value || intent.uncertain.value">{{ t('wms.stocktake.btn.startCount') }}</el-button>
        <el-button v-if="canSaveCounts" type="primary" @click="onSaveCounts" :disabled="intent.busy.value || intent.uncertain.value" :loading="saving">{{ t('wms.common.save') }}</el-button>
        <el-button v-if="canSubmit" type="warning" @click="onSubmit" :disabled="intent.busy.value || intent.uncertain.value">{{ t('wms.stocktake.btn.submitReview') }}</el-button>
        <el-button v-if="canApprove" v-permission="'wms-stocktake:approve'" type="success" @click="onApprove" :disabled="intent.busy.value || intent.uncertain.value">{{ t('wms.stocktake.btn.approve') }} + {{ t('wms.stocktake.btn.adjust') }}</el-button>
        <el-button v-if="canCancel" v-permission="'wms-stocktake:cancel'" type="danger" plain @click="onCancel" :disabled="intent.busy.value || intent.uncertain.value">{{ t('wms.outbound.btn.cancel') }}</el-button>
      </div>
    </el-affix>
  </div>
</template>

<script setup lang="ts">
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { ref, reactive, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { stockTakeApi } from '@/api/wms/stockTake'
import type { StockTake, StockTakeDetail, StockTakeCountInput } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const intent = useOperationsIntent(() => { reads++; resetForm() })
const saving = intent.busy
let reads = 0, revision = 0

const filterText = ref('')

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.stocktake.status.planned'),
  1: t('wms.stocktake.status.counting'),
  2: t('wms.stocktake.status.diffReview'),
  3: t('wms.stocktake.status.awaitingApproval'),
  4: t('wms.stocktake.status.completed'),
  9: t('wms.stocktake.status.cancelled'),
}))
const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.stocktake.type.full'),
  2: t('wms.stocktake.type.cycle'),
  3: t('wms.stocktake.type.adhoc'),
}))
const approvalMap = computed<Record<number, string>>(() => ({
  0: t('wms.stocktake.approval.pending'),
  1: t('wms.stocktake.approval.auto'),
  2: t('wms.stocktake.approval.approved'),
  9: t('wms.stocktake.approval.rejected'),
}))

const form = reactive<StockTake>({
  stockTakeNo: '', stockTakeType: 1, plannedDate: '',
  status: 0, targetWarehouseCd: '', details: [],
})

watch(form, () => revision++, { deep: true, flush: 'sync' })
function resetForm() { Object.assign(form, { stockTakeNo: '', stockTakeType: 1, plannedDate: '', status: 0, targetWarehouseCd: '', targetLocationPrefix: '', targetProductCd: '', approvalThresholdAmount: undefined, details: [] }) }
const editable = computed(() => form.status === 1 && !intent.busy.value && !intent.uncertain.value)
const canStartCount = computed(() => form.status === 0)
const canSaveCounts = computed(() => form.status === 1 && !intent.busy.value && !intent.uncertain.value)
const canSubmit = computed(() => form.status === 1 && !intent.busy.value && !intent.uncertain.value)
const canApprove = computed(() => form.status === 2 || form.status === 3)
const canCancel = computed(() => form.status !== 4 && form.status !== 9)

const filteredDetails = computed(() => {
  if (!filterText.value) return form.details
  const k = filterText.value.toLowerCase()
  return form.details.filter(d =>
    d.productCd.toLowerCase().includes(k) ||
    d.locationCd.toLowerCase().includes(k) ||
    d.lotNo.toLowerCase().includes(k))
})

const diffCount = computed(() =>
  form.details.filter(d => d.countedQty != null && (d.diffQty ?? 0) !== 0).length)

// 原 statusTagOf(info/primary/warning/danger/success/info) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'info', 1: 'info', 2: 'warn', 3: 'danger', 4: 'ok', 9: 'info' } as const)[s as 0] || 'info'
}
// 原 approvalTagOf(info/success/success/danger) → 设计系统 Tone（保色）
function approvalTone(s: number): Tone {
  return ({ 0: 'info', 1: 'ok', 2: 'ok', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number | null | undefined): string {
  if (n == null) return '—'
  return fmtQty(n, 4)
}

function recalcDiff(row: StockTakeDetail) {
  if (row.countedQty == null) { row.diffQty = undefined; row.diffAmount = undefined; return }
  row.diffQty = Number(row.countedQty) - Number(row.bookQty)
  row.diffAmount = row.unitPrice != null ? row.diffQty * Number(row.unitPrice) : undefined
}

async function load(no: string, preserveDraft = false) {
  const token = intent.capture(), id = ++reads, edit = revision
  try {
    const res = await stockTakeApi.get(no)
    if (!intent.current(token) || id !== reads || edit !== revision) return
    resetForm(); Object.assign(form, res.data)
  } catch {
    if (intent.current(token) && id === reads) {
      if (!preserveDraft && edit === revision) { resetForm(); form.stockTakeNo = no }
      intent.error.value = '読込できませんでした。保存結果は一覧で確認してください。'
    }
  }
}
async function transition(action: 'startCount' | 'submit' | 'approve' | 'cancel') {
  const token = intent.begin(); if (!token) return
  const no = form.stockTakeNo, edit = revision
  try {
    if (action !== 'startCount') await ElMessageBox.confirm(t(action === 'cancel' ? 'wms.inbound.msg.cancelAsk' : 'wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    if (!no || !intent.current(token) || no !== form.stockTakeNo) return
    await stockTakeApi[action](no)
    if (!intent.current(token) || no !== form.stockTakeNo) return
    ElMessage.success(t('wms.common.success'))
    const unchanged = revision === edit
    if (action === 'approve') form.status = 4
    if (action === 'cancel') form.status = 9
    if (action === 'startCount') form.status = 1
    if (unchanged) await load(no, true)
  } catch (e) { intent.failed(token, e) } finally { intent.finish(token) }
}
const onStartCount = () => transition('startCount')
const onSubmit = () => transition('submit')
const onApprove = () => transition('approve')
const onCancel = () => transition('cancel')
async function onSaveCounts() {
  const token = intent.begin(); if (!token) return
  const no = form.stockTakeNo, edit = revision
  const inputs: StockTakeCountInput[] = JSON.parse(JSON.stringify(form.details.filter(d => d.countedQty != null).map(d => ({
    lineNo: d.lineNo, countedQty: Number(d.countedQty), diffReasonCd: d.diffReasonCd, diffReasonText: d.diffReasonText, remarks: d.remarks,
  }))))
  try {
    if (!inputs.length) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
    await stockTakeApi.updateCounts(no, inputs)
    if (!intent.current(token) || no !== form.stockTakeNo) return
    ElMessage.success(t('wms.common.success'))
    if (revision === edit) await load(no, true)
  } catch (e) { intent.failed(token, e) } finally { intent.finish(token) }
}

function goBack() { router.push('/wms/stock-take-list') }

watch(() => route.query.no, async no => {
  intent.invalidate(); reads++; resetForm()
  if (typeof no === 'string' && no) await load(no)
}, { immediate: true })
</script>

<style scoped>
.wms-stock-take { padding: 16px; padding-bottom: 60px; }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line-soft); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
.plus { color: var(--cp-ok); font-weight: 600; }
.minus { color: var(--cp-danger); font-weight: 600; }
.dash { color: var(--cp-faint); }
</style>
