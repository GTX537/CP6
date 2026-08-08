<!--
  VMI 在庫（預り在庫）—— el-tabs 三视图，每个 tab 一张查询列表 → 各 tab 内嵌 CpListPage 迁移（WMS 批次5）。
  el-tabs 是页面外壳（模板无对应契约，保留）；数量/金额列用 map；期限 col slot 保逾期红字；操作/確定/確定フラグ 走 col slot。
  客户汇总无查询无分页(paginated=false)；明细无搜索栏，刷新+客户标签放 toolbar slot，fetch 闭包依 selectedCustomer 守卫。
  跨 tab 联动：openDetails 切 tab + detailsRef.reload()；計算/確定 后 billingsRef.reload()。
-->
<template>
  <div class="wms-vmi">
    <el-tabs v-model="activeTab" type="card">
      <!-- ───── 客户汇总 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.customers')" name="customers">
        <CpListPage
          ref="customersRef"
          :columns="customerColumns"
          :fetch="fetchCustomers"
          :search-fields="customerSearchFields"
          :filter-labels="filterLabels"
          :paginated="false"
        >
          <template #col-_action="{ row }">
            <el-button link type="primary" size="small" @click="openDetails(row.customerCd)">{{ t('wms.vmi.btn.viewDetail') }}</el-button>
          </template>
        </CpListPage>
      </el-tab-pane>

      <!-- ───── 明细 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.details') + (selectedCustomer ? ` (${selectedCustomer})` : '')" name="details" :disabled="!selectedCustomer">
        <CpListPage
          ref="detailsRef"
          :columns="detailColumns"
          :fetch="fetchDetails"
          :paginated="false"
        >
          <template #toolbar>
            <el-tag>{{ selectedCustomer }}</el-tag>
            <el-button @click="reloadDetails">{{ t('wms.common.refresh') }}</el-button>
          </template>
          <template #col-expiryDate="{ row }">
            <span :class="expiryClass(row.expiryDate)">{{ row.expiryDate || '-' }}</span>
          </template>
        </CpListPage>
      </el-tab-pane>

      <!-- ───── 保管料 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.billings')" name="billings">
        <CpListPage
          ref="billingsRef"
          :columns="billingColumns"
          :fetch="fetchBillings"
          :search-fields="billingSearchFields"
          :filter-labels="filterLabels"
          :paginated="false"
        >
          <template #toolbar>
            <el-button v-permission="'wms-vmi:calculate'" type="warning" @click="openCalc">{{ t('wms.vmi.btn.calculate') }}</el-button>
          </template>
          <template #col-billingAmount="{ row }">
            <b>{{ formatMoney(row.billingAmount) }}</b>
          </template>
          <template #col-confirmed="{ row }">
            <el-tag v-if="row.confirmed" type="success" size="small">{{ t('wms.common.confirm') }}</el-tag>
            <el-tag v-else type="info" size="small">-</el-tag>
          </template>
          <template #col-_action="{ row }">
            <el-button v-if="!row.confirmed" v-permission="'wms-vmi:confirm'" link type="primary" size="small" @click="onConfirm(row)">{{ t('wms.vmi.btn.confirm') }}</el-button>
          </template>
        </CpListPage>
      </el-tab-pane>
    </el-tabs>

    <!-- 计算保管料 Dialog -->
    <CpFormDialog
      v-model="calcDialog"
      :title="t('wms.vmi.dlg.calculate')"
      width="480"
      :form="calcForm"
      :rules="calcRules"
      :submit="onCalc"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.vmi.btn.calculate') }"
    >
      <el-alert :title="t('wms.vmi.msg.calcHint')" type="info" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form-item :label="t('wms.vmi.fld.yearMonth')" prop="yearMonth">
        <el-input v-model="calcForm.yearMonth" placeholder="2026-05" maxlength="7" />
      </el-form-item>
      <el-form-item :label="t('wms.vmi.fld.dailyRate')" prop="dailyStorageRate">
        <el-input-number v-model="calcForm.dailyStorageRate" :min="0" :precision="4" controls-position="right" style="width: 100%" />
      </el-form-item>
    </CpFormDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { vmiApi } from '@/api/wms/paperIndustry'
import type { VmiBilling, VmiCustomerSummary, VmiStockDetail } from '@/types/wms/wms'
import { formatQty as fmtQty, formatCurrency } from '@/utils/format'

const { t } = useI18n()
const activeTab = ref<'customers' | 'details' | 'billings'>('customers')

const customersRef = ref<ListPageExpose | null>(null)
const detailsRef = ref<ListPageExpose | null>(null)
const billingsRef = ref<ListPageExpose | null>(null)

const selectedCustomer = ref('')

function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return fmtQty(n, 2)
}
function formatMoney(n: number | undefined | null) {
  if (n == null) return ''
  return formatCurrency(n)
}
function expiryClass(d: string | undefined) {
  if (!d) return ''
  const days = Math.floor((new Date(d).getTime() - Date.now()) / 86400000)
  if (days < 0) return 'expiry-expired'
  if (days < 30) return 'expiry-soon'
  return ''
}

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

