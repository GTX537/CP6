<template>
  <div class="bank-recon">
    <div class="page-header">
      <h2>{{ t('bankrecon.workbench') }}</h2>
      <span class="subtitle">{{ statement ? statement.no : t('bankrecon.workbench.subtitle') }}</span>
    </div>

    <el-card shadow="never" v-loading="loading">
      <div class="table-toolbar">
        <el-button type="primary" size="small" @click="doAutoMatch">{{ t('bankrecon.btn.autoMatch') }}</el-button>
        <el-button size="small" :disabled="!selectedLines.length || !selectedCands.length" @click="doManualMatch">
          {{ t('bankrecon.btn.manualMatch') }}
        </el-button>
        <el-button size="small" :disabled="!selectedLines.length" @click="genVoucherVisible = true">
          {{ t('bankrecon.btn.genVoucher') }}
        </el-button>
        <el-button size="small" :disabled="!selectedLines.length" @click="doMarkPending">
          {{ t('bankrecon.btn.markPending') }}
        </el-button>
        <el-button v-if="statement?.status === 0" type="warning" size="small" @click="doPreLock">
          {{ t('bankrecon.btn.lock') }}
        </el-button>
        <el-button v-if="statement?.status === 1" size="small" @click="doUnlock">
          {{ t('bankrecon.btn.unlock') }}
        </el-button>
        <el-button size="small" @click="loadAll">{{ t('刷新') }}</el-button>
      </div>

      <el-row :gutter="12">
        <!-- 左：银行流水 -->
        <el-col :span="12">
          <div class="panel-title">{{ t('bankrecon.panel.bankLines') }}</div>
          <el-table :data="lines" border stripe size="small" max-height="400" v-loading="linesLoading"
            @selection-change="(v: BankStatementLine[]) => selectedLines = v"
            @current-change="onPickLine" highlight-current-row>
            <el-table-column type="selection" width="40" />
            <el-table-column prop="txnDate" :label="t('bankrecon.field.txnDate')" width="100">
              <template #default="{ row }">{{ (row.txnDate || '').slice(0, 10) }}</template>
            </el-table-column>
            <el-table-column :label="t('bankrecon.field.direction')" width="70" align="center">
              <template #default="{ row }">
                <el-tag :type="row.direction === 1 ? 'success' : 'danger'" size="small">
                  {{ row.direction === 1 ? t('bankrecon.direction.deposit') : t('bankrecon.direction.withdrawal') }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="amount" :label="t('bankrecon.field.amount')" width="110" align="right" />
            <el-table-column prop="description" :label="t('bankrecon.field.description')" min-width="100" show-overflow-tooltip />
            <el-table-column :label="t('bankrecon.field.matchStatus')" width="90" align="center">
              <template #default="{ row }">
                <el-tag :type="MATCH_STATUS_TAG[row.matchStatus] || 'info'" size="small">
                  {{ t(MATCH_STATUS_I18N[row.matchStatus] || 'bankrecon.matchStatus.unmatched') }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </el-col>

        <!-- 右：候选凭证行 -->
        <el-col :span="12">
          <div class="panel-title">
            {{ t('bankrecon.panel.candidates') }}
            <el-checkbox v-model="widenSearch" size="small" style="margin-left:8px" @change="onPickLine(currentLine)">
              {{ t('bankrecon.btn.widen') }}
            </el-checkbox>
          </div>
          <el-table :data="candidates" border stripe size="small" max-height="400" v-loading="candsLoading"
            @selection-change="(v: BankCandidateLine[]) => selectedCands = v">
            <el-table-column type="selection" width="40" />
            <el-table-column prop="entryNo" :label="t('bankrecon.field.entryNo')" width="140" />
            <el-table-column prop="voucherDate" :label="t('bankrecon.field.voucherDate')" width="100">
              <template #default="{ row }">{{ (row.voucherDate || '').slice(0, 10) }}</template>
            </el-table-column>
            <el-table-column prop="bankSignedAmount" :label="t('bankrecon.field.amount')" width="110" align="right" />
            <el-table-column prop="memo" :label="t('bankrecon.field.memo')" min-width="120" show-overflow-tooltip />
          </el-table>
        </el-col>
      </el-row>

      <!-- 调节表面板 -->
      <el-descriptions v-if="recon" :title="t('bankrecon.reconStatement')" border :column="2" size="small" style="margin-top:16px">
        <el-descriptions-item :label="t('bankrecon.field.opening')">{{ recon.openingBalance }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.closing')">{{ recon.closingBalance }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.totalDeposit')">{{ recon.totalDeposit }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.totalWithdrawal')">{{ recon.totalWithdrawal }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.glBankEndingBalance')">{{ recon.glBankEndingBalance }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.internalDiff')">
          <span :style="{ color: recon.statementInternalDiff === 0 ? '#67c23a' : '#f56c6c', fontWeight: 600 }">
            {{ recon.statementInternalDiff }}
          </span>
        </el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.bankAdjusted')">{{ recon.bankAdjustedBalance }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.bookAdjusted')">{{ recon.bookAdjustedBalance }}</el-descriptions-item>
        <el-descriptions-item :label="t('bankrecon.field.reconciledDiff')" :span="2">
          <span :style="{ color: recon.reconciledDiff === 0 ? '#67c23a' : '#f56c6c', fontWeight: 700, fontSize: '14px' }">
            {{ recon.reconciledDiff }}
          </span>
        </el-descriptions-item>
      </el-descriptions>
    </el-card>

    <!-- 生成凭证对话框 -->
    <el-dialog v-model="genVoucherVisible" :title="t('bankrecon.btn.genVoucher')" width="480px">
      <el-form :model="genForm" label-width="120px" size="small">
        <el-form-item :label="t('bankrecon.field.counterAccount')">
          <el-input v-model="genForm.counterAccountId" :placeholder="t('bankrecon.field.counterAccountHint')" />
        </el-form-item>
        <el-form-item :label="t('bankrecon.field.partner')">
          <el-input v-model="genForm.partnerId" :placeholder="t('bankrecon.field.partnerHint')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="genVoucherVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="genVoucherLoading" @click="doGenVoucher">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- 未达 unmatch 确认对话框 -->
    <el-dialog v-model="unmatchVisible" :title="t('bankrecon.btn.unmatch')" width="360px">
      <p>{{ t('bankrecon.msg.unmatchConfirm') }}</p>
      <template #footer>
        <el-button @click="unmatchVisible = false">{{ t('取消') }}</el-button>
        <el-button type="danger" @click="doUnmatch">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { bankStatementApi, bankReconApi } from '@/api/fin/bankRecon'
import type { BankStatement, BankStatementLine, BankCandidateLine, ReconciliationStatement } from '@/types/fin/bankRecon'

const { t } = useI18n()
const route = useRoute()

const statementId = ref((route.query.id as string) || '')
const statement = ref<BankStatement | null>(null)
const lines = ref<BankStatementLine[]>([])
const candidates = ref<BankCandidateLine[]>([])
const selectedLines = ref<BankStatementLine[]>([])
const selectedCands = ref<BankCandidateLine[]>([])
const recon = ref<ReconciliationStatement | null>(null)
const loading = ref(false)
const linesLoading = ref(false)
const candsLoading = ref(false)
const widenSearch = ref(false)
const currentLine = ref<BankStatementLine | null>(null)

// gen voucher
const genVoucherVisible = ref(false)
const genVoucherLoading = ref(false)
const genForm = reactive({ counterAccountId: '', partnerId: '' })

// unmatch
const unmatchVisible = ref(false)
const unmatchGroupId = ref('')

const MATCH_STATUS_TAG: Record<number, '' | 'success' | 'info' | 'warning' | 'danger'> = {
  0: 'info', 1: 'success', 2: 'warning', 3: 'warning', 4: 'warning',
}
const MATCH_STATUS_I18N: Record<number, string> = {
  0: 'bankrecon.matchStatus.unmatched',
  1: 'bankrecon.matchStatus.matched',
  2: 'bankrecon.matchStatus.pending',
  3: 'bankrecon.matchStatus.pending',
  4: 'bankrecon.matchStatus.pending',
}

async function loadAll() {
  if (!statementId.value) return
  loading.value = true
  try {
    const [stmtRes, reconRes] = await Promise.all([
      bankStatementApi.get(statementId.value),
      bankReconApi.reconStatement(statementId.value),
    ])
    statement.value = stmtRes?.data?.statement ?? null
    lines.value = stmtRes?.data?.lines ?? []
    recon.value = reconRes?.data ?? null
    candidates.value = []
    currentLine.value = null
  } finally {
    loading.value = false
  }
}

async function onPickLine(row: BankStatementLine | null) {
  currentLine.value = row
  if (!row || !statementId.value) { candidates.value = []; return }
  candsLoading.value = true
  try {
    const r = await bankReconApi.candidates(statementId.value, row.id!, widenSearch.value)
    candidates.value = r?.data ?? []
  } finally {
    candsLoading.value = false
  }
}

async function doAutoMatch() {
  if (!statementId.value) return
  linesLoading.value = true
  try {
    const r = await bankReconApi.autoMatch(statementId.value)
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.autoMatchDone'))
      await loadAll()
    } else {
      ElMessage.error(r.message)
    }
  } finally {
    linesLoading.value = false
  }
}

