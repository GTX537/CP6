<template>
  <details class="workspace-preferences">
    <summary>指标显示与顺序</summary>
    <p v-if="message" role="status">{{ message }}</p>
    <ol v-if="snapshot" class="workspace-preference-list">
      <li v-for="(key, index) in visible" :key="key">
        <label><input type="checkbox" checked :disabled="editingLocked" @change="setVisible(key, false)" />{{ t('dashboard.' + key) }}</label>
        <button class="btn" type="button" :disabled="editingLocked || index === 0" :aria-label="t('dashboard.' + key) + '上移'" @click="move(index, -1)">上移</button>
        <button class="btn" type="button" :disabled="editingLocked || index === visible.length - 1" :aria-label="t('dashboard.' + key) + '下移'" @click="move(index, 1)">下移</button>
      </li>
      <li v-for="key in hidden" :key="key">
        <label><input type="checkbox" :disabled="editingLocked" @change="setVisible(key, true)" />{{ t('dashboard.' + key) }}</label>
      </li>
    </ol>
    <div class="workspace-preference-actions">
      <button v-if="state === 'unknown'" class="btn" type="button" @click="readOriginal">查询原保存结果</button>
      <button v-else-if="state === 'missing'" class="btn" type="button" @click="writeOriginal">用原操作重试</button>
      <button v-else-if="state === 'conflict'" class="btn" type="button" @click="load(true)">读取最新版本并保留草稿</button>
      <button v-else-if="state === 'unavailable'" class="btn" type="button" @click="load(true)">重读个性配置</button>
      <button v-else-if="snapshot" class="btn btn-primary" type="button" :disabled="editingLocked || !dirty" @click="save">保存个性配置</button>
    </div>
  </details>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import {
  metricKeys, workspaceMetricsApi, type MetricKey, type WorkspacePreferenceCommand,
  type WorkspacePreferenceReceipt, type WorkspacePreferenceSnapshot,
} from '@/api/sys/workspaceMetrics'

type State = 'loading' | 'ready' | 'saving' | 'conflict' | 'unknown' | 'missing' | 'unavailable'
const { t } = useI18n()
const emit = defineEmits<{ visible: [keys: MetricKey[]] }>()
const storeKey = 'cp6-workspace-preferences-pending'
const snapshot = ref<WorkspacePreferenceSnapshot>()
const visible = ref<MetricKey[]>([...metricKeys])
const hidden = computed(() => metricKeys.filter(key => !visible.value.includes(key)))
const state = ref<State>('loading')
const message = ref('')
const dirty = computed(() => snapshot.value !== undefined &&
  snapshot.value.visibleMetricKeys.join(',') !== visible.value.join(','))
const editingLocked = computed(() => state.value !== 'ready' && state.value !== 'conflict')
let stamp = identityStamp()
let activeRead: AbortController | undefined
let pending: WorkspacePreferenceCommand | undefined
let inactive = false

function setVisible(key: MetricKey, show: boolean) {
  if (editingLocked.value) return
  visible.value = show ? [...visible.value, key] : visible.value.filter(current => current !== key)
}
function move(index: number, delta: number) {
  const next = index + delta
  if (editingLocked.value || next < 0 || next >= visible.value.length) return
  const key = visible.value[index]
  const neighbour = visible.value[next]
  if (!key || !neighbour) return
  const order = [...visible.value]
  order[index] = neighbour
  order[next] = key
  visible.value = order
}
function validKeys(keys: unknown): keys is MetricKey[] {
  return Array.isArray(keys) && keys.every(key => metricKeys.some(metric => metric === key)) && new Set(keys).size === keys.length
}
function validRevision(value: unknown): boolean {
  return typeof value === 'string' && /^\d{1,19}$/.test(value)
}
function sameContext(expected: string) {
  return !inactive && expected === stamp && expected === identityStamp()
}
function accept(current: WorkspacePreferenceSnapshot, preserveDraft = false) {
  if (!validRevision(current.revision) || !validKeys(current.visibleMetricKeys)) throw new Error('个性配置无效。')
  const preserve = preserveDraft && snapshot.value !== undefined && dirty.value
  snapshot.value = current
  if (!preserve) visible.value = [...current.visibleMetricKeys]
  emit('visible', current.visibleMetricKeys)
  state.value = 'ready'
}
function retain(command: WorkspacePreferenceCommand | undefined) {
  pending = command
  if (command) sessionStorage.setItem(storeKey, JSON.stringify({ identity: stamp, command }))
  else sessionStorage.removeItem(storeKey)
}
function newRead() {
  activeRead?.abort()
  const request = new AbortController()
  activeRead = request
  return request
}
function status(error: unknown) {
  return (error as { response?: { status?: number } })?.response?.status
}

