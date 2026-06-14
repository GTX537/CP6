<template>
  <div class="wms-stock">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.warehouseCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.location')"><el-input v-model="query.locationCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.product')"><el-input v-model="query.productCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.lot')"><el-input v-model="query.lotNo" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.stock.fld.owner')">
          <el-select v-model="query.ownerType" clearable style="width: 120px">
            <el-option :label="t('wms.stock.fld.ownerSelf')" value="SELF" />
            <el-option :label="t('wms.stock.fld.ownerCustomer')" value="CUSTOMER" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.hasStockOnly">{{ t('wms.stock.fld.hasStockOnly') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('wms.common.clear') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px"><el-tag size="small">{{ t('wms.common.total') }}: {{ total }}</el-tag></div>
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row @current-change="onSelect">
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="80" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
        <el-table-column prop="physicalQty" :label="t('wms.stock.col.physical')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.physicalQty) }}</template>
        </el-table-column>
        <el-table-column prop="allocatedQty" :label="t('wms.stock.col.allocated')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.allocatedQty) }}</template>
        </el-table-column>
        <el-table-column prop="availableQty" :label="t('wms.stock.col.available')" width="120" align="right">
          <template #default="{ row }">
            <span :class="{ neg: row.availableQty < 0 }">{{ formatQty(row.availableQty) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="unitCd" :label="t('wms.common.unit')" width="80" />
        <el-table-column prop="expiryDate" :label="t('wms.common.expiryDate')" width="120">
          <template #default="{ row }">{{ row.expiryDate?.slice(0, 10) || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stock.col.owner')" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.ownerType === 'CUSTOMER'" type="warning" size="small">{{ t('wms.stock.flag.vmi') }}</el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.stock.col.flag')" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.recallFlag" type="danger" size="small">{{ t('wms.stock.flag.recall') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.stock.col.qc')" width="100">
          <template #default="{ row }">
            <el-tag :type="qcTagOf(row.qcStatus)" size="small">
              {{ t(`wms.stock.qc.${row.qcStatus || 'PENDING'}`) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openHistory(row)">{{ t('wms.common.history') }}</el-button>
            <el-button link type="warning" size="small" @click="openQcDialog(row)">{{ t('wms.stock.qc.btn') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-model:current-page="query.page" v-model:page-size="query.pageSize" :total="total"
        :page-sizes="[50, 100, 200]" layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="reload" @size-change="reload"
      />
    </el-card>

    <!-- Phase 7 Gap 1.3 — QC 状态设置弹窗 -->
    <el-dialog v-model="qcDialogVisible" :title="t('wms.stock.qc.dlgTitle')" width="520">
      <div v-if="qcTarget" style="margin-bottom: 12px; padding: 8px 12px; background: #f5f7fa; border-radius: 4px; font-size: 13px">
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
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { stockApi } from '@/api/wms/stock'
import type { Stock, StockTransaction } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const query = reactive<{
  warehouseCd?: string
  locationCd?: string
  productCd?: string
  lotNo?: string
  ownerType?: string
  hasStockOnly: boolean
  page: number
  pageSize: number
}>({ hasStockOnly: true, page: 1, pageSize: 50 })

const rows = ref<Stock[]>([])
const total = ref(0)
const loading = ref(false)
const selected = ref<Stock | null>(null)

const historyVisible = ref(false)
const historyStock = ref<Stock | null>(null)
const historyTxns = ref<StockTransaction[]>([])

function formatQty(n: number | null | undefined): string {
  if (n == null) return ''
  return fmtQty(n, 4)
}

function txnTagOf(t: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[t as 'IN'] || 'info'
}

function onSelect(row: Stock | null) { selected.value = row }

async function reload() {
  loading.value = true
  try {
    const res = await stockApi.search(query)
    rows.value = res.data.items
    total.value = res.data.total
  } finally { loading.value = false }
}

function resetQuery() {
  query.warehouseCd = undefined
  query.locationCd = undefined
  query.productCd = undefined
  query.lotNo = undefined
  query.ownerType = undefined
  query.hasStockOnly = true
  query.page = 1
  reload()
}

async function openHistory(row: Stock) {
  historyStock.value = row
  const res = await stockApi.history(row.id, 365)
  historyTxns.value = res.data.transactions
  historyVisible.value = true
}

// ───── Phase 7 Gap 1.3 QC ステータス設定 ─────
const qcDialogVisible = ref(false)
const qcTarget = ref<Stock | null>(null)
const qcNewStatus = ref<string>('')
const qcReason = ref('')
const qcSaving = ref(false)

function qcTagOf(s?: string): 'success' | 'danger' | 'warning' | 'info' {
  switch (s) {
    case 'PASSED': return 'success'
    case 'FAILED': return 'danger'
    case 'HOLD': return 'warning'
    case 'PENDING':
    default: return 'info'
  }
}

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
      // 局部更新 + 列表全刷新
      if (qcTarget.value) qcTarget.value.qcStatus = res.data.qcStatus
      await reload()
    } else {
      ElMessage.error(res.message || 'Unknown error')
    }
  } catch (e: any) {
    ElMessage.error(e?.message ?? 'Network error')
  } finally {
    qcSaving.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.wms-stock { padding: 16px; }
.search-card { margin-bottom: 12px; }
.neg { color: #f56c6c; font-weight: 600; }
</style>
