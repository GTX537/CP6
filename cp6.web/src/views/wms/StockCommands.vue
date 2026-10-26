<template>
  <section v-if="canQuery" class="stock-commands" aria-label="在庫操作">
    <div class="command-heading">
      <strong>{{ t('在庫操作') }}</strong>
      <el-button v-if="canWrite('APPLY')" :disabled="busy || !!pending || !scope" @click="openNew">{{ t('新しい在庫の調整') }}</el-button>
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
    <el-dialog v-model="visible" :title="kind === 'MOVE' ? t('棚移動') : t('在庫調整')" width="560" :close-on-click-modal="false">
      <template v-if="target">
        <template v-if="!newEntry">
          <p>{{ t('倉庫／ロケーション') }}: {{ target.warehouseCd }} / {{ target.locationCd }}</p>
          <p>{{ t('製品／ロット') }}: {{ target.productCd }} / {{ target.lotNo || '—' }}</p>
        </template>
        <p>{{ t('この倉庫・ロケーション・製品・ロットの在庫を変更します。確定時の権限と在庫制約が適用されます。') }}</p>
        <el-form label-position="top" @submit.prevent="submit">
          <template v-if="newEntry">
            <el-form-item :label="t('倉庫コード')" required><el-input v-model="target.warehouseCd" :disabled="busy" /></el-form-item>
            <el-form-item :label="t('ロケーションコード')" required><el-input v-model="target.locationCd" :disabled="busy" /></el-form-item>
            <el-form-item :label="t('製品コード')" required><el-input v-model="target.productCd" :disabled="busy" /></el-form-item>
            <el-form-item :label="t('ロット番号')"><el-input v-model="target.lotNo" :disabled="busy" /></el-form-item>
            <el-form-item :label="t('単位')"><el-input v-model="target.unitCd" :disabled="busy" /></el-form-item>
          </template>
          <el-form-item v-if="kind === 'MOVE'" :label="t('移動先ロケーション（同じ倉庫内）')" required>
            <el-input v-model="destination" :disabled="busy" :placeholder="t('移動元と異なるロケーション')" />
          </el-form-item>
          <p v-if="kind === 'MOVE'">{{ t('所有者・単位・品質などの在庫属性は維持されます。移動先に異なる属性の在庫がある場合は移動できません。') }}</p>
          <el-button :disabled="busy" @click="loadCreationContext">{{ t('在庫・所属を確認') }}</el-button>
          <template v-if="creation?.requiresCreation">
            <p>{{ t('この場所に新しい在庫を作成します。所属部門を選択してください。') }}</p>
            <el-form-item :label="t('所属部門')" :required="!creation.allowsUnbound">
              <el-select v-model="department" :disabled="busy" :clearable="creation.allowsUnbound" :placeholder="t('所属部門を選択')">
                <el-option v-if="creation.allowsUnbound" :label="t('部門を指定しない')" value="" />
                <el-option v-for="item in creation.departments" :key="item.id" :label="`${item.code} ${item.name}`" :value="item.id" />
              </el-select>
            </el-form-item>
          </template>
          <el-form-item :label="kind === 'MOVE' ? t('移動数量（正数）') : t('調整差分（増加は正、減少は負）')" required>
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
import type { Stock, StockOperation, StockOperationScope, StockCreationContext, StockCreationKey, StockCreationIntent } from '@/types/wms/wms'

