<template>
  <div class="wms-slotting">
    <el-card v-if="mode === 'list'" shadow="never" class="search-card">
      <el-form inline size="small">
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="filterWh" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="filterStatus" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="analyzeDialog = true">{{ t('wms.slotting.btn.analyze') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-if="mode === 'list'" shadow="never">
      <el-table :data="plans" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="slottingPlanNo" :label="t('wms.slotting.fld.no')" width="200" />
        <el-table-column :label="t('wms.common.status')" width="120">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="100" />
        <el-table-column prop="analysisDays" :label="t('wms.slotting.fld.analysisDays')" width="130" align="right" />
        <el-table-column prop="txnSampleCount" :label="t('wms.slotting.fld.sampleCount')" width="130" align="right" />
        <el-table-column prop="recommendationCount" :label="t('wms.slotting.fld.recCount')" width="130" align="right" />
        <el-table-column prop="analyzedAt" :label="t('wms.slotting.fld.analyzedAt')" width="160">
          <template #default="{ row }">{{ row.analyzedAt?.replace('T', ' ').slice(0, 16) || '—' }}</template>
        </el-table-column>
        <el-table-column prop="approverCd" :label="t('wms.stocktake.fld.approver')" width="120" />
        <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row.slottingPlanNo)">{{ t('wms.common.open') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <template v-if="mode === 'detail' && currentResult">
      <el-card shadow="never">
        <template #header>
          <div style="display: flex; align-items: center; gap: 12px">
            <span style="font-weight: 600">{{ t('wms.slotting.title') }} [{{ currentResult.plan.slottingPlanNo }}]</span>
            <el-tag :type="statusTagOf(currentResult.plan.status)" size="small">{{ statusMap[currentResult.plan.status] }}</el-tag>
          </div>
        </template>
        <el-descriptions :column="4" size="small" border>
          <el-descriptions-item :label="t('wms.common.warehouse')">{{ currentResult.plan.warehouseCd }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.slotting.fld.analysisDays')">{{ currentResult.plan.analysisDays }} {{ t('wms.slotting.unit.day') }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.slotting.fld.sampleCount')">{{ currentResult.plan.txnSampleCount }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.slotting.fld.recCount')">{{ currentResult.plan.recommendationCount }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.slotting.fld.analyzedAt')">{{ currentResult.plan.analyzedAt?.replace('T', ' ').slice(0, 16) }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.stocktake.fld.approver')">{{ currentResult.plan.approverCd || '—' }}</el-descriptions-item>
        </el-descriptions>
      </el-card>

      <el-card shadow="never" style="margin-top: 12px">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <span style="font-weight: 600">
              {{ t('wms.slotting.rec.title') }}
              <el-tag type="warning" size="small" style="margin-left: 8px">
                {{ t('wms.slotting.rec.needsMove') }}: {{ relocCount }}
              </el-tag>
            </span>
          </div>
        </template>
        <el-table :data="currentResult.recommendations" border size="small" stripe max-height="500">
          <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
          <el-table-column :label="t('wms.slotting.rec.rank')" width="80" align="center">
            <template #default="{ row }">
              <el-tag :type="rankTagOf(row.abcRank)" size="small">{{ row.abcRank }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="productCd" :label="t('wms.common.product')" min-width="140" />
          <el-table-column prop="outCount" :label="t('wms.slotting.rec.outCount')" width="100" align="right" />
          <el-table-column prop="outQty" :label="t('wms.slotting.rec.outQty')" width="120" align="right">
            <template #default="{ row }">{{ formatQty(row.outQty) }}</template>
          </el-table-column>
          <el-table-column prop="currentLocationCd" :label="t('wms.slotting.rec.currentLoc')" width="180" />
          <el-table-column prop="recommendedLocationPattern" :label="t('wms.slotting.rec.recPattern')" width="180" />
          <el-table-column :label="t('wms.slotting.rec.needsMove')" width="110" align="center">
            <template #default="{ row }">
              <el-tag v-if="row.needsRelocation" type="warning" size="small">{{ t('wms.common.confirm') }}</el-tag>
              <span v-else style="color: #909399">—</span>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-affix position="bottom" :offset="0">
        <div class="action-bar">
          <el-button @click="mode = 'list'">{{ t('wms.common.back') }}</el-button>
          <el-button v-if="currentResult.plan.status === 1" type="success" @click="onApprove">{{ t('wms.stocktake.btn.approve') }}</el-button>
          <el-button v-if="currentResult.plan.status !== 9 && currentResult.plan.status !== 2" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>

    <el-dialog v-model="analyzeDialog" :title="t('wms.slotting.btn.analyze')" width="500">
      <el-form :model="analyzeForm" label-width="160px" size="small">
        <el-form-item :label="t('wms.common.warehouse')" required>
          <el-input v-model="analyzeForm.warehouseCd" maxlength="10" />
        </el-form-item>
        <el-form-item :label="t('wms.slotting.fld.analysisDays')">
          <el-input-number v-model="analyzeForm.analysisDays" :min="1" :max="365" controls-position="right" />
        </el-form-item>
        <el-alert type="info" :closable="false" :title="t('wms.slotting.msg.analyzeHint')" />
      </el-form>
      <template #footer>
        <el-button @click="analyzeDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onAnalyze" :loading="saving">{{ t('wms.slotting.btn.analyze') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { slottingApi } from '@/api/wms/logistics'
import type { SlottingPlan, SlottingPlanResult } from '@/types/wms'

const { t } = useI18n()
const mode = ref<'list' | 'detail'>('list')
const filterWh = ref('')
const filterStatus = ref<number | undefined>()
const plans = ref<SlottingPlan[]>([])
const loading = ref(false)
const currentResult = ref<SlottingPlanResult | null>(null)
const analyzeDialog = ref(false)
const analyzeForm = reactive({ warehouseCd: '', analysisDays: 90 })
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.slotting.status.analyzing'),
  1: t('wms.slotting.status.recommended'),
  2: t('wms.slotting.status.approved'),
  9: t('wms.slotting.status.cancelled'),
}))

const relocCount = computed(() =>
  currentResult.value?.recommendations.filter(r => r.needsRelocation).length || 0)

function statusTagOf(s: number): 'info' | 'primary' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function rankTagOf(r: string): 'success' | 'warning' | 'info' {
  return ({ A: 'success', B: 'warning', C: 'info' } as const)[r as 'A'] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reload() {
  loading.value = true
  try { plans.value = (await slottingApi.search(filterWh.value || undefined, filterStatus.value)).data || [] }
  finally { loading.value = false }
}

async function openDetail(no: string) {
  const res = await slottingApi.get(no)
  currentResult.value = res.data
  mode.value = 'detail'
}

async function onAnalyze() {
  if (!analyzeForm.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    const res = await slottingApi.analyze(analyzeForm.warehouseCd, analyzeForm.analysisDays)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.slottingPlanNo}`)
    analyzeDialog.value = false
    await openDetail(res.data.slottingPlanNo)
  } finally { saving.value = false }
}

async function onApprove() {
  if (!currentResult.value) return
  try {
    await ElMessageBox.confirm(t('wms.slotting.msg.approveAsk'), t('wms.common.confirm'), { type: 'warning' })
    await slottingApi.approve(currentResult.value.plan.slottingPlanNo)
    ElMessage.success(t('wms.common.success'))
    await openDetail(currentResult.value.plan.slottingPlanNo)
  } catch { /* */ }
}

async function onCancel() {
  if (!currentResult.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await slottingApi.cancel(currentResult.value.plan.slottingPlanNo)
    ElMessage.success(t('wms.common.success'))
    await openDetail(currentResult.value.plan.slottingPlanNo)
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-slotting { padding: 16px; padding-bottom: 60px; }
.search-card { margin-bottom: 12px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
