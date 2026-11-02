<template>
  <el-card v-if="visible || storageError" class="tenant-creation-panel">
    <template #header>{{ t('创建租户') }}</template>
    <p>{{ t('创建租户及首个管理员，复制当前默认管理员菜单。首次登录必须修改密码，不授予平台管理员权限。') }}</p>
    <p v-if="template">{{ t('已读取模板') }}：<code>{{ template.version }}</code> · {{ template.menuIds.length }} {{ t('个菜单') }}</p>
    <el-button :disabled="busy || !!pending || !!storageError" @click="loadTemplate">{{ t('读取当前创建模板') }}</el-button>
    <el-form label-width="120px" :disabled="busy || !!pending || !!storageError">
      <el-form-item :label="t('租户编码')"><el-input v-model="draft.code" :aria-label="t('租户编码')" maxlength="50" /></el-form-item>
      <el-form-item :label="t('新租户名称')"><el-input v-model="draft.name" :aria-label="t('新租户名称')" maxlength="200" /></el-form-item>
      <el-form-item :label="t('首个管理员账号')"><el-input v-model="draft.adminUserName" :aria-label="t('首个管理员账号')" maxlength="100" /></el-form-item>
      <el-form-item :label="t('新租户到期时间')">
        <el-input v-model="expiry" :aria-label="t('新租户到期时间')" placeholder="2030-01-02T03:04:05.123456" />
        <span>{{ t('完整日期和时间，不含时区偏移；留空表示不限期。') }}</span>
      </el-form-item>
      <el-form-item :label="t('新租户备注')"><el-input v-model="draft.remark" type="textarea" :aria-label="t('新租户备注')" maxlength="500" /></el-form-item>
    </el-form>
    <el-button type="primary" :disabled="!canSubmit" @click="submit">{{ t('确认创建租户') }}</el-button>
    <div v-if="pending" class="creation-pending">
      <p>{{ t('创建结果待确认，原请求已保存。请查询或按原请求重试，确认前不能再次创建。') }}</p>
      <p>{{ t('原操作') }}：<code>{{ pending.operationRef }}</code></p>
      <el-button :disabled="busy || !!storageError" @click="recover(false)">{{ t('查询原创建结果') }}</el-button>
      <el-button :disabled="busy || !!storageError" @click="recover(true)">{{ t('按原创建请求重试') }}</el-button>
    </div>
    <div v-if="receipt" class="creation-receipt">
      <p>{{ t('已确认创建') }}：{{ receipt.tenantCode }} · <code>{{ receipt.tenantId }}</code></p>
      <p>{{ t('管理员账号') }}：{{ receipt.adminUserName }}</p>
      <p>{{ t('这是原创建结果，租户当前状态请从列表读取。') }}</p>
      <p v-if="needsReset">{{ t('原结果不包含临时密码。若首次密码未收到或未保管，请通过 IAM 受控凭据重置流程设置新凭据，不要重新创建租户。') }}</p>
    </div>
    <el-alert v-if="message || storageError" :title="storageError || message" :type="storageError || failed ? 'error' : 'info'" :closable="false" show-icon />
    <el-dialog :model-value="!!password" :title="t('一次性临时密码')" width="520px" :close-on-click-modal="false" @update:model-value="closePassword">
      <p>{{ t('请安全交付给管理员。关闭后不再显示，刷新或查询原结果也不会返回密码。') }}</p>
      <p>{{ receipt?.adminUserName }}</p><code class="creation-password">{{ password }}</code>
      <template #footer><el-button type="primary" @click="closePassword">{{ t('已保管，关闭密码') }}</el-button></template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { tenantApi, type TenantCreationCommand, type TenantCreationReceipt, type TenantCreationResult, type TenantCreationTemplate } from '@/api/platform/tenant'

