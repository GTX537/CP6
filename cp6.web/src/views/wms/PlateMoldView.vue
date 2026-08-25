<!--
  版・金型在庫 —— 査詢一覧ページ → CpPageShell + CpListPage（単表スクロール paginated=false）。
  状態=kind:'tag'+map（共有 Tone）；種別=map（タグなし文案置換）；寿命比率=col slot（el-progress 保持）；操作=col slot（状態別ボタン, 補償 #16）。
  新規/使用記録/メンテ開始/寿命警報 は編集ダイアログとして el-dialog 保持（CpFormDialog 契約に載らない複合フォーム）。
  変更後は listRef.reload()（:key 再マウント不使用）。
-->
<template>
  <div class="wms-plate-mold">
    <CpPageShell :title="t('wms.plate.title')" :count="total">
      <template #actions>
        <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        <el-button type="warning" @click="openWarnings">{{ t('wms.plate.btn.warnings') }}</el-button>
      </template>

      <CpListPage
        ref="listRef"
        :columns="columns"
        :fetch="fetchList"
        :search-fields="searchFields"
        :filter-labels="filterLabels"
        :paginated="false"
        @total-change="total = $event"
      >
        <template #col-lifeRatio="{ row }">
          <div class="life-txt">{{ formatQty(row.usedShots) || 0 }} / {{ formatQty(row.maxShots) || '—' }}</div>
          <el-progress
            v-if="row.maxShots"
            :percentage="Math.min(100, Math.round((row.usedShots / row.maxShots) * 100))"
            :stroke-width="6" :show-text="false"
            :status="lifeStatus(row)"
          />
        </template>
        <template #col-_action="{ row }">
          <el-button v-if="row.status === 0" link type="primary" size="small" @click="openUse(row)">{{ t('wms.plate.btn.use') }}</el-button>
          <el-button v-if="row.status === 0 || row.status === 2" link type="warning" size="small" @click="openMaintStart(row)">{{ t('wms.plate.btn.maintStart') }}</el-button>
          <el-button v-if="row.status === 1" link type="success" size="small" @click="onMaintEnd(row)">{{ t('wms.plate.btn.maintEnd') }}</el-button>
          <el-button v-if="row.status !== 3" v-permission="'wms-plate-mold:dispose'" link type="danger" size="small" @click="onDiscard(row)">{{ t('wms.plate.btn.discard') }}</el-button>
        </template>
      </CpListPage>
    </CpPageShell>

    <!-- 新建 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.plate.dlg.create')" width="700">
      <el-alert :title="t('wms.plate.msg.lifeAuto')" type="info" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.type')" required>
            <el-select v-model="editing.plateType">
              <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.colorCount')"><el-input-number v-model="editing.colorCount" :min="1" :max="12" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.customer')"><el-input v-model="editing.customerCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.vmi.fld.customerName')"><el-input v-model="editing.customerName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.product')"><el-input v-model="editing.productCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.productName')"><el-input v-model="editing.productName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.sizeNote')"><el-input v-model="editing.sizeNote" maxlength="40" placeholder="540×750" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.madeDate')"><el-date-picker v-model="editing.madeDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.madeCost')"><el-input-number v-model="editing.madeCost" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.maxShots')"><el-input-number v-model="editing.maxShots" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.plate.fld.nextMaint')"><el-date-picker v-model="editing.nextMaintenanceDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')"><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.location')"><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 使用記録 Dialog -->
    <el-dialog v-model="useDialog" :title="t('wms.plate.dlg.use') + ' — ' + useTarget?.plateNo" width="420">
      <el-form label-width="140px" size="small">
        <el-form-item :label="t('wms.plate.fld.usedShots')">
          <el-tag>{{ formatQty(useTarget?.usedShots) || 0 }} / {{ formatQty(useTarget?.maxShots) || '—' }}</el-tag>
        </el-form-item>
        <el-form-item :label="t('wms.plate.fld.shots')" required>
          <el-input-number v-model="useShots" :min="1" controls-position="right" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="useDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onUse" :loading="saving">{{ t('wms.plate.btn.use') }}</el-button>
      </template>
    </el-dialog>

    <!-- メンテ開始 Dialog -->
    <el-dialog v-model="maintDialog" :title="t('wms.plate.dlg.maintStart') + ' — ' + maintTarget?.plateNo" width="420">
      <el-alert :title="t('wms.plate.msg.maintReset')" type="warning" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form label-width="140px" size="small">
        <el-form-item :label="t('wms.plate.fld.nextMaint')">
          <el-date-picker v-model="maintNextDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="maintDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onMaintStart" :loading="saving">{{ t('wms.plate.btn.maintStart') }}</el-button>
      </template>
    </el-dialog>

    <!-- 寿命警報 Dialog -->
    <el-dialog v-model="warningsDialog" :title="t('wms.plate.btn.warnings')" width="800">
      <el-alert :title="t('wms.plate.msg.warnHint')" type="warning" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-table :data="warningsList" border stripe size="small" max-height="450">
        <el-table-column prop="plateNo" :label="t('wms.plate.fld.no')" width="160" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><CpTag :tone="statusTone(row.status)">{{ statusMap[row.status] }}</CpTag></template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('wms.plate.fld.customer')" width="100" />
        <el-table-column prop="productCd" :label="t('wms.plate.fld.product')" width="120" />
        <el-table-column :label="t('wms.plate.fld.lifeRatio')" min-width="200">
          <template #default="{ row }">
            <div class="life-txt">{{ formatQty(row.usedShots) || 0 }} / {{ formatQty(row.maxShots) || '—' }}</div>
            <el-progress v-if="row.maxShots"
              :percentage="Math.min(100, Math.round((row.usedShots / row.maxShots) * 100))"
              :stroke-width="8" :status="lifeStatus(row)" />
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { plateMoldApi } from '@/api/wms/paperIndustry2'
import type { PlateMoldStock, PlateMoldSearchQuery } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()
const total = ref<number>()
const saving = ref(false)
const listRef = ref<ListPageExpose | null>(null)

