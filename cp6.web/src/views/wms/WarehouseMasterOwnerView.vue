<template>
  <CpPageShell :title="isLocation ? '庫位マスタ' : '倉庫マスタ'" :count="result?.total">
    <template #actions><el-button :disabled="!canQuery || busy" @click="initialize">更新</el-button></template>
    <el-alert v-if="!canQuery" title="現在の参照権限を確認できません。" type="warning" :closable="false" />
    <el-alert v-if="error" :title="error" type="warning" :closable="false" />
    <el-alert v-if="notice" :title="notice" type="info" :closable="false" />
    <el-card v-if="pending" shadow="never">
      <p>名称変更の結果が未確認です。新しい変更の前に、元の操作を確認してください。</p>
      <p>操作 ID: {{ pending.operationId }}</p>
      <el-button :disabled="!canQuery || busy" @click="recover(false)">元の操作を確認</el-button>
      <el-button :disabled="!canAbandon || busy" @click="recover(true)">未確定の操作を封じる</el-button>
    </el-card>
    <el-form inline @submit.prevent="search">
      <template v-if="isLocation">
        <el-form-item label="倉庫コード"><el-input v-model="warehouseSearch" maxlength="10" /></el-form-item>
        <el-button :disabled="!canQuery" @click="loadChoices(1)">倉庫検索</el-button>
        <el-form-item label="倉庫"><el-select v-model="warehouseId" :disabled="!canQuery" style="width: 260px">
          <el-option v-for="w in choices?.items || []" :key="w.id" :value="w.id" :label="`${w.warehouseCd} ${w.nameReadable ? w.warehouseName || '—' : '名称参照不可'}`" />
        </el-select></el-form-item>
        <el-pagination v-if="choices" layout="prev, pager, next" :total="choices.total" :page-size="choices.pageSize" :current-page="choices.page" @current-change="loadChoices" />
      </template>
      <el-form-item :label="isLocation ? '庫位コード' : '倉庫コード'"><el-input v-model="queryText" :maxlength="isLocation ? 30 : 10" clearable /></el-form-item>
      <el-button native-type="submit" :disabled="!canQuery || (isLocation && !warehouseId)" :loading="loading">検索</el-button>
    </el-form>
    <p class="master-note">現在参照できる記録を表示しています。名称のみ変更できます。作成・削除や在庫制御の変更は、この画面では利用できません。</p>
    <p v-if="isLocation" class="master-note">容量と予約数量は保存された原値です。単位付き容量・確定予約残は未確認です。0 を無制限とは扱いません。</p>
    <p v-else class="master-note">Site とライフサイクルは未確認です。拠点コードから Site は推定しません。</p>
    <el-table v-if="result" :data="result.items" row-key="id">
      <el-table-column :prop="isLocation ? 'locationCd' : 'warehouseCd'" label="コード" />
      <el-table-column label="名称"><template #default="{ row }">{{ row.nameReadable ? rowName(row) ?? '未設定' : '参照不可' }}</template></el-table-column>
      <template v-if="isLocation">
        <el-table-column prop="rawCapacityQty" label="保存容量（原値）" />
        <el-table-column prop="rawReservedCapacityQty" label="保存予約量（原値）" />
        <el-table-column label="元の親参照"><template #default="{ row }">{{ row.parentState === 'AUTHORIZED_ORIGINAL_REFERENCE' ? row.parentLocationCd : row.parentState === 'ROOT_RECORD' ? '親の記録なし' : '参照不可・未確認' }}</template></el-table-column>
      </template>
      <el-table-column v-else prop="baseCd" label="保存拠点コード" />
      <el-table-column label="操作"><template #default="{ row }"><el-button link :disabled="!canEdit || !row.nameWritable || !!pending || busy || storageBlocked" @click="openName(row)">名称変更</el-button><RouterLink v-if="isLocation && permission.has('wms-location-rule:query')" :to="`/wms/location/${row.id}/rules`">规则候选</RouterLink><RouterLink v-if="!isLocation" :to="`/wms/warehouse/${row.id}/hierarchy`">库位层级策略</RouterLink><RouterLink v-if="!isLocation" :to="`/wms/warehouse/${row.id}/rule-references`">库位规则字典</RouterLink><RouterLink v-if="!isLocation" :to="`/wms/warehouse/${row.id}/clock-policy`">时钟政策</RouterLink></template></el-table-column>
    </el-table>
    <el-pagination v-if="result" layout="total, prev, pager, next" :total="result.total" :page-size="result.pageSize" :current-page="result.page" @current-change="goPage" />
    <el-dialog :model-value="!!editing" title="名称変更" :before-close="closeEditor" :close-on-click-modal="false">
      <p>{{ editing ? rowCode(editing) : '' }}</p>
      <el-input v-model="name" maxlength="100" :disabled="clearName || busy" />
      <el-checkbox v-if="isLocation" v-model="clearName" :disabled="busy">名称を未設定にする</el-checkbox>
      <template #footer><el-button :disabled="busy" @click="closeEditor()">取消</el-button><el-button type="primary" :loading="busy" :disabled="!canEdit || !!pending || storageBlocked" @click="saveName">保存</el-button></template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { warehouseOwnerApi as api } from '@/api/wms/warehouseOwner'
