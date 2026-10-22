<template>
  <section aria-label="独立对象部门绑定" v-loading="busy">
    <h3>独立对象部门绑定</h3>
    <p>明确指定库存、库存流水、盘点单或无订单生产指示所属的组织部门。请先核实实际归属；不会根据当前操作人自动填入。</p>
    <p>停用绑定或其组织部门后，对应部门范围将不可用。全租户及本人范围仍按各自授权判断。</p>
    <el-button v-permission="'pub-dept:edit'" :disabled="busy || !!pending" @click="open()">新增绑定</el-button>
    <el-button :disabled="busy || editing" @click="load">刷新绑定</el-button>
    <p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending" role="status">
      有待核对的原操作，请先查询或封闭；不会自动重复保存。
      <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <table v-if="editor">
      <thead><tr><th>对象类型</th><th>对象</th><th>组织部门</th><th>当前状态</th><th>操作</th></tr></thead>
      <tbody><tr v-for="row in editor.mappings" :key="row.id">
        <td>{{ kindName(row.objectKind) }}</td><td>{{ row.objectName || '对象已不可用' }}</td><td>{{ departmentName(row.departmentId) }}</td>
        <td>{{ row.available ? '可用' : row.enabled ? '部门不可用' : '已停用' }}</td>
        <td><el-button v-permission="'pub-dept:edit'" :disabled="busy || !!pending" @click="open(row.id)">调整或停用</el-button></td>
      </tr></tbody>
    </table>
    <form v-if="editing && editor" @submit.prevent="save">
      <label>对象类型 <select v-model="draft.objectKind" aria-label="对象类型" :disabled="busy || !!draft.id || !!pending" @change="resetObject">
        <option v-for="kind in kinds" :key="kind.value" :value="kind.value">{{ kind.label }}</option>
      </select></label>
      <template v-if="!draft.id">
        <label>查找对象 <input v-model="search" aria-label="查找对象" maxlength="100" :disabled="busy || !!pending" /></label>
        <el-button :disabled="busy || !!pending || !search.trim()" @click="findObjects">查找</el-button>
        <label>对象 <select v-model="draft.objectId" aria-label="对象" required :disabled="busy || !!pending">
          <option value="" disabled>请选择已核实的对象</option>
          <option v-for="row in objects" :key="row.id" :value="row.id">{{ row.name }}</option>
        </select></label>
        <p>最多显示50项。库存可按仓库、库位、产品或批号查找；其他对象按业务单号查找。</p>
      </template>
      <p v-else>当前对象：{{ editor.mappings.find(row => row.id === draft.id)?.objectName || '对象已不可用' }}</p>
      <label>组织部门 <select v-model="draft.departmentId" aria-label="组织部门" required :disabled="busy || !!pending">
        <option value="" disabled>请选择已核实的组织部门</option>
        <option v-for="row in editor.departments" :key="row.id" :value="row.id" :disabled="!row.available && row.id !== draft.departmentId">{{ row.name }}{{ row.available ? '' : '（不可用）' }}</option>
      </select></label>
      <label><input v-model="draft.enabled" type="checkbox" aria-label="启用绑定" :disabled="busy || !!pending" />启用绑定</label>
      <label>变更理由 <input v-model="draft.reason" aria-label="绑定变更理由" required maxlength="256" :disabled="busy || !!pending" /></label>
      <p>更换组织部门会立即改变该对象的部门范围。盘点明细还需对应库存权限；关联订单的生产指示沿用订单归属。</p>
      <p v-if="conflict">资料已变化或原操作已封闭。请重新读取，核对保留的草稿后再次明确保存。</p>
      <el-button v-if="conflict" :disabled="busy || !!pending" @click="open(draft.id, true)">重新读取并保留草稿</el-button>
      <el-button v-permission="'pub-dept:edit'" native-type="submit" :disabled="busy || !!pending || conflict">确认保存绑定</el-button>
      <el-button :disabled="busy" @click="editing = false">关闭编辑</el-button>
    </form>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { iamApi, type IamStandaloneMappingEditor, type IamStandaloneMappingInput, type IamScope, type StandaloneKind, type StandaloneObjectChoice } from '@/api/sys/iam'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, AuthenticationChangedError, identityStamp, requireIdentity } from '@/auth/identity'

