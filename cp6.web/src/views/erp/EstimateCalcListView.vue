<!--
  見積計算書一覧 —— ERP 迁移批次2（CpListPage）。
  照会一覧（onMounted 自動取得・単一 fetch）+ サーバサイド列ソート（#19 sortable:'custom'）を dogfood。
  金額/数量＝kind:'num'+map（formatQty/formatNumber）、日付＝kind:'date'、更新日時＝map（fmtDateTime）、
  操作（照会/編集/複製/削除）＝col slot、新規＝toolbar slot（#16 action button）。
  拠点 select は masterApi.getBases を onMounted ロード。子タブ保存/削除 → postMessage → listRef.reload()。
  ページ標題キー無し（不臆造）のため CpPageShell は被せず CpListPage スタンドアロン（総数はページャに表示）。
  モバイル専用カード分岐は撤去し設計システム標準（el-table 横スクロール）に統一。
-->
<template>
  <CpListPage
    ref="listRef"
    :columns="columns"
    :fetch="fetchList"
    :search-fields="searchFields"
    :filter-labels="filterLabels"
  >
    <template #toolbar>
      <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
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
import { Plus } from '@element-plus/icons-vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { estimateCalcApi } from '@/api/erp/estimateCalc'
import { masterApi } from '@/api/erp/master'
import type { EstimateCalcListItem, MasterBase } from '@/types/erp/estimateCalc'
import { formatQty, formatNumber } from '@/utils/format'

const { t } = useI18n()
const listRef = ref<InstanceType<typeof CpListPage>>()
const bases = ref<MasterBase[]>([])

const fmtDateTime = (v?: string) => (v ? v.replace('T', ' ').slice(0, 19) : '')
const fmtNum = (v?: number) => (v == null ? '' : formatQty(v))
const fmtMoney = (v?: number) => (v == null ? '' : formatNumber(v, 'decimal'))

const columns = computed<ListColumn[]>(() => [
  { prop: 'qtnCalcNo', label: t('sales.term.calcNo'), width: 140, sortable: 'custom' },
  { prop: 'qtnDate', label: t('sales.qtn.qtnDate'), width: 110, kind: 'date', sortable: 'custom' },
  { prop: 'qtnBaseCd', label: t('sales.term.base'), width: 80, sortable: 'custom' },
  { prop: 'staffCd', label: t('sales.term.staff'), width: 90, sortable: 'custom' },
  { prop: 'customerCd', label: t('sales.term.customer'), width: 120, sortable: 'custom' },
  { prop: 'customerProductName1', label: t('顧客品名'), minWidth: 200, overflowTooltip: true, sortable: 'custom' },
  { prop: 'orderQty', label: t('sales.term.qty'), width: 100, kind: 'num', sortable: 'custom',
    map: (v) => ({ label: fmtNum(v as number) }) },
  { prop: 'estimateUnitPrice', label: t('sales.term.unitPrice'), width: 120, kind: 'num', sortable: 'custom',
    map: (v) => ({ label: fmtMoney(v as number) }) },
  { prop: 'qtnDiv', label: t('見積区分'), width: 100, sortable: 'custom' },
  { prop: 'modifyDate', label: t('最終更新'), width: 160, sortable: 'custom',
    map: (_v, row) => ({ label: fmtDateTime((row as EstimateCalcListItem).modifyDate || (row as EstimateCalcListItem).createDate) }) },
  { prop: '_action', label: t('sales.list.action'), width: 260, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('sales.btn.search'),
  reset: t('sales.btn.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'qtnCalcNo', label: t('sales.term.calcNo'), type: 'text', placeholder: t('例: 00000001') },
  { key: 'customerCd', label: t('sales.term.customer'), type: 'text', placeholder: t('sales.term.customer') + ' CD' },
  {
    key: 'baseCd', label: t('sales.term.base'), type: 'select',
    options: bases.value.map((b) => ({ label: `${b.baseCd} ${b.baseName}`, value: b.baseCd })),
  },
  { key: 'dateRange', label: t('sales.qtn.qtnDate'), type: 'daterange', valueFormat: 'YYYY-MM-DD' },
])

const fetchList: ListFetch = async ({ page, size, filters, sortField, sortOrder }) => {
  const f = filters as Record<string, unknown>
  const range = (f.dateRange as [string, string] | undefined) || null
  const res = await estimateCalcApi.getList({
    page,
    pageSize: size,
    qtnCalcNo: (f.qtnCalcNo as string) || '',
    customerCd: (f.customerCd as string) || '',
    baseCd: (f.baseCd as string) || '',
    dateFrom: range ? range[0] : '',
    dateTo: range ? range[1] : '',
    sortField: sortField || '',
    sortOrder: sortOrder || '',
  })
  if (res.code === 0) return { rows: res.data.rows ?? [], total: res.data.total ?? 0 }
  return { rows: [], total: 0 }
}

function openInWindow(op: 'new' | 'view' | 'edit' | 'copy', no?: string) {
  const qs = new URLSearchParams({ op })
  if (no) qs.set('no', no)
  const url = `${window.location.origin}/estimate-calc/window?${qs.toString()}`
  const win = window.open(url, '_blank')
  if (!win) {
    ElMessage.warning(t('新页签被浏览器拦截，请允许本站点打开新页签后再试'))
  }
}

function onNew() { openInWindow('new') }
function onView(row: EstimateCalcListItem) { openInWindow('view', row.qtnCalcNo) }
function onEdit(row: EstimateCalcListItem) { openInWindow('edit', row.qtnCalcNo) }
function onCopy(row: EstimateCalcListItem) { openInWindow('copy', row.qtnCalcNo) }

async function onDelete(row: EstimateCalcListItem) {
  try {
    await ElMessageBox.confirm(t('削除 {no} ? （論理削除、復旧不可）', { no: row.qtnCalcNo }), t('確認'), { type: 'warning' })
  } catch {
    return
  }
  try {
    const res = await estimateCalcApi.remove(row.qtnCalcNo)
    if (res.code === 0) {
      ElMessage.success(t('削除完了'))
      listRef.value?.reload()
    }
  } catch { /* interceptor toast */ }
}

// 子ウィンドウ保存/削除通知で一覧リフレッシュ
function handleMessage(e: MessageEvent) {
  if (e.origin !== window.location.origin) return
  const data = e.data
  if (data?.source === 'cp6-estimate' && (data.type === 'saved' || data.type === 'deleted')) {
    listRef.value?.reload()
  }
}

onMounted(async () => {
  window.addEventListener('message', handleMessage)
  const baseRes = await masterApi.getBases()
  bases.value = baseRes.data ?? []
})

onBeforeUnmount(() => {
  window.removeEventListener('message', handleMessage)
})
</script>
