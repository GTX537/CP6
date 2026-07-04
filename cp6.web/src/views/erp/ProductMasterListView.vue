<!--
  Web 製品マスタ一覧 —— ERP 迁移批次2（CpListPage）。
  照会一覧（onMounted 自動取得）+ サーバサイド列ソート（#19 sortable:'custom'）を dogfood。
  ステータス列＝kind:'tag'+map、WF/MC 転送＝col slot（アイコン、色 token 化）、更新日＝map（fmtDt）、
  操作（照会/編集/複製/削除）＝col slot、新規/CSV 出力＝toolbar slot（#16 action button）、
  ステータス複数チェック＝toolbar slot（#15 の checkbox-group 拡張、page-level ref を fetch closure が読む）。
  CSV 出力は CpFilterBar 内包の filters を親が読めない（模板缺口）ため、fetch closure で最後の filters/sort を stash して再利用。
  子タブ保存/削除 → postMessage → listRef.reload()。行ダブルクリック→照会は操作列「照会」ボタンに集約（row-click 未透過 #16）。
  ページ標題キー無し（不臆造）のため CpPageShell 非適用。モバイルは設計システム標準（el-table 横スクロール）。
-->
<template>
  <CpListPage
    ref="listRef"
    :columns="columns"
    :fetch="fetchList"
    :search-fields="searchFields"
    :filter-labels="filterLabels"
    @reset="statusSel = []"
  >
    <template #toolbar>
      <el-checkbox-group v-model="statusSel" @change="listRef?.reload()">
        <el-checkbox :value="0">{{ t('未承認') }}</el-checkbox>
        <el-checkbox :value="1">{{ t('承認待') }}</el-checkbox>
        <el-checkbox :value="9">{{ t('承認済/転送済') }}</el-checkbox>
      </el-checkbox-group>
      <div class="tb-spacer" />
      <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
      <el-button :icon="Download" :loading="exporting" @click="onExportCsv">{{ t('sales.btn.exportCsv') }}</el-button>
    </template>

    <template #col-status="{ row }">
      <CpTag :tone="statusTone(row.status)">{{ statusLabel(row.status) }}</CpTag>
    </template>
    <template #col-wf="{ row }">
      <el-icon v-if="row.wfApprovalFlg" class="flg-on"><Check /></el-icon>
      <span v-else>-</span>
    </template>
    <template #col-mc="{ row }">
      <el-icon v-if="row.mcTransferFlg" class="flg-link"><Link /></el-icon>
      <span v-else>-</span>
    </template>
    <template #col-_action="{ row }">
      <el-button link type="primary" @click="onView(row)">{{ t('sales.op.view') }}</el-button>
      <el-button link type="warning" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
      <el-button link type="success" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
      <el-button link type="danger" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
    </template>
  </CpListPage>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Check, Link, Download } from '@element-plus/icons-vue'
import CpListPage, { type ListColumn, type ListFetch, type SortOrder } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { productApi } from '@/api/erp/product'
import type { ProductListItemDto, ProductQuery } from '@/types/erp/productMaster'

const { t } = useI18n()
const listRef = ref<InstanceType<typeof CpListPage>>()
const exporting = ref(false)
const statusSel = ref<number[]>([])

// —— CSV 出力用：fetch closure が最後の filters/sort を stash（CpListPage 内包 filters を親が読めない代償）——
const lastFilters = ref<Record<string, unknown>>({})
const lastSort = ref<{ sortField?: string; sortOrder?: SortOrder }>({})

function fmtDt(v?: string) { return v ? v.replace('T', ' ').slice(0, 16) : '' }
function statusLabel(s: number): string {
  return s === 9 ? t('sales.status.approved') : s === 1 ? t('sales.status.pendingApproval') : t('sales.status.notRegistered')
}
function statusTone(s: number): Tone {
  return s === 9 ? 'ok' : s === 1 ? 'warn' : 'info'
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'productCd', label: t('sales.term.productCd'), width: 160, fixed: 'left', sortable: 'custom' },
  { prop: 'setProductCd', label: t('セット製品CD'), width: 140, sortable: 'custom' },
  { prop: 'setProductName', label: t('セット品名'), minWidth: 180, overflowTooltip: true, sortable: 'custom' },
  { prop: 'customerCd', label: t('sales.term.customer') + ' CD', width: 100, sortable: 'custom' },
  { prop: 'customerName', label: t('sales.term.customer') + t('sales.term.bpName').slice(-1), minWidth: 160, overflowTooltip: true },
  { prop: 'customerItemName1', label: t('顧客品名1'), minWidth: 160, overflowTooltip: true, sortable: 'custom' },
  { prop: 'customerItemName2', label: t('顧客品名2'), minWidth: 140, overflowTooltip: true, sortable: 'custom' },
  { prop: 'projectNoParent', label: t('親案件'), width: 100, sortable: 'custom' },
  { prop: 'projectNoChild', label: t('子案件'), width: 100, sortable: 'custom' },
  { prop: 'quotationNo', label: t('sales.term.qtnNo'), width: 120, sortable: 'custom' },
  { prop: 'estimateCalcNo', label: t('sales.term.calcNo'), width: 130, sortable: 'custom' },
  { prop: 'status', label: t('sales.term.status'), width: 100, sortable: 'custom' },
  { prop: 'wf', label: 'WF', width: 60, align: 'center' },
  { prop: 'mc', label: t('MC転送'), width: 80, align: 'center' },
  { prop: 'modifyDate', label: t('更新日'), width: 160, sortable: 'custom', map: (v) => ({ label: fmtDt(v as string) }) },
  { prop: '_action', label: t('sales.list.action'), width: 280, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('sales.btn.search'),
  reset: t('sales.btn.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'productCdFrom', label: t('sales.term.productCd') + ' ' + t('sales.search.from'), type: 'text' },
  { key: 'productCdTo', label: t('sales.term.productCd') + ' ' + t('sales.search.to'), type: 'text' },
  { key: 'setProductCd', label: t('セット製品CD'), type: 'text' },
  { key: 'customerCd', label: t('sales.term.customer') + ' CD', type: 'text' },
  { key: 'projectNoParent', label: t('親案件'), type: 'text' },
  { key: 'projectNoChild', label: t('子案件'), type: 'text' },
  { key: 'quotationNo', label: t('sales.term.qtnNo'), type: 'text' },
  { key: 'estimateCalcNo', label: t('sales.term.calcNo'), type: 'text' },
  { key: 'customerItemName1', label: t('顧客品名1'), type: 'text' },
  { key: 'customerItemName2', label: t('顧客品名2'), type: 'text' },
  { key: 'designProposalNo', label: t('設計提案NO'), type: 'text' },
  { key: 'modifyDateRange', label: t('更新日'), type: 'daterange', valueFormat: 'YYYY-MM-DD' },
])

