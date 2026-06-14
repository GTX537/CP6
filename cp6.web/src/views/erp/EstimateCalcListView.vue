<template>
  <div class="estimate-list">
    <!-- 查询区 -->
    <el-card shadow="never" class="search-card">
      <el-form inline :model="query" @submit.prevent="onSearch">
        <el-form-item :label="t('sales.term.calcNo')">
          <el-input v-model="query.qtnCalcNo" :placeholder="t('例: 00000001')" clearable style="width: 180px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer')">
          <el-input v-model="query.customerCd" :placeholder="t('sales.term.customer') + ' CD'" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.base')">
          <el-select v-model="query.baseCd" :placeholder="t('全部')" clearable style="width: 160px">
            <el-option v-for="b in bases" :key="b.baseCd" :value="b.baseCd" :label="`${b.baseCd} ${b.baseName}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('sales.qtn.qtnDate')">
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
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="onSearch">{{ t('sales.btn.search') }}</el-button>
          <el-button :icon="RefreshLeft" @click="onReset">{{ t('sales.btn.clear') }}</el-button>
          <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 列表 -->
    <el-card shadow="never" class="table-card">
      <!-- 桌面端：完整表格 -->
      <el-table
        v-if="!isMobile"
        :data="rows"
        v-loading="loading"
        stripe
        border
        style="width: 100%"
        @row-dblclick="(row: EstimateCalcListItem) => onView(row)"
        @sort-change="onSortChange"
      >
        <el-table-column prop="qtnCalcNo" :label="t('sales.term.calcNo')" width="140" sortable="custom" />
        <el-table-column prop="qtnDate" :label="t('sales.qtn.qtnDate')" width="110" sortable="custom">
          <template #default="{ row }">{{ fmtDate(row.qtnDate) }}</template>
        </el-table-column>
        <el-table-column prop="qtnBaseCd" :label="t('sales.term.base')" width="80" sortable="custom" />
        <el-table-column prop="staffCd" :label="t('sales.term.staff')" width="90" sortable="custom" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="120" sortable="custom" />
        <el-table-column prop="customerProductName1" :label="t('顧客品名')" min-width="200" show-overflow-tooltip sortable="custom" />
        <el-table-column prop="orderQty" :label="t('sales.term.qty')" width="100" align="right" sortable="custom">
          <template #default="{ row }">{{ fmtNum(row.orderQty) }}</template>
        </el-table-column>
        <el-table-column prop="estimateUnitPrice" :label="t('sales.term.unitPrice')" width="120" align="right" sortable="custom">
          <template #default="{ row }">{{ fmtMoney(row.estimateUnitPrice) }}</template>
        </el-table-column>
        <el-table-column prop="qtnDiv" :label="t('見積区分')" width="100" sortable="custom" />
        <el-table-column prop="modifyDate" :label="t('最終更新')" width="160" sortable="custom">
          <template #default="{ row }">{{ fmtDateTime(row.modifyDate || row.createDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('sales.list.action')" width="260" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="onView(row)">{{ t('sales.op.view') }}</el-button>
            <el-button link type="warning" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
            <el-button link type="success" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
            <el-button link type="danger" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 手机端：卡片列表 -->
      <div v-else class="mobile-card-list" v-loading="loading">
        <el-empty v-if="!rows.length && !loading" :image-size="80" />
        <div
          v-for="row in rows"
          :key="row.qtnCalcNo"
          class="mobile-card"
          @click="onView(row)"
        >
          <div class="mc-head">
            <div class="mc-no">{{ row.qtnCalcNo }}</div>
            <el-tag size="small" type="info">{{ fmtDate(row.qtnDate) }}</el-tag>
          </div>
          <div class="mc-title">{{ row.customerProductName1 || '—' }}</div>
          <div class="mc-meta">
            <span>客: {{ row.customerCd || '—' }}</span>
            <span>拠点: {{ row.qtnBaseCd || '—' }}</span>
          </div>
          <div class="mc-meta">
            <span>数量: {{ fmtNum(row.orderQty) }}</span>
            <span class="mc-price">単価: ¥{{ fmtMoney(row.estimateUnitPrice) }}</span>
          </div>
          <div class="mc-actions" @click.stop>
            <el-button link type="warning" size="small" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
            <el-button link type="success" size="small" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
            <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
          </div>
        </div>
      </div>

      <div class="pagination">
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50]"
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
import { ref, reactive, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, RefreshLeft, Plus } from '@element-plus/icons-vue'
import { estimateCalcApi } from '@/api/erp/estimateCalc'
import { masterApi } from '@/api/erp/master'
import type { EstimateCalcListItem, MasterBase, EstimateCalcQuery } from '@/types/erp/estimateCalc'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { formatQty, formatNumber } from '@/utils/format'

