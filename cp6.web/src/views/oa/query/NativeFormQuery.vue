<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { getActingAs, setActingAs, clearActingAs } from '@/stores/oaActingAs'
import { taskDelegationApi, type TaskGrantPrincipal } from '@/api/oa/taskDelegation'
import { nativeQueryApi as api, type QueryFilter, type QueryItem, type QueryPage, type QueryDetail } from '@/api/oa/nativeQuery'
import { formObject, readFormJson, schemaFields } from '../catalog/personalFormJson'
import PersonalFormFields from '../catalog/PersonalFormFields.vue'
import QueryUserPicker from './QueryUserPicker.vue'
const route = useRoute(), filter = reactive({ starterId: '', handlerId: '', flowKey: '', formKey: '', keyword: '', status: '', formVersion: '', hasInstance: '', from: '', to: '' })
const rows = ref<QueryPage>(), detail = ref<QueryDetail>(), page = ref(1), asOf = ref<string>(), busy = ref(false), detailBusy = ref(false)
const error = ref(''), zone = ref(''), zoneError = ref(''), principals = ref<TaskGrantPrincipal[]>([]), principalError = ref(''), chosen = ref(getActingAs()?.userId ?? '')
const fields = computed(() => detail.value?.form ? schemaFields(detail.value.form.schemaJson) : [])
const values = computed(() => detail.value?.form ? formObject(readFormJson(detail.value.form.dataJson)) : {})
let live = true, generation = 0, detailGeneration = 0, contextGeneration = 0, principalGeneration = 0
let listController: AbortController | undefined, detailController: AbortController | undefined, contextController: AbortController | undefined
const stamp = () => JSON.stringify([identityStamp(), getActingAs()?.userId ?? null])
let identity = stamp()
function clear() {
  generation++; detailGeneration++; listController?.abort(); detailController?.abort(); rows.value = undefined; detail.value = undefined
  busy.value = detailBusy.value = false; asOf.value = undefined; page.value = 1
}
function changed() {
  if (identity === stamp()) return
  identity = stamp(); clear(); contextGeneration++; principalGeneration++; contextController?.abort(); zone.value = ''; principals.value = []
  Object.keys(filter).forEach(k => { filter[k as keyof typeof filter] = '' }); chosen.value = getActingAs()?.userId ?? ''
  error.value = '身份已改变，请重新查询。'
}
watch(filter, clear)
async function context() {
  const current = ++contextGeneration, who = stamp(); contextController?.abort(); contextController = new AbortController(); zone.value = ''; zoneError.value = ''
  try { const value = await api.context(contextController.signal); if (live && current === contextGeneration && who === stamp()) zone.value = value.timeZoneId }
  catch { if (live && current === contextGeneration && who === stamp()) zoneError.value = '未能取得租户时区，暂不能按日期筛选。' }
}
async function grants() {
  const current = ++principalGeneration, who = stamp(); principals.value = []; principalError.value = ''
  try { const value = await taskDelegationApi.received(); if (live && current === principalGeneration && who === stamp()) principals.value = value }
  catch { if (live && current === principalGeneration && who === stamp()) principalError.value = '暂时无法读取代理授权，可切回本人查询。' }
}
function query(): QueryFilter {
  return { page: page.value, pageSize: 20, snapshotAsOf: asOf.value, starterId: filter.starterId || undefined, handlerId: filter.handlerId || undefined,
    flowKey: filter.flowKey || undefined, formKey: filter.formKey || undefined, keyword: filter.keyword || undefined,
    status: filter.status === '' ? undefined : Number(filter.status), formVersion: filter.formVersion === '' ? undefined : Number(filter.formVersion),
    hasInstance: filter.hasInstance === '' ? undefined : filter.hasInstance === 'yes', from: filter.from || undefined, to: filter.to || undefined, timeZoneId: zone.value || undefined }
}
function rejected(e: any) {
  const status = e?.response?.status, code = e?.response?.data?.code
  error.value = [401, 403, 404].includes(status) ? '当前身份无权读取这些记录，已清除原有内容。'
    : code === 'QUERY_TIMEZONE_CHANGED' ? '租户时区已变化，请重新读取时区并确认日期。'
      : status === 422 ? '查询条件无效，请检查日期、版本和筛选值。' : '查询失败，请重试。'
}
async function load(reset = false) {
  if (reset) { page.value = 1; asOf.value = undefined }
  const current = ++generation, who = stamp(); listController?.abort(); listController = new AbortController()
  detailGeneration++; detailController?.abort(); detail.value = undefined; detailBusy.value = false; rows.value = undefined; error.value = ''; busy.value = true
  try {
    const value = await api.search(query(), listController.signal)
    if (!live || current !== generation || who !== stamp()) return
    rows.value = value; asOf.value = value.snapshotAsOf
  } catch (e) { if (live && current === generation && who === stamp()) rejected(e) }
  finally { if (live && current === generation) busy.value = false }
}
async function open(row: Pick<QueryItem, 'instanceId' | 'formDataId'>) {
  const current = ++detailGeneration, who = stamp(); detailController?.abort(); detailController = new AbortController(); detail.value = undefined; detailBusy.value = true; error.value = ''
  try {
    const value = row.instanceId ? await api.instance(row.instanceId, detailController.signal) : await api.form(row.formDataId!, detailController.signal)
    if (live && current === detailGeneration && who === stamp()) detail.value = value
  } catch (e: any) { if (live && current === detailGeneration && who === stamp()) { if ([401, 403, 404].includes(e?.response?.status)) clear(); rejected(e) } }
  finally { if (live && current === detailGeneration) detailBusy.value = false }
}
async function principal() {
  const selected = principals.value.find(p => p.userId === chosen.value)
  if (chosen.value && !selected) { principalError.value = '请重新读取当前有效的代理授权。'; return }
  try { if (selected) setActingAs({ userId: selected.userId, userName: selected.name }); else clearActingAs() }
  catch { clear(); principalError.value = '无法保存身份选择，请重试。'; return }
  changed(); await Promise.all([load(true), context(), grants()])
}
const statuses = ['进行中', '已通过', '已驳回', '已撤回', '挂起', '退回待重填']
function status(row: QueryItem) { return row.submissionState === 'SubmittedUnbound' ? '已提交，未启动审批' : statuses[row.instanceStatus!] ?? '未知状态' }
function application(row: QueryItem) { return ({ UNPROVEN: '业务应用未证', APPLIED: '业务已应用', BLOCKED: '业务应用受阻', NOT_APPLIED: '业务尚未应用' } as Record<string, string>)[row.applicationSummary?.applicationState ?? ''] ?? row.applicationSummary?.applicationState }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onMounted(async () => {
  const who = stamp()
  await Promise.all([load(), context(), grants()])
  if (!live || who !== stamp()) return
  if (typeof route.query.dataId === 'string') await open({ formDataId: route.query.dataId, instanceId: null })
  else if (typeof route.query.instanceId === 'string') await open({ instanceId: route.query.instanceId, formDataId: null })
})
onBeforeUnmount(() => { live = false; clear(); contextGeneration++; principalGeneration++; contextController?.abort(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <section aria-label="提交与流程查询" class="query">
    <p>查询当前有权查看的提交和流程记录。未启动审批的提交会单独显示；流程通过与业务应用分别记录。</p>
    <div class="row"><label>查看身份<select v-model="chosen" aria-label="查看身份" @change="principal"><option value="">本人</option><option v-for="p in principals" :key="p.userId" :value="p.userId">代理 {{ p.name }}</option></select></label><button @click="grants">重新读取代理授权</button></div>
    <p v-if="principalError" role="alert">{{ principalError }}</p>
    <form @submit.prevent="load(true)">
      <QueryUserPicker :key="identity + '-starter'" v-model="filter.starterId" label="发起人" /><QueryUserPicker :key="identity + '-handler'" v-model="filter.handlerId" label="处理人" />
      <label>表单代码<input v-model="filter.formKey" maxlength="100" /></label><label>表单版本<input v-model="filter.formVersion" type="number" min="1" step="1" /></label>
      <label>流程代码<input v-model="filter.flowKey" maxlength="100" /></label><label>关键词<input v-model="filter.keyword" maxlength="200" placeholder="流程代码或业务编号" /></label>
      <label>审批关联<select v-model="filter.hasInstance" aria-label="审批关联"><option value="">全部</option><option value="yes">已启动审批</option><option value="no">未启动审批</option></select></label>
      <label>流程状态<select v-model="filter.status" aria-label="流程状态"><option value="">全部</option><option v-for="(name, value) in statuses" :key="value" :value="String(value)">{{ name }}</option></select></label>
      <label>提交开始日期<input v-model="filter.from" type="date" :disabled="!zone" /></label><label>提交结束日期<input v-model="filter.to" type="date" :disabled="!zone" /></label>
      <button :disabled="busy">查询</button><button type="button" @click="context">重新读取时区</button>
    </form>
    <p v-if="zone">日期按租户时区 {{ zone }} 计算，包含开始日与结束日。</p><p v-if="zoneError" role="alert">{{ zoneError }}</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="busy" role="status">正在查询</p>
    <template v-if="rows">
      <p>共 {{ rows.total }} 条当前可见记录。翻页沿用首次查询截点，重新查询可查看最新提交。</p><p v-if="!rows.items.length">没有符合条件的记录。</p>
      <table v-else aria-label="提交与流程记录"><thead><tr><th>表单与流程</th><th>提交时间（UTC）</th><th>状态</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows.items" :key="row.id"><td><p>{{ row.formName || row.formKey || '无普通表单' }}<span v-if="row.formVersion"> · v{{ row.formVersion }}</span></p><p>{{ row.flowName || row.flowKey || '未关联流程' }}<span v-if="row.flowVersion"> · v{{ row.flowVersion }}</span></p><p v-if="row.title">{{ row.title }}</p></td>
          <td>{{ row.submittedAtUtc }}</td><td>{{ status(row) }}<p v-if="row.applicationSummary">{{ application(row) }}<span v-if="row.applicationSummary.pendingReceiptGap">；回执尚有缺口</span></p></td><td><button @click="open(row)">查看记录</button></td></tr>
      </tbody></table>
      <nav aria-label="查询分页"><button :disabled="busy || page === 1" @click="page--; load()">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || page * rows.pageSize >= rows.total" @click="page++; load()">下一页</button></nav>
    </template>
    <p v-if="detailBusy" role="status">正在读取详情</p>
    <article v-if="detail" aria-label="提交记录详情">
      <h3>{{ detail.item.formName || detail.item.flowName || '提交记录' }}</h3><p>{{ status(detail.item) }}。此处仅供查看。</p>
      <p v-if="detail.form">表单版本 {{ detail.item.formVersion }}；内容来自此次提交的固定版本。</p>
      <PersonalFormFields v-if="detail.form" :fields="fields" :model-value="values" :mask="detail.form.fieldMask" disabled />
      <section v-if="detail.item.applicationSummary"><h4>业务应用</h4><p>{{ application(detail.item) }}</p><p v-if="detail.item.applicationSummary.pendingReceiptGap">回执链仍有缺口，当前显示已连续确认的结果。</p>
        <p v-if="detail.item.applicationSummary.businessResultRef">业务结果：{{ detail.item.applicationSummary.businessResultRef.id }} · {{ detail.item.applicationSummary.businessResultRef.version }}</p>
        <router-link v-if="detail.item.applicationSummary.detailRoute" :to="detail.item.applicationSummary.detailRoute">查看业务对象</router-link>
      </section>
      <section v-if="detail.instance"><h4>任务与办理记录</h4><ul><li v-for="task in detail.instance.tasks" :key="task.taskId">节点 {{ task.nodeId }} · 指定处理人 {{ task.principalId }}</li></ul>
        <ul><li v-for="vote in detail.instance.votes" :key="vote.voteId">{{ vote.occurredAtUtc }} · {{ vote.approved ? '同意' : '拒绝' }} · {{ vote.actualUserId }} · {{ vote.comment }}</li></ul>
        <h4>流程历史</h4><ul><li v-for="event in detail.instance.history" :key="event.eventId">{{ event.recordedAt }} · {{ event.action }} · {{ event.comment }}</li></ul>
      </section>
    </article>
  </section>
</template>
<style scoped>
.query { padding: 16px; }form, .row, nav { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin: 16px 0; }label { display: flex; gap: 8px; align-items: center; }input, select, button { padding: 8px; border: 1px solid var(--el-border-color); border-radius: 6px; background: var(--el-bg-color); color: inherit; }input { max-width: 190px; }button { cursor: pointer; }button:disabled { opacity: .5; cursor: default; }table { width: 100%; border-collapse: collapse; }th, td { text-align: left; padding: 12px; border-bottom: 1px solid var(--el-border-color); }article { margin-top: 24px; }p, li { overflow-wrap: anywhere; }[role=alert] { color: var(--el-color-danger); }
</style>
