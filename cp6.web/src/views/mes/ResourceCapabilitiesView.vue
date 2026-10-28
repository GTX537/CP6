<template>
  <main class="capability-page">
    <h1>{{ kind === 'MACHINE' ? '机器能力目录' : '工作中心能力目录' }}</h1>
    <p>登记资源的能力声明及准确单位。目录保存后仍需现场核实和专业准入，不能据此开工。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="recovery"><h2>待确认的原操作</h2><p>提交结果尚未确认。先查询原操作；关闭未提交键后，迟到请求将被拒绝。</p>
      <button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">关闭未提交原操作</button></section>
    <form @submit.prevent="reload(1)"><label>资源搜索<input v-model="search" aria-label="能力资源搜索" maxlength="100" /></label><button :disabled="busy">查找资源</button></form>
    <ul><li v-for="row in rows" :key="row.reference.id"><button :disabled="busy" @click="open(row.reference.id)">{{ row.source.master.code }} · {{ row.source.master.name || '未填写名称' }}</button></li></ul>
    <div><button :disabled="busy || page === 1" @click="reload(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="reload(page + 1)">下一页</button></div>
    <section v-if="editor && body">
      <h2>{{ editor.resource.source.master.name || editor.resource.source.master.code }}</h2>
      <p>资源版本 {{ body.source.version }}；目录版本 {{ editor.expectedVersion === 'absent' ? '尚未登记' : editor.expectedVersion }}。</p>
      <p v-if="editor.current">声明已记录，现场资格待核实。{{ editor.current.missing.map(finding).join('；') }}</p>
      <details><summary>原资源能力描述</summary><pre>{{ editor.resource.source.capability }}</pre><p>原空值与旧默认值按来源保留，其含义需核实。选择工时表达单位不会改写原字段的分钟、秒或小时含义，也不会自动换算数值。</p></details>
      <form class="unit-search" @submit.prevent="findUnits"><label>单位搜索<input v-model="unitSearch" aria-label="能力单位搜索" maxlength="100" /></label><button :disabled="locked">查找单位</button></form>
      <form @submit.prevent="save">
        <fieldset :disabled="locked || !canEdit">
          <legend>目录声明</legend><label>整体依据说明<textarea v-model="body.basis" aria-label="能力整体依据" maxlength="2000" required /></label>
          <label>工时单位<select :value="refKey(body.timeUnit)" aria-label="能力工时单位" @change="chooseTime($event)"><option value="">未知 / 暂不声明</option><option v-if="body.timeUnit && !units.some(u => refKey(u.reference) === refKey(body!.timeUnit))" :value="refKey(body.timeUnit)">已选单位 v{{ body.timeUnit.version }}</option><option v-for="u in units" :key="refKey(u.reference)" :value="refKey(u.reference)">{{ u.profile.code }} · v{{ u.reference.version }}</option></select></label>
          <p v-if="body.timeBasis">已绑定时间含义版本 {{ body.timeBasis.version }}。</p>
          <label v-if="kind === 'MACHINE'">产出单位<select :value="refKey(body.outputUnit)" aria-label="能力产出单位" @change="body.outputUnit = selectedUnit($event)"><option value="">未知 / 暂不声明</option><option v-if="body.outputUnit && !units.some(u => refKey(u.reference) === refKey(body!.outputUnit))" :value="refKey(body.outputUnit)">已选单位 v{{ body.outputUnit.version }}</option><option v-for="u in units" :key="refKey(u.reference)" :value="refKey(u.reference)">{{ u.profile.code }} · v{{ u.reference.version }}</option></select></label>
          <section v-for="(item, index) in body.capabilities" :key="index" class="item">
            <h3>能力 {{ index + 1 }}</h3><label>能力键<input v-model="item.key" :aria-label="`能力键 ${index + 1}`" maxlength="64" required /></label>
            <label>值类型<select v-model="item.valueKind" :aria-label="`能力类型 ${index + 1}`" @change="changeKind(item)"><option value="DECIMAL">数值</option><option value="ENUM">分类</option></select></label>
            <label v-if="item.valueKind === 'DECIMAL'">精确数值（固定 8 位小数）<input v-model="item.decimalValue" :aria-label="`能力数值 ${index + 1}`" inputmode="decimal" required /></label>
            <label v-else>分类值<input v-model="item.enumValue" :aria-label="`能力分类 ${index + 1}`" maxlength="64" required /></label>
            <label v-if="item.valueKind === 'DECIMAL'">单位<select :value="refKey(item.unit)" :aria-label="`能力单位 ${index + 1}`" required @change="chooseItemUnit(item, $event)"><option value="">请选择准确单位</option><option v-if="item.unit && !units.some(u => refKey(u.reference) === refKey(item.unit))" :value="refKey(item.unit)">已选单位 v{{ item.unit.version }}</option><option v-for="u in units" :key="refKey(u.reference)" :value="refKey(u.reference)">{{ u.profile.code }} · {{ u.profile.dimensionKey }} · v{{ u.reference.version }}</option></select></label>
            <label>量纲<input v-model="item.dimension" :aria-label="`能力量纲 ${index + 1}`" maxlength="32" :readonly="item.valueKind === 'DECIMAL'" required /></label>
            <label>有效起点（含时区）<input v-model="item.validFrom" :aria-label="`能力起点 ${index + 1}`" placeholder="2026-10-28T08:00:00+08:00" required /></label>
            <label>有效终点（不含）<input :value="item.validTo || ''" :aria-label="`能力终点 ${index + 1}`" placeholder="空表示未设终点" @input="item.validTo = ($event.target as HTMLInputElement).value || null" /></label>
            <label>本项依据说明<textarea v-model="item.basis" :aria-label="`能力依据 ${index + 1}`" maxlength="2000" required /></label>
            <button type="button" @click="body.capabilities.splice(index, 1)">移除此项</button>
          </section>
          <button v-if="kind === 'MACHINE'" type="button" :disabled="body.capabilities.length >= 100" @click="addItem">增加能力</button>
          <p v-if="kind === 'MACHINE' && !body.capabilities.length">尚未声明具体能力，读取时保持未知。</p>
          <label>本次登记原因<textarea v-model="reason" aria-label="能力登记原因" maxlength="2000" required /></label>
          <button type="submit">保存能力声明</button>
        </fieldset>
      </form>
      <form @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="能力历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取能力历史</button></form>
      <article v-if="history"><h3>历史目录 v{{ history.reference.version }}</h3><p>{{ history.body.basis }}</p><p>{{ history.missing.map(finding).join('；') }}</p><pre>{{ history.originalJson }}</pre></article>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { resourceApi, type ResourceKind, type ResourceScope, type ResourceVersion } from '@/api/mes/resourceSources'
