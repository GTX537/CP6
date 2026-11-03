<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { getActingAs } from '@/stores/oaActingAs'
import { nativeTaskBatchApi as api, type TransferFilter, type TransferPreview, type TransferReport } from '@/api/oa/nativeTaskBatch'
import QueryUserPicker from '../query/QueryUserPicker.vue'
const opened = ref(false), from = ref(''), to = ref(''), reason = ref(''), flow = ref(''), before = ref(''), taskIds = ref('')
const preview = ref<TransferPreview>(), report = ref<TransferReport>(), busy = ref(false), error = ref(''), message = ref('')
const cache = 'cp6-native-transfer-batch', identity = () => JSON.stringify([identityStamp(), getActingAs()?.userId ?? null])
type Recovery = { identity: string; command: string; batchId?: string; digest?: string; confirmed: boolean }
const recovery = ref<Recovery>(); let raw: string | undefined, generation = 0, live = true
const frozen = computed(() => !!recovery.value || busy.value)
const canExecute = computed(() => !!preview.value?.items.length && !!recovery.value && !busy.value && !getActingAs())
function remember() { if (recovery.value) sessionStorage.setItem(cache, JSON.stringify(recovery.value)); else sessionStorage.removeItem(cache) }
function clear() { preview.value = undefined; report.value = undefined; raw = undefined; from.value = ''; to.value = ''; reason.value = ''; flow.value = ''; before.value = ''; taskIds.value = '' }
function identityChanged() { generation++; clear(); recovery.value = undefined; busy.value = false; error.value = '身份已变化，请重新读取。'; try { remember() } catch { /* no write sent */ } }
try { const stored = JSON.parse(sessionStorage.getItem(cache) ?? 'null'); if (stored?.identity === identity() && typeof stored.command === 'string') { recovery.value = stored; opened.value = true } }
catch { error.value = '无法读取浏览器恢复记录，请检查浏览器存储。' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || getActingAs()) return
  const epoch = identity(), request = generation; busy.value = true; error.value = ''
  const check = () => { if (!live || generation !== request || identity() !== epoch) throw new Error('STALE') }
  try { await action(check); check() }
  catch (e: any) {
    try { check(); if ([401, 403, 404].includes(e?.response?.status)) { clear(); error.value = '当前权限拒绝访问。原批次记录已保留，请恢复权限后查询。' }
      else error.value = recovery.value ? '结果尚未确认，请查询原预览或逐项结果。重试只处理原名单。' : '无法生成预览，请核对人员、范围和权限。' } catch { /* old identity */ }
  } finally { if (live && generation === request) busy.value = false }
}
function accept(value: TransferPreview) { preview.value = value; recovery.value = { ...recovery.value!, batchId: value.batchId, digest: value.previewDigest }; remember() }
async function inspect() {
  if (recovery.value || !from.value || !to.value || from.value === to.value || !reason.value.trim()) return
  const ids = taskIds.value.trim() ? taskIds.value.trim().split(/[\s,，]+/) : null
  if (ids && (ids.length > 500 || new Set(ids).size !== ids.length || ids.some(id => !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id)))) { error.value = '任务编号须唯一且不超过 500 个。'; return }
  const cutoff = before.value ? new Date(before.value) : null
  if (cutoff && !Number.isFinite(cutoff.getTime())) { error.value = '截止时间无效。'; return }
  const filter: TransferFilter = { fromUserId: from.value, toUserId: to.value, reason: reason.value, flowKey: flow.value || null, beforeUtc: cutoff?.toISOString() ?? null, explicitTaskIds: ids }
  const command = crypto.randomUUID(); raw = JSON.stringify({ commandId: command, expectedVersion: null, body: filter }); recovery.value = { identity: identity(), command, confirmed: false }
  try { remember() } catch { recovery.value = undefined; raw = undefined; error.value = '浏览器无法保存恢复记录，请求未发送。'; return }
  await run(async check => { const value = await api.preview(raw!); check(); accept(value) })
}
async function recoverPreview(retry = false) {
  if (!recovery.value) return
  await run(async check => { const value = retry && raw ? await api.preview(raw) : await api.original(recovery.value!.command); check(); if (value) accept(value); else message.value = '尚未观察到原预览。预览不转交任务；可继续查询或按原输入重试。' })
}
async function execute() {
  if (!canExecute.value) return
  recovery.value = { ...recovery.value!, confirmed: true }
  try { remember() } catch { error.value = '无法保存批次确认，请求未发送。'; return }
  const intent = { ...recovery.value }
  await run(async check => { const value = await api.execute(intent.batchId!, intent.digest!); check(); report.value = value; message.value = '逐项结果已返回。未确认项请继续查询；版本冲突需重新预览。' })
}
async function refresh() {
  if (!recovery.value?.batchId) return
  const batch = recovery.value.batchId; await run(async check => { const value = await api.report(batch); check(); report.value = value })
}
function reset() {
  if (busy.value || recovery.value?.confirmed && (!report.value || report.value.items.some(i => i.state === 'UNKNOWN'))) return
  generation++; clear(); recovery.value = undefined; remember(); message.value = ''; error.value = ''
}
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section aria-label="管理员批量转交"><button :disabled="!!getActingAs()" @click="opened = !opened">管理员批量转交</button>
    <div v-if="opened"><p>仅处理预览中确认的任务，最多 500 项。接收人必须具有当前审批资格；转交不算审批。</p>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
      <fieldset :disabled="frozen" :inert="frozen"><QueryUserPicker v-model="from" label="原办理人" /><QueryUserPicker v-model="to" label="接收人" />
        <label>流程标识（可选）<input v-model="flow" maxlength="100" /></label><label>任务分配截止时间（本地时间，可选）<input v-model="before" type="datetime-local" /></label>
        <label>任务编号（可选，逗号或换行分隔）<textarea v-model="taskIds" /></label><label>转交原因<textarea v-model="reason" maxlength="1000" /></label></fieldset>
      <button :disabled="frozen" @click="inspect">预览准确名单</button>
      <template v-if="recovery"><button :disabled="busy" @click="recoverPreview()">查询原预览</button><button v-if="raw && !preview" :disabled="busy" @click="recoverPreview(true)">原输入重试预览</button>
        <button v-if="recovery.batchId" :disabled="busy" @click="refresh">查询逐项结果</button><button :disabled="busy || recovery.confirmed && (!report || report.items.some(i => i.state === 'UNKNOWN'))" @click="reset">开始新预览</button></template>
      <div v-if="preview"><p>预览时间：{{ new Date(preview.previewedAt).toLocaleString() }} · {{ preview.items.length }} 项 · 原因：{{ preview.filter.reason }}</p>
        <p>原办理人：{{ preview.filter.fromUserId }} → 接收人：{{ preview.filter.toUserId }}</p>
        <table><thead><tr><th>任务</th><th>流程</th><th>来源</th></tr></thead><tbody><tr v-for="item in preview.items" :key="item.taskId"><td>{{ item.taskId }}</td><td>{{ item.flowKey }}</td><td>{{ item.contextKind }}</td></tr></tbody></table>
        <button :disabled="!canExecute" @click="execute">{{ recovery?.confirmed ? '按原名单重试' : '确认转交以上任务' }}</button></div>
      <table v-if="report"><thead><tr><th>任务</th><th>结果</th><th>原因</th></tr></thead><tbody><tr v-for="item in report.items" :key="item.taskId"><td>{{ item.taskId }}</td><td>{{ { SUCCEEDED: '已转交', REJECTED: '未接受', UNKNOWN: '待确认' }[item.state] }}</td><td>{{ item.error }}</td></tr></tbody></table>
    </div>
  </section>
</template>
<style scoped>section { margin: 12px 0; }label { display: block; margin: 8px 0; }textarea { display: block; min-width: 300px; }table { border-collapse: collapse; margin: 8px 0; }td, th { padding: 5px; border: 1px solid var(--el-border-color); }button { margin: 4px; }</style>
