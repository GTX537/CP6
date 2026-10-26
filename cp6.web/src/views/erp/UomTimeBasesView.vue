<template>
  <main class="time-page" :aria-busy="busy">
    <header><div><h1>单位时间含义</h1><p>明确登记基准单位对应的秒、分钟或小时，供资源工时精确换算。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="panel"><h2>待确认操作</h2><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section class="panel"><form @submit.prevent="loadPage(1)"><label>单位编码 <input v-model="search" aria-label="时间单位搜索" maxlength="100" /></label><button :disabled="busy">搜索</button></form>
      <table><thead><tr><th>基准单位</th><th>量纲</th><th>单位版本</th><th>操作</th></tr></thead><tbody><tr v-for="row in rows.filter(r => r.profile.mode === 'BASE')" :key="row.reference.id"><td>{{ row.profile.code }} / {{ row.profile.name }}</td><td>{{ row.profile.dimensionKey }}</td><td>{{ row.reference.version }}</td><td><button :disabled="busy" @click="open(row.reference.id)">查看时间含义</button></td></tr></tbody></table>
      <p v-if="!rows.some(r => r.profile.mode === 'BASE')">此页没有可查看的基准单位。</p><nav><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <section v-if="editor" class="panel"><h2>{{ editor.currentBase.profile.code }} · 时间含义</h2><p>单位版本 {{ editor.currentBase.reference.version }}；时间含义版本 {{ editor.expectedVersion === 'absent' ? '尚未登记' : editor.expectedVersion }}。</p>
      <p v-if="editor.definition">{{ editor.definition.currentApplicable ? '当前可用于换算' : '当前不可采用，请核对单位版本和启用状态' }}；已登记含义：{{ labels[editor.definition.definition.basis] }}。</p>
      <p>单位编码和量纲名称不会自动赋予时间含义。请依据原单位定义登记。</p>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending || !canEdit"><label>一个基准单位代表 <select v-model="basis" aria-label="时间含义" required><option value="">请选择</option><option value="SECOND">一秒</option><option value="MINUTE">一分钟</option><option value="HOUR">一小时</option></select></label>
        <label>登记依据 <textarea v-model="reason" aria-label="登记依据" required maxlength="500" /></label><label><input v-model="enabled" type="checkbox" aria-label="启用时间含义" />启用</label><button v-if="canEdit">保存时间含义</button></fieldset></form>
      <form v-if="editor.definition" @submit.prevent="loadHistory"><label>历史版本 <input v-model="historyVersion" aria-label="时间含义历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h3>历史版本 {{ history.reference.version }}</h3><p>{{ labels[history.definition.basis] }} · {{ history.definition.reason }} · {{ history.currentApplicable ? '当前可采用' : '不可作为当前换算依据' }}</p><pre>{{ history.originalJson }}</pre></article>
      <details v-if="editor.definition"><summary>当前登记原件</summary><pre>{{ editor.definition.originalJson }}</pre></details>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { uomApi, type UomVersion } from '@/api/erp/uomProfiles'
