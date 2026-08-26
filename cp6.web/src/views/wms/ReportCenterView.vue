<template>
  <div class="wms-report">
    <el-card shadow="never" class="search-card">
      <el-form inline size="small">
        <el-form-item :label="t('wms.report.fld.type')">
          <el-select v-model="reportType" style="width: 180px" @change="onTypeChange">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>

        <!-- 月次 -->
        <el-form-item v-if="reportType === 'monthly'" :label="t('wms.report.fld.yearMonth')">
          <el-input v-model="form.yearMonth" placeholder="2026-05" style="width: 130px" />
        </el-form-item>

        <!-- ABC -->
        <el-form-item v-if="reportType === 'abc'" :label="t('wms.report.fld.analysisDays')">
          <el-input-number v-model="form.analysisDays" :min="1" :max="3650" style="width: 130px" controls-position="right" />
        </el-form-item>

        <!-- 滞留 -->
        <el-form-item v-if="reportType === 'dead'" :label="t('wms.report.fld.idleDays')">
          <el-input-number v-model="form.idleDays" :min="1" :max="3650" style="width: 130px" controls-position="right" />
        </el-form-item>

        <!-- 入出庫 -->
        <template v-if="reportType === 'inbound' || reportType === 'outbound'">
          <el-form-item :label="t('wms.report.fld.fromDate')">
            <el-date-picker v-model="form.fromDate" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
          </el-form-item>
          <el-form-item :label="t('wms.report.fld.toDate')">
            <el-date-picker v-model="form.toDate" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
          </el-form-item>
          <el-form-item :label="t('wms.report.fld.product')">
            <el-input v-model="form.productCd" clearable style="width: 140px" />
          </el-form-item>
        </template>

        <el-form-item :label="t('wms.report.fld.warehouse')">
          <el-input v-model="form.warehouseCd" clearable style="width: 120px" />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" @click="run" :loading="loading">{{ t('wms.report.btn.run') }}</el-button>
          <el-button :disabled="rows.length === 0" @click="downloadCsv">{{ t('wms.report.btn.csv') }}</el-button>
        </el-form-item>
        <el-form-item>
          <CpTag tone="info">{{ t('wms.report.fld.rows') }}: {{ rows.length }}</CpTag>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <CpEmpty v-if="rows.length === 0" :text="t('wms.report.msg.noData')" />

      <!-- Monthly -->
      <el-table v-if="reportType === 'monthly' && rows.length > 0" :data="rows" border stripe size="small" max-height="650">
        <el-table-column prop="yearMonth" :label="t('wms.report.fld.yearMonth')" width="100" />
        <el-table-column prop="warehouseCd" :label="t('wms.report.fld.warehouse')" width="100" />
        <el-table-column prop="productCd" :label="t('wms.report.fld.product')" width="160" />
        <el-table-column prop="physicalQty" :label="t('wms.report.monthly.physical')" width="120" align="right">
          <template #default="{ row }">{{ fmtQty(row.physicalQty) }}</template>
        </el-table-column>
        <el-table-column prop="allocatedQty" :label="t('wms.report.monthly.allocated')" width="120" align="right">
          <template #default="{ row }">{{ fmtQty(row.allocatedQty) }}</template>
        </el-table-column>
        <el-table-column prop="availableQty" :label="t('wms.report.monthly.available')" width="120" align="right">
          <template #default="{ row }">{{ fmtQty(row.availableQty) }}</template>
        </el-table-column>
        <el-table-column prop="lotCount" :label="t('wms.report.monthly.lotCount')" width="90" align="right" />
        <el-table-column prop="estimatedValue" :label="t('wms.report.monthly.value')" width="160" align="right">
          <template #default="{ row }">{{ fmtMoney(row.estimatedValue) }}</template>
        </el-table-column>
      </el-table>

      <!-- ABC -->
      <el-table v-if="reportType === 'abc' && rows.length > 0" :data="rows" border stripe size="small" max-height="650">
        <el-table-column prop="productCd" :label="t('wms.report.fld.product')" width="200" />
        <el-table-column prop="outCount" :label="t('wms.report.abc.outCount')" width="140" align="right" />
        <el-table-column prop="outQty" :label="t('wms.report.abc.outQty')" width="160" align="right">
          <template #default="{ row }">{{ fmtQty(row.outQty) }}</template>
        </el-table-column>
        <el-table-column prop="cumulativeRatio" :label="t('wms.report.abc.cumRatio')" width="140" align="right">
          <template #default="{ row }">{{ row.cumulativeRatio }}%</template>
        </el-table-column>
        <el-table-column :label="t('wms.report.abc.rank')" width="100" align="center">
          <template #default="{ row }">
            <CpTag :tone="row.abcRank === 'A' ? 'danger' : row.abcRank === 'B' ? 'warn' : 'info'">{{ row.abcRank }}</CpTag>
          </template>
        </el-table-column>
      </el-table>

      <!-- DeadStock -->
      <el-table v-if="reportType === 'dead' && rows.length > 0" :data="rows" border stripe size="small" max-height="650">
        <el-table-column prop="warehouseCd" :label="t('wms.report.fld.warehouse')" width="100" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="productCd" :label="t('wms.report.fld.product')" width="140" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="140" />
        <el-table-column prop="physicalQty" :label="t('wms.report.monthly.physical')" width="120" align="right">
          <template #default="{ row }">{{ fmtQty(row.physicalQty) }}</template>
        </el-table-column>
        <el-table-column prop="lastMovedAt" :label="t('wms.report.dead.lastMoved')" width="180" :formatter="formatDateTimeCell" />
        <el-table-column prop="idleDays" :label="t('wms.report.dead.idleDays')" width="120" align="right">
          <template #default="{ row }">
            <CpTag :tone="row.idleDays > 180 ? 'danger' : row.idleDays > 90 ? 'warn' : 'info'">{{ row.idleDays }}</CpTag>
          </template>
        </el-table-column>
        <el-table-column prop="estimatedValue" :label="t('wms.report.monthly.value')" width="140" align="right">
          <template #default="{ row }">{{ fmtMoney(row.estimatedValue) }}</template>
        </el-table-column>
      </el-table>

      <!-- InboundHistory / OutboundHistory -->
      <el-table v-if="(reportType === 'inbound' || reportType === 'outbound') && rows.length > 0" :data="rows" border stripe size="small" max-height="650">
        <el-table-column prop="txnNo" :label="t('wms.report.hist.txnNo')" width="200" />
        <el-table-column prop="txnDateTime" :label="t('wms.report.hist.dateTime')" width="180" :formatter="formatDateTimeCell" />
        <el-table-column prop="warehouseCd" :label="t('wms.report.fld.warehouse')" width="90" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="130" />
        <el-table-column prop="productCd" :label="t('wms.report.fld.product')" width="130" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="130" />
        <el-table-column prop="qty" :label="t('wms.common.qty')" width="120" align="right">
          <template #default="{ row }">{{ fmtQty(row.qty) }}</template>
        </el-table-column>
        <el-table-column prop="relatedNo" :label="t('wms.report.hist.relatedNo')" width="170" />
        <el-table-column prop="relatedType" :label="t('wms.report.hist.relatedType')" width="120" />
        <el-table-column prop="operatorCd" :label="t('wms.report.hist.operator')" width="100" />
      </el-table>

      <el-alert v-if="rows.length >= 5000" :title="t('wms.report.msg.maxLimit')" type="warning" :closable="false" show-icon style="margin-top: 12px" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { reportApi } from '@/api/wms/reportCenter'
