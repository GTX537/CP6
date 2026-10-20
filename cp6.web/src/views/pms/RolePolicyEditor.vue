<template>
  <section :aria-label="title" :aria-busy="loading || writing">
    <h2>{{ title }}</h2>
    <div class="toolbar">
      <label>角色 <select v-model="selectedRoleId" aria-label="角色" :disabled="writing || !!pending" @change="load(selectedRoleId ?? undefined)">
        <option :value="null">请选择角色</option>
        <option v-for="r in roles" :key="r.roleId" :value="r.roleId">{{ r.roleName }}{{ r.enable ? '' : '（停用）' }}</option>
      </select></label>
      <el-button :disabled="loading || writing || !!pending" @click="load(selectedRoleId ?? undefined)">重新读取</el-button>
      <span v-if="loading" role="status">读取中…</span>
    </div>
    <el-alert v-if="notice" :title="notice" :closable="false" role="status" />
    <div v-if="pending" class="recovery" role="status">
      上一次操作尚待核对；只查询或封闭原操作，不重新发送保存。
      <el-button :disabled="writing" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="writing" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <p>切换角色或资源会放弃当前未保存草稿。角色配置默认值不代表用户组合多个角色后的有效权限。</p>
    <el-alert v-if="editor?.warnings.length" title="存在旧配置中的未注册或无效项。请核对下方配置及替换范围，再明确保存。" :closable="false" />
    <form v-if="editor && editor.roleId !== null && !loading" @submit.prevent="save">
      <fieldset :disabled="writing || !!pending || conflict || needsReload">
        <legend>{{ title }}</legend>
        <template v-if="mode === 'actions'">
          <p>保存将替换本角色菜单和动作集合。选择父菜单包含子菜单，选择子菜单保留导航父节点；取消菜单会同时取消其子菜单和对应动作。</p>
          <div v-for="m in editor.catalog.menus" :key="m.menuId" class="menu-row">
            <label><input :checked="menuIds.includes(m.menuId)" type="checkbox" @change="toggleMenu(m.menuId, ($event.target as HTMLInputElement).checked)" />{{ m.menuName }}</label>
            <label v-for="a in editor.catalog.actions.filter(a => a.menuId === m.menuId)" :key="a.actionCode">
              <input :checked="hasAction(a)" type="checkbox" :disabled="!menuIds.includes(m.menuId)" @change="toggleAction(a, ($event.target as HTMLInputElement).checked)" />
              {{ a.actionName || a.actionCode }}
            </label>
          </div>
          <div v-for="id in unknownMenus" :key="id">
            <label><input :checked="true" type="checkbox" @change="toggleMenu(id, false)" />不可用历史菜单 {{ id }}（取消以移除）</label>
          </div>
          <div v-for="a in unknownActions" :key="`${a.menuId}:${a.actionCode}`">
            <label><input :checked="true" type="checkbox" @change="toggleAction(a, false)" />未注册历史动作 {{ a.menuId }} / {{ a.actionCode }}（取消以移除）</label>
          </div>
        </template>
        <template v-if="mode === 'scopes'">
          <p>保存将替换本角色全部资源范围；未注册的历史资源配置将移除。自定义范围只可选当前租户可用部门。</p>
          <div v-for="r in editor.catalog.scopes" :key="r.key" class="scope-row">
            <label>{{ r.name }} <select v-model.number="scopeDraft(r.key).scopeType" :aria-label="`${r.name}范围`" @change="normalizeScope(r.key)">
              <option v-for="n in r.supports" :key="n" :value="n">{{ scopeNames[n] }}</option>
              <option v-if="!r.supports.includes(scopeDraft(r.key).scopeType)" :value="scopeDraft(r.key).scopeType">旧值无效，请重新选择</option>
            </select></label>
            <label v-if="scopeDraft(r.key).scopeType === 4">{{ r.name }}自定义部门
              <select v-model="scopeDraft(r.key).customDeptIds" multiple :aria-label="`${r.name}自定义部门`">
                <option v-for="d in editor.catalog.departments" :key="d.id" :value="d.id" :disabled="!d.available">{{ d.deptName }}{{ d.available ? '' : '（不可用）' }}</option>
              </select>
            </label>
          </div>
        </template>
        <template v-if="mode === 'fields'">
          <label>资源 <select v-model="resourceKey" aria-label="资源" @change="selectResource">
            <option v-for="r in editor.catalog.fields" :key="r.key" :value="r.key">{{ r.key }}</option>
          </select></label>
          <p>保存只替换所选资源的完整字段配置，其他资源保留；默认可读写不落库，未注册的历史字段将移除。</p>
          <label v-for="f in fieldDomain" :key="f.name">{{ f.label }}
            <select v-model.number="fieldDraft(f.name).access" :aria-label="`${f.label}访问级`">
              <option :value="1">可读写（默认）</option><option :value="2">只读</option><option :value="3">隐藏</option>
              <option v-if="![1, 2, 3].includes(fieldDraft(f.name).access)" :value="fieldDraft(f.name).access">旧值无效，请重新选择</option>
            </select>
          </label>
        </template>
        <label>变更理由 <input v-model="reason" required maxlength="256" /></label>
      </fieldset>
      <p v-if="conflict">版本或引用已改变。先封闭原操作，再重新读取并核对草稿。</p>
      <el-button v-if="conflict" :disabled="writing || !!pending" @click="load(selectedRoleId ?? undefined, true)">重新读取并保留草稿</el-button>
      <el-button v-permission="permission" type="primary" native-type="submit" :disabled="writing || !!pending || conflict || needsReload || mode === 'fields' && !resourceKey">确认保存</el-button>
    </form>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { iamPolicyApi, type IamPolicyEditor, type IamPolicyInput, type IamPolicyMode, type PolicyAction, type PolicyScope, type PolicyField } from '@/api/sys/iamPolicy'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
