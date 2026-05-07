<template>
  <div class="fsc-view">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="120px" size="small" inline>
        <el-form-item label="拠点" required>
          <el-input v-model="query.baseCd" placeholder="必須" style="width: 130px" />
        </el-form-item>
        <el-form-item label="担当者">
          <el-input v-model="query.staffCd" style="width: 130px" />
        </el-form-item>
        <el-form-item label="作成日 FROM">
          <el-date-picker v-model="query.issueDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="作成日 TO">
          <el-date-picker v-model="query.issueDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="御見積書 FROM"><el-input v-model="query.qtnNoFrom" style="width: 150px" /></el-form-item>
        <el-form-item label="御見積書 TO"><el-input v-model="query.qtnNoTo" style="width: 150px" /></el-form-item>
        <el-form-item label="得意先"><el-input v-model="query.customerCd" style="width: 150px" /></el-form-item>
        <el-form-item label="案件 No"><el-input v-model="query.projectNo" style="width: 150px" /></el-form-item>
        <el-form-item label="ステータス">
          <el-checkbox v-model="query.includeUnissued">未発行</el-checkbox>
          <el-checkbox v-model="query.includeIssued">発行済</el-checkbox>
        </el-form-item>
        <el-form-item label="出力フォーマット" required>
          <el-select v-model="formatName" placeholder="必須" clearable style="width: 220px">
            <el-option v-for="f in formats" :key="f.name" :label="f.name" :value="f.name" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">検索</el-button>
          <el-button @click="resetQuery">クリア</el-button>
          <el-button type="success" :icon="Document" @click="onIssue" :loading="issuing" :disabled="checkedCount === 0">
            発行（{{ checkedCount }} 件）
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
        <el-tag v-if="checkedCount > 0" type="success" size="small" style="margin-left: 8px;">発行☑ {{ checkedCount }} 件</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600">
        <el-table-column prop="rowNo" label="NO" width="60" align="center" />
        <el-table-column prop="staffCd" label="担当" width="100" />
        <el-table-column prop="staffName" label="担当者名" width="120" />
        <el-table-column prop="customerCd" label="得意先" width="100" />
        <el-table-column prop="customerName" label="得意先名" width="160" />
        <el-table-column prop="projectNo" label="案件NO" width="120" />
        <el-table-column prop="qtnNo" label="御見積書NO" width="130" />
        <el-table-column prop="qtnIssueDate" label="発行日" width="110">
          <template #default="{ row }">{{ row.qtnIssueDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="customerItemName1" label="顧客品名1" min-width="160" />
        <el-table-column prop="customerItemName2" label="顧客品名2" min-width="160" />
        <el-table-column prop="quantity" label="見積数" width="100" align="right" />
        <el-table-column prop="unitPrice" label="単価" width="120" align="right" />
        <el-table-column prop="amount" label="金額" width="120" align="right" />
        <el-table-column prop="totalAmount" label="合計金額" width="130" align="right" />
        <el-table-column label="ステータス" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? '確定' : '未' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="発行☑" width="80" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.issue" />
          </template>
        </el-table-column>
        <el-table-column prop="fscManagementNo" label="FSC管理NO" width="130" />
        <el-table-column label="発行済" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.issuedLabel" type="success" size="small">{{ row.issuedLabel }}</el-tag>
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
import { ElMessage, ElMessageBox } from 'element-plus'
import { Document } from '@element-plus/icons-vue'
import { fscApi } from '@/api/fsc'
import type { FscChecklistQueryDto, FscChecklistItemDto, FscFormat } from '@/types/fsc'

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
  if (!query.baseCd) { ElMessage.warning('E10022: 拠点を指定してください'); return }
  if (!query.includeUnissued && !query.includeIssued) {
    ElMessage.warning('E10030: ステータスのいずれかを選択してください'); return
  }
  // FROM ≤ TO
  if (query.issueDateFrom && query.issueDateTo && query.issueDateFrom > query.issueDateTo) {
    ElMessage.warning('E10036: 御見積書作成日は FROM ≤ TO で指定してください'); return
  }
  if (query.qtnNoFrom && query.qtnNoTo && query.qtnNoFrom > query.qtnNoTo) {
    ElMessage.warning('E10036: 御見積書 NO は FROM ≤ TO で指定してください'); return
  }

  loading.value = true
  try {
    const r = await fscApi.search(query)
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
    if (k === 'includeUnissued') query.includeUnissued = true
    else if (k === 'includeIssued') query.includeIssued = false
    else (query as any)[k] = undefined
  })
  query.page = 1
  rows.value = []
  total.value = 0
}

async function onIssue() {
  if (!formatName.value) { ElMessage.warning('E10022: 出力フォーマットを選択してください'); return }
  const checked = rows.value.filter(r => r.issue)
  if (checked.length === 0) { ElMessage.warning('発行☑の行がありません'); return }

  try {
    await ElMessageBox.confirm(`${checked.length} 件のチェックシートを発行します。よろしいですか？`, '確認', { type: 'warning' })
  } catch { return }

  issuing.value = true
  try {
    const r = await fscApi.issue({
      formatName: formatName.value,
      targets: checked.map(c => ({ qtnNo: c.qtnNo, qtnCalcNo: c.qtnCalcNo, customerCd: c.customerCd, staffCd: c.staffCd })),
    })
    if (r.code === 0 && r.data) {
      ElMessage.success(`${r.data.issuedCount} 件のチェックシートを発行しました`)
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
