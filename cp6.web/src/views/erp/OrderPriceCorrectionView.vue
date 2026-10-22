<!--
  受注単価訂正 —— 検索先行（拠点必須・自動取得なし）＋ type=selection 選択に連動する行内編集グリッド
  （変更後単価/特値/理由の el-input-number・checkbox・input を選択行のみ活性化）＋ 一括更新の特殊页。
  行内編集グリッド形態は CpListPage の読取リスト契約に載らないため「非表格特殊页」として token 化：
  el-form/el-table/行内エディタ/一括更新フローは原様保全し、状態・件数 el-tag → CpTag(+tone)、仮単価警告アイコン
  色値 #e6a23c → --cp-warn トークン化のみ。
-->
<template>
  <div class="price-correction">
    <!-- 検索条件 -->
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('sales.term.base')" required>
          <el-input v-model="query.baseCd" style="width: 120px" :placeholder="t('sales.search.required')" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.from')">
          <el-input v-model="query.customerCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.to')">
          <el-input v-model="query.customerCdTo" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.from')">
          <el-date-picker v-model="query.orderDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.to')">
          <el-date-picker v-model="query.orderDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.haibaiNo') + '1'">
          <el-input v-model="query.haibaiNo1" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.productCd')">
          <el-input v-model="query.productCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('顧客品名')">
          <el-input v-model="query.customerItemName" style="width: 200px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.qty') + ' FROM-TO'">
          <el-input-number v-model="query.qtyFrom" :precision="2" :controls="false" style="width: 110px" />
          〜
          <el-input-number v-model="query.qtyTo" :precision="2" :controls="false" style="width: 110px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.amount') + ' FROM-TO'">
          <el-input-number v-model="query.amountFrom" :precision="0" :controls="false" style="width: 130px" />
          〜
          <el-input-number v-model="query.amountTo" :precision="0" :controls="false" style="width: 130px" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.onlyProvisional">{{ t('sales.pc.provisional') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button v-permission="'erp-order-price-correction:correct'" type="success" @click="onSubmit" :loading="submitting" :disabled="selectedRows.length === 0 || outcomeUnknown || !!pendingOperation || !operationScope">
            {{ t('sales.pc.updateSelected') }}（{{ selectedRows.length }}）
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-alert v-if="submitNotice" :title="submitNotice" type="warning" show-icon :closable="false" />
    <div v-if="pendingOperation" style="margin: 12px 0">
      <span>{{ t('確認待ちの操作') }}: {{ pendingOperation }}</span>
      <el-button @click="recoverOperation(false)" :disabled="submitting || recovering">{{ t('元の結果を確認') }}</el-button>
      <el-button @click="recoverOperation(true)" :disabled="submitting || recovering">{{ t('未完了の操作を封じる') }}</el-button>
    </div>
    <el-button v-else-if="!operationScope" @click="loadOperationScope">{{ t('現在のユーザーを確認') }}</el-button>

    <!-- 結果 -->
    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <CpTag tone="info">{{ t('合計 {n} 件', { n: total }) }}</CpTag>
        <CpTag v-if="selectedRows.length > 0" tone="ok" style="margin-left: 8px;">
          {{ t('選択中 {n} 件', { n: selectedRows.length }) }}
        </CpTag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600" @selection-change="onSelectionChange">
        <el-table-column type="selection" width="50" :selectable="canEditRow" />
        <el-table-column prop="rowNo" label="No" width="60" align="center" />
        <el-table-column :label="t('状態')" width="80" align="center">
          <template #default="{ row }">
            <CpTag :tone="approvalTone(row.approvalStatus)">{{ approvalLabel(row.approvalStatus) }}</CpTag>
          </template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('得意先')" width="100" />
        <el-table-column prop="customerName" :label="t('得意先名')" width="160" />
        <el-table-column prop="salesPersonName" :label="t('売上担当')" width="120" />
        <el-table-column prop="orderSheetNo" :label="t('注文書NO')" width="120" />
        <el-table-column prop="haibaiNo1" :label="t('手配NO1')" width="140" />
        <el-table-column prop="productCd" :label="t('製品CD')" width="140" />
        <el-table-column prop="cpItemName1" :label="t('品名1')" min-width="160" />
        <el-table-column prop="orderDate" :label="t('受注日')" width="100" />
        <el-table-column prop="customerDeliveryDate" :label="t('客先納期')" width="100" />
        <el-table-column prop="quantity" :label="t('数量')" width="100" align="right" />
        <el-table-column prop="qtyUnit" :label="t('単位')" width="60" />
        <el-table-column v-if="showPrices" prop="individualUnitPriceBefore" :label="t('個別単価(変更前)')" width="130" align="right" />
        <el-table-column v-if="showPrices" prop="setUnitPriceBefore" :label="t('セット単価(変更前)')" width="130" align="right" />
        <el-table-column prop="unitPriceUnit" :label="t('単位')" width="60" />
        <el-table-column v-if="showPrices" :label="t('個別単価(変更後)')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.individualUnitPriceAfter" :disabled="!isRowSelected(row)" :precision="4" :controls="false" size="small" style="width: 130px" />
          </template>
        </el-table-column>
        <el-table-column v-if="showPrices" :label="t('セット単価(変更後)')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.setUnitPriceAfter" :disabled="!isRowSelected(row)" :precision="4" :controls="false" size="small" style="width: 130px" />
          </template>
        </el-table-column>
        <el-table-column v-if="showPrices" :label="t('特値')" width="60" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.specialPriceFlg" true-value="1" false-value="0" :disabled="!isRowSelected(row)" />
          </template>
        </el-table-column>
        <el-table-column v-if="showPrices" :label="t('仮単価')" width="60" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.provisionalPriceFlg" class="prov-warn"><Warning /></el-icon>
          </template>
        </el-table-column>
        <el-table-column v-if="showPrices" :label="t('単価変更理由')" min-width="180">
          <template #default="{ row }">
            <el-input v-model="row.priceChangeReason" :disabled="!isRowSelected(row)" size="small" />
          </template>
        </el-table-column>
        <el-table-column v-if="showAmounts" prop="amount" :label="t('金額')" width="130" align="right" />
      </el-table>

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="search"
        @size-change="search"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'

