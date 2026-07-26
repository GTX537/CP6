<!--
  受注一覧 —— ERP 迁移批次2（CpListPage）。
  検索先行（拠点等を選んでから照会・onMounted 自動取得なし）→ lazy モード（#18）を dogfood。
  サーバサイド列ソート（#19 sortable:'custom'）も dogfood。数量/単価/金額＝kind:'num'（原様 raw 右寄せ）、
  通貨(currencyCd+fxRate)/預り売上フラグ＝col slot、操作（詳細/追跡/取消）＝col slot、
  受注取消は OrderCancelDialog を兄弟要素で保持（col slot から起動、成功→reload）。
  預り売上のみ/mc未転送のみ＝toolbar checkbox（#15）、CSV 出力＝toolbar（#16）。
  CSV 出力は CpFilterBar 内包 filters を親が読めないため fetch closure で最後の filters/sort/checkbox を stash して再利用。
  ページ標題キー無し（不臆造）のため CpPageShell 非適用。件数は toolbar CpTag（原様の位置）。モバイルは設計システム標準（横スクロール）。
-->
<template>
  <div class="order-list">
    <CpListPage
      ref="listRef"
      lazy
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      :empty-text="t('sales.err.E10008')"
      @total-change="total = $event"
      @reset="onFilterReset"
    >
      <template #toolbar>
        <CpTag tone="info">{{ t('sales.list.totalCount', { n: total ?? 0 }) }}</CpTag>
        <el-checkbox v-model="onlyConsignedSales" @change="listRef?.reload()">{{ t('sales.order.consignedSale') }}</el-checkbox>
        <el-checkbox v-model="onlyMcUntransferred" @change="listRef?.reload()">{{ t('sales.order.mcUntransferred') }}</el-checkbox>
        <div class="tb-spacer" />
        <el-button :icon="Download" :loading="exporting" @click="onExportCsv">{{ t('sales.btn.exportCsv') }}</el-button>
      </template>

      <template #col-currency="{ row }">
        <span>{{ row.currencyCd || 'JPY' }}</span>
        <span v-if="row.currencyCd && row.currencyCd !== 'JPY'" class="fx-rate-hint"> @{{ row.fxRate }}</span>
      </template>
      <template #col-consigned="{ row }">
        <el-icon v-if="row.consignedSalesFlg === '1'" class="flg-on"><Check /></el-icon>
      </template>
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="goDetail(row)">{{ t('詳細') }}</el-button>
        <el-button link type="primary" size="small" :icon="Connection" @click.stop="goTrace(row.webOrderNo)">
          {{ t('erp.orderTrace.btn.trace') }}
        </el-button>
        <el-button v-permission="'erp-order:cancel'" link type="danger" size="small" @click.stop="openCancelDialog(row)">
          {{ t('sales.cancel.btn') }}
        </el-button>
      </template>
    </CpListPage>

    <!-- Phase 6 受注取消ダイアログ（CpListPage 外の兄弟要素として保持） -->
    <OrderCancelDialog
      v-if="cancelDialogVisible"
      v-model="cancelDialogVisible"
      :web-order-no="cancelTargetWebOrderNo"
      @cancelled="onCancelled"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check, Connection } from '@element-plus/icons-vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose, type SortOrder } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import { orderApi } from '@/api/erp/order'
import type { OrderQueryDto, OrderListItemDto } from '@/types/erp/order'
import OrderCancelDialog from './OrderCancelDialog.vue'

const { t } = useI18n()
const router = useRouter()

const listRef = ref<ListPageExpose>()
const total = ref<number>()
const exporting = ref(false)

// —— toolbar checkbox（#15）——
const onlyConsignedSales = ref(false)
const onlyMcUntransferred = ref(false)

// クリア連動（#22 reset 透传）：原 resetQuery は onlyConsignedSales/onlyMcUntransferred も初期化していた。
// emit は reset 起因の load() より先に同期発火するため、直後の fetch は既にクリア済みの値を読む。
function onFilterReset() {
  onlyConsignedSales.value = false
  onlyMcUntransferred.value = false
}

// —— CSV 出力用 stash（CpListPage 内包 filters を親が読めない代償）——
const lastFilters = ref<Record<string, unknown>>({})
const lastSort = ref<{ sortField?: string; sortOrder?: SortOrder }>({})

// —— Phase 6 取消ダイアログ ——
const cancelDialogVisible = ref(false)
const cancelTargetWebOrderNo = ref('')
function openCancelDialog(row: OrderListItemDto) {
  cancelTargetWebOrderNo.value = row.webOrderNo
  cancelDialogVisible.value = true
}
function onCancelled() {
  ElMessage.success(t('sales.cancel.successMsg'))
  listRef.value?.reload()
}

