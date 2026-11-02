<template>
  <el-card v-if="targetId || storageError" class="tenant-management-panel">
    <template #header>{{ t('租户配置与启停') }}</template>
    <p>{{ t('租户') }}：{{ snapshot?.tenantCode || pending?.snapshot.tenantCode || targetId }}</p>
    <p v-if="snapshot">{{ t('已读版本') }} {{ snapshot.revision }} · {{ snapshot.enable ? t('已启用') : t('已停用') }}</p>
    <p>{{ t('停用会限制新的登录和业务操作，历史数据及未完任务保留。此处显示本系统状态，关联服务的同步结果需另行确认。') }}</p>
    <p>{{ t('若停用当前登录所属租户，需要另一位平台管理员恢复。') }}</p>
    <el-button :disabled="busy || !!pending || !!storageError" @click="load">{{ t('读取当前状态（替换草稿）') }}</el-button>
    <el-form label-width="120px" :disabled="busy || !!pending || !snapshot || conflict || !!storageError">
      <el-form-item :label="t('租户名称')"><el-input v-model="draft.name" :aria-label="t('租户名称')" maxlength="200" /></el-form-item>
      <el-form-item :label="t('到期时间')">
        <el-input v-model="expireText" :aria-label="t('到期时间')" placeholder="2030-01-02T03:04:05.123456" />
        <span>{{ t('保留完整日期和时间，不含时区偏移；留空表示不限期。') }}</span>
      </el-form-item>
      <el-form-item :label="t('时区')">
        <el-select v-model="draft.timeZoneId" filterable allow-create clearable :aria-label="t('时区')">
          <el-option v-for="tz in TIMEZONE_OPTIONS" :key="tz.value" :label="tz.label" :value="tz.value" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('备注')"><el-input v-model="draft.remark" type="textarea" :aria-label="t('备注')" maxlength="500" /></el-form-item>
    </el-form>
    <div class="management-actions">
      <el-button type="primary" :disabled="!canSubmit" @click="submit('update')">{{ t('保存租户配置') }}</el-button>
      <el-button v-if="snapshot?.enable" type="warning" :disabled="!canSubmit" @click="submit('suspend')">{{ t('确认停用租户') }}</el-button>
      <el-button v-else-if="snapshot" type="success" :disabled="!canSubmit" @click="submit('reactivate')">{{ t('确认恢复租户') }}</el-button>
    </div>
    <div v-if="pending" class="management-pending">
      <p>{{ t('原操作结果待确认，确认前不能提交另一操作。刷新后可继续查询或按原请求重试。') }}</p>
      <p>{{ t('原操作') }}：<code>{{ pending.command.operationRef }}</code></p>
      <el-button :disabled="busy || !!storageError" @click="recover(false)">{{ t('查询原操作结果') }}</el-button>
      <el-button :disabled="busy || !!storageError" @click="recover(true)">{{ t('按原请求重试') }}</el-button>
    </div>
    <p v-if="receipt">{{ t('已确认原操作') }}：<code>{{ receipt.operationRef }}</code> · {{ t('结果版本') }} {{ receipt.snapshot.revision }} · {{ t('该结果是历史回执，请读取当前状态后继续。') }}</p>
    <el-alert v-if="storageError || message" :title="storageError || message" :type="storageError || failed ? 'error' : 'info'" :closable="false" show-icon />
  </el-card>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { tenantApi, type TenantConfiguration, type TenantManagementCommand, type TenantManagementReceipt, type TenantManagementSnapshot } from '@/api/platform/tenant'
import { TIMEZONE_OPTIONS } from '@/api/platform/timezones'

