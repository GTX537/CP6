<template>
  <div class="pur-po">
    <div class="page-header">
      <h2>{{ t('采购订单') }}</h2>
      <span class="subtitle">{{ t('建单带出币种/税码/汇率，行三累计锚（收货/验收/开票）派生订单状态') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="supplierId" size="small" style="width: 150px" :placeholder="t('供应商')" clearable @change="reload" />
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in PO_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button v-permission="'pur-po:add'" type="primary" size="small" @click="openCreate">{{ t('新建采购单') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="poNo" :label="t('订单号')" width="150" />
        <el-table-column :label="t('供应商')" width="150" show-overflow-tooltip>
          <template #default="{ row }">{{ row.supplierName || row.supplierId }}</template>
        </el-table-column>
        <el-table-column :label="t('类型')" width="90" align="center">
          <template #default="{ row }">{{ t(PO_TYPE_LABEL[row.type] || '') }}</template>
        </el-table-column>
        <el-table-column :label="t('订单日期')" width="110">
          <template #default="{ row }">{{ (row.orderDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('币种')" width="70">
          <template #default="{ row }">{{ row.currencyCd || 'JPY' }}</template>
        </el-table-column>
        <el-table-column prop="netAmount" :label="t('净额')" width="110" align="right" />
        <el-table-column prop="grossAmount" :label="t('价税合计')" width="120" align="right" />
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="PO_STATUS_TAG[row.status] || 'info'" size="small">{{ t(PO_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="170" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row)">{{ t('查看') }}</el-button>
            <el-button v-if="row.status === 0" v-permission="'pur-po:submit'" link type="success" size="small" @click="doSubmit(row)">{{ t('送审') }}</el-button>
            <el-button v-if="row.status === 0 || row.status === 2" v-permission="'pur-po:cancel'" link type="danger" size="small" @click="doCancel(row)">{{ t('取消') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 建单 -->
    <el-dialog v-model="createVisible" :title="t('新建采购单')" width="820">
      <el-form :model="form" label-width="90px" size="small">
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('供应商')" required><el-input v-model="form.supplierId" maxlength="20" :placeholder="t('发注先编码')" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('类型')"><el-select v-model="form.type" style="width:100%"><el-option v-for="o in PO_TYPE_OPTIONS" :key="o.value" :value="o.value" :label="t(o.label)" /></el-select></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('订单日期')"><el-date-picker v-model="form.orderDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
        </el-row>
        <el-form-item :label="t('备注')"><el-input v-model="form.remarks" maxlength="500" /></el-form-item>

        <div class="lines-head">
          <span>{{ t('明细行') }}</span>
          <el-button link type="primary" size="small" @click="addLine">{{ t('添加行') }}</el-button>
        </div>
        <el-table :data="form.lines" border size="small">
          <el-table-column type="index" :label="t('行')" width="50" />
          <el-table-column :label="t('物料')" min-width="180">
            <template #default="{ row }"><el-input v-model="row.itemId" size="small" maxlength="40" /></template>
          </el-table-column>
          <el-table-column :label="t('数量')" width="120">
            <template #default="{ row }"><el-input-number v-model="row.qty" :min="0" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('单价')" width="140">
            <template #default="{ row }"><el-input-number v-model="row.unitPrice" :min="0" :precision="4" size="small" controls-position="right" style="width:100%" :placeholder="t('留空=带价')" /></template>
          </el-table-column>
          <el-table-column :label="t('要求交期')" width="150">
            <template #default="{ row }"><el-date-picker v-model="row.requiredDate" type="date" size="small" value-format="YYYY-MM-DD" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('操作')" width="60">
            <template #default="{ $index }"><el-button link type="danger" size="small" @click="form.lines.splice($index, 1)">{{ t('删') }}</el-button></template>
          </el-table-column>
        </el-table>
        <div class="hint">{{ t('单价留空时按供应商阶梯价自动带出，无适用价则挡单') }}</div>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- 明细 -->
    <el-dialog v-model="detailVisible" :title="t('采购订单') + ' ' + (detail?.poNo || '')" width="900">
      <template v-if="detail">
        <el-descriptions :column="3" size="small" border>
          <el-descriptions-item :label="t('供应商')">{{ detail.supplierName || detail.supplierId }}</el-descriptions-item>
          <el-descriptions-item :label="t('类型')">{{ t(PO_TYPE_LABEL[detail.type] || '') }}</el-descriptions-item>
          <el-descriptions-item :label="t('状态')"><el-tag :type="PO_STATUS_TAG[detail.status!] || 'info'" size="small">{{ t(PO_STATUS_LABEL[detail.status!] || '') }}</el-tag></el-descriptions-item>
          <el-descriptions-item :label="t('币种')">{{ detail.currencyCd || 'JPY' }}</el-descriptions-item>
          <el-descriptions-item :label="t('汇率')">{{ detail.fxRate }}</el-descriptions-item>
          <el-descriptions-item :label="t('入账基准')">{{ t(POSTING_BASIS_LABEL[detail.postingBasis || '2'] || '') }}</el-descriptions-item>
          <el-descriptions-item :label="t('净额')">{{ detail.netAmount }}</el-descriptions-item>
          <el-descriptions-item :label="t('税额')">{{ detail.taxAmount }}</el-descriptions-item>
          <el-descriptions-item :label="t('价税合计')">{{ detail.grossAmount }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 10px">
          <el-table-column prop="lineNo" :label="t('行')" width="50" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="140" show-overflow-tooltip />
          <el-table-column prop="qty" :label="t('数量')" width="90" align="right" />
          <el-table-column prop="unitPrice" :label="t('单价')" width="100" align="right" />
          <el-table-column prop="receivedQty" :label="t('已收货')" width="90" align="right" />
          <el-table-column prop="acceptedQty" :label="t('已验收')" width="90" align="right" />
          <el-table-column prop="invoicedQty" :label="t('已开票')" width="90" align="right" />
          <el-table-column :label="t('匹配')" width="90" align="center">
            <template #default="{ row }">{{ t(LINE_MATCH_LABEL[row.matchStatus] || '') }}</template>
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
import { poApi } from '@/api/pur/pur'
import {
  PO_STATUS_LABEL, PO_STATUS_TAG, PO_TYPE_LABEL, PO_TYPE_OPTIONS, POSTING_BASIS_LABEL,
  type PurchaseOrder, type PoCreateForm, type PoLineCreateForm,
} from '@/types/pur/pur'

const { t } = useI18n()
const LINE_MATCH_LABEL: Record<number, string> = { 0: '未匹配', 1: '部分匹配', 2: '匹配完成' }

const rows = ref<PurchaseOrder[]>([])
const loading = ref(false)
const saving = ref(false)
const supplierId = ref('')
const status = ref<number | undefined>(undefined)
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref<PurchaseOrder | null>(null)

function emptyLine(): PoLineCreateForm {
  return { itemId: '', qty: 1, unitPrice: null, taxCodeId: null, requiredDate: null }
}
function emptyForm(): PoCreateForm {
  return { supplierId: '', type: 1, orderDate: new Date().toISOString().slice(0, 10), remarks: '', lines: [emptyLine()] }
}
const form = reactive<PoCreateForm>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await poApi.list(supplierId.value || undefined, status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  form.lines = [emptyLine()]
  createVisible.value = true
}
function addLine() { form.lines.push(emptyLine()) }

async function submit() {
  if (!form.supplierId?.trim()) { ElMessage.warning(t('供应商必填')); return }
  if (form.lines.length === 0 || form.lines.some(l => !l.itemId?.trim() || (l.qty ?? 0) <= 0)) {
    ElMessage.warning(t('每行需填物料且数量大于0')); return
  }
  saving.value = true
  try {
    await poApi.create({ ...form })
    ElMessage.success(t('已新建'))
    createVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function openDetail(row: PurchaseOrder) {
  if (!row.poNo) return
  const res = await poApi.get(row.poNo)
  detail.value = res?.data || null
  detailVisible.value = true
}

async function doSubmit(row: PurchaseOrder) {
  if (!row.poNo) return
  await poApi.submit(row.poNo)
  ElMessage.success(t('已送审'))
  await reload()
}

async function doCancel(row: PurchaseOrder) {
  if (!row.poNo) return
  await ElMessageBox.confirm(t('确认取消该采购单？'), t('提示'), { type: 'warning' })
  await poApi.cancel(row.poNo)
  ElMessage.success(t('已取消'))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.pur-po { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