import { checkMasterLocator, checkMasterOperation, checkMasterPage, checkMasterResult, checkMasterScope } from '@/types/wms/warehouseOwner'
import type { MasterKind, MasterLocator, MasterPage, MasterQuery, MasterRow, MasterScope, WarehouseObservation } from '@/types/wms/warehouseOwner'
const props = defineProps<{ kind: MasterKind }>()
const isLocation = computed(() => props.kind === 'locations'), resource = computed(() => isLocation.value ? 'wms-location' : 'wms-warehouse')
const permission = usePermissionStore()
const canQuery = computed(() => permission.loaded && permission.has(`${resource.value}:query`) && (!isLocation.value || permission.has('wms-warehouse:query')))
const canEdit = computed(() => canQuery.value && permission.has(`${resource.value}:edit`))
const canAbandon = computed(() => canQuery.value && permission.has(`${resource.value}:abandon`))
const result = ref<MasterPage | null>(null), choices = ref<MasterPage<WarehouseObservation> | null>(null)
const queryText = ref(''), warehouseSearch = ref(''), warehouseId = ref(''), page = ref(1), loading = ref(false), busy = ref(false)
const editing = ref<MasterRow | null>(null), name = ref(''), clearName = ref(false)
const pending = ref<MasterLocator | null>(null), scope = ref<MasterScope | null>(null), storageBlocked = ref(false), error = ref(''), notice = ref('')
let readSequence = 0, choiceSequence = 0, initSequence = 0
const intent = useOperationsIntent(clearAll)
const rowName = (row: MasterRow) => 'locationName' in row ? row.locationName : row.warehouseName
const rowCode = (row: MasterRow) => 'locationCd' in row ? row.locationCd : row.warehouseCd
const dirty = computed(() => !!editing.value && (clearName.value || name.value !== (rowName(editing.value) ?? '')))
const storageKey = (p: MasterScope, kind: MasterKind) => `cp6.master-name:${kind}:${p.tenantId}:${p.actorId}`
function clearRead() { readSequence++; result.value = null; loading.value = false }
function clearEditor() { editing.value = null; name.value = ''; clearName.value = false }
function clearAll() {
  initSequence++; choiceSequence++; clearRead(); clearEditor(); choices.value = null; warehouseId.value = ''
  scope.value = null; pending.value = null; storageBlocked.value = false; busy.value = false; error.value = ''; notice.value = ''
}
async function initialize() {
  if (busy.value || !canQuery.value) return
  const before = intent.capture()
  if (!await allowDiscard() || !intent.current(before) || !canQuery.value) return
  clearAll(); const n = ++initSequence, token = intent.capture(), kind = props.kind
  try {
    const response = await api.scope(kind)
    if (!intent.current(token) || n !== initSequence || !canQuery.value) return
    if (response.code !== 0) throw new Error('SCOPE_UNAVAILABLE')
    scope.value = checkMasterScope(response.data)
    try {
      const stored = sessionStorage.getItem(storageKey(scope.value, kind))
      if (stored) pending.value = checkMasterLocator(JSON.parse(stored), scope.value, kind)
    } catch { storageBlocked.value = true; error.value = '保存された操作情報を確認できません。新しい変更は停止しています。' }
    if (isLocation.value) await loadChoices(1); else await load(1)
  } catch { if (intent.current(token) && n === initSequence) error.value = '現在の参照範囲を確認できません。更新してください。' }
}
async function loadChoices(target: number) {
  const n = ++choiceSequence, token = intent.capture(); choices.value = null
  if (!canQuery.value || !scope.value) return
  const q: MasterQuery = { warehouseCd: warehouseSearch.value || undefined, page: target, pageSize: 50 }
  try {
    const response = await api.warehouses(q)
    if (!intent.current(token) || n !== choiceSequence || !canQuery.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    choices.value = checkMasterPage(response.data, 'warehouses', q)
  } catch { if (intent.current(token) && n === choiceSequence) error.value = '倉庫候補を取得できません。' }
}
async function load(target: number) {
  clearRead(); const n = readSequence, token = intent.capture(), kind = props.kind, parent = warehouseId.value
  if (!canQuery.value || !scope.value || (isLocation.value && !parent)) return
  const q: MasterQuery = { page: target, pageSize: 50, ...(isLocation.value ? { locationCd: queryText.value || undefined } : { warehouseCd: queryText.value || undefined }) }
  loading.value = true
  try {
    const response = kind === 'warehouses' ? await api.warehouses(q) : await api.locations(parent, q)
    if (!intent.current(token) || n !== readSequence || !canQuery.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    result.value = checkMasterPage<MasterRow>(response.data, kind, q, parent); page.value = target
  } catch { if (intent.current(token) && n === readSequence) error.value = '現在の記録を取得できません。権限と最新状態を確認してください。' }
  finally { if (intent.current(token) && n === readSequence) loading.value = false }
}
const search = () => load(1), goPage = (target: number) => load(target)
async function allowDiscard() {
  if (!dirty.value) return true
  try { await ElMessageBox.confirm('未保存の名称変更を破棄しますか。', '名称変更'); return true } catch { return false }
}
async function closeEditor(done?: () => void) {
  const token = intent.capture()
  if (busy.value || !await allowDiscard() || !intent.current(token)) return
  clearEditor(); done?.()
}
async function openName(row: MasterRow) {
  const allowed = () => canEdit.value && row.nameWritable && !pending.value && !busy.value && !storageBlocked.value && result.value?.items.includes(row)
  if (!allowed()) return
  const token = intent.capture()
  if (!await allowDiscard() || !intent.current(token) || !allowed()) return
  editing.value = { ...row }; name.value = rowName(row) ?? ''; clearName.value = false; error.value = ''; notice.value = ''
}
function finishPending(p: MasterLocator) {
  if (pending.value?.operationId !== p.operationId) throw new Error('PENDING_CHANGED')
  sessionStorage.removeItem(storageKey(p, p.kind)); pending.value = null; storageBlocked.value = false; clearEditor()
}
async function saveName() {
  const row = editing.value, owner = scope.value
  if (!row || !owner || !canEdit.value || !row.nameWritable || pending.value || busy.value || storageBlocked.value) return
  if ((!isLocation.value && !name.value.trim()) || name.value.length > 100 || /[\u0000-\u001f\u007f-\u009f]/.test(name.value)) { error.value = '名称の入力を確認してください。'; return }
  const token = intent.capture(), p: MasterLocator = { ...owner, kind: props.kind, objectId: row.id, operationId: crypto.randomUUID() }
  const patch = clearName.value && isLocation.value ? { name: { op: 'CLEAR' as const } } : { name: { op: 'SET' as const, value: name.value } }
  try { sessionStorage.setItem(storageKey(p, p.kind), JSON.stringify(p)) } catch { storageBlocked.value = true; error.value = '操作情報を保存できないため、送信していません。'; return }
  pending.value = p; busy.value = true; error.value = ''
  try {
    const response = await api.rename(p.kind, p.objectId, p.operationId, row.etag, patch)
    if (!intent.current(token) || !canQuery.value) return
    if (response.code !== 0) throw new Error('WRITE_UNCONFIRMED')
    const original = checkMasterResult(response.data, p); finishPending(p)
    notice.value = original.effect === 'NO_CHANGE' ? '元の操作は確定済みです。名称と行バージョンに変更はありません。' : '元の名称変更は確定済みです。現在の記録を再取得します。'
    await load(page.value)
  } catch { if (intent.current(token)) { clearEditor(); error.value = '変更結果は未確認です。再送せず、元の操作を確認してください。' } }
  finally { if (intent.current(token)) busy.value = false }
}
async function recover(close: boolean) {
  const p = pending.value
  if (!p || busy.value || !canQuery.value || (close && !canAbandon.value)) return
  const token = intent.capture()
  if (close) { try { await ElMessageBox.confirm('未確定の場合、元の操作を封じて遅延送信を拒否します。確定済みの場合は元の結果を返します。', '元の操作'); } catch { return } }
  if (!intent.current(token) || pending.value?.operationId !== p.operationId || !canQuery.value || (close && !canAbandon.value)) return
  busy.value = true; error.value = ''
  try {
    const response = close ? await api.close(p.kind, p.objectId, p.operationId) : await api.operation(p.kind, p.objectId, p.operationId)
    if (!intent.current(token) || !canQuery.value) return
    if (response.code !== 0) throw new Error('RECOVERY_FAILED')
    const original = checkMasterOperation(response.data, p)
    if (original.state === 'UNKNOWN') { notice.value = '元の結果はまだ確認できません。未実行とは判断できません。'; return }
    finishPending(p); notice.value = original.state === 'CLOSED' ? '元の操作は封鎖済みです。遅延送信は拒否されます。' : '元の操作は確定済みです。現在の記録を再取得します。'
    await load(page.value)
  } catch { if (intent.current(token)) error.value = '元の操作を確認できません。操作情報は保持しています。' }
  finally { if (intent.current(token)) busy.value = false }
}
watch(queryText, () => { clearRead(); page.value = 1 }, { flush: 'sync' })
watch(warehouseSearch, () => { choiceSequence++; choices.value = null }, { flush: 'sync' })
watch(warehouseId, () => { clearRead(); page.value = 1; if (warehouseId.value) void load(1) }, { flush: 'sync' })
watch(() => [props.kind, canQuery.value, canEdit.value, canAbandon.value], () => { intent.invalidate(); clearAll() }, { flush: 'sync' })
function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value || busy.value) { event.preventDefault(); event.returnValue = '' } }
onBeforeRouteLeave(() => busy.value ? false : allowDiscard())
onMounted(() => { window.addEventListener('beforeunload', beforeUnload); void initialize() })
onBeforeUnmount(() => { clearAll(); window.removeEventListener('beforeunload', beforeUnload) })
</script>
<style scoped>
.master-note { color: var(--cp-muted); font-size: var(--cp-fs-sm); }
</style>
