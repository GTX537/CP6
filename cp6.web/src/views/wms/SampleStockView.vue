<template>
  <div class="wms-sample">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.sample.fld.no')"><el-input v-model="query.sampleNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.sample.fld.type')">
          <el-select v-model="query.sampleType" clearable style="width: 120px">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.sample.fld.customer')"><el-input v-model="query.customerCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.sample.fld.product')"><el-input v-model="query.productCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 120px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.overdueOnly">{{ t('wms.sample.btn.overdue') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="sampleNo" :label="t('wms.sample.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="100">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.sample.fld.type')" width="100">
          <template #default="{ row }">{{ typeMap[row.sampleType] || row.sampleType }}</template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('wms.sample.fld.customer')" width="120" />
        <el-table-column prop="customerName" :label="t('wms.vmi.fld.customerName')" min-width="140" show-overflow-tooltip />
        <el-table-column prop="productCd" :label="t('wms.sample.fld.product')" width="120" />
        <el-table-column prop="productName" :label="t('wms.common.productName')" min-width="140" show-overflow-tooltip />
        <el-table-column prop="quantity" :label="t('wms.sample.fld.qty')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.quantity) }} {{ row.unitCd }}</template>
        </el-table-column>
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="lentTo" :label="t('wms.sample.fld.lentTo')" width="140" />
        <el-table-column prop="lentAt" :label="t('wms.sample.fld.lentAt')" width="170" />
        <el-table-column prop="expectedReturnDate" :label="t('wms.sample.fld.expReturn')" width="130">
          <template #default="{ row }">
            <span :class="overdueClass(row)">{{ row.expectedReturnDate || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="returnedAt" :label="t('wms.sample.fld.returnedAt')" width="170" />
        <el-table-column :label="t('wms.common.action')" width="240" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0 || row.status === 2" link type="primary" size="small" @click="openLend(row)">{{ t('wms.sample.btn.lend') }}</el-button>
            <el-button v-if="row.status === 1" link type="success" size="small" @click="onReturn(row)">{{ t('wms.sample.btn.return') }}</el-button>
            <el-button v-if="row.status !== 3" link type="danger" size="small" @click="onExpire(row)">{{ t('wms.sample.btn.expire') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.sample.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.sample.fld.type')" required>
            <el-select v-model="editing.sampleType">
              <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.sample.fld.qty')" required><el-input-number v-model="editing.quantity" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.sample.fld.customer')"><el-input v-model="editing.customerCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.vmi.fld.customerName')"><el-input v-model="editing.customerName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.sample.fld.product')"><el-input v-model="editing.productCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.productName')"><el-input v-model="editing.productName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'Unit'"><el-input v-model="editing.unitCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')"><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.location')"><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 借出 Dialog -->
    <el-dialog v-model="lendDialog" :title="t('wms.sample.dlg.lend') + ' — ' + lendTarget?.sampleNo" width="460">
      <el-form label-width="140px" size="small">
        <el-form-item :label="t('wms.sample.fld.lentTo')" required>
          <el-input v-model="lendForm.lentTo" maxlength="60" />
        </el-form-item>
        <el-form-item :label="t('wms.sample.fld.expReturn')">
          <el-date-picker v-model="lendForm.expectedReturnDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="lendDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onLend" :loading="saving">{{ t('wms.sample.btn.lend') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { sampleApi } from '@/api/wms/paperIndustry2'
import type { SampleStock, SampleSearchQuery } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()
const query = reactive<SampleSearchQuery>({ pageSize: 100, overdueOnly: false })
const rows = ref<SampleStock[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const lendDialog = ref(false)
const lendTarget = ref<SampleStock | null>(null)
const lendForm = reactive<{ lentTo: string; expectedReturnDate?: string }>({ lentTo: '' })

const typeMap = computed<Record<string, string>>(() => ({
  PROTO: t('wms.sample.type.proto'),
  COLOR: t('wms.sample.type.color'),
  DUMMY: t('wms.sample.type.dummy'),
  OTHER: t('wms.sample.type.other'),
}))

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.sample.status.inStock'),
  1: t('wms.sample.status.lentOut'),
  2: t('wms.sample.status.returned'),
  3: t('wms.sample.status.expired'),
}))

function statusTagOf(s: number): 'success' | 'warning' | 'primary' | 'info' {
  return ({ 0: 'success', 1: 'warning', 2: 'primary', 3: 'info' } as const)[s as 0] || 'info'
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return fmtQty(n, 4)
}
function overdueClass(row: SampleStock): string {
  if (row.status !== 1 || !row.expectedReturnDate) return ''
  return new Date(row.expectedReturnDate) < new Date() ? 'sample-overdue' : ''
}

async function reload() {
  loading.value = true
  try { rows.value = (await sampleApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    sampleType: 'PROTO', customerCd: '', customerName: '',
    productCd: '', productName: '',
    quantity: 1, unitCd: 'PCS',
    warehouseCd: '', locationCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await sampleApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.sampleNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openLend(row: SampleStock) {
  lendTarget.value = row
  lendForm.lentTo = ''
  lendForm.expectedReturnDate = undefined
  lendDialog.value = true
}

async function onLend() {
  if (!lendTarget.value || !lendForm.lentTo) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await sampleApi.lend(lendTarget.value.sampleNo, lendForm.lentTo, lendForm.expectedReturnDate)
    ElMessage.success(t('wms.common.success'))
    lendDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onReturn(row: SampleStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.sample.btn.return')}: ${row.sampleNo}`, t('wms.common.confirm'), { type: 'warning' })
    await sampleApi.return(row.sampleNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

async function onExpire(row: SampleStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.sample.btn.expire')}: ${row.sampleNo}`, t('wms.common.confirm'), { type: 'warning' })
    await sampleApi.expire(row.sampleNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-sample { padding: 16px; }
.search-card { margin-bottom: 12px; }
.sample-overdue { color: #f56c6c; font-weight: bold; }
</style>
