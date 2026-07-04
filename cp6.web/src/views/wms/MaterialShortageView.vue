<!--
  材料欠品 —— 查询列表页。CpPageShell(:count) + CpStatCard(未解決件数) + CpListPage（サーバ分页、码値状態列 map）。
  対応(解決/棄却)ダイアログは remark 入力の複合フォームのため el-dialog 保持。
  status 既定値 OPEN は fetch 側で seed（CpFilterBar は初期 filter 値を持たない、缺口 #17）。
-->
<template>
  <CpPageShell :title="t('wms.materialShortage.title')" :count="total">
    <div class="kpi-row">
      <CpStatCard
        :label="t('wms.materialShortage.kpi.openCount')"
        :value="openCount"
        :tone="openCount > 0 ? 'danger' : 'brand'"
      />
    </div>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-detectedAt="{ row }">{{ formatDateTime(row.detectedAt) }}</template>
      <template #col-requiredQty="{ row }">{{ formatQty(row.requiredQty) }}</template>
      <template #col-availableQty="{ row }">{{ formatQty(row.availableQty) }}</template>
      <template #col-shortQty="{ row }">
        <span class="short-qty num">{{ formatQty(shortQty(row)) }}</span>
      </template>
      <template #col-_action="{ row }">
        <el-button
          link
          type="success"
          size="small"
          :disabled="row.status !== 'OPEN'"
          @click="openAction(row, 'resolve')"
        >
          {{ t('wms.materialShortage.btn.resolve') }}
        </el-button>
        <el-button
          link
          type="info"
          size="small"
          :disabled="row.status !== 'OPEN'"
          @click="openAction(row, 'dismiss')"
        >
          {{ t('wms.materialShortage.btn.dismiss') }}
        </el-button>
      </template>
    </CpListPage>

    <el-dialog v-model="actionDialogVisible" :title="actionTitle" width="520">
      <el-form label-position="top">
        <el-form-item :label="t('wms.materialShortage.dlg.remarkLabel')">
          <el-input
            v-model="actionRemark"
            type="textarea"
            :rows="4"
            maxlength="500"
            show-word-limit
            :placeholder="t('wms.materialShortage.dlg.remarkPlaceholder')"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="actionDialogVisible = false" :disabled="actionSaving">
          {{ t('wms.materialShortage.btn.cancel') }}
        </el-button>
        <el-button type="primary" :loading="actionSaving" @click="submitAction">
          {{ t('wms.materialShortage.btn.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { materialShortageApi } from '@/api/wms/materialShortage'
import type { MaterialShortage, MaterialShortageStatus } from '@/types/wms/materialShortage'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

type ActionType = 'resolve' | 'dismiss'

const total = ref<number>()
const openCount = ref(0)
const listRef = ref<InstanceType<typeof CpListPage>>()

const actionDialogVisible = ref(false)
const actionSaving = ref(false)
const actionType = ref<ActionType>('resolve')
const actionTarget = ref<MaterialShortage | null>(null)
const actionRemark = ref('')

const actionTitle = computed(() =>
  actionType.value === 'resolve'
    ? t('wms.materialShortage.dlg.resolveTitle')
    : t('wms.materialShortage.dlg.dismissTitle'),
)

const statusMap = computed<Record<string, string>>(() => ({
  OPEN: t('wms.materialShortage.status.OPEN'),
  RESOLVED: t('wms.materialShortage.status.RESOLVED'),
  DISMISSED: t('wms.materialShortage.status.DISMISSED'),
}))

function statusTone(status: MaterialShortageStatus): Tone {
  if (status === 'OPEN') return 'danger'
  if (status === 'RESOLVED') return 'ok'
  return 'muted'
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'detectedAt', label: t('wms.materialShortage.col.detectedAt'), width: 170 },
  { prop: 'workOrderNo', label: t('wms.materialShortage.col.wo'), width: 160, overflowTooltip: true },
  { prop: 'relatedOutboundNo', label: t('wms.materialShortage.col.outbound'), width: 160, overflowTooltip: true },
  { prop: 'productCd', label: t('wms.materialShortage.col.product'), width: 130, overflowTooltip: true },
  { prop: 'lotNo', label: t('wms.materialShortage.col.lot'), width: 120, overflowTooltip: true },
  { prop: 'requiredQty', label: t('wms.materialShortage.col.requiredQty'), width: 120, kind: 'num' },
  { prop: 'availableQty', label: t('wms.materialShortage.col.availableQty'), width: 120, kind: 'num' },
  { prop: 'shortQty', label: t('wms.materialShortage.col.shortQty'), width: 120, kind: 'num' },
  { prop: 'status', label: t('wms.materialShortage.col.status'), width: 120, kind: 'tag',
    map: (v) => ({ label: statusMap.value[v as string] || String(v ?? ''), tone: statusTone(v as MaterialShortageStatus) }) },
  { prop: 'remark', label: t('wms.materialShortage.col.remark'), minWidth: 180, overflowTooltip: true },
  { prop: '_action', label: t('wms.materialShortage.col.action'), width: 180, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.materialShortage.search.btnSearch'),
  reset: t('wms.materialShortage.search.btnReset'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'workOrderNo', label: t('wms.materialShortage.search.workOrderNo'), type: 'text' },
  {
    key: 'status', label: t('wms.materialShortage.search.status'), type: 'select',
    options: [
      { label: t('wms.materialShortage.status.ALL'), value: '' },
      { label: t('wms.materialShortage.status.OPEN'), value: 'OPEN' },
      { label: t('wms.materialShortage.status.RESOLVED'), value: 'RESOLVED' },
      { label: t('wms.materialShortage.status.DISMISSED'), value: 'DISMISSED' },
    ],
  },
])

function normalizePaged(res: any) {
  const data = res?.data || {}
  return {
    items: (data.items || data.Items || []) as MaterialShortage[],
    total: Number(data.total ?? data.Total ?? 0),
  }
}

function formatQty(n: number | null | undefined): string {
  if (n == null) return ''
  return fmtQty(n, 4)
}

function formatDateTime(value?: string): string {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

function shortQty(row: MaterialShortage): number {
  return Math.max(0, Number(row.requiredQty || 0) - Number(row.availableQty || 0))
}

// —— 取数：list + openCount(KPI) を並列取得。status 未指定(初期/リセット)は既定 OPEN、''は全件 ——
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const status = f.status === undefined ? 'OPEN' : (f.status as MaterialShortageStatus | '')
  const [listRes, openRes] = await Promise.all([
    materialShortageApi.search({
      status,
      workOrderNo: (f.workOrderNo as string) || undefined,
      page,
      pageSize: size,
    }),
    materialShortageApi.search({ status: 'OPEN', page: 1, pageSize: 1 }),
  ])
  const list = normalizePaged(listRes)
  openCount.value = normalizePaged(openRes).total
  return { rows: list.items, total: list.total }
}

function openAction(row: MaterialShortage, type: ActionType) {
  actionTarget.value = row
  actionType.value = type
  actionRemark.value = ''
  actionDialogVisible.value = true
}

async function submitAction() {
  if (!actionTarget.value) return
  actionSaving.value = true
  try {
    if (actionType.value === 'resolve') {
      await materialShortageApi.resolve(actionTarget.value.id, { remark: actionRemark.value })
      ElMessage.success(t('wms.materialShortage.msg.resolved'))
    } else {
      await materialShortageApi.dismiss(actionTarget.value.id, { remark: actionRemark.value })
      ElMessage.success(t('wms.materialShortage.msg.dismissed'))
    }
    actionDialogVisible.value = false
    listRef.value?.reload()
  } finally {
    actionSaving.value = false
  }
}
</script>

<style scoped>
.kpi-row {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 260px));
  gap: 12px;
}
.short-qty {
  color: var(--cp-danger);
  font-weight: 600;
}
</style>