const { t } = useI18n()
import { Warning } from '@element-plus/icons-vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { orderApi } from '@/api/erp/order'
import { AUTH_EVENT, AuthenticationChangedError, identityStamp, requireIdentity } from '@/auth/identity'
import { pendingPriceOperation, withPriceOperation, completePriceOperation } from '@/auth/priceCorrectionRecovery'
import type {
  OrderPriceCorrectionQueryDto,
  OrderPriceCorrectionItemDto,
  PriceCorrectionOperationScope,
} from '@/types/erp/order'

const query = reactive<OrderPriceCorrectionQueryDto>({
  page: 1,
  pageSize: 100,
})
const rows = ref<OrderPriceCorrectionItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)
const outcomeUnknown = ref(false)
const submitNotice = ref('')
const selectedRows = ref<OrderPriceCorrectionItemDto[]>([])
const operationScope = ref<PriceCorrectionOperationScope | null>(null)
const pendingOperation = ref<string | null>(null)
const recovering = ref(false)
let mounted = true
async function loadOperationScope() {
  const stamp = identityStamp()
  try {
    const response = await orderApi.priceCorrectionOperationScope(); requireIdentity(stamp)
    if (!mounted) return
    if (response.code !== 0 || !response.data) throw new Error('Scope unavailable')
    const pending = pendingPriceOperation(response.data)
    operationScope.value = response.data; pendingOperation.value = pending
    outcomeUnknown.value = !!pending
  } catch {
    if (mounted && stamp === identityStamp()) {
      operationScope.value = null
      submitNotice.value = t('現在のユーザーと元の操作を確認できません。保存前に再確認してください。')
    }
  }
}
onMounted(loadOperationScope)
async function recoverOperation(close: boolean) {
  if (!operationScope.value || submitting.value || recovering.value) return
  const scope = operationScope.value, stamp = identityStamp()
  recovering.value = true
  try {
    if (close) await ElMessageBox.confirm(t('未完了の元の操作を封じます。すでに保存済みの場合は元の結果を表示します。'), t('確認'))
    requireIdentity(stamp)
    const result = await withPriceOperation(scope, false, async (id, originalStamp) => {
      const response = await (close ? orderApi.closePriceCorrectionOperation(id) : orderApi.readPriceCorrectionOperation(id))
      requireIdentity(originalStamp)
      if (response.code !== 0 || !response.data || response.data.operationId !== id
        || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(response.data.state)
        || response.data.state === 'COMMITTED' && response.data.result?.operationId !== id) throw new Error('Original result mismatch')
      if (response.data.state !== 'UNKNOWN') completePriceOperation(scope, id, originalStamp)
      return response.data
    })
    requireIdentity(stamp); if (!mounted) return
    pendingOperation.value = pendingPriceOperation(scope); outcomeUnknown.value = result.state === 'UNKNOWN'
    submitNotice.value = result.state === 'UNKNOWN' ? t('元の結果は未確認です。未実行とは判断できません。再送せず確認を続けてください。')
      : result.state === 'CLOSED' ? t('元の操作を封じました。再検索して新しい変更を準備できます。')
      : t('元の更新を確認しました。更新: {updated} 件 / 承認依頼: {wf} 件（外部送信待ち）。',
        { updated: result.result!.updatedCount, wf: result.result!.wfRequestedCount })
    if (result.state === 'COMMITTED' && result.result!.conflictedKeys?.length)
      ElMessage.warning(t('競合: {keys}', { keys: result.result!.conflictedKeys.join(', ') }))
    if (result.state !== 'UNKNOWN') clearResults()
  } catch {
    if (mounted && stamp === identityStamp()) {
      clearResults(); submitNotice.value = t('元の結果を確認できません。現在の権限を確認してください。操作は再送していません。')
    }
  } finally { if (mounted && stamp === identityStamp()) recovering.value = false }
}
const showPrices = computed(() => rows.value.some(row => row.priceReadable !== false))
const showAmounts = computed(() => rows.value.some(row => row.amountReadable !== false))
let searchGeneration = 0
let pageIdentity = identityStamp()
function identityChanged() {
  if (pageIdentity === identityStamp()) return
  pageIdentity = identityStamp()
  operationScope.value = null; pendingOperation.value = null; outcomeUnknown.value = false; recovering.value = false
  resetQuery()
  void loadOperationScope()
  submitNotice.value = t('ログイン状態が変わりました。現在のアカウントで再検索してください。')
}
window.addEventListener(AUTH_EVENT, identityChanged)
window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => {
  mounted = false
  searchGeneration++
  window.removeEventListener(AUTH_EVENT, identityChanged)
  window.removeEventListener('storage', identityChanged)
})

