<!--
  御見積一覧 —— ERP 迁移批次3（CpListPage）。
  照会一覧（onMounted 自動取得）+ サーバソート（#19 sortable:'custom'）。ページ標題キー無し（不臆造）→ CpListPage スタンドアロン、件数はページャ total。
  qtnNo=kind:'mono'、issueDate=kind:'date'、数量/単価/金額=kind:'num'+map（formatQty/formatNumber）、status=kind:'tag'+map（statusTone）。
  staffCd（担当者名 tooltip）/操作=col slot。ステータス複数チェック（0/9/C）=toolbar（#15、fetch closure が statusSel を読む、@reset で #22 クリア）、新規=toolbar（#16）。
  参照/訂正/流用は別タブ MSBBPA030（openInWindow）、子タブ postMessage（cp6-quotation saved/deleted）→ reload。モバイル専用カードは設計システム標準（横スクロール）へ統一。
-->
<template>
  <div class="quotation-list">
    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @reset="onFilterReset"
    >
      <template #toolbar>
        <el-checkbox-group v-model="statusSel" @change="listRef?.reload()">
          <el-checkbox value="0">{{ t('sales.fsc.notConfirmed') }}</el-checkbox>
          <el-checkbox value="9">{{ t('sales.status.approved') }}</el-checkbox>
          <el-checkbox value="C">{{ t('sales.status.confirmed') }}</el-checkbox>
        </el-checkbox-group>
        <div class="tb-spacer" />
        <el-button type="success" :icon="Plus" @click="onNew">{{ t('sales.btn.new') }}</el-button>
      </template>

      <template #col-staffCd="{ row }">
        <el-tooltip v-if="row.staffName" :content="row.staffName" placement="top">
          <span>{{ row.staffCd }}</span>
        </el-tooltip>
        <span v-else>{{ row.staffCd }}</span>
      </template>

      <template #col-_action="{ row }">
        <el-button link type="primary" @click="onView(row)">{{ t('sales.op.view') }}</el-button>
        <el-button link type="warning" @click="onEdit(row)">{{ t('sales.op.edit') }}</el-button>
        <el-button link type="success" @click="onCopy(row)">{{ t('sales.op.copy') }}</el-button>
        <el-button link type="info" @click="onIssue(row)">{{ t('sales.btn.issue') }}</el-button>
        <el-button link type="danger" @click="onDelete(row)">{{ t('sales.op.delete') }}</el-button>
      </template>
    </CpListPage>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import CpListPage, { type ListColumn, type ListFetch, type SortOrder } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { quotationApi } from '@/api/erp/quotation'
import { masterApi } from '@/api/erp/master'
import type { QuotationListItem, QuotationQuery } from '@/types/erp/quotation'
import type { MasterBase } from '@/types/erp/estimateCalc'
import { formatQty, formatNumber } from '@/utils/format'

const { t } = useI18n()

const listRef = ref<InstanceType<typeof CpListPage>>()
const bases = ref<MasterBase[]>([])

// —— ステータス複数チェック（#15、toolbar）——
const statusSel = ref<string[]>([])
// クリア連動（#22 reset 透传）：原 onReset は statusSel も初期化していた。
// emit は reset 起因の load() より先に同期発火するため、直後の fetch は既にクリア済みの値を読む。
function onFilterReset() {
  statusSel.value = []
}