import { formatQty, formatCurrency, formatDateTimeCell } from '@/utils/format'

const { t } = useI18n()

type ReportType = 'monthly' | 'abc' | 'dead' | 'inbound' | 'outbound'
const reportType = ref<ReportType>('monthly')
const rows = ref<any[]>([])
const loading = ref(false)

const today = new Date()
const ym = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
const firstOfMonth = `${ym}-01`
const todayStr = today.toISOString().slice(0, 10)

const form = reactive({
  yearMonth: ym,
  analysisDays: 90,
  idleDays: 90,
  fromDate: firstOfMonth,
  toDate: todayStr,
  warehouseCd: '',
  productCd: '',
})

const typeMap = computed<Record<ReportType, string>>(() => ({
  monthly: t('wms.report.type.monthly'),
  abc: t('wms.report.type.abc'),
  dead: t('wms.report.type.dead'),
  inbound: t('wms.report.type.inbound'),
  outbound: t('wms.report.type.outbound'),
}))

function fmtQty(n: number | undefined | null) {
  if (n == null) return ''
  return formatQty(n, 4)
}
function fmtMoney(n: number | undefined | null) {
  if (n == null) return ''
  return formatCurrency(n)
}

function onTypeChange() { rows.value = [] }

async function run() {
  loading.value = true
  rows.value = []
  try {
    let res: any
    const wh = form.warehouseCd || undefined
    switch (reportType.value) {
      case 'monthly':
        res = await reportApi.monthlyStock(form.yearMonth, wh); break
      case 'abc':
        res = await reportApi.abcAnalysis(form.analysisDays, wh); break
      case 'dead':
        res = await reportApi.deadStock(form.idleDays, wh); break
      case 'inbound':
        res = await reportApi.inboundHistory(form.fromDate, form.toDate, wh, form.productCd || undefined); break
      case 'outbound':
        res = await reportApi.outboundHistory(form.fromDate, form.toDate, wh, form.productCd || undefined); break
    }
    rows.value = res?.data || []
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || 'Error')
  } finally { loading.value = false }
}

async function downloadCsv() {
  const wh = form.warehouseCd || undefined
  const params: any = { warehouseCd: wh }
  let url = ''
  let filename = ''
  switch (reportType.value) {
    case 'monthly':
      url = '/wms/report-center/monthly-stock/csv'
      params.yearMonth = form.yearMonth
      filename = `monthly-stock-${form.yearMonth}.csv`
      break
    case 'abc':
      url = '/wms/report-center/abc-analysis/csv'
      params.analysisDays = form.analysisDays
      filename = `abc-analysis-${form.analysisDays}d.csv`
      break
    case 'dead':
      url = '/wms/report-center/dead-stock/csv'
      params.idleDays = form.idleDays
      filename = `dead-stock-${form.idleDays}d.csv`
      break
    case 'inbound':
      url = '/wms/report-center/inbound-history/csv'
      params.fromDate = form.fromDate
      params.toDate = form.toDate
      params.productCd = form.productCd || undefined
      filename = `inbound-${form.fromDate}-${form.toDate}.csv`
      break
    case 'outbound':
      url = '/wms/report-center/outbound-history/csv'
      params.fromDate = form.fromDate
      params.toDate = form.toDate
      params.productCd = form.productCd || undefined
      filename = `outbound-${form.fromDate}-${form.toDate}.csv`
      break
  }
  try { await reportApi.downloadCsv(url, params, filename) }
  catch (e: any) { ElMessage.error(e?.message || 'Download failed') }
}
</script>

<style scoped>
.wms-report { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
