<template>
  <main class="resource-page" :aria-busy="busy">
    <header><div><h1>{{ kind === 'MACHINE' ? '设备来源目录' : '工作中心来源目录' }}</h1><p>核对原资源资料与历史版本，更新名称等普通资料。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="panel"><h2>待确认操作</h2><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section class="panel">
      <form @submit.prevent="loadPage(1)"><label>编码或名称 <input v-model="search" aria-label="资源搜索" maxlength="100" /></label><button :disabled="busy">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>编码 / 名称</th><th>来源版本</th><th>原状态</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.reference.id"><td>{{ row.source.master.code }} / {{ row.source.master.name ?? '未填写' }}</td><td>{{ row.reference.version }}</td><td>{{ row.sourceEnabled ? '原记录启用' : '原记录停用或删除' }}</td><td><button :disabled="busy" @click="open(row)">查看资料</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前范围内没有资源。</p>
      <nav aria-label="资源分页"><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <section v-if="selected" class="panel">
      <h2>{{ selected.source.master.code }} · 资料</h2><p class="identity">资源 ID：{{ selected.reference.id }}；来源版本 {{ selected.reference.version }}。</p>
      <p>资料 {{ selected.axes.master }} · 能力描述 {{ selected.axes.capability }} · 旧归属 {{ selected.axes.assignment }} · 控制状态 {{ selected.axes.control }}</p>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending || !canEdit || selected.source.control.isDeleted"><div class="fields">
        <label v-for="field in fields" :key="field.key">{{ field.label }}<input v-model="draft[field.key]" :aria-label="field.label" :type="field.date ? 'date' : 'text'" :maxlength="field.max" :required="field.key === 'name' && kind === 'MACHINE'" :disabled="cleared[field.key]" />
          <span v-if="field.key !== 'name' || kind === 'WORK_CENTER'" class="clear"><input v-model="cleared[field.key]" type="checkbox" :aria-label="'清空' + field.label" />清空</span></label>
      </div><button v-if="canEdit">保存普通资料</button></fieldset></form>
      <h3>原能力描述</h3><dl><template v-for="(value, key) in selected.source.capability" :key="key"><dt>{{ labels[key] || key }}</dt><dd>{{ value ?? '未填写' }}</dd></template></dl>
      <h3>原归属代码</h3><dl><template v-for="(value, key) in selected.source.assignment" :key="key"><dt>{{ labels[key] || key }}</dt><dd>{{ value ?? '未填写' }}</dd></template></dl>
      <p>以上数值与旧归属来自原资料。能力资格、正式站点归属和生产准入尚需分别确认。</p>
      <section v-if="permissions.has('erp-uom-profile:query')" aria-label="资源工时换算">
        <h3>工时单位换算</h3><p>读取本资源的原数值，按已登记的单位时间含义精确换算。</p>
        <form @submit.prevent="findTimeUnits"><label>目标单位搜索 <input v-model="unitSearch" aria-label="工时单位搜索" maxlength="100" /></label><button :disabled="busy">查询工时单位</button></form>
        <form @submit.prevent="convertTime"><label>原工时字段 <select v-model="timeField" aria-label="原工时字段" :disabled="busy" @change="timeResult = null"><option value="">请选择</option><template v-if="kind === 'MACHINE'"><option value="standardCycleSeconds">原标准周期秒</option><option value="plannedRunMinutesPerDay">原计划运行分钟 / 日</option></template><option v-else value="dailyCapacityHours">原每日产能小时</option></select></label>
          <label>目标单位 <select v-model="targetUnit" aria-label="工时目标单位" :disabled="busy" @change="timeResult = null"><option value="">请选择查询结果</option><option v-for="unit in timeUnits" :key="unit.reference.id" :value="unit.reference.id">{{ unit.profile.code }} / {{ unit.profile.name }} · 版本 {{ unit.reference.version }}</option></select></label><button :disabled="busy || !timeField || !targetUnit">换算原工时</button></form>
        <article v-if="timeResult" aria-label="工时换算结果"><p>原值：{{ timeResult.nativeQuantity ?? '未填写' }} {{ timeLabels[timeResult.nativeBasis] }}；{{ timeResult.state === 'KNOWN' ? '换算值：' + timeResult.convertedQuantity : '暂不可换算：' + timeResult.reasons.map(r => timeReasons[r] || r).join('、') }}。</p>
          <p>资源版本 {{ timeResult.sourceRef.version }}；目标单位版本 {{ timeResult.targetRef.version }}；时间含义版本 {{ timeResult.timeBasisRef?.version ?? '未采用' }}。</p><p>结果仅反映本次查询，资源产能资格仍需另行确认。</p></article>
      </section>
      <form @submit.prevent="loadHistory"><label>历史版本 <input v-model="historyVersion" aria-label="资源历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h3>历史版本 {{ history.reference.version }}</h3><p>{{ history.source.master.name ?? '未填写名称' }} · {{ history.isCurrent ? '当前版本' : '历史记录' }}</p><pre>{{ history.originalJson }}</pre></article>
      <details><summary>当前来源原件</summary><p class="identity">摘要：{{ selected.reference.digest }}</p><pre>{{ selected.originalJson }}</pre></details>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { resourceApi, type ResourceKind, type ResourceScope, type ResourceVersion } from '@/api/mes/resourceSources'
