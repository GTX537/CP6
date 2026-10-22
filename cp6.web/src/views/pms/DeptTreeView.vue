<template>
  <section v-loading="busy" aria-label="组织部门管理">
    <div class="toolbar">
      <h2>部门管理</h2>
      <el-button v-permission="'pub-dept:add'" type="primary" :disabled="busy || !!pending" @click="open('dept-create')">新增根部门</el-button>
      <el-button :disabled="busy || visible" @click="load">刷新目录</el-button>
      <el-button v-permission="'pub-dept:query'" :disabled="busy || visible" @click="mappingVisible = !mappingVisible">订单部门绑定</el-button>
    </div>
    <OrderDepartmentMappings v-if="mappingVisible" />
    <el-button v-permission="'pub-dept:query'" :disabled="busy || visible" @click="standaloneVisible = !standaloneVisible">独立对象部门绑定</el-button>
    <StandaloneDepartmentMappings v-if="standaloneVisible" />
    <el-alert v-if="notice && !visible" :title="notice" :closable="false" role="status" />
    <div v-if="pending && !visible" class="recovery" role="status">
      有待核对的原操作。先查询或封闭，不能重复保存或再次移动。
      <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <el-table :data="tree" row-key="id" border default-expand-all :tree-props="{ children: 'children' }">
      <el-table-column prop="deptCode" label="部门编码" width="160" />
      <el-table-column prop="deptName" label="部门名称" min-width="180" />
      <el-table-column label="负责人" min-width="130"><template #default="{ row }">{{ leaderName(row.leaderId) }}</template></el-table-column>
      <el-table-column label="状态" width="160"><template #default="{ row }">{{ row.available ? '可用' : row.enable ? '祖先不可用' : '已停用' }}</template></el-table-column>
      <el-table-column label="操作" min-width="310"><template #default="{ row }">
        <el-button v-permission="'pub-dept:add'" link :disabled="busy || !!pending || !row.available" @click="open('dept-create', row.id)">新增子部门</el-button>
        <el-button v-permission="'pub-dept:edit'" link :disabled="busy || !!pending" @click="open('dept-update', row.id)">编辑</el-button>
        <el-button v-permission="'pub-dept:edit'" link :disabled="busy || !!pending" @click="open('dept-move', row.id)">移动</el-button>
        <el-button link :disabled="busy" @click="showReferences(row.id)">引用与删除限制</el-button>
      </template></el-table-column>
    </el-table>
    <aside v-if="anomalies.length" role="alert">
      <h3>目录数据异常</h3>
      <p>以下节点的父链或路径异常，不能作为有效部门范围。请交管理员按原数据依据修复。</p>
      <ul><li v-for="row in anomalies" :key="row.id">{{ row.deptCode }} · {{ row.deptName }}</li></ul>
    </aside>
    <el-dialog v-model="visible" :title="mode === 'dept-move' ? '移动部门' : draft.id ? '编辑部门' : '新增部门'" width="660px">
      <el-alert v-if="notice && visible" :title="notice" :closable="false" role="status" />
      <div v-if="pending" class="recovery" role="status">
        有待核对的原操作。先查询或封闭，不能重复保存或再次移动。
        <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
        <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
      </div>
      <form v-if="editor" @submit.prevent="save">
        <template v-if="mode !== 'dept-move'">
          <label>上级部门 <span>{{ departmentName(draft.parentId) }}</span></label>
          <label>部门编码 <input v-model="draft.deptCode" aria-label="部门编码" required maxlength="50" :disabled="!!draft.id || !!pending" /></label>
          <label>部门名称 <input v-model="draft.deptName" aria-label="部门名称" required maxlength="100" :disabled="!!pending" /></label>
          <label>负责人 <select v-model="draft.leaderId" aria-label="负责人" :disabled="!!pending"><option :value="null">未设置</option>
            <option v-for="user in editor.leaders" :key="user.id" :value="user.id" :disabled="!user.enable && user.id !== originalLeader">
              {{ user.nickName || user.userName }}{{ user.enable ? '' : '（已停用）' }}
            </option>
          </select></label>
          <label>排序 <input v-model.number="draft.sort" type="number" step="1" aria-label="排序" :disabled="!!pending" /></label>
          <label><input v-model="draft.enable" type="checkbox" :disabled="!!pending" />启用（停用后本部门及依赖其祖先链的范围不可用）</label>
        </template>
        <template v-else>
          <p>移动部门：{{ departmentName(draft.id || null) }}</p>
          <p>原上级：{{ departmentName(preview ? preview.oldParentId : originalParent) }}</p>
          <label>新上级 <select v-model="draft.parentId" aria-label="新上级" :disabled="!!pending"><option :value="null">根部门</option>
            <option v-for="row in editor.departments" :key="row.id" :value="row.id" :disabled="!row.available || descendantIds.has(row.id)">{{ row.deptName }}{{ row.available ? '' : '（不可用）' }}</option>
          </select></label>
          <el-button :disabled="busy || !!pending || conflict" @click="prepareMove">预览移动影响</el-button>
          <div v-if="preview" class="preview" role="status">
            <p>受影响子部门 {{ preview.affectedDescendantCount }} 个，用户 {{ preview.affectedUsersCount }} 人，范围引用 {{ preview.scopeRefs.length }} 项。</p>
            <p>历史审批快照保留原记录。当前审批路由随组织变化；下游采用状态需另行核对。</p>
            <ul v-if="preview.scopeRefs.length"><li v-for="(item, index) in preview.scopeRefs" :key="index">角色 {{ item.roleId }} · {{ item.resourceKey }} · 范围 {{ item.scopeType }}</li></ul>
          </div>
        </template>
        <label>变更理由 <input v-model="draft.reason" aria-label="变更理由" required maxlength="256" :disabled="!!pending" /></label>
        <p v-if="conflict">组织或引用版本已变动。封闭原操作后重新读取并核对；移动须再次预览。</p>
        <el-button v-if="conflict" :disabled="busy || !!pending" @click="reloadDraft">重新读取并保留草稿</el-button>
        <el-button type="primary" native-type="submit" :disabled="busy || !!pending || conflict || mode === 'dept-move' && !preview">{{ mode === 'dept-move' ? '确认移动' : '确认保存' }}</el-button>
        <el-button @click="visible = false">关闭</el-button>
      </form>
    </el-dialog>
    <el-dialog v-model="referencesVisible" title="部门引用与删除限制" width="560px">
      <template v-if="references">
        <p>直接子部门 {{ references.childCount }} 个，用户引用 {{ references.userCount }} 个，范围引用 {{ references.scopeRefs.length }} 项。</p>
        <p>业务引用、历史审批及隐私保留条件尚未完成统一核验，当前禁止物理删除。可在核对影响后停用部门，保留历史引用。</p>
      </template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { iamApi, type IamDepartment, type IamDepartmentEditor, type IamDepartmentInput, type IamDepartmentPreview, type IamDepartmentReferences, type IamScope } from '@/api/sys/iam'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import OrderDepartmentMappings from './OrderDepartmentMappings.vue'
