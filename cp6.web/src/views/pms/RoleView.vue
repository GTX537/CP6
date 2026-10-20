<template>
  <section v-loading="busy" aria-label="角色管理">
    <div class="toolbar">
      <el-button :disabled="busy" @click="load">刷新角色</el-button>
      <el-button v-permission="'role:add'" type="primary" :disabled="busy || !!pending" @click="open()">新增角色</el-button>
    </div>
    <el-alert v-if="notice && !visible" :title="notice" :closable="false" role="status" />
    <div v-if="pending && !visible" role="status" class="recovery">
      上一次操作尚待核对，不能再次保存。
      <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <el-table :data="editor?.roles || []" row-key="roleId">
      <el-table-column prop="roleId" label="角色编号" />
      <el-table-column prop="roleName" label="角色名称" />
      <el-table-column prop="description" label="说明" />
      <el-table-column label="状态"><template #default="{ row }">{{ row.enable ? '启用' : '停用' }}</template></el-table-column>
      <el-table-column label="操作"><template #default="{ row }">
        <el-button v-permission="'role:edit'" link :disabled="busy || !!pending" @click="open(row.roleId)">编辑角色</el-button>
        <el-button :disabled="busy" link @click="references(row.roleId)">查看引用</el-button>
      </template></el-table-column>
    </el-table>
    <p>角色编号创建后不可更改。停用保留关联与历史记录；硬删除需完整留存审查。</p>
    <p v-if="refs" role="status">角色 {{ refs.roleId }}：主角色用户 {{ refs.primaryUsers }} 人，附加关联 {{ refs.additionalUsers }} 项，
      菜单 {{ refs.menus }} 项，动作 {{ refs.actions }} 项，数据范围 {{ refs.dataScopes }} 项，字段配置 {{ refs.fields }} 项，
      SSO 默认引用 {{ refs.ssoDefaults }} 项，SSO 已批准角色 {{ refs.ssoApproved ? '是' : '否' }}。历史业务、审批及留存约束尚不能完整核实，当前不可硬删除。</p>
    <el-dialog v-model="visible" :title="creating ? '新增角色' : '编辑角色'" width="600px" :close-on-click-modal="false">
      <el-alert v-if="notice && visible" :title="notice" :closable="false" role="status" />
      <div v-if="pending && visible" role="status" class="recovery">
        上一次操作尚待核对，不能再次保存。
        <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
        <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
      </div>
      <form @submit.prevent="save">
        <label>角色编号 <input v-model.number="draft.roleId" type="number" min="-2147483648" max="2147483647" step="1" required :disabled="!creating || !!pending" /></label>
        <label>角色名称 <input v-model="draft.roleName" required maxlength="100" :disabled="!!pending" /></label>
        <label>说明 <textarea v-model="draft.description" maxlength="500" :disabled="!!pending" /></label>
        <label>排序 <input v-model.number="draft.orderNo" type="number" min="-2147483648" max="2147483647" step="1" required :disabled="!!pending" /></label>
        <label><input v-model="draft.enable" type="checkbox" :disabled="!!pending" />启用</label>
        <label>变更理由 <input v-model="draft.reason" required maxlength="256" :disabled="!!pending" /></label>
        <p v-if="conflict">角色或引用版本已改变。重新读取后请核对草稿，再明确确认保存。</p>
        <el-button v-if="conflict" :disabled="busy || !!pending" @click="open(draft.roleId, true)">重新读取并保留草稿</el-button>
        <el-button type="primary" native-type="submit" :disabled="busy || !!pending || conflict">确认保存</el-button>
        <el-button :disabled="busy" @click="visible = false">关闭</el-button>
      </form>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { iamApi, type IamRoleEditor, type IamRoleInput, type IamRoleReferences } from '@/api/sys/iam'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
