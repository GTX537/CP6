<template>
  <div class="quotation-list">
    <!-- 検索エリア -->
    <el-card shadow="never" class="search-card">
      <el-form inline :model="query" @submit.prevent="onSearch">
        <el-form-item :label="t('sales.term.qtnNo')">
          <el-input v-model="query.qtnNoFrom" :placeholder="t('sales.search.from')" clearable style="width: 140px" />
          <span style="margin: 0 4px">~</span>
          <el-input v-model="query.qtnNoTo" :placeholder="t('sales.search.to')" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('sales.qtn.issueDate')">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="~"
            :start-placeholder="t('sales.search.dateFrom')"
            :end-placeholder="t('sales.search.dateTo')"
            style="width: 260px"
          />
        </el-form-item>
        <el-form-item :label="t('sales.term.base')">
          <el-select v-model="query.baseCd" :placeholder="t('全部')" clearable style="width: 160px">
            <el-option
              v-for="b in bases"
              :key="b.baseCd"
              :value="b.baseCd"
              :label="`${b.baseCd} ${b.baseName}`"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('sales.term.staff')">
          <el-input v-model="query.staffCd" :placeholder="t('sales.term.staff') + ' CD'" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer')">
          <el-input v-model="query.customerCd" :placeholder="t('sales.term.customer') + ' CD'" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('親案件')">
          <el-input v-model="query.projectNoParent" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item :label="t('品名')">
          <el-input v-model="query.customerProductName1" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.status')">
          <el-checkbox-group v-model="statusSel">
            <el-checkbox value="0">{{ t('sales.fsc.notConfirmed') }}</el-checkbox>
            <el-checkbox value="9">{{ t('sales.status.approved') }}</el-checkbox>
            <el-checkbox value="C">{{ t('sales.status.confirmed') }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="onSearch">{{ t('sales.btn.search') }}</el-button>
          <el-button :icon="RefreshLeft" @click="onReset">{{ t('sales.btn.clear') }}</el-button>
          <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 一覧 -->
    <el-card shadow="never" class="table-card">
      <!-- 桌面端：完整表格 -->
      <el-table
        v-if="!isMobile"
        :data="rows"
        v-loading="loading"
        stripe
        border
        style="width: 100%"
        @row-dblclick="(row: QuotationListItem) => onView(row)"
        @sort-change="onSortChange"
      >
        <el-table-column prop="qtnNo" :label="t('sales.term.qtnNo')" width="120" fixed="left" sortable="custom" />
        <el-table-column prop="qtnIssueDate" :label="t('sales.qtn.issueDate')" width="110" sortable="custom">
          <template #default="{ row }">{{ fmtDate(row.qtnIssueDate) }}</template>
        </el-table-column>
        <el-table-column prop="baseCd" :label="t('sales.term.base')" width="70" sortable="custom" />
        <el-table-column prop="staffCd" :label="t('sales.term.staff')" width="80" sortable="custom">
          <template #default="{ row }">
            <el-tooltip v-if="row.staffName" :content="row.staffName" placement="top">
              <span>{{ row.staffCd }}</span>
            </el-tooltip>
            <span v-else>{{ row.staffCd }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('sales.term.customer') + ' CD'" width="90" sortable="custom" />
        <el-table-column prop="customerName" :label="t('sales.term.customer') + t('sales.term.bpName').slice(-1)" min-width="160" show-overflow-tooltip sortable="custom" />
        <el-table-column prop="projectNoParent" :label="t('親案件')" width="110" sortable="custom" />
        <el-table-column prop="projectNoChild" :label="t('子案件')" width="110" sortable="custom" />
        <el-table-column prop="itemName1" :label="t('品名1')" min-width="160" show-overflow-tooltip />
        <el-table-column :label="t('初行数量')" width="100" align="right">
          <template #default="{ row }">{{ fmtNum(row.firstQuantity) }}</template>
        </el-table-column>
        <el-table-column :label="'初行' + t('sales.term.unitPrice')" width="110" align="right">
          <template #default="{ row }">{{ fmtMoney(row.firstUnitPrice) }}</template>
        </el-table-column>
        <el-table-column :label="'初行' + t('sales.term.amount')" width="130" align="right">
          <template #default="{ row }">{{ fmtMoney(row.firstAmount) }}</template>
        </el-table-column>
        <el-table-column prop="totalAmount" :label="t('sales.fsc.totalAmount')" width="130" align="right" sortable="custom">
          <template #default="{ row }">{{ fmtMoney(row.totalAmount) }}</template>
        </el-table-column>
        <el-table-column :label="t('sales.term.status')" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">
              {{ row.status }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('sales.list.action')" width="320" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="onView(row)">{{ t('sales.op.view') }}</el-button>
            <el-button link type="warning" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
            <el-button link type="success" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
            <el-button link type="info" @click="onIssue(row)">{{ t('sales.btn.issue') }}</el-button>
            <el-button link type="danger" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 手机端：卡片列表 -->
      <div v-else class="mobile-card-list" v-loading="loading">
        <el-empty v-if="!rows.length && !loading" :image-size="80" />
        <div
          v-for="row in rows"
          :key="row.qtnNo"
          class="mobile-card"
          @click="onView(row)"
        >
          <div class="mc-head">
            <div class="mc-no">{{ row.qtnNo }}</div>
            <el-tag :type="statusTagType(row.status) as any" size="small">{{ row.status }}</el-tag>
          </div>
          <div class="mc-title">{{ row.customerName || row.customerCd }}</div>
          <div class="mc-meta">
            <span>{{ fmtDate(row.qtnIssueDate) }}</span>
            <span class="mc-price">合計: ¥{{ fmtMoney(row.totalAmount) }}</span>
          </div>
          <div v-if="row.itemName1" class="mc-meta">
            <span class="mc-truncate">品: {{ row.itemName1 }}</span>
          </div>
          <div class="mc-actions" @click.stop>
            <el-button link type="warning" size="small" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
            <el-button link type="success" size="small" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
            <el-button link type="info" size="small" @click="onIssue(row)">{{ t('sales.btn.issue') }}</el-button>
            <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
          </div>
        </div>
      </div>

      <div class="pagination">
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          :layout="isMobile ? 'prev, pager, next' : 'total, sizes, prev, pager, next'"
          :pager-count="isMobile ? 5 : 7"
          :small="isMobile"
          background
          @current-change="loadData"
          @size-change="loadData"
        />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, RefreshLeft, Plus } from '@element-plus/icons-vue'
import { quotationApi } from '@/api/quotation'
import { masterApi } from '@/api/master'
import type { QuotationListItem, QuotationQuery } from '@/types/quotation'
import type { MasterBase } from '@/types/estimateCalc'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()

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
  sortField: '',
  sortOrder: '',
})
const dateRange = ref<[string, string] | null>(null)
const statusSel = ref<string[]>([])

function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  loadData()
}

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

/* 手机卡片 */
.mobile-card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.mobile-card {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 10px;
  padding: 12px 14px;
  cursor: pointer;
  transition: box-shadow 0.15s ease;
}
.mobile-card:active {
  box-shadow: 0 2px 8px rgba(64,158,255,0.15);
}
.mc-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}
.mc-no {
  font-weight: 600;
  font-size: 15px;
  color: #303133;
}
.mc-title {
  font-size: 14px;
  color: #303133;
  margin-bottom: 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mc-meta {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  color: #909399;
  padding: 2px 0;
}
.mc-truncate {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mc-price {
  color: #f56c6c;
  font-weight: 600;
}
.mc-actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #ebeef5;
}

@media (max-width: 767px) {
  .quotation-list {
    padding: 12px;
  }
  .pagination {
    text-align: center;
  }
}
</style>
