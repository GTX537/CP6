<template>
  <div class="plate-mold-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="120px" size="small" inline>
        <el-form-item :label="t('sales.term.base')">
          <el-input v-model="query.baseCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.staff')"><el-input v-model="query.staffCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.pm.applyStartDate') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.stDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.pm.applyStartDate') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.stDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.term.haibaiNo')"><el-input v-model="query.arrangeNo" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.fsc.case')"><el-input v-model="query.projectNo" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.pm.no') + ' ' + t('sales.search.from')"><el-input v-model="query.wdPtnNoFrom" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.pm.no') + ' ' + t('sales.search.to')"><el-input v-model="query.wdPtnNoTo" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.pm.name')"><el-input v-model="query.vrsnName" style="width: 200px" /></el-form-item>
        <el-form-item :label="t('sales.pm.class')"><el-input v-model="query.typeClass" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.pm.newVersion')"><el-input v-model="query.newVerCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.term.customer')"><el-input v-model="query.customerCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.pm.repProductCd')"><el-input v-model="query.representativeProductCd" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.pm.process')"><el-input v-model="query.processCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.term.supplier')"><el-input v-model="query.supplierCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.pm.location')"><el-input v-model="query.placeCd" style="width: 130px" /></el-form-item>
        <el-form-item :label="t('sales.pm.shelfLine')"><el-input v-model="query.shelfLineCd" style="width: 130px" /></el-form-item>

        <el-collapse v-model="advOpen" style="width: 100%">
          <el-collapse-item :title="t('sales.list.detail')" name="adv">
            <el-form-item :label="t('sales.pm.passCount') + ' ' + t('sales.search.from')"><el-input-number v-model="query.wdQtyFrom" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.passCount') + ' ' + t('sales.search.to')"><el-input-number v-model="query.wdQtyTo" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.limitPass') + ' ' + t('sales.search.from')"><el-input-number v-model="query.limitWdQtyFrom" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.limitPass') + ' ' + t('sales.search.to')"><el-input-number v-model="query.limitWdQtyTo" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.lastUsedDate') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.achieveDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.lastUsedDate') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.achieveDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.arrivalDate') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.arrivalDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.arrivalDate') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.arrivalDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.dispScheduled') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.dispScheduleFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.dispScheduled') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.dispScheduleTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnScheduled') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.returnScheduleFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnScheduled') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.returnScheduleTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnDate') + ' ' + t('sales.search.from')"><el-date-picker v-model="query.returnDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnDate') + ' ' + t('sales.search.to')"><el-date-picker v-model="query.returnDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item :label="t('sales.term.status')">
          <el-checkbox v-model="query.onlyLatestRev">{{ t('sales.pm.onlyLatestRev') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.show') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">{{ t('sales.btn.exportCsv') }}</el-button>
          <el-button type="warning" :disabled="checkedCount === 0" @click="onIssueLabel" :loading="issuing">
            {{ t('sales.btn.label') }} ({{ checkedCount }})
          </el-button>
          <el-button v-if="isPickerMode" type="success" :disabled="!selectedRow" @click="onPick">
            {{ t('sales.btn.selectReturn') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">{{ t('sales.list.totalCount', { n: total }) }}</el-tag>
        <el-tag v-if="checkedCount" type="success" size="small" style="margin-left: 8px;">{{ t('sales.btn.issue') }} ☑ {{ checkedCount }}</el-tag>
      </div>

      <el-table
        :data="rows" border stripe size="small" max-height="600" style="width: 100%"
        highlight-current-row @current-change="(r: PlateMoldListItemDto | null) => selectedRow = r"
        @sort-change="onSortChange"
      >
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column :label="t('sales.btn.issue')" width="60" align="center">
          <template #default="{ row }"><el-checkbox v-model="row.issue" /></template>
        </el-table-column>
        <el-table-column prop="wdPtnNo" :label="t('sales.pm.no')" width="160" fixed="left" sortable="custom" />
        <el-table-column prop="vrsnName" :label="t('sales.pm.name')" min-width="160" fixed="left" sortable="custom" />
        <el-table-column prop="wdRev" :label="t('sales.term.rev')" width="60" align="center" sortable="custom" />
        <el-table-column prop="typeClass" :label="t('sales.pm.class')" width="100" sortable="custom" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="100" sortable="custom" />
        <el-table-column prop="customerName" :label="t('sales.term.customer')" width="160" />
        <el-table-column prop="representativeProductCd" :label="t('sales.term.productCd')" width="130" sortable="custom" />
        <el-table-column prop="representativeProductName" :label="t('sales.term.product')" min-width="200" />
        <el-table-column prop="wdQty" :label="t('sales.pm.passCount')" width="100" align="right" sortable="custom" />
        <el-table-column prop="limitWdQty" :label="t('sales.pm.limitPass')" width="110" align="right" sortable="custom" />
        <el-table-column prop="achieveDate" :label="t('sales.pm.lastUsedDate')" width="130" sortable="custom">
          <template #default="{ row }">{{ row.achieveDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="supplierCd" :label="t('sales.term.supplier')" width="100" sortable="custom" />
        <el-table-column prop="supplierName" :label="t('sales.term.supplier')" width="160" />
        <el-table-column prop="arrivalActualDate" :label="t('sales.pm.arrivalDate')" width="120" sortable="custom">
          <template #default="{ row }">{{ row.arrivalActualDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="dispScheduleDate" :label="t('sales.pm.dispScheduled')" width="120" sortable="custom">
          <template #default="{ row }">{{ row.dispScheduleDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnScheduleDate" :label="t('sales.pm.returnScheduled')" width="120" sortable="custom">
          <template #default="{ row }">{{ row.returnScheduleDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnDate" :label="t('sales.pm.returnDate')" width="120" sortable="custom">
          <template #default="{ row }">{{ row.returnDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnReason" label="返却理由" min-width="160" />
        <el-table-column prop="processCd" :label="t('sales.pm.process')" width="100" sortable="custom" />
        <el-table-column prop="placeCd" :label="t('sales.pm.location')" width="100" />
        <el-table-column prop="shelfLineCd" :label="t('sales.pm.shelfLine')" width="120" />
        <el-table-column prop="newVerCd" :label="t('sales.pm.newVersion')" width="100" />
        <el-table-column prop="bladeWidth" label="刃渡(巾)" width="100" align="right" />
        <el-table-column prop="bladeFlow" label="刃渡(流れ)" width="100" align="right" />
        <el-table-column prop="compositionQty" label="付数" width="80" align="right" />
        <el-table-column prop="mfgQty" label="個数" width="80" align="right" />
        <el-table-column prop="colorQty" label="色数" width="80" align="right" />
        <el-table-column prop="strippingCd" label="落丁型" width="100" />
        <el-table-column prop="standingCd" label="置き版" width="100" />
        <el-table-column prop="hammerCd" label="ハンマー" width="100" />
        <el-table-column prop="oversightCd" label="見落とし型" width="110" />
        <el-table-column prop="memo" label="備考" min-width="160" />
        <el-table-column :label="t('sales.list.action')" width="100" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goView(row)">{{ t('sales.list.detail') }}</el-button>
          </template>
        </el-table-column>
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
import { ref, reactive, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download } from '@element-plus/icons-vue'
import { plateMoldApi } from '@/api/plateMold'
import type { PlateMoldQueryDto, PlateMoldListItemDto } from '@/types/plateMold'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const query = reactive<PlateMoldQueryDto>({
  onlyLatestRev: true, page: 1, pageSize: 100,
  sortField: '', sortOrder: '',
})
const advOpen = ref<string[]>([])
const rows = ref<PlateMoldListItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const exporting = ref(false)
const issuing = ref(false)
const selectedRow = ref<PlateMoldListItemDto | null>(null)

const isPickerMode = computed(() => route.query.picker === '1')
const checkedCount = computed(() => rows.value.filter(r => r.issue).length)

function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  search()
}

async function search() {
  // FROM ≤ TO チェック（簡易：5 ペア）
  const pairs: [keyof PlateMoldQueryDto, keyof PlateMoldQueryDto, string][] = [
    ['stDateFrom', 'stDateTo', t('sales.pm.applyStartDate')],
    ['wdQtyFrom', 'wdQtyTo', t('sales.pm.passCount')],
    ['limitWdQtyFrom', 'limitWdQtyTo', t('sales.pm.limitPass')],
    ['achieveDateFrom', 'achieveDateTo', t('sales.pm.lastUsedDate')],
    ['arrivalDateFrom', 'arrivalDateTo', t('sales.pm.arrivalDate')],
  ]
  for (const [f, tk, label] of pairs) {
    const fv = query[f] as any, tv = query[tk] as any
    if (fv != null && tv != null && fv > tv) {
      ElMessage.warning(`${t('sales.err.E10036')} (${label})`); return
    }
  }
  loading.value = true
  try {
    const r = await plateMoldApi.search(query)
    if (r.code === 0 && r.data) {
      rows.value = r.data.rows
      total.value = r.data.total
      if (rows.value.length === 0) ElMessage.info(t('sales.err.E10008'))
    }
  } finally { loading.value = false }
}

function resetQuery() {
  Object.keys(query).forEach(k => {
    if (k === 'page' || k === 'pageSize' || k === 'onlyLatestRev') return
    ;(query as any)[k] = undefined
  })
  query.onlyLatestRev = true
  query.page = 1
  rows.value = []; total.value = 0
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = await plateMoldApi.exportCsv(query) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `plate-molds_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  } finally { exporting.value = false }
}

async function onIssueLabel() {
  const targets = rows.value.filter(r => r.issue).map(r => ({ wdPtnNo: r.wdPtnNo, wdRev: r.wdRev }))
  if (targets.length === 0) { ElMessage.warning(t('sales.err.E10010')); return }
  issuing.value = true
  try {
    const blob = await plateMoldApi.issueLabel(targets) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `plate-mold-labels_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
    rows.value.forEach(r => r.issue = false)
  } finally { issuing.value = false }
}

function onPick() {
  if (!selectedRow.value) { ElMessage.warning(t('sales.err.E10009')); return }
  // 検索モード：呼出元 PA140 へ戻す（簡略：query string で返す）
  router.push({ path: '/plate-mold', query: { wdPtnNo: selectedRow.value.wdPtnNo, rev: selectedRow.value.wdRev } })
}

function goView(row: PlateMoldListItemDto) {
  router.push({ path: '/plate-mold', query: { wdPtnNo: row.wdPtnNo, rev: row.wdRev, mode: 'view' } })
}
</script>

<style scoped>
.plate-mold-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
:deep(.el-form-item) { margin-bottom: 8px; }
</style>
