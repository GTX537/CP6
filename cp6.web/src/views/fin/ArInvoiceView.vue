<template>
  <div class="ar-invoice">
    <div class="page-header">
      <h2>{{ t('应收发票') }}</h2>
      <span class="subtitle">{{ t('录入草稿→过账自动生成凭证（借应收/贷收入+销项税）；出货自动开票') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="customerId" size="small" style="width: 150px" :placeholder="t('客户')" clearable @change="reload" />
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in AR_INVOICE_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button v-permission="'fin-ar-invoice:add'" type="primary" size="small" @click="openCreate(false)">{{ t('新建发票') }}</el-button>
        <el-button v-permission="'fin-ar-invoice:credit-memo'" size="small" @click="openCreate(true)">{{ t('销售红字') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="no" :label="t('发票号')" width="150" />
        <el-table-column prop="customerId" :label="t('客户')" width="110" />
        <el-table-column prop="shipmentId" :label="t('来源出货')" width="120" show-overflow-tooltip />
        <el-table-column :label="t('记账日期')" width="110">
          <template #default="{ row }">{{ (row.invoiceDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('币种')" width="70">
          <template #default="{ row }">{{ row.currencyCd || 'JPY' }}</template>
        </el-table-column>
        <el-table-column prop="grossAmount" :label="t('价税合计')" width="120" align="right" />
        <el-table-column prop="settledAmount" :label="t('已核销')" width="110" align="right" />
        <el-table-column :label="t('状态')" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="AR_INVOICE_STATUS_TAG[row.status] || 'info'" size="small">{{ t(AR_INVOICE_STATUS_LABEL[row.status] || '') }}</el-tag>
            <el-tag v-if="row.isCreditMemo" size="small" type="danger" effect="plain" style="margin-left:4px">{{ t('红字') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" v-permission="'fin-ar-invoice:post'" link type="primary" size="small" @click="doPost(row)">{{ t('过账') }}</el-button>
            <el-button v-if="row.status === 1 || row.status === 2" v-permission="'fin-ar-invoice:reverse'" link type="danger" size="small" @click="doReverse(row)">{{ t('红冲') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.isCreditMemo ? t('销售红字') : t('新建发票')" width="760">
      <el-form :model="form" label-width="100px" size="small">
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('客户')" required><el-input v-model="form.customerId" maxlength="20" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('币种')"><el-input v-model="form.currencyCd" maxlength="3" :placeholder="t('留空=本位币')" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('汇率')"><el-input-number v-model="form.fxRate" :min="0" :step="0.0001" :precision="6" style="width:100%" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('记账日期')" required><el-date-picker v-model="form.invoiceDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('到期日')" required><el-date-picker v-model="form.dueDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col v-if="form.isCreditMemo" :span="8"><el-form-item :label="t('退货成本')"><el-input-number v-model="form.costAmount" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
        </el-row>

        <div class="lines-head">
          <span>{{ t('明细行') }}</span>
          <el-button link type="primary" size="small" @click="addLine">{{ t('添加行') }}</el-button>
        </div>
        <el-table :data="form.lines" border size="small">
          <el-table-column :label="t('品目')" min-width="140">
            <template #default="{ row }"><el-input v-model="row.itemId" size="small" :placeholder="t('品目（可空）')" /></template>
          </el-table-column>
          <el-table-column :label="t('数量')" width="90">
            <template #default="{ row }"><el-input-number v-model="row.qty" :min="0" size="small" controls-position="right" style="width:100%" @change="calcLine(row)" /></template>
          </el-table-column>
          <el-table-column :label="t('单价')" width="110">
            <template #default="{ row }"><el-input-number v-model="row.unitPrice" :min="0" size="small" controls-position="right" style="width:100%" @change="calcLine(row)" /></template>
          </el-table-column>
          <el-table-column :label="t('金额')" width="110">
            <template #default="{ row }"><el-input-number v-model="row.amount" :min="0" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('税码')" width="150">
            <template #default="{ row }">
              <el-select v-model="row.taxCodeId" clearable size="small" style="width:100%" :placeholder="t('无税')">
                <el-option v-for="tc in taxCodes" :key="tc.id" :value="tc.id!" :label="`${tc.code} ${(tc.rate * 100).toFixed(0)}%`" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('操作')" width="60">
            <template #default="{ $index }"><el-button link type="danger" size="small" @click="form.lines.splice($index, 1)">{{ t('删') }}</el-button></template>
          </el-table-column>
        </el-table>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { arInvoiceApi, apMasterApi } from '@/api/fin/fin'
import {
  AR_INVOICE_STATUS_LABEL, AR_INVOICE_STATUS_TAG,
  type ArInvoice, type ArInvoiceLine, type TaxCode,
} from '@/types/fin/fin'

const { t } = useI18n()
const rows = ref<ArInvoice[]>([])
const taxCodes = ref<TaxCode[]>([])
const loading = ref(false)
const saving = ref(false)
const customerId = ref('')
const status = ref<number | undefined>(undefined)
const dialogVisible = ref(false)

function emptyForm(creditMemo: boolean): ArInvoice {
  const today = new Date().toISOString().slice(0, 10)
  return {
    customerId: '', invoiceDate: today, dueDate: today,
    currencyCd: '', fxRate: 1, costAmount: 0, isCreditMemo: creditMemo, lines: [],
  }
}
const form = reactive<ArInvoice>(emptyForm(false))

function calcLine(row: ArInvoiceLine) {
  row.amount = Math.round((row.qty || 0) * (row.unitPrice || 0) * 100) / 100
}
function addLine() {
  form.lines.push({ itemId: '', qty: 1, unitPrice: 0, amount: 0, taxCodeId: null })
}

async function reload() {
  loading.value = true
  try {
    const res = await arInvoiceApi.list(customerId.value || undefined, status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

async function openCreate(creditMemo: boolean) {
  Object.assign(form, emptyForm(creditMemo))
  form.lines = [{ itemId: '', qty: 1, unitPrice: 0, amount: 0, taxCodeId: null }]
  dialogVisible.value = true
  if (taxCodes.value.length === 0) {
    taxCodes.value = (await apMasterApi.taxCodes())?.data || []
  }
}

async function submit() {
  if (!form.customerId?.trim()) { ElMessage.warning(t('客户必填')); return }
  if (form.lines.length === 0 || form.lines.some(l => l.amount <= 0)) {
    ElMessage.warning(t('每行金额需大于0')); return
  }
  saving.value = true
  try {
    if (form.isCreditMemo) {
      await arInvoiceApi.creditMemo({
        creditNoteId: crypto.randomUUID(),
        customerId: form.customerId, invoiceDate: form.invoiceDate, dueDate: form.dueDate,
        currencyCd: form.currencyCd, fxRate: form.fxRate, estimatedCost: form.costAmount || 0,
        lines: form.lines.map(l => ({ itemId: l.itemId, qty: l.qty, unitPrice: l.unitPrice, taxCodeId: l.taxCodeId })),
      })
      ElMessage.success(t('已生成红字'))
    } else {
      await arInvoiceApi.create({ ...form })
      ElMessage.success(t('已新建'))
    }
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function doPost(row: ArInvoice) {
  if (!row.id) return
  await arInvoiceApi.post(row.id)
  ElMessage.success(t('已过账'))
  await reload()
}

async function doReverse(row: ArInvoice) {
  let reason = ''
  try {
    const r = await ElMessageBox.prompt(t('请输入红冲原因'), t('红冲发票'), { inputPattern: /.+/, inputErrorMessage: t('原因必填') })
    reason = r.value
  } catch { return }
  if (!row.id) return
  await arInvoiceApi.reverse(row.id, reason)
  ElMessage.success(t('已红冲'))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.ar-invoice { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
</style>
