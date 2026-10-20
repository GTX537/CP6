<template>
  <section aria-label="菜单动作定义" :aria-busy="loading || writing">
    <header>
      <h2>菜单动作定义</h2>
      <el-button :disabled="writing" @click="$emit('back')">返回角色授权</el-button>
    </header>
    <p>维护菜单可分配的动作；保存会替换所选菜单的完整定义。已被角色引用的动作不能删除，新增定义不会自动授予角色。</p>
    <div class="toolbar">
      <label>菜单 <select v-model="menuId" aria-label="菜单" :disabled="loading || writing || !!pending" @change="loadMenu(false)">
        <option :value="null">请选择菜单</option>
        <option v-for="menu in menus" :key="menu.menuId" :value="menu.menuId">{{ menu.menuName }}（{{ menu.menuId }}）{{ menu.enable ? '' : '（停用）' }}</option>
      </select></label>
      <el-button :disabled="loading || writing || !!pending" @click="reload">重新读取</el-button>
    </div>
    <el-alert v-if="notice" :title="notice" :closable="false" role="status" />
    <div v-if="pending" class="recovery" role="status">
      上一次操作尚待核对；只查询或封闭原操作，不重新发送保存。
      <el-button :disabled="writing" @click="resolve(false)">查询原操作</el-button>
      <el-button :disabled="writing" @click="resolve(true)">封闭原操作</el-button>
    </div>
    <form v-if="editor && !loading" @submit.prevent="save">
      <fieldset :disabled="writing || !!pending || conflict || needsReload">
        <legend>当前菜单完整动作集合</legend>
        <p>动作代码是权限身份，改变代码等同删除旧动作并新增；不会迁移既有角色授权。</p>
        <div v-for="(action, index) in actions" :key="index" class="action-row">
          <label>动作代码 <input v-model="action.actionCode" :aria-label="`动作代码 ${index + 1}`" required maxlength="50" /></label>
          <label>动作名称 <input v-model="action.actionName" :aria-label="`动作名称 ${index + 1}`" maxlength="100" /></label>
          <label>排序 <input v-model.number="action.sort" :aria-label="`动作排序 ${index + 1}`" type="number" min="-2147483648" max="2147483647" step="1" required /></label>
          <el-button :aria-label="`移除动作 ${index + 1}`" @click="actions.splice(index, 1)">移除</el-button>
        </div>
        <el-button @click="actions.push({ actionCode: '', actionName: '', sort: 0 })">新增动作</el-button>
        <p v-if="actions.length === 0">保存空集合将清除本菜单全部动作定义；有角色引用时服务器会拒绝。</p>
        <label>变更理由 <input v-model="reason" required maxlength="256" /></label>
      </fieldset>
      <p v-if="conflict">版本或引用已改变。封闭原操作后，重新读取并核对保留的草稿，再确认保存。</p>
      <el-button v-if="conflict" :disabled="loading || writing || !!pending" @click="loadMenu(true)">重新读取并保留草稿</el-button>
      <el-button v-permission="'pub-role-perm:edit'" type="primary" native-type="submit"
        :disabled="loading || writing || !!pending || conflict || needsReload">确认保存动作</el-button>
    </form>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { iamPolicyApi, type IamMenuActionEditor, type IamPolicyEditor, type MenuActionDefinition } from '@/api/sys/iamPolicy'