const { t } = useI18n(), emit = defineEmits<{ changed: [] }>()
const visible = ref(false), busy = ref(false), template = ref<TenantCreationTemplate | null>(null), conflict = ref(false)
const pending = ref<TenantCreationCommand | null>(null), receipt = ref<TenantCreationReceipt | null>(null), password = ref(''), needsReset = ref(false)
const draft = reactive({ code: '', name: '', adminUserName: '', remark: '' as string | null }), expiry = ref('')
const message = ref(''), failed = ref(false), storageError = ref('')
let stamp = identityStamp(), disposed = false
const key = () => `cp6-tenant-creation-pending-v1:${stamp}`
const active = (started: string) => !disposed && stamp === started && identityStamp() === started
const canSubmit = computed(() => !!template.value && !busy.value && !pending.value && !conflict.value && !storageError.value && !password.value)
function notice(text: string, error = false) { message.value = t(text); failed.value = error }
function closePassword() { password.value = '' }
function reset() {
  visible.value = false; busy.value = false; template.value = null; conflict.value = false; pending.value = null; receipt.value = null
  password.value = ''; needsReset.value = false; message.value = ''; storageError.value = ''; expiry.value = ''
  Object.assign(draft, { code: '', name: '', adminUserName: '', remark: '' })
  try {
    const raw = sessionStorage.getItem(key())
    if (raw) {
      const original = JSON.parse(raw) as TenantCreationCommand
      pending.value = original; visible.value = true
      Object.assign(draft, { code: original.code, name: original.name, adminUserName: original.adminUserName, remark: original.remark })
      expiry.value = original.expireDate ?? ''
    }
  } catch { storageError.value = t('无法读取原创建记录，请保留标签页并恢复浏览器存储后刷新。') }
}
function identityChanged() { const next = identityStamp(); if (next !== stamp) { stamp = next; reset() } }
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { disposed = true; closePassword(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
reset()
function failure(cause: unknown) {
  const response = (cause as { response?: { data?: { code?: string; message?: string } } }).response
  notice(response?.data?.message || response?.data?.code || (cause as Error).message || '请求失败', true)
}
async function open() {
  visible.value = true
  if (!busy.value && !pending.value && !storageError.value && !template.value) await loadTemplate()
  await nextTick(); document.querySelector('.tenant-creation-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
defineExpose({ open })
async function loadTemplate() {
  if (busy.value || pending.value || storageError.value) return
  const started = stamp; busy.value = true; template.value = null
  try {
    const value = await tenantApi.creationTemplate()
    if (active(started)) { template.value = value; conflict.value = false; notice('当前模板已读取，草稿保留。请核对后创建。') }
  } catch (cause) { if (active(started)) failure(cause) }
  finally { if (active(started)) busy.value = false }
}
function clearPending() { sessionStorage.removeItem(key()); pending.value = null }
function accept(value: TenantCreationResult) {
  clearPending(); receipt.value = value.receipt; password.value = value.tempPassword ?? ''; needsReset.value = value.needsCredentialReset
  template.value = null; conflict.value = false; notice('原创建结果已确认。'); emit('changed')
}
function rejected(cause: unknown, initial: boolean) {
  const response = (cause as { response?: { status?: number; data?: { code?: string } } }).response
  // These Owner rejections occur only after the authoritative original-command read.
  // A later auth denial or a query 404 cannot disprove an earlier unknown commit.
  const rejectedByOwner = ['TENANT_TEMPLATE_CHANGED', 'TENANT_TEMPLATE_NOT_READY', 'E-SEC-033'].includes(response?.data?.code ?? '')
  if (rejectedByOwner || initial && response?.status && [400, 401, 403, 404, 409, 412, 413, 422].includes(response.status)) {
    try { clearPending() } catch { storageError.value = t('无法清除原创建记录，请恢复浏览器存储后刷新。') }
    if (response?.status === 412) { conflict.value = true; notice('创建模板已变化，草稿保留。请读取当前创建模板再确认。', true); return }
  }
  failure(cause)
}
async function submit() {
  if (!canSubmit.value) return
  const original: TenantCreationCommand = { operationRef: crypto.randomUUID(), expectedTemplateVersion: template.value!.version,
    ...draft, expireDate: expiry.value === '' ? null : expiry.value }
  try { sessionStorage.setItem(key(), JSON.stringify(original)) }
  catch { storageError.value = t('无法保存原创建请求，本次尚未发送。恢复浏览器存储后刷新。'); return }
  const started = stamp; pending.value = original; busy.value = true
  try { const value = await tenantApi.createVersioned(original); if (active(started)) accept(value) }
  catch (cause) { if (active(started)) rejected(cause, true) }
  finally { if (active(started)) busy.value = false }
}
async function recover(retry: boolean) {
  if (busy.value || !pending.value || storageError.value) return
  const started = stamp, original = pending.value; busy.value = true
  try {
    const value = retry ? await tenantApi.createVersioned(original) : await tenantApi.originalCreation(original.operationRef)
    if (active(started)) accept(value)
  } catch (cause) {
    if (!active(started)) return
    if (!retry && (cause as { response?: { status?: number } }).response?.status === 404)
      notice('尚未查到原创建结果，原请求仍保留；可按原请求重试。', true)
    else rejected(cause, false)
  } finally { if (active(started)) busy.value = false }
}
</script>

<style scoped>
.tenant-creation-panel { margin: 20px 0; max-width: 900px; }
.tenant-creation-panel code { overflow-wrap: anywhere; }
.tenant-creation-panel .el-form, .creation-pending, .creation-receipt, .tenant-creation-panel .el-alert { margin-top: 20px; }
.creation-password { font-size: 20px; color: #b45309; }
</style>