const columns = computed<ListColumn<OrderListItemDto>[]>(() => [
  { prop: 'rowNo', label: t('sales.list.no'), width: 60, align: 'center' },
  { prop: 'customerCd', label: t('sales.term.customer'), width: 100, sortable: 'custom' },
  { prop: 'customerName', label: t('sales.term.customer') + t('sales.term.bpName').slice(-1), width: 160 },
  { prop: 'salesPersonName', label: t('sales.term.staff'), width: 120 },
  { prop: 'orderSheetNo', label: t('注文書NO'), width: 120 },
  { prop: 'haibaiNo1', label: t('手配NO1'), width: 140, sortable: 'custom' },
  { prop: 'defectiveHaibaiNo', label: t('不適合手配NO'), width: 140, sortable: 'custom' },
  { prop: 'mcOrderNo', label: t('注文NO(mc)'), width: 140, sortable: 'custom' },
  { prop: 'orderDate', label: t('受注日'), width: 100 },
  { prop: 'customerDeliveryDate', label: t('客先納期'), width: 100, sortable: 'custom' },
  { prop: 'productCd', label: t('製品CD'), width: 140, sortable: 'custom' },
  { prop: 'cpItemOrComposition', label: t('CP品名/構成'), minWidth: 180 },
  { prop: 'sheetFlute', label: t('段'), width: 60, sortable: 'custom' },
  { prop: 'compositionF', label: t('表(構成)'), width: 140 },
  { prop: 'compositionC', label: t('中(構成)'), width: 140 },
  { prop: 'compositionB', label: t('裏(構成)'), width: 140 },
  { prop: 'quantity', label: t('数量'), width: 100, kind: 'num', sortable: 'custom' },
  { prop: 'qtyUnit', label: t('単位'), width: 60 },
  { prop: 'individualUnitPrice', label: t('個別単価'), width: 120, kind: 'num', sortable: 'custom' },
  { prop: 'setUnitPrice', label: t('セット単価'), width: 120, kind: 'num', sortable: 'custom' },
  { prop: 'amount', label: t('受注金額'), width: 130, kind: 'num', sortable: 'custom' },
  { prop: 'currency', label: t('通貨'), width: 110, align: 'center' },
  { prop: 'consigned', label: t('預り売上'), width: 80, align: 'center' },
  { prop: 'slipNote', label: t('伝票備考'), minWidth: 160, sortable: 'custom' },
  { prop: '_action', label: t('操作'), width: 230, align: 'center', fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('sales.btn.search'),
  reset: t('sales.btn.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'baseCd', label: t('sales.term.base'), type: 'text' },
  { key: 'customerCd', label: t('sales.term.customer') + ' ' + t('sales.search.from'), type: 'text' },
  { key: 'customerCdTo', label: t('sales.term.customer') + ' ' + t('sales.search.to'), type: 'text' },
  { key: 'orderType', label: t('sales.term.orderType'), type: 'text' },
  { key: 'orderDateFrom', label: t('sales.term.orderDate') + ' ' + t('sales.search.from'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'orderDateTo', label: t('sales.term.orderDate') + ' ' + t('sales.search.to'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'deliveryDateFrom', label: t('sales.term.deliveryDate') + ' ' + t('sales.search.from'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'deliveryDateTo', label: t('sales.term.deliveryDate') + ' ' + t('sales.search.to'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'haibaiNo1From', label: t('sales.term.haibaiNo') + '1 ' + t('sales.search.from'), type: 'text' },
  { key: 'haibaiNo1To', label: t('sales.term.haibaiNo') + '1 ' + t('sales.search.to'), type: 'text' },
  { key: 'orderSheetNo', label: t('sales.term.orderSheet'), type: 'text' },
  { key: 'productCd', label: t('sales.term.productCd'), type: 'text' },
  { key: 'customerItemName', label: t('顧客品名'), type: 'text' },
  { key: 'sheetFlute', label: t('シート段'), type: 'text' },
  { key: 'paperCd', label: t('原紙CD'), type: 'text' },
  { key: 'printCd', label: t('印刷CD'), type: 'text' },
  { key: 'embossCd', label: t('エンボスCD'), type: 'text' },
  { key: 'makerCd', label: t('メーカCD'), type: 'text' },
  { key: 'carrier', label: t('運送会社'), type: 'text' },
])

// —— filters + checkbox + sort → API query（空値除去；#17）——
function buildQuery(filters: Record<string, unknown>, sortField?: string, sortOrder?: SortOrder): OrderQueryDto {
  const q: Record<string, unknown> = { ...filters }
  q.onlyConsignedSales = onlyConsignedSales.value || undefined
  q.onlyMcUntransferred = onlyMcUntransferred.value || undefined
  q.sortField = sortField || undefined
  q.sortOrder = sortOrder || undefined
  Object.keys(q).forEach((k) => {
    const v = q[k]
    if (v === '' || v === null || v === undefined) delete q[k]
  })
  return q as unknown as OrderQueryDto
}

const fetchList: ListFetch<OrderListItemDto> = async ({ page, size, filters, sortField, sortOrder }) => {
  lastFilters.value = filters
  lastSort.value = { sortField, sortOrder }
  const q = buildQuery(filters, sortField, sortOrder)
  const res = await orderApi.searchList({ ...q, page, pageSize: size })
  if (res.code === 0 && res.data) return { rows: res.data.rows, total: res.data.total }
  return { rows: [], total: 0 }
}

async function onExportCsv() {
  exporting.value = true
  try {
    const q = buildQuery(lastFilters.value, lastSort.value.sortField, lastSort.value.sortOrder)
    const blob = await orderApi.exportListCsv(q) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `orders_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  } catch { /* interceptor toast */ } finally {
    exporting.value = false
  }
}

function goDetail(row: OrderListItemDto) {
  router.push({ path: '/order', query: { webOrderNo: row.webOrderNo } })
}
function goTrace(webOrderNo: string) {
  router.push({ path: '/erp/order-trace', query: { webOrderNo } })
}
</script>

<style scoped>
.order-list { padding: 0; }
.tb-spacer { flex: 1; }
.fx-rate-hint { color: var(--cp-muted); }
.flg-on { color: var(--cp-ok); }
</style>
