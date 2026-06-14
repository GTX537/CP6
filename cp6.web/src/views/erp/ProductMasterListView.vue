<template>
  <div class="product-list">
    <!-- 検索エリア -->
    <el-card shadow="never" class="search-card">
      <el-form inline :model="query" @submit.prevent="onSearch">
        <el-form-item :label="t('sales.term.productCd')">
          <el-input v-model="query.productCdFrom" :placeholder="t('sales.search.from')" clearable style="width: 160px" />
          <span style="margin: 0 4px">~</span>
          <el-input v-model="query.productCdTo" :placeholder="t('sales.search.to')" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('セット製品CD')">
          <el-input v-model="query.setProductCd" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' CD'">
          <el-input v-model="query.customerCd" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('親案件')">
          <el-input v-model="query.projectNoParent" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item :label="t('子案件')">
          <el-input v-model="query.projectNoChild" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.qtnNo')">
          <el-input v-model="query.quotationNo" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.calcNo')">
          <el-input v-model="query.estimateCalcNo" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('顧客品名1')">
          <el-input v-model="query.customerItemName1" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item :label="t('顧客品名2')">
          <el-input v-model="query.customerItemName2" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('設計提案NO')">
          <el-input v-model="query.designProposalNo" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('更新日')">
          <el-date-picker
            v-model="modifyDateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="~"
            :start-placeholder="t('sales.search.from')"
            :end-placeholder="t('sales.search.to')"
            style="width: 260px"
          />
        </el-form-item>
        <el-form-item :label="t('sales.term.status')">
          <el-checkbox-group v-model="statusSel">
            <el-checkbox :value="0">{{ t('未承認') }}</el-checkbox>
            <el-checkbox :value="1">{{ t('承認待') }}</el-checkbox>
            <el-checkbox :value="9">{{ t('承認済/転送済') }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="onSearch">{{ t('sales.btn.search') }}</el-button>
          <el-button :icon="RefreshLeft" @click="onReset">{{ t('sales.btn.clear') }}</el-button>
          <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
          <el-button :icon="Download" :loading="exporting" @click="onExportCsv">{{ t('sales.btn.exportCsv') }}</el-button>
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
        @row-dblclick="(row: ProductListItemDto) => onView(row)"
        @sort-change="onSortChange"
      >
        <el-table-column prop="productCd" :label="t('sales.term.productCd')" width="160" fixed="left" sortable="custom" />
        <el-table-column prop="setProductCd" :label="t('セット製品CD')" width="140" sortable="custom" />
        <el-table-column prop="setProductName" :label="t('セット品名')" min-width="180" show-overflow-tooltip sortable="custom" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer') + ' CD'" width="100" sortable="custom" />
        <el-table-column prop="customerName" :label="t('sales.term.customer') + t('sales.term.bpName').slice(-1)" min-width="160" show-overflow-tooltip />
        <el-table-column prop="customerItemName1" :label="t('顧客品名1')" min-width="160" show-overflow-tooltip sortable="custom" />
        <el-table-column prop="customerItemName2" :label="t('顧客品名2')" min-width="140" show-overflow-tooltip sortable="custom" />
        <el-table-column prop="projectNoParent" :label="t('親案件')" width="100" sortable="custom" />
        <el-table-column prop="projectNoChild" :label="t('子案件')" width="100" sortable="custom" />
        <el-table-column prop="quotationNo" :label="t('sales.term.qtnNo')" width="120" sortable="custom" />
        <el-table-column prop="estimateCalcNo" :label="t('sales.term.calcNo')" width="130" sortable="custom" />
        <el-table-column prop="status" :label="t('sales.term.status')" width="100" sortable="custom">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="WF" width="60" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.wfApprovalFlg" color="#67c23a"><Check /></el-icon>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('MC転送')" width="80" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.mcTransferFlg" color="#409eff"><Link /></el-icon>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="modifyDate" :label="t('更新日')" width="160" sortable="custom">
          <template #default="{ row }">{{ fmtDt(row.modifyDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('sales.list.action')" width="280" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="onView(row)">{{ t('sales.op.view') }}</el-button>
            <el-button link type="warning" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
            <el-button link type="success" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
            <el-button link type="danger" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
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
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, RefreshLeft, Plus, Check, Link, Download } from '@element-plus/icons-vue'
import { productApi } from '@/api/erp/product'
import type { ProductListItemDto, ProductQuery } from '@/types/erp/productMaster'

const loading = ref(false)
const exporting = ref(false)
const rows = ref<ProductListItemDto[]>([])
const total = ref(0)

