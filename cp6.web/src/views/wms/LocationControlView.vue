<template>
  <main class="location-control">
    <h1>库位本域封锁</h1>
    <p>管理当前库位的本域限制。解除指定封锁不会解除初始化隔离、质量或其他来源限制，也不代表库位已启用。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <section v-if="pending"><p>有一笔操作结果待确认。请先查询，避免重复提交。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !permissions.has('wms-location:abandon')" @click="recover(true)">关闭未提交原操作</button></section>
    <section v-if="state">
      <p>控制版本 {{ state.controlRevision }} · 未解除本域封锁 {{ state.openBlocks }} 项</p>
      <p>{{ state.isBlocked ? '当前库位仍有封锁标记。' : '当前没有封锁标记；作业资格仍须另行核定。' }}</p>
      <p v-if="state.initializationQuarantined">库位处于初始化隔离，尚未启用。</p>
      <p v-if="state.otherRestrictionRetained">已有或其他来源的限制仍保留；本页不能解除这些限制。</p>
      <label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" :disabled="busy || !!pending" /></label>
      <button :disabled="busy || !!pending || !reason.trim() || !permissions.has('wms-location:block')" @click="block">添加本域封锁</button>
      <section v-if="prepared && !state.entries.some(e => e.reference.id === prepared?.reference.id)"><p>本次解除决定已保存，绑定封锁 {{ prepared.blockId }}。</p><button :disabled="busy || !!pending || !reason.trim() || !permissions.has('wms-location:unblock') || prepared.etag !== state.etag" @click="applyPrepared">应用本次解除决定</button></section>
      <h2>决定与历史</h2><p v-if="!state.entries.length">当前页没有可读取的决定。</p>
      <article v-for="e in state.entries" :key="e.reference.id">
        <p>{{ kind(e.original.kind) }} · {{ e.original.reason }} · {{ e.state === 'OPEN' ? '未解除' : e.state === 'RELEASED' ? '已解除' : '已记录' }}</p>
        <button v-if="e.original.kind === 'BLOCK' && e.state === 'OPEN'" :disabled="busy || !!pending || !reason.trim() || !permissions.has('wms-location:unblock')" @click="decide(e)">记录此项解除决定</button>
        <button v-if="e.original.kind === 'RELEASE_DECISION' && e.original.actorId === state.actorId" :disabled="busy || !!pending || !reason.trim() || !permissions.has('wms-location:unblock') || e.original.nativeEtag !== state.etag || e.original.controlBefore !== state.controlRevision" @click="release(e)">按此决定解除</button>
      </article>
      <button :disabled="busy || page === 1" @click="move(-1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || state.entries.length < 50" @click="move(1)">下一页</button>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { locationControlApi as api, type ControlView, type ControlEntry, type ControlResult } from '@/api/wms/locationControl'
import { controlView, pendingControl, sendControl, recoverControl } from '@/auth/locationControlRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const route = useRoute(), location = computed(() => String(route.params.id || '')), permissions = usePermissionStore()
const state = ref<ControlView | null>(null), page = ref(1), pending = ref<string | null>(null), busy = ref(false), reason = ref(''), error = ref(''), notice = ref('')
const prepared = ref<ControlResult | null>(null)
let live = true, generation = 0
function clear() { state.value = null; pending.value = null; reason.value = ''; prepared.value = null }
function kind(k: string) { return k === 'BLOCK' ? '本域封锁' : k === 'RELEASE' ? '指定解除' : '本域解除决定' }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(), id = location.value; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || id !== location.value || !authenticationCanRead() || !permissions.has('wms-location:query')) throw new Error('身份或库位读取权限已变化。') }
  try { check(); await work(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403, 404].includes(e?.response?.status)) clear(); error.value = e?.message || '操作未完成，请核对权限与当前状态。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (state.value) { try { pending.value = pendingControl(state.value) } catch (e: any) { error.value = e.message; pending.value = 'invalid' } } } }
}
async function load(check: () => void) { const v = controlView(value(await api.read(location.value, page.value)), state.value ?? undefined); check(); if (v.locationId !== location.value) throw new Error('返回库位不一致。'); state.value = v; pending.value = pendingControl(v) }
async function reload() { await run(async check => { clear(); await load(check) }) }
async function command(route: 'blocks' | 'release-decisions' | 'releases', body: object, permission: string) {
  await run(async check => { if (!state.value || !permissions.has(permission)) throw new Error('当前没有操作权限。'); const verify = () => { check(); if (!permissions.has(permission)) throw new Error('操作权限已变化。') }; const r = await sendControl(state.value, route, body, verify); check(); prepared.value = r.result?.effect === 'RELEASE_DECISION' ? r.result : null; reason.value = ''; notice.value = route === 'release-decisions' ? '已记录本域解除决定，请核对后应用。' : '本域控制已保存。'; await load(check) })
}
function block() { return command('blocks', { reason: reason.value, blockScope: 'LOCAL_OPERATIONAL' }, 'wms-location:block') }
function decide(e: ControlEntry) { return command('release-decisions', { blockId: e.original.blockId, reason: reason.value }, 'wms-location:unblock') }
function release(e: ControlEntry) { return command('releases', { blockId: e.original.blockId, reason: reason.value, decisionRef: e.reference }, 'wms-location:unblock') }
function applyPrepared() { if (prepared.value) return command('releases', { blockId: prepared.value.blockId, reason: reason.value, decisionRef: prepared.value.reference }, 'wms-location:unblock') }
async function recover(close: boolean) { await run(async check => { if (!state.value) return; const verify = () => { check(); if (close && !permissions.has('wms-location:abandon')) throw new Error('当前没有恢复权限。') }; const r = await recoverControl(state.value, close, verify); prepared.value = r.result?.effect === 'RELEASE_DECISION' ? r.result : null; notice.value = r.state === 'UNKNOWN' ? '原操作仍待确认，请保留恢复记录。' : r.state === 'CLOSED' ? '未提交原操作已关闭。' : '已查回原结果。'; await load(check) }) }
async function move(delta: number) { page.value += delta; await run(load) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = '' }
watch([location, () => permissions.loaded, () => permissions.has('wms-location:query'), () => permissions.has('wms-location:block'), () => permissions.has('wms-location:unblock')], () => { reset(); page.value = 1 })
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.location-control{max-width:1000px;margin:auto;padding:24px}.location-control p{line-height:1.6}.location-control button{margin:8px}.location-control textarea{display:block;width:min(100%,600px);min-height:70px}.location-control article{padding:12px;border-bottom:1px solid #ddd}[role=alert]{color:#9f2424}
</style>
