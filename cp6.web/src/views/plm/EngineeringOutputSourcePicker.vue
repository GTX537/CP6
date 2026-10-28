<template>
  <div class="source-picker">
    <p v-if="modelValue">已选准确版本 {{ modelValue.version }} · {{ modelValue.id }}</p>
    <label>{{ label }}搜索<input v-model="search" :aria-label="`${label}搜索`" :disabled="disabled || busy" /></label>
    <button type="button" :disabled="disabled || busy" @click="find(1)">查找{{ label }}</button><button v-if="more" type="button" :disabled="disabled || busy" @click="find(page + 1)">更多{{ label }}</button>
    <label>{{ label }}<select :value="''" :aria-label="label" :disabled="disabled || busy" @change="choose(($event.target as HTMLSelectElement).value)"><option value="">{{ modelValue ? '保留已选准确版本' : '请选择' }}</option><option v-for="(row, i) in rows" :key="key(row.reference)" :value="String(i)">{{ row.label }} · v{{ row.reference.version }}</option></select></label>
    <button v-if="optional && modelValue" type="button" :disabled="disabled || busy" @click="emit('update:modelValue', null)">清除{{ label }}</button><p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>
<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { manufacturingApi } from '@/api/plm/engineeringManufacturing'
import { qualityApi } from '@/api/mes/qualitySpecification'
import { outputRelationsApi } from '@/api/plm/engineeringOutputRelations'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ kind: 'manufacturing' | 'quality' | 'relations'; label: string; modelValue: EngineeringReference | null; disabled?: boolean; optional?: boolean; productId?: string; revisionId?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: EngineeringReference | null] }>()
const permissions = usePermissionStore(), search = ref(''), busy = ref(false), page = ref(1), more = ref(false), error = ref('')
const rows = ref<{ reference: EngineeringReference; label: string }[]>([])
const key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const allowed = () => permissions.has(({ manufacturing: 'plm-engineering-manufacturing', quality: 'quality-specification', relations: 'plm-engineering-output-relations' }[props.kind]) + ':query')
let epoch = 0, live = true, stamp: ReturnType<typeof identityStamp> | null = null
function clear() { epoch++; rows.value = []; more.value = false; busy.value = false; error.value = ''; stamp = null; search.value = '' }
async function find(target: number) {
  if (busy.value || props.disabled) return
  const identity = identityStamp(), generation = epoch
  const check = () => { requireIdentity(identity); if (!live || generation !== epoch || !authenticationCanRead() || !allowed()) throw new Error('当前无权读取此来源。') }
  busy.value = true; error.value = ''
  try {
    check(); if (target === 1) rows.value = []
    let values: typeof rows.value, count: number
    if (props.kind === 'manufacturing') { const r = await manufacturingApi.list(props.revisionId, search.value, target); check(); if (r.code !== 0) throw new Error(); count = r.data.length; values = r.data.filter(x => x.isCurrent && (!props.productId || x.body.productRef.id === props.productId)).map(x => ({ reference: x.reference, label: x.body.name })) }
    else if (props.kind === 'quality') { const r = await qualityApi.list(search.value, target); check(); if (r.code !== 0) throw new Error(); count = r.data.length; values = r.data.filter(x => x.currentApproval?.body.specification && key(x.currentApproval.body.specification) === key(x.version.reference) && !x.missing.length && (!props.productId || x.version.body.product.id === props.productId)).map(x => ({ reference: x.version.reference, label: x.version.body.name })) }
    else { const r = await outputRelationsApi.list(search.value, target); check(); if (r.code !== 0) throw new Error(); count = r.data.length; values = r.data.filter(x => !x.missing.length && x.control.state === 'QUALIFIED' && (!props.revisionId || x.version.original.revisionOrigin.id === props.revisionId)).map(x => ({ reference: x.version.reference, label: x.version.original.body.name })) }
    check(); rows.value.push(...values); more.value = count === 20; page.value = target; stamp = identity
    if (!rows.value.length) error.value = '没有符合条件的当前来源，请核对权限、名称与批准状态。'
  } catch { if (live && generation === epoch) { rows.value = []; stamp = null; error.value = '来源读取未完成，请重新查询。' } }
  finally { if (live && generation === epoch) busy.value = false }
}
function choose(value: string) { if (props.disabled || busy.value || !stamp || value === '') return; try { requireIdentity(stamp); if (!authenticationCanRead() || !allowed()) throw new Error(); const row = rows.value[Number(value)]; if (row) emit('update:modelValue', JSON.parse(JSON.stringify(row.reference))) } catch { clear() } }
watch(() => [props.kind, props.productId, props.revisionId], clear); watch(allowed, yes => { if (!yes) clear() }); window.addEventListener(AUTH_EVENT, clear)
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, clear) })
</script>
<style scoped>.source-picker{padding:10px;border:1px solid var(--el-border-color);border-radius:4px}label{display:grid;gap:4px;margin:6px 0}input,select,button{font:inherit;max-width:100%;padding:6px}button{margin-right:6px}p{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}</style>