function isRowSelected(row: OrderPriceCorrectionItemDto): boolean {
  return canEditRow(row) && selectedRows.value.includes(row)
}

function canEditRow(row: OrderPriceCorrectionItemDto): boolean {
  return row.priceWritable !== false && row.priceReadable !== false && !outcomeUnknown.value
}
function onSelectionChange(sel: OrderPriceCorrectionItemDto[]) {
  selectedRows.value = sel.filter(canEditRow)
}

async function search() {
  if (!query.baseCd) {
    ElMessage.warning(t('sales.err.E10022') + ': ' + t('sales.term.base'))
    return
  }
  const generation = ++searchGeneration, stamp = identityStamp()
  clearResults()
  loading.value = true
  try {
    const res = await orderApi.searchPriceCorrection({ ...query })
    requireIdentity(stamp)
    if (generation !== searchGeneration) return
    if (res.code === 0 && res.data) {
      rows.value = res.data.rows
      total.value = res.data.total
      if (rows.value.length === 0) ElMessage.info(t('sales.err.E10008'))
    }
  } catch {
    if (generation === searchGeneration) {
      clearResults()
      submitNotice.value = t('現在の検索結果を確認できません。権限と検索条件を確認して再検索してください。')
    }
  } finally {
    if (generation === searchGeneration) loading.value = false
  }
}

function clearResults() {
  rows.value = []
  total.value = 0
  selectedRows.value = []
}
function resetQuery() {
  searchGeneration++
  loading.value = false
  Object.keys(query).forEach(k => {
    if (k !== 'page' && k !== 'pageSize') (query as any)[k] = undefined
  })
  query.page = 1
  clearResults()
}