import { timeApi, type TimeBasis, type TimeEditor, type TimeVersion, type TimeScope } from '@/api/erp/uomTimeBases'
import { timeResponse, pendingTimeOperation, saveTimeOperation, resolveTimeOperation, type PendingTimeOperation } from '@/auth/timeOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canEdit = computed(() => permissions.has('erp-uom-profile:edit'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<TimeScope | null>(null), pending = ref<PendingTimeOperation | null>(null)
const search = ref(''), page = ref(1), rows = ref<UomVersion[]>([]), editor = ref<TimeEditor | null>(null), history = ref<TimeVersion | null>(null), historyVersion = ref('1')
const basis = ref<TimeBasis | ''>(''), enabled = ref(false), reason = ref(''), labels = { SECOND: '一秒', MINUTE: '一分钟', HOUR: '一小时' }
let live = true, generation = 0
function clearEditor() { editor.value = null; history.value = null; basis.value = ''; enabled.value = false; reason.value = '' }
function clearPrivate() { clearEditor(); rows.value = []; scope.value = null; pending.value = null; search.value = ''; historyVersion.value = '1' }
function guard() { const stamp = identityStamp(), current = generation; return () => { requireIdentity(stamp); if (!live || generation !== current) throw new Error('当前页面已变化。') } }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const check = guard(); busy.value = true; error.value = ''; notice.value = ''
  try { await action(check); check() } catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) clearPrivate()
    const code = failure?.response?.data?.message || failure?.message || ''
    error.value = ['TIME_BASIS_CHANGED', 'UOM_PROFILE_CHANGED'].includes(code) ? '版本已变化，请刷新并重新选择。' : /^[A-Z][A-Z0-9_]+$/.test(code) ? '无法完成，请核对权限、当前单位版本及原操作状态。' : code || '暂时无法完成。'
  } catch { /* obsolete response */ } } finally { try { check(); try { pending.value = scope.value ? pendingTimeOperation(scope.value, 'TIME_BASIS') : null } catch (failure: any) { error.value = failure.message }; busy.value = false } catch { /* identity handler owns the page */ } }
}
async function reload() { await run(async check => { clearEditor(); rows.value = []; if (!permissions.loaded) await permissions.loadMyActions(); check()
  const current = timeResponse(await timeApi.scope()); check(); scope.value = current
  const result = timeResponse(await uomApi.list(search.value, page.value)); check(); rows.value = result
}) }
async function loadPage(target: number) { await run(async check => { clearEditor(); const result = timeResponse(await uomApi.list(search.value, target)); check(); rows.value = result; page.value = target }) }
async function open(id: string) { await run(async check => { clearEditor(); const value = timeResponse(await timeApi.editor(id)); check(); editor.value = value
  basis.value = value.definition?.definition.basis ?? ''; enabled.value = value.definition?.definition.enabled ?? false; reason.value = value.definition?.definition.reason ?? ''
}) }
async function save() {
  if (!editor.value || !scope.value || !canEdit.value || !basis.value) return
  const current = editor.value, currentScope = scope.value, definition = { baseRef: current.currentBase.reference, basis: basis.value, enabled: enabled.value, reason: reason.value }
  await run(async check => { await saveTimeOperation(currentScope, 'TIME_BASIS', current.currentBase.reference.id, check, operationId => timeApi.save({ operationId, expectedVersion: current.expectedVersion, definition })); check(); clearEditor(); notice.value = '时间含义已保存，请重新打开核对当前版本。' })
}
async function loadHistory() { const current = editor.value; if (!current) return; await run(async check => { history.value = null; const value = timeResponse(await timeApi.read(current.currentBase.reference.id, historyVersion.value)); check(); history.value = value }) }
async function recover(close: boolean) { const current = scope.value; if (!current) return; await run(async check => { const result = await resolveTimeOperation(current, 'TIME_BASIS', close, check); check(); if (result.state === 'COMMITTED') clearEditor()
  notice.value = result.state === 'COMMITTED' ? '原操作已提交，请重新打开查看。' : result.state === 'CLOSED' ? '未提交操作已结束。' : '结果仍未确认，请继续查询或结束未提交操作。'
}) }
function identityChanged() { generation++; clearPrivate(); busy.value = false; error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged); clearPrivate() })
</script>
<style scoped>
.time-page{max-width:1100px;margin:auto;padding:24px;color:var(--text,#18343d)}header,nav,form{display:flex;gap:14px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}.panel{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px;overflow:auto}button,input,select,textarea{font:inherit;padding:8px 12px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}button:focus-visible,input:focus,select:focus,textarea:focus{outline:2px solid #14969e;outline-offset:2px}table{width:100%;min-width:520px;border-collapse:collapse}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce5e9}fieldset{border:0;padding:0;display:grid;gap:16px;width:100%}fieldset label{display:flex;gap:10px;align-items:center;flex-wrap:wrap}textarea{min-width:240px;flex:1;min-height:60px}nav,form{margin-top:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f6f7;padding:14px;max-height:360px;overflow:auto}[role=alert]{color:#a02828}@media(max-width:600px){.time-page{padding:10px}.panel{padding:14px}}
</style>