async function doManualMatch() {
  if (!selectedLines.value.length || !selectedCands.value.length) return
  const rowVersion = selectedLines.value[0]?.rowVersion ?? undefined
  try {
    const r = await bankReconApi.manualMatch(
      statementId.value,
      selectedLines.value.map(l => l.id!),
      selectedCands.value.map(c => c.journalLineId),
      rowVersion,
    )
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.matched'))
      await loadAll()
    } else if (r.message === 'E-A4-CONCURRENCY-001') {
      await ElMessageBox.alert(t('bankrecon.msg.concurrencyRefresh'), t('bankrecon.msg.concurrencyTitle'), { type: 'warning' })
      await loadAll()
    } else {
      ElMessage.error(r.message)
    }
  } catch (e: any) {
    const code = e?.response?.data?.message ?? e?.message
    if (code === 'E-A4-CONCURRENCY-001') {
      await ElMessageBox.alert(t('bankrecon.msg.concurrencyRefresh'), t('bankrecon.msg.concurrencyTitle'), { type: 'warning' })
      await loadAll()
    } else {
      ElMessage.error(code || t('bankrecon.msg.matchFailed'))
    }
  }
}

async function doMarkPending() {
  if (!selectedLines.value.length) return
  const rowVersion = selectedLines.value[0]?.rowVersion ?? undefined
  try {
    const r = await bankReconApi.markPending(statementId.value, selectedLines.value.map(l => l.id!), 4, rowVersion)
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.markedPending'))
      await loadAll()
    } else if (r.message === 'E-A4-CONCURRENCY-001') {
      await ElMessageBox.alert(t('bankrecon.msg.concurrencyRefresh'), t('bankrecon.msg.concurrencyTitle'), { type: 'warning' })
      await loadAll()
    } else {
      ElMessage.error(r.message)
    }
  } catch (e: any) {
    const code = e?.response?.data?.message ?? e?.message
    if (code === 'E-A4-CONCURRENCY-001') {
      await ElMessageBox.alert(t('bankrecon.msg.concurrencyRefresh'), t('bankrecon.msg.concurrencyTitle'), { type: 'warning' })
      await loadAll()
    } else {
      ElMessage.error(code || t('bankrecon.msg.markFailed'))
    }
  }
}

