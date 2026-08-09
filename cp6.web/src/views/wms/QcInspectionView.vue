<!--
  受入検品 —— list+detail 単一ファイル（mode トグル）。
  list モード → CpPageShell（:count←total）+ CpListPage（単表スクロール paginated=false）。
    状態=kind:'tag'+map（共有 Tone）；判定=col slot（null 時タグ非表示を保つ）；到着日時=col slot（yyyy-MM-dd HH:mm）。
    検索 4：inspectionNo/inboundNo/status/finalJudgement。fromInbound は CpPageShell #actions。
  detail モード（特殊エディタ領域）→ 基本情報を CpDetailPanel 化、明細編集テーブルは保持、action-bar/判定ダイアログを token 化。
    back で mode=list → CpListPage が再マウントし自動 reload。
-->
<template>
  <div class="wms-qc">
    <!-- ───── 一覧 ───── -->
    <CpPageShell v-if="mode === 'list'" :title="t('wms.qc.title')" :count="total">
      <template #actions>
        <el-button @click="bridgeDialog = true">{{ t('wms.qc.btn.fromInbound') }}</el-button>
      </template>

      <CpListPage
        :columns="columns"
        :fetch="fetchList"
        :search-fields="searchFields"
        :filter-labels="filterLabels"
        :paginated="false"
        @total-change="total = $event"
      >
        <template #col-finalJudgement="{ row }">
          <CpTag v-if="row.finalJudgement" :tone="judgementTone(row.finalJudgement)">{{ judgementMap[row.finalJudgement] }}</CpTag>
        </template>
        <template #col-arrivalDateTime="{ row }">{{ row.arrivalDateTime?.replace('T', ' ').slice(0, 16) }}</template>
        <template #col-_action="{ row }">
          <el-button link type="primary" size="small" @click="openDetail(row.inspectionNo)">{{ t('wms.common.open') }}</el-button>
        </template>
      </CpListPage>
    </CpPageShell>

    <!-- ───── 詳細エディタ ───── -->
    <template v-if="mode === 'detail' && current">
      <el-card shadow="never">
        <template #header>
          <div class="card-hd">
            <span class="hd-title">{{ t('wms.qc.title') }} [{{ current.inspectionNo }}]</span>
            <CpTag :tone="statusTone(current.status)">{{ statusMap[current.status] }}</CpTag>
            <CpTag v-if="current.finalJudgement" :tone="judgementTone(current.finalJudgement)">{{ judgementMap[current.finalJudgement] }}</CpTag>
          </div>
        </template>
        <CpDetailPanel :cols="3" :items="detailItems" />
      </el-card>

      <el-card shadow="never" style="margin-top: 12px">
        <template #header><span class="hd-title">{{ t('wms.common.detail') }}</span></template>
        <el-table :data="current.items" border size="small" max-height="500">
          <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
          <el-table-column :label="t('wms.common.product')" width="120">
            <template #default="{ row }">{{ row.productCd }}</template>
          </el-table-column>
          <el-table-column :label="t('wms.common.productName')" min-width="160">
            <template #default="{ row }">{{ row.productName }}</template>
          </el-table-column>
          <el-table-column :label="t('wms.inbound.col.expectedQty')" width="100" align="right">
            <template #default="{ row }">{{ formatQty(row.expectedQty) }}</template>
          </el-table-column>
          <el-table-column :label="t('wms.qc.col.receivedQty')" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.receivedQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.qc.col.acceptedQty')" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.acceptedQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.qc.col.rejectedQty')" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.rejectedQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.qc.col.pendingQty')" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.pendingQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.qc.col.defectReason')" width="160">
            <template #default="{ row }">
              <el-input v-model="row.defectReasonCd" :disabled="!editable" maxlength="20" />
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-affix position="bottom" :offset="0">
        <div class="action-bar">
          <el-button @click="mode = 'list'">{{ t('wms.common.back') }}</el-button>
          <el-button v-if="editable" type="primary" @click="onSaveItems" :loading="saving">{{ t('wms.common.save') }}</el-button>
          <el-button v-if="canJudge" v-permission="'wms-qc-inspection:judge'" type="success" @click="onJudgeClick">{{ t('wms.qc.btn.judge') }}</el-button>
          <el-button v-if="canCancel" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>

    <el-dialog v-model="bridgeDialog" :title="t('wms.qc.btn.fromInbound')" width="500">
      <el-form size="small" label-width="160px">
        <el-form-item :label="t('wms.inbound.fld.no')">
          <el-input v-model="bridgeInboundNo" :placeholder="t('例: {sample}', { sample: 'IN20260523-00001' })">
            <template #append>
              <el-button type="primary" @click="onBridge" :loading="bridging">{{ t('wms.common.expand') }}</el-button>
            </template>
          </el-input>
        </el-form-item>
      </el-form>
    </el-dialog>

    <el-dialog v-model="judgeDialog" :title="t('wms.qc.btn.judge')" width="540">
      <el-form :model="judgeForm" label-width="160px" size="small">
        <el-form-item :label="t('wms.qc.fld.judgement')" required>
          <el-select v-model="judgeForm.finalJudgement" style="width: 100%">
            <el-option v-for="(l, v) in judgementMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.qc.fld.judgementReason')">
          <el-input v-model="judgeForm.reason" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item v-if="judgeForm.finalJudgement === 'PASS'" :label="t('wms.qc.fld.acceptWh')">
          <el-input v-model="judgeForm.acceptWarehouseCd" :placeholder="t('（空=元入庫予定の倉庫）')" />
        </el-form-item>
        <div v-if="judgeForm.finalJudgement === 'PASS'" class="judge-hint">
          {{ t('wms.qc.msg.passAutoReceipt') }}
        </div>
      </el-form>
      <template #footer>
        <el-button @click="judgeDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onJudgeConfirm" :loading="saving">{{ t('wms.common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpDetailPanel, { type DetailItem } from '@/components/templates/CpDetailPanel.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { qcInspectionApi } from '@/api/wms/qcInspection'
import type { QcInspection, QcInspectionSearchQuery, QcJudgeRequest } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const mode = ref<'list' | 'detail'>('list')
const total = ref<number>()

const current = ref<QcInspection | null>(null)
const saving = ref(false)

const bridgeDialog = ref(false)
const bridgeInboundNo = ref('')
const bridging = ref(false)

const judgeDialog = ref(false)
const judgeForm = reactive<QcJudgeRequest>({ finalJudgement: 'PASS' })

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.qc.status.created'),
  1: t('wms.qc.status.inspecting'),
  2: t('wms.qc.status.judged'),
  9: t('wms.qc.status.cancelled'),
}))
const judgementMap = computed<Record<string, string>>(() => ({
  PASS: t('wms.qc.judge.pass'),
  CONDITIONAL: t('wms.qc.judge.conditional'),
  HOLD: t('wms.qc.judge.hold'),
  FAIL: t('wms.qc.judge.fail'),
  RETURN: t('wms.qc.judge.return'),
}))