const editor = ref<IamRoleEditor | null>(null), refs = ref<IamRoleReferences | null>(null), pending = ref<PendingIamOperation | null>(null)
const busy = ref(false), visible = ref(false), creating = ref(false), conflict = ref(false), notice = ref('')
const empty = (): IamRoleInput => ({ roleId: 0, roleName: '', description: null, enable: true, orderNo: 0, expectedVersion: 'absent', expectedReferencesVersion: '', reason: '' })
const draft = reactive(empty())
let generation = 0, currentStamp = identityStamp()
function syncPending() { pending.value = editor.value ? pendingIamOperation(editor.value) : null }
function clear(text: string) { generation++; editor.value = null; refs.value = null; pending.value = null; visible.value = false; busy.value = false; Object.assign(draft, empty()); conflict.value = false; notice.value = text }
function fail(error: any) {
  if ([401, 403].includes(error?.response?.status)) clear('当前身份或权限不可用，已清除页面资料；请重新进入。')
  else notice.value = error?.response?.data?.code || error?.message || '操作失败，请核对状态后继续。'
}
async function load() {
  const request = ++generation, stamp = identityStamp(); busy.value = true
  try { const value = await iamApi.roleEditor(); requireIdentity(stamp); if (request !== generation) return; editor.value = value; syncPending() }
  catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function open(id?: number, keep = false) {
  const request = ++generation, stamp = identityStamp(); busy.value = true
  try {
    const value = await iamApi.roleEditor(); requireIdentity(stamp); if (request !== generation) return
    const create = keep ? creating.value : id === undefined, role = create ? undefined : value.roles.find(r => r.roleId === id)
    if (!create && !role) throw new Error('角色已不可用，请刷新角色列表。')
    editor.value = value; syncPending(); creating.value = create
    if (!keep) Object.assign(draft, empty(), role ? { roleId: role.roleId, roleName: role.roleName, description: role.description, enable: role.enable, orderNo: role.orderNo } : {})
    draft.expectedVersion = role?.version || 'absent'; draft.expectedReferencesVersion = value.referencesVersion
    conflict.value = false; visible.value = true; notice.value = keep ? '已读取当前版本并保留草稿，请核对后再次确认。' : ''
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
async function refreshAfterCommit(message: string) {
  notice.value = message; visible.value = false
  const request = generation, stamp = identityStamp()
  try { const value = await iamApi.roleEditor(); requireIdentity(stamp); if (request === generation) editor.value = value }
  catch (error: any) { if (request === generation) { if ([401, 403].includes(error?.response?.status)) fail(error); else notice.value = message + ' 列表刷新失败，可单独刷新。' } }
}
async function save() {
  if (busy.value || pending.value || conflict.value || !editor.value) return
  const request = generation, stamp = identityStamp(), input = { ...draft }, create = creating.value; busy.value = true
  try {
    const result = await saveIamOperation(editor.value, create ? 'role-create' : 'role-update', undefined, id => iamApi.saveRole(input, create, id))
    requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshAfterCommit('角色已保存。')
  } catch (error: any) { if (request === generation) { conflict.value = error?.response?.status === 412; fail(error) } }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
async function resolve(close: boolean) {
  if (busy.value || !editor.value) return
  const request = generation, stamp = identityStamp(); busy.value = true
  try {
    const result = await resolveIamOperation(editor.value, close); requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshAfterCommit('原操作已保存，不再重放。')
    else if (result.state === 'CLOSED') { conflict.value = true; notice.value = '原操作已封闭，请重新读取当前版本并核对草稿。' }
    else notice.value = '尚未确认原操作结果；可继续查询或封闭，不能重新保存。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
async function references(id: number) {
  const request = ++generation, stamp = identityStamp(); busy.value = true; refs.value = null
  try { const value = await iamApi.roleReferences(id); requireIdentity(stamp); if (request === generation) refs.value = value }
  catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) busy.value = false }
}
function changed() {
  if (currentStamp !== identityStamp()) { currentStamp = identityStamp(); clear('登录身份已改变，请重新进入角色管理。'); return }
  try { syncPending() } catch (error) { clear('原操作记录无法读取，请核对后重新进入。') }
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void load() })
onBeforeUnmount(() => { generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 16px; }
form { display: grid; gap: 14px; }
label { display: flex; align-items: center; gap: 10px; }
input:not([type='checkbox']), textarea { flex: 1; min-width: 0; padding: 7px; }
.recovery { padding: 12px 0; }
</style>
