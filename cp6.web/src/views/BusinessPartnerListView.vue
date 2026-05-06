<template>
  <div class="bp-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-divider content-position="left">属性 FLG（1 つ以上必須）</el-divider>
        <div class="flg-row">
          <el-checkbox v-model="query.includeCustomer">得意先</el-checkbox>
          <el-checkbox v-model="query.includeAccountsReceivable">売掛先</el-checkbox>
          <el-checkbox v-model="query.includeBilling">請求先</el-checkbox>
          <el-checkbox v-model="query.includeReceipt">入金先</el-checkbox>
          <el-checkbox v-model="query.includeDelivery">納品先</el-checkbox>
          <el-checkbox v-model="query.includeCreditMgmt">与信管理先</el-checkbox>
          <el-checkbox v-model="query.includeSupplier">発注先</el-checkbox>
          <el-checkbox v-model="query.includeAccountsPayable">買掛先</el-checkbox>
          <el-checkbox v-model="query.includePaymentSchedule">支払予定管理先</el-checkbox>
          <el-checkbox v-model="query.includePayment">支払先</el-checkbox>
          <el-checkbox v-model="query.includeMaker">メーカ</el-checkbox>
        </div>

        <el-divider content-position="left">基本条件</el-divider>
        <el-form-item label="登録日 FROM"><el-date-picker v-model="query.registeredDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item label="登録日 TO"><el-date-picker v-model="query.registeredDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item label="取引先"><el-input v-model="query.bpCd" style="width: 160px" /></el-form-item>
        <el-form-item label="取引先名"><el-input v-model="query.bpName" style="width: 200px" /></el-form-item>
        <el-form-item label="法人番号"><el-input v-model="query.ein" style="width: 160px" /></el-form-item>
        <el-form-item label="標準企業コード"><el-input v-model="query.stdCoCd" style="width: 160px" /></el-form-item>
        <el-form-item label="郵便番号"><el-input v-model="query.zipCd" style="width: 130px" /></el-form-item>
        <el-form-item label="住所(LIKE)"><el-input v-model="query.addr" style="width: 200px" /></el-form-item>
        <el-form-item label="TEL"><el-input v-model="query.tel" style="width: 160px" /></el-form-item>
        <el-form-item label="営業担当"><el-input v-model="query.salesStaffCd" style="width: 130px" /></el-form-item>
        <el-form-item label="業務担当"><el-input v-model="query.businessStaffCd" style="width: 130px" /></el-form-item>

        <el-collapse v-model="advOpen" style="width: 100%">
          <el-collapse-item title="詳細：取引先分類 1〜10" name="adv">
            <el-form-item v-for="i in 10" :key="i" :label="`分類${i}`">
              <el-input v-model="(query as any)[`bpClass${String(i).padStart(2,'0')}`]" style="width: 110px" />
            </el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item label="ステータス">
          <el-checkbox v-model="query.includePreRegistered">事前登録</el-checkbox>
          <el-checkbox v-model="query.includeRegistered">本登録</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">検索</el-button>
          <el-button @click="resetQuery">クリア</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">CSV 出力</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
        <el-button v-if="selectedRow" type="primary" link size="small" style="margin-left: 12px" @click="goView">参照モードで開く</el-button>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600" highlight-current-row @current-change="onCurrentChange">
        <el-table-column prop="rowNo" label="№" width="60" align="center" />
        <el-table-column label="ステータス" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="bpCd" label="取引先" width="120" />
        <el-table-column prop="bpName" label="取引先名" min-width="200" />
        <el-table-column prop="bpAbbrev" label="略称" width="120" />
        <el-table-column prop="salesStaffCd" label="営業担当" width="120" />
        <el-table-column prop="businessStaffCd" label="業務担当" width="120" />
        <el-table-column prop="ein" label="法人番号" width="140" />
        <el-table-column prop="stdCoCd" label="標準企業" width="140" />
        <el-table-column prop="addr1" label="住所1" width="120" />
        <el-table-column prop="addr2" label="住所2" width="120" />
        <el-table-column label="得" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.customerFlg" /></template></el-table-column>
        <el-table-column label="売" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.accountsReceivableFlg" /></template></el-table-column>
        <el-table-column label="請" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.billingFlg" /></template></el-table-column>
        <el-table-column label="入" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.receiptFlg" /></template></el-table-column>
        <el-table-column label="納" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.deliveryFlg" /></template></el-table-column>
        <el-table-column label="信" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.creditMgmtFlg" /></template></el-table-column>
        <el-table-column label="発" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.supplierFlg" /></template></el-table-column>
        <el-table-column label="買" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.accountsPayableFlg" /></template></el-table-column>
        <el-table-column label="予" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.paymentScheduleFlg" /></template></el-table-column>
        <el-table-column label="払" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.paymentFlg" /></template></el-table-column>
        <el-table-column label="メ" width="44" align="center"><template #default="{ row }"><FlgIcon :on="row.makerFlg" /></template></el-table-column>
        <el-table-column prop="createDate" label="登録日" width="110">
          <template #default="{ row }">{{ row.createDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="creator" label="登録担当" width="110" />
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
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check } from '@element-plus/icons-vue'
import { bpApi } from '@/api/businessPartner'
import type { BpQueryDto, BpListItemDto } from '@/types/businessPartner'

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
  if (!anyFlg) { ElMessage.warning('E10030: 属性 FLG のいずれかを選択してください'); return }
  if (!query.includePreRegistered && !query.includeRegistered) { ElMessage.warning('E10030: ステータスのいずれかを選択してください'); return }

  loading.value = true
  try {
    const r = await bpApi.search(query)
    if (r.code === 0 && r.data) {
      rows.value = r.data.rows
      total.value = r.data.total
      if (rows.value.length === 0) ElMessage.info('E10008: 検索結果がありません')
    }
  } finally {
    loading.value = false
  }
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

function statusLabel(s: number): string {
  return s === 0 ? '事前登録' : s === 1 ? '本登録' : s === 9 ? '削除済' : '-'
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
