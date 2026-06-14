<template>
  <div class="wms-stock-take">
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; align-items: center; gap: 12px">
          <span style="font-weight: 600">{{ t('wms.stocktake.title') }} [{{ form.stockTakeNo }}]</span>
          <el-tag :type="statusTagOf(form.status)" size="small">{{ statusMap[form.status] }}</el-tag>
          <el-tag size="small">{{ typeMap[form.stockTakeType] }}</el-tag>
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
            <span v-if="row.countedQty == null" style="color: #aaa">—</span>
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
            <el-tag size="small" :type="approvalTagOf(row.approvalStatus)">{{ approvalMap[row.approvalStatus] }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="ADJ" width="140">
          <template #default="{ row }">
            <el-tooltip v-if="row.adjustTxnNo" :content="row.adjustTxnNo"><el-tag type="success" size="small">ADJ</el-tag></el-tooltip>
            <span v-else style="color: #aaa">—</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-affix position="bottom" :offset="0">
      <div class="action-bar">
        <el-button @click="goBack">{{ t('wms.common.back') }}</el-button>
        <el-button v-if="canStartCount" type="primary" @click="onStartCount">{{ t('wms.stocktake.btn.startCount') }}</el-button>
        <el-button v-if="canSaveCounts" type="primary" @click="onSaveCounts" :loading="saving">{{ t('wms.common.save') }}</el-button>
        <el-button v-if="canSubmit" type="warning" @click="onSubmit">{{ t('wms.stocktake.btn.submitReview') }}</el-button>
        <el-button v-if="canApprove" type="success" @click="onApprove">{{ t('wms.stocktake.btn.approve') }} + {{ t('wms.stocktake.btn.adjust') }}</el-button>
        <el-button v-if="canCancel" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
      </div>
    </el-affix>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { stockTakeApi } from '@/api/wms/stockTake'
import type { StockTake, StockTakeDetail, StockTakeCountInput } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const saving = ref(false)
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

const editable = computed(() => form.status === 1)
const canStartCount = computed(() => form.status === 0)
const canSaveCounts = computed(() => form.status === 1)
const canSubmit = computed(() => form.status === 1)
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

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'danger' | 'success' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'danger', 4: 'success', 9: 'info' } as const)[s as 0] || 'info'
}
function approvalTagOf(s: number): 'info' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'success', 2: 'success', 9: 'danger' } as const)[s as 0] || 'info'
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

async function load(no: string) {
  const res = await stockTakeApi.get(no)
  Object.assign(form, res.data)
}

async function onStartCount() {
  await stockTakeApi.startCount(form.stockTakeNo)
  ElMessage.success(t('wms.common.success'))
  await load(form.stockTakeNo)
}

async function onSaveCounts() {
  const inputs: StockTakeCountInput[] = form.details
    .filter(d => d.countedQty != null)
    .map(d => ({
      lineNo: d.lineNo,
      countedQty: Number(d.countedQty),
      diffReasonCd: d.diffReasonCd,
      diffReasonText: d.diffReasonText,
      remarks: d.remarks,
    }))
  if (inputs.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    await stockTakeApi.updateCounts(form.stockTakeNo, inputs)
    ElMessage.success(t('wms.common.success'))
    await load(form.stockTakeNo)
  } finally { saving.value = false }
}

async function onSubmit() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await stockTakeApi.submit(form.stockTakeNo)
    ElMessage.success(t('wms.common.success'))
    await load(form.stockTakeNo)
  } catch { /* */ }
}

async function onApprove() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await stockTakeApi.approve(form.stockTakeNo)
    ElMessage.success(t('wms.common.success'))
    await load(form.stockTakeNo)
  } catch { /* */ }
}

async function onCancel() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await stockTakeApi.cancel(form.stockTakeNo)
    ElMessage.success(t('wms.common.success'))
    await load(form.stockTakeNo)
  } catch { /* */ }
}

function goBack() { router.push('/wms/stock-take-list') }

onMounted(async () => {
  const no = route.query.no as string
  if (no) await load(no)
})
</script>

<style scoped>
.wms-stock-take { padding: 16px; padding-bottom: 60px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
.plus { color: var(--el-color-success); font-weight: 600; }
.minus { color: var(--el-color-danger); font-weight: 600; }
</style>
