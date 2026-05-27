<template>
  <div class="wms-remnant">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.remnant.fld.no')"><el-input v-model="query.remnantNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.remnant.fld.matType')">
          <el-select v-model="query.materialType" clearable style="width: 120px">
            <el-option v-for="(l, v) in matTypeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.matGrade')"><el-input v-model="query.materialGrade" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 120px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.sourceWO')"><el-input v-model="query.sourceWorkOrderNo" clearable style="width: 140px" /></el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
          <el-button type="success" @click="matchDialog = true">{{ t('wms.remnant.btn.match') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="remnantNo" :label="t('wms.remnant.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="100">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.remnant.fld.matType')" width="80">
          <template #default="{ row }">{{ matTypeMap[row.materialType] || row.materialType }}</template>
        </el-table-column>
        <el-table-column prop="materialGrade" :label="t('wms.remnant.fld.matGrade')" width="100" />
        <el-table-column prop="widthMm" :label="t('wms.remnant.fld.widthMm')" width="90" align="right" />
        <el-table-column prop="lengthMm" :label="t('wms.remnant.fld.lengthMm')" width="100" align="right" />
        <el-table-column prop="thicknessUm" :label="t('wms.remnant.fld.thickness')" width="100" align="right" />
        <el-table-column prop="quantity" :label="t('wms.remnant.fld.qty')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.quantity) }} {{ row.unitCd }}</template>
        </el-table-column>
        <el-table-column prop="sourceWorkOrderNo" :label="t('wms.remnant.fld.sourceWO')" width="160" />
        <el-table-column prop="sourceRollNo" :label="t('wms.remnant.fld.sourceRoll')" width="160" />
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="80" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="120" />
        <el-table-column prop="reservedFor" :label="t('wms.remnant.fld.reservedFor')" width="140" />
        <el-table-column prop="registeredAt" :label="t('wms.sample.fld.registeredAt')" width="170" />
        <el-table-column :label="t('wms.common.action')" width="240" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="openReserve(row)">{{ t('wms.remnant.btn.reserve') }}</el-button>
            <el-button v-if="row.status === 1" link type="warning" size="small" @click="onUnreserve(row)">{{ t('wms.remnant.btn.unreserve') }}</el-button>
            <el-button v-if="row.status === 0 || row.status === 1" link type="success" size="small" @click="onUse(row)">{{ t('wms.remnant.btn.use') }}</el-button>
            <el-button v-if="row.status !== 3" link type="danger" size="small" @click="onDispose(row)">{{ t('wms.remnant.btn.dispose') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.remnant.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.matType')" required>
            <el-select v-model="editing.materialType">
              <el-option v-for="(l, v) in matTypeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.matGrade')"><el-input v-model="editing.materialGrade" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.widthMm')" required><el-input-number v-model="editing.widthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.lengthMm')" required><el-input-number v-model="editing.lengthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.thickness')"><el-input-number v-model="editing.thicknessUm" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.qty')" required><el-input-number v-model="editing.quantity" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'Unit'"><el-input v-model="editing.unitCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.sourceWO')"><el-input v-model="editing.sourceWorkOrderNo" maxlength="25" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.sourceRoll')"><el-input v-model="editing.sourceRollNo" maxlength="25" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.location')" required><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 预约 Dialog -->
    <el-dialog v-model="reserveDialog" :title="t('wms.remnant.dlg.reserve') + ' — ' + reserveTarget?.remnantNo" width="420">
      <el-form label-width="120px" size="small">
        <el-form-item :label="t('wms.remnant.fld.reservedFor')" required>
          <el-input v-model="reserveFor" maxlength="30" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reserveDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onReserve" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 再利用検索 Dialog -->
    <el-dialog v-model="matchDialog" :title="t('wms.remnant.dlg.match')" width="700">
      <el-alert :title="t('wms.remnant.msg.matchHint')" type="info" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form :model="matchForm" inline label-width="100px" size="small">
        <el-form-item :label="t('wms.remnant.fld.matType')" required>
          <el-select v-model="matchForm.materialType" style="width: 120px">
            <el-option v-for="(l, v) in matTypeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.minWidth')" required>
          <el-input-number v-model="matchForm.minWidthMm" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.minLength')" required>
          <el-input-number v-model="matchForm.minLengthMm" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="runMatch" :loading="loading">{{ t('wms.common.search') }}</el-button>
        </el-form-item>
      </el-form>
      <el-table :data="matchResults" border stripe size="small" max-height="350">
        <el-table-column prop="remnantNo" :label="t('wms.remnant.fld.no')" width="160" />
        <el-table-column prop="materialGrade" :label="t('wms.remnant.fld.matGrade')" width="100" />
        <el-table-column prop="widthMm" :label="t('wms.remnant.fld.widthMm')" width="90" align="right" />
        <el-table-column prop="lengthMm" :label="t('wms.remnant.fld.lengthMm')" width="100" align="right" />
        <el-table-column prop="quantity" :label="t('wms.remnant.fld.qty')" width="90" align="right" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { remnantApi } from '@/api/wms/paperIndustry2'
import type { RemnantMaterial, RemnantSearchQuery } from '@/types/wms'

const { t } = useI18n()
const query = reactive<RemnantSearchQuery>({ pageSize: 100 })
const rows = ref<RemnantMaterial[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const reserveDialog = ref(false)
const reserveTarget = ref<RemnantMaterial | null>(null)
const reserveFor = ref('')

const matchDialog = ref(false)
const matchForm = reactive({ materialType: 'PAPER', minWidthMm: 500, minLengthMm: 700 })
const matchResults = ref<RemnantMaterial[]>([])

const matTypeMap = computed<Record<string, string>>(() => ({
  PAPER: t('wms.remnant.mat.paper'),
  FILM: t('wms.remnant.mat.film'),
  OTHER: t('wms.remnant.mat.other'),
}))

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.remnant.status.available'),
  1: t('wms.remnant.status.reserved'),
  2: t('wms.remnant.status.used'),
  3: t('wms.remnant.status.disposed'),
}))

function statusTagOf(s: number): 'success' | 'warning' | 'info' | 'danger' {
  return ({ 0: 'success', 1: 'warning', 2: 'info', 3: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return Number(n).toLocaleString('ja-JP', { maximumFractionDigits: 4 })
}

async function reload() {
  loading.value = true
  try { rows.value = (await remnantApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    materialType: 'PAPER', materialGrade: '',
    widthMm: 500, lengthMm: 700, thicknessUm: undefined,
    quantity: 1, unitCd: 'SHT',
    sourceWorkOrderNo: '', sourceRollNo: '',
    warehouseCd: '', locationCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await remnantApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.remnantNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openReserve(row: RemnantMaterial) {
  reserveTarget.value = row
  reserveFor.value = ''
  reserveDialog.value = true
}

async function onReserve() {
  if (!reserveTarget.value || !reserveFor.value) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await remnantApi.reserve(reserveTarget.value.remnantNo, reserveFor.value)
    ElMessage.success(t('wms.common.success'))
    reserveDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onUnreserve(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.unreserve')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.unreserve(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function onUse(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.use')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.markUsed(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function onDispose(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.dispose')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.dispose(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function runMatch() {
  loading.value = true
  try { matchResults.value = (await remnantApi.match(matchForm.materialType, matchForm.minWidthMm, matchForm.minLengthMm)).data || [] }
  finally { loading.value = false }
}

onMounted(reload)
</script>

<style scoped>
.wms-remnant { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