import StandaloneDepartmentMappings from './StandaloneDepartmentMappings.vue'

type Mode = 'dept-create' | 'dept-update' | 'dept-move'
type Node = IamDepartment & { children: Node[] }
const editor = ref<IamDepartmentEditor | null>(null), scope = ref<IamScope | null>(null), pending = ref<PendingIamOperation | null>(null)
const busy = ref(false), visible = ref(false), notice = ref(''), conflict = ref(false), mode = ref<Mode>('dept-create')
const mappingVisible = ref(false), standaloneVisible = ref(false)
const preview = ref<IamDepartmentPreview | null>(null), references = ref<IamDepartmentReferences | null>(null), referencesVisible = ref(false)
const originalParent = ref<string | null>(null), originalLeader = ref<string | null>(null)
const empty = (): IamDepartmentInput => ({ parentId: null, deptCode: '', deptName: '', leaderId: null, sort: 0, enable: true, expectedVersion: 'absent', expectedTreeVersion: '', expectedReferencesVersion: '', reason: '' })
const draft = reactive<IamDepartmentInput>(empty())
let generation = 0, previewGeneration = 0, referenceGeneration = 0
const tree = computed(() => {
  const nodes = new Map<string, Node>((editor.value?.departments || []).filter(d => !d.diagnostic).map(d => [d.id, { ...d, children: [] }]))
  const roots: Node[] = []
  for (const row of nodes.values()) { if (row.parentId === null) roots.push(row); else nodes.get(row.parentId)?.children.push(row) }
  return roots
})
const anomalies = computed(() => editor.value?.departments.filter(d => d.diagnostic) || [])
const descendantIds = computed(() => {
  const ids = new Set<string>(draft.id ? [draft.id] : []), queue = [...ids]
  for (let index = 0; index < queue.length; index++) for (const row of editor.value?.departments || []) {
    if (row.parentId === queue[index] && !ids.has(row.id)) { ids.add(row.id); queue.push(row.id) }
  }
  return ids
})
function departmentName(id: string | null) { return id === null ? '根部门' : editor.value?.departments.find(d => d.id === id)?.deptName || '节点已不可用' }
function leaderName(id: string | null) { const user = editor.value?.leaders.find(u => u.id === id); return user ? user.nickName || user.userName : '未设置或已不可用' }
function message(error: any) { return error?.response?.data?.code || error?.message || '操作未完成，请核对当前状态。' }
function syncPending() { pending.value = scope.value ? pendingIamOperation(scope.value) : null }
function invalidate(text: string) {
  generation++; previewGeneration++; referenceGeneration++; editor.value = null; scope.value = null; pending.value = null
  visible.value = false; referencesVisible.value = false; references.value = null; preview.value = null; busy.value = false
  Object.assign(draft, empty()); draft.id = undefined; conflict.value = false; notice.value = text
}
function fail(error: any) {
  if ([401, 403].includes(error?.response?.status)) invalidate('当前身份或权限不可用，已清除页面资料；请重新进入。')
  else notice.value = message(error)
}
async function read(request: number, stamp: string) {
  const value = await iamApi.departmentEditor(); requireIdentity(stamp)
  if (request !== generation) return null
  editor.value = value; scope.value = value; syncPending(); return value
}
async function refreshCommitted(request: number, stamp: string) {
  const saved = notice.value
  try { await read(request, stamp) }
  catch (error: any) {
    if (request !== generation) return
    if ([401, 403].includes(error?.response?.status)) fail(error)
    else notice.value = saved + ' 目录刷新失败，可点击刷新目录；无需再次保存。'
  }
}
async function load() {
  if (busy.value) return
  const request = ++generation, stamp = identityStamp(); busy.value = true
  try { await read(request, stamp) } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function open(kind: Mode, id?: string, keep = false) {
  if (busy.value || pending.value) return
  const request = ++generation, stamp = identityStamp(); busy.value = true; preview.value = null; previewGeneration++
  try {
    const value = await read(request, stamp); if (!value) return
    const selected = value.departments.find(d => d.id === id)
    if (id && !selected || selected?.diagnostic) throw new Error('部门已不可用，请核对目录异常。')
    if (!keep) {
      Object.assign(draft, empty()); draft.id = kind === 'dept-create' ? undefined : id
      if (kind === 'dept-create') draft.parentId = id || null
      else if (selected) Object.assign(draft, { deptCode: selected.deptCode, deptName: selected.deptName, parentId: selected.parentId, leaderId: selected.leaderId, sort: selected.sort, enable: selected.enable })
    }
    if (kind === 'dept-update' && selected) { draft.parentId = selected.parentId; draft.deptCode = selected.deptCode }
    draft.expectedVersion = kind === 'dept-create' ? 'absent' : selected!.version
    draft.expectedTreeVersion = value.treeVersion; draft.expectedReferencesVersion = value.referencesVersion
    originalParent.value = selected?.parentId || null; originalLeader.value = selected?.leaderId || null
    mode.value = kind; conflict.value = false; visible.value = true
    if (keep) notice.value = '已读取当前版本并保留草稿。请核对当前资料与选择，移动需重新预览。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function reloadDraft() { await open(mode.value, mode.value === 'dept-create' ? draft.parentId || undefined : draft.id, true) }
watch(() => draft.parentId, () => { previewGeneration++; preview.value = null })
async function prepareMove() {
  if (busy.value || pending.value || conflict.value || mode.value !== 'dept-move' || !draft.id) return
  const request = generation, revision = ++previewGeneration, stamp = identityStamp(), id = draft.id, parent = draft.parentId; busy.value = true
  try {
    const value = await iamApi.departmentPreview(id, parent); requireIdentity(stamp)
    if (request !== generation || revision !== previewGeneration || draft.parentId !== parent) return
    if (value.id !== id || value.newParentId !== parent) throw new Error('预览与当前移动目标不一致，请重新读取。')
    preview.value = value; draft.expectedVersion = value.version; draft.expectedTreeVersion = value.treeVersion; draft.expectedReferencesVersion = value.referencesVersion
  } catch (error) { if (request === generation && revision === previewGeneration) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function save() {
  if (busy.value || pending.value || conflict.value || !scope.value || mode.value === 'dept-move' && (!preview.value || preview.value.newParentId !== draft.parentId || preview.value.id !== draft.id)) return
  const request = generation, stamp = identityStamp(), kind = mode.value
  const input = { ...draft, previewVersion: kind === 'dept-move' ? preview.value!.previewVersion : undefined }; busy.value = true
  try {
    const result = await saveIamOperation(scope.value, kind, draft.id, id => iamApi.saveDepartment(input, kind, id)); requireIdentity(stamp)
    if (request !== generation) return
    if (result.state === 'COMMITTED') { visible.value = false; notice.value = 'Main 已保存组织变更。下游投影与业务采用需另行确认。'; await refreshCommitted(request, stamp) }
  } catch (error: any) { if (request === generation) { conflict.value = error?.response?.status === 412; preview.value = null; fail(error) } }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
async function resolve(close: boolean) {
  if (!scope.value || busy.value) return
  const request = generation, stamp = identityStamp(); busy.value = true
  try {
    const result = await resolveIamOperation(scope.value, close); requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') { visible.value = false; notice.value = '原操作已在 Main 保存，不再重放。'; await refreshCommitted(request, stamp) }
    else if (result.state === 'CLOSED') { conflict.value = true; preview.value = null; notice.value = '原操作已封闭。重新读取当前版本，核对并再次预览后才能提交。' }
    else notice.value = '原操作结果仍未确认，请继续查询或封闭。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
async function showReferences(id: string) {
  const request = generation, revision = ++referenceGeneration, stamp = identityStamp()
  try { const value = await iamApi.departmentReferences(id); requireIdentity(stamp); if (request === generation && revision === referenceGeneration) { references.value = value; referencesVisible.value = true } }
  catch (error) { if (request === generation && revision === referenceGeneration) fail(error) }
}
let currentStamp = identityStamp()
function changed() {
  if (identityStamp() !== currentStamp) { currentStamp = identityStamp(); invalidate('登录身份已改变，请重新进入部门管理。') }
  else try { syncPending() } catch (error) { notice.value = message(error) }
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void load() })
onBeforeUnmount(() => { generation++; previewGeneration++; referenceGeneration++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>

<style scoped>
section { padding: 20px; }
.toolbar { display: flex; gap: 12px; align-items: center; margin-bottom: 16px; }
h2 { margin: 0 auto 0 0; }
form { display: grid; gap: 14px; }
label { display: flex; align-items: center; gap: 10px; }
input:not([type='checkbox']), select { flex: 1; min-width: 0; padding: 7px; }
.recovery, .preview, aside { padding: 14px; margin: 10px 0; background: var(--el-fill-color-light); }
</style>
