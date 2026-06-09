<template>
  <div class="credit-note-list">
    <div class="page-header">
      <h2>{{ t('erp.creditNote.title') }}</h2>
    </div>

    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('erp.creditNote.search.customer')">
          <el-input v-model="query.customerCd" clearable style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('erp.creditNote.search.webOrderNo')">
          <el-input v-model="query.webOrderNo" clearable style="width: 170px" />
        </el-form-item>
        <el-form-item :label="t('erp.creditNote.search.type')">
          <el-select v-model="query.type" clearable style="width: 150px">
            <el-option :label="t('erp.creditNote.type.ALL')" value="" />
            <el-option :label="t('erp.creditNote.type.REFUND')" value="REFUND" />
            <el-option :label="t('erp.creditNote.type.EXCHANGE')" value="EXCHANGE" />
            <el-option :label="t('erp.creditNote.type.SCRAP')" value="SCRAP" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('erp.creditNote.search.dateFrom')">
          <el-date-picker
            v-model="query.dateFrom"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 150px"
            clearable
          />
        </el-form-item>
        <el-form-item :label="t('erp.creditNote.search.dateTo')">
          <el-date-picker
            v-model="query.dateTo"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 150px"
            clearable
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">
            {{ t('erp.creditNote.btn.search') }}
          </el-button>
          <el-button @click="resetQuery">{{ t('erp.creditNote.btn.reset') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-tag size="small">{{ t('erp.creditNote.total', { n: total }) }}</el-tag>
      </div>

      <el-table v-if="!isMobile" :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="issueDate" :label="t('erp.creditNote.col.issueDate')" width="120">
          <template #default="{ row }">{{ formatDate(row.issueDate) }}</template>
        </el-table-column>
        <el-table-column prop="creditNoteNo" :label="t('erp.creditNote.col.no')" width="150" show-overflow-tooltip />
        <el-table-column prop="webOrderNo" :label="t('erp.creditNote.col.webOrderNo')" width="150">
          <template #default="{ row }">
            <el-button v-if="row.webOrderNo" link type="primary" size="small" @click="goOrder(row.webOrderNo)">
              {{ row.webOrderNo }}
            </el-button>
          </template>
        </el-table-column>
        <el-table-column prop="rmaNo" :label="t('erp.creditNote.col.rmaNo')" width="140" show-overflow-tooltip />
        <el-table-column :label="t('erp.creditNote.col.customer')" min-width="170" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.customerName || row.customerCd }}
          </template>
        </el-table-column>
        <el-table-column prop="type" :label="t('erp.creditNote.col.type')" width="120">
          <template #default="{ row }">
            <el-tag :type="typeTag(row.type)" size="small">
              {{ t(`erp.creditNote.type.${row.type}`) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="productCd" :label="t('erp.creditNote.col.product')" width="130" show-overflow-tooltip />
        <el-table-column prop="qty" :label="t('erp.creditNote.col.qty')" width="110" align="right">
          <template #default="{ row }">{{ formatQty(row.qty) }}</template>
        </el-table-column>
        <el-table-column prop="amount" :label="t('erp.creditNote.col.amount')" width="120" align="right">
          <template #default="{ row }">{{ formatAmount(row.amount) }}</template>
        </el-table-column>
        <el-table-column prop="reason" :label="t('erp.creditNote.col.reason')" min-width="220">
          <template #default="{ row }">
            <el-tooltip v-if="row.reason && row.reason.length > 50" :content="row.reason" placement="top">
              <span>{{ truncateReason(row.reason) }}</span>
            </el-tooltip>
            <span v-else>{{ row.reason }}</span>
          </template>
        </el-table-column>
      </el-table>

      <div v-else class="credit-note-card-list" v-loading="loading">
        <el-empty v-if="!rows.length && !loading" :image-size="80" />
        <div v-for="row in rows" :key="row.creditNoteNo" class="credit-note-card">
          <div class="card-head">
            <div>
              <div class="card-no">{{ row.creditNoteNo }}</div>
              <div class="card-date">{{ formatDate(row.issueDate) }}</div>
            </div>
            <el-tag :type="typeTag(row.type)" size="small">
              {{ t(`erp.creditNote.type.${row.type}`) }}
            </el-tag>
          </div>
          <div class="card-row">
            <span>{{ t('erp.creditNote.col.webOrderNo') }}</span>
            <el-button v-if="row.webOrderNo" link type="primary" size="small" @click="goOrder(row.webOrderNo)">
              {{ row.webOrderNo }}
            </el-button>
          </div>
          <div class="card-row">
            <span>{{ t('erp.creditNote.col.rmaNo') }}</span>
            <strong>{{ row.rmaNo || '-' }}</strong>
          </div>
          <div class="card-row">
            <span>{{ t('erp.creditNote.col.customer') }}</span>
            <strong>{{ row.customerName || row.customerCd }}</strong>
          </div>
          <div class="card-row">
            <span>{{ t('erp.creditNote.col.amount') }}</span>
            <strong>{{ formatAmount(row.amount) }}</strong>
          </div>
          <div v-if="row.reason" class="card-reason">{{ truncateReason(row.reason) }}</div>
        </div>
      </div>

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        :layout="isMobile ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper'"
        :pager-count="isMobile ? 5 : 7"
        :small="isMobile"
        background
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="reload"
        @size-change="reload"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { creditNoteApi } from '@/api/erp/creditNote'
import { useBreakpoint } from '@/composables/useBreakpoint'
import type { CreditNoteListItem, CreditNoteQuery, CreditNoteType } from '@/types/erp/creditNote'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

const query = reactive<Required<Pick<CreditNoteQuery, 'page' | 'pageSize'>> & {
  customerCd?: string
  webOrderNo?: string
  type: CreditNoteType | ''
  dateFrom?: string
  dateTo?: string
}>({
  type: '',
  page: 1,
  pageSize: 50,
})

const rows = ref<CreditNoteListItem[]>([])
const total = ref(0)
const loading = ref(false)

function normalizePaged(res: any) {
  const data = res?.data || {}
  return {
    items: (data.items || data.Items || []) as CreditNoteListItem[],
    total: Number(data.total ?? data.Total ?? 0),
  }
}

function typeTag(type: CreditNoteType): 'warning' | 'info' | 'danger' {
  if (type === 'REFUND') return 'warning'
  if (type === 'EXCHANGE') return 'info'
  return 'danger'
}

function formatDate(value?: string): string {
  return value ? value.slice(0, 10) : ''
}

function formatQty(value?: number | null): string {
  if (value == null) return ''
  return Number(value).toLocaleString('ja-JP', { maximumFractionDigits: 4 })
}

function formatAmount(value?: number | null): string {
  if (value == null) return ''
  return Number(value).toLocaleString('ja-JP', { maximumFractionDigits: 2 })
}

function truncateReason(value?: string): string {
  if (!value) return ''
  return value.length > 50 ? `${value.slice(0, 50)}...` : value
}

async function reload() {
  loading.value = true
  try {
    const res = await creditNoteApi.search({
      customerCd: query.customerCd,
      webOrderNo: query.webOrderNo,
      type: query.type,
      dateFrom: query.dateFrom,
      dateTo: query.dateTo,
      page: query.page,
      pageSize: query.pageSize,
    })
    const paged = normalizePaged(res)
    rows.value = paged.items
    total.value = paged.total
  } finally {
    loading.value = false
  }
}

function search() {
  query.page = 1
  reload()
}

function resetQuery() {
  query.customerCd = undefined
  query.webOrderNo = undefined
  query.type = ''
  query.dateFrom = undefined
  query.dateTo = undefined
  query.page = 1
  reload()
}

function goOrder(webOrderNo: string) {
  router.push({ path: '/order', query: { webOrderNo } })
}

onMounted(reload)
</script>

<style scoped>
.credit-note-list {
  padding: 16px;
}

.page-header {
  margin-bottom: 12px;
}

.page-header h2 {
  margin: 0;
  color: #303133;
  font-size: 20px;
  font-weight: 650;
  line-height: 1.3;
}

.search-card {
  margin-bottom: 12px;
}

.table-toolbar {
  margin-bottom: 8px;
}

.credit-note-card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.credit-note-card {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  padding: 12px;
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 8px;
  border-bottom: 1px dashed #ebeef5;
  margin-bottom: 8px;
}

.card-no {
  color: #303133;
  font-size: 15px;
  font-weight: 650;
}

.card-date {
  color: #909399;
  font-size: 12px;
  margin-top: 2px;
}

.card-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  min-height: 26px;
}

.card-row span {
  color: #909399;
}

.card-row strong {
  color: #303133;
  font-weight: 500;
  text-align: right;
  word-break: break-all;
}

.card-reason {
  margin-top: 8px;
  color: #606266;
  font-size: 13px;
  line-height: 1.5;
  word-break: break-word;
}

@media (max-width: 767px) {
  .credit-note-list {
    padding: 12px;
  }

  .search-card :deep(.el-card__body) {
    padding: 12px;
  }

  .search-card :deep(.el-form-item) {
    display: flex;
    flex-direction: column;
    align-items: stretch;
    margin-right: 0;
    margin-bottom: 12px;
    width: 100%;
  }

  .search-card :deep(.el-form-item__label) {
    justify-content: flex-start;
    height: auto;
    line-height: 20px;
    margin-bottom: 4px;
  }

  .search-card :deep(.el-form-item__content) {
    width: 100%;
  }

  .search-card :deep(.el-input),
  .search-card :deep(.el-select),
  .search-card :deep(.el-date-editor.el-input) {
    width: 100% !important;
  }
}
</style>
