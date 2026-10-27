<template>
  <section class="registration" :aria-busy="busy">
    <h2>站点与计数来源</h2>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>存在尚未确认的实物登记。</p><button :disabled="busy" @click="recover(false)">查询原登记</button><button :disabled="busy" @click="recover(true)">结束未提交登记</button></div>
    <button v-if="permission.has('wms-plate-mold:add')" :disabled="busy || !scope || !!pending" @click="beginCreate">新建实物</button>
    <button v-if="selected" :disabled="busy" @click="read">查看站点与计数来源</button>
    <button v-if="selected && permission.has('wms-plate-mold:initialize')" :disabled="busy || !!pending || selected.source.status === 3" @click="beginLegacy">登记旧实物站点</button>
    <article v-if="registration">
      <p>站点：{{ registration.currentSite.code }} · {{ registration.currentSite.name }}{{ registration.currentSite.enabled ? '' : '（已停用）' }}</p>
      <p>周期起点：{{ registration.initialCycleShots }}；当前计数：{{ registration.currentCycleShots ?? '待核对' }}；终身次数：{{ registration.lifetimeShots ?? '未知' }}。</p>
      <p>周期编号：{{ registration.cycleNo ?? '历史缺证或待核对' }}；登记起点身份：{{ registration.cycleAnchorId }}。当前使用资格：未知。</p>
      <p v-if="registration.cycleKnowledge === 'RECONCILIATION_REQUIRED'">登记后检测到未核实的次数或维护变化，需核对实际记录。</p>
      <p v-if="!registration.inputContract || registration.inputContract === 'tool.registration.v1'">该登记保存的是旧版规范化文本，无法还原输入时的原始字符序列。</p>
    </article>
    <form v-if="mode" aria-label="实物登记" @submit.prevent="save"><fieldset :disabled="busy || !!pending || completed">
      <h3>{{ mode === 'CREATE' ? '新建未关联实物' : '确认旧实物的站点和观察起点' }}</h3>
      <p v-if="mode === 'CREATE'">仅用于经确认尚未使用的新实物。当前周期和终身次数从零开始；客户、产品、库位保持未关联。</p>
      <p v-else>保留原记录的次数；不补造历史周期编号或终身次数，不改变旧仓库、库位和维护状态。</p>
      <label>搜索站点 <input v-model="search" aria-label="搜索站点" maxlength="100" /></label><button type="button" @click="searchSites">搜索站点</button>
      <label>真实站点 <select v-model="siteId" aria-label="真实站点" required><option value="">请选择</option><option v-for="site in sites" :key="site.reference.id" :value="site.reference.id">{{ site.code }} · {{ site.name }}</option></select></label>
      <template v-if="mode === 'CREATE'">
        <label>所属部门 <select v-model="departmentId" aria-label="所属部门"><option v-if="context?.allowsUnbound" value="">不绑定部门</option><option v-for="d in context?.departments" :key="d.id" :value="d.id">{{ d.code }} · {{ d.name }}</option></select></label>
        <label>实物类型 <select v-model="plateType" aria-label="实物类型"><option v-for="kind in ['PLATE', 'MOLD', 'CYL', 'OTHER']" :key="kind">{{ kind }}</option></select></label>
        <div class="fields"><label v-for="field in fields" :key="field.key">{{ field.label }}
          <input v-model="draft[field.key]" :aria-label="'新建' + field.label" :maxlength="field.max" :type="field.date ? 'date' : 'text'" :disabled="omitted[field.key] || !writable(field.key)" />
          <span><input v-model="omitted[field.key]" type="checkbox" :aria-label="'不登记' + field.label" :disabled="!writable(field.key)" />不登记</span>
        </label></div>
      </template>
      <label>核实依据 <textarea v-model="evidence" aria-label="核实依据" maxlength="1000" required /></label>
      <button :disabled="!siteId || !evidence.trim()">{{ mode === 'CREATE' ? '确认新建实物' : '确认旧实物登记' }}</button>
    </fieldset></form>
    <p>站点和计数登记不代表工程放行或使用资格；使用、维护与报废仍需对应业务入口。</p>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolRegistrationApi as api, type ToolCreationContext, type ToolRegistrationView, type ToolSite, type ToolRegistrationCommand } from '@/api/wms/toolRegistrations'