const emit = defineEmits<{ changed: [] }>(), { t } = useI18n()
type Pending = { targetId: string; snapshot: TenantManagementSnapshot; command: TenantManagementCommand }
const targetId = ref(''), snapshot = ref<TenantManagementSnapshot | null>(null), receipt = ref<TenantManagementReceipt | null>(null)
const draft = reactive<TenantConfiguration>({ name: '', expireDate: null, remark: null, timeZoneId: null }), expireText = ref('')
const pending = ref<Pending | null>(null), busy = ref(false), conflict = ref(false), message = ref(''), failed = ref(false), storageError = ref('')
const canSubmit = computed(() => !!snapshot.value && !busy.value && !pending.value && !conflict.value && !storageError.value)
let stamp = identityStamp(), disposed = false
const storageKey = () => `cp6-tenant-management-pending-v1:${stamp}`
const active = (started: string) => !disposed && stamp === started && identityStamp() === started
function notice(text: string, error = false) { message.value = t(text); failed.value = error }
function fill(row: TenantManagementSnapshot) {
  Object.assign(draft, { name: row.tenantName, expireDate: row.expireDate, remark: row.remark, timeZoneId: row.timeZoneId })
  expireText.value = row.expireDate ?? ''
}
function reset() {
  targetId.value = ''; snapshot.value = null; receipt.value = null; pending.value = null
  busy.value = false; conflict.value = false; message.value = ''; storageError.value = ''; expireText.value = ''
  Object.assign(draft, { name: '', expireDate: null, remark: null, timeZoneId: null })
  try {
    const raw = sessionStorage.getItem(storageKey())
    if (raw) {
      pending.value = JSON.parse(raw) as Pending; targetId.value = pending.value.targetId
      snapshot.value = pending.value.snapshot; fill(pending.value.snapshot)
      if (pending.value.command.configuration) {
        Object.assign(draft, pending.value.command.configuration); expireText.value = draft.expireDate ?? ''
      }
    }
  } catch { storageError.value = t('无法读取原操作记录，请保留标签页并恢复浏览器存储后刷新。') }
}
function identityChanged() { const next = identityStamp(); if (next !== stamp) { stamp = next; reset() } }
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
reset()
function failure(cause: unknown) {
  const response = (cause as { response?: { data?: { code?: string; message?: string } } }).response
  notice(response?.data?.message || response?.data?.code || (cause as Error).message || '请求失败', true)
}
async function open(id: string) {
  if (busy.value || pending.value || storageError.value) { notice('请先确认当前原操作结果。', true); return }
  targetId.value = id; receipt.value = null; await load(); await nextTick()
  document.querySelector('.tenant-management-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
defineExpose({ open })
async function load() {
  if (busy.value || pending.value || storageError.value || !targetId.value) return
  const started = stamp; busy.value = true; snapshot.value = null
  try {
    const row = await tenantApi.management(targetId.value)
    if (!active(started)) return
    snapshot.value = row; fill(row); conflict.value = false; notice('已读取当前状态，请核对后提交。')
  } catch (cause) { if (active(started)) failure(cause) }
  finally { if (active(started)) busy.value = false }
}
function clearPending() { sessionStorage.removeItem(storageKey()); pending.value = null }
function accept(value: TenantManagementReceipt) {
  clearPending(); receipt.value = value; snapshot.value = null; conflict.value = false
  notice('原操作已确认。草稿保留；请读取当前状态后继续。'); emit('changed')
}
async function submit(kind: TenantManagementCommand['kind']) {
  if (!canSubmit.value) return
  const original: Pending = { targetId: targetId.value, snapshot: { ...snapshot.value! }, command: {
    operationRef: crypto.randomUUID(), expectedRevision: snapshot.value!.revision, kind,
    configuration: kind === 'update' ? { ...draft, expireDate: expireText.value === '' ? null : expireText.value } : null } }
  try { sessionStorage.setItem(storageKey(), JSON.stringify(original)) }
  catch { storageError.value = t('无法保存原操作记录，本次尚未发送。恢复浏览器存储后刷新。'); return }
  const started = stamp; pending.value = original; busy.value = true
  try { const value = await tenantApi.manage(original.targetId, original.command); if (active(started)) accept(value) }
  catch (cause) {
    if (!active(started)) return
    const status = (cause as { response?: { status?: number } }).response?.status
    if (status && [400, 401, 403, 404, 409, 412, 413, 422].includes(status)) {
      try { clearPending() } catch { storageError.value = t('无法清除原操作记录，请恢复浏览器存储后刷新。') }
      if (status === 412) { conflict.value = true; notice('租户状态已变化，草稿已保留。请备份草稿后读取当前状态再编辑。', true); return }
    }
    failure(cause)
  } finally { if (active(started)) busy.value = false }
}
async function recover(retry: boolean) {
  if (busy.value || !pending.value || storageError.value) return
  const started = stamp, original = pending.value; busy.value = true
  try {
    const value = retry ? await tenantApi.manage(original.targetId, original.command)
      : await tenantApi.originalManagement(original.targetId, original.command.operationRef)
    if (active(started)) accept(value)
  } catch (cause) {
    if (!active(started)) return
    if (!retry && (cause as { response?: { status?: number } }).response?.status === 404)
      notice('尚未查到原结果，原请求仍保留；可按原请求重试。', true)
    else failure(cause) // A later denial cannot disprove an earlier unknown commit.
  } finally { if (active(started)) busy.value = false }
}
</script>

<style scoped>
.tenant-management-panel { margin: 20px 0; max-width: 900px; }
.tenant-management-panel .el-form { margin-top: 20px; }
.tenant-management-panel code { overflow-wrap: anywhere; }
.management-actions, .management-pending, .tenant-management-panel .el-alert { margin-top: 16px; }
</style>
