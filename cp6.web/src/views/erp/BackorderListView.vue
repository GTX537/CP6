<template>
  <div class="backorder-list">
    <div class="page-header">
      <h2>{{ t('erp.backorder.title') }}</h2>
    </div>

    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small" @submit.prevent>
        <el-form-item :label="t('erp.backorder.search.customer')">
          <el-input v-model="query.customerCd" clearable class="filter-input" @keyup.enter="search" />
        </el-form-item>
        <el-form-item :label="t('erp.backorder.search.dateFrom')">
          <el-date-picker
            v-model="query.dateFrom"
            type="date"
            value-format="YYYY-MM-DD"
            clearable
            class="filter-date"
          />
        </el-form-item>
        <el-form-item :label="t('erp.backorder.search.dateTo')">
          <el-date-picker
            v-model="query.dateTo"
            type="date"
            value-format="YYYY-MM-DD"
            clearable
            class="filter-date"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="search">
            {{ t('erp.backorder.btn.search') }}
          </el-button>
          <el-button :icon="RefreshLeft" :disabled="loading" @click="resetQuery">
            {{ t('erp.backorder.btn.reset') }}
          </el-button>
          <el-button :icon="Refresh" circle :loading="loading" @click="reload" />
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <el-tag size="small">{{ t('erp.backorder.total', { n: rows.length }) }}</el-tag>
      </div>

      <el-table v-if="!isMobile" :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="webOrderNo" :label="t('erp.backorder.col.webOrderNo')" width="155">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goOrder(row.webOrderNo)">
              {{ row.webOrderNo }}
            </el-button>
          </template>
        </el-table-column>
        <el-table-column :label="t('erp.backorder.col.customer')" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.customerName || row.customerCd }}
          </template>
        </el-table-column>
        <el-table-column prop="detailNo" :label="t('erp.backorder.col.detailNo')" width="88" align="right" />
        <el-table-column prop="productCd" :label="t('erp.backorder.col.product')" width="140" show-overflow-tooltip />
        <el-table-column prop="orderedQty" :label="t('erp.backorder.col.orderedQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.orderedQty) }}</template>
        </el-table-column>
        <el-table-column prop="shippedQty" :label="t('erp.backorder.col.shippedQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.shippedQty) }}</template>
        </el-table-column>
        <el-table-column prop="backorderQty" :label="t('erp.backorder.col.backorderQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.backorderQty) }}</template>
        </el-table-column>
        <el-table-column prop="remainingQty" :label="t('erp.backorder.col.remainingQty')" width="130" align="right">
          <template #default="{ row }">
            <strong class="remaining">{{ formatQty(row.remainingQty) }}</strong>
          </template>
        </el-table-column>
        <el-table-column prop="lastShipDate" :label="t('erp.backorder.col.lastShipDate')" width="128">
          <template #default="{ row }">{{ formatDate(row.lastShipDate) || '-' }}</template>
        </el-table-column>
        <el-table-column :label="t('erp.backorder.col.actions')" width="190" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button type="warning" link size="small" :icon="Check" @click="openAction(row, 'close')">
                {{ t('erp.backorder.btn.close') }}
              </el-button>
              <el-button type="primary" link size="small" :icon="CopyDocument" @click="openAction(row, 'split')">
                {{ t('erp.backorder.btn.split') }}
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <div v-else class="bo-card-list" v-loading="loading">
        <el-empty v-if="!rows.length && !loading" :description="t('erp.backorder.empty')" :image-size="80" />
        <div v-for="row in rows" :key="`${row.webOrderNo}-${row.detailNo}`" class="bo-card">
          <div class="card-head">
            <div>
              <el-button link type="primary" size="small" class="card-no" @click="goOrder(row.webOrderNo)">
                {{ row.webOrderNo }}
              </el-button>
              <div class="card-sub">{{ row.customerName || row.customerCd }}</div>
            </div>
            <el-tag type="warning" size="small">{{ formatQty(row.remainingQty) }}</el-tag>
          </div>
          <div class="card-grid">
            <span>{{ t('erp.backorder.col.detailNo') }}</span>
            <strong>{{ row.detailNo }}</strong>
            <span>{{ t('erp.backorder.col.product') }}</span>
            <strong>{{ row.productCd }}</strong>
            <span>{{ t('erp.backorder.col.shippedQty') }}</span>
            <strong>{{ formatQty(row.shippedQty) }} / {{ formatQty(row.orderedQty) }}</strong>
            <span>{{ t('erp.backorder.col.lastShipDate') }}</span>
            <strong>{{ formatDate(row.lastShipDate) || '-' }}</strong>
          </div>
          <div class="card-actions">
            <el-button type="warning" size="small" :icon="Check" @click="openAction(row, 'close')">
              {{ t('erp.backorder.btn.close') }}
            </el-button>
            <el-button type="primary" size="small" :icon="CopyDocument" @click="openAction(row, 'split')">
              {{ t('erp.backorder.btn.split') }}
            </el-button>
          </div>
        </div>
      </div>
    </el-card>

    <el-dialog
      v-model="dialog.visible"
      :title="dialog.action === 'close' ? t('erp.backorder.dialog.closeTitle') : t('erp.backorder.dialog.splitTitle')"
      width="460px"
      class="backorder-dialog"
    >
      <div v-if="dialog.row" class="dialog-target">
        <span>{{ dialog.row.webOrderNo }} / {{ dialog.row.detailNo }}</span>
        <strong>{{ formatQty(dialog.row.remainingQty) }}</strong>
      </div>
      <el-form label-position="top" size="small">
        <el-form-item :label="t('erp.backorder.dialog.reason')">
          <el-input
            v-model="dialog.reason"
            type="textarea"
            :rows="4"
            maxlength="100"
            show-word-limit
            autofocus
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog.visible = false">{{ t('erp.backorder.btn.cancel') }}</el-button>
        <el-button type="primary" :loading="actionLoading" @click="submitAction">
          {{ t('erp.backorder.btn.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Check, CopyDocument, Refresh, RefreshLeft, Search } from '@element-plus/icons-vue'
import { backorderApi } from '@/api/erp/backorder'
import { useBreakpoint } from '@/composables/useBreakpoint'
import type { BackorderQueueItem, BackorderQueueQuery } from '@/types/backorder'

type BackorderAction = 'close' | 'split'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

const query = reactive<BackorderQueueQuery>({})
const rows = ref<BackorderQueueItem[]>([])
const loading = ref(false)
const actionLoading = ref(false)
const dialog = reactive<{
  visible: boolean
  action: BackorderAction
  row: BackorderQueueItem | null
  reason: string
}>({
  visible: false,
  action: 'close',
  row: null,
  reason: '',
})

async function reload() {
  loading.value = true
  try {
    const res = await backorderApi.queue({
      customerCd: query.customerCd,
      dateFrom: query.dateFrom,
      dateTo: query.dateTo,
    })
    rows.value = res.data || []
  } finally {
    loading.value = false
  }
}

function search() {
  reload()
}

function resetQuery() {
  query.customerCd = undefined
  query.dateFrom = undefined
  query.dateTo = undefined
  reload()
}

function openAction(row: BackorderQueueItem, action: BackorderAction) {
  dialog.row = row
  dialog.action = action
  dialog.reason = ''
  dialog.visible = true
}

async function submitAction() {
  const row = dialog.row
  const reason = dialog.reason.trim()
  if (!row) return
  if (!reason) {
    ElMessage.warning(t('erp.backorder.msg.reasonRequired'))
    return
  }

  actionLoading.value = true
  try {
    const request = { reason }
    const res = dialog.action === 'close'
      ? await backorderApi.closeRemaining(row.webOrderNo, row.detailNo, request)
      : await backorderApi.splitToNewOrder(row.webOrderNo, row.detailNo, request)

    dialog.visible = false
    if (dialog.action === 'split' && res.data?.newWebOrderNo) {
      ElMessage.success(t('erp.backorder.msg.split', { no: res.data.newWebOrderNo }))
    } else {
      ElMessage.success(t('erp.backorder.msg.closed'))
    }
    await reload()
  } finally {
    actionLoading.value = false
  }
}

function goOrder(webOrderNo: string) {
  router.push({ path: '/order', query: { webOrderNo } })
}

function formatQty(value?: number | null): string {
  if (value == null) return ''
  return Number(value).toLocaleString('ja-JP', { maximumFractionDigits: 4 })
}

function formatDate(value?: string): string {
  return value ? value.slice(0, 10) : ''
}

onMounted(reload)
</script>

<style scoped>
.backorder-list {
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

.filter-input {
  width: 160px;
}

.filter-date {
  width: 150px;
}

.table-toolbar {
  margin-bottom: 8px;
}

.remaining {
  color: #b88230;
  font-weight: 650;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.bo-card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.bo-card {
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
  padding: 0;
  font-size: 15px;
  font-weight: 650;
}

.card-sub {
  color: #909399;
  font-size: 12px;
  margin-top: 2px;
}

.card-grid {
  display: grid;
  grid-template-columns: minmax(88px, auto) minmax(0, 1fr);
  gap: 7px 12px;
  font-size: 13px;
}

.card-grid span {
  color: #909399;
}

.card-grid strong {
  color: #303133;
  font-weight: 500;
  text-align: right;
  word-break: break-all;
}

.card-actions {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-top: 12px;
}

.dialog-target {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 8px 10px;
  margin-bottom: 12px;
  background: #f5f7fa;
  border-radius: 6px;
  color: #606266;
}

.dialog-target strong {
  color: #b88230;
}

@media (max-width: 767px) {
  .backorder-list {
    padding: 12px;
  }

  .search-card :deep(.el-card__body),
  .table-card :deep(.el-card__body) {
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

  .search-card :deep(.el-form-item__content),
  .filter-input,
  .filter-date {
    width: 100% !important;
  }

  .backorder-dialog {
    width: calc(100vw - 24px) !important;
  }
}
</style>