const props = defineProps<{ mode: IamPolicyMode }>()
const title = computed(() => ({ actions: '角色功能权限', scopes: '角色数据范围', fields: '角色字段权限' })[props.mode])
const permission = computed(() => ({ actions: 'pub-role-perm:edit', scopes: 'pub-data-scope:edit', fields: 'pub-field-perm:edit' })[props.mode])
const scopeNames: Record<number, string> = { 1: '本人', 2: '本部门', 3: '本部门及下级', 4: '自定义部门', 5: '全部' }
const roles = ref<IamPolicyEditor['roles']>([]), selectedRoleId = ref<number | null>(null), editor = ref<IamPolicyEditor | null>(null)
const loading = ref(false), writing = ref(false), notice = ref(''), reason = ref(''), conflict = ref(false), needsReload = ref(false)
const pending = ref<PendingIamOperation | null>(null), menuIds = ref<number[]>([]), actions = ref<PolicyAction[]>([])
const scopes = ref<PolicyScope[]>([]), fields = ref<PolicyField[]>([]), resourceKey = ref('')
const fieldDomain = computed(() => editor.value?.catalog.fields.find(r => r.key === resourceKey.value)?.fields || [])
const unknownMenus = computed(() => menuIds.value.filter(id => !editor.value?.catalog.menus.some(m => m.menuId === id)))
const unknownActions = computed(() => actions.value.filter(a => !editor.value?.catalog.actions.some(x => x.menuId === a.menuId && x.actionCode === a.actionCode)))
let generation = 0, currentStamp = identityStamp()
const hasAction = (a: PolicyAction) => actions.value.some(x => x.menuId === a.menuId && x.actionCode === a.actionCode)
function toggleAction(a: PolicyAction, checked: boolean) { actions.value = actions.value.filter(x => x.menuId !== a.menuId || x.actionCode !== a.actionCode); if (checked) actions.value.push({ menuId: a.menuId, actionCode: a.actionCode }) }
function toggleMenu(id: number, checked: boolean) {
  const catalog = editor.value?.catalog.menus || [], selected = new Set(menuIds.value), descendants = new Set<number>()
  const visit = (key: number) => {
    if (descendants.has(key)) return
    descendants.add(key)
    for (const child of catalog.filter(x => x.parentId === key)) visit(child.menuId)
  }
  visit(id)
  for (const key of descendants) { if (checked) selected.add(key); else selected.delete(key) }
  // Match the previous tree's checked + half-checked navigation parents without looping on malformed legacy trees.
  const visited = new Set<number>([id])
  let parent = catalog.find(x => x.menuId === id)?.parentId
  while (parent != null && !visited.has(parent)) {
    visited.add(parent)
    if (checked || catalog.some(x => x.parentId === parent && selected.has(x.menuId))) selected.add(parent)
    else selected.delete(parent)
    parent = catalog.find(x => x.menuId === parent)?.parentId
  }
  menuIds.value = [...selected]
  actions.value = actions.value.filter(x => selected.has(x.menuId))
}
function scopeDraft(key: string) { return scopes.value.find(x => x.resourceKey === key)! }
function fieldDraft(name: string) { return fields.value.find(x => x.fieldName === name)! }
function normalizeScope(key: string) { const scope = scopeDraft(key); if (scope.scopeType !== 4) scope.customDeptIds = [] }
function syncPending() { pending.value = editor.value ? pendingIamOperation(editor.value) : null }
function apply(value: IamPolicyEditor, keep: boolean) {
  if (!keep) { menuIds.value = [...value.function.menuIds]; actions.value = value.function.actions.map(x => ({ ...x })); reason.value = '' }
  scopes.value = value.catalog.scopes.map(r => {
    const old = keep ? scopes.value.find(x => x.resourceKey === r.key) : undefined, current = old || value.dataScopes.find(x => x.resourceKey === r.key)
    return { resourceKey: r.key, scopeType: current?.scopeType ?? r.default, customDeptIds: [...(current?.customDeptIds || [])] }
  })
  if (!value.catalog.fields.some(x => x.key === resourceKey.value)) resourceKey.value = value.catalog.fields[0]?.key || ''
  const domain = value.catalog.fields.find(x => x.key === resourceKey.value)?.fields || []
  fields.value = domain.map(f => ({ resourceKey: resourceKey.value, fieldName: f.name,
    access: (keep ? fields.value.find(x => x.resourceKey === resourceKey.value && x.fieldName === f.name) : undefined)?.access
      ?? value.fieldPerms.find(x => x.resourceKey === resourceKey.value && x.fieldName === f.name)?.access ?? 1 }))
}
function selectResource() { if (editor.value) { fields.value = []; apply(editor.value, false) } }
function clear(text: string) {
  generation++; editor.value = null; roles.value = []; pending.value = null; selectedRoleId.value = null
  menuIds.value = []; actions.value = []; scopes.value = []; fields.value = []; resourceKey.value = ''; reason.value = ''
  loading.value = false; writing.value = false; conflict.value = false; needsReload.value = false; notice.value = text
}
function fail(error: any) {
  if ([401, 403].includes(error?.response?.status)) clear('当前身份或权限不可用，已清除授权资料。')
  else notice.value = error?.response?.data?.code || error?.message || '操作失败，请核对状态。'
}
async function load(roleId?: number, keep = false) {
  const request = ++generation, stamp = identityStamp(); loading.value = true
  try {
    const value = await iamPolicyApi.read(props.mode, roleId); requireIdentity(stamp); if (request !== generation) return
    editor.value = value; roles.value = value.roles; selectedRoleId.value = value.roleId; syncPending(); apply(value, keep)
    conflict.value = false; needsReload.value = false
    notice.value = keep ? '已读取当前版本并保留草稿；目录变化时新增项载入当前配置，请逐项核对后再次确认。' : ''
  } catch (error) { if (request === generation) { needsReload.value = true; fail(error) } }
  finally { if (request === generation) loading.value = false }
}
async function refreshCommitted(message: string) {
  notice.value = message; needsReload.value = true
  const request = generation, stamp = identityStamp()
  try {
    const value = await iamPolicyApi.read(props.mode, selectedRoleId.value ?? undefined); requireIdentity(stamp)
    if (request !== generation) return
    editor.value = value; roles.value = value.roles; apply(value, false); conflict.value = false; needsReload.value = false
  } catch (error: any) {
    if (request === generation) { if ([401, 403].includes(error?.response?.status)) fail(error); else notice.value = message + ' 配置刷新失败，请单独重新读取，无需重存。' }
  }
}
async function save() {
  const value = editor.value
  if (!value || value.roleId === null || value.roleId !== selectedRoleId.value || loading.value || writing.value || pending.value || conflict.value || needsReload.value) return
  const request = generation, stamp = identityStamp(); writing.value = true
  const input: IamPolicyInput = { expectedVersion: value.version, expectedReferencesVersion: value.referencesVersion, expectedCatalogVersion: value.catalog.version, reason: reason.value }
  if (props.mode === 'actions') { input.menuIds = [...menuIds.value]; input.actions = actions.value.map(x => ({ ...x })) }
  if (props.mode === 'scopes') input.scopes = scopes.value.map(x => ({ ...x, customDeptIds: [...x.customDeptIds] }))
  if (props.mode === 'fields') { input.resourceKey = resourceKey.value; input.fields = fields.value.map(x => ({ ...x })) }
  try {
    const result = await saveIamOperation(value, `role-policy-${props.mode}`, undefined, id => iamPolicyApi.save(props.mode, value.roleId!, input, id))
    requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted('授权配置已保存。')
  } catch (error: any) { if (request === generation) { conflict.value = error?.response?.status === 412; fail(error) } }
  finally { if (request === generation) { syncPending(); writing.value = false } }
}
async function resolve(close: boolean) {
  if (!editor.value || writing.value) return
  const request = generation, stamp = identityStamp(); writing.value = true
  try {
    const result = await resolveIamOperation(editor.value, close); requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted('原操作已保存，不再重放。')
    else if (result.state === 'CLOSED') { conflict.value = true; notice.value = '原操作已封闭，请重新读取当前版本并核对草稿。' }
    else notice.value = '尚未确认结果；可继续查询或封闭原操作，不能再次保存。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) { syncPending(); writing.value = false } }
}
function changed() {
  if (identityStamp() !== currentStamp) { currentStamp = identityStamp(); clear('登录身份已改变，请重新进入授权管理。'); return }
  try { syncPending() } catch { clear('原操作记录无法读取，请核对后重新进入。') }
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void load() })
onBeforeUnmount(() => { generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
section { padding: 20px; }
.toolbar, .menu-row { display: flex; align-items: center; flex-wrap: wrap; gap: 14px; }
form, fieldset, .scope-row { display: grid; gap: 14px; }
label { display: flex; align-items: center; gap: 8px; }
select, input:not([type='checkbox']) { min-width: 150px; padding: 7px; }
select[multiple] { min-height: 100px; }
.recovery { padding: 16px 0; }
</style>
