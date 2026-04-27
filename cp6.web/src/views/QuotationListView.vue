<template>
  <div class="quotation-list">
    <!-- 検索エリア -->
    <el-card shadow="never" class="search-card">
      <el-form inline :model="query" @submit.prevent="onSearch">
        <el-form-item label="御見積書No">
          <el-input v-model="query.qtnNoFrom" placeholder="From" clearable style="width: 140px" />
          <span style="margin: 0 4px">~</span>
          <el-input v-model="query.qtnNoTo" placeholder="To" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item label="発行日">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="~"
            start-placeholder="開始日"
            end-placeholder="終了日"
            style="width: 260px"
          />
        </el-form-item>
        <el-form-item label="拠点">
          <el-select v-model="query.baseCd" placeholder="全部" clearable style="width: 160px">
            <el-option
              v-for="b in bases"
              :key="b.baseCd"
              :value="b.baseCd"
              :label="`${b.baseCd} ${b.baseName}`"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="担当">
          <el-input v-model="query.staffCd" placeholder="担当CD" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item label="顧客">
          <el-input v-model="query.customerCd" placeholder="顧客CD" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item label="親案件">
          <el-input v-model="query.projectNoParent" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item label="品名">
          <el-input v-model="query.customerProductName1" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item label="ステータス">
          <el-checkbox-group v-model="statusSel">
            <el-checkbox value="0">未承認</el-checkbox>
            <el-checkbox value="9">承認済</el-checkbox>
            <el-checkbox value="C">見積確定済</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="onSearch">検索</el-button>
          <el-button :icon="RefreshLeft" @click="onReset">クリア</el-button>
          <el-button type="success" :icon="Plus" @click="onNew">新規作成</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 一覧 -->
    <el-card shadow="never" class="table-card">
      <el-table
        :data="rows"
        v-loading="loading"
        stripe
        border
        style="width: 100%"
        @row-dblclick="(row: QuotationListItem) => onView(row)"
      >
        <el-table-column prop="qtnNo" label="御見積書No" width="120" fixed="left" />
        <el-table-column prop="qtnIssueDate" label="発行日" width="110">
          <template #default="{ row }">{{ fmtDate(row.qtnIssueDate) }}</template>
        </el-table-column>
        <el-table-column prop="baseCd" label="拠点" width="70" />
        <el-table-column prop="staffCd" label="担当" width="80">
          <template #default="{ row }">
            <el-tooltip v-if="row.staffName" :content="row.staffName" placement="top">
              <span>{{ row.staffCd }}</span>
            </el-tooltip>
            <span v-else>{{ row.staffCd }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="customerCd" label="顧客CD" width="90" />
        <el-table-column prop="customerName" label="顧客名" min-width="160" show-overflow-tooltip />
        <el-table-column prop="projectNoParent" label="親案件" width="110" />
        <el-table-column prop="projectNoChild" label="子案件" width="110" />
        <el-table-column prop="itemName1" label="品名1" min-width="160" show-overflow-tooltip />
        <el-table-column label="初行数量" width="100" align="right">
          <template #default="{ row }">{{ fmtNum(row.firstQuantity) }}</template>
        </el-table-column>
        <el-table-column label="初行単価" width="110" align="right">
          <template #default="{ row }">{{ fmtMoney(row.firstUnitPrice) }}</template>
        </el-table-column>
        <el-table-column label="初行金額" width="130" align="right">
          <template #default="{ row }">{{ fmtMoney(row.firstAmount) }}</template>
        </el-table-column>
        <el-table-column label="合計金額" width="130" align="right">
          <template #default="{ row }">{{ fmtMoney(row.totalAmount) }}</template>
        </el-table-column>
        <el-table-column label="状態" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">
              {{ row.status }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="320" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="onView(row)">照会</el-button>
            <el-button link type="warning" @click="onEdit(row)">訂正</el-button>
            <el-button link type="success" @click="onCopy(row)">流用</el-button>
            <el-button link type="info" @click="onIssue(row)">発行</el-button>
            <el-button link type="danger" @click="onDelete(row)">削除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination">
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadData"
          @size-change="loadData"
        />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, RefreshLeft, Plus } from '@element-plus/icons-vue'
import { quotationApi } from '@/api/quotation'
import { masterApi } from '@/api/master'
import type { QuotationListItem, QuotationQuery } from '@/types/quotation'
import type { MasterBase } from '@/types/estimateCalc'

const loading = ref(false)
const rows = ref<QuotationListItem[]>([])
const total = ref(0)
const bases = ref<MasterBase[]>([])

const query = reactive<QuotationQuery>({
  page: 1,
  pageSize: 20,
  qtnNoFrom: '',
  qtnNoTo: '',
  baseCd: '',
  staffCd: '',
  customerCd: '',
  projectNoParent: '',
  projectNoChild: '',
  projectNoMaterial: '',
  customerProductName1: '',
  customerProductName2: '',
  issueDateFrom: '',
  issueDateTo: '',
  statuses: [],
})
const dateRange = ref<[string, string] | null>(null)
const statusSel = ref<string[]>([])

watch(statusSel, v => (query.statuses = v.length ? [...v] : undefined))

function fmtDate(v?: string) {
  return v ? v.slice(0, 10) : ''
}
function fmtNum(v?: number) {
  return v == null ? '' : v.toLocaleString()
}
function fmtMoney(v?: number) {
  return v == null
    ? ''
    : v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
function statusTagType(s?: string): 'info' | 'warning' | 'success' {
  if (s === '見積確定済') return 'success'
  if (s === '承認済') return 'warning'
  return 'info'
}

async function loadData() {
  try {
    loading.value = true
    if (dateRange.value) {
      query.issueDateFrom = dateRange.value[0]
      query.issueDateTo = dateRange.value[1]
    } else {
      query.issueDateFrom = ''
      query.issueDateTo = ''
    }
    // 空字符串不要发送给后端
    const payload = { ...query } as unknown as Record<string, unknown>
    Object.keys(payload).forEach(k => {
      const v = payload[k]
      if (v === '' || v === null) delete payload[k]
    })
    const res = await quotationApi.getList(payload as unknown as QuotationQuery)
    if (res.code === 0) {
      rows.value = res.data.rows ?? []
      total.value = res.data.total ?? 0
    }
  } finally {
    loading.value = false
  }
}

function onSearch() {
  query.page = 1
  loadData()
}
function onReset() {
  Object.assign(query, {
    page: 1,
    pageSize: 20,
    qtnNoFrom: '',
    qtnNoTo: '',
    baseCd: '',
    staffCd: '',
    customerCd: '',
    projectNoParent: '',
    projectNoChild: '',
    projectNoMaterial: '',
    customerProductName1: '',
    customerProductName2: '',
    issueDateFrom: '',
    issueDateTo: '',
    statuses: [],
  })
  dateRange.value = null
  statusSel.value = []
  loadData()
}

/** 新しいタブで MSBBPA030 を開く */
function openInWindow(opc: 'new' | 'view' | 'edit' | 'copy', no?: string) {
  const qs = new URLSearchParams({ op: opc })
  if (no) qs.set('no', no)
  const url = `${window.location.origin}/quotation/window?${qs.toString()}`
  const w = window.open(url, '_blank')
  if (!w) {
    ElMessage.warning('新しいタブがブロックされました。このサイトに対してポップアップを許可してください')
  }
}

function onView(row: QuotationListItem) {
  openInWindow('view', row.qtnNo)
}
function onEdit(row: QuotationListItem) {
  openInWindow('edit', row.qtnNo)
}
function onCopy(row: QuotationListItem) {
  openInWindow('copy', row.qtnNo)
}
function onNew() {
  openInWindow('new')
}

async function onIssue(row: QuotationListItem) {
  try {
    const { value: choices } = await ElMessageBox.prompt(
      `${row.qtnNo} を発行します。対象を選択してください`,
      '発行',
      {
        inputType: 'text',
        inputValue: 'Q,SC,C',
        inputPlaceholder: 'Q=御見積書 / SC=提出用計算書 / C=計算書（カンマ区切り）',
        confirmButtonText: '発行',
        cancelButtonText: 'キャンセル',
      }
    )
    const set = new Set(String(choices).split(',').map(s => s.trim().toUpperCase()))
    const res = await quotationApi.issue(row.qtnNo, {
      issueQuotation: set.has('Q'),
      issueSubmitCalc: set.has('SC'),
      issueCalc: set.has('C'),
    })
    if (res.code === 0) {
      ElMessage.success(`発行しました: ${res.data.files.join(' , ') || '(ファイルなし)'}`)
      loadData()
    }
  } catch {
    /* キャンセル */
  }
}

async function onDelete(row: QuotationListItem) {
  if (row.status === '見積確定済') {
    ElMessage.warning('確定済の御見積書は削除できません。先に確定取消してください')
    return
  }
  try {
    await ElMessageBox.confirm(
      `${row.qtnNo} を削除します（論理削除・復旧不可）。よろしいですか？`,
      '削除確認',
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    const res = await quotationApi.remove(row.qtnNo)
    if (res.code === 0) {
      ElMessage.success('削除しました')
      loadData()
    }
  } catch {
    /* interceptor toast */
  }
}

// 子タブから保存/削除通知を受け取ったら自動リロード
function handleMessage(e: MessageEvent) {
  if (e.origin !== window.location.origin) return
  const data = e.data
  if (data?.source === 'cp6-quotation' && (data.type === 'saved' || data.type === 'deleted')) {
    loadData()
  }
}

onMounted(async () => {
  window.addEventListener('message', handleMessage)
  const [baseRes] = await Promise.all([masterApi.getBases(), loadData()])
  bases.value = baseRes.data ?? []
})

onBeforeUnmount(() => {
  window.removeEventListener('message', handleMessage)
})
</script>

<style scoped>
.quotation-list {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.search-card :deep(.el-card__body),
.table-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.pagination {
  margin-top: 12px;
  text-align: right;
}
</style>