const createDialog = ref(false)
const editing = ref<any>(null)

const useDialog = ref(false)
const useTarget = ref<PlateMoldStock | null>(null)
const useShots = ref(0)

const maintDialog = ref(false)
const maintTarget = ref<PlateMoldStock | null>(null)
const maintNextDate = ref<string | undefined>(undefined)

const warningsDialog = ref(false)
const warningsList = ref<PlateMoldStock[]>([])

const typeMap = computed<Record<string, string>>(() => ({
  PLATE: t('wms.plate.type.plate'),
  MOLD: t('wms.plate.type.mold'),
  CYL: t('wms.plate.type.cyl'),
  OTHER: t('wms.plate.type.other'),
}))

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.plate.status.usable'),
  1: t('wms.plate.status.maintenance'),
  2: t('wms.plate.status.lifeReached'),
  3: t('wms.plate.status.discarded'),
}))

function statusTone(s: number): Tone {
  return ({ 0: 'ok', 1: 'warn', 2: 'danger', 3: 'muted' } as const)[s as 0] || 'info'
}

function lifeStatus(row: PlateMoldStock): 'success' | 'warning' | 'exception' | undefined {
  if (!row.maxShots) return undefined
  const r = row.usedShots / row.maxShots
  if (r >= 1) return 'exception'
  if (r >= 0.9) return 'warning'
  return 'success'
}

