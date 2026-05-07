<template>
  <div class="plate-mold-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="120px" size="small" inline>
        <el-form-item label="拠点">
          <el-input v-model="query.baseCd" style="width: 130px" />
        </el-form-item>
        <el-form-item label="担当者"><el-input v-model="query.staffCd" style="width: 130px" /></el-form-item>
        <el-form-item label="適用開始日 FROM"><el-date-picker v-model="query.stDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item label="適用開始日 TO"><el-date-picker v-model="query.stDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
        <el-form-item label="手配NO"><el-input v-model="query.arrangeNo" style="width: 150px" /></el-form-item>
        <el-form-item label="案件NO"><el-input v-model="query.projectNo" style="width: 150px" /></el-form-item>
        <el-form-item label="版型NO FROM"><el-input v-model="query.wdPtnNoFrom" style="width: 150px" /></el-form-item>
        <el-form-item label="版型NO TO"><el-input v-model="query.wdPtnNoTo" style="width: 150px" /></el-form-item>
        <el-form-item label="版型名称"><el-input v-model="query.vrsnName" style="width: 200px" /></el-form-item>
        <el-form-item label="版型分類"><el-input v-model="query.typeClass" style="width: 130px" /></el-form-item>
        <el-form-item label="新版区分"><el-input v-model="query.newVerCd" style="width: 130px" /></el-form-item>
        <el-form-item label="得意先"><el-input v-model="query.customerCd" style="width: 130px" /></el-form-item>
        <el-form-item label="代表製品CD"><el-input v-model="query.representativeProductCd" style="width: 150px" /></el-form-item>
        <el-form-item label="工程"><el-input v-model="query.processCd" style="width: 130px" /></el-form-item>
        <el-form-item label="発注先"><el-input v-model="query.supplierCd" style="width: 130px" /></el-form-item>
        <el-form-item label="場所"><el-input v-model="query.placeCd" style="width: 130px" /></el-form-item>
        <el-form-item label="棚・ライン"><el-input v-model="query.shelfLineCd" style="width: 130px" /></el-form-item>

        <el-collapse v-model="advOpen" style="width: 100%">
          <el-collapse-item title="詳細：通し数 / 限界 / 各日付" name="adv">
            <el-form-item label="通し数 FROM"><el-input-number v-model="query.wdQtyFrom" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item label="通し数 TO"><el-input-number v-model="query.wdQtyTo" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item label="限界通し数 FROM"><el-input-number v-model="query.limitWdQtyFrom" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item label="限界通し数 TO"><el-input-number v-model="query.limitWdQtyTo" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item label="最終使用 FROM"><el-date-picker v-model="query.achieveDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="最終使用 TO"><el-date-picker v-model="query.achieveDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="入荷日 FROM"><el-date-picker v-model="query.arrivalDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="入荷日 TO"><el-date-picker v-model="query.arrivalDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="廃棄予定 FROM"><el-date-picker v-model="query.dispScheduleFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="廃棄予定 TO"><el-date-picker v-model="query.dispScheduleTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="返却予定 FROM"><el-date-picker v-model="query.returnScheduleFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="返却予定 TO"><el-date-picker v-model="query.returnScheduleTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="返却日 FROM"><el-date-picker v-model="query.returnDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
            <el-form-item label="返却日 TO"><el-date-picker v-model="query.returnDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" /></el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item label="表示条件">
          <el-checkbox v-model="query.onlyLatestRev">最新Revのみ表示</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">表示</el-button>
          <el-button @click="resetQuery">クリア</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">CSV 出力</el-button>
          <el-button type="warning" :disabled="checkedCount === 0" @click="onIssueLabel" :loading="issuing">
            ラベル発行 ({{ checkedCount }})
          </el-button>
          <el-button v-if="isPickerMode" type="success" :disabled="!selectedRow" @click="onPick">
            選択して戻る
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
        <el-tag v-if="checkedCount" type="success" size="small" style="margin-left: 8px;">発行☑ {{ checkedCount }} 件</el-tag>
      </div>

      <el-table
        :data="rows" border stripe size="small" max-height="600" style="width: 100%"
        highlight-current-row @current-change="(r: PlateMoldListItemDto | null) => selectedRow = r"
      >
        <el-table-column prop="rowNo" label="NO" width="60" align="center" />
        <el-table-column label="発行" width="60" align="center">
          <template #default="{ row }"><el-checkbox v-model="row.issue" /></template>
        </el-table-column>
        <el-table-column prop="wdPtnNo" label="版型NO" width="160" fixed="left" />
        <el-table-column prop="vrsnName" label="版型名" min-width="160" fixed="left" />
        <el-table-column prop="wdRev" label="Rev" width="60" align="center" />
        <el-table-column prop="typeClass" label="版型分類" width="100" />
        <el-table-column prop="customerCd" label="得意先" width="100" />
        <el-table-column prop="customerName" label="得意先名" width="160" />
        <el-table-column prop="representativeProductCd" label="製品コード" width="130" />
        <el-table-column prop="representativeProductName" label="製品名" min-width="200" />
        <el-table-column prop="wdQty" label="通し数" width="100" align="right" />
        <el-table-column prop="limitWdQty" label="限界通し数" width="110" align="right" />
        <el-table-column prop="achieveDate" label="最終使用実績日" width="130">
          <template #default="{ row }">{{ row.achieveDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="supplierCd" label="発注先" width="100" />
        <el-table-column prop="supplierName" label="発注先名" width="160" />
        <el-table-column prop="arrivalActualDate" label="入荷日" width="120">
          <template #default="{ row }">{{ row.arrivalActualDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="dispScheduleDate" label="廃棄予定日" width="120">
          <template #default="{ row }">{{ row.dispScheduleDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnScheduleDate" label="返却予定日" width="120">
          <template #default="{ row }">{{ row.returnScheduleDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnDate" label="返却日" width="120">
          <template #default="{ row }">{{ row.returnDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="returnReason" label="返却理由" min-width="160" />
        <el-table-column prop="processCd" label="工程" width="100" />
        <el-table-column prop="placeCd" label="場所" width="100" />
        <el-table-column prop="shelfLineCd" label="棚・ライン" width="120" />
        <el-table-column prop="newVerCd" label="新版区分" width="100" />
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
        <el-table-column label="操作" width="100" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goView(row)">詳細</el-button>
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
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download } from '@element-plus/icons-vue'
import { plateMoldApi } from '@/api/plateMold'
import type { PlateMoldQueryDto, PlateMoldListItemDto } from '@/types/plateMold'