import { pendingResourceOperation, resolveResourceOperation, saveResourceOperation, resourceResponse, type PendingResourceOperation } from '@/auth/resourceOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import { uomApi, type UomVersion } from '@/api/erp/uomProfiles'
import { timeApi, type ResourceTimeResult } from '@/api/erp/uomTimeBases'
const props = withDefaults(defineProps<{ kind?: ResourceKind }>(), { kind: 'MACHINE' })
const kind = computed(() => props.kind), permissions = usePermissionStore()
const canEdit = computed(() => permissions.has((kind.value === 'MACHINE' ? 'mes-machine' : 'mes-work-center') + ':edit'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<ResourceScope | null>(null), pending = ref<PendingResourceOperation | null>(null)
const search = ref(''), page = ref(1), rows = ref<ResourceVersion[]>([]), selected = ref<ResourceVersion | null>(null), history = ref<ResourceVersion | null>(null), historyVersion = ref('1')
const draft = ref<Record<string, string>>({}), cleared = ref<Record<string, boolean>>({})
const unitSearch = ref(''), targetUnit = ref(''), timeField = ref(''), timeUnits = ref<UomVersion[]>([]), timeResult = ref<ResourceTimeResult | null>(null)
const timeLabels = { SECOND: '秒', MINUTE: '分钟', HOUR: '小时' }
const timeReasons: Record<string, string> = { TIME_BASIS_REQUIRED: '尚未登记时间含义', TIME_BASIS_SOURCE_CHANGED: '单位版本已变化，需重新登记时间含义', TIME_BASIS_DISABLED: '时间含义已停用', RESOURCE_SOURCE_DISABLED: '原资源已停用或删除', RESOURCE_TIME_VALUE_MISSING: '原工时未填写' }
function clearTime() { unitSearch.value = ''; targetUnit.value = ''; timeField.value = ''; timeUnits.value = []; timeResult.value = null }
const fields = computed(() => [{ key: 'name', label: '资源名称', max: 100, date: false }, ...(kind.value === 'MACHINE' ? [
  { key: 'manufacturer', label: '制造商', max: 50, date: false }, { key: 'model', label: '型号', max: 50, date: false },
  { key: 'installDate', label: '安装日期', max: 10, date: true }, { key: 'lastMaintenanceDate', label: '上次维护日期', max: 10, date: true },
  { key: 'nextMaintenanceDate', label: '下次维护日期', max: 10, date: true }, { key: 'remarks', label: '备注', max: 500, date: false }] : [])])
const labels: Record<string, string> = { machineType: '设备类型', processCode: '原工程代码', plannedRunMinutesPerDay: '原计划运行分钟 / 日', standardCycleSeconds: '原标准周期秒', capacityPerHour: '原每小时产能', dailyCapacityHours: '原每日产能小时', legacyWorkCenterCode: '旧工作中心代码', legacyBaseCode: '旧基地代码' }
let live = true, generation = 0
function clearPrivate() { clearTime(); search.value = ''; rows.value = []; selected.value = null; history.value = null; draft.value = {}; cleared.value = {}; scope.value = null; pending.value = null }
watch(selected, clearTime)
function guard() { const stamp = identityStamp(), current = generation; return () => { requireIdentity(stamp); if (!live || current !== generation) throw new Error('当前页面已变化。') } }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const check = guard(); busy.value = true; error.value = ''; notice.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) clearPrivate();
    const code = failure?.response?.data?.message || failure?.message || ''
    error.value = code === 'RESOURCE_SOURCE_CHANGED' ? '资料已更新，请刷新并重新打开后修改。' : code === 'RESOURCE_SOURCE_UNAVAILABLE' ? '原来源暂不可用，请稍后核对。' : /^[A-Z][A-Z0-9_]+$/.test(code) ? '当前无法完成，请确认权限、输入或原操作状态。' : code || '暂时无法完成。'
  } catch { /* obsolete response */ } }
  finally { try { check(); try { pending.value = scope.value ? pendingResourceOperation(scope.value, kind.value) : null } catch (failure: any) { error.value = failure.message }; busy.value = false } catch { /* identity handler owns the new page */ } }
}
async function reload() { await run(async check => {
  selected.value = null; history.value = null; rows.value = []
  if (!permissions.loaded) await permissions.loadMyActions(); check()
  const current = resourceResponse(await resourceApi(kind.value).scope()); check(); scope.value = current
  const result = resourceResponse(await resourceApi(kind.value).list(search.value, page.value)); check(); rows.value = result
}) }
async function loadPage(target: number) { await run(async check => { const result = resourceResponse(await resourceApi(kind.value).list(search.value, target)); check(); rows.value = result; page.value = target; selected.value = null; history.value = null }) }
function editable(value: string | null | undefined, date: boolean) { return value == null ? '' : date ? value.slice(0, 10) : value }
async function open(row: ResourceVersion) { await run(async check => {
  selected.value = null; history.value = null; const result = resourceResponse(await resourceApi(kind.value).read(row.reference.id)); check(); selected.value = result
  draft.value = {}; cleared.value = {}; for (const field of fields.value) { const value = result.source.master[field.key]; draft.value[field.key] = editable(value, field.date); cleared.value[field.key] = value == null }
}) }
async function save() {
  if (!selected.value || !scope.value || !canEdit.value) return
  const current = selected.value, selectedScope = scope.value, channel = kind.value
  const patches = fields.value.flatMap<{ field: string; value: string | null }>(field => { const before = current.source.master[field.key] ?? null
    const value = cleared.value[field.key] ? null : draft.value[field.key] ?? ''
    return value === null ? before === null ? [] : [{ field: field.key, value }] : value === editable(before, field.date) && before !== null ? [] : [{ field: field.key, value }]
  })
  if (!patches.length) { notice.value = '资料没有变化。'; return }
  await run(async check => { await saveResourceOperation(selectedScope, channel, current.reference.id, check,
    operationId => resourceApi(channel).save({ operationId, expected: current.reference, patches })); check(); selected.value = null; history.value = null
    const values = resourceResponse(await resourceApi(channel).list(search.value, page.value)); check(); rows.value = values; notice.value = '普通资料已保存。'
  })
}
async function loadHistory() { const current = selected.value; if (!current) return; await run(async check => {
  history.value = null; const result = resourceResponse(await resourceApi(kind.value).read(current.reference.id, historyVersion.value)); check(); history.value = result
}) }
async function findTimeUnits() { await run(async check => { timeUnits.value = []; targetUnit.value = ''; timeResult.value = null; const units = resourceResponse(await uomApi.list(unitSearch.value)); check(); timeUnits.value = units.filter(u => u.profile.enabled) }) }
async function convertTime() {
  const current = selected.value, unit = timeUnits.value.find(u => u.reference.id === targetUnit.value), field = timeField.value
  if (!current || !unit || !field) return
  await run(async check => { timeResult.value = null; const result = resourceResponse(await timeApi.resource(current.reference, field, unit.reference)); check(); timeResult.value = result })
}
async function recover(close: boolean) { const current = scope.value; if (!current) return; await run(async check => {
  const result = await resolveResourceOperation(current, kind.value, close, check); check()
  if (result.state === 'COMMITTED') { selected.value = null; history.value = null; rows.value = [] }
  notice.value = result.state === 'COMMITTED' ? '原操作已提交，请刷新查看。' : result.state === 'CLOSED' ? '未提交操作已结束。' : '结果仍未确认，请继续查询或结束未提交操作。'
}) }
function identityChanged() { generation++; clearPrivate(); busy.value = false; error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
watch(() => props.kind, identityChanged)
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged); clearPrivate() })
</script>
<style scoped>
.resource-page{max-width:1200px;margin:auto;padding:24px;color:var(--text,#18343d)}header,nav,form{display:flex;gap:14px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{margin-bottom:8px}.panel{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px}button,input,select{font:inherit;padding:8px 12px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}button:focus-visible,input:focus,select:focus{outline:2px solid #14969e;outline-offset:2px}.table-scroll{overflow:auto}table{width:100%;min-width:580px;border-collapse:collapse}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce5e9}nav{margin-top:16px}fieldset{border:0;padding:0;width:100%;min-width:0}.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:16px;margin-bottom:20px}.fields label{display:grid;gap:6px}.clear{font-size:13px;display:flex;align-items:center;gap:6px}dl{display:grid;grid-template-columns:minmax(140px,1fr) 2fr;gap:10px}dd{margin:0;overflow-wrap:anywhere}.identity,pre{overflow-wrap:anywhere;white-space:pre-wrap}pre{background:#f3f6f7;padding:14px;max-height:360px;overflow:auto}[role=alert]{color:#a02828}@media(max-width:600px){.resource-page{padding:10px}.panel{padding:14px}}
</style>
