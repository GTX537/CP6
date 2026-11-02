<template>
  <section v-if="!identityChanged && (menuId !== null || pending)" class="menu-display-panel" aria-label="菜单展示配置">
    <h3>名称、图标与显示状态</h3>
    <p>选中菜单 #{{ pending?.menuId ?? menuId }} · 保存前核对全局影响。隐藏菜单不会撤销已有业务动作权限。</p>
    <p v-if="current">当前版本 {{ revision }}：{{ current.menuName }} / {{ current.icon ?? '无图标' }} / {{ current.enable ? '显示' : '隐藏' }}</p>
    <el-button :disabled="busy || !!pending || disabled || !!storageError" @click="load(true)">读取当前展示配置（保留草稿）</el-button>
    <el-form label-width="110px" :disabled="busy || !!pending || disabled || !!storageError">
      <el-form-item label="显示名称"><el-input v-model="draft.menuName" aria-label="显示名称" maxlength="100" /></el-form-item>
      <el-form-item label="显示图标"><el-input v-model="draft.icon" aria-label="显示图标" maxlength="100" :disabled="draft.noIcon" /><el-checkbox v-model="draft.noIcon">无图标</el-checkbox></el-form-item>
      <el-form-item label="进入导航"><el-switch v-model="draft.enable" aria-label="进入导航" /></el-form-item>
      <el-form-item label="修改原因"><el-input v-model="draft.reason" aria-label="修改原因" maxlength="256" /></el-form-item>
    </el-form>
    <el-button type="primary" :disabled="!canSubmit" @click="submit">保存展示配置</el-button>
    <div v-if="pending">
      <p>展示配置结果待确认，原请求已保存：{{ pending.operationRef }}</p>
      <el-button :disabled="busy || disabled || !!storageError" @click="recover(false)">查询原展示操作</el-button>
      <el-button :disabled="busy || disabled || !!storageError" @click="recover(true)">按原展示请求重试</el-button>
    </div>
    <p v-if="message || storageError" role="status">{{ storageError || message }}</p>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { menuApi, type MenuDisplayCommand, type MenuItem, type MenuTreeReceipt } from '@/api/sys/menu'
