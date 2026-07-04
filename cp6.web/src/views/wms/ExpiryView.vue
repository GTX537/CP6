<!--
  賞味期限管理 —— CpPageShell + CpListPage 迁移（WMS 批次1）。
  selectable 勾选 + toolbar slot（超期/損失額 概览 pill + 一括廃棄按钮）；数量/金額/残日 走 col slot 保留原格式化与语义色。
  概览指标（超期件数/損失合計）在 fetch 包装内按「全量结果」计算（CpListPage 不外露 rows，只 emit total）。
  「N 日以内」为 type:'number'(1..365)（契约扩展二轮 #10）；字段初值空，fetch 内缺省按 30 查（原页初值 30 的等价语义）。
  :paginated="false"（契约扩展二轮 #11）→ 单表滚动、跨全量勾选一括廃棄恢复；廃棄成功后 listRef.reload()
  in-place 刷新（契约扩展二轮 #12），保留当前搜索条件。数据源 expiryApi.expiring 返回扁平数组无 total。
-->
<template>
  <CpPageShell :title="t('wms.expiry.title')" :count="total">
    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      :paginated="false"
      selectable
      row-key="stockId"
      @selection-change="onSelectionChange"
      @total-change="total = $event"
    >
      <template #toolbar>
        <CpTag v-if="overdueCount > 0" tone="danger">{{ t('wms.expiry.col.overdue') }}: {{ overdueCount }}</CpTag>
        <CpTag v-if="totalLoss > 0" tone="warn">{{ t('wms.expiry.col.totalLoss') }}: ¥{{ formatMoney(totalLoss) }}</CpTag>
        <span class="tb-spacer" />
        <el-button type="danger" :disabled="selected.length === 0" @click="onDispose">
          {{ t('wms.expiry.btn.dispose') }} ({{ selected.length }})
        </el-button>
      </template>

      <template #col-physicalQty="{ row }">{{ formatQty(row.physicalQty) }}</template>
      <template #col-daysUntilExpiry="{ row }">
        <span :class="dayClass(row.daysUntilExpiry)">{{ row.daysUntilExpiry }}</span>
      </template>
      <template #col-unitPrice="{ row }">{{ row.unitPrice != null ? `¥${formatMoney(row.unitPrice)}` : '—' }}</template>
      <template #col-lossAmount="{ row }">{{ row.lossAmount != null ? `¥${formatMoney(row.lossAmount)}` : '—' }}</template>
    </CpListPage>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag from '@/components/base/CpTag.vue'
import { expiryApi } from '@/api/wms/expiry'
import type { ExpiryStock } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const selected = ref<ExpiryStock[]>([])
const overdueCount = ref(0)
const totalLoss = ref(0)
// 廃棄成功后命令式 reload（保留当前搜索条件，in-place 刷新）
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)

function dayClass(d: number) {
  if (d < 0) return 'overdue'
  if (d < 7) return 'soon'
  return ''
}
function formatMoney(n: number) { return formatQty(Math.round(Number(n) || 0), 0) }

const columns = computed<ListColumn[]>(() => [
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 140 },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 90 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'physicalQty', label: t('wms.stock.col.physical'), width: 100, align: 'right' },
  { prop: 'expiryDate', label: t('wms.common.expiryDate'), width: 120, kind: 'date' },
  { prop: 'daysUntilExpiry', label: t('wms.expiry.col.daysLeft'), width: 100, align: 'right' },
  { prop: 'unitPrice', label: t('wms.common.unitPrice'), width: 110, align: 'right' },
  { prop: 'lossAmount', label: t('wms.expiry.col.lossAmount'), width: 120, align: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  // 原页 el-input-number(1..365) 形态恢复；字段初值空 → fetch 内缺省 30（原页初值 30 的等价语义）
  { key: 'days', label: t('wms.expiry.fld.daysWithin'), type: 'number', min: 1, max: 365, placeholder: '30' },
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
])

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const rawDays = f.days == null || f.days === '' ? 30 : Number(f.days)
  const days = Number.isFinite(rawDays) && rawDays > 0 ? Math.min(rawDays, 365) : 30
  const wh = f.warehouseCd ? String(f.warehouseCd) : undefined
  const res = await expiryApi.expiring(days, wh)
  const all = res.data || []
  // 概览指标按全量结果计算（模板只 emit total，rows 不外露 → 在 fetch 包装内算）
  overdueCount.value = all.filter(r => r.daysUntilExpiry < 0).length
  totalLoss.value = all.reduce((sum, r) => sum + (r.lossAmount || 0), 0)
  // :paginated=false → page 恒 1、size=1000：slice 等于全量透传（数据量远小于 1000）
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

function onSelectionChange(sel: unknown[]) { selected.value = sel as ExpiryStock[] }

async function onDispose() {
  if (selected.value.length === 0) return
  try {
    await ElMessageBox.confirm(
      t('wms.expiry.msg.confirmDispose', { n: selected.value.length }),
      t('wms.common.confirm'),
      { type: 'warning' }
    )
    const reason = await ElMessageBox.prompt(t('wms.expiry.msg.reasonAsk'), t('wms.common.confirm'), {
      inputValue: t('賞味期限切れ廃棄'),
    }).then(r => r.value).catch(() => null)
    if (reason == null) return
    const res = await expiryApi.dispose({ stockIds: selected.value.map(s => s.stockId), reason })
    ElMessage.success(`${res.data.disposed} ${t('wms.expiry.msg.disposed')}`)
    selected.value = []
    listRef.value?.reload() // in-place 重查，保留当前搜索条件
  } catch { /* */ }
}
</script>

<style scoped>
.tb-spacer { flex: 1; }
.overdue { color: var(--cp-danger); font-weight: 700; }
.soon { color: var(--cp-warn); font-weight: 600; }
</style>
