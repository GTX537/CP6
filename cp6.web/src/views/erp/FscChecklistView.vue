<!--
  FSC チェックシート —— 検索先行（拠点必須・自動取得なし・FROM≤TO/フォーマット必須のクロス項目検証）＋発行アクションの特殊页。
  CpListPage の onMounted 自動 fetch 契約と相反（拠点未指定では取得しない）ため「非表格特殊页」として token 化：
  el-form 検索区／el-table／el-pagination／発行フローは原様保全し、状態・件数・発行済 el-tag → CpTag(+tone) のみ置換。
-->
<template>
  <div class="fsc-view">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="120px" size="small" inline>
        <el-form-item :label="t('sales.term.base')" required>
          <el-input v-model="query.baseCd" :placeholder="t('sales.search.required')" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.staff')">
          <el-input v-model="query.staffCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.qtn.issueDate') + ' ' + t('sales.search.from')">
          <el-date-picker v-model="query.issueDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.qtn.issueDate') + ' ' + t('sales.search.to')">
          <el-date-picker v-model="query.issueDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.qtnNo') + ' ' + t('sales.search.from')"><el-input v-model="query.qtnNoFrom" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.term.qtnNo') + ' ' + t('sales.search.to')"><el-input v-model="query.qtnNoTo" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.term.customer')"><el-input v-model="query.customerCd" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.fsc.case')"><el-input v-model="query.projectNo" style="width: 150px" /></el-form-item>
        <el-form-item :label="t('sales.term.status')">
          <el-checkbox v-model="query.includeUnissued">{{ t('sales.fsc.unissued') }}</el-checkbox>
          <el-checkbox v-model="query.includeIssued">{{ t('sales.fsc.issued') }}</el-checkbox>
        </el-form-item>
        <el-form-item :label="t('sales.fsc.format')" required>
          <el-select v-model="formatName" :placeholder="t('sales.search.required')" clearable style="width: 220px">
            <el-option v-for="f in formats" :key="f.name" :label="f.name" :value="f.name" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button v-permission="'erp-fsc-checklist:issue'" type="success" :icon="Document" @click="onIssue" :loading="issuing" :disabled="checkedCount === 0">
            {{ t('sales.btn.issue') }}（{{ checkedCount }}）
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <CpTag tone="info">{{ t('sales.list.totalCount', { n: total }) }}</CpTag>
        <CpTag v-if="checkedCount > 0" tone="ok" style="margin-left: 8px;">{{ t('sales.btn.issue') }} ☑ {{ checkedCount }}</CpTag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600">
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column prop="staffCd" :label="t('sales.term.staff')" width="100" />
        <el-table-column prop="staffName" :label="t('sales.term.staff') + t('sales.term.bpName').slice(-1)" width="120" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="100" />
        <el-table-column prop="customerName" :label="t('sales.term.customer') + t('sales.term.bpName').slice(-1)" width="160" />
        <el-table-column prop="projectNo" :label="t('sales.fsc.case')" width="120" />
        <el-table-column prop="qtnNo" :label="t('sales.term.qtnNo')" width="130" />
        <el-table-column prop="qtnIssueDate" :label="t('sales.qtn.issueDate')" width="110">
          <template #default="{ row }">{{ row.qtnIssueDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="customerItemName1" :label="t('sales.fsc.itemName1')" min-width="160" />
        <el-table-column prop="customerItemName2" :label="t('sales.fsc.itemName2')" min-width="160" />
        <el-table-column prop="quantity" :label="t('sales.fsc.estimateQty')" width="100" align="right" />
        <el-table-column prop="unitPrice" :label="t('sales.term.unitPrice')" width="120" align="right" />
        <el-table-column prop="amount" :label="t('sales.term.amount')" width="120" align="right" />
        <el-table-column prop="totalAmount" :label="t('sales.fsc.totalAmount')" width="130" align="right" />
        <el-table-column :label="t('sales.term.status')" width="100" align="center">
          <template #default="{ row }">
            <CpTag :tone="row.status === 1 ? 'ok' : 'info'">{{ row.status === 1 ? t('sales.fsc.confirmed') : t('sales.fsc.notConfirmed') }}</CpTag>
          </template>
        </el-table-column>
        <el-table-column :label="t('sales.btn.issue') + '☑'" width="80" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.issue" />
          </template>
        </el-table-column>
        <el-table-column prop="fscManagementNo" :label="t('sales.fsc.mgmtNo')" width="130" />
        <el-table-column :label="t('sales.fsc.issued')" width="80" align="center">
          <template #default="{ row }">
            <CpTag v-if="row.issuedLabel" tone="ok">{{ row.issuedLabel }}</CpTag>
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
import { ref, reactive, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Document } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import { fscApi } from '@/api/erp/fsc'
import type { FscChecklistQueryDto, FscChecklistItemDto, FscFormat } from '@/types/erp/fsc'