import type { IamScope } from '@/api/sys/iam'
import { pendingIamOperation, resolveIamOperation, saveIamOperation, type PendingIamOperation } from '@/auth/iamOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
defineEmits<{ back: [] }>()
const scope = ref<IamScope | null>(null), menus = ref<IamPolicyEditor['catalog']['menus']>([])
const menuId = ref<number | null>(null), editor = ref<IamMenuActionEditor | null>(null)
const actions = ref<MenuActionDefinition[]>([]), reason = ref(''), notice = ref('')
const loading = ref(false), writing = ref(false), conflict = ref(false), needsReload = ref(false)
const pending = ref<PendingIamOperation | null>(null)
let generation = 0, currentStamp = identityStamp()
function syncPending() { pending.value = scope.value ? pendingIamOperation(scope.value) : null }
function clear(text: string) {
  generation++; scope.value = null; menus.value = []; menuId.value = null; editor.value = null
  actions.value = []; reason.value = ''; pending.value = null; conflict.value = false; needsReload.value = true
  loading.value = false; writing.value = false; notice.value = text
}
function fail(error: any) {
  if ([401, 403].includes(error?.response?.status)) clear('当前身份或权限不可用，已清除动作资料。')
  else notice.value = error?.response?.data?.code === 'IAM_MENU_ACTION_REFERENCED'
    ? '动作仍被角色引用，未保存。请先处理角色授权，再核对原操作和当前版本。'
    : error?.response?.data?.code || error?.message || '操作失败，请核对当前状态。'
}
async function reload() {
  if (writing.value || pending.value) return
  const request = ++generation, stamp = identityStamp(); loading.value = true; needsReload.value = true
  try {
    const value = await iamPolicyApi.read('actions'); requireIdentity(stamp); if (request !== generation) return
    scope.value = value; menus.value = value.catalog.menus; syncPending()
    if (menuId.value !== null && !menus.value.some(x => x.menuId === menuId.value)) {
      editor.value = null; menuId.value = null; actions.value = []; reason.value = ''
      notice.value = '原菜单已不可用，请选择当前菜单。'
    }
    if (menuId.value !== null && !pending.value) await loadMenu(false)
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) loading.value = false }
}
async function loadMenu(keep: boolean) {
  if (writing.value || pending.value) return
  const request = ++generation, stamp = identityStamp(); loading.value = true; needsReload.value = true
  if (menuId.value === null) { editor.value = null; actions.value = []; reason.value = ''; loading.value = false; return }
  const selected = menuId.value
  // A draft can only survive a reread of the same target.
  keep = keep && editor.value?.menuId === selected
  if (!keep) { editor.value = null; actions.value = []; reason.value = '' }
  try {
    const value = await iamPolicyApi.readMenuActions(selected); requireIdentity(stamp); if (request !== generation) return
    if (value.menuId !== selected || value.tenantId !== scope.value?.tenantId || value.actorId !== scope.value?.actorId) throw new Error('动作目录身份不匹配。')
    editor.value = value; if (!keep) actions.value = value.actions.map(x => ({ ...x }))
    syncPending(); conflict.value = false; needsReload.value = false
    notice.value = keep ? '已读取当前版本并保留草稿，请核对动作引用后再次确认。' : ''
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) loading.value = false }
}
async function refreshCommitted(text: string) {
  notice.value = text; needsReload.value = true
  if (menuId.value === null) return
  const request = generation, stamp = identityStamp(), selected = menuId.value
  try {
    const value = await iamPolicyApi.readMenuActions(selected); requireIdentity(stamp); if (request !== generation) return
    if (value.menuId !== selected || value.tenantId !== scope.value?.tenantId || value.actorId !== scope.value?.actorId) throw new Error('动作目录身份不匹配。')
    editor.value = value; actions.value = value.actions.map(x => ({ ...x })); reason.value = ''; conflict.value = false; needsReload.value = false
  } catch (error: any) {
    if (request === generation) {
      if ([401, 403].includes(error?.response?.status)) fail(error)
      else notice.value = text + ' 动作刷新失败，请单独重新读取，无需重存。'
    }
  }
}
async function save() {
  const value = editor.value
  if (!value || loading.value || writing.value || pending.value || conflict.value || needsReload.value) return
  if (!reason.value.trim() || actions.value.some(x => !x.actionCode.trim() || x.actionCode.length > 50 || x.actionName.length > 100 || !Number.isInteger(x.sort) || x.sort < -2147483648 || x.sort > 2147483647)
    || new Set(actions.value.map(x => x.actionCode)).size !== actions.value.length) { notice.value = '请填写理由及合法、不重复的动作代码和排序。'; return }
  const request = generation, stamp = identityStamp(); writing.value = true
  const input = { menuId: value.menuId, expectedVersion: value.version, expectedReferencesVersion: value.referencesVersion,
    expectedCatalogVersion: value.catalogVersion, reason: reason.value, actions: actions.value.map(x => ({ ...x })) }
  try {
    const result = await saveIamOperation(value, 'menu-actions', undefined, id => iamPolicyApi.saveMenuActions(input, id))
    requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted('菜单动作已保存。')
  } catch (error: any) { if (request === generation) { conflict.value = error?.response?.status === 412; fail(error) } }
  finally { if (request === generation) { syncPending(); writing.value = false } }
}
async function resolve(close: boolean) {
  if (!scope.value || writing.value) return
  const request = generation, stamp = identityStamp(); writing.value = true
  try {
    const result = await resolveIamOperation(scope.value, close); requireIdentity(stamp); if (request !== generation) return
    if (result.state === 'COMMITTED') await refreshCommitted('原操作已保存，不再重放。')
    else if (result.state === 'CLOSED') { conflict.value = true; needsReload.value = true; notice.value = '原操作已封闭，请重新读取并核对草稿。' }
    else notice.value = '尚未确认原操作结果，可继续查询或封闭，不能再次保存。'
  } catch (error) { if (request === generation) fail(error) }
  finally { if (request === generation) { syncPending(); writing.value = false } }
}
function changed() {
  if (identityStamp() !== currentStamp) { currentStamp = identityStamp(); clear('登录身份已改变，请重新进入动作管理。'); return }
  try {
    const original = pending.value; syncPending()
    if (original && !pending.value) { needsReload.value = true; notice.value = '其他标签页已核对原操作，请重新读取当前版本。' }
  } catch { clear('原操作记录无法读取，请核对后重新进入。') }
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void reload() })
onBeforeUnmount(() => { generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
section { padding: 20px; }
header, .toolbar, .action-row { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
fieldset { display: grid; gap: 16px; }
label { display: inline-flex; align-items: center; gap: 8px; }
input, select { padding: 7px; max-width: 100%; }
.recovery { padding: 16px 0; }
</style>
