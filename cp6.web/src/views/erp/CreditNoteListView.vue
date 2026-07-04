<!--
  クレジットノート一覧 —— ERP 迁移批次2（CpPageShell + CpListPage）。
  照会一覧（onMounted 自動取得・必須検索条件なし・単一 fetch）で CpListPage 契約に素直に載る。
  種別列＝kind:'tag'+map（REFUND=warn/EXCHANGE=info/SCRAP=danger）、発行日＝kind:'date'、
  数量/金額＝kind:'num'+map（formatQty）、Web受注NO＝col slot（明細へ遷移リンク）、理由＝col slot（50 字省略+tooltip）、取引先＝map(name||cd)。
  モバイル専用カード分岐は撤去し、設計システム標準（main.css: el-table 横スクロール）に統一。
-->
<template>
  <CpPageShell :title="t('erp.creditNote.title')" :count="total">
    <CpListPage
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-webOrderNo="{ row }">
        <el-button v-if="row.webOrderNo" link type="primary" size="small" @click="goOrder(row.webOrderNo)">
          {{ row.webOrderNo }}
        </el-button>
      </template>
      <template #col-reason="{ row }">
        <el-tooltip v-if="row.reason && row.reason.length > 50" :content="row.reason" placement="top">
          <span>{{ truncateReason(row.reason) }}</span>
        </el-tooltip>
        <span v-else>{{ row.reason }}</span>
      </template>
    </CpListPage>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { creditNoteApi } from '@/api/erp/creditNote'
import { formatQty } from '@/utils/format'
import type { CreditNoteType } from '@/types/erp/creditNote'

const { t } = useI18n()
const router = useRouter()

const total = ref<number>()

function typeTone(type: CreditNoteType): Tone {
  if (type === 'REFUND') return 'warn'
  if (type === 'EXCHANGE') return 'info'
  return 'danger'
}
function formatAmount(value?: number | null): string {
  return value == null ? '' : formatQty(value, 2)
}
function truncateReason(value?: string): string {
  if (!value) return ''
  return value.length > 50 ? `${value.slice(0, 50)}...` : value
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'issueDate', label: t('erp.creditNote.col.issueDate'), width: 120, kind: 'date' },
  { prop: 'creditNoteNo', label: t('erp.creditNote.col.no'), width: 150, overflowTooltip: true },
  { prop: 'webOrderNo', label: t('erp.creditNote.col.webOrderNo'), width: 150 },
  { prop: 'rmaNo', label: t('erp.creditNote.col.rmaNo'), width: 140, overflowTooltip: true },
  { prop: 'customerName', label: t('erp.creditNote.col.customer'), minWidth: 170, overflowTooltip: true,
    map: (_v, row) => ({ label: (row as { customerName?: string; customerCd?: string }).customerName || (row as { customerCd?: string }).customerCd || '' }) },
  { prop: 'type', label: t('erp.creditNote.col.type'), width: 120, kind: 'tag',
    map: (v) => ({ label: t(`erp.creditNote.type.${v}`), tone: typeTone(v as CreditNoteType) }) },
  { prop: 'productCd', label: t('erp.creditNote.col.product'), width: 130, overflowTooltip: true },
  { prop: 'qty', label: t('erp.creditNote.col.qty'), width: 110, kind: 'num',
    map: (v) => ({ label: v == null ? '' : formatQty(v as number) }) },
  { prop: 'amount', label: t('erp.creditNote.col.amount'), width: 120, kind: 'num',
    map: (v) => ({ label: formatAmount(v as number) }) },
  { prop: 'reason', label: t('erp.creditNote.col.reason'), minWidth: 220 },
])

const filterLabels = computed(() => ({
  search: t('erp.creditNote.btn.search'),
  reset: t('erp.creditNote.btn.reset'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'customerCd', label: t('erp.creditNote.search.customer'), type: 'text' },
  { key: 'webOrderNo', label: t('erp.creditNote.search.webOrderNo'), type: 'text' },
  {
    key: 'type', label: t('erp.creditNote.search.type'), type: 'select',
    options: [
      { label: t('erp.creditNote.type.ALL'), value: '' },
      { label: t('erp.creditNote.type.REFUND'), value: 'REFUND' },
      { label: t('erp.creditNote.type.EXCHANGE'), value: 'EXCHANGE' },
      { label: t('erp.creditNote.type.SCRAP'), value: 'SCRAP' },
    ],
  },
  { key: 'dateFrom', label: t('erp.creditNote.search.dateFrom'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'dateTo', label: t('erp.creditNote.search.dateTo'), type: 'date', valueFormat: 'YYYY-MM-DD' },
])

function normalizePaged(res: unknown) {
  const data = (res as { data?: Record<string, unknown> })?.data || {}
  return {
    items: (data.items || data.Items || []) as unknown[],
    total: Number(data.total ?? data.Total ?? 0),
  }
}

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const res = await creditNoteApi.search({
    customerCd: (f.customerCd as string) || undefined,
    webOrderNo: (f.webOrderNo as string) || undefined,
    type: (f.type as CreditNoteType | '') || '',
    dateFrom: (f.dateFrom as string) || undefined,
    dateTo: (f.dateTo as string) || undefined,
    page,
    pageSize: size,
  })
  const paged = normalizePaged(res)
  return { rows: paged.items, total: paged.total }
}

function goOrder(webOrderNo: string) {
  router.push({ path: '/order', query: { webOrderNo } })
}
</script>
