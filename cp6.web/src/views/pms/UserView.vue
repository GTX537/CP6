<template>
  <section v-loading="busy" aria-label="用户与角色管理">
    <div class="toolbar">
      <el-input v-model="keyword" aria-label="搜索用户" placeholder="搜索用户" @keyup.enter="loadList" />
      <el-button @click="loadList">查询</el-button>
      <el-button v-permission="'user:add'" type="primary" :disabled="!!pending" @click="openEditor()">新增用户</el-button>
    </div>
    <el-alert v-if="notice" :title="notice" :closable="false" role="status" />
    <div v-if="pending" class="recovery" role="status">
      上一次操作尚待核对。查询结果或封闭原操作后才能再次保存。
      <el-button :disabled="busy" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="busy" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <el-table :data="rows" row-key="id">
      <el-table-column prop="userName" label="用户名" />
      <el-table-column prop="nickName" label="显示名" />
      <el-table-column prop="email" label="邮箱" />
      <el-table-column label="状态"><template #default="{ row }">{{ row.enable ? '启用' : '停用' }}</template></el-table-column>
      <el-table-column label="操作"><template #default="{ row }">
        <el-button v-permission="'user:edit'" link :disabled="!!pending" @click="openEditor(row.id)">编辑资料与角色</el-button>
      </template></el-table-column>
    </el-table>
    <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev, pager, next, total" @current-change="loadList" />
    <p>停用保留历史身份；删除需通过受控留存审查。</p>
    <el-dialog v-model="visible" :title="draft.id ? '编辑用户与角色' : '新增用户'" width="660px" :close-on-click-modal="false" @closed="clearPassword">
      <form v-if="editor" @submit.prevent="save">
        <label>用户名 <input v-model="draft.userName" required maxlength="100" :disabled="!!pending" /></label>
        <label>显示名 <input v-model="draft.nickName" maxlength="100" :disabled="!!pending" /></label>
        <label>邮箱 <input v-model="draft.email" maxlength="100" :disabled="!!pending" /></label>
        <label><input v-model="draft.enable" type="checkbox" :disabled="!!pending" />启用</label>
        <fieldset :disabled="!!pending"><legend>角色（主角色必须包含于已选角色）</legend>
          <label v-for="role in editor.roles" :key="role.roleId">
            <input v-model="draft.roleIds" type="checkbox" :value="role.roleId" :disabled="!role.enable && !originalRoleIds.includes(role.roleId)" />
            {{ role.roleName }}{{ role.enable ? '' : '（已停用，仅保留历史关联）' }}
          </label>
          <label>主角色 <select v-model="draft.roleId" aria-label="主角色"><option :value="null">无</option>
            <option v-for="role in primaryRoles" :key="role.roleId" :value="role.roleId">{{ role.roleName }}</option>
          </select></label>
        </fieldset>
        <label>所属部门 <select v-model="draft.deptId" aria-label="所属部门" :disabled="!!pending"><option :value="null">未设置</option>
          <option v-for="dept in editor.departments" :key="dept.id" :value="dept.id" :disabled="!dept.available && dept.id !== editor.user?.deptId">
            {{ dept.deptName }}{{ dept.available ? '' : '（不可新分配）' }}
          </option>
        </select></label>
        <label>直属上级 <select v-model="draft.managerId" aria-label="直属上级" :disabled="!!pending"><option :value="null">未设置</option>
          <option v-for="user in editor.managers" :key="user.id" :value="user.id" :disabled="user.id === draft.id || !user.enable && user.id !== editor.user?.managerId">
            {{ user.nickName || user.userName }}{{ user.enable ? '' : '（已停用）' }}
          </option>
        </select></label>
        <label v-if="draft.id"><input v-model="resetPassword" type="checkbox" :disabled="!!pending" @change="clearPassword" />明确重置密码（强制改密并吊销旧会话）</label>
        <label v-if="!draft.id || resetPassword">{{ draft.id ? '新密码' : '初始密码' }}
          <input v-model="draft.password" type="password" autocomplete="new-password" maxlength="200" required :disabled="!!pending" />
        </label>
        <label>变更理由 <input v-model="draft.reason" required maxlength="256" :disabled="!!pending" /></label>
        <p v-if="conflict">资料或引用版本已改变。可保留非敏感草稿重新读取，核对后再次确认。密码不会保留。</p>
        <el-button v-if="conflict" :disabled="busy || !!pending" @click="reloadDraft">重新读取并保留草稿</el-button>
        <el-button type="primary" native-type="submit" :disabled="busy || !!pending || conflict">确认保存</el-button>
        <el-button @click="visible = false">关闭</el-button>
      </form>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { userApi } from '@/api/sys/user'
import { iamApi, type IamScope, type IamUserEditor, type IamUserInput } from '@/api/sys/iam'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'