const { isMobile } = useBreakpoint()

const loading = ref(false)
const rows = ref<EstimateCalcListItem[]>([])
const total = ref(0)
const bases = ref<MasterBase[]>([])

const query = reactive<EstimateCalcQuery>({
  page: 1,
  pageSize: 10,
  qtnCalcNo: '',
  customerCd: '',
  baseCd: '',
  dateFrom: '',
  dateTo: '',
  sortField: '',
  sortOrder: '',
})
const dateRange = ref<[string, string] | null>(null)

// 表头点击排序：Element Plus 传 { prop, order: 'ascending'|'descending'|null }
function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  loadData()
}

const fmtDate = (v?: string) => (v ? v.slice(0, 10) : '')
const fmtDateTime = (v?: string) => (v ? v.replace('T', ' ').slice(0, 19) : '')
const fmtNum = (v?: number) => (v == null ? '' : formatQty(v))
const fmtMoney = (v?: number) => (v == null ? '' : formatNumber(v, 'decimal'))

async function loadData() {
  try {
    loading.value = true
    if (dateRange.value) {
      query.dateFrom = dateRange.value[0]
      query.dateTo = dateRange.value[1]
    } else {
      query.dateFrom = ''
      query.dateTo = ''
    }
    const res = await estimateCalcApi.getList(query)
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
  query.qtnCalcNo = ''
  query.customerCd = ''
  query.baseCd = ''
  dateRange.value = null
  query.page = 1
  loadData()
}

/**
 * 以新页签打开見積計算書编辑页
 * 保存/删除后父页签自动刷新列表（通过 postMessage）
 * 注意：不传 features 字符串，浏览器会按"新标签页"处理而非弹窗
 */
function openInWindow(op: 'new' | 'view' | 'edit' | 'copy', no?: string) {
  const qs = new URLSearchParams({ op })
  if (no) qs.set('no', no)
  const url = `${window.location.origin}/estimate-calc/window?${qs.toString()}`

  const win = window.open(url, '_blank')
  if (!win) {
    ElMessage.warning('新页签被浏览器拦截，请允许本站点打开新页签后再试')
  }
}

function onView(row: EstimateCalcListItem) {
  openInWindow('view', row.qtnCalcNo)
}
function onEdit(row: EstimateCalcListItem) {
  openInWindow('edit', row.qtnCalcNo)
}
function onCopy(row: EstimateCalcListItem) {
  openInWindow('copy', row.qtnCalcNo)
}
async function onDelete(row: EstimateCalcListItem) {
  try {
    await ElMessageBox.confirm(`削除 ${row.qtnCalcNo} ? （論理削除、復旧不可）`, '確認', {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    const res = await estimateCalcApi.remove(row.qtnCalcNo)
    if (res.code === 0) {
      ElMessage.success('削除完了')
      loadData()
    }
  } catch {
    // interceptor shows error
  }
}

function onNew() {
  openInWindow('new')
}

// 监听子窗口保存/删除消息，自动刷新列表
function handleMessage(e: MessageEvent) {
  if (e.origin !== window.location.origin) return
  const data = e.data
  if (data?.source === 'cp6-estimate' && (data.type === 'saved' || data.type === 'deleted')) {
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
.estimate-list {
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
.mc-price {
  color: #f56c6c;
  font-weight: 600;
}
.mc-actions {
  display: flex;
  justify-content: flex-end;
  gap: 4px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #ebeef5;
}

@media (max-width: 767px) {
  .estimate-list {
    padding: 12px;
  }
  .search-card :deep(.el-card__body),
  .table-card :deep(.el-card__body) {
    padding: 12px;
  }
  .pagination {
    text-align: center;
  }
}
</style>