const editable = computed(() => current.value && (current.value.status === 0 || current.value.status === 1))
const canJudge = computed(() => current.value && current.value.status !== 2 && current.value.status !== 9)
const canCancel = computed(() => current.value && current.value.status !== 2 && current.value.status !== 9)

function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'info', 2: 'ok', 9: 'muted' } as const)[s as 0] || 'info'
}
function judgementTone(j: string): Tone {
  return ({ PASS: 'ok', CONDITIONAL: 'warn', HOLD: 'warn', FAIL: 'danger', RETURN: 'danger' } as const)[j as 'PASS'] || 'info'
}

// —— 一覧 ——
const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const columns = computed<ListColumn<QcInspection>[]>(() => [
  { prop: 'inspectionNo', label: t('wms.qc.fld.inspectionNo'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: statusMap.value[v as number] ?? '', tone: statusTone(v as number) }) },
  { prop: 'finalJudgement', label: t('wms.qc.fld.judgement'), width: 120 },
  { prop: 'inboundNo', label: t('wms.inbound.fld.no'), width: 180 },
  { prop: 'supplierName', label: t('wms.inbound.fld.supplierName'), minWidth: 160, overflowTooltip: true },
  { prop: 'arrivalDateTime', label: t('wms.qc.fld.arrivalDateTime'), width: 160 },
  { prop: 'generatedReceiptNo', label: t('wms.qc.fld.generatedReceipt'), width: 180 },
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
])

