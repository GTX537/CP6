<template>
  <div class="wms-ink-lot">
    <el-tabs v-model="activeTab" type="card">
      <!-- ───── ロット一覧タブ ───── -->
      <el-tab-pane :label="t('wms.ink.tab.lots')" name="lots">
        <el-card shadow="never" class="search-card">
          <el-form :model="lotQuery" inline size="small">
            <el-form-item :label="t('wms.ink.fld.lotNo')">
              <el-input v-model="lotQuery.inkLotNo" clearable style="width: 180px" />
            </el-form-item>
            <el-form-item :label="t('wms.ink.fld.colorCode')">
              <el-input v-model="lotQuery.colorCode" clearable style="width: 120px" />
            </el-form-item>
            <el-form-item :label="t('wms.ink.fld.inkType')">
              <el-select v-model="lotQuery.inkType" clearable style="width: 120px">
                <el-option v-for="(l, v) in inkTypeMap" :key="v" :label="l" :value="v" />
              </el-select>
            </el-form-item>
            <el-form-item :label="t('wms.ink.fld.openStatus')">
              <el-select v-model="lotQuery.openStatus" clearable style="width: 120px">
                <el-option :label="openMap.UNOPENED" value="UNOPENED" />
                <el-option :label="openMap.OPENED" value="OPENED" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-checkbox v-model="lotQuery.expiringWithin30Days">{{ t('wms.ink.btn.expiring') }}</el-checkbox>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadLots" :loading="loading">{{ t('wms.common.search') }}</el-button>
              <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
              <el-button type="warning" @click="openMixDialog">{{ t('wms.ink.btn.mix') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="lots" border stripe size="small" max-height="600" highlight-current-row>
            <el-table-column prop="inkLotNo" :label="t('wms.ink.fld.lotNo')" width="180" />
            <el-table-column prop="colorCode" :label="t('wms.ink.fld.colorCode')" width="120" />
            <el-table-column :label="t('wms.ink.fld.inkType')" width="100">
              <template #default="{ row }">{{ inkTypeMap[row.inkType] || row.inkType }}</template>
            </el-table-column>
            <el-table-column :label="t('wms.ink.fld.openStatus')" width="100">
              <template #default="{ row }">
                <el-tag :type="row.openStatus === 'OPENED' ? 'warning' : 'success'" size="small">{{ openMap[row.openStatus] }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="expiryDate" :label="t('wms.ink.fld.expiry')" width="120">
              <template #default="{ row }">
                <span :class="expiryClass(row.expiryDate)">{{ row.expiryDate }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="quantity" :label="t('wms.common.qty')" width="90" align="right">
              <template #default="{ row }">{{ formatQty(row.quantity) }} {{ row.unitCd }}</template>
            </el-table-column>
            <el-table-column prop="viscosityCp" :label="t('wms.ink.fld.viscosity')" width="100" align="right" />
            <el-table-column prop="solidContent" :label="t('wms.ink.fld.solidContent')" width="90" align="right" />
            <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
            <el-table-column prop="supplierCd" :label="t('wms.ink.fld.supplier')" width="120" />
            <el-table-column :label="t('wms.common.action')" width="120" fixed="right">
              <template #default="{ row }">
                <el-button v-if="row.openStatus === 'UNOPENED'" link type="primary" size="small" @click="openOpenDialog(row)">{{ t('wms.ink.btn.open') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <!-- ───── 調色履歴タブ ───── -->
      <el-tab-pane :label="t('wms.ink.tab.matches')" name="matches">
        <el-card shadow="never" class="search-card">
          <el-form inline size="small">
            <el-form-item :label="t('wms.vmi.fld.customerCd')">
              <el-input v-model="matchQuery.customerCd" clearable style="width: 140px" />
            </el-form-item>
            <el-form-item :label="t('wms.ink.fld.colorCode')">
              <el-input v-model="matchQuery.colorCode" clearable style="width: 140px" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadMatches" :loading="loading">{{ t('wms.common.search') }}</el-button>
              <el-button @click="openRecordDialog">{{ t('wms.ink.btn.record') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="matches" border stripe size="small" max-height="600">
            <el-table-column prop="matchNo" :label="t('wms.ink.fld.matchNo')" width="180" />
            <el-table-column prop="customerCd" :label="t('wms.vmi.fld.customerCd')" width="120" />
            <el-table-column prop="customerName" :label="t('wms.vmi.fld.customerName')" width="160" />
            <el-table-column prop="colorCode" :label="t('wms.ink.fld.colorCode')" width="120" />
            <el-table-column prop="consumedQty" :label="t('wms.ink.fld.consumedQty')" width="100" align="right" />
            <el-table-column prop="matchedAt" :label="t('wms.ink.fld.matchedAt')" width="170" />
            <el-table-column prop="operatorCd" :label="t('wms.ink.fld.operator')" width="120" />
            <el-table-column prop="remarks" :label="t('wms.common.remarks')" min-width="180" show-overflow-tooltip />
          </el-table>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <!-- ─── 新建ロット Dialog ─── -->
    <el-dialog v-model="createDialog" :title="t('wms.ink.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.colorCode')" required><el-input v-model="editing.colorCode" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.inkType')" required>
            <el-select v-model="editing.inkType">
              <el-option v-for="(l, v) in inkTypeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.expiry')" required><el-date-picker v-model="editing.expiryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.qty')" required><el-input-number v-model="editing.quantity" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'Unit'"><el-input v-model="editing.unitCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.viscosity')"><el-input-number v-model="editing.viscosityCp" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.solidContent')"><el-input-number v-model="editing.solidContent" :min="0" :max="100" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.location')"><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.supplier')"><el-input v-model="editing.supplierCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 開封 Dialog ─── -->
    <el-dialog v-model="openDialog" :title="t('wms.ink.dlg.open') + ' — ' + openTarget?.inkLotNo" width="420">
      <el-form label-width="160px" size="small">
        <el-form-item :label="t('wms.ink.fld.expiry')">
          <el-tag>{{ openTarget?.expiryDate }}</el-tag>
        </el-form-item>
        <el-form-item :label="t('wms.ink.fld.newExpiry')">
          <el-date-picker v-model="openNewExpiry" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-alert :title="t('wms.ink.msg.openHint')" type="info" :closable="false" show-icon />
      </el-form>
      <template #footer>
        <el-button @click="openDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onOpenLot" :loading="saving">{{ t('wms.ink.btn.open') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 混合 Dialog ─── -->
    <el-dialog v-model="mixDialog" :title="t('wms.ink.dlg.mix')" width="600">
      <el-form :model="mixForm" label-width="160px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.parentLotA')" required><el-input v-model="mixForm.parentLotNoA" maxlength="25" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.parentQtyA')" required><el-input-number v-model="mixForm.parentQtyA" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.parentLotB')" required><el-input v-model="mixForm.parentLotNoB" maxlength="25" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.parentQtyB')" required><el-input-number v-model="mixForm.parentQtyB" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.newColorCode')"><el-input v-model="mixForm.newColorCode" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.newLocationCd')"><el-input v-model="mixForm.newLocationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="mixForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="mixDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onMix" :loading="saving">{{ t('wms.ink.btn.mix') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 調色登記 Dialog ─── -->
    <el-dialog v-model="recordDialog" :title="t('wms.ink.dlg.record')" width="600">
      <el-form v-if="recordForm" :model="recordForm" label-width="160px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.vmi.fld.customerCd')" required><el-input v-model="recordForm.customerCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.vmi.fld.customerName')"><el-input v-model="recordForm.customerName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.colorCode')" required><el-input v-model="recordForm.colorCode" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.ink.fld.consumedQty')" required><el-input-number v-model="recordForm.consumedQty" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.ink.fld.formula')"><el-input v-model="recordForm.formulaJson" type="textarea" :rows="3" placeholder='{"M":50,"Y":30,"K":20}' /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="recordForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="recordDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onRecordMatch" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { inkApi } from '@/api/wms/paperIndustry'
import type { InkLot, InkLotSearchQuery, InkColorMatchHistory, MixInkRequest } from '@/types/wms'

const { t } = useI18n()
const activeTab = ref<'lots' | 'matches'>('lots')
const loading = ref(false)
const saving = ref(false)

// Lots
const lotQuery = reactive<InkLotSearchQuery>({ pageSize: 100, expiringWithin30Days: false })
const lots = ref<InkLot[]>([])

// Matches
const matchQuery = reactive<{ customerCd?: string; colorCode?: string }>({})
const matches = ref<InkColorMatchHistory[]>([])

// Dialogs
const createDialog = ref(false)
const editing = ref<any>(null)

const openDialog = ref(false)
const openTarget = ref<InkLot | null>(null)
const openNewExpiry = ref<string | undefined>(undefined)

const mixDialog = ref(false)
const mixForm = reactive<MixInkRequest>({ parentLotNoA: '', parentQtyA: 0, parentLotNoB: '', parentQtyB: 0 })

const recordDialog = ref(false)
const recordForm = ref<any>(null)

const inkTypeMap = computed<Record<string, string>>(() => ({
  OFFSET: t('wms.ink.type.offset'),
  FLEXO: t('wms.ink.type.flexo'),
  UV: t('wms.ink.type.uv'),
  OTHER: t('wms.ink.type.other'),
}))

const openMap = computed<Record<string, string>>(() => ({
  UNOPENED: t('wms.ink.open.unopened'),
  OPENED: t('wms.ink.open.opened'),
}))

function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return Number(n).toLocaleString('ja-JP', { maximumFractionDigits: 3 })
}

function expiryClass(d: string | undefined) {
  if (!d) return ''
  const days = Math.floor((new Date(d).getTime() - Date.now()) / 86400000)
  if (days < 0) return 'expiry-expired'
  if (days < 30) return 'expiry-soon'
  return ''
}

async function reloadLots() {
  loading.value = true
  try { lots.value = (await inkApi.searchLots(lotQuery)).data || [] }
  finally { loading.value = false }
}

async function reloadMatches() {
  loading.value = true
  try { matches.value = (await inkApi.searchMatches(matchQuery.customerCd, matchQuery.colorCode)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    colorCode: '', inkType: 'OFFSET',
    expiryDate: '', quantity: 0, unitCd: 'kg',
    viscosityCp: undefined, solidContent: undefined,
    locationCd: '', supplierCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await inkApi.createLot(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.inkLotNo}`)
    createDialog.value = false
    await reloadLots()
  } finally { saving.value = false }
}

function openOpenDialog(row: InkLot) {
  openTarget.value = row
  openNewExpiry.value = row.expiryDate
  openDialog.value = true
}

async function onOpenLot() {
  if (!openTarget.value) return
  saving.value = true
  try {
    await inkApi.openLot(openTarget.value.inkLotNo, openNewExpiry.value)
    ElMessage.success(t('wms.common.success'))
    openDialog.value = false
    await reloadLots()
  } finally { saving.value = false }
}

function openMixDialog() {
  mixForm.parentLotNoA = ''; mixForm.parentQtyA = 0
  mixForm.parentLotNoB = ''; mixForm.parentQtyB = 0
  mixForm.newColorCode = undefined; mixForm.newLocationCd = undefined; mixForm.remarks = undefined
  mixDialog.value = true
}

async function onMix() {
  if (!mixForm.parentLotNoA || !mixForm.parentLotNoB || mixForm.parentQtyA <= 0 || mixForm.parentQtyB <= 0) {
    ElMessage.warning(t('wms.common.required'))
    return
  }
  saving.value = true
  try {
    const res = await inkApi.mix({ ...mixForm })
    ElMessage.success(`${t('wms.common.success')}: ${res.data.newLotNo}`)
    mixDialog.value = false
    await reloadLots()
  } finally { saving.value = false }
}

function openRecordDialog() {
  recordForm.value = {
    customerCd: '', customerName: '', colorCode: '',
    consumedQty: 0, formulaJson: '', remarks: '',
  }
  recordDialog.value = true
}

async function onRecordMatch() {
  if (!recordForm.value?.customerCd || !recordForm.value?.colorCode) {
    ElMessage.warning(t('wms.common.required'))
    return
  }
  saving.value = true
  try {
    const res = await inkApi.recordMatch(recordForm.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.matchNo}`)
    recordDialog.value = false
    await reloadMatches()
  } finally { saving.value = false }
}

onMounted(reloadLots)
</script>

<style scoped>
.wms-ink-lot { padding: 16px; }
.search-card { margin-bottom: 12px; }
.expiry-expired { color: #f56c6c; font-weight: bold; }
.expiry-soon { color: #e6a23c; font-weight: bold; }
</style>
