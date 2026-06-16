<template>
  <div class="pur-gr">
    <div class="page-header">
      <h2>{{ t('采购收货') }}</h2>
      <span class="subtitle">{{ t('双基准：着荷=收货即验收；检收=收货待检，质检合格后验收。委托 WMS 物理入库') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="poNo" size="small" style="width: 150px" :placeholder="t('采购订单号')" clearable @change="reload" />
        <el-input v-model="supplierId" size="small" style="width: 150px" :placeholder="t('供应商')" clearable @change="reload" />
        <el-button type="primary" size="small" @click="openCreate">{{ t('新建收货') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="grNo" :label="t('收货单号')" width="150" />
        <el-table-column prop="poNo" :label="t('采购订单号')" width="150" />
        <el-table-column prop="supplierId" :label="t('供应商')" width="120" />
        <el-table-column :label="t('收货日期')" width="110">
          <template #default="{ row }">{{ (row.receiptDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('入账基准')" width="100" align="center">
          <template #default="{ row }">{{ t(POSTING_BASIS_LABEL[row.postingBasis || '2'] || '') }}</template>
        </el-table-column>
        <el-table-column :label="t('WMS入库单')" width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.wmsInboundNo || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="GR_STATUS_TAG[row.status] || 'info'" size="small">{{ t(GR_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row)">{{ t('查看') }}</el-button>
            <el-button v-if="row.status === 2" link type="success" size="small" @click="doApplyQc(row)">{{ t('应用检收') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建收货 -->
    <el-dialog v-model="createVisible" :title="t('新建收货')" width="820">
      <el-form :model="form" label-width="90px" size="small">
        <el-row :gutter="12">
          <el-col :span="10">
            <el-form-item :label="t('采购订单号')" required>
              <el-input v-model="form.poNo" maxlength="20">
                <template #append><el-button @click="loadPoLines" :loading="loadingPo">{{ t('载入订单行') }}</el-button></template>
              </el-input>
            </el-form-item>
          </el-col>
          <el-col :span="7"><el-form-item :label="t('收货日期')"><el-date-picker v-model="form.receiptDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="7"><el-form-item :label="t('入库仓库')"><el-input v-model="form.warehouseCd" maxlength="10" /></el-form-item></el-col>
        </el-row>
        <el-form-item :label="t('备注')"><el-input v-model="form.remarks" maxlength="500" /></el-form-item>

        <div class="lines-head"><span>{{ t('收货明细') }}</span></div>
        <el-table :data="lineRows" border size="small">
          <el-table-column prop="poLineNo" :label="t('行')" width="60" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="160" show-overflow-tooltip />
          <el-table-column prop="orderedQty" :label="t('订购量')" width="100" align="right" />
          <el-table-column prop="receivedQty" :label="t('已收货')" width="100" align="right" />
          <el-table-column :label="t('本次收货')" width="150">
            <template #default="{ row }"><el-input-number v-model="row.thisQty" :min="0" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
        </el-table>
        <div class="hint">{{ t('本次收货量超出订购未收量将被挡（超收）') }}</div>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" :disabled="lineRows.length === 0" @click="submit">{{ t('确认收货') }}</el-button>
      </template>
    </el-dialog>

    <!-- 明细 -->
    <el-dialog v-model="detailVisible" :title="t('收货单') + ' ' + (detail?.grNo || '')" width="820">
      <template v-if="detail">
        <el-descriptions :column="3" size="small" border>
          <el-descriptions-item :label="t('采购订单号')">{{ detail.poNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('供应商')">{{ detail.supplierId }}</el-descriptions-item>
          <el-descriptions-item :label="t('状态')"><el-tag :type="GR_STATUS_TAG[detail.status!] || 'info'" size="small">{{ t(GR_STATUS_LABEL[detail.status!] || '') }}</el-tag></el-descriptions-item>
          <el-descriptions-item :label="t('入账基准')">{{ t(POSTING_BASIS_LABEL[detail.postingBasis || '2'] || '') }}</el-descriptions-item>
          <el-descriptions-item :label="t('WMS入库单')">{{ detail.wmsInboundNo || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('入库仓库')">{{ detail.warehouseCd || '—' }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 10px">
          <el-table-column prop="lineNo" :label="t('行')" width="50" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="140" show-overflow-tooltip />
          <el-table-column prop="receivedQty" :label="t('收货量')" width="100" align="right" />
          <el-table-column prop="acceptedQty" :label="t('合格量')" width="100" align="right" />
          <el-table-column prop="rejectedQty" :label="t('不良量')" width="100" align="right" />
          <el-table-column :label="t('检收状态')" width="100" align="center">
            <template #default="{ row }">{{ t(QC_STATUS_LABEL[row.qcStatus || ''] || row.qcStatus || '') }}</template>
          </el-table-column>
        </el-table>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { grApi, poApi } from '@/api/pur/pur'
import {
  GR_STATUS_LABEL, GR_STATUS_TAG, QC_STATUS_LABEL, POSTING_BASIS_LABEL,
  type GoodsReceipt, type GrCreateForm,
} from '@/types/pur/pur'

const { t } = useI18n()
const rows = ref<GoodsReceipt[]>([])
const loading = ref(false)
const loadingPo = ref(false)
const saving = ref(false)
const poNo = ref('')
const supplierId = ref('')
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref<GoodsReceipt | null>(null)

interface GrLineRow { poLineNo: number; itemId: string; orderedQty: number; receivedQty: number; thisQty: number }
const lineRows = ref<GrLineRow[]>([])

function emptyForm(): GrCreateForm {
  return { poNo: '', receiptDate: new Date().toISOString().slice(0, 10), warehouseCd: '', remarks: '', lines: [] }
}
const form = reactive<GrCreateForm>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await grApi.list(poNo.value || undefined, supplierId.value || undefined)
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
        const ordered = l.qty || 0
        const received = l.receivedQty || 0
        return { poLineNo: l.lineNo || 0, itemId: l.itemId, orderedQty: ordered, receivedQty: received, thisQty: Math.max(0, ordered - received) }
      })
    if (lineRows.value.length === 0) ElMessage.info(t('该订单无可收货明细'))
  } finally {
    loadingPo.value = false
  }
}

async function submit() {
  const lines = lineRows.value.filter(l => (l.thisQty ?? 0) > 0).map(l => ({ poLineNo: l.poLineNo, receivedQty: l.thisQty }))
  if (lines.length === 0) { ElMessage.warning(t('请填写至少一行收货量')); return }
  saving.value = true
  try {
    await grApi.confirm({ poNo: form.poNo.trim(), receiptDate: form.receiptDate, warehouseCd: form.warehouseCd || null, remarks: form.remarks || null, lines })
    ElMessage.success(t('已收货'))
    createVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function openDetail(row: GoodsReceipt) {
  if (!row.grNo) return
  const res = await grApi.get(row.grNo)
  detail.value = res?.data || null
  detailVisible.value = true
}

async function doApplyQc(row: GoodsReceipt) {
  if (!row.grNo) return
  await grApi.applyQc(row.grNo)
  ElMessage.success(t('检收已应用'))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.pur-gr { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
