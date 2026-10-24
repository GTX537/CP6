<template>
  <section v-if="canQuery" class="stock-commands" aria-label="在庫操作">
    <div class="command-heading">
      <strong>{{ t('在庫操作') }}</strong>
      <el-button :disabled="busy" @click="loadScope">{{ t('操作状態を更新') }}</el-button>
    </div>
    <p v-if="error" role="alert">{{ t(error) }}</p>
    <div v-if="pending" role="status">
      <p>{{ t('結果未確認の操作') }}: {{ pending.operationId }} / {{ pending.kind === 'APPLY' ? t('在庫調整') : t('棚移動') }}</p>
      <p>{{ t('再送せず、元の操作結果を確認してください。未確定のまま新しい操作は開始できません。') }}</p>
      <el-button :disabled="busy" @click="resolve(false)">{{ t('元の結果を確認') }}</el-button>
      <el-button :disabled="busy" @click="resolve(true)">{{ t('未実行の元操作を封閉') }}</el-button>
      <p>{{ t('封閉は未実行の操作だけを確定終了します。既に確定した在庫変更は取り消しません。') }}</p>
    </div>
    <p v-if="result" role="status">
      {{ result.state === 'COMMITTED' ? t('在庫操作は確定済みです。') : result.state === 'CLOSED' ? t('元操作は封閉済みです。在庫変更は実行されません。') : t('結果は未確定です。元の操作番号を保持しています。') }}
      <span v-if="result.result">{{ result.result.txnNo || `${result.result.outTxnNo} / ${result.result.inTxnNo}` }}</span>
    </p>
    <el-dialog v-model="visible" :title="t('在庫調整')" width="560" :close-on-click-modal="false">
      <template v-if="target">
        <p>{{ t('倉庫／ロケーション') }}: {{ target.warehouseCd }} / {{ target.locationCd }}</p>
        <p>{{ t('製品／ロット') }}: {{ target.productCd }} / {{ target.lotNo || '—' }}</p>
        <p>{{ t('この倉庫・ロケーション・製品・ロットの在庫を変更します。確定時の権限と在庫制約が適用されます。') }}</p>
        <el-form label-position="top" @submit.prevent="submit">
          <el-form-item :label="t('調整差分（増加は正、減少は負）')" required>
            <el-input v-model="quantity" :disabled="busy" inputmode="decimal" :placeholder="t('整数13桁・小数8桁まで')" />
          </el-form-item>
          <el-form-item :label="t('備考')"><el-input v-model="remark" :disabled="busy" type="textarea" maxlength="500" /></el-form-item>
          <p v-if="error" role="alert">{{ t(error) }}</p>
        </el-form>
      </template>
      <template #footer>
        <el-button :disabled="busy" @click="visible = false">{{ t('閉じる') }}</el-button>
        <el-button type="primary" :disabled="busy || !!pending || !scope" :loading="busy" @click="submit">{{ t('確定する') }}</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { stockApi } from '@/api/wms/stock'
import { pendingStockOperation, resolveStockOperation, saveStockOperation, stockQuantity, stockResponse, type PendingStockOperation } from '@/auth/stockOperationRecovery'
import type { Stock, StockOperation, StockOperationScope } from '@/types/wms/wms'