const route = useRoute()
const router = useRouter()

const query = reactive<PlateMoldQueryDto>({
  onlyLatestRev: true, page: 1, pageSize: 100,
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

async function search() {
  // FROM ≤ TO チェック（簡易：5 ペア）
  const pairs: [keyof PlateMoldQueryDto, keyof PlateMoldQueryDto, string][] = [
    ['stDateFrom', 'stDateTo', '適用開始日'],
    ['wdQtyFrom', 'wdQtyTo', '通し数'],
    ['limitWdQtyFrom', 'limitWdQtyTo', '限界通し数'],
    ['achieveDateFrom', 'achieveDateTo', '最終使用実績日'],
    ['arrivalDateFrom', 'arrivalDateTo', '入荷日'],
  ]
  for (const [f, t, label] of pairs) {
    const fv = query[f] as any, tv = query[t] as any
    if (fv != null && tv != null && fv > tv) {
      ElMessage.warning(`E10036: FROM≦TO の関係で指定してください。項目名:${label}`); return
    }
  }
  loading.value = true
  try {
    const r = await plateMoldApi.search(query)
    if (r.code === 0 && r.data) {
      rows.value = r.data.rows
      total.value = r.data.total
      if (rows.value.length === 0) ElMessage.info('E10008: 検索結果がありません')
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
  if (targets.length === 0) { ElMessage.warning('E10010: 発行対象がありません'); return }
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
  if (!selectedRow.value) { ElMessage.warning('E10009: 選択行がありません'); return }
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