async function doGenVoucher() {
  if (!selectedLines.value.length || !genForm.counterAccountId.trim()) {
    ElMessage.warning(t('bankrecon.field.counterAccount') + t('必填'))
    return
  }
  genVoucherLoading.value = true
  try {
    const res = await bankReconApi.generateVoucher(
      statementId.value,
      selectedLines.value.map(l => l.id!),
      genForm.counterAccountId,
      undefined,
      genForm.partnerId || undefined,
    )
    const fail = (res?.data || []).filter((x: any) => !x.ok)
    if (fail.length) {
      ElMessage.warning(`${t('bankrecon.msg.partialFail')}: ${fail.map((f: any) => f.code).join(', ')}`)
    } else {
      ElMessage.success(t('bankrecon.msg.voucherGenerated'))
    }
    genVoucherVisible.value = false
    await loadAll()
  } finally {
    genVoucherLoading.value = false
  }
}

async function doPreLock() {
  if (!statementId.value) return
  // 锁前刷新调节表数据
  try {
    const r = await bankReconApi.reconStatement(statementId.value)
    recon.value = r?.data ?? null
  } catch { /* ignore, use existing recon */ }

  if (!recon.value) {
    ElMessage.warning(t('bankrecon.msg.reconNotLoaded'))
    return
  }
  const rv = recon.value
  const diffColor = rv.reconciledDiff === 0 ? '✅' : '⚠️'
  const msgBody = [
    `${t('bankrecon.field.bankAdjusted')}: ${rv.bankAdjustedBalance}`,
    `${t('bankrecon.field.bookAdjusted')}: ${rv.bookAdjustedBalance}`,
    `${t('bankrecon.field.internalDiff')}: ${rv.statementInternalDiff}`,
    `${t('bankrecon.field.reconciledDiff')}: ${rv.reconciledDiff} ${diffColor}`,
  ].join('\n')

  try {
    await ElMessageBox.confirm(msgBody, t('bankrecon.dlg.lockConfirm'), {
      confirmButtonText: t('bankrecon.btn.lock'),
      cancelButtonText: t('取消'),
      type: rv.reconciledDiff === 0 ? 'info' : 'warning',
    })
  } catch {
    return  // user cancelled
  }

  try {
    const res = await bankReconApi.lock(statementId.value)
    if (res.code === 0) {
      ElMessage.success(t('bankrecon.msg.locked'))
      await loadAll()
    } else {
      ElMessage.error(res.message)
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('bankrecon.msg.lockFailed'))
  }
}

async function doUnlock() {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      t('bankrecon.dlg.unlockReason'),
      t('bankrecon.btn.unlock'),
      {
        inputValidator: (v: string) => { if (!v?.trim()) return t('必填'); return true },
        confirmButtonText: t('bankrecon.btn.unlock'),
        cancelButtonText: t('取消'),
      },
    )
    const r = await bankReconApi.unlock(statementId.value, reason)
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.unlocked'))
      await loadAll()
    } else {
      ElMessage.error(r.message)
    }
  } catch { /* cancelled */ }
}

async function doUnmatch() {
  if (!unmatchGroupId.value) return
  try {
    const r = await bankReconApi.unmatch(unmatchGroupId.value)
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.btn.unmatch'))
      unmatchVisible.value = false
      await loadAll()
    } else {
      ElMessage.error(r.message)
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('bankrecon.btn.unmatch'))
  }
}

onMounted(loadAll)
</script>

<style scoped>
.bank-recon { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.panel-title { font-weight: 600; font-size: 13px; color: #606266; margin-bottom: 6px; display: flex; align-items: center; }
</style>
