<template>
  <div class="bp-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-divider content-position="left">{{ t('sales.search.required') }} FLG</el-divider>
        <div class="flg-row">
          <el-checkbox v-model="query.includeCustomer">{{ t('sales.role.customer') }}</el-checkbox>
          <el-checkbox v-model="query.includeAccountsReceivable">{{ t('sales.role.ar') }}</el-checkbox>
          <el-checkbox v-model="query.includeBilling">{{ t('sales.role.billing') }}</el-checkbox>
          <el-checkbox v-model="query.includeReceipt">{{ t('sales.role.receipt') }}</el-checkbox>
          <el-checkbox v-model="query.includeDelivery">{{ t('sales.role.delivery') }}</el-checkbox>
          <el-checkbox v-model="query.includeCreditMgmt">{{ t('sales.role.creditMgmt') }}</el-checkbox>
          <el-checkbox v-model="query.includeSupplier">{{ t('sales.role.supplier') }}</el-checkbox>
          <el-checkbox v-model="query.includeAccountsPayable">{{ t('sales.role.ap') }}</el-checkbox>
          <el-checkbox v-model="query.includePaymentSchedule">{{ t('sales.role.paymentSch') }}</el-checkbox>
          <el-checkbox v-model="query.includePayment">{{ t('sales.role.payment') }}</el-checkbox>
          <el-checkbox v-model="query.includeMaker">{{ t('sales.role.maker') }}</el-checkbox>
        </div>

        <el-divider content-position="left">{{ t('sales.section.searchCond') }}</el-divider>
        <el-form-item :label="t('登録日 FROM')"><el-date-picker v-model="query.registeredDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('登録日 TO')"><el-date-picker v-model="query.registeredDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.term.bp')"><el-input v-model="query.bpCd" style="width: 160px" /></el-form-item>
        <el-form-item :label="t('sales.term.bpName')"><el-input v-model="query.bpName" style="width: 200px" /></el-form-item>
        <el-form-item :label="t('法人番号')"><el-input v-model="query.ein" style="width: 160px" /></el-form-item>
        <el-form-item :label="t('標準企業コード')"><el-input v-model="query.stdCoCd" style="width: 160px" /></el-form-item>
        <el-form-item :label="t('郵便番号')"><el-input v-model="query.zipCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('住所(LIKE)')"><el-input v-model="query.addr" style="width: 200px" /></el-form-item>
        <el-form-item label="TEL"><el-input v-model="query.tel" style="width: 160px" /></el-form-item>
        <el-form-item :label="t('sales.term.salesStaff')"><el-input v-model="query.salesStaffCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.term.businessStaff')"><el-input v-model="query.businessStaffCd" style="width: 130px" /></el-form-item>

        <el-collapse v-model="advOpen" style="width: 100%">
          <el-collapse-item :title="t('sales.section.advSearch') + '：取引先分類 1〜10'" name="adv">
            <el-form-item v-for="i in 10" :key="i" :label="`分類${i}`">
              <el-input v-model="(query as any)[`bpClass${String(i).padStart(2,'0')}`]" style="width: 110px" />
            </el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item :label="t('sales.term.status')">
          <el-checkbox v-model="query.includePreRegistered">{{ t('sales.op.preregister') }}</el-checkbox>
          <el-checkbox v-model="query.includeRegistered">{{ t('sales.op.register') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">{{ t('sales.btn.exportCsv') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">{{ t('sales.list.totalCount', { n: total }) }}</el-tag>
        <el-button v-if="selectedRow" type="primary" link size="small" style="margin-left: 12px" @click="goView">{{ t('sales.btn.openView') }}</el-button>
        <el-button v-if="selectedRow" type="warning" link size="small" style="margin-left: 4px" @click="goEdit">{{ t('sales.op.edit') }}</el-button>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600" highlight-current-row @current-change="onCurrentChange" @sort-change="onSortChange">
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column prop="status" :label="t('sales.term.status')" width="100" sortable="custom">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="bpCd" :label="t('sales.term.bp')" width="120" sortable="custom" />
        <el-table-column prop="bpName" :label="t('sales.term.bpName')" min-width="200" sortable="custom" />
        <el-table-column prop="bpAbbrev" :label="t('略称')" width="120" sortable="custom" />
        <el-table-column prop="salesStaffCd" :label="t('sales.term.salesStaff')" width="120" sortable="custom" />
        <el-table-column prop="businessStaffCd" :label="t('sales.term.businessStaff')" width="120" sortable="custom" />
        <el-table-column prop="ein" :label="t('法人番号')" width="140" sortable="custom" />
        <el-table-column prop="stdCoCd" :label="t('標準企業')" width="140" sortable="custom" />
        <el-table-column prop="addr1" :label="t('住所1')" width="120" />
        <el-table-column prop="addr2" :label="t('住所2')" width="120" />
        <el-table-column :label="t('得')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.customerFlg" /></template></el-table-column>
        <el-table-column :label="t('売')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.accountsReceivableFlg" /></template></el-table-column>
        <el-table-column :label="t('請')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.billingFlg" /></template></el-table-column>
        <el-table-column :label="t('入')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.receiptFlg" /></template></el-table-column>
        <el-table-column :label="t('納')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.deliveryFlg" /></template></el-table-column>
        <el-table-column :label="t('信')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.creditMgmtFlg" /></template></el-table-column>
        <el-table-column :label="t('発')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.supplierFlg" /></template></el-table-column>
        <el-table-column :label="t('買')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.accountsPayableFlg" /></template></el-table-column>
        <el-table-column :label="t('予')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.paymentScheduleFlg" /></template></el-table-column>
        <el-table-column :label="t('払')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.paymentFlg" /></template></el-table-column>
        <el-table-column :label="t('メ')" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.makerFlg" /></template></el-table-column>
        <el-table-column prop="createDate" :label="t('登録日')" width="110" sortable="custom">
          <template #default="{ row }">{{ row.createDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="creator" :label="t('登録担当')" width="110" />
      </el-table>

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="search" @size-change="search"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check } from '@element-plus/icons-vue'
import { bpApi } from '@/api/erp/businessPartner'
import type { BpQueryDto, BpListItemDto } from '@/types/erp/businessPartner'

const { t } = useI18n()
const router = useRouter()

// 简易内联组件：FLG 图标
const FlgIcon = (props: { on: boolean }) => props.on
  ? h(Check, { color: '#67c23a', style: 'font-size: 16px' })
  : h('span', { style: 'color:#dcdfe6' }, '-')

const query = reactive<BpQueryDto>({
  includeCustomer: true, includeAccountsReceivable: true, includeBilling: true,
  includeReceipt: true, includeDelivery: true, includeCreditMgmt: true,
  includeSupplier: true, includeAccountsPayable: true, includePaymentSchedule: true,
  includePayment: true, includeMaker: true,
  includePreRegistered: true, includeRegistered: true,
  page: 1, pageSize: 100,
  sortField: '', sortOrder: '',
})
const advOpen = ref<string[]>([])
const rows = ref<BpListItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const exporting = ref(false)
const selectedRow = ref<BpListItemDto | null>(null)

async function search() {
  // E10030: 属性 FLG / ステータス いずれかは必須
  const anyFlg = query.includeCustomer || query.includeAccountsReceivable || query.includeBilling
    || query.includeReceipt || query.includeDelivery || query.includeCreditMgmt
    || query.includeSupplier || query.includeAccountsPayable || query.includePaymentSchedule
    || query.includePayment || query.includeMaker
  if (!anyFlg) { ElMessage.warning(t('sales.err.E10030') + ': FLG'); return }
  if (!query.includePreRegistered && !query.includeRegistered) { ElMessage.warning(t('sales.err.E10030') + ': ' + t('sales.term.status')); return }

  loading.value = true
  try {
    const r = await bpApi.search(query)
    if (r.code === 0 && r.data) {
      rows.value = r.data.rows
      total.value = r.data.total
      if (rows.value.length === 0) ElMessage.info(t('sales.err.E10008'))
    }
  } finally {
    loading.value = false
  }
}

function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  search()
}

