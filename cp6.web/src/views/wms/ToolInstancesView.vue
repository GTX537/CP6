<template>
  <main class="tools" :aria-busy="busy">
    <header><div><h1>实物版模</h1><p>查看原始记录、当前周期计数和资料变更历史。</p></div><button :disabled="busy" @click="reload">重新载入</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="panel"><h2>待确认操作</h2><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section class="panel">
      <form aria-label="实物查询" @submit.prevent="loadPage(1)"><label>实物编号 <input v-model="search" maxlength="100" aria-label="实物编号" /></label><button :disabled="busy">查询</button></form>
      <div class="table-scroll"><table><thead><tr><th>实物编号 / 类型</th><th>本周期次数 / 上限</th><th>旧状态</th><th>所在仓库 / 库位</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.reference.id"><td>{{ row.source.plateNo }} / {{ row.source.plateType }}</td><td>{{ row.source.usedShots }} / {{ row.source.maxShots ?? '未登记' }}</td><td>{{ status(row.source.status) }}</td><td>{{ row.source.warehouseCd ?? '未映射' }} / {{ row.source.locationCd ?? '未映射' }}</td><td><button :disabled="busy" @click="open(row.reference.id)">查看资料</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前权限范围内没有记录。</p>
      <nav aria-label="实物分页"><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页 · 共 {{ total }} 条</span><button :disabled="busy || page * 50 >= total" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <ToolMaintenancePanel :scope="scope" :selected="selected" @authority-denied="registrationDenied" />
    <ToolActualUsagePanel :scope="scope" :selected="selected" @authority-denied="registrationDenied" />
    <ToolAssociationPanel :scope="scope" :selected="selected" @authority-denied="registrationDenied" />
    <ToolRegistrationPanel :scope="scope" :selected="selected" @authority-denied="registrationDenied" />
    <section v-if="selected" class="panel">
      <h2>{{ selected.source.plateNo }} · 资料</h2><p>来源版本 {{ selected.reference.version }} · 实物 ID：{{ selected.reference.id }}</p>
      <p>原记录本周期已用 {{ selected.source.usedShots }} 次；正式周期编号、终身次数和站点尚未核实的记录，可在“站点与计数来源”中核对。当前使用资格：未知。</p>
      <p>工程关联请在上方按准确技术版本核对。使用、维护与报废入口仍待对应业务规则和来源接通。</p>
      <dl><dt>客户</dt><dd>{{ visible('CustomerCd', selected.source.customerCd) }} / {{ visible('CustomerName', selected.source.customerName) }}</dd><dt>产品</dt><dd>{{ visible('ProductCd', selected.source.productCd) }} / {{ visible('ProductName', selected.source.productName) }}</dd><dt>登记成本</dt><dd>{{ visible('MadeCost', selected.source.madeCost) }}</dd></dl>
      <form aria-label="保存实物资料" @submit.prevent="save"><fieldset :disabled="busy || !!pending || completed || !canWrite">
        <div class="fields"><label v-for="field in fields" :key="field.key">{{ field.label }}
          <input v-model="draft[field.key]" :aria-label="field.label" :type="field.date ? 'date' : 'text'" :maxlength="field.max" :disabled="cleared[field.key] || !editable(field.key)" />
          <span><input v-model="cleared[field.key]" type="checkbox" :aria-label="'清空' + field.label" :disabled="!editable(field.key)" />清空</span>
        </label></div>
        <button v-if="canWrite" :disabled="!patches.length">保存资料</button>
      </fieldset></form><p>资料更新不会改变使用次数、所在位置、维护状态或工程定义。</p>
      <form aria-label="读取实物历史" @submit.prevent="readHistory"><label>历史版本 <input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h3>历史版本 {{ history.reference.version }}</h3><p>原周期次数 {{ history.source.usedShots }}；登记成本 {{ history.readableFields.includes('MadeCost') ? history.source.madeCost ?? '未填写' : '无权查看' }}。</p><pre>{{ history.source }}</pre></article>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import ToolAssociationPanel from './ToolAssociationPanel.vue'