async function load(preserveDraft = false) {
  if (localStorage.getItem('cp6_authed') !== '1') return
  const request = newRead()
  const expected = stamp
  state.value = 'loading'
  try {
    const saved = await workspaceMetricsApi.getPreferences(request.signal)
    if (request !== activeRead || !sameContext(expected)) return
    accept(saved, preserveDraft)
    message.value = preserveDraft && dirty.value ? '已读取最新版本。请核对草稿后保存。' : ''
  } catch {
    if (request !== activeRead || !sameContext(expected)) return
    state.value = 'unavailable'
    message.value = '个性配置暂不可用。'
  }
}
function applied(receipt: WorkspacePreferenceReceipt, command: WorkspacePreferenceCommand) {
  if (receipt.operationRef !== command.operationRef) throw new Error('个性配置回执不一致。')
  accept(receipt.snapshot)
  retain(undefined)
  message.value = '已保存，版本 ' + receipt.snapshot.revision + '。'
}
function save() {
  if (state.value !== 'ready' || pending || !snapshot.value || !dirty.value) return
  const command = { operationRef: crypto.randomUUID(), expectedRevision: snapshot.value.revision,
    visibleMetricKeys: [...visible.value] }
  retain(command)
  void writeOriginal()
}
async function writeOriginal() {
  const command = pending
  if (!command || state.value === 'saving' || !sameContext(stamp)) return
  const expected = stamp
  state.value = 'saving'
  message.value = '正在保存个性配置。'
  try {
    const receipt = await workspaceMetricsApi.savePreferences(command)
    if (sameContext(expected) && command === pending) applied(receipt, command)
  } catch (error) {
    if (!sameContext(expected) || command !== pending) return
    const failure = status(error)
    if (failure === 412) {
      retain(undefined)
      state.value = 'conflict'
      message.value = '配置已由其他会话更新。草稿已保留。'
    } else if (failure === 400 || failure === 403 || failure === 409) {
      retain(undefined)
      state.value = 'unavailable'
      message.value = '服务器拒绝本次保存。请重新读取个性配置。'
    } else {
      state.value = 'unknown'
      message.value = '保存结果待确认。请查询原操作。'
    }
  }
}
async function readOriginal() {
  const command = pending
  if (!command) return
  const request = newRead()
  const expected = stamp
  state.value = 'loading'
  try {
    const receipt = await workspaceMetricsApi.readPreferenceOperation(command.operationRef, request.signal)
    if (request === activeRead && sameContext(expected) && command === pending) applied(receipt, command)
  } catch (error) {
    if (request !== activeRead || !sameContext(expected) || command !== pending) return
    const missing = status(error) === 404
    state.value = missing ? 'missing' : 'unknown'
    message.value = missing ? '尚未查到原结果。可用原操作重试。' : '暂时无法确认原保存结果。'
  }
}
function updateContext() {
  const next = identityStamp()
  if (stamp === next) return
  activeRead?.abort()
  stamp = next
  retain(undefined)
  snapshot.value = undefined
  visible.value = [...metricKeys]
  emit('visible', visible.value)
  state.value = 'loading'
  message.value = ''
  void load()
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, updateContext)
  window.addEventListener('storage', updateContext)
  try {
    const stored = JSON.parse(sessionStorage.getItem(storeKey) ?? 'null')
    if (stored?.identity === stamp && stored.command &&
        typeof stored.command.operationRef === 'string' &&
        validRevision(stored.command.expectedRevision) && validKeys(stored.command.visibleMetricKeys)) {
      pending = stored.command
      void readOriginal()
      return
    }
  } catch { /* 无法解析旧草稿时读取当前配置。 */ }
  retain(undefined)
  void load()
})
onUnmounted(() => {
  inactive = true
  activeRead?.abort()
  window.removeEventListener(AUTH_EVENT, updateContext)
  window.removeEventListener('storage', updateContext)
})
</script>

<style scoped>
.workspace-preferences { margin-block: 0.75rem; }
.workspace-preferences summary { cursor: pointer; }
.workspace-preference-list { margin: 0.5rem 0; padding-inline-start: 1.5rem; }
.workspace-preference-list li { margin: 0.25rem 0; }
.workspace-preference-list label { margin-inline-end: 0.55rem; }
.workspace-preference-list input { margin-inline-end: 0.45rem; }
.workspace-preference-actions { margin-top: 0.5rem; }
</style>