// —— filters + status + sort → API query（空値は除去；#17 undefined/'' 同一視）——
function buildQuery(filters: Record<string, unknown>, sortField?: string, sortOrder?: SortOrder): ProductQuery {
  const range = (filters.modifyDateRange as [string, string] | undefined) || null
  const q: Record<string, unknown> = {
    productCdFrom: filters.productCdFrom,
    productCdTo: filters.productCdTo,
    setProductCd: filters.setProductCd,
    customerCd: filters.customerCd,
    projectNoParent: filters.projectNoParent,
    projectNoChild: filters.projectNoChild,
    quotationNo: filters.quotationNo,
    estimateCalcNo: filters.estimateCalcNo,
    customerItemName1: filters.customerItemName1,
    customerItemName2: filters.customerItemName2,
    designProposalNo: filters.designProposalNo,
    modifyDateFrom: range ? range[0] : undefined,
    modifyDateTo: range ? range[1] : undefined,
    statuses: statusSel.value.length ? [...statusSel.value] : undefined,
    sortField: sortField || undefined,
    sortOrder: sortOrder || undefined,
  }
  Object.keys(q).forEach((k) => {
    const v = q[k]
    if (v === '' || v === null || v === undefined) delete q[k]
  })
  return q as unknown as ProductQuery
}

const fetchList: ListFetch = async ({ page, size, filters, sortField, sortOrder }) => {
  lastFilters.value = filters
  lastSort.value = { sortField, sortOrder }
  const q = buildQuery(filters, sortField, sortOrder)
  const res = await productApi.getList({ ...q, page, pageSize: size } as ProductQuery)
  if (res.code === 0) return { rows: res.data.rows ?? [], total: res.data.total ?? 0 }
  return { rows: [], total: 0 }
}

function openInWindow(op: 'new' | 'view' | 'edit' | 'copy' | 'delete', cd?: string) {
  const qs = new URLSearchParams({ op })
  if (cd) qs.set('cd', cd)
  const w = window.open(`${window.location.origin}/product/window?${qs.toString()}`, '_blank')
  if (!w) ElMessage.warning(t('新しいタブがブロックされました。このサイトに対してポップアップを許可してください'))
}
function onNew() { openInWindow('new') }
function onView(row: ProductListItemDto) { openInWindow('view', row.productCd) }
function onEdit(row: ProductListItemDto) { openInWindow('edit', row.productCd) }
function onCopy(row: ProductListItemDto) { openInWindow('copy', row.productCd) }

async function onExportCsv() {
  exporting.value = true
  try {
    const q = buildQuery(lastFilters.value, lastSort.value.sortField, lastSort.value.sortOrder)
    const blob = await productApi.exportCsv(q)
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
    /* interceptor toast */
  } finally {
    exporting.value = false
  }
}

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
      listRef.value?.reload()
    }
  } catch { /* interceptor toast */ }
}

function handleMessage(e: MessageEvent) {
  if (e.origin !== window.location.origin) return
  const data = e.data
  if (data?.source === 'cp6-product' && (data.type === 'saved' || data.type === 'deleted')) {
    listRef.value?.reload()
  }
}

onMounted(() => { window.addEventListener('message', handleMessage) })
onBeforeUnmount(() => { window.removeEventListener('message', handleMessage) })
</script>

<style scoped>
/* toolbar: ステータスチェック群と右寄せアクションの分離（純レイアウト） */
.tb-spacer { flex: 1; }
.flg-on { color: var(--cp-ok); }
.flg-link { color: var(--cp-info); }
</style>
