<!--
  スロッティング計画 —— WMS 迁移批次2。一覧/明细双态同一组件：
  一覧态 → CpPageShell + CpListPage（状態 tag map / 数字列 num / 実行日時 map；操作 col slot 開く）；分析用 CpFormDialog（default slot）。
  明细态 → CpDetailPanel（基本情報）＋ 推薦 el-table（特殊子表：ABCランク/要移動 用 CpTag，保留 el-table 承载静态推薦数据）＋ el-affix 操作条。
  CpListPage 用 v-show 常挂（切明细不卸载），保留筛选/页码上下文；模式与原页 mode ref 一致，功能零丢失。
-->
<template>
  <CpPageShell :title="t('wms.slotting.title')" :count="mode === 'list' ? total : undefined">
    <template #actions>
      <el-button v-if="mode === 'list'" @click="analyzeDialog = true">{{ t('wms.slotting.btn.analyze') }}</el-button>
      <el-button v-else @click="backToList">{{ t('wms.common.back') }}</el-button>
    </template>

    <!-- 一覧态（常挂，切明细用 v-show 隐藏以保留筛选上下文） -->
    <div v-show="mode === 'list'">
      <CpListPage
        ref="listRef"
        :columns="columns"
        :fetch="fetchList"
        :search-fields="searchFields"
        :filter-labels="filterLabels"
        @total-change="total = $event"
      >
        <template #col-_action="{ row }">
          <el-button link type="primary" size="small" @click="openDetail(row.slottingPlanNo)">{{ t('wms.common.open') }}</el-button>
        </template>
      </CpListPage>
    </div>

    <!-- 明细态 -->
    <template v-if="mode === 'detail' && currentResult">
      <div class="cp-card">
        <CpSectionHeader :title="`${t('wms.slotting.title')} [${currentResult.plan.slottingPlanNo}]`">
          <template #extra>
            <CpTag :tone="statusTone(currentResult.plan.status)">{{ codeLabel(statusMap, currentResult.plan.status) }}</CpTag>
          </template>
        </CpSectionHeader>
        <div class="cp-card-body">
          <CpDetailPanel :cols="4" :items="planItems" />
        </div>
      </div>

      <div class="cp-card">
        <CpSectionHeader :title="t('wms.slotting.rec.title')">
          <template #extra>
            <CpTag tone="warn">{{ t('wms.slotting.rec.needsMove') }}: {{ relocCount }}</CpTag>
          </template>
        </CpSectionHeader>
        <div class="cp-card-body">
          <el-table :data="currentResult.recommendations" border size="small" stripe max-height="500">
            <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
            <el-table-column :label="t('wms.slotting.rec.rank')" width="80" align="center">
              <template #default="{ row }"><CpTag :tone="rankTone(row.abcRank)">{{ row.abcRank }}</CpTag></template>
            </el-table-column>
            <el-table-column prop="productCd" :label="t('wms.common.product')" min-width="140" />
            <el-table-column prop="outCount" :label="t('wms.slotting.rec.outCount')" width="100" align="right" />
            <el-table-column prop="outQty" :label="t('wms.slotting.rec.outQty')" width="120" align="right">
              <template #default="{ row }">{{ formatQty(row.outQty) }}</template>
            </el-table-column>
            <el-table-column prop="currentLocationCd" :label="t('wms.slotting.rec.currentLoc')" width="180" />
            <el-table-column prop="recommendedLocationPattern" :label="t('wms.slotting.rec.recPattern')" width="180" />
            <el-table-column prop="targetLocationCd" :label="t('wms.replenish.fld.toLoc')" width="160">
              <template #default="{ row }">{{ row.targetLocationCd || '—' }}</template>
            </el-table-column>
            <el-table-column prop="mobileTaskNo" label="MOVE Task" width="190">
              <template #default="{ row }"><span class="cp-mono">{{ row.mobileTaskNo || '—' }}</span></template>
            </el-table-column>
            <el-table-column :label="t('wms.slotting.rec.needsMove')" width="110" align="center">
              <template #default="{ row }">
                <CpTag v-if="row.needsRelocation" tone="warn">{{ t('wms.common.confirm') }}</CpTag>
                <span v-else class="cp-dash">—</span>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>

      <el-affix position="bottom" :offset="0">
        <div class="action-bar">
          <el-button v-if="currentResult.plan.status === 1" type="success" @click="onApprove">{{ t('wms.stocktake.btn.approve') }}</el-button>
          <el-button v-if="currentResult.plan.status !== 9" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>

    <!-- 分析 -->
    <CpFormDialog
      v-model="analyzeDialog"
      :title="t('wms.slotting.btn.analyze')"
      width="500"
      :form="analyzeForm"
      :rules="analyzeRules"
      :submit="submitAnalyze"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.slotting.btn.analyze') }"
    >
      <el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd">
        <el-input v-model="analyzeForm.warehouseCd" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.slotting.fld.analysisDays')" prop="analysisDays">
        <el-input-number v-model="analyzeForm.analysisDays" :min="1" :max="365" controls-position="right" />
      </el-form-item>
      <el-alert type="info" :closable="false" :title="t('wms.slotting.msg.analyzeHint')" />
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import CpDetailPanel, { type DetailItem } from '@/components/templates/CpDetailPanel.vue'
import CpSectionHeader from '@/components/base/CpSectionHeader.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { slottingApi } from '@/api/wms/logistics'
import type { SlottingPlan, SlottingPlanResult } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()
const mode = ref<'list' | 'detail'>('list')
const total = ref<number>()
const listRef = ref<ListPageExpose>()
const currentResult = ref<SlottingPlanResult | null>(null)
// 明细内承認/取消后置脏标记：返回一覧时命令式刷新 CpListPage（v-show 常挂不自动重取）
const listDirty = ref(false)