const editor = ref<IamStandaloneMappingEditor | null>(null), scope = ref<IamScope | null>(null)
const busy = ref(false), editing = ref(false), conflict = ref(false), notice = ref('')
const pending = ref<PendingIamOperation | null>(null)
const empty = (): IamStandaloneMappingInput => ({ objectKind: 'stock', objectId: '', departmentId: '', enabled: true, expectedVersion: 'absent', expectedReferencesVersion: '', reason: '' })
const draft = reactive(empty())
const kinds: { value: StandaloneKind; label: string }[] = [
  { value: 'stock', label: '库存' }, { value: 'stock-transaction', label: '库存流水' },
  { value: 'stocktake', label: '盘点单' }, { value: 'work-order', label: '无订单生产指示' }
]
const search = ref(''), objects = ref<StandaloneObjectChoice[]>([])
function kindName(kind: StandaloneKind) { return kinds.find(row => row.value === kind)?.label || kind }
function resetObject() { draft.objectId = ''; objects.value = []; search.value = '' }
async function findObjects() {
  if (busy.value || pending.value || draft.id || !search.value.trim()) return
  const request = generation, stamp = identityStamp(), kind = draft.objectKind; busy.value = true
  objects.value = []; draft.objectId = ''
  try {
    const rows = await iamApi.standaloneObjects(kind, search.value); requireIdentity(stamp)
    if (request === generation && kind === draft.objectKind) objects.value = rows
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
let generation = 0, currentStamp = identityStamp()
function departmentName(id: string) { return editor.value?.departments.find(d => d.id === id)?.name || '不可用' }
function syncPending() { pending.value = scope.value ? pendingIamOperation(scope.value) : null }
function invalidate() {
  generation++; editor.value = null; scope.value = null; pending.value = null; busy.value = false
  editing.value = false; conflict.value = false; Object.assign(draft, empty()); draft.id = undefined; resetObject()
  notice.value = '当前身份或权限已改变，已清除页面资料；请重新读取。'
}
function fail(error: any) {
  if (error instanceof AuthenticationChangedError || [401, 403].includes(error?.response?.status)) invalidate()
  else notice.value = error?.response?.data?.code || error?.message || '操作未完成，请核对当前状态。'
}
async function read(request: number, stamp: string) {
  const value = await iamApi.standaloneMappings(); requireIdentity(stamp)
  if (request !== generation) return null
  editor.value = value; scope.value = value; syncPending(); return value
}
async function load() {
  if (busy.value || editing.value) return
  const request = ++generation, stamp = identityStamp(); busy.value = true; editor.value = null
  try { await read(request, stamp) } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function open(id?: string, keep = false) {
  if (busy.value || pending.value) return
  const request = ++generation, stamp = identityStamp(); busy.value = true
  try {
    const value = await read(request, stamp); if (!value) return
    const row = value.mappings.find(m => m.id === id)
    if (id && !row) throw new Error('原绑定已不可用，请刷新后核对。')
    if (!keep) { Object.assign(draft, empty(), row ? { id: row.id, objectKind: row.objectKind, objectId: row.objectId, departmentId: row.departmentId, enabled: row.enabled } : { id: undefined }) }
    draft.expectedVersion = row?.version || 'absent'; draft.expectedReferencesVersion = value.referencesVersion
    if (!keep) { objects.value = []; search.value = '' }
    conflict.value = false; editing.value = true
    notice.value = keep ? '已保留草稿，请核对最新资料后明确保存。' : ''
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function refreshCommitted(request: number, stamp: string) {
  editing.value = false; editor.value = null; notice.value = '原操作已保存，不再重放。'
  try { await read(request, stamp) }
  catch (error: any) {
    if (request !== generation) return
    if (error instanceof AuthenticationChangedError || [401, 403].includes(error?.response?.status)) fail(error)
    else notice.value += ' 绑定列表刷新失败，请点击刷新绑定。'
  }
}
async function save() {
  if (busy.value || pending.value || conflict.value || !editing.value || !scope.value || !draft.objectId || !draft.departmentId || !draft.reason.trim()) return
  const request = generation, stamp = identityStamp(), input = { ...draft }; busy.value = true
  try {
    const result = await saveIamOperation(scope.value, 'dept-standalone-mapping', input.id, id => iamApi.saveStandaloneMapping(input, id))
    requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted(request, stamp)
  } catch (error: any) { if (request === generation) { conflict.value = error?.response?.status === 412; fail(error) } }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
async function resolve(close: boolean) {
  if (!scope.value || busy.value) return
  const request = generation, stamp = identityStamp(); busy.value = true
  try {
    const result = await resolveIamOperation(scope.value, close); requireIdentity(stamp)
    if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted(request, stamp)
    else if (result.state === 'CLOSED') { conflict.value = true; notice.value = '原操作已封闭。请重新读取后核对，不会自动保存草稿。' }
    else notice.value = '原操作尚未确认，请继续查询或封闭。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
function changed() {
  if (currentStamp !== identityStamp()) { currentStamp = identityStamp(); invalidate() }
  else try { syncPending() } catch (error) { fail(error) }
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void load() })
onBeforeUnmount(() => { generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>

<style scoped>
section { border: 1px solid var(--el-border-color); padding: 16px; margin-bottom: 16px; }
table { width: 100%; text-align: left; margin-top: 12px; } td, th { padding: 8px; }
form { display: grid; gap: 12px; margin-top: 16px; }
label { display: flex; gap: 10px; align-items: center; }
input:not([type='checkbox']), select { padding: 7px; min-width: 240px; }
</style>