function fmtNum(v?: number) {
  return v == null ? '' : formatQty(v)
}
function fmtMoney(v?: number) {
  return v == null ? '' : formatNumber(v, 'decimal')
}
function statusTone(s?: string): Tone {
  if (s === '見積確定済') return 'ok'
  if (s === '承認済') return 'warn'
  return 'info'
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'qtnNo', label: t('sales.term.qtnNo'), kind: 'mono', width: 120, fixed: 'left', sortable: 'custom' },
  { prop: 'qtnIssueDate', label: t('sales.qtn.issueDate'), kind: 'date', width: 110, sortable: 'custom' },
  { prop: 'baseCd', label: t('sales.term.base'), width: 70, sortable: 'custom' },
  { prop: 'staffCd', label: t('sales.term.staff'), width: 80, sortable: 'custom' },
  { prop: 'customerCd', label: t('sales.term.customer') + ' CD', width: 90, sortable: 'custom' },
  { prop: 'customerName', label: t('sales.term.customer') + t('sales.term.bpName').slice(-1), minWidth: 160, overflowTooltip: true, sortable: 'custom' },
  { prop: 'projectNoParent', label: t('親案件'), width: 110, sortable: 'custom' },
  { prop: 'projectNoChild', label: t('子案件'), width: 110, sortable: 'custom' },
  { prop: 'itemName1', label: t('品名1'), minWidth: 160, overflowTooltip: true },
  { prop: 'firstQuantity', label: t('初行数量'), width: 100, kind: 'num', map: (v) => ({ label: fmtNum(v as number) }) },
  { prop: 'firstUnitPrice', label: t('初行') + t('sales.term.unitPrice'), width: 110, kind: 'num', map: (v) => ({ label: fmtMoney(v as number) }) },
  { prop: 'firstAmount', label: t('初行') + t('sales.term.amount'), width: 130, kind: 'num', map: (v) => ({ label: fmtMoney(v as number) }) },
  { prop: 'totalAmount', label: t('sales.fsc.totalAmount'), width: 130, kind: 'num', sortable: 'custom', map: (v) => ({ label: fmtMoney(v as number) }) },
  { prop: 'status', label: t('sales.term.status'), width: 100, kind: 'tag', map: (v) => ({ label: String(v ?? ''), tone: statusTone(v as string) }) },
  { prop: '_action', label: t('sales.list.action'), width: 320, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('sales.btn.search'),
  reset: t('sales.btn.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'qtnNoFrom', label: t('sales.term.qtnNo') + ' ' + t('sales.search.from'), type: 'text' },
  { key: 'qtnNoTo', label: t('sales.term.qtnNo') + ' ' + t('sales.search.to'), type: 'text' },
  { key: 'issueDate', label: t('sales.qtn.issueDate'), type: 'daterange', valueFormat: 'YYYY-MM-DD' },
  { key: 'baseCd', label: t('sales.term.base'), type: 'select',
    options: bases.value.map(b => ({ label: `${b.baseCd} ${b.baseName}`, value: b.baseCd })) },
  { key: 'staffCd', label: t('sales.term.staff'), type: 'text' },
  { key: 'customerCd', label: t('sales.term.customer'), type: 'text' },
  { key: 'projectNoParent', label: t('親案件'), type: 'text' },
  { key: 'customerProductName1', label: t('品名'), type: 'text' },
])

const fetchList: ListFetch = async ({ page, size, filters, sortField, sortOrder }) => {
  const f = filters as Record<string, unknown>
  const range = f.issueDate as [string, string] | undefined
  const q: Record<string, unknown> = {
    qtnNoFrom: f.qtnNoFrom,
    qtnNoTo: f.qtnNoTo,
    baseCd: f.baseCd,
    staffCd: f.staffCd,
    customerCd: f.customerCd,
    projectNoParent: f.projectNoParent,
    customerProductName1: f.customerProductName1,
    issueDateFrom: range?.[0],
    issueDateTo: range?.[1],
    statuses: statusSel.value.length ? [...statusSel.value] : undefined,
    sortField: sortField as string | undefined,
    sortOrder: sortOrder as SortOrder | undefined,
    page,
    pageSize: size,
  }
  Object.keys(q).forEach((k) => {
    const v = q[k]
    if (v === '' || v === null || v === undefined) delete q[k]
  })
  const res = await quotationApi.getList(q as unknown as QuotationQuery)
  if (res.code === 0) return { rows: res.data.rows ?? [], total: res.data.total ?? 0 }
  return { rows: [], total: 0 }
}

/** 新しいタブで MSBBPA030 を開く */
function openInWindow(opc: 'new' | 'view' | 'edit' | 'copy', no?: string) {
  const qs = new URLSearchParams({ op: opc })
  if (no) qs.set('no', no)
  const url = `${window.location.origin}/quotation/window?${qs.toString()}`
  const w = window.open(url, '_blank')
  if (!w) {
    ElMessage.warning(t('新しいタブがブロックされました。このサイトに対してポップアップを許可してください'))
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
      t('{no} を発行します。対象を選択してください', { no: row.qtnNo }),
      t('発行'),
      {
        inputType: 'text',
        inputValue: 'Q,SC,C',
        inputPlaceholder: t('Q=御見積書 / SC=提出用計算書 / C=計算書（カンマ区切り）'),
        confirmButtonText: t('発行'),
        cancelButtonText: t('キャンセル'),
      }
    )
    const set = new Set(String(choices).split(',').map(s => s.trim().toUpperCase()))
    const res = await quotationApi.issue(row.qtnNo, {
      issueQuotation: set.has('Q'),
      issueSubmitCalc: set.has('SC'),
      issueCalc: set.has('C'),
    })
    if (res.code === 0) {
      ElMessage.success(t('発行しました: {files}', { files: res.data.files.join(' , ') || t('(ファイルなし)') }))
      listRef.value?.reload()
    }
  } catch {
    /* キャンセル */
  }
}

async function onDelete(row: QuotationListItem) {
  if (row.status === '見積確定済') {
    ElMessage.warning(t('確定済の御見積書は削除できません。先に確定取消してください'))
    return
  }
  try {
    await ElMessageBox.confirm(
      t('{no} を削除します（論理削除・復旧不可）。よろしいですか？', { no: row.qtnNo }),
      t('削除確認'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    const res = await quotationApi.remove(row.qtnNo)
    if (res.code === 0) {
      ElMessage.success(t('削除しました'))
      listRef.value?.reload()
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

<style scoped>
.quotation-list { padding: 0; }
.tb-spacer { flex: 1; }
</style>