const searchFields = computed<FilterField[]>(() => [
  { key: 'inspectionNo', label: t('wms.qc.fld.inspectionNo'), type: 'text' },
  { key: 'inboundNo', label: t('wms.inbound.fld.no'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  {
    key: 'finalJudgement', label: t('wms.qc.fld.judgement'), type: 'select',
    options: Object.entries(judgementMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
])

const fetchList: ListFetch<QcInspection> = async ({ filters }) => {
  const f = filters as Record<string, unknown>
  const q: QcInspectionSearchQuery = { pageSize: 500 }
  if (f.inspectionNo) q.inspectionNo = String(f.inspectionNo)
  if (f.inboundNo) q.inboundNo = String(f.inboundNo)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  if (f.finalJudgement) q.finalJudgement = String(f.finalJudgement)
  const all = (await qcInspectionApi.search(q)).data || []
  return { rows: all, total: all.length }
}

// —— 詳細（基本情報 → CpDetailPanel） ——
const detailItems = computed<DetailItem[]>(() => {
  const c = current.value
  if (!c) return []
  return [
    { label: t('wms.inbound.fld.no'), value: c.inboundNo || '—' },
    { label: t('wms.inbound.fld.supplierName'), value: c.supplierName || '—' },
    { label: t('wms.qc.fld.arrivalDateTime'), value: c.arrivalDateTime?.replace('T', ' ').slice(0, 16) || '—' },
    { label: t('wms.qc.fld.inspector'), value: c.inspectorCd || '—' },
    { label: t('wms.qc.fld.generatedReceipt'), value: c.generatedReceiptNo || '—' },
    { label: t('wms.qc.fld.judgementReason'), value: c.judgementReason || '—' },
  ]
})

async function openDetail(no?: string) {
  if (!no) return
  const res = await qcInspectionApi.get(no)
  current.value = res.data
  mode.value = 'detail'
}

async function onBridge() {
  if (!bridgeInboundNo.value) return
  bridging.value = true
  try {
    const res = await qcInspectionApi.createFromInbound(bridgeInboundNo.value)
    bridgeDialog.value = false
    bridgeInboundNo.value = ''
    await openDetail(res.data.inspectionNo)
    ElMessage.success(t('wms.common.success'))
  } finally { bridging.value = false }
}

async function onSaveItems() {
  if (!current.value) return
  saving.value = true
  try {
    await qcInspectionApi.saveItems(current.value.inspectionNo!, current.value.items)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.inspectionNo!)
  } finally { saving.value = false }
}

function onJudgeClick() {
  judgeForm.finalJudgement = 'PASS'
  judgeForm.reason = ''
  judgeForm.acceptWarehouseCd = undefined
  judgeDialog.value = true
}

async function onJudgeConfirm() {
  if (!current.value) return
  saving.value = true
  try {
    const res = await qcInspectionApi.judge(current.value.inspectionNo!, judgeForm)
    if (res.data.generatedReceiptNo) {
      ElMessage.success(`${t('wms.common.success')}: ${res.data.generatedReceiptNo}`)
    } else {
      ElMessage.success(t('wms.common.success'))
    }
    judgeDialog.value = false
    await openDetail(current.value.inspectionNo!)
  } finally { saving.value = false }
}

async function onCancel() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await qcInspectionApi.cancel(current.value.inspectionNo!)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.inspectionNo!)
  } catch { /* */ }
}
</script>

<style scoped>
.wms-qc { padding: 16px; padding-bottom: 60px; }
.card-hd { display: flex; align-items: center; gap: 12px; }
.hd-title { font-weight: 600; }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line-soft); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
.judge-hint { margin-left: 160px; color: var(--cp-muted); font-size: var(--cp-fs-xs); }
</style>