function resetQuery() {
  Object.keys(query).forEach(k => {
    if (k === 'page' || k === 'pageSize') return
    if (k.startsWith('include')) (query as any)[k] = false
    else (query as any)[k] = undefined
  })
  query.page = 1
  rows.value = []
  total.value = 0
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = await bpApi.exportCsv(query) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `business-partners_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  } finally {
    exporting.value = false
  }
}

function onCurrentChange(row: BpListItemDto | null) {
  selectedRow.value = row
}

function goView() {
  if (!selectedRow.value) return
  router.push({ path: '/business-partner', query: { bpCd: selectedRow.value.bpCd, mode: 'view' } })
}

function goEdit() {
  if (!selectedRow.value) return
  router.push({ path: '/business-partner', query: { bpCd: selectedRow.value.bpCd, mode: 'edit' } })
}

function statusLabel(s: number): string {
  return s === 0 ? t('sales.op.preregister') : s === 1 ? t('sales.op.register') : s === 9 ? t('sales.op.delete') : '-'
}
function statusTagType(s: number): 'info' | 'success' | 'danger' {
  return s === 0 ? 'info' : s === 1 ? 'success' : 'danger'
}
</script>

<style scoped>
.bp-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
.flg-row { display: flex; gap: 12px; flex-wrap: wrap; padding: 4px 8px; }
</style>
