<!--
  在庫照会 —— CpPageShell + CpListPage 迁移（WMS 批次5，服务端分页）。
  数量列 map（formatQty）；有効在庫 col slot（负数红字）；期限/所有者/リコール/QC 走 col slot 保留条件 tag/占位。
  hasStockOnly 复选（CpFilterBar 无 boolean 字段类型，缺口 #15）→ CpListPage toolbar slot，fetch 闭包读取 + 切换后 reload()。
  QC 設定弹窗(radio+textarea, 自定义 res.code 处理) / 履歴弹窗(只读 descriptions+表) → 保留原 el-dialog（逃生舱）。
  分页服务端：fetch 透传 page/size；QC 保存后 listRef.reload()。
-->
<template>
  <CpPageShell :title="t('wms.stock.title')" :count="total">
    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #toolbar>
        <el-checkbox v-model="hasStockOnly" @change="reloadList">{{ t('wms.stock.fld.hasStockOnly') }}</el-checkbox>
      </template>

      <template #col-availableQty="{ row }">
        <span :class="{ neg: row.availableQty < 0 }">{{ formatQty(row.availableQty) }}</span>
      </template>

      <template #col-expiryDate="{ row }">{{ row.expiryDate?.slice(0, 10) || '—' }}</template>

      <template #col-owner="{ row }">
        <el-tag v-if="row.ownerType === 'CUSTOMER'" type="warning" size="small">{{ t('wms.stock.flag.vmi') }}</el-tag>
        <span v-else>—</span>
      </template>

      <template #col-flag="{ row }">
        <el-tag v-if="row.recallFlag" type="danger" size="small">{{ t('wms.stock.flag.recall') }}</el-tag>
      </template>

      <template #col-qc="{ row }">
        <el-tag :type="qcTagOf(row.qcStatus)" size="small">{{ t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) }}</el-tag>
      </template>

      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="openHistory(row)">{{ t('wms.common.history') }}</el-button>
        <el-button link type="warning" size="small" @click="openQcDialog(row)">{{ t('wms.stock.qc.btn') }}</el-button>
      </template>
    </CpListPage>

    <!-- Phase 7 Gap 1.3 — QC 状态设置弹窗（radio + 自定义结果处理，保留原机制） -->
    <el-dialog v-model="qcDialogVisible" :title="t('wms.stock.qc.dlgTitle')" width="520">
      <div v-if="qcTarget" class="qc-info">
        <div><strong>{{ t('wms.common.product') }}</strong>: {{ qcTarget.productCd }} / <strong>{{ t('wms.common.lot') }}</strong>: {{ qcTarget.lotNo }}</div>
        <div><strong>{{ t('wms.common.warehouse') }}</strong>: {{ qcTarget.warehouseCd }} / <strong>{{ t('wms.common.location') }}</strong>: {{ qcTarget.locationCd }}</div>
        <div><strong>{{ t('wms.stock.qc.current') }}</strong>: <el-tag :type="qcTagOf(qcTarget.qcStatus)" size="small">{{ t(`wms.stock.qc.${qcTarget.qcStatus || 'PENDING'}`) }}</el-tag></div>
      </div>

      <el-form label-position="top">
        <el-form-item :label="t('wms.stock.qc.newStatus')" required>
          <el-radio-group v-model="qcNewStatus">
            <el-radio-button value="PENDING">{{ t('wms.stock.qc.PENDING') }}</el-radio-button>
            <el-radio-button value="PASSED">{{ t('wms.stock.qc.PASSED') }}</el-radio-button>
            <el-radio-button value="FAILED">{{ t('wms.stock.qc.FAILED') }}</el-radio-button>
            <el-radio-button value="HOLD">{{ t('wms.stock.qc.HOLD') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('wms.stock.qc.reason')">
          <el-input
            v-model="qcReason"
            type="textarea"
            :rows="2"
            maxlength="200"
            show-word-limit
            :placeholder="t('wms.stock.qc.reasonPlaceholder')"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="qcDialogVisible = false" :disabled="qcSaving">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" :loading="qcSaving" :disabled="!qcNewStatus" @click="onQcSave">
          {{ t('wms.common.confirm') }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 履歴弹窗（只读 descriptions + 表，保留原机制） -->
    <el-dialog v-model="historyVisible" :title="t('wms.stock.dlg.history')" width="900">
      <div v-if="historyStock" style="margin-bottom: 8px">
        <el-descriptions :column="4" size="small" border>
          <el-descriptions-item :label="t('wms.common.product')">{{ historyStock.productCd }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.common.lot')">{{ historyStock.lotNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.common.warehouse')">{{ historyStock.warehouseCd }}</el-descriptions-item>
          <el-descriptions-item :label="t('wms.common.location')">{{ historyStock.locationCd }}</el-descriptions-item>
        </el-descriptions>
      </div>
      <el-table :data="historyTxns" border stripe size="small" max-height="500">
        <el-table-column prop="txnDateTime" :label="t('wms.stock.col.txnDateTime')" width="170">
          <template #default="{ row }">{{ row.txnDateTime?.replace('T', ' ').slice(0, 19) }}</template>
        </el-table-column>
        <el-table-column prop="txnType" :label="t('wms.stock.col.txnType')" width="80">
          <template #default="{ row }"><el-tag size="small" :type="txnTagOf(row.txnType)">{{ row.txnType }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.qty) }}</template>
        </el-table-column>
        <el-table-column prop="relatedNo" :label="t('wms.stock.col.relatedNo')" width="180" />
        <el-table-column prop="operatorCd" :label="t('wms.common.operator')" width="100" />
        <el-table-column prop="remark" :label="t('wms.common.remarks')" show-overflow-tooltip />
      </el-table>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import { stockApi } from '@/api/wms/stock'
import type { Stock, StockTransaction } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }

// hasStockOnly：CpFilterBar 无 boolean 字段类型（缺口 #15）→ toolbar slot 复选，fetch 闭包读取
const hasStockOnly = ref(true)

function formatQty(n: number | null | undefined): string {
  if (n == null) return ''
  return fmtQty(n, 4)
}

function txnTagOf(v: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[v as 'IN'] || 'info'
}
function qcTagOf(s?: string): 'success' | 'danger' | 'warning' | 'info' {
  switch (s) {
    case 'PASSED': return 'success'
    case 'FAILED': return 'danger'
    case 'HOLD': return 'warning'
    case 'PENDING':
    default: return 'info'
  }
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 120 },
  { prop: 'physicalQty', label: t('wms.stock.col.physical'), width: 120, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'allocatedQty', label: t('wms.stock.col.allocated'), width: 120, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'availableQty', label: t('wms.stock.col.available'), width: 120, align: 'right' },
  { prop: 'unitCd', label: t('wms.common.unit'), width: 80 },
  { prop: 'expiryDate', label: t('wms.common.expiryDate'), width: 120 },
  { prop: 'owner', label: t('wms.stock.col.owner'), width: 100 },
  { prop: 'flag', label: t('wms.stock.col.flag'), width: 100 },
  { prop: 'qc', label: t('wms.stock.col.qc'), width: 100 },
  { prop: '_action', label: t('wms.common.action'), width: 180, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  { key: 'locationCd', label: t('wms.common.location'), type: 'text' },
  { key: 'productCd', label: t('wms.common.product'), type: 'text' },
  { key: 'lotNo', label: t('wms.common.lot'), type: 'text' },
  {
    key: 'ownerType', label: t('wms.stock.fld.owner'), type: 'select',
    options: [
      { label: t('wms.stock.fld.ownerSelf'), value: 'SELF' },
      { label: t('wms.stock.fld.ownerCustomer'), value: 'CUSTOMER' },
    ],
  },
])

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: Record<string, unknown> = { page, pageSize: size, hasStockOnly: hasStockOnly.value }
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.locationCd) q.locationCd = String(f.locationCd)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.lotNo) q.lotNo = String(f.lotNo)
  if (f.ownerType) q.ownerType = String(f.ownerType)
  const res = await stockApi.search(q as never)
  return { rows: res.data.items, total: res.data.total }
}