function backToList() {
  if (listDirty.value) { listRef.value?.reload(); listDirty.value = false }
  mode.value = 'list'
}

// —— 码值映射 ——
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.slotting.status.analyzing'),
  1: t('wms.slotting.status.recommended'),
  2: t('wms.slotting.status.approved'),
  9: t('wms.slotting.status.cancelled'),
}))
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'info', 2: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}
function rankTone(r: string): Tone {
  return ({ A: 'ok', B: 'warn', C: 'muted' } as const)[r as 'A'] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const relocCount = computed(() =>
  currentResult.value?.recommendations.filter(r => r.needsRelocation).length || 0)

// —— 明细基本情報（CpDetailPanel） ——
const planItems = computed<DetailItem[]>(() => {
  const p = currentResult.value?.plan
  if (!p) return []
  return [
    { label: t('wms.common.warehouse'), value: p.warehouseCd },
    { label: t('wms.slotting.fld.analysisDays'), value: `${p.analysisDays} ${t('wms.slotting.unit.day')}` },
    { label: t('wms.slotting.fld.sampleCount'), value: p.txnSampleCount, kind: 'num' },
    { label: t('wms.slotting.fld.recCount'), value: p.recommendationCount, kind: 'num' },
    { label: t('wms.slotting.fld.analyzedAt'), value: p.analyzedAt ? String(p.analyzedAt).replace('T', ' ').slice(0, 16) : '—' },
    { label: t('wms.stocktake.fld.approver'), value: p.approverCd || '—' },
  ]
})

// —— 一覧列 ——
const columns = computed<ListColumn<SlottingPlan>[]>(() => [
  { prop: 'slottingPlanNo', label: t('wms.slotting.fld.no'), kind: 'mono', width: 200 },
  { prop: 'status', label: t('wms.common.status'), width: 120, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 100 },
  { prop: 'analysisDays', label: t('wms.slotting.fld.analysisDays'), width: 130, kind: 'num' },
  { prop: 'txnSampleCount', label: t('wms.slotting.fld.sampleCount'), width: 130, kind: 'num' },
  { prop: 'recommendationCount', label: t('wms.slotting.fld.recCount'), width: 130, kind: 'num' },
  { prop: 'analyzedAt', label: t('wms.slotting.fld.analyzedAt'), width: 160,
    map: (v) => ({ label: v ? String(v).replace('T', ' ').slice(0, 16) : '—' }) },
  { prop: 'approverCd', label: t('wms.stocktake.fld.approver'), width: 120 },
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
])

const filterLabels = computed(() => ({ search: t('wms.common.search'), reset: t('wms.common.clear') }))

const searchFields = computed<FilterField[]>(() => [
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

// —— 取数：slottingApi.search(wh, status) 返回扁平数组无 total → 客户端分页 ——
const fetchList: ListFetch<SlottingPlan> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const wh = f.warehouseCd ? String(f.warehouseCd) : undefined
  const status = f.status !== undefined && f.status !== '' ? Number(f.status) : undefined
  const res = await slottingApi.search(wh, status)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

async function openDetail(no: string) {
  const res = await slottingApi.get(no)
  currentResult.value = res.data
  mode.value = 'detail'
}

// —— 分析弹窗 ——
const analyzeDialog = ref(false)
const analyzeForm = reactive({ warehouseCd: '', analysisDays: 90 })
const analyzeRules = computed<FormRules>(() => ({
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
async function submitAnalyze() {
  const res = await slottingApi.analyze(analyzeForm.warehouseCd, analyzeForm.analysisDays)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.slottingPlanNo}`)
  await openDetail(res.data.slottingPlanNo)
}

// —— 明细操作 ——
async function onApprove() {
  if (!currentResult.value) return
  try {
    await ElMessageBox.confirm(t('wms.slotting.msg.approveAsk'), t('wms.common.confirm'), { type: 'warning' })
    const res = await slottingApi.approve(currentResult.value.plan.slottingPlanNo)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.generated} MOVE`)
    listDirty.value = true
    await openDetail(currentResult.value.plan.slottingPlanNo)
  } catch { /* */ }
}
async function onCancel() {
  if (!currentResult.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await slottingApi.cancel(currentResult.value.plan.slottingPlanNo)
    ElMessage.success(t('wms.common.success'))
    listDirty.value = true
    await openDetail(currentResult.value.plan.slottingPlanNo)
  } catch { /* */ }
}
</script>

<style scoped>
/* 卡片壳（复用设计系统卡片 token；非硬编码） */
.cp-card { background: var(--cp-card); border-radius: var(--cp-r-md); box-shadow: var(--cp-shadow-1); overflow: hidden; }
.cp-card-body { padding: 16px 20px; }
.cp-dash { color: var(--cp-muted); }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