async function onSubmit() {
  if (submitting.value || outcomeUnknown.value || pendingOperation.value || !operationScope.value) return
  if (selectedRows.value.length === 0) {
    ElMessage.warning(t('sales.err.E10009'))
    return
  }
  if (selectedRows.value.some(row => !canEditRow(row))) return
  const request = {
    items: selectedRows.value.map(r => ({
      webOrderNo: r.webOrderNo,
      webOrderDetailNo: r.webOrderDetailNo,
      individualUnitPriceAfter: r.individualUnitPriceAfter,
      setUnitPriceAfter: r.setUnitPriceAfter,
      specialPriceFlg: r.specialPriceFlg,
      priceChangeReason: r.priceChangeReason,
      rowVersion: r.rowVersion,
    })),
  }
  const submittedIdentity = identityStamp()
  const scope = operationScope.value
  let attempted = false
  submitting.value = true
  try {
    await ElMessageBox.confirm(
      t('{n} 件の単価訂正を実行します。よろしいですか？', { n: selectedRows.value.length }),
      t('確認'), { type: 'warning' },
    )
  } catch { submitting.value = false; return }

  try {
    requireIdentity(submittedIdentity)
    submitNotice.value = ''
    const res = await withPriceOperation(scope, true, async (operationId, stamp) => {
      pendingOperation.value = operationId
      attempted = true
      const response = await orderApi.batchUpdatePrice({ ...request, operationId })
      requireIdentity(stamp)
      if (response.code !== 0 || response.data?.operationId !== operationId) throw new Error('Original result mismatch')
      completePriceOperation(scope, operationId, stamp)
      return response
    })
    requireIdentity(submittedIdentity)
    if (!mounted) return
    pendingOperation.value = pendingPriceOperation(scope)
    if (res.code !== 0 || !res.data) throw new Error('Price correction result unavailable')
    if (res.code === 0 && res.data) {
      ElMessage.success(t(
        res.data.workflowDeliveryState === 'AwaitingAdapter'
          ? '更新: {updated} 件 / 承認依頼保存: {wf} 件（外部送信待ち）'
          : '更新: {updated} 件 / WF 起票: {wf} 件',
        { updated: res.data.updatedCount, wf: res.data.wfRequestedCount },
      ))
      if (res.data.conflictedKeys.length > 0) {
        ElMessage.warning(t('競合: {keys}', { keys: res.data.conflictedKeys.join(', ') }))
      }
      await search()
      selectedRows.value = []
    }
  } catch (error: unknown) {
    if (!mounted || submittedIdentity !== identityStamp()) return
    if (error instanceof AuthenticationChangedError) {
      resetQuery()
      submitNotice.value = t('ログイン状態が変わりました。元の更新は再送していません。送信済みの場合は元のアカウントで保存状態を確認してください。')
      return
    }
    if (!attempted) {
      try { pendingOperation.value = pendingPriceOperation(scope) } catch { operationScope.value = null }
      outcomeUnknown.value = !!pendingOperation.value
      submitNotice.value = t('保存前の確認を完了できません。元の操作とブラウザーの保存機能を確認してください。更新は送信していません。')
      return
    }
    const response = (error as { response?: { status?: number; data?: { message?: string } } })?.response
    const status = response?.status
    const knownUnavailable = ['AUTHORITY_FENCE_UNAVAILABLE', 'OWNER_SCOPE_MAPPING_UNAVAILABLE'].includes(response?.data?.message || '')
    if (!status || (status >= 500 && !knownUnavailable)) {
      outcomeUnknown.value = true
      submitNotice.value = t('更新結果を確認できません。元の結果を確認してください。操作は再送しません。')
    } else if (status === 401) {
      resetQuery()
      submitNotice.value = t('ログインが無効になりました。再ログイン後に受注の状態を確認してください。更新は再送していません。')
    } else if (status === 409) {
      submitNotice.value = t('他の処理によって受注が更新されました。入力内容を控えてから再検索してください。')
    } else if (status === 403) {
      resetQuery()
      submitNotice.value = t('現在の権限では更新できません。以前の資料は消去しました。再検索して現在の権限を確認してください。')
    } else if (knownUnavailable) {
      resetQuery()
      submitNotice.value = t('現在のデータ範囲または権限を確認できないため更新できません。管理者に確認してください。')
    } else {
      submitNotice.value = t('更新は完了していません。入力内容を確認してください。')
    }
  } finally {
    submitting.value = false
  }
}

function approvalLabel(s?: number): string {
  if (s === 1) return t('承認依頼中')
  if (s === 9) return t('承認済')
  return t('未')
}
// approvalStatus → CpTag Tone（原 approvalTagType の warning/success/info 意図を保色）
function approvalTone(s?: number): Tone {
  if (s === 1) return 'warn'
  if (s === 9) return 'ok'
  return 'info'
}
</script>

<style scoped>
.price-correction { padding: 16px; }
.search-card { margin-bottom: 12px; }
.prov-warn { color: var(--cp-warn); }
</style>