const { t } = useI18n(), permissions = usePermissionStore(), emit = defineEmits<{ committed: [] }>()
const scope = ref<StockOperationScope | null>(null), pending = ref<PendingStockOperation | null>(null), result = ref<StockOperation | null>(null)
const target = ref<Stock | null>(null), visible = ref(false)
const quantity = ref(''), remark = ref(''), error = ref(''), busy = ref(false)
const canQuery = computed(() => permissions.loaded && permissions.has('wms-stock:query'))
const canWrite = () => canQuery.value && permissions.has('wms-stock:adjust')
let reads = 0
function clear() {
  reads++; scope.value = null; pending.value = null; result.value = null; target.value = null
  visible.value = false; quantity.value = ''; remark.value = ''; error.value = ''; busy.value = false
}
const intent = useOperationsIntent(clear)
function syncPending() { if (scope.value) pending.value = pendingStockOperation(scope.value) }
async function loadScope() {
  if (busy.value) return
  const token = intent.capture(), read = ++reads
  scope.value = null; pending.value = null; result.value = null; visible.value = false; target.value = null; error.value = ''
  if (!canQuery.value) return
  try {
    const value = stockResponse(await stockApi.operationScope())
    if (!intent.current(token) || read !== reads || !canQuery.value) return
    const saved = pendingStockOperation(value); scope.value = value; pending.value = saved
  } catch { if (intent.current(token) && read === reads) error.value = '操作状態を読み込めません。権限と保存された元の操作記録を確認してください。' }
}
function open(row: Stock) {
  if (!scope.value || busy.value || !canWrite()) return
  try { syncPending(); if (pending.value) return } catch { error.value = '保存された元の操作記録を確認してください。'; return }
  target.value = { ...row }; quantity.value = ''; remark.value = ''; error.value = ''; result.value = null; visible.value = true
}
async function submit() {
  if (busy.value || !visible.value || !target.value || !scope.value || !canWrite()) return
  const token = intent.capture(), bound = { ...scope.value }, row = { ...target.value }
  let qty: string
  try {
    qty = stockQuantity(quantity.value, false)
    if (remark.value.length > 500) throw new Error('備考は500文字以内で入力してください。')
  } catch (failure: any) { error.value = failure.message; return }
  const common = { warehouseCd: row.warehouseCd, productCd: row.productCd, lotNo: row.lotNo, qty, remark: remark.value || undefined }
  const command = { ...common, txnType: 'ADJ' as const, locationCd: row.locationCd,
    unitCd: row.unitCd, ownerType: row.ownerType, ownerCd: row.ownerCd, paperRollNo: row.paperRollNo }
  busy.value = true; error.value = ''; result.value = null
  const guard = () => { if (!intent.current(token) || !canWrite()) throw new Error('操作画面が変わりました。') }
  try {
    const value = await saveStockOperation(bound, 'APPLY', guard, id => stockApi.apply({ ...command, operationId: id }))
    if (!intent.current(token)) return
    result.value = value; visible.value = false; target.value = null; emit('committed')
  } catch { if (intent.current(token)) { visible.value = false; target.value = null; error.value = '結果を確認できません。操作状態を更新し、元の番号で確認してください。'; } }
  finally { if (intent.current(token)) { busy.value = false; try { syncPending() } catch { scope.value = null; error.value = '保存された元の操作記録を確認してください。' } } }
}
async function resolve(close: boolean) {
  if (busy.value || !scope.value || !canQuery.value) return
  const token = intent.capture(), bound = { ...scope.value }; busy.value = true; error.value = ''; result.value = null
  const guard = () => { if (!intent.current(token) || !canQuery.value) throw new Error('操作画面が変わりました。') }
  try {
    const value = await resolveStockOperation(bound, close, guard)
    if (!intent.current(token)) return
    result.value = value; if (value.state === 'COMMITTED') emit('committed')
  } catch { if (intent.current(token)) error.value = '元の結果を確認できません。権限と操作状態を確認してください。元の番号は保持されます。' }
  finally { if (intent.current(token)) { busy.value = false; try { syncPending() } catch { scope.value = null; error.value = '保存された元の操作記録を確認してください。' } } }
}
function storageChanged(event: StorageEvent) {
  if (event.key !== null && !event.key.startsWith('cp6_stock_operation_v1:')) return
  if (busy.value || !scope.value) return
  result.value = null; visible.value = false; target.value = null
  try { syncPending() } catch { scope.value = null; error.value = '保存された元の操作記録を確認してください。' }
}
watch(() => [permissions.loaded, ...permissions.actionKeys].join('|'), () => { intent.invalidate(); clear(); void loadScope() }, { flush: 'sync' })
onMounted(() => { void loadScope(); window.addEventListener('storage', storageChanged) })
onBeforeUnmount(() => { clear(); window.removeEventListener('storage', storageChanged) })
defineExpose({ open })
</script>

<style scoped>
.stock-commands { margin-block: 12px; padding: 12px; border: 1px solid var(--cp-border); border-radius: var(--cp-r-sm); }
.command-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
[role="alert"] { color: var(--cp-danger); }
</style>