import ToolMaintenancePanel from './ToolMaintenancePanel.vue'
import ToolActualUsagePanel from './ToolActualUsagePanel.vue'
import ToolRegistrationPanel from './ToolRegistrationPanel.vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import { toolInstanceApi as api, type ToolInstanceView } from '@/api/wms/toolInstances'
import { pendingToolInstance, resolveToolInstance, saveToolInstance, instanceResponse as result, type PendingToolInstance } from '@/auth/toolInstanceRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permission = usePermissionStore(), busy = ref(false), error = ref(''), notice = ref('')
const scope = ref<EngineeringScope | null>(null), pending = ref<PendingToolInstance | null>(null), rows = ref<ToolInstanceView[]>([])
const selected = ref<ToolInstanceView | null>(null), history = ref<ToolInstanceView | null>(null), completed = ref(false)
const search = ref(''), page = ref(1), total = ref(0), historyVersion = ref('1'), draft = ref<Record<string, string>>({}), cleared = ref<Record<string, boolean>>({})
const fields = [{ key: 'colorCount', label: '色数', max: 2 }, { key: 'sizeNote', label: '尺寸说明', max: 40 }, { key: 'madeDate', label: '制造日期', max: 10, date: true }, { key: 'madeCost', label: '登记成本', max: 19 }, { key: 'maxShots', label: '最大次数', max: 10 }, { key: 'remarks', label: '备注', max: 500 }]
const canWrite = computed(() => !!selected.value && selected.value.source.status !== 3 && permission.has('wms-plate-mold:edit'))
function editable(key: string) { const field = ({ madeCost: 'MadeCost', remarks: 'Remarks' } as Record<string, string>)[key]; return !field || !!selected.value?.readableFields.includes(field) && !!selected.value?.writableFields.includes(field) }
function initial(key: string): string | null { if (!selected.value) return null; const value = selected.value.source[key as keyof ToolInstanceView['source']]; return value == null ? null : key === 'madeDate' ? String(value).slice(0, 10) : String(value) }
const patches = computed(() => fields.filter(f => editable(f.key)).map(f => ({ field: f.key, value: cleared.value[f.key] ? null : draft.value[f.key] ?? '' })).filter(p => p.value !== initial(p.field)))
function status(value: number) { return ({ 0: '原记录可用', 1: '维护中', 2: '达到寿命', 3: '已报废' } as Record<number, string>)[value] ?? `旧状态 ${value}` }
function visible(field: string, value: string | null) { return selected.value?.readableFields.includes(field) ? value ?? '未填写' : '无权查看' }
let live = true, generation = 0
function clear() { scope.value = null; pending.value = null; rows.value = []; selected.value = null; history.value = null; draft.value = {}; cleared.value = {}; completed.value = false; notice.value = ''; search.value = ''; page.value = 1; total.value = 0 }
function registrationDenied() { clear(); error.value = '当前权限已变化，请重新核对。' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation, check = () => { requireIdentity(stamp); if (!live || generation !== current) throw new Error('页面身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) { clear(); error.value = '当前权限已变化，请重新核对。' }
    else error.value = failure?.response?.status === 412 ? '原记录版本已变化。输入和预期版本已保留；请查询或结束原操作，再明确重新载入。' : failure?.response?.status === 404 ? '该历史版本不存在；不会替换为当前版本。' : failure?.message || '操作尚未确认，请查询原操作。'
  } catch { /* Ignore a previous identity response. */ } }
  finally { try { check(); if (scope.value) pending.value = pendingToolInstance(scope.value); busy.value = false } catch { if (live && generation === current) busy.value = false } }
}
function accept(value: ToolInstanceView, id: string) { if (value.tenantId !== scope.value?.tenantId || value.reference.id !== id || value.reference.owner !== 'Main.Wms.ToolInstance.v1') throw new Error('来源与当前对象或身份不一致。'); return value }
async function reload() { await run(async check => { rows.value = []; selected.value = null; history.value = null; draft.value = {}; cleared.value = {}; completed.value = false
  const identity = result(await api.scope()); check(); scope.value = identity; const list = result(await api.list(search.value, page.value)); check(); rows.value = list.items.map(v => accept(v, v.reference.id)); total.value = list.total
}) }
async function loadPage(target: number) { await run(async check => { rows.value = []; const list = result(await api.list(search.value, target)); check(); rows.value = list.items.map(v => accept(v, v.reference.id)); total.value = list.total; page.value = target }) }
async function open(id: string) { await run(async check => { const value = result(await api.read(id)); check(); selected.value = accept(value, id); history.value = null; historyVersion.value = '1'; completed.value = false; notice.value = ''; draft.value = {}; cleared.value = {}
  for (const field of fields) { const value = initial(field.key); draft.value[field.key] = value ?? ''; cleared.value[field.key] = value == null }
}) }
async function save() {
  if (!scope.value || !selected.value || !canWrite.value || pending.value || completed.value || !patches.value.length) return
  const identity = scope.value, view = selected.value, expected = { ...view.reference }, edits = patches.value.map(p => ({ ...p }))
  await run(async check => { await saveToolInstance(identity, expected.id, check, operationId => api.save(expected.id, view.etag, { operationId, expected, patches: edits })); check(); completed.value = true; notice.value = '实物资料已保存，请重新载入当前版本。' })
}
async function recover(close: boolean) { const identity = scope.value; if (!identity) return; await run(async check => { const operation = await resolveToolInstance(identity, close, check); check(); completed.value = operation.state === 'COMMITTED'; notice.value = operation.state === 'UNKNOWN' ? '结果仍未知，请继续查询或结束未提交操作。' : operation.state === 'CLOSED' ? '未提交操作已结束；输入仍保留。' : '原操作已提交，请重新载入当前资料。' }) }
async function readHistory() { const id = selected.value?.reference.id; if (!id) return; await run(async check => { history.value = null; const value = result(await api.read(id, historyVersion.value)); check(); history.value = accept(value, id) }) }
function identityChanged() { generation++; clear(); busy.value = false; error.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.tools{max-width:1200px;margin:auto;padding:24px;color:var(--text,#18343d)}header,nav{display:flex;justify-content:space-between;gap:14px;align-items:center}.panel{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px}button,input{font:inherit;padding:8px 12px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer;margin:6px}button:disabled{opacity:.5;cursor:default}button:focus-visible,input:focus{outline:2px solid #14969e;outline-offset:2px}label{display:block;margin:12px 0}.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px}.fields span{display:block}fieldset{border:0;padding:0}.table-scroll{overflow:auto}table{width:100%;min-width:680px;border-collapse:collapse}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce5e9;overflow-wrap:anywhere}p,pre{overflow-wrap:anywhere}pre{white-space:pre-wrap;background:#f3f6f7;padding:14px;max-height:320px;overflow:auto}[role=alert]{color:#a02828}@media(max-width:600px){.tools{padding:10px}.panel{padding:14px}header{align-items:flex-start;flex-direction:column}}
</style>