const rows = ref<any[]>([]), total = ref(0), page = ref(1), keyword = ref('')
const busy = ref(false), notice = ref(''), visible = ref(false), conflict = ref(false), resetPassword = ref(false)
const editor = ref<IamUserEditor | null>(null), scope = ref<IamScope | null>(null), pending = ref<PendingIamOperation | null>(null)
const empty = (): IamUserInput => ({ userName: '', nickName: '', email: '', password: '', enable: true, roleId: null, roleIds: [], deptId: null, managerId: null, expectedVersion: 'absent', expectedReferencesVersion: '', reason: '' })
const draft = reactive<IamUserInput>(empty())
const originalRoleIds = computed(() => editor.value?.user?.roleIds || [])
const primaryRoles = computed(() => editor.value?.roles.filter(r => draft.roleIds.includes(r.roleId)) || [])
let generation = 0, listGeneration = 0
watch(() => [...draft.roleIds], ids => { if (draft.roleId !== null && !ids.includes(draft.roleId)) draft.roleId = null })
function clearPassword() { draft.password = '' }
function syncPending() { pending.value = scope.value ? pendingIamOperation(scope.value) : null }
function errorMessage(error: any) { return error?.response?.data?.code || error?.message || '操作失败，请核对状态后继续。' }
async function loadList() {
  const request = ++listGeneration, stamp = identityStamp()
  try {
    const value: any = await userApi.getList({ page: page.value, pageSize: 20, keyword: keyword.value })
    requireIdentity(stamp); if (request !== listGeneration) return
    rows.value = value.rows || []; total.value = value.total || 0
  } catch (error) { if (request === listGeneration) notice.value = errorMessage(error) }
}
async function openEditor(id?: string, keepDraft = false) {
  const request = ++generation, stamp = identityStamp(); busy.value = true; clearPassword()
  try {
    const value = await iamApi.userEditor(id); requireIdentity(stamp); if (request !== generation) return
    editor.value = value; scope.value = value; syncPending()
    if (!keepDraft) {
      const user = value.user
      Object.assign(draft, empty(), user ? { userName: user.userName, nickName: user.nickName, email: user.email,
        enable: user.enable, roleId: user.roleId, roleIds: [...user.roleIds], deptId: user.deptId, managerId: user.managerId } : {})
      draft.id = value.user?.id; resetPassword.value = false
    }
    draft.expectedVersion = value.user?.version || 'absent'; draft.expectedReferencesVersion = value.referencesVersion
    conflict.value = false; visible.value = true
    if (keepDraft) notice.value = '已读取当前版本并保留非敏感草稿，请核对角色与组织选择后再次确认。'
  } catch (error) { if (request === generation) notice.value = errorMessage(error) }
  finally { if (request === generation) busy.value = false }
}
async function reloadDraft() { await openEditor(draft.id, true) }
async function save() {
  if (busy.value || pending.value || conflict.value || !scope.value) return
  const request = generation, stamp = identityStamp(); busy.value = true
  const input = { ...draft, roleIds: [...draft.roleIds], password: !draft.id || resetPassword.value ? draft.password : undefined }
  try {
    const result = await saveIamOperation(scope.value, draft.id ? 'user-update' : 'user-create', draft.id, id => iamApi.saveUser(input, id))
    requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') { visible.value = false; notice.value = 'Main 已保存。下游投影状态需由对应系统确认。'; await loadList() }
  } catch (error: any) {
    if (request === generation) { conflict.value = error?.response?.status === 412; notice.value = `${errorMessage(error)}；请查询或封闭原操作。` }
  } finally { input.password = undefined; clearPassword(); if (request === generation) { syncPending(); busy.value = false } }
}
async function resolve(close: boolean) {
  if (!scope.value || busy.value) return
  const request = generation, stamp = identityStamp(); busy.value = true
  try {
    const result = await resolveIamOperation(scope.value, close); requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') { visible.value = false; notice.value = '原操作已在 Main 保存，不再重放。'; await loadList() }
    else if (result.state === 'CLOSED') { conflict.value = true; notice.value = '原操作已封闭。请重新读取当前版本，核对草稿后再保存。' }
    else notice.value = '尚未确认原操作结果；可继续查询或封闭，不能重新保存。'
  } catch (error) { if (request === generation) notice.value = errorMessage(error) }
  finally { if (request === generation) { syncPending(); busy.value = false } }
}
let currentStamp = identityStamp()
function changed() {
  if (identityStamp() === currentStamp) { try { syncPending() } catch (error) { notice.value = errorMessage(error) }; return }
  currentStamp = identityStamp(); generation++; listGeneration++; clearPassword(); Object.assign(draft, empty()); draft.id = undefined
  rows.value = []; total.value = 0; editor.value = null; scope.value = null; pending.value = null; visible.value = false; busy.value = false
  notice.value = '登录身份已改变，请重新进入用户管理。'
}
onMounted(async () => {
  window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
  const request = generation, stamp = identityStamp()
  try { const value = await iamApi.userEditor(); requireIdentity(stamp); if (request === generation) { scope.value = value; syncPending() } }
  catch (error) { notice.value = errorMessage(error) }
  await loadList()
})
onBeforeUnmount(() => { generation++; listGeneration++; clearPassword(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 16px; }
form { display: grid; gap: 14px; }
label { display: flex; align-items: center; gap: 10px; }
fieldset { display: grid; gap: 8px; }
input:not([type='checkbox']), select { flex: 1; min-width: 0; padding: 7px; }
.recovery { padding: 12px 0; }
</style>
