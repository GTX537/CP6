<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { AUTH_EPOCH, AUTH_RECORD, IMPERSONATION, AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { definitionCloneApi as api, type CloneIntent, type CloneResult, type CloneSource, type DefinitionKind } from '@/api/oa/definitionClone'
const props = defineProps<{ kind: DefinitionKind; source?: CloneSource; disabled: boolean }>()
const emit = defineEmits<{ reload: [key: string]; locked: [value: boolean]; privateClear: [] }>()
const key = ref(''), name = ref(''), error = ref(''), message = ref(''), busy = ref(false), unavailableCache = ref(false)
const pending = ref<CloneIntent>()
const cache = `cp6-definition-clone-${props.kind}-pending`
const unavailable = computed(() => busy.value || unavailableCache.value || !!pending.value || props.disabled || !props.source)
let live = true, generation = 0, controller: AbortController | undefined
class StorageError extends Error {}
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value }))
  else sessionStorage.removeItem(cache)
}
watch(() => [props.source?.key, props.source?.versionId], () => { if (!pending.value) { key.value = ''; name.value = ''; error.value = ''; message.value = '' } })
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>) {
  if (busy.value || unavailableCache.value) return
  const identity = identityStamp(), request = generation; controller = new AbortController(); busy.value = true; error.value = ''; emit('locked', true)
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try { await action(check, controller.signal); check() }
  catch (e: any) {
    try {
      check()
      if (e instanceof StorageError) error.value = e.message
      else if ([401,403].includes(e?.response?.status)) { key.value = ''; name.value = ''; emit('privateClear'); error.value = '当前权限无法克隆，已清除可见内容；未确认的原命令仍保留。' }
      else error.value = pending.value ? '克隆结果尚未确认，请查询原结果或以原意图重试。' : '克隆未被接受，请核对来源、新标识和当前权限。'
    } catch { /* Response belongs to an old identity. */ }
  } finally { if (live && generation === request) { busy.value = false; emit('locked', !!pending.value || unavailableCache.value) } }
}
function accept(result: CloneResult) {
  const intent = pending.value!
  if (result.outcome !== 'SUCCEEDED' || result.commandId !== intent.command.commandId || result.result?.key !== intent.command.body.newKey) throw new Error('原命令未确认')
  const target = result.result.key; pending.value = undefined; remember(); message.value = '已创建停用草稿，来源版本与已有运行记录保持不变。'
  emit('locked', false); emit('reload', target)
}
function retry() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const intent = pending.value!, wasUnknown = !!intent.unknown
    try { remember(); intent.unknown = true; remember() }
    catch { intent.unknown = wasUnknown; throw new StorageError('浏览器无法保存恢复记录，本次克隆请求尚未发送。') }
    let result
    try { result = await api.execute(JSON.parse(JSON.stringify(intent)), signal) }
    catch (e: any) { check(); if (!wasUnknown && [400,401,403,404,409,412,422].includes(e?.response?.status)) { pending.value = undefined; remember() }; throw e }
    check(); accept(result)
  })
}
function clone() {
  if (unavailable.value || !key.value.trim() || !name.value.trim()) return
  pending.value = { kind: props.kind, sourceKey: props.source!.key, command: { commandId: crypto.randomUUID(), expectedVersion: null,
    body: { sourceVersionId: props.source!.versionId, newKey: key.value, newName: name.value } } }
  return retry()
}
function recover() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const result = await api.original(pending.value!, signal); check()
    if (result.outcome === 'SUCCEEDED') accept(result)
    else message.value = '尚未观察到原结果，不能据此判断未提交。请继续查询或以原意图重试。'
  })
}
function identityChanged() {
  generation++; controller?.abort(); pending.value = undefined; busy.value = false; key.value = ''; name.value = ''; message.value = ''
  try { remember(); unavailableCache.value = false } catch { unavailableCache.value = true }
  emit('locked', unavailableCache.value); error.value = '登录身份已变化，请重新读取来源。'
}
function storageChanged(event: StorageEvent) {
  if (event.key === null || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION, 'cp6_authed', 'userName'].includes(event.key)) identityChanged()
}
onMounted(() => {
  try {
    const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null'), p = saved?.pending
    if (saved?.identity === identityStamp() && p) {
      if (p.kind !== props.kind || typeof p.sourceKey !== 'string' || !p.command?.commandId || p.command.expectedVersion !== null
          || typeof p.command.body?.sourceVersionId !== 'string' || typeof p.command.body?.newKey !== 'string' || typeof p.command.body?.newName !== 'string') throw new Error('INVALID_PENDING')
      pending.value = { ...p, unknown: true }; emit('locked', true); message.value = '有一次克隆尚待确认，请先查询原结果。'
    } else sessionStorage.removeItem(cache)
  } catch { unavailableCache.value = true; emit('locked', true); error.value = '无法读取克隆恢复记录，请恢复浏览器存储后重新打开；原缓存已保留。' }
})
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storageChanged)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storageChanged) })
</script>
<template>
  <section aria-label="克隆已发布版本">
    <h4>克隆已发布版本</h4>
    <p v-if="source">来源：{{ source.key }}，版本 {{ source.version }}。创建新停用草稿{{ kind === 'FLOW' ? '，流程身份码将清空' : '' }}。</p>
    <p v-else>先在历史版本列表选中要克隆的版本。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <label>新标识<input v-model="key" :disabled="unavailable" maxlength="100" /></label>
    <label>新名称<input v-model="name" :disabled="unavailable" maxlength="200" /></label>
    <button :disabled="unavailable || !key.trim() || !name.trim()" @click="clone">确认克隆此发布版本</button>
    <template v-if="pending"><button :disabled="busy || unavailableCache" @click="recover">查询原克隆结果</button><button :disabled="busy || unavailableCache" @click="retry">以原克隆意图重试</button></template>
  </section>
</template>
<style scoped>
section { border-top: 1px solid #dbe3ed; margin-top: 16px; padding-top: 12px; } label { display: inline-grid; gap: 4px; margin: 4px 12px 4px 0; } input { padding: 6px; } button { padding: 8px; margin: 4px; } [role=alert] { color: #a63727; }
</style>