import { pendingToolRegistration, resolveToolRegistration, saveToolRegistration, type PendingToolRegistration } from '@/auth/toolRegistrationRecovery'
import { instanceResponse as result } from '@/auth/toolInstanceRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>()
const emit = defineEmits<{ 'authority-denied': [] }>(), permission = usePermissionStore()
const busy = ref(false), error = ref(''), notice = ref(''), completed = ref(false), pending = ref<PendingToolRegistration | null>(null)
const registration = ref<ToolRegistrationView | null>(null), mode = ref<'CREATE' | 'BIND_LEGACY' | null>(null), context = ref<ToolCreationContext | null>(null)
const sites = ref<ToolSite[]>([]), siteId = ref(''), search = ref(''), departmentId = ref(''), plateType = ref('PLATE'), evidence = ref(''), objectId = ref('')
const baseline = ref<ToolInstanceView | null>(null), draft = ref<Record<string, string>>({}), omitted = ref<Record<string, boolean>>({})
const fields = [{ key: 'colorCount', label: '色数', max: 2 }, { key: 'sizeNote', label: '尺寸说明', max: 40 }, { key: 'madeDate', label: '制造日期', max: 10, date: true }, { key: 'madeCost', label: '登记成本', max: 19 }, { key: 'maxShots', label: '最大次数', max: 10 }, { key: 'remarks', label: '备注', max: 500 }]
let live = true, generation = 0
function clear() { mode.value = null; context.value = null; registration.value = null; baseline.value = null; sites.value = []; siteId.value = ''; departmentId.value = ''; search.value = ''; plateType.value = 'PLATE'; evidence.value = ''; objectId.value = ''; draft.value = {}; omitted.value = {}; completed.value = false; pending.value = null; notice.value = '' }
function writable(key: string) { const field = ({ madeCost: 'MadeCost', remarks: 'Remarks' } as Record<string, string>)[key]; return !field || !!context.value?.writableFields.includes(field) }
function refreshPending() { pending.value = props.scope ? pendingToolRegistration(props.scope) : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope) return
  const stamp = identityStamp(), current = generation, identity = `${props.scope.tenantId}:${props.scope.actorId}`
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !props.scope || `${props.scope.tenantId}:${props.scope.actorId}` !== identity) throw new Error('页面身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新载入。' }
    else error.value = failure?.response?.status === 412 ? '实物、站点或创建权限版本已变化。输入和原版本已保留，请先查询或结束原登记。' : failure?.message || '结果尚未确认，请查询原登记。'
  } catch { /* Discard late responses for an old identity. */ } }
  finally { try { check(); refreshPending(); busy.value = false } catch { if (live && generation === current) busy.value = false } }
}
function acceptSites(value: ToolSite[]) { if (!Array.isArray(value) || value.some(s => !s.enabled || s.reference.owner !== 'Main.Space.PhysicalSiteIdentity.v1' || !/^[1-9][0-9]*$/.test(s.reference.version) || !/^[0-9a-f]{64}$/.test(s.reference.digest))) throw new Error('站点来源无法确认。'); return value }
async function searchSites() { await run(async check => { const value = result(await api.sites(search.value)); check(); sites.value = acceptSites(value); siteId.value = '' }) }
async function beginCreate() {
  if (!permission.has('wms-plate-mold:add') || pending.value) return
  await run(async check => { const value = result(await api.context()); check(); if (value.tenantId !== props.scope?.tenantId || value.actorId !== props.scope.actorId) throw new Error('创建身份无法确认。')
    const choices = result(await api.sites('')); check(); clear(); context.value = value; sites.value = acceptSites(choices); mode.value = 'CREATE'; objectId.value = crypto.randomUUID(); for (const f of fields) omitted.value[f.key] = true
  })
}
async function beginLegacy() {
  const selected = props.selected; if (!selected || selected.source.status === 3 || !permission.has('wms-plate-mold:initialize') || pending.value) return
  await run(async check => { const choices = result(await api.sites('')); check(); clear(); sites.value = acceptSites(choices); mode.value = 'BIND_LEGACY'; baseline.value = JSON.parse(JSON.stringify(selected)) as ToolInstanceView; objectId.value = selected.reference.id })
}
async function read() {
  const id = props.selected?.reference.id; if (!id) return
  await run(async check => { registration.value = null; const response = await api.read(id); check(); if (response?.code !== 0) throw new Error('登记来源无法确认。'); const value = response.data
    if (value && (value.tenantId !== props.scope?.tenantId || value.reference.owner !== 'Main.Wms.ToolRegistration.v1' || value.reference.id !== id || value.currentNative.id !== id)) throw new Error('登记来源与当前实物不一致。')
    registration.value = value; if (!value) notice.value = '该实物尚未登记站点和周期起点。'
  })
}
function text(key: string) { return omitted.value[key] || !writable(key) ? null : draft.value[key] ?? '' }
function integer(key: string) { const value = text(key); if (value == null) return null; if (!/^[1-9][0-9]*$/.test(value) || Number(value) > 2147483647) throw new Error('次数和色数必须为合法正整数。'); return Number(value) }
async function save() {
  const identity = props.scope, kind = mode.value, site = sites.value.find(s => s.reference.id === siteId.value)
  if (!identity || !kind || !site || pending.value || completed.value || !permission.has(`wms-plate-mold:${kind === 'CREATE' ? 'add' : 'initialize'}`)) return
  await run(async check => {
    const command: Omit<ToolRegistrationCommand, 'operationId'> = { objectId: objectId.value, kind, site: { ...site.reference }, evidence: evidence.value }
    if (kind === 'CREATE') { if (!context.value || !context.value.allowsUnbound && !departmentId.value) throw new Error('请选择当前允许的所属部门。'); command.creationAuthorityVersion = context.value.authorityVersion; command.departmentId = departmentId.value || null
      command.profile = { plateType: plateType.value, colorCount: integer('colorCount'), maxShots: integer('maxShots'), sizeNote: text('sizeNote'), madeDate: text('madeDate'), madeCost: text('madeCost'), remarks: text('remarks') }
    } else { if (!baseline.value) return; command.expected = { ...baseline.value.reference } }
    const etag = baseline.value?.etag
    await saveToolRegistration(identity, command.objectId, kind === 'CREATE' ? 'TOOL_CREATE' : 'TOOL_INITIALIZE', check, operationId => kind === 'CREATE' ? api.create({ ...command, operationId }) : api.bind(command.objectId, etag!, { ...command, operationId })); check(); completed.value = true; notice.value = '实物登记已提交，请明确重新载入当前资料。'
  })
}
async function recover(close: boolean) { const identity = props.scope; if (!identity) return; await run(async check => { const value = await resolveToolRegistration(identity, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '登记结果仍未知。' : value.state === 'CLOSED' ? '未提交登记已结束，输入仍保留。' : '原登记已提交，请重新载入当前资料。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = '' }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id], () => { changed(); try { refreshPending() } catch (e: any) { error.value = e.message } }, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; changed(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.registration{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px}button,input,select,textarea{font:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer;margin:6px}button:disabled{opacity:.5;cursor:default}label{display:block;margin:12px 0}.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:12px}.fields span{display:block}fieldset{border:0;padding:0}textarea{display:block;width:min(100%,540px);min-height:80px}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}button:focus-visible,input:focus,select:focus,textarea:focus{outline:2px solid #14969e;outline-offset:2px}
</style>