import { capabilityApi, type CapabilityBody, type CapabilityEditor, type CapabilityItem, type CapabilitySnapshot } from '@/api/mes/resourceCapabilities'
import { uomApi, type UomReference, type UomVersion } from '@/api/erp/uomProfiles'
import { timeApi } from '@/api/erp/uomTimeBases'
import { capabilityResponse as result, pendingCapabilityOperation, saveCapabilityOperation, resolveCapabilityOperation, type PendingCapabilityOperation } from '@/auth/resourceCapabilityRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = withDefaults(defineProps<{ kind?: ResourceKind }>(), { kind: 'MACHINE' }), kind = computed(() => props.kind)
const permissions = usePermissionStore(), canEdit = computed(() => permissions.has((kind.value === 'MACHINE' ? 'mes-machine' : 'mes-work-center') + ':capability-edit'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<ResourceScope | null>(null), pending = ref<PendingCapabilityOperation | null>(null), locked = computed(() => busy.value || !!pending.value)
const search = ref(''), page = ref(1), rows = ref<ResourceVersion[]>([]), editor = ref<CapabilityEditor | null>(null), body = ref<CapabilityBody | null>(null), reason = ref('')
const unitSearch = ref(''), units = ref<UomVersion[]>([]), history = ref<CapabilitySnapshot | null>(null), historyVersion = ref('1')
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const refKey = (value: UomReference | null) => value ? `${value.id}:${value.version}:${value.digest}` : ''
function finding(value: string) { const labels: Record<string, string> = { RESOURCE_QUALIFICATION_REQUIRED: '现场及生产资格待核实', RESOURCE_TIME_UNIT_REQUIRED: '工时单位未声明', RESOURCE_OUTPUT_UNIT_REQUIRED: '产出单位未声明', RESOURCE_CAPABILITIES_UNKNOWN: '具体能力未知', RESOURCE_CAPABILITY_CHANGED: '目录已有后继版本', RESOURCE_SOURCE_CHANGED_OR_DISABLED: '原资源已变化或停用', UNIT_SOURCE_CHANGED_OR_DISABLED: '单位已变化或停用', TIME_BASIS_CHANGED_OR_DISABLED: '时间含义已变化或停用' }; return labels[value] || '来源待核实' }
let live = true, generation = 0
function clearEditor() { editor.value = null; body.value = null; reason.value = ''; units.value = []; unitSearch.value = ''; history.value = null }
function clearPrivate() { clearEditor(); rows.value = []; scope.value = null; pending.value = null; search.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (failure: any) { try { check(); const status = failure?.response?.status; if ([401, 403].includes(status)) clearPrivate(); error.value = status === 412 ? '资源、单位或目录版本已变化，请查询原操作后重新打开。' : [401, 403].includes(status) ? '当前无权读取资源或单位。' : /^[A-Z][A-Z0-9_]+$/.test(failure?.response?.data?.message || '') ? '请核对输入、来源与原操作状态。' : failure?.message || '暂无法确认结果。' } catch { /* Obsolete identity response. */ } }
  finally { try { check(); pending.value = scope.value ? pendingCapabilityOperation(scope.value, kind.value) : null; busy.value = false } catch { if (live && generation === epoch) busy.value = false } }
}
async function reload(next = 1) { await run(async check => { clearEditor(); rows.value = []; if (!permissions.loaded) await permissions.loadMyActions(); check(); const identity = result(await capabilityApi(kind.value).scope()); check(); scope.value = identity; const value = result(await resourceApi(kind.value).list(search.value, next)); check(); rows.value = value; page.value = next }) }
function setEditor(value: CapabilityEditor) { clearEditor(); editor.value = value; body.value = value.current ? copy(value.current.body) : { source: value.resource.reference, timeUnit: null, timeBasis: null, outputUnit: null, capabilities: [], basis: '' }; body.value.source = copy(value.resource.reference) }
async function open(id: string) { await run(async check => { clearEditor(); const value = result(await capabilityApi(kind.value).editor(id)); check(); setEditor(value) }) }
function addItem() { body.value?.capabilities.push({ key: '', valueKind: 'DECIMAL', decimalValue: '', enumValue: null, unit: null, dimension: '', validFrom: '', validTo: null, basis: '' }) }
function changeKind(item: CapabilityItem) { item.decimalValue = item.valueKind === 'DECIMAL' ? '' : null; item.enumValue = item.valueKind === 'ENUM' ? '' : null; item.unit = null; item.dimension = '' }
function selectedUnit(event: Event) { const value = (event.target as HTMLSelectElement).value; if (!value) return null; const unit = units.value.find(u => refKey(u.reference) === value); if (!unit) throw new Error('请重新选择准确单位。'); return copy(unit.reference) }
function chooseItemUnit(item: CapabilityItem, event: Event) { item.unit = selectedUnit(event); item.dimension = units.value.find(u => refKey(u.reference) === refKey(item.unit))?.profile.dimensionKey || '' }
async function findUnits() { await run(async check => { const value = result(await uomApi.list(unitSearch.value, 1, 100)); check(); units.value = value.filter(u => u.isCurrent && u.profile.enabled === true) }) }
async function chooseTime(event: Event) { const selected = selectedUnit(event); if (!selected) { if (body.value) { body.value.timeUnit = null; body.value.timeBasis = null }; return }; await run(async check => { const unit = units.value.find(u => refKey(u.reference) === refKey(selected))!; const base = unit.profile.mode === 'BASE' ? unit.reference : unit.profile.baseRef!; const observed = result(await timeApi.editor(base.id)); check(); if (!observed.definition?.currentApplicable || refKey(observed.currentBase.reference) !== refKey(base)) throw new Error('该单位尚无当前可用的时间含义，请先在时间单位目录登记。'); if (body.value) { body.value.timeUnit = selected; body.value.timeBasis = observed.definition.reference } }) }
async function save() { await run(async check => { if (!scope.value || !body.value || !editor.value || !canEdit.value) throw new Error('请先选择有权资源。'); const frozen = copy(body.value), expectedVersion = editor.value.expectedVersion, why = reason.value; await saveCapabilityOperation(scope.value, kind.value, frozen.source.id, check, operationId => capabilityApi(kind.value).save({ operationId, expectedVersion, body: frozen, reason: why })); check(); clearEditor(); const value = result(await capabilityApi(kind.value).editor(frozen.source.id)); check(); setEditor(value); notice.value = '能力声明已保存，现场资格仍待核实。' }) }
async function recover(close: boolean) { await run(async check => { if (!scope.value) throw new Error('请先载入身份。'); const value = await resolveCapabilityOperation(scope.value, kind.value, close, check); check(); if (value.state === 'COMMITTED' && value.result) { clearEditor(); const current = result(await capabilityApi(kind.value).editor(value.result.objectId)); check(); setEditor(current); notice.value = '已找回原提交结果。' } else notice.value = value.state === 'CLOSED' ? '未提交键已关闭，可以明确开始新操作。' : '结果仍未知，请继续按原键查询。' }) }
async function readHistory() { await run(async check => { if (!editor.value) return; const value = result(await capabilityApi(kind.value).read(editor.value.resource.reference.id, historyVersion.value)); check(); history.value = value }) }
function reset() { generation++; busy.value = false; clearPrivate(); if (authenticationCanRead()) void reload() }
watch(kind, reset)
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clearPrivate(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.capability-page{max-width:1100px;margin:auto;padding:24px}.capability-page section,.capability-page fieldset{margin:18px 0;padding:18px;border:1px solid #ccd6df;border-radius:8px}.capability-page label{display:flex;flex-direction:column;gap:5px;margin:12px 0}.capability-page input,.capability-page select,.capability-page textarea{padding:8px;max-width:700px}.capability-page button{margin:5px;padding:7px 13px}.capability-page pre{white-space:pre-wrap;overflow-wrap:anywhere}.recovery{background:#fff7dc}.item{background:#f6f8fa}[role=alert]{color:#a32626}
</style>