// —— 履歴 ——
const historyVisible = ref(false)
const historyStock = ref<Stock | null>(null)
const historyTxns = ref<StockTransaction[]>([])
async function openHistory(row: Stock) {
  historyStock.value = row
  const res = await stockApi.history(row.id, 365)
  historyTxns.value = res.data.transactions
  historyVisible.value = true
}

// —— Phase 7 Gap 1.3 QC ステータス設定 ——
const qcDialogVisible = ref(false)
const qcTarget = ref<Stock | null>(null)
const qcNewStatus = ref<string>('')
const qcReason = ref('')
const qcSaving = ref(false)

function openQcDialog(row: Stock) {
  qcTarget.value = row
  qcNewStatus.value = row.qcStatus || 'PENDING'
  qcReason.value = ''
  qcDialogVisible.value = true
}
async function onQcSave() {
  if (!qcTarget.value || !qcNewStatus.value) return
  qcSaving.value = true
  try {
    const res = await stockApi.setQcStatus(qcTarget.value.id, qcNewStatus.value, qcReason.value || undefined)
    if (res.code === 0 && res.data) {
      ElMessage.success(t('wms.stock.qc.savedMsg'))
      qcDialogVisible.value = false
      if (qcTarget.value) qcTarget.value.qcStatus = res.data.qcStatus
      reloadList()
    } else {
      ElMessage.error(res.message || 'Unknown error')
    }
  } catch (e: any) {
    ElMessage.error(e?.message ?? 'Network error')
  } finally {
    qcSaving.value = false
  }
}
</script>

<style scoped>
.neg { color: var(--cp-danger); font-weight: 600; }
.qc-info { margin-bottom: 12px; padding: 8px 12px; background: var(--cp-bg-th); border-radius: var(--cp-r-sm); font-size: var(--cp-fs-base); }
</style>