// —— 一覧 ——
const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const columns = computed<ListColumn<PlateMoldStock>[]>(() => [
  { prop: 'plateNo', label: t('wms.plate.fld.no'), kind: 'mono', width: 160 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: statusMap.value[v as number] ?? '', tone: statusTone(v as number) }) },
  { prop: 'plateType', label: t('wms.plate.fld.type'), width: 80,
    map: (v) => ({ label: typeMap.value[v as string] || (v == null ? '' : String(v)) }) },
  { prop: 'customerCd', label: t('wms.plate.fld.customer'), width: 100 },
  { prop: 'productCd', label: t('wms.plate.fld.product'), width: 100 },
  { prop: 'productName', label: t('wms.common.productName'), minWidth: 160, overflowTooltip: true },
  { prop: 'colorCount', label: t('wms.plate.fld.colorCount'), width: 80, kind: 'num' },
  { prop: 'sizeNote', label: t('wms.plate.fld.sizeNote'), width: 120 },
  { prop: 'lifeRatio', label: t('wms.plate.fld.lifeRatio'), width: 180 },
  { prop: 'lastUsedAt', label: t('wms.plate.fld.lastUsed'), kind: 'datetime', width: 180 },
  { prop: 'nextMaintenanceDate', label: t('wms.plate.fld.nextMaint'), width: 130 },
  { prop: '_action', label: t('wms.common.action'), width: 260, fixed: 'right' },
])

const searchFields = computed<FilterField[]>(() => [
  { key: 'plateNo', label: t('wms.plate.fld.no'), type: 'text' },
  {
    key: 'plateType', label: t('wms.plate.fld.type'), type: 'select',
    options: Object.entries(typeMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  { key: 'customerCd', label: t('wms.plate.fld.customer'), type: 'text' },
  { key: 'productCd', label: t('wms.plate.fld.product'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const fetchList: ListFetch<PlateMoldStock> = async ({ filters }) => {
  const f = filters as Record<string, unknown>
  const q: PlateMoldSearchQuery = { pageSize: 500 }
  if (f.plateNo) q.plateNo = String(f.plateNo)
  if (f.plateType) q.plateType = String(f.plateType)
  if (f.customerCd) q.customerCd = String(f.customerCd)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const all = (await plateMoldApi.search(q)).data || []
  return { rows: all, total: all.length }
}

function reloadList() { listRef.value?.reload() }

function openCreate() {
  editing.value = {
    plateType: 'PLATE', customerCd: '', customerName: '',
    productCd: '', productName: '', colorCount: 4, sizeNote: '',
    madeDate: undefined, madeCost: undefined, maxShots: undefined,
    nextMaintenanceDate: undefined, warehouseCd: '', locationCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await plateMoldApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.plateNo}`)
    createDialog.value = false
    reloadList()
  } finally { saving.value = false }
}

function openUse(row: PlateMoldStock) {
  useTarget.value = row
  useShots.value = 0
  useDialog.value = true
}

async function onUse() {
  if (!useTarget.value || useShots.value <= 0) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await plateMoldApi.recordUsage(useTarget.value.plateNo, useShots.value)
    ElMessage.success(t('wms.common.success'))
    useDialog.value = false
    reloadList()
  } finally { saving.value = false }
}

function openMaintStart(row: PlateMoldStock) {
  maintTarget.value = row
  maintNextDate.value = row.nextMaintenanceDate
  maintDialog.value = true
}

async function onMaintStart() {
  if (!maintTarget.value) return
  saving.value = true
  try {
    await plateMoldApi.startMaintenance(maintTarget.value.plateNo, maintNextDate.value)
    ElMessage.success(t('wms.common.success'))
    maintDialog.value = false
    reloadList()
  } finally { saving.value = false }
}

async function onMaintEnd(row: PlateMoldStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.plate.btn.maintEnd')}: ${row.plateNo}`, t('wms.common.confirm'), { type: 'warning' })
    await plateMoldApi.completeMaintenance(row.plateNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}

async function onDiscard(row: PlateMoldStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.plate.btn.discard')}: ${row.plateNo}`, t('wms.common.confirm'), { type: 'warning' })
    await plateMoldApi.discard(row.plateNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}

async function openWarnings() {
  saving.value = true
  try {
    warningsList.value = (await plateMoldApi.warnings(0.9)).data || []
    warningsDialog.value = true
  } finally { saving.value = false }
}
</script>

<style scoped>
.wms-plate-mold { padding: 16px; }
.life-txt { font-size: var(--cp-fs-2xs); }
</style>
