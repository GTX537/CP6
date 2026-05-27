<template>
  <div class="wms-paper-roll">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.paperRoll.fld.rollNo')"><el-input v-model="query.rollNo" clearable style="width: 200px" /></el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.grade')"><el-input v-model="query.paperGrade" clearable style="width: 100px" /></el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.widthMm')"><el-input-number v-model="query.widthMm" :min="0" controls-position="right" style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.grain')">
          <el-select v-model="query.grainDirection" clearable style="width: 100px">
            <el-option label="T" value="T" /><el-option label="Y" value="Y" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 120px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
          <el-button type="warning" @click="slitDialog = true">{{ t('wms.paperRoll.btn.slit') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="rollNo" :label="t('wms.paperRoll.fld.rollNo')" width="200" />
        <el-table-column :label="t('wms.common.status')" width="100">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="paperGrade" :label="t('wms.paperRoll.fld.grade')" width="80" />
        <el-table-column prop="widthMm" :label="t('wms.paperRoll.fld.widthMm')" width="80" align="right" />
        <el-table-column prop="grainDirection" :label="t('wms.paperRoll.fld.grain')" width="60" align="center" />
        <el-table-column prop="basisWeight" :label="t('wms.paperRoll.fld.basis')" width="90" align="right" />
        <el-table-column :label="t('wms.paperRoll.fld.remaining')" width="160">
          <template #default="{ row }">
            <div>{{ formatQty(row.remainingLengthM) }} / {{ formatQty(row.originalLengthM) }} m</div>
            <el-progress :percentage="Math.round(row.remainingLengthM / row.originalLengthM * 100)" :stroke-width="6" :show-text="false" />
          </template>
        </el-table-column>
        <el-table-column prop="coreDiameterInch" :label="t('wms.paperRoll.fld.core')" width="80" align="right">
          <template #default="{ row }">{{ row.coreDiameterInch }}″</template>
        </el-table-column>
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="parentRollNo" :label="t('wms.paperRoll.fld.parentRoll')" width="180" />
        <el-table-column :label="t('wms.common.action')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status !== 3" link type="primary" size="small" @click="openConsume(row)">{{ t('wms.paperRoll.btn.consume') }}</el-button>
            <el-button v-if="row.status !== 3" link type="danger" size="small" @click="onDispose(row)">{{ t('wms.paperRoll.btn.dispose') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 入庫 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.paperRoll.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.grade')" required><el-input v-model="editing.paperGrade" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.widthMm')" required><el-input-number v-model="editing.widthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.basis')"><el-input-number v-model="editing.basisWeight" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.grain')"><el-select v-model="editing.grainDirection"><el-option label="T" value="T" /><el-option label="Y" value="Y" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.lengthM')" required><el-input-number v-model="editing.originalLengthM" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.core')"><el-input-number v-model="editing.coreDiameterInch" :min="1" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.location')" required><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.mfgDate')"><el-date-picker v-model="editing.mfgDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.mfgLot')"><el-input v-model="editing.mfgLotNo" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.disposeTh')"><el-input-number v-model="editing.disposeThresholdM" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 消費 Dialog -->
    <el-dialog v-model="consumeDialog" :title="t('wms.paperRoll.btn.consume') + ' — ' + consumeTarget?.rollNo" width="420">
      <el-form label-width="120px" size="small">
        <el-form-item :label="t('wms.paperRoll.fld.remaining')">
          <el-tag>{{ formatQty(consumeTarget?.remainingLengthM) }} m</el-tag>
        </el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.consumeLen')" required>
          <el-input-number v-model="consumeLen" :min="0" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="consumeDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onConsume" :loading="saving">{{ t('wms.paperRoll.btn.consume') }}</el-button>
      </template>
    </el-dialog>

    <!-- スリッター Dialog -->
    <el-dialog v-model="slitDialog" :title="t('wms.paperRoll.btn.slit')" width="500">
      <el-form :model="slitForm" label-width="140px" size="small">
        <el-form-item :label="t('wms.paperRoll.fld.parentRoll')" required>
          <el-input v-model="slitForm.parentRollNo" :placeholder="t('wms.paperRoll.msg.parentHint')" maxlength="25" />
        </el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.childWidths')">
          <el-input v-model="slitChildWidthsStr" placeholder="例: 905,390" />
          <span style="color: #999">{{ t('wms.paperRoll.msg.widthsHint') }}</span>
        </el-form-item>
        <el-form-item :label="t('wms.paperRoll.fld.keepRemnant')">
          <el-switch v-model="slitForm.keepRemnant" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="slitDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onSlit" :loading="saving">{{ t('wms.paperRoll.btn.slit') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { paperRollApi } from '@/api/wms/paperIndustry'
import type { PaperRoll, PaperRollSearchQuery } from '@/types/wms'

const { t } = useI18n()
const query = reactive<PaperRollSearchQuery>({ pageSize: 100 })
const rows = ref<PaperRoll[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const consumeDialog = ref(false)
const consumeTarget = ref<PaperRoll | null>(null)
const consumeLen = ref(0)

const slitDialog = ref(false)
const slitForm = reactive({ parentRollNo: '', keepRemnant: true })
const slitChildWidthsStr = ref('')

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.paperRoll.status.inStock'),
  1: t('wms.paperRoll.status.inUse'),
  2: t('wms.paperRoll.status.remnant'),
  3: t('wms.paperRoll.status.disposed'),
}))

function statusTagOf(s: number): 'success' | 'primary' | 'warning' | 'danger' {
  return ({ 0: 'success', 1: 'primary', 2: 'warning', 3: 'danger' } as const)[s as 0] || 'info' as any
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return Number(n).toLocaleString('ja-JP', { maximumFractionDigits: 2 })
}

async function reload() {
  loading.value = true
  try { rows.value = (await paperRollApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    paperGrade: '', widthMm: 905, basisWeight: 280, grainDirection: 'T',
    originalLengthM: 0, coreDiameterInch: 3,
    warehouseCd: '', locationCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await paperRollApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.rollNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openConsume(row: PaperRoll) {
  consumeTarget.value = row
  consumeLen.value = 0
  consumeDialog.value = true
}

async function onConsume() {
  if (!consumeTarget.value || consumeLen.value <= 0) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await paperRollApi.consume(consumeTarget.value.rollNo, consumeLen.value)
    ElMessage.success(t('wms.common.success'))
    consumeDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onSlit() {
  if (!slitForm.parentRollNo || !slitChildWidthsStr.value) { ElMessage.warning(t('wms.common.required')); return }
  const widths = slitChildWidthsStr.value.split(',').map(s => parseInt(s.trim())).filter(n => n > 0)
  if (widths.length === 0) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    const res = await paperRollApi.slit({
      parentRollNo: slitForm.parentRollNo,
      childWidths: widths,
      keepRemnant: slitForm.keepRemnant,
    })
    ElMessage.success(`${t('wms.common.success')}: ${res.data.createdRolls.length} ${t('wms.paperRoll.msg.rollsCreated')}`)
    slitDialog.value = false
    slitForm.parentRollNo = ''
    slitChildWidthsStr.value = ''
    await reload()
  } finally { saving.value = false }
}

async function onDispose(row: PaperRoll) {
  try {
    await ElMessageBox.confirm(`${t('wms.paperRoll.btn.dispose')}: ${row.rollNo}`, t('wms.common.confirm'), { type: 'warning' })
    await paperRollApi.dispose(row.rollNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-paper-roll { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
