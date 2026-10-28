<template>
  <div class="source-picker">
    <p v-if="modelValue">已选版本 {{ modelValue.version }} · {{ modelValue.id }}</p>
    <label>{{ label }}搜索<input v-model="search" :aria-label="`${label}搜索`" :disabled="disabled || busy" /></label>
    <button type="button" :disabled="disabled || busy" @click="find(1)">查找{{ label }}</button>
    <button v-if="more" type="button" :disabled="disabled || busy" @click="find(page + 1)">更多{{ label }}</button>
    <label>{{ label }}<select :value="''" :aria-label="label" :disabled="disabled || busy" @change="choose(($event.target as HTMLSelectElement).value)"><option value="">{{ modelValue ? '保留已选准确版本' : '请选择' }}</option><option v-for="(row, i) in rows" :key="key(row.reference)" :value="String(i)">{{ row.label }} · v{{ row.reference.version }}</option></select></label>
    <button v-if="optional && modelValue" type="button" :disabled="disabled || busy" @click="emit('update:modelValue', null)">清除{{ label }}</button>
    <p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>
<script setup lang="ts">
import { ref, onBeforeUnmount, watch } from 'vue'
import { engineeringApi, type EngineeringReference } from '@/api/plm/engineeringProducts'
import { documentApi } from '@/api/plm/controlledDocument'
import { uomApi } from '@/api/erp/uomProfiles'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ kind: 'product' | 'site' | 'document' | 'unit'; label: string; modelValue: EngineeringReference | null; disabled?: boolean; optional?: boolean; identity?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: EngineeringReference | null] }>()
const permissions = usePermissionStore(), search = ref(''), busy = ref(false), error = ref(''), page = ref(1), more = ref(false)
const rows = ref<{ reference: EngineeringReference; label: string }[]>([])
let epoch = 0, live = true, loadedStamp: ReturnType<typeof identityStamp> | null = null
const key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const allowed = () => permissions.has(({ product: 'plm-engineering-product', site: 'space-site', document: 'plm-controlled-document', unit: 'erp-uom-profile' }[props.kind]) + ':query')
function clear() { epoch++; rows.value = []; more.value = false; busy.value = false; search.value = ''; error.value = ''; loadedStamp = null }
async function find(target: number) {
  if (busy.value || props.disabled) return
  const stamp = identityStamp(), generation = epoch, term = search.value
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead() || !allowed()) throw new Error('当前无权读取此来源。') }
  busy.value = true; error.value = ''
  try {
    check(); if (target === 1) rows.value = []
    let values: { reference: EngineeringReference; label: string }[], count: number
    if (props.kind === 'product') { const r = await engineeringApi.list(term, target); check(); if (r.code !== 0) throw new Error('来源读取失败'); count = r.data.length; values = r.data.filter(x => x.isCurrent && x.product.enabled).map(x => ({ reference: x.reference, label: `${x.product.code} / ${x.product.name}` })) }
    else if (props.kind === 'site') { const r = await engineeringApi.siteChoices(term); check(); if (r.code !== 0) throw new Error('来源读取失败'); count = 0; values = r.data.filter(x => x.enabled).map(x => ({ reference: x.reference, label: `${x.code} / ${x.name}` })) }
    else if (props.kind === 'unit') { const r = await uomApi.list(term, target); check(); if (r.code !== 0) throw new Error('来源读取失败'); count = r.data.length; values = r.data.filter(x => x.isCurrent && x.profile.enabled).map(x => ({ reference: x.reference, label: `${x.profile.code} / ${x.profile.name}` })) }
    else { const r = await documentApi.list(undefined, term, target); check(); if (r.code !== 0) throw new Error('来源读取失败'); count = r.data.length; values = r.data.filter(x => x.isCurrent && x.state === 'APPROVED' && x.readiness === 'CONTROLLED').map(x => ({ reference: x.reference, label: x.body.name })) }
    check(); rows.value.push(...values.filter(x => !props.identity || x.reference.id === props.identity)); page.value = target; more.value = count === 20; loadedStamp = stamp
    if (!rows.value.length) error.value = '没有符合条件的当前来源，请核对名称、权限与批准状态。'
  } catch (e: any) { if (live && epoch === generation) { rows.value = []; loadedStamp = null; error.value = e?.response?.status === 403 ? '当前无权读取此来源。' : '来源读取未完成，请重新查找。' } }
  finally { if (live && epoch === generation) busy.value = false }
}
function choose(value: string) {
  if (props.disabled || busy.value || value === '' || !loadedStamp) return
  try { requireIdentity(loadedStamp); if (!authenticationCanRead() || !allowed()) throw new Error(); const row = rows.value[Number(value)]; if (row) emit('update:modelValue', JSON.parse(JSON.stringify(row.reference))) } catch { clear() }
}
watch(() => props.kind, clear); watch(allowed, ok => { if (!ok) clear() }); window.addEventListener(AUTH_EVENT, clear)
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, clear) })
</script>
<style scoped>.source-picker{padding:10px;border:1px solid var(--el-border-color);border-radius:4px}label{display:grid;gap:4px;margin:6px 0}input,select,button{font:inherit;max-width:100%;padding:6px}button{margin-right:6px}p{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}</style>
