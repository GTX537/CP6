<template>
  <div class="wms-expiry">
    <el-card shadow="never" class="search-card">
      <el-form inline size="small">
        <el-form-item :label="t('wms.expiry.fld.daysWithin')">
          <el-input-number v-model="days" :min="1" :max="365" controls-position="right" style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('wms.common.warehouse')">
          <el-input v-model="warehouseCd" clearable style="width: 140px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button type="danger" :disabled="selected.length === 0" @click="onDispose">
            {{ t('wms.expiry.btn.dispose') }} ({{ selected.length }})
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px">
        <el-tag size="small">{{ t('wms.common.total') }}: {{ rows.length }}</el-tag>
        <el-tag v-if="overdueCount > 0" type="danger" size="small" style="margin-left: 8px">{{ t('wms.expiry.col.overdue') }}: {{ overdueCount }}</el-tag>
        <el-tag v-if="totalLoss > 0" type="warning" size="small" style="margin-left: 8px">{{ t('wms.expiry.col.totalLoss') }}: ¥{{ formatMoney(totalLoss) }}</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" max-height="650" highlight-current-row @selection-change="onSelectionChange">
        <el-table-column type="selection" width="40" />
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="140" />
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="90" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="physicalQty" :label="t('wms.stock.col.physical')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.physicalQty) }}</template>
        </el-table-column>
        <el-table-column prop="expiryDate" :label="t('wms.common.expiryDate')" width="120">
          <template #default="{ row }">{{ row.expiryDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.expiry.col.daysLeft')" width="100" align="right">
          <template #default="{ row }">
            <span :class="dayClass(row.daysUntilExpiry)">{{ row.daysUntilExpiry }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="unitPrice" :label="t('wms.common.unitPrice')" width="110" align="right">
          <template #default="{ row }">{{ row.unitPrice != null ? `¥${formatMoney(row.unitPrice)}` : '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.expiry.col.lossAmount')" width="120" align="right">
          <template #default="{ row }">{{ row.lossAmount != null ? `¥${formatMoney(row.lossAmount)}` : '—' }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { expiryApi } from '@/api/wms/expiry'
import type { ExpiryStock } from '@/types/wms'

const { t } = useI18n()

const days = ref(30)
const warehouseCd = ref('')
const rows = ref<ExpiryStock[]>([])
const selected = ref<ExpiryStock[]>([])
const loading = ref(false)

const overdueCount = computed(() => rows.value.filter(r => r.daysUntilExpiry < 0).length)
const totalLoss = computed(() => rows.value.reduce((sum, r) => sum + (r.lossAmount || 0), 0))

function dayClass(d: number) {
  if (d < 0) return 'overdue'
  if (d < 7) return 'soon'
  return ''
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }
function formatMoney(n: number) { return Math.round(Number(n) || 0).toLocaleString('ja-JP') }

function onSelectionChange(sel: ExpiryStock[]) { selected.value = sel }

async function reload() {
  loading.value = true
  try {
    const res = await expiryApi.expiring(days.value, warehouseCd.value || undefined)
    rows.value = res.data || []
  } finally { loading.value = false }
}

async function onDispose() {
  if (selected.value.length === 0) return
  try {
    await ElMessageBox.confirm(
      t('wms.expiry.msg.confirmDispose', { n: selected.value.length }),
      t('wms.common.confirm'),
      { type: 'warning' }
    )
    const reason = await ElMessageBox.prompt(t('wms.expiry.msg.reasonAsk'), t('wms.common.confirm'), {
      inputValue: '賞味期限切れ廃棄',
    }).then(r => r.value).catch(() => null)
    if (reason == null) return
    const res = await expiryApi.dispose({ stockIds: selected.value.map(s => s.stockId), reason })
    ElMessage.success(`${res.data.disposed} ${t('wms.expiry.msg.disposed')}`)
    reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-expiry { padding: 16px; }
.search-card { margin-bottom: 12px; }
.overdue { color: var(--el-color-danger); font-weight: 700; }
.soon { color: var(--el-color-warning); font-weight: 600; }
</style>
