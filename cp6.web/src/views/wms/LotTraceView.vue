<template>
  <div class="wms-lot-trace">
    <el-card shadow="never" class="search-card">
      <el-form inline size="small">
        <el-form-item :label="t('wms.common.product')">
          <el-input v-model="productCd" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('wms.common.lot')">
          <el-input v-model="lotNo" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('wms.lotTrace.fld.direction')">
          <el-radio-group v-model="direction" size="small">
            <el-radio-button value="FORWARD">{{ t('wms.lotTrace.dir.forward') }}</el-radio-button>
            <el-radio-button value="BACKWARD">{{ t('wms.lotTrace.dir.backward') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="trace" :loading="loading">{{ t('wms.lotTrace.btn.trace') }}</el-button>
          <el-button @click="loadSummary" :disabled="!productCd || !lotNo">{{ t('wms.lotTrace.btn.summary') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row v-if="summary" :gutter="12" style="margin-bottom: 12px">
      <el-col :span="24">
        <el-card shadow="never">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <span style="font-weight: 600">{{ t('wms.lotTrace.summary.title') }}</span>
              <el-button v-if="!summary.recallFlag" v-permission="'wms-lot-trace:recall'" type="danger" size="small" @click="setRecall(true)">{{ t('wms.lotTrace.btn.setRecall') }}</el-button>
              <el-button v-else v-permission="'wms-lot-trace:recall'" type="warning" size="small" @click="setRecall(false)">{{ t('wms.lotTrace.btn.clearRecall') }}</el-button>
            </div>
          </template>
          <el-descriptions :column="4" size="small" border>
            <el-descriptions-item :label="t('wms.common.product')">{{ summary.productCd }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.common.lot')">{{ summary.lotNo }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.stock.col.physical')">{{ formatQty(summary.totalPhysicalQty) }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.stock.col.available')">{{ formatQty(summary.totalAvailableQty) }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.lotTrace.summary.locationCount')">{{ summary.locationCount }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.common.expiryDate')">{{ summary.expiryDate?.slice(0, 10) || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="t('wms.stock.flag.recall')">
              <CpTag v-if="summary.recallFlag" tone="danger">{{ t('wms.lotTrace.summary.recalled') }}</CpTag>
              <span v-else>—</span>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>
    </el-row>

    <el-row v-if="result" :gutter="12">
      <!-- 影响 list（顾客 or 仕入先） -->
      <el-col :span="10">
        <el-card shadow="never">
          <template #header>
            <span style="font-weight: 600">
              {{ direction === 'FORWARD' ? t('wms.lotTrace.affected.customers') : t('wms.lotTrace.affected.suppliers') }}
              <CpTag tone="warn" style="margin-left: 8px">{{ affectedList.length }}</CpTag>
            </span>
          </template>
          <el-empty v-if="affectedList.length === 0" :description="t('wms.lotTrace.affected.none')" />
          <el-table v-else :data="affectedList" size="small" border max-height="500">
            <el-table-column v-if="direction === 'FORWARD'" prop="customerCd" :label="t('wms.outbound.fld.customerCd')" width="100" />
            <el-table-column v-if="direction === 'FORWARD'" prop="customerName" :label="t('wms.outbound.fld.customerName')" min-width="150" show-overflow-tooltip />
            <el-table-column v-if="direction === 'FORWARD'" prop="outboundNo" :label="t('wms.outbound.fld.no')" width="180" />
            <el-table-column v-if="direction === 'BACKWARD'" prop="supplierCd" :label="t('wms.inbound.fld.supplierCd')" width="100" />
            <el-table-column v-if="direction === 'BACKWARD'" prop="supplierName" :label="t('wms.inbound.fld.supplierName')" min-width="150" show-overflow-tooltip />
            <el-table-column v-if="direction === 'BACKWARD'" prop="inboundNo" :label="t('wms.receipt.title')" width="180" />
            <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
              <template #default="{ row }">{{ formatQty(row.qty) }}</template>
            </el-table-column>
            <el-table-column :label="t('wms.lotTrace.col.at')" width="160">
              <template #default="{ row }">{{ (row.shippedAt || row.receivedAt)?.replace('T', ' ').slice(0, 16) }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>

      <!-- 时序トランザクション -->
      <el-col :span="14">
        <el-card shadow="never">
          <template #header>
            <span style="font-weight: 600">
              {{ t('wms.lotTrace.nodes.title') }}
              <CpTag tone="muted" style="margin-left: 8px">{{ result.nodes.length }}</CpTag>
            </span>
          </template>
          <el-empty v-if="result.nodes.length === 0" :description="t('wms.common.noSelection')" />
          <el-timeline v-else>
            <el-timeline-item v-for="n in result.nodes" :key="n.txnNo"
                :type="timelineTypeOf(n.txnType)" :timestamp="n.txnAt?.replace('T', ' ').slice(0, 19)">
              <CpTag :tone="txnTone(n.txnType)">{{ n.txnType }}</CpTag>
              <span style="margin-left: 8px">{{ n.warehouseCd }} - {{ n.locationCd }}</span>
              <span :class="n.qty < 0 ? 'qty-out' : 'qty-in'" style="margin-left: 8px; font-weight: 600">
                {{ n.qty > 0 ? '+' : '' }}{{ formatQty(n.qty) }}
              </span>
              <div v-if="n.relatedNo" style="color: var(--cp-muted); font-size: 12px; margin-top: 4px">
                [{{ n.relatedType }}] {{ n.relatedNo }}
              </div>
              <div v-if="n.remark" style="color: var(--cp-text); font-size: 12px; margin-top: 4px">{{ n.remark }}</div>
            </el-timeline-item>
          </el-timeline>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { lotTraceApi } from '@/api/wms/lotTrace'
import type { LotTraceResult, LotStockSummary } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const productCd = ref('')
const lotNo = ref('')
const direction = ref<'FORWARD' | 'BACKWARD'>('FORWARD')
const loading = ref(false)

const result = ref<LotTraceResult | null>(null)
const summary = ref<LotStockSummary | null>(null)

const affectedList = computed<any[]>(() => {
  if (!result.value) return []
  return direction.value === 'FORWARD' ? result.value.affectedCustomers : result.value.affectedSuppliers
})

// el-timeline-item :type 用 EP type（时间轴圆点色，el-timeline 为特殊组件保留）
function txnTagOf(t: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[t as 'IN'] || 'info'
}
function timelineTypeOf(t: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' { return txnTagOf(t) }
// CpTag 用共享 Tone（保色：success→ok / danger→danger / warning→warn / primary→info / info→muted）
function txnTone(t: string): Tone {
  return ({ IN: 'ok', OUT: 'danger', RSV: 'warn', UNRSV: 'muted', MOVE: 'info', ADJ: 'muted' } as const)[t as 'IN'] || 'muted'
}

async function trace() {
  if (!productCd.value || !lotNo.value) { ElMessage.warning(t('wms.common.required')); return }
  loading.value = true
  try {
    const res = direction.value === 'FORWARD'
      ? await lotTraceApi.forward(productCd.value, lotNo.value)
      : await lotTraceApi.backward(productCd.value, lotNo.value)
    result.value = res.data
    // 同時に summary も取得
    try { const s = await lotTraceApi.summary(productCd.value, lotNo.value); summary.value = s.data } catch { summary.value = null }
  } finally { loading.value = false }
}

async function loadSummary() {
  try {
    const s = await lotTraceApi.summary(productCd.value, lotNo.value)
    summary.value = s.data
  } catch { summary.value = null }
}

async function setRecall(flag: boolean) {
  if (!productCd.value || !lotNo.value || !summary.value) return
  try {
    const action = flag ? t('wms.lotTrace.btn.setRecall') : t('wms.lotTrace.btn.clearRecall')
    await ElMessageBox.confirm(`${action}: ${productCd.value} / ${lotNo.value}`, t('wms.common.confirm'), { type: 'warning' })
    const res = await lotTraceApi.recall(productCd.value, lotNo.value, flag)
    ElMessage.success(t('wms.lotTrace.msg.recallApplied', { n: res.data.affectedStocks }))
    await loadSummary()
  } catch { /* */ }
}
</script>

<style scoped>
.wms-lot-trace { padding: 16px; }
.search-card { margin-bottom: 12px; }
.qty-in { color: var(--cp-ok); }
.qty-out { color: var(--cp-danger); }
</style>
