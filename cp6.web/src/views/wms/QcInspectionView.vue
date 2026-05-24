<template>
  <div class="wms-qc">
    <el-card v-if="mode === 'list'" shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.qc.fld.inspectionNo')"><el-input v-model="query.inspectionNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.inbound.fld.no')"><el-input v-model="query.inboundNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 140px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.qc.fld.judgement')">
          <el-select v-model="query.finalJudgement" clearable style="width: 140px">
            <el-option v-for="(l, v) in judgementMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="bridgeDialog = true">{{ t('wms.qc.btn.fromInbound') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-if="mode === 'list'" shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="650" highlight-current-row>
        <el-table-column prop="inspectionNo" :label="t('wms.qc.fld.inspectionNo')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.qc.fld.judgement')" width="120">
          <template #default="{ row }">
            <el-tag v-if="row.finalJudgement" :type="judgementTagOf(row.finalJudgement)" size="small">{{ judgementMap[row.finalJudgement] }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="inboundNo" :label="t('wms.inbound.fld.no')" width="180" />
        <el-table-column prop="supplierName" :label="t('wms.inbound.fld.supplierName')" min-width="160" show-overflow-tooltip />
        <el-table-column prop="arrivalDateTime" :label="t('wms.qc.fld.arrivalDateTime')" width="160">
          <template #default="{ row }">{{ row.arrivalDateTime?.replace('T', ' ').slice(0, 16) }}</template>
        </el-table-column>
        <el-table-column prop="generatedReceiptNo" :label="t('wms.qc.fld.generatedReceipt')" width="180" />
        <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row.inspectionNo)">{{ t('wms.common.open') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Detail editor -->
    <template v-if="mode === 'detail' && current">
      <el-card shadow="never">
        <template #header>
          <div style="display: flex; align-items: center; gap: 12px">
            <span style="font-weight: 600">{{ t('wms.qc.title') }} [{{ current.inspectionNo }}]</span>
            <el-tag :type="statusTagOf(current.status)" size="small">{{ statusMap[current.status] }}</el-tag>
            <el-tag v-if="current.finalJudgement" :type="judgementTagOf(current.finalJudgement)" size="small">
              {{ judgementMap[current.finalJudgement] }}
            </el-tag>
          </div>
        </template>
        <el-descriptions :column="3" size="small" border>
          <el-descriptions-item :label="t('wms.inbound.fld.no')">{{ current.inboundNo || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.inbound.fld.supplierName')">{{ current.supplierName || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.qc.fld.arrivalDateTime')">{{ current.arrivalDateTime?.replace('T', ' ').slice(0, 16) }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.qc.fld.inspector')">{{ current.inspectorCd || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.qc.fld.generatedReceipt')">{{ current.generatedReceiptNo || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.qc.fld.judgementReason')">{{ current.judgementReason || '—' }}</el-descriptions-item>
        </el-descriptions>
      </el-card>

      <el-card shadow="never" style="margin-top: 12px">
        <template #header><span style="font-weight: 600">{{ t('wms.common.detail') }}</span></template>
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
          <el-button v-if="canJudge" type="success" @click="onJudgeClick">{{ t('wms.qc.btn.judge') }}</el-button>
          <el-button v-if="canCancel" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>

    <el-dialog v-model="bridgeDialog" :title="t('wms.qc.btn.fromInbound')" width="500">
      <el-form size="small" label-width="160px">
        <el-form-item :label="t('wms.inbound.fld.no')">
          <el-input v-model="bridgeInboundNo" placeholder="例: IN20260523-00001">
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
          <el-input v-model="judgeForm.acceptWarehouseCd" placeholder="（空=元入庫予定の倉庫）" />
        </el-form-item>
        <div v-if="judgeForm.finalJudgement === 'PASS'" style="margin-left: 160px; color: #909399; font-size: 12px">
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
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { qcInspectionApi } from '@/api/wms/qcInspection'
import type { QcInspection, QcInspectionSearchQuery, QcJudgeRequest } from '@/types/wms'

const { t } = useI18n()

const mode = ref<'list' | 'detail'>('list')
const query = reactive<QcInspectionSearchQuery>({ pageSize: 100 })
const rows = ref<QcInspection[]>([])
const loading = ref(false)

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

function statusTagOf(s: number): 'info' | 'primary' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function judgementTagOf(j: string): 'success' | 'warning' | 'danger' | 'info' {
  return ({ PASS: 'success', CONDITIONAL: 'warning', HOLD: 'warning', FAIL: 'danger', RETURN: 'danger' } as const)[j as 'PASS'] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reload() {
  loading.value = true
  try { rows.value = (await qcInspectionApi.search(query)).data || [] }
  finally { loading.value = false }
}

async function openDetail(no: string) {
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

onMounted(reload)
</script>

<style scoped>
.wms-qc { padding: 16px; padding-bottom: 60px; }
.search-card { margin-bottom: 12px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