const props = defineProps<{ menuId: number | null; disabled?: boolean }>()
const emit = defineEmits<{ changed: []; blocked: [value: boolean] }>()
const busy = ref(false), identityChanged = ref(false), revision = ref<string | null>(null), current = ref<MenuItem | null>(null)
const pending = ref<MenuDisplayCommand | null>(null), message = ref(''), storageError = ref('')
const draft = reactive({ menuName: '', icon: '', noIcon: true, enable: true, reason: '' })
const stamp = identityStamp(), key = 'cp6-menu-display-pending-v1:' + stamp
let disposed = false, generation = 0
const active = () => !disposed && !identityChanged.value && identityStamp() === stamp
const canSubmit = computed(() => active() && !!revision.value && current.value?.menuId === props.menuId && !pending.value && !busy.value && !props.disabled && !storageError.value && !!draft.menuName.trim() && !!draft.reason.trim())
function restoreDraft(command: MenuDisplayCommand) {
  Object.assign(draft, { menuName: command.menuName, icon: command.icon ?? '', noIcon: command.icon === null, enable: command.enable, reason: command.reason })
}
try { pending.value = JSON.parse(sessionStorage.getItem(key) || 'null'); if (pending.value) restoreDraft(pending.value) }
catch { storageError.value = '无法读取原展示记录，请恢复浏览器存储后重新打开页面。' }
watch(() => busy.value || !!pending.value || !!storageError.value, value => emit('blocked', value), { immediate: true, flush: 'sync' })
function identityChange() { if (!active()) { identityChanged.value = true; generation++; emit('blocked', true) } }
window.addEventListener(AUTH_EVENT, identityChange); window.addEventListener('storage', identityChange)
onBeforeUnmount(() => { disposed = true; generation++; window.removeEventListener(AUTH_EVENT, identityChange); window.removeEventListener('storage', identityChange); emit('blocked', false) })
function failure(error: unknown, initial = false, retry = false) {
  const response = (error as { response?: { status?: number; data?: { code?: string } } }).response
  const code = response?.data?.code
  // Only post-original Owner rejections disprove an earlier unknown commit.
  if (initial && response?.status && [400, 401, 403, 404, 409, 412, 413, 422].includes(response.status)
    || retry && ['MENU_TREE_VERSION_CHANGED', 'MENU_TREE_NODE_NOT_FOUND'].includes(code ?? '')) clearPending()
  if (response?.status === 412) { revision.value = null; message.value = '版本已变化，草稿保留；读取当前展示配置后核对再提交。' }
  else message.value = code || (error as Error).message || '请求失败，原操作待确认。'
}
function clearPending() {
  try { sessionStorage.removeItem(key); pending.value = null }
  catch { storageError.value = '无法清除原展示记录，请恢复浏览器存储后重新打开并查询。' }
}
async function load(preserve: boolean) {
  if (!active() || busy.value || pending.value || props.disabled || storageError.value || props.menuId === null) return
  const id = props.menuId, request = ++generation; busy.value = true; revision.value = null
  try {
    const snapshot = await menuApi.treeCurrent()
    if (!active() || request !== generation || props.menuId !== id) return
    const node = snapshot.menus.find(x => x.menuId === id)
    if (!node) { current.value = null; message.value = '当前菜单已不存在，草稿保留。'; return }
    current.value = node; revision.value = snapshot.revision
    if (!preserve) Object.assign(draft, { menuName: node.menuName, icon: node.icon ?? '', noIcon: node.icon === null, enable: node.enable, reason: '' })
    message.value = preserve ? '当前值已读取，草稿保留。核对后可再次提交。' : ''
  } catch (error) { if (active() && request === generation) failure(error) }
  finally { busy.value = false }
}
watch(() => props.menuId, () => { generation++; revision.value = null; current.value = null; if (!pending.value) void load(false) }, { immediate: true })
function accept(receipt: MenuTreeReceipt) {
  clearPending(); revision.value = null; current.value = null
  message.value = `原展示操作已确认，提交时版本 ${receipt.appliedRevision}。当前列表和导航另行读取。`
}
async function submit() {
  if (!canSubmit.value) return
  const command: MenuDisplayCommand = { operationRef: crypto.randomUUID(), expectedRevision: revision.value!, menuId: props.menuId!,
    menuName: draft.menuName, icon: draft.noIcon ? null : draft.icon, enable: draft.enable, reason: draft.reason }
  const id = props.menuId; busy.value = true; let confirmed = false
  try {
    const impact = await menuApi.displayPreview(command)
    if (!active() || props.menuId !== id) return
    try { await ElMessageBox.confirm(`展示配置将影响共享树。引用范围：${impact.affectedTenantsCount} 个租户、${impact.affectedRolesCount} 个角色（全树保守统计）。确认保存？`, '共享展示配置影响', { type: 'warning' }) } catch { return }
    if (!active() || props.menuId !== id || props.disabled) return
    try { sessionStorage.setItem(key, JSON.stringify(command)) }
    catch { storageError.value = '无法保存原展示请求，本次尚未发送。恢复浏览器存储后重开。'; return }
    pending.value = command
    const receipt = await menuApi.displayExecute(command)
    if (active()) { accept(receipt); confirmed = true }
  } catch (error) { if (active()) failure(error, true) }
  finally { busy.value = false }
  if (confirmed && active()) emit('changed')
}
async function recover(retry: boolean) {
  if (!active() || !pending.value || busy.value || props.disabled || storageError.value) return
  const command = pending.value; busy.value = true; let confirmed = false
  try {
    const receipt = retry ? await menuApi.displayExecute(command) : await menuApi.treeOriginal(command.operationRef)
    if (active()) { accept(receipt); confirmed = true }
  } catch (error) {
    if (active()) {
      if (!retry && (error as { response?: { status?: number } }).response?.status === 404) message.value = '尚未查到原展示结果，原请求仍保留。'
      else failure(error, false, retry)
    }
  } finally { busy.value = false }
  if (confirmed && active()) emit('changed')
}
</script>

<style scoped>
.menu-display-panel { padding: 20px; margin-bottom: 20px; border: 1px solid var(--el-border-color); border-radius: 12px; background: var(--el-bg-color); }
.menu-display-panel .el-form { max-width: 720px; margin-top: 16px; }
.menu-display-panel p { overflow-wrap: anywhere; }
</style>