// ───── 客户汇总 ─────
const customerColumns = computed<ListColumn<VmiCustomerSummary>[]>(() => [
  { prop: 'customerCd', label: t('wms.vmi.fld.customerCd'), width: 140 },
  { prop: 'customerName', label: t('wms.vmi.fld.customerName'), minWidth: 180 },
  { prop: 'skuCount', label: t('wms.vmi.fld.skuCount'), width: 100, kind: 'num' },
  { prop: 'totalPhysicalQty', label: t('wms.vmi.fld.physical'), width: 120, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'totalAllocatedQty', label: t('wms.vmi.fld.allocated'), width: 120, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'totalAvailableQty', label: t('wms.vmi.fld.available'), width: 120, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'estimatedValue', label: t('wms.vmi.fld.estValue'), width: 140, align: 'right', map: (v) => ({ label: formatMoney(v as number) }) },
  { prop: '_action', label: t('wms.common.action'), width: 140, fixed: 'right' },
])
const customerSearchFields = computed<FilterField[]>(() => [
  { key: 'customerCd', label: t('wms.vmi.fld.customerCd'), type: 'text' },
])
const fetchCustomers: ListFetch<VmiCustomerSummary> = async ({ filters }) => {
  const cd = filters.customerCd ? String(filters.customerCd) : undefined
  const all = (await vmiApi.customers(cd)).data || []
  return { rows: all, total: all.length }
}

async function openDetails(cd: string) {
  selectedCustomer.value = cd
  activeTab.value = 'details'
  detailsRef.value?.reload()
}

// ───── 明细 ─────
const detailColumns = computed<ListColumn<VmiStockDetail>[]>(() => [
  { prop: 'productCd', label: t('wms.common.product'), width: 140 },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 140 },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 100 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'physicalQty', label: t('wms.vmi.fld.physical'), width: 110, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'availableQty', label: t('wms.vmi.fld.available'), width: 110, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'receiveDate', label: t('wms.vmi.fld.receiveDate'), width: 120 },
  { prop: 'expiryDate', label: t('wms.common.expiryDate'), width: 120 },
])
const fetchDetails: ListFetch<VmiStockDetail> = async () => {
  if (!selectedCustomer.value) return { rows: [], total: 0 }
  const all = (await vmiApi.details(selectedCustomer.value)).data || []
  return { rows: all, total: all.length }
}
function reloadDetails() { detailsRef.value?.reload() }

// ───── 保管料 ─────
const billingColumns = computed<ListColumn<VmiBilling>[]>(() => [
  { prop: 'billingNo', label: t('wms.vmi.fld.billingNo'), width: 200 },
  { prop: 'customerCd', label: t('wms.vmi.fld.customerCd'), width: 120 },
  { prop: 'customerName', label: t('wms.vmi.fld.customerName'), minWidth: 160, overflowTooltip: true },
  { prop: 'yearMonth', label: t('wms.vmi.fld.yearMonth'), width: 100 },
  { prop: 'skuCount', label: t('wms.vmi.fld.skuCount'), width: 90, kind: 'num' },
  { prop: 'beginQty', label: t('wms.vmi.fld.beginQty'), width: 110, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'endQty', label: t('wms.vmi.fld.endQty'), width: 110, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'avgQty', label: t('wms.vmi.fld.avgQty'), width: 110, align: 'right', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'dailyStorageRate', label: t('wms.vmi.fld.dailyRate'), width: 110, align: 'right', map: (v) => ({ label: formatMoney(v as number) }) },
  { prop: 'billingAmount', label: t('wms.vmi.fld.billingAmt'), width: 140, align: 'right' },
  { prop: 'calculatedAt', label: t('wms.vmi.fld.calculatedAt'), width: 170 },
  { prop: 'confirmed', label: t('wms.vmi.fld.confirmed'), width: 100, align: 'center' },
  { prop: '_action', label: t('wms.common.action'), width: 120, fixed: 'right' },
])
const billingSearchFields = computed<FilterField[]>(() => [
  { key: 'customerCd', label: t('wms.vmi.fld.customerCd'), type: 'text' },
  { key: 'yearMonth', label: t('wms.vmi.fld.yearMonth'), type: 'text', placeholder: '2026-05' },
  {
    key: 'confirmed', label: t('wms.vmi.fld.confirmed'), type: 'select',
    options: [
      { label: t('wms.common.confirm'), value: true },
      { label: '—', value: false },
    ],
  },
])
const fetchBillings: ListFetch<VmiBilling> = async ({ filters }) => {
  const cd = filters.customerCd ? String(filters.customerCd) : undefined
  const ym = filters.yearMonth ? String(filters.yearMonth) : undefined
  const confirmed = filters.confirmed === undefined || filters.confirmed === '' ? undefined : Boolean(filters.confirmed)
  const all = (await vmiApi.billings(cd, ym, confirmed)).data || []
  return { rows: all, total: all.length }
}

// —— 计算保管料 ——
const calcDialog = ref(false)
const calcForm = reactive({ yearMonth: '', dailyStorageRate: 1.0 })
const calcRules = computed<FormRules>(() => ({
  yearMonth: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  dailyStorageRate: [{ required: true, trigger: 'change', validator: (_r, v, cb) => (typeof v === 'number' && v > 0 ? cb() : cb(new Error(t('wms.common.required')))) }],
}))
function openCalc() {
  const now = new Date()
  calcForm.yearMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  calcForm.dailyStorageRate = 1.0
  calcDialog.value = true
}
async function onCalc() {
  const res = await vmiApi.calculate(calcForm.yearMonth, calcForm.dailyStorageRate)
  ElMessage.success(t('wms.vmi.msg.upserted', { n: res.data.upserted }))
  billingsRef.value?.reload()
}

async function onConfirm(row: VmiBilling) {
  try {
    await ElMessageBox.confirm(`${t('wms.vmi.btn.confirm')}: ${row.billingNo}`, t('wms.common.confirm'), { type: 'warning' })
    await vmiApi.confirm(row.billingNo)
    ElMessage.success(t('wms.common.success'))
    billingsRef.value?.reload()
  } catch { /* */ }
}
</script>

<style scoped>
.wms-vmi { padding: 16px; }
.expiry-expired { color: var(--cp-danger); font-weight: bold; }
.expiry-soon { color: var(--cp-warn); font-weight: bold; }
</style>
