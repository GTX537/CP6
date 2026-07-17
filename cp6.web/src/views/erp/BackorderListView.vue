<!--
  受注残一覧 —— ERP 迁移批次3（CpPageShell + CpListPage）。
  照会一覧（backorderApi.queue は扁平配列・ページングなし）→ paginated=false（単表スクロール、原 max-height 相当）。
  webOrderNo/remainingQty/操作＝col slot、数量列＝kind:'num'+map formatQty、得意先＝map(name||cd)。
  close/split アクションは確認ダイアログを CpListPage 外の兄弟要素で保持（原様、reason 必須検証保全）。
  件数は toolbar CpTag（原 table-toolbar の erp.backorder.total 文言を保持）、更新ボタンも toolbar。
-->
<template>
  <CpPageShell :title="t('erp.backorder.title')">
    <CpListPage
      ref="listRef"
      :paginated="false"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      :empty-text="t('erp.backorder.empty')"
      @total-change="total = $event"
    >
      <template #toolbar>
        <CpTag tone="info">{{ t('erp.backorder.total', { n: total ?? 0 }) }}</CpTag>
        <div class="tb-spacer" />
        <el-button :icon="Refresh" circle :loading="loading" @click="listRef?.reload()" />
      </template>

      <template #col-webOrderNo="{ row }">
        <el-button link type="primary" size="small" @click="goOrder(row.webOrderNo)">
          {{ row.webOrderNo }}
        </el-button>
      </template>

      <template #col-remainingQty="{ row }">
        <strong class="remaining">{{ formatQty(row.remainingQty) }}</strong>
      </template>

      <template #col-_action="{ row }">
        <div class="row-actions">
          <el-button v-permission="'erp-backorder:close'" type="warning" link size="small" :icon="Check" @click="openAction(row, 'close')">
            {{ t('erp.backorder.btn.close') }}
          </el-button>
          <el-button v-permission="'erp-backorder:split'" type="primary" link size="small" :icon="CopyDocument" @click="openAction(row, 'split')">
            {{ t('erp.backorder.btn.split') }}
          </el-button>
        </div>
      </template>
    </CpListPage>

    <!-- close / split 確認ダイアログ（CpListPage 外の兄弟要素） -->
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
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Check, CopyDocument, Refresh } from '@element-plus/icons-vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import { backorderApi } from '@/api/erp/backorder'
import { formatQty } from '@/utils/format'
import type { BackorderQueueItem } from '@/types/erp/backorder'

type BackorderAction = 'close' | 'split'

const { t } = useI18n()
const router = useRouter()

const listRef = ref<InstanceType<typeof CpListPage>>()
const total = ref<number>()
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

const columns = computed<ListColumn[]>(() => [
  { prop: 'webOrderNo', label: t('erp.backorder.col.webOrderNo'), width: 155 },
  { prop: 'customerName', label: t('erp.backorder.col.customer'), minWidth: 180, overflowTooltip: true,
    map: (_v, row) => ({ label: (row as BackorderQueueItem).customerName || (row as BackorderQueueItem).customerCd || '' }) },
  { prop: 'detailNo', label: t('erp.backorder.col.detailNo'), width: 88, align: 'right' },
  { prop: 'productCd', label: t('erp.backorder.col.product'), width: 140, overflowTooltip: true },
  { prop: 'orderedQty', label: t('erp.backorder.col.orderedQty'), width: 120, kind: 'num',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'shippedQty', label: t('erp.backorder.col.shippedQty'), width: 120, kind: 'num',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'backorderQty', label: t('erp.backorder.col.backorderQty'), width: 120, kind: 'num',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'remainingQty', label: t('erp.backorder.col.remainingQty'), width: 130, align: 'right' },
  { prop: 'lastShipDate', label: t('erp.backorder.col.lastShipDate'), width: 128,
    map: (v) => ({ label: formatDate(v as string) || '-' }) },
  { prop: '_action', label: t('erp.backorder.col.actions'), width: 190, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('erp.backorder.btn.search'),
  reset: t('erp.backorder.btn.reset'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'customerCd', label: t('erp.backorder.search.customer'), type: 'text' },
  { key: 'dateFrom', label: t('erp.backorder.search.dateFrom'), type: 'date', valueFormat: 'YYYY-MM-DD' },
  { key: 'dateTo', label: t('erp.backorder.search.dateTo'), type: 'date', valueFormat: 'YYYY-MM-DD' },
])

const fetchList: ListFetch = async ({ filters }) => {
  const f = filters as Record<string, unknown>
  loading.value = true
  try {
    const res = await backorderApi.queue({
      customerCd: (f.customerCd as string) || undefined,
      dateFrom: (f.dateFrom as string) || undefined,
      dateTo: (f.dateTo as string) || undefined,
    })
    const all = res.data || []
    return { rows: all, total: all.length }
  } finally {
    loading.value = false
  }
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
    await listRef.value?.reload()
  } finally {
    actionLoading.value = false
  }
}

function goOrder(webOrderNo: string) {
  router.push({ path: '/order', query: { webOrderNo } })
}

function formatDate(value?: string): string {
  return value ? value.slice(0, 10) : ''
}
</script>

<style scoped>
.tb-spacer { flex: 1; }

.remaining {
  color: var(--cp-warn);
  font-weight: 650;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.dialog-target {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 8px 10px;
  margin-bottom: 12px;
  background: var(--cp-bg);
  border-radius: var(--cp-r-sm);
  color: var(--cp-text);
}

.dialog-target strong {
  color: var(--cp-warn);
}

@media (max-width: 767px) {
  .backorder-dialog {
    width: calc(100vw - 24px) !important;
  }
}
</style>