const query = reactive<ProductQuery>({
  page: 1,
  pageSize: 20,
  productCdFrom: '',
  productCdTo: '',
  customerCd: '',
  setProductCd: '',
  projectNoParent: '',
  projectNoChild: '',
  quotationNo: '',
  estimateCalcNo: '',
  customerItemName1: '',
  customerItemName2: '',
  designProposalNo: '',
  modifyDateFrom: '',
  modifyDateTo: '',
  statuses: [],
  sortField: '',
  sortOrder: '',
})
const statusSel = ref<number[]>([])
const modifyDateRange = ref<[string, string] | null>(null)
watch(statusSel, v => (query.statuses = v.length ? [...v] : undefined))

function fmtDt(v?: string) {
  return v ? v.replace('T', ' ').slice(0, 16) : ''
}
function statusLabel(s: number): string {
  return s === 9 ? t('sales.status.approved') : s === 1 ? t('sales.status.pendingApproval') : t('sales.status.notRegistered')
}
function statusTagType(s: number): 'info' | 'warning' | 'success' {
  return s === 9 ? 'success' : s === 1 ? 'warning' : 'info'
}

function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  loadData()
}

async function loadData() {
  try {
    loading.value = true
    if (modifyDateRange.value) {
      query.modifyDateFrom = modifyDateRange.value[0]
      query.modifyDateTo = modifyDateRange.value[1]
    } else {
      query.modifyDateFrom = ''
      query.modifyDateTo = ''
    }
    const payload = { ...query } as unknown as Record<string, unknown>
    Object.keys(payload).forEach(k => {
      const v = payload[k]
      if (v === '' || v === null) delete payload[k]
    })
    const res = await productApi.getList(payload as unknown as ProductQuery)
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
    productCdFrom: '',
    productCdTo: '',
    customerCd: '',
    setProductCd: '',
    projectNoParent: '',
    projectNoChild: '',
    quotationNo: '',
    estimateCalcNo: '',
    customerItemName1: '',
    customerItemName2: '',
    designProposalNo: '',
    modifyDateFrom: '',
    modifyDateTo: '',
    statuses: [],
  })
  statusSel.value = []
  modifyDateRange.value = null
  loadData()
}

function openInWindow(opc: 'new' | 'view' | 'edit' | 'copy' | 'delete', cd?: string) {
  const qs = new URLSearchParams({ op: opc })
  if (cd) qs.set('cd', cd)
  const url = `${window.location.origin}/product/window?${qs.toString()}`
  const w = window.open(url, '_blank')
  if (!w) {
    ElMessage.warning(t('新しいタブがブロックされました。このサイトに対してポップアップを許可してください'))
  }
}

async function onExportCsv() {
  try {
    exporting.value = true
    if (modifyDateRange.value) {
      query.modifyDateFrom = modifyDateRange.value[0]
      query.modifyDateTo = modifyDateRange.value[1]
    } else {
      query.modifyDateFrom = ''
      query.modifyDateTo = ''
    }
    const payload = { ...query } as unknown as Record<string, unknown>
    Object.keys(payload).forEach(k => {
      const v = payload[k]
      if (v === '' || v === null) delete payload[k]
    })
    // ページング無視で全件出力
    delete payload.page
    delete payload.pageSize
    const blob = await productApi.exportCsv(payload as unknown as ProductQuery)
    // ブラウザでダウンロード
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    const ts = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14)
    link.download = `products_${ts}.csv`
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(url)
    ElMessage.success(t('CSV を出力しました'))
  } catch {
    /* http interceptor toast */
  } finally {
    exporting.value = false
  }
}

function onView(row: ProductListItemDto) { openInWindow('view', row.productCd) }
function onEdit(row: ProductListItemDto) { openInWindow('edit', row.productCd) }
function onCopy(row: ProductListItemDto) { openInWindow('copy', row.productCd) }
function onNew() { openInWindow('new') }

async function onDelete(row: ProductListItemDto) {
  if (row.mcTransferFlg) {
    ElMessage.warning(t('mc転送済の製品は削除できません'))
    return
  }
  try {
    await ElMessageBox.confirm(
      t('{cd} を削除します（論理削除・復旧不可）。よろしいですか？', { cd: row.productCd }),
      t('削除確認'),
      { type: 'warning' },
    )
  } catch { return }
  try {
    const res = await productApi.remove(row.productCd)
    if (res.code === 0) {
      ElMessage.success(t('削除しました'))
      loadData()
    }
  } catch { /* interceptor toast */ }
}

// 子タブから保存/削除通知
function handleMessage(e: MessageEvent) {
  if (e.origin !== window.location.origin) return
  const data = e.data
  if (data?.source === 'cp6-product' && (data.type === 'saved' || data.type === 'deleted')) {
    loadData()
  }
}

onMounted(() => {
  window.addEventListener('message', handleMessage)
  loadData()
})

onBeforeUnmount(() => {
  window.removeEventListener('message', handleMessage)
})
</script>

<style scoped>
.product-list {
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
