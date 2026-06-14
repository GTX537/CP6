<template>
  <div class="wms-vmi">
    <el-tabs v-model="activeTab" type="card">
      <!-- ───── 客户汇总 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.customers')" name="customers">
        <el-card shadow="never" class="search-card">
          <el-form inline size="small">
            <el-form-item :label="t('wms.vmi.fld.customerCd')">
              <el-input v-model="customerQuery" clearable style="width: 160px" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadCustomers" :loading="loading">{{ t('wms.common.search') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="customers" border stripe size="small" max-height="600" highlight-current-row>
            <el-table-column prop="customerCd" :label="t('wms.vmi.fld.customerCd')" width="140" />
            <el-table-column prop="customerName" :label="t('wms.vmi.fld.customerName')" min-width="180" />
            <el-table-column prop="skuCount" :label="t('wms.vmi.fld.skuCount')" width="100" align="right" />
            <el-table-column prop="totalPhysicalQty" :label="t('wms.vmi.fld.physical')" width="120" align="right">
              <template #default="{ row }">{{ formatQty(row.totalPhysicalQty) }}</template>
            </el-table-column>
            <el-table-column prop="totalAllocatedQty" :label="t('wms.vmi.fld.allocated')" width="120" align="right">
              <template #default="{ row }">{{ formatQty(row.totalAllocatedQty) }}</template>
            </el-table-column>
            <el-table-column prop="totalAvailableQty" :label="t('wms.vmi.fld.available')" width="120" align="right">
              <template #default="{ row }">{{ formatQty(row.totalAvailableQty) }}</template>
            </el-table-column>
            <el-table-column prop="estimatedValue" :label="t('wms.vmi.fld.estValue')" width="140" align="right">
              <template #default="{ row }">{{ formatMoney(row.estimatedValue) }}</template>
            </el-table-column>
            <el-table-column :label="t('wms.common.action')" width="140" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openDetails(row.customerCd)">{{ t('wms.vmi.btn.viewDetail') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <!-- ───── 明细 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.details') + (selectedCustomer ? ` (${selectedCustomer})` : '')" name="details" :disabled="!selectedCustomer">
        <el-card shadow="never" class="search-card">
          <el-form inline size="small">
            <el-form-item :label="t('wms.vmi.fld.customerCd')">
              <el-tag>{{ selectedCustomer }}</el-tag>
            </el-form-item>
            <el-form-item>
              <el-button @click="reloadDetails" :loading="loading">{{ t('wms.common.refresh') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="details" border stripe size="small" max-height="600">
            <el-table-column prop="productCd" :label="t('wms.common.product')" width="140" />
            <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="140" />
            <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="100" />
            <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
            <el-table-column prop="physicalQty" :label="t('wms.vmi.fld.physical')" width="110" align="right">
              <template #default="{ row }">{{ formatQty(row.physicalQty) }}</template>
            </el-table-column>
            <el-table-column prop="availableQty" :label="t('wms.vmi.fld.available')" width="110" align="right">
              <template #default="{ row }">{{ formatQty(row.availableQty) }}</template>
            </el-table-column>
            <el-table-column prop="receiveDate" :label="t('wms.vmi.fld.receiveDate')" width="120" />
            <el-table-column prop="expiryDate" :label="t('wms.common.expiryDate')" width="120">
              <template #default="{ row }">
                <span :class="expiryClass(row.expiryDate)">{{ row.expiryDate || '-' }}</span>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <!-- ───── 保管料 ───── -->
      <el-tab-pane :label="t('wms.vmi.tab.billings')" name="billings">
        <el-card shadow="never" class="search-card">
          <el-form inline size="small">
            <el-form-item :label="t('wms.vmi.fld.customerCd')">
              <el-input v-model="billingQuery.customerCd" clearable style="width: 140px" />
            </el-form-item>
            <el-form-item :label="t('wms.vmi.fld.yearMonth')">
              <el-input v-model="billingQuery.yearMonth" placeholder="2026-05" clearable style="width: 120px" />
            </el-form-item>
            <el-form-item :label="t('wms.vmi.fld.confirmed')">
              <el-select v-model="billingQuery.confirmed" clearable style="width: 120px">
                <el-option :label="t('wms.common.confirm')" :value="true" />
                <el-option label="—" :value="false" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadBillings" :loading="loading">{{ t('wms.common.search') }}</el-button>
              <el-button type="warning" @click="openCalc">{{ t('wms.vmi.btn.calculate') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="billings" border stripe size="small" max-height="600">
            <el-table-column prop="billingNo" :label="t('wms.vmi.fld.billingNo')" width="200" />
            <el-table-column prop="customerCd" :label="t('wms.vmi.fld.customerCd')" width="120" />
            <el-table-column prop="customerName" :label="t('wms.vmi.fld.customerName')" min-width="160" show-overflow-tooltip />
            <el-table-column prop="yearMonth" :label="t('wms.vmi.fld.yearMonth')" width="100" />
            <el-table-column prop="skuCount" :label="t('wms.vmi.fld.skuCount')" width="90" align="right" />
            <el-table-column prop="beginQty" :label="t('wms.vmi.fld.beginQty')" width="110" align="right">
              <template #default="{ row }">{{ formatQty(row.beginQty) }}</template>
            </el-table-column>
            <el-table-column prop="endQty" :label="t('wms.vmi.fld.endQty')" width="110" align="right">
              <template #default="{ row }">{{ formatQty(row.endQty) }}</template>
            </el-table-column>
            <el-table-column prop="avgQty" :label="t('wms.vmi.fld.avgQty')" width="110" align="right">
              <template #default="{ row }">{{ formatQty(row.avgQty) }}</template>
            </el-table-column>
            <el-table-column prop="dailyStorageRate" :label="t('wms.vmi.fld.dailyRate')" width="110" align="right">
              <template #default="{ row }">{{ formatMoney(row.dailyStorageRate) }}</template>
            </el-table-column>
            <el-table-column prop="billingAmount" :label="t('wms.vmi.fld.billingAmt')" width="140" align="right">
              <template #default="{ row }"><b>{{ formatMoney(row.billingAmount) }}</b></template>
            </el-table-column>
            <el-table-column prop="calculatedAt" :label="t('wms.vmi.fld.calculatedAt')" width="170" />
            <el-table-column :label="t('wms.vmi.fld.confirmed')" width="100" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.confirmed" type="success" size="small">{{ t('wms.common.confirm') }}</el-tag>
                <el-tag v-else type="info" size="small">-</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('wms.common.action')" width="120" fixed="right">
              <template #default="{ row }">
                <el-button v-if="!row.confirmed" link type="primary" size="small" @click="onConfirm(row)">{{ t('wms.vmi.btn.confirm') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <!-- 计算保管料 Dialog -->
    <el-dialog v-model="calcDialog" :title="t('wms.vmi.dlg.calculate')" width="480">
      <el-alert :title="t('wms.vmi.msg.calcHint')" type="info" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form :model="calcForm" label-width="160px" size="small">
        <el-form-item :label="t('wms.vmi.fld.yearMonth')" required>
          <el-input v-model="calcForm.yearMonth" placeholder="2026-05" maxlength="7" />
        </el-form-item>
        <el-form-item :label="t('wms.vmi.fld.dailyRate')" required>
          <el-input-number v-model="calcForm.dailyStorageRate" :min="0" :precision="4" controls-position="right" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="calcDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCalc" :loading="saving">{{ t('wms.vmi.btn.calculate') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { vmiApi } from '@/api/wms/paperIndustry'
import type { VmiCustomerSummary, VmiStockDetail, VmiBilling } from '@/types/wms/wms'
import { formatQty as fmtQty, formatCurrency } from '@/utils/format'

const { t } = useI18n()
const activeTab = ref<'customers' | 'details' | 'billings'>('customers')
const loading = ref(false)
const saving = ref(false)

const customerQuery = ref('')
const customers = ref<VmiCustomerSummary[]>([])

const selectedCustomer = ref('')
const details = ref<VmiStockDetail[]>([])

const billingQuery = reactive<{ customerCd?: string; yearMonth?: string; confirmed?: boolean }>({})
const billings = ref<VmiBilling[]>([])

const calcDialog = ref(false)
const calcForm = reactive({ yearMonth: '', dailyStorageRate: 1.0 })

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

async function reloadCustomers() {
  loading.value = true
  try { customers.value = (await vmiApi.customers(customerQuery.value || undefined)).data || [] }
  finally { loading.value = false }
}

async function openDetails(cd: string) {
  selectedCustomer.value = cd
  activeTab.value = 'details'
  await reloadDetails()
}

async function reloadDetails() {
  if (!selectedCustomer.value) return
  loading.value = true
  try { details.value = (await vmiApi.details(selectedCustomer.value)).data || [] }
  finally { loading.value = false }
}

async function reloadBillings() {
  loading.value = true
  try { billings.value = (await vmiApi.billings(billingQuery.customerCd, billingQuery.yearMonth, billingQuery.confirmed)).data || [] }
  finally { loading.value = false }
}

function openCalc() {
  const now = new Date()
  const ym = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  calcForm.yearMonth = ym
  calcForm.dailyStorageRate = 1.0
  calcDialog.value = true
}

async function onCalc() {
  if (!calcForm.yearMonth || calcForm.dailyStorageRate <= 0) {
    ElMessage.warning(t('wms.common.required'))
    return
  }
  saving.value = true
  try {
    const res = await vmiApi.calculate(calcForm.yearMonth, calcForm.dailyStorageRate)
    ElMessage.success(t('wms.vmi.msg.upserted', { n: res.data.upserted }))
    calcDialog.value = false
    await reloadBillings()
  } finally { saving.value = false }
}

async function onConfirm(row: VmiBilling) {
  try {
    await ElMessageBox.confirm(`${t('wms.vmi.btn.confirm')}: ${row.billingNo}`, t('wms.common.confirm'), { type: 'warning' })
    await vmiApi.confirm(row.billingNo)
    ElMessage.success(t('wms.common.success'))
    await reloadBillings()
  } catch { /* */ }
}

onMounted(() => {
  reloadCustomers()
  reloadBillings()
})
</script>

<style scoped>
.wms-vmi { padding: 16px; }
.search-card { margin-bottom: 12px; }
.expiry-expired { color: #f56c6c; font-weight: bold; }
.expiry-soon { color: #e6a23c; font-weight: bold; }
</style>
