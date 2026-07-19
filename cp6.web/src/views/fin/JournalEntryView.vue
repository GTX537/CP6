<template>
  <div class="journal-entry">
    <div class="page-header">
      <h2>{{ t('记账凭证') }}</h2>
      <span class="subtitle">{{ t('借贷恒等 + 双人复核 + 红冲（不可改不可删）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="statusFilter" size="small" clearable :placeholder="t('全部状态')" style="width: 130px" @change="reload">
          <el-option v-for="(lbl, k) in JOURNAL_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-button v-permission="'fin-journal:add'" type="primary" size="small" @click="openCreate">{{ t('新建凭证') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="no" :label="t('凭证号')" width="170" />
        <el-table-column :label="t('记账日期')" width="120">
          <template #default="{ row }">{{ fmtDate(row.voucherDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('来源')" width="80">
          <template #default="{ row }">{{ t(VOUCHER_SOURCE_LABEL[row.source] || '') }}</template>
        </el-table-column>
        <el-table-column prop="description" :label="t('摘要')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('金额')" width="120" align="right">
          <template #default="{ row }">{{ fmtAmt(sumDebit(row)) }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="JOURNAL_STATUS_TAG[row.status]" size="small">{{ t(JOURNAL_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="makerId" :label="t('制单人')" width="110" show-overflow-tooltip />
        <el-table-column :label="t('操作')" width="230" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="view(row)">{{ t('查看') }}</el-button>
            <el-button v-if="row.status === 0" v-permission="'fin-journal:submit'" link type="success" size="small" @click="act(row, 'submit')">{{ t('提交') }}</el-button>
            <el-button v-if="row.status === 1" v-permission="'fin-journal:post'" link type="success" size="small" @click="act(row, 'post')">{{ t('过账') }}</el-button>
            <el-button v-if="row.status === 1" v-permission="'fin-journal:reject'" link type="warning" size="small" @click="reject(row)">{{ t('驳回') }}</el-button>
            <el-button v-if="row.status === 2" v-permission="'fin-journal:reverse'" link type="danger" size="small" @click="reverse(row)">{{ t('红冲') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建凭证 -->
    <el-dialog v-model="createVisible" :title="t('新建凭证')" width="820" top="6vh">
      <el-form :model="form" label-width="80px" inline>
        <el-form-item :label="t('记账日期')" required>
          <el-date-picker v-model="form.voucherDate" type="date" value-format="YYYY-MM-DD" />
        </el-form-item>
        <el-form-item :label="t('摘要')">
          <el-input v-model="form.description" style="width: 380px" maxlength="200" />
        </el-form-item>
      </el-form>

      <el-table :data="form.lines" border size="small" class="line-table">
        <el-table-column :label="t('科目')" min-width="220">
          <template #default="{ row }">
            <el-select v-model="row.accountId" filterable size="small" style="width: 100%" :placeholder="t('选择科目')">
              <el-option v-for="a in leafAccounts" :key="a.id" :value="a.id!" :label="`${a.code} ${a.name}`" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column :label="t('借方')" width="150">
          <template #default="{ row }">
            <el-input-number v-model="row.debit" :min="0" :precision="2" :controls="false" size="small" style="width: 100%" @change="row.credit = 0" />
          </template>
        </el-table-column>
        <el-table-column :label="t('贷方')" width="150">
          <template #default="{ row }">
            <el-input-number v-model="row.credit" :min="0" :precision="2" :controls="false" size="small" style="width: 100%" @change="row.debit = 0" />
          </template>
        </el-table-column>
        <el-table-column :label="t('往来单位')" width="130">
          <template #default="{ row }">
            <el-input v-model="row.partnerId" size="small" :placeholder="t('应收应付必填')" />
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="70" align="center">
          <template #default="{ $index }">
            <el-button link type="danger" size="small" @click="form.lines.splice($index, 1)">{{ t('删') }}</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="line-foot">
        <el-button size="small" @click="addLine">{{ t('添加分录行') }}</el-button>
        <span class="totals" :class="{ unbalanced: !balanced }">
          {{ t('借合计') }}: {{ fmtAmt(totalDebit) }} / {{ t('贷合计') }}: {{ fmtAmt(totalCredit) }}
          <el-tag :type="balanced ? 'success' : 'danger'" size="small">{{ balanced ? t('平') : t('不平') }}</el-tag>
        </span>
      </div>

      <template #footer>
        <el-button @click="createVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="saveDraft">{{ t('保存草稿') }}</el-button>
      </template>
    </el-dialog>

    <!-- 查看凭证 -->
    <el-dialog v-model="viewVisible" :title="t('凭证详情')" width="760" top="6vh">
      <el-descriptions v-if="current" :column="2" border size="small">
        <el-descriptions-item :label="t('凭证号')">{{ current.no }}</el-descriptions-item>
        <el-descriptions-item :label="t('状态')">{{ t(JOURNAL_STATUS_LABEL[current.status] || '') }}</el-descriptions-item>
        <el-descriptions-item :label="t('记账日期')">{{ fmtDate(current.voucherDate) }}</el-descriptions-item>
        <el-descriptions-item :label="t('来源')">{{ t(VOUCHER_SOURCE_LABEL[current.source] || '') }}</el-descriptions-item>
        <el-descriptions-item :label="t('制单人')">{{ current.makerId }}</el-descriptions-item>
        <el-descriptions-item :label="t('过账人')">{{ current.checkerId || '-' }}</el-descriptions-item>
        <el-descriptions-item :label="t('摘要')" :span="2">{{ current.description }}</el-descriptions-item>
        <el-descriptions-item v-if="current.rejectReason" :label="t('驳回原因')" :span="2">{{ current.rejectReason }}</el-descriptions-item>
      </el-descriptions>
      <el-table v-if="current" :data="current.lines" border size="small" class="line-table">
        <el-table-column :label="t('科目')" min-width="200">
          <template #default="{ row }">{{ accountLabel(row.accountId) }}</template>
        </el-table-column>
        <el-table-column :label="t('借方')" width="130" align="right"><template #default="{ row }">{{ row.debit ? fmtAmt(row.debit) : '' }}</template></el-table-column>
        <el-table-column :label="t('贷方')" width="130" align="right"><template #default="{ row }">{{ row.credit ? fmtAmt(row.credit) : '' }}</template></el-table-column>
        <el-table-column prop="partnerId" :label="t('往来单位')" width="120" />
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { glAccountApi, journalApi } from '@/api/fin/fin'
import {
  JOURNAL_STATUS_LABEL, JOURNAL_STATUS_TAG, VOUCHER_SOURCE_LABEL,
  type GlAccount, type JournalEntry, type JournalLine,
} from '@/types/fin/fin'

const { t } = useI18n()

const rows = ref<JournalEntry[]>([])
const leafAccounts = ref<GlAccount[]>([])
const accountMap = ref<Record<string, GlAccount>>({})
const loading = ref(false)
const saving = ref(false)
const statusFilter = ref<number | undefined>(undefined)

const createVisible = ref(false)
const viewVisible = ref(false)
const current = ref<JournalEntry | null>(null)

function emptyLine(): JournalLine { return { accountId: '', debit: 0, credit: 0, partnerId: null } }
function emptyForm(): JournalEntry {
  return { voucherDate: today(), source: 0, status: 0, description: '', lines: [emptyLine(), emptyLine()] }
}
const form = reactive<JournalEntry>(emptyForm())

const totalDebit = computed(() => form.lines.reduce((s, l) => s + (l.debit || 0), 0))
const totalCredit = computed(() => form.lines.reduce((s, l) => s + (l.credit || 0), 0))
const balanced = computed(() => totalDebit.value > 0 && Math.abs(totalDebit.value - totalCredit.value) < 0.005)

async function reload() {
  loading.value = true
  try {
    const res = await journalApi.list(undefined, statusFilter.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

async function loadAccounts() {
  const res = await glAccountApi.list(undefined, false)
  const all = res?.data || []
  leafAccounts.value = all.filter(a => a.isLeaf)
  accountMap.value = Object.fromEntries(all.map(a => [a.id!, a]))
}

function openCreate() {
  Object.assign(form, emptyForm())
  createVisible.value = true
}
function addLine() { form.lines.push(emptyLine()) }

async function saveDraft() {
  const valid = form.lines.filter(l => l.accountId && (l.debit > 0 || l.credit > 0))
  if (valid.length < 2) { ElMessage.warning(t('凭证至少要有借贷两行')); return }
  saving.value = true
  try {
    await journalApi.create({ ...form, lines: valid })
    ElMessage.success(t('已保存草稿'))
    createVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function act(row: JournalEntry, kind: 'submit' | 'post') {
  if (!row.id) return
  if (kind === 'submit') await journalApi.submit(row.id)
  else await journalApi.post(row.id)
  ElMessage.success(kind === 'submit' ? t('已提交复核') : t('已过账'))
  await reload()
}

async function reject(row: JournalEntry) {
  const r = await ElMessageBox.prompt(t('请输入驳回原因'), t('驳回'), { inputValidator: v => !!v || t('原因必填') }).catch(() => null)
  if (!r || !row.id) return
  await journalApi.reject(row.id, r.value)
  ElMessage.success(t('已驳回'))
  await reload()
}

async function reverse(row: JournalEntry) {
  const r = await ElMessageBox.prompt(t('请输入红冲原因'), t('红冲'), { inputValidator: v => !!v || t('原因必填') }).catch(() => null)
  if (!r || !row.id) return
  await journalApi.reverse(row.id, r.value)
  ElMessage.success(t('已红冲'))
  await reload()
}

async function view(row: JournalEntry) {
  if (!row.id) return
  const res = await journalApi.get(row.id)
  current.value = res?.data || null
  viewVisible.value = true
}

function sumDebit(e: JournalEntry): number { return (e.lines || []).reduce((s, l) => s + (l.debit || 0), 0) }
function accountLabel(id: string): string { const a = accountMap.value[id]; return a ? `${a.code} ${a.name}` : id }
function fmtAmt(v: number): string { return (v || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
function fmtDate(v?: string): string { return v ? v.slice(0, 10) : '' }
function today(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

onMounted(async () => { await loadAccounts(); await reload() })
</script>

<style scoped>
.journal-entry { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
.line-table { margin-top: 8px; }
.line-foot { margin-top: 10px; display: flex; justify-content: space-between; align-items: center; }
.totals { font-size: 13px; color: #303133; display: flex; gap: 8px; align-items: center; }
.totals.unbalanced { color: #f56c6c; }
</style>
