<template>
  <div class="ap-invoice">
    <div class="page-header">
      <h2>{{ t('应付发票') }}</h2>
      <span class="subtitle">{{ t('录入草稿→过账自动生成凭证（借费用+进项税/贷应付）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="supplierId" size="small" style="width: 150px" :placeholder="t('供应商')" clearable @change="reload" />
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in AP_INVOICE_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button type="primary" size="small" @click="openCreate(false)">{{ t('新建发票') }}</el-button>
        <el-button size="small" @click="openCreate(true)">{{ t('供应商红字') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="no" :label="t('发票号')" width="150" />
        <el-table-column prop="supplierId" :label="t('供应商')" width="110" />
        <el-table-column prop="supplierInvoiceNo" :label="t('供方发票号')" width="120" show-overflow-tooltip />
        <el-table-column :label="t('记账日期')" width="110">
          <template #default="{ row }">{{ (row.invoiceDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('币种')" width="70">
          <template #default="{ row }">{{ row.currencyCd || 'JPY' }}</template>
        </el-table-column>
        <el-table-column prop="grossAmount" :label="t('价税合计')" width="120" align="right" />
        <el-table-column prop="settledAmount" :label="t('已核销')" width="110" align="right" />
        <el-table-column :label="t('状态')" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="AP_INVOICE_STATUS_TAG[row.status] || 'info'" size="small">{{ t(AP_INVOICE_STATUS_LABEL[row.status] || '') }}</el-tag>
            <el-tag v-if="row.isCreditMemo" size="small" type="danger" effect="plain" style="margin-left:4px">{{ t('红字') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="110" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="doPost(row)">{{ t('过账') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.isCreditMemo ? t('供应商红字') : t('新建发票')" width="760">
      <el-form :model="form" label-width="100px" size="small">
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('供应商')" required><el-input v-model="form.supplierId" maxlength="20" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('供方发票号')"><el-input v-model="form.supplierInvoiceNo" maxlength="50" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('币种')"><el-input v-model="form.currencyCd" maxlength="3" :placeholder="t('留空=本位币')" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('记账日期')" required><el-date-picker v-model="form.invoiceDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('到期日')" required><el-date-picker v-model="form.dueDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('汇率')"><el-input-number v-model="form.fxRate" :min="0" :step="0.0001" :precision="6" style="width:100%" /></el-form-item></el-col>
        </el-row>

        <div class="lines-head">
          <span>{{ t('明细行') }}</span>
          <el-button link type="primary" size="small" @click="addLine">{{ t('添加行') }}</el-button>
        </div>
        <el-table :data="form.lines" border size="small">
          <el-table-column :label="t('费用/资产科目')" min-width="180">
            <template #default="{ row }">
              <el-select v-model="row.expenseAccountId" filterable size="small" style="width:100%" :placeholder="t('选择科目')">
                <el-option v-for="a in leafAccounts" :key="a.id" :value="a.id!" :label="`${a.code} ${a.name}`" />
              </el-select>
            </template>
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
import { ElMessage } from 'element-plus'
import { apInvoiceApi, glAccountApi, apMasterApi } from '@/api/fin/fin'
import {
  AP_INVOICE_STATUS_LABEL, AP_INVOICE_STATUS_TAG,
  type ApInvoice, type ApInvoiceLine, type GlAccount, type TaxCode,
} from '@/types/fin/fin'

const { t } = useI18n()
const rows = ref<ApInvoice[]>([])
const leafAccounts = ref<GlAccount[]>([])
const taxCodes = ref<TaxCode[]>([])
const loading = ref(false)
const saving = ref(false)
const supplierId = ref('')
const status = ref<number | undefined>(undefined)
const dialogVisible = ref(false)

function emptyForm(creditMemo: boolean): ApInvoice {
  const today = new Date().toISOString().slice(0, 10)
  return {
    supplierId: '', supplierInvoiceNo: '', invoiceDate: today, dueDate: today,
    currencyCd: '', fxRate: 1, isCreditMemo: creditMemo, lines: [],
  }
}
const form = reactive<ApInvoice>(emptyForm(false))

function calcLine(row: ApInvoiceLine) {
  row.amount = Math.round((row.qty || 0) * (row.unitPrice || 0) * 100) / 100
}
function addLine() {
  form.lines.push({ expenseAccountId: '', qty: 1, unitPrice: 0, amount: 0, taxCodeId: null })
}

async function reload() {
  loading.value = true
  try {
    const res = await apInvoiceApi.list(supplierId.value || undefined, status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

async function openCreate(creditMemo: boolean) {
  Object.assign(form, emptyForm(creditMemo))
  form.lines = [{ expenseAccountId: '', qty: 1, unitPrice: 0, amount: 0, taxCodeId: null }]
  dialogVisible.value = true
  if (leafAccounts.value.length === 0) {
    const [acc, tax] = await Promise.all([glAccountApi.list('CN-GAAP', false), apMasterApi.taxCodes()])
    leafAccounts.value = (acc?.data || []).filter(a => a.isLeaf && a.isActive)
    taxCodes.value = tax?.data || []
  }
}

async function submit() {
  if (!form.supplierId?.trim()) { ElMessage.warning(t('供应商必填')); return }
  if (form.lines.length === 0 || form.lines.some(l => !l.expenseAccountId || l.amount <= 0)) {
    ElMessage.warning(t('每行需选科目且金额大于0')); return
  }
  saving.value = true
  try {
    await apInvoiceApi.create({ ...form })
    ElMessage.success(t('已新建'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function doPost(row: ApInvoice) {
  if (!row.id) return
  await apInvoiceApi.post(row.id)
  ElMessage.success(t('已过账'))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.ap-invoice { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
</style>
