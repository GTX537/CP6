<template>
  <div class="pur-match">
    <div class="page-header">
      <h2>{{ t('三单匹配') }}</h2>
      <span class="subtitle">{{ t('PO+收货验收+供应商发票三方对齐：容差内自动建应付，超容差挂起人工裁决') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="poNo" size="small" style="width: 150px" :placeholder="t('采购订单号')" clearable @change="reload" />
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in MATCH_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button v-permission="'pur-match:add'" type="primary" size="small" @click="openCreate">{{ t('匹配供应商发票') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="matchNo" :label="t('匹配单号')" width="150" />
        <el-table-column prop="poNo" :label="t('采购订单号')" width="150" />
        <el-table-column prop="supplierInvoiceNo" :label="t('供方发票号')" width="140" show-overflow-tooltip />
        <el-table-column :label="t('匹配日期')" width="110">
          <template #default="{ row }">{{ (row.matchDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('最大价差')" width="100" align="right">
          <template #default="{ row }">{{ pct(row.maxPriceVarPct) }}</template>
        </el-table-column>
        <el-table-column :label="t('应付发票号')" width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.apInvoiceNo || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="MATCH_STATUS_TAG[row.status] || 'info'" size="small">{{ t(MATCH_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="190" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row)">{{ t('查看') }}</el-button>
            <el-button v-if="row.status === 1" v-permission="'pur-match:release'" link type="success" size="small" @click="doRelease(row)">{{ t('放行') }}</el-button>
            <el-button v-if="row.status === 1" v-permission="'pur-match:reject'" link type="danger" size="small" @click="doReject(row)">{{ t('拒绝') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 匹配发票 -->
    <el-dialog v-model="createVisible" :title="t('匹配供应商发票')" width="860">
      <el-form :model="form" label-width="100px" size="small">
        <el-row :gutter="12">
          <el-col :span="9">
            <el-form-item :label="t('采购订单号')" required>
              <el-input v-model="form.poNo" maxlength="20">
                <template #append><el-button @click="loadPoLines" :loading="loadingPo">{{ t('载入可开票行') }}</el-button></template>
              </el-input>
            </el-form-item>
          </el-col>
          <el-col :span="8"><el-form-item :label="t('供方发票号')" required><el-input v-model="form.supplierInvoiceNo" maxlength="50" /></el-form-item></el-col>
          <el-col :span="7"><el-form-item :label="t('发票日期')"><el-date-picker v-model="form.invoiceDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
        </el-row>

        <div class="lines-head"><span>{{ t('发票明细') }}</span></div>
        <el-table :data="lineRows" border size="small">
          <el-table-column prop="poLineNo" :label="t('行')" width="60" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="150" show-overflow-tooltip />
          <el-table-column prop="remainAccepted" :label="t('可开票量')" width="110" align="right" />
          <el-table-column prop="poUnitPrice" :label="t('订单单价')" width="110" align="right" />
          <el-table-column :label="t('发票数量')" width="140">
            <template #default="{ row }"><el-input-number v-model="row.qty" :min="0" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('发票单价')" width="140">
            <template #default="{ row }"><el-input-number v-model="row.unitPrice" :min="0" :precision="4" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
        </el-table>
        <div class="hint">{{ t('容差内自动建应付并累加已开票量；超容差或超可开票量则挂起待人工放行') }}</div>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" :disabled="lineRows.length === 0" @click="submit">{{ t('匹配') }}</el-button>
      </template>
    </el-dialog>

    <!-- 明细 -->
    <el-dialog v-model="detailVisible" :title="t('三单匹配') + ' ' + (detail?.matchNo || '')" width="880">
      <template v-if="detail">
        <el-descriptions :column="3" size="small" border>
          <el-descriptions-item :label="t('采购订单号')">{{ detail.poNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('供方发票号')">{{ detail.supplierInvoiceNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('状态')"><el-tag :type="MATCH_STATUS_TAG[detail.status!] || 'info'" size="small">{{ t(MATCH_STATUS_LABEL[detail.status!] || '') }}</el-tag></el-descriptions-item>
          <el-descriptions-item :label="t('应付发票号')">{{ detail.apInvoiceNo || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('最大价差')">{{ pct(detail.maxPriceVarPct) }}</el-descriptions-item>
          <el-descriptions-item :label="t('处理人')">{{ detail.handledBy || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('差异说明')" :span="3">{{ detail.note || '—' }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 10px">
          <el-table-column prop="poLineNo" :label="t('行')" width="50" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="130" show-overflow-tooltip />
          <el-table-column prop="qty" :label="t('发票数量')" width="100" align="right" />
          <el-table-column prop="unitPrice" :label="t('发票单价')" width="100" align="right" />
          <el-table-column prop="remainAccepted" :label="t('可开票量')" width="100" align="right" />
          <el-table-column :label="t('价差')" width="90" align="right">
            <template #default="{ row }">{{ pct(row.priceVarPct) }}</template>
          </el-table-column>
          <el-table-column :label="t('容差内')" width="80" align="center">
            <template #default="{ row }">
              <el-tag size="small" :type="row.withinTolerance ? 'success' : 'danger'">{{ t(row.withinTolerance ? '是' : '否') }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { matchApi, poApi } from '@/api/pur/pur'
import {
  MATCH_STATUS_LABEL, MATCH_STATUS_TAG,
  type ThreeWayMatch, type MatchInvoiceForm,
} from '@/types/pur/pur'

const { t } = useI18n()
const rows = ref<ThreeWayMatch[]>([])
const loading = ref(false)
const loadingPo = ref(false)
const saving = ref(false)
const poNo = ref('')
const status = ref<number | undefined>(undefined)
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref<ThreeWayMatch | null>(null)

interface MatchLineRow { poLineNo: number; itemId: string; remainAccepted: number; poUnitPrice: number; qty: number; unitPrice: number }
const lineRows = ref<MatchLineRow[]>([])

function pct(v?: number) { return v == null ? '—' : (v * 100).toFixed(2) + '%' }

function emptyForm(): MatchInvoiceForm {
  return { poNo: '', supplierInvoiceNo: '', invoiceDate: new Date().toISOString().slice(0, 10), lines: [] }
}
const form = reactive<MatchInvoiceForm>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await matchApi.list(poNo.value || undefined, status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  lineRows.value = []
  createVisible.value = true
}

async function loadPoLines() {
  if (!form.poNo?.trim()) { ElMessage.warning(t('请先填采购订单号')); return }
  loadingPo.value = true
  try {
    const res = await poApi.get(form.poNo.trim())
    const po = res?.data
    if (!po) { ElMessage.warning(t('采购订单不存在')); return }
    lineRows.value = (po.lines || [])
      .filter(l => (l.status ?? 0) === 0)
      .map(l => {
        const remain = Math.max(0, (l.acceptedQty || 0) - (l.invoicedQty || 0))
        return { poLineNo: l.lineNo || 0, itemId: l.itemId, remainAccepted: remain, poUnitPrice: l.unitPrice || 0, qty: remain, unitPrice: l.unitPrice || 0 }
      })
      .filter(l => l.remainAccepted > 0)
    if (lineRows.value.length === 0) ElMessage.info(t('该订单当前无可开票量'))
  } finally {
    loadingPo.value = false
  }
}

async function submit() {
  const lines = lineRows.value.filter(l => (l.qty ?? 0) > 0).map(l => ({ poLineNo: l.poLineNo, qty: l.qty, unitPrice: l.unitPrice, taxCodeId: null }))
  if (lines.length === 0) { ElMessage.warning(t('请填写至少一行发票明细')); return }
  if (!form.supplierInvoiceNo?.trim()) { ElMessage.warning(t('供方发票号必填')); return }
  saving.value = true
  try {
    const res = await matchApi.match({ poNo: form.poNo.trim(), supplierInvoiceNo: form.supplierInvoiceNo.trim(), invoiceDate: form.invoiceDate, lines })
    const r = res?.data
    if (r?.apCreated) ElMessage.success(t('匹配通过，已建应付发票 {no}', { no: r.apInvoiceNo || '' }))
    else ElMessage.warning(t('差异超容差，已挂起待人工裁决'))
    createVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function openDetail(row: ThreeWayMatch) {
  if (!row.matchNo) return
  const res = await matchApi.get(row.matchNo)
  detail.value = res?.data || null
  detailVisible.value = true
}

async function doRelease(row: ThreeWayMatch) {
  if (!row.matchNo) return
  const { value } = await ElMessageBox.prompt(t('接受差异并建应付，请填处理说明'), t('放行挂起单'), { inputType: 'textarea' })
  const res = await matchApi.release(row.matchNo, value || undefined)
  ElMessage.success(t('已放行，应付发票 {no}', { no: res?.data?.apInvoiceNo || '' }))
  await reload()
}

async function doReject(row: ThreeWayMatch) {
  if (!row.matchNo) return
  const { value } = await ElMessageBox.prompt(t('拒绝该匹配（不建应付），请填原因'), t('拒绝挂起单'), { inputType: 'textarea' })
  await matchApi.reject(row.matchNo, value || undefined)
  ElMessage.success(t('已拒绝'))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.pur-match { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
