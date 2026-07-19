<template>
  <div class="payment">
    <div class="page-header">
      <h2>{{ t('付款') }}</h2>
      <span class="subtitle">{{ t('付款/预付过账→核销应付（多对多+尾差+汇差）→撤销红冲') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="supplierId" size="small" style="width: 150px" :placeholder="t('供应商')" clearable @change="reload" />
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in PAYMENT_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button v-permission="'fin-ap-payment:add'" type="primary" size="small" @click="openPay">{{ t('新建付款') }}</el-button>
        <el-button size="small" @click="bankDialog = true">{{ t('银行账户') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="no" :label="t('付款单号')" width="150" />
        <el-table-column prop="supplierId" :label="t('供应商')" width="110" />
        <el-table-column :label="t('付款日期')" width="110"><template #default="{ row }">{{ (row.payDate || '').slice(0, 10) }}</template></el-table-column>
        <el-table-column :label="t('币种')" width="70"><template #default="{ row }">{{ row.currencyCd || 'JPY' }}</template></el-table-column>
        <el-table-column prop="amount" :label="t('金额')" width="120" align="right" />
        <el-table-column prop="settledAmount" :label="t('已核销')" width="110" align="right" />
        <el-table-column :label="t('预付')" width="60" align="center"><template #default="{ row }"><el-tag v-if="row.isPrepayment" size="small" type="warning">{{ t('是') }}</el-tag></template></el-table-column>
        <el-table-column :label="t('状态')" width="90" align="center">
          <template #default="{ row }"><el-tag :type="PAYMENT_STATUS_TAG[row.status] || 'info'" size="small">{{ t(PAYMENT_STATUS_LABEL[row.status] || '') }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="150" fixed="right">
          <template #default="{ row }">
            <template v-if="row.status === 1">
            <el-button v-permission="'fin-ap-payment:settle'" link type="primary" size="small" @click="openSettle(row)">{{ t('核销') }}</el-button>
            <el-button v-permission="'fin-ap-payment:reverse'" link type="danger" size="small" @click="doReverse(row)">{{ t('撤销') }}</el-button>
            </template>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 付款 -->
    <el-dialog v-model="payDialog" :title="t('新建付款')" width="560">
      <el-form :model="form" label-width="100px" size="small">
        <el-form-item :label="t('供应商')" required><el-input v-model="form.supplierId" maxlength="20" /></el-form-item>
        <el-form-item :label="t('付款日期')" required><el-date-picker v-model="form.payDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item>
        <el-form-item :label="t('金额')" required><el-input-number v-model="form.amount" :min="0" controls-position="right" style="width:100%" /></el-form-item>
        <el-form-item :label="t('币种')"><el-input v-model="form.currencyCd" maxlength="3" :placeholder="t('留空=本位币')" /></el-form-item>
        <el-form-item :label="t('汇率')"><el-input-number v-model="form.fxRate" :min="0" :step="0.0001" :precision="6" style="width:100%" /></el-form-item>
        <el-form-item :label="t('付款方式')"><el-select v-model="form.method" style="width:100%"><el-option v-for="o in PAYMENT_METHOD_OPTIONS" :key="o.value" :value="o.value" :label="t(o.label)" /></el-select></el-form-item>
        <el-form-item :label="t('银行账户')" required>
          <el-select v-model="form.bankAccountId" style="width:100%" :placeholder="t('选择银行账户')">
            <el-option v-for="b in banks" :key="b.id" :value="b.id!" :label="`${b.code} ${b.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('预付款')"><el-switch v-model="form.isPrepayment" /><span class="hint">{{ t('预付走预付账款，不冲应付') }}</span></el-form-item>
      </el-form>
      <template #footer><el-button @click="payDialog = false" :disabled="saving">{{ t('取消') }}</el-button><el-button type="primary" :loading="saving" @click="submitPay">{{ t('确定') }}</el-button></template>
    </el-dialog>

    <!-- 核销 -->
    <el-dialog v-model="settleDialog" :title="t('核销')" width="720">
      <p class="hint">{{ t('付款单 {no} 可用 {amt}', { no: settlePayment?.no, amt: (settlePayment?.amount || 0) - (settlePayment?.settledAmount || 0) }) }}</p>
      <el-table :data="openInvoices" border size="small" max-height="360">
        <el-table-column prop="no" :label="t('发票号')" width="150" />
        <el-table-column :label="t('欠款')" width="120" align="right"><template #default="{ row }">{{ (row.grossAmount || 0) - (row.settledAmount || 0) }}</template></el-table-column>
        <el-table-column :label="t('本次核销')" width="150">
          <template #default="{ row }"><el-input-number v-model="applyMap[row.id]" :min="0" size="small" controls-position="right" style="width:100%" /></template>
        </el-table-column>
        <el-table-column :label="t('折扣')" width="120">
          <template #default="{ row }"><el-input-number v-model="discountMap[row.id]" :min="0" size="small" controls-position="right" style="width:100%" /></template>
        </el-table-column>
      </el-table>
      <el-form-item :label="t('折扣科目')" label-width="90px" style="margin-top:8px">
        <el-select v-model="discountAccountId" clearable filterable size="small" style="width:320px" :placeholder="t('有折扣时必填')">
          <el-option v-for="a in leafAccounts" :key="a.id" :value="a.id!" :label="`${a.code} ${a.name}`" />
        </el-select>
      </el-form-item>
      <template #footer><el-button @click="settleDialog = false" :disabled="saving">{{ t('取消') }}</el-button><el-button type="primary" :loading="saving" @click="submitSettle">{{ t('确定') }}</el-button></template>
    </el-dialog>

    <!-- 银行账户 -->
    <el-dialog v-model="bankDialog" :title="t('银行账户')" width="560">
      <el-form :model="bankForm" label-width="100px" size="small" inline>
        <el-form-item :label="t('编码')"><el-input v-model="bankForm.code" maxlength="20" style="width:120px" /></el-form-item>
        <el-form-item :label="t('名称')"><el-input v-model="bankForm.name" maxlength="100" style="width:150px" /></el-form-item>
        <el-form-item :label="t('银行存款科目')">
          <el-select v-model="bankForm.glAccountId" filterable size="small" style="width:220px" :placeholder="t('选择科目')">
            <el-option v-for="a in leafAccounts" :key="a.id" :value="a.id!" :label="`${a.code} ${a.name}`" />
          </el-select>
        </el-form-item>
        <el-button v-permission="'fin-ap-payment:bank'" type="primary" size="small" @click="addBank">{{ t('新建') }}</el-button>
      </el-form>
      <el-table :data="banks" border size="small">
        <el-table-column prop="code" :label="t('编码')" width="120" />
        <el-table-column prop="name" :label="t('名称')" />
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { paymentApi, apInvoiceApi, apMasterApi, glAccountApi } from '@/api/fin/fin'
import {
  PAYMENT_STATUS_LABEL, PAYMENT_STATUS_TAG, PAYMENT_METHOD_OPTIONS,
  type Payment, type ApInvoice, type BankAccount, type GlAccount, type SettlementApply,
} from '@/types/fin/fin'

const { t } = useI18n()
const rows = ref<Payment[]>([])
const banks = ref<BankAccount[]>([])
const leafAccounts = ref<GlAccount[]>([])
const loading = ref(false)
const saving = ref(false)
const supplierId = ref('')
const status = ref<number | undefined>(undefined)

const payDialog = ref(false)
const bankDialog = ref(false)
const settleDialog = ref(false)

function emptyForm(): Payment {
  return { supplierId: '', payDate: new Date().toISOString().slice(0, 10), amount: 0, currencyCd: '', fxRate: 1, method: 1, bankAccountId: '', isPrepayment: false }
}
const form = reactive<Payment>(emptyForm())
const bankForm = reactive<BankAccount>({ code: '', name: '', glAccountId: '' })

const settlePayment = ref<Payment | null>(null)
const openInvoices = ref<ApInvoice[]>([])
const applyMap = reactive<Record<string, number>>({})
const discountMap = reactive<Record<string, number>>({})
const discountAccountId = ref<string | null>(null)

async function reload() {
  loading.value = true
  try {
    rows.value = (await paymentApi.list(supplierId.value || undefined, status.value))?.data || []
  } finally {
    loading.value = false
  }
}

async function ensureRefData() {
  if (banks.value.length === 0) banks.value = (await apMasterApi.bankAccounts())?.data || []
  if (leafAccounts.value.length === 0) {
    const acc = await glAccountApi.list('CN-GAAP', false)
    leafAccounts.value = (acc?.data || []).filter(a => a.isLeaf && a.isActive)
  }
}

async function openPay() {
  Object.assign(form, emptyForm())
  await ensureRefData()
  payDialog.value = true
}

async function submitPay() {
  if (!form.supplierId?.trim() || !form.bankAccountId || form.amount <= 0) { ElMessage.warning(t('供应商/银行账户/金额必填')); return }
  saving.value = true
  try {
    await paymentApi.pay({ ...form })
    ElMessage.success(t('已付款'))
    payDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function doReverse(row: Payment) {
  let reason = ''
  try {
    const r = await ElMessageBox.prompt(t('请输入撤销原因'), t('撤销付款'), { inputPattern: /.+/, inputErrorMessage: t('原因必填') })
    reason = r.value
  } catch { return }
  if (!row.id) return
  await paymentApi.reverse(row.id, reason)
  ElMessage.success(t('已撤销'))
  await reload()
}

async function openSettle(row: Payment) {
  settlePayment.value = row
  await ensureRefData()
  const list = (await apInvoiceApi.list(row.supplierId))?.data || []
  openInvoices.value = list.filter(i => (i.status === 1 || i.status === 2) && !i.isCreditMemo)
  Object.keys(applyMap).forEach(k => delete applyMap[k])
  Object.keys(discountMap).forEach(k => delete discountMap[k])
  discountAccountId.value = null
  settleDialog.value = true
}

async function submitSettle() {
  if (!settlePayment.value?.id) return
  const applies: SettlementApply[] = openInvoices.value
    .filter(i => (applyMap[i.id!] || 0) > 0 || (discountMap[i.id!] || 0) > 0)
    .map(i => ({ invoiceId: i.id!, appliedAmount: applyMap[i.id!] || 0, discountAmount: discountMap[i.id!] || 0, discountAccountId: discountAccountId.value }))
  if (applies.length === 0) { ElMessage.warning(t('请填写核销金额')); return }
  saving.value = true
  try {
    await paymentApi.settle(settlePayment.value.id, applies)
    ElMessage.success(t('已核销'))
    settleDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function addBank() {
  if (!bankForm.code?.trim() || !bankForm.name?.trim() || !bankForm.glAccountId) { ElMessage.warning(t('编码/名称/科目必填')); return }
  await apMasterApi.createBankAccount({ ...bankForm })
  ElMessage.success(t('已新建'))
  bankForm.code = ''; bankForm.name = ''; bankForm.glAccountId = ''
  banks.value = (await apMasterApi.bankAccounts())?.data || []
}

onMounted(async () => { await reload() })
</script>

<style scoped>
.payment { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.hint { color: #909399; font-size: 12px; margin-left: 8px; }
</style>