const { t } = useI18n(), permissions = usePermissionStore(), emit = defineEmits<{ committed: [] }>()
const scope = ref<StockOperationScope | null>(null), pending = ref<PendingStockOperation | null>(null), result = ref<StockOperation | null>(null)
const target = ref<Stock | null>(null), visible = ref(false)
const newEntry = ref(false), creation = ref<StockCreationContext | null>(null), department = ref('')
let creationSignature = '', dialogIdentity: ReturnType<typeof intent.capture> | null = null
const kind = ref<PendingStockOperation['kind']>('APPLY'), destination = ref('')
const quantity = ref(''), remark = ref(''), error = ref(''), busy = ref(false)
const canQuery = computed(() => permissions.loaded && permissions.has('wms-stock:query'))
const canWrite = (operation: PendingStockOperation['kind']) => canQuery.value && permissions.has(operation === 'MOVE' ? 'wms-stock:move' : 'wms-stock:adjust')
let reads = 0
function clear() {
  reads++; scope.value = null; pending.value = null; result.value = null; target.value = null
  creation.value = null; creationSignature = ''; department.value = ''; newEntry.value = false; dialogIdentity = null
  visible.value = false; kind.value = 'APPLY'; destination.value = ''; quantity.value = ''; remark.value = ''; error.value = ''; busy.value = false
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
function open(row: Stock, operation: PendingStockOperation['kind'] = 'APPLY') {
  if (!scope.value || busy.value || !canWrite(operation)) return
  try { syncPending(); if (pending.value) return } catch { error.value = '保存された元の操作記録を確認してください。'; return }
  target.value = { ...row }; kind.value = operation; destination.value = ''; quantity.value = ''; remark.value = ''; error.value = ''; result.value = null; visible.value = true
  newEntry.value = false; creation.value = null; creationSignature = ''; department.value = ''; dialogIdentity = intent.capture()
}
function openNew() {
  open({ id: '', warehouseCd: '', locationCd: '', productCd: '', lotNo: '', unitCd: '',
    physicalQty: 0, allocatedQty: 0, availableQty: 0, ownerType: 'SELF', recallFlag: false })
  if (visible.value && target.value?.id === '') newEntry.value = true
}
function targetKey(): StockCreationKey | null {
  const row = target.value
  return row ? { warehouseCd: row.warehouseCd, locationCd: kind.value === 'MOVE' ? destination.value : row.locationCd,
    productCd: row.productCd, lotNo: row.lotNo } : null
}
function signature(key: StockCreationKey | null, operation = kind.value) { return JSON.stringify([operation, key]) }
watch(() => signature(targetKey()), () => { creation.value = null; creationSignature = ''; department.value = '' }, { flush: 'sync' })
function checkKey(key: StockCreationKey) {
  if (!key.warehouseCd.trim() || !key.locationCd.trim() || !key.productCd.trim()) throw new Error('倉庫・ロケーション・製品を入力してください。')
}
async function readCreation(key: StockCreationKey, operation: PendingStockOperation['kind'], bound: StockOperationScope,
  token: ReturnType<typeof intent.capture>) {
  const sign = signature(key, operation)
  const value = creation.value && creationSignature === sign ? creation.value
    : stockResponse(await stockApi.creationContext(operation === 'MOVE' ? 'move' : 'adjust', key))
  if (!intent.current(token) || !canWrite(operation) || value.tenantId !== bound.tenantId || value.actorId !== bound.actorId)
    throw new Error('操作画面が変わりました。')
  if (signature(targetKey()) === sign) { creation.value = value; creationSignature = sign }
  return value
}
async function loadCreationContext() {
  if (busy.value || !scope.value || !target.value || !canWrite(kind.value)) return
  if (!dialogIdentity || !intent.current(dialogIdentity)) { clear(); return }
  const token = dialogIdentity, key = targetKey()!, operation = kind.value, bound = { ...scope.value }
  busy.value = true; error.value = ''; creation.value = null; department.value = ''
  try { checkKey(key); await readCreation(key, operation, bound, token) }
  catch { if (intent.current(token)) error.value = '在庫・所属を確認できません。入力と現在の権限を確認してください。' }
  finally { if (intent.current(token)) busy.value = false }
}
async function submit() {
  if (busy.value || !visible.value || !target.value || !scope.value || !canWrite(kind.value)) return
  if (!dialogIdentity || !intent.current(dialogIdentity)) { clear(); return }
  const token = dialogIdentity, bound = { ...scope.value }, row = { ...target.value }, key = targetKey()!
  const operation = kind.value, toLocationCd = destination.value
  const selectedDepartment = department.value, creating = newEntry.value
  let qty: string
  try {
    checkKey(key)
    qty = stockQuantity(quantity.value, operation === 'MOVE')
    if (operation === 'MOVE' && (!toLocationCd.trim() || [...toLocationCd].length > 30)) throw new Error('移動先ロケーションを30文字以内で入力してください。')
    // Preserve the key bytes; the Owner applies the database's location identity/equality rules.
    if (operation === 'MOVE' && toLocationCd === row.locationCd) throw new Error('移動先は移動元と異なるロケーションを指定してください。')
    if (remark.value.length > 500) throw new Error('備考は500文字以内で入力してください。')
  } catch (failure: any) { error.value = failure.message; return }
  const common = { warehouseCd: row.warehouseCd, productCd: row.productCd, lotNo: row.lotNo, qty, remark: remark.value || undefined }
  const adjustment = { ...common, txnType: 'ADJ' as const, locationCd: row.locationCd,
    unitCd: row.unitCd, ownerType: row.ownerType, ownerCd: row.ownerCd, paperRollNo: row.paperRollNo }
  const move = { ...common, fromLocationCd: row.locationCd, toLocationCd }
  busy.value = true; error.value = ''; result.value = null
  const guard = () => { if (!intent.current(token) || !canWrite(operation)) throw new Error('操作画面が変わりました。') }
  let submitted = false
  try {
    const context = await readCreation(key, operation, bound, token); guard()
    if (creating && !context.requiresCreation) { error.value = 'この在庫は既に存在します。一覧から対象の在庫を選択してください。'; return }
    let newStock: StockCreationIntent | undefined
    if (context.requiresCreation) {
      if (!selectedDepartment && !context.allowsUnbound) { error.value = '所属部門を選択してください。'; return }
      if (selectedDepartment && !context.departments.some(d => d.id === selectedDepartment)) { error.value = '所属部門を確認し直してください。'; return }
      newStock = { expectedAuthorityVersion: context.authorityVersion, departmentId: selectedDepartment || null }
    }
    const creationInput = newStock ? { newStock } : {}
    submitted = true
    const value = await saveStockOperation(bound, operation, guard, id => operation === 'MOVE'
      ? stockApi.move({ ...move, ...creationInput, operationId: id }) : stockApi.apply({ ...adjustment, ...creationInput, operationId: id }))
    if (!intent.current(token)) return
    result.value = value; visible.value = false; target.value = null; emit('committed')
  } catch { if (intent.current(token)) {
    if (submitted) { visible.value = false; target.value = null; error.value = '結果を確認できません。操作状態を更新し、元の番号で確認してください。' }
    else error.value = '在庫・所属を確認できません。入力と現在の権限を確認してください。'
  } }
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