const { t } = useI18n()

const query = reactive<FscChecklistQueryDto>({
  includeUnissued: true, includeIssued: false,
  page: 1, pageSize: 100,
})
const rows = ref<FscChecklistItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const issuing = ref(false)
const formatName = ref<string>('')
const formats = ref<FscFormat[]>([])

const checkedCount = computed(() => rows.value.filter(r => r.issue).length)

onMounted(async () => {
  try {
    const r = await fscApi.getFormats()
    if (r.code === 0 && r.data) {
      formats.value = r.data
      if (formats.value.length > 0) formatName.value = formats.value[0]!.name
    }
  } catch { /* */ }
})

async function search() {
  if (!query.baseCd) { ElMessage.warning(t('拠点を指定してください')); return }
  if (!query.includeUnissued && !query.includeIssued) {
    ElMessage.warning(t('ステータスのいずれかを選択してください')); return
  }
  // FROM ≤ TO
  if (query.issueDateFrom && query.issueDateTo && query.issueDateFrom > query.issueDateTo) {
    ElMessage.warning(t('御見積書作成日は FROM ≤ TO で指定してください')); return
  }
  if (query.qtnNoFrom && query.qtnNoTo && query.qtnNoFrom > query.qtnNoTo) {
    ElMessage.warning(t('御見積書 NO は FROM ≤ TO で指定してください')); return
  }

  loading.value = true
  try {
    const r = await fscApi.search(query)
    if (r.code === 0 && r.data) {
      rows.value = r.data.rows
      total.value = r.data.total
      if (rows.value.length === 0) ElMessage.info(t('検索結果がありません'))
    }
  } finally {
    loading.value = false
  }
}

function resetQuery() {
  Object.keys(query).forEach(k => {
    if (k === 'page' || k === 'pageSize') return
    if (k === 'includeUnissued') query.includeUnissued = true
    else if (k === 'includeIssued') query.includeIssued = false
    else (query as any)[k] = undefined
  })
  query.page = 1
  rows.value = []
  total.value = 0
}

async function onIssue() {
  if (!formatName.value) { ElMessage.warning(t('出力フォーマットを選択してください')); return }
  const checked = rows.value.filter(r => r.issue)
  if (checked.length === 0) { ElMessage.warning(t('発行☑の行がありません')); return }

  try {
    await ElMessageBox.confirm(t('{n} 件のチェックシートを発行します。よろしいですか？', { n: checked.length }), t('確認'), { type: 'warning' })
  } catch { return }

  issuing.value = true
  try {
    const r = await fscApi.issue({
      formatName: formatName.value,
      targets: checked.map(c => ({ qtnNo: c.qtnNo, qtnCalcNo: c.qtnCalcNo, customerCd: c.customerCd, staffCd: c.staffCd })),
    })
    if (r.code === 0 && r.data) {
      ElMessage.success(t('{n} 件のチェックシートを発行しました', { n: r.data.issuedCount }))
      // Excel ダウンロード（最初の 1 件をブラウザで開く）
      if (r.data.items.length > 0) {
        const first = r.data.items[0]!
        if (first.content) {
          const bytes = atob(first.content)
          const arr = new Uint8Array(bytes.length)
          for (let i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i)
          const blob = new Blob([arr], { type: 'application/octet-stream' })
          const url = URL.createObjectURL(blob)
          const a = document.createElement('a')
          a.href = url; a.download = first.excelFileName; a.click()
          URL.revokeObjectURL(url)
        }
      }
      await search()
    }
  } finally {
    issuing.value = false
  }
}
</script>

<style scoped>
.fsc-view { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
